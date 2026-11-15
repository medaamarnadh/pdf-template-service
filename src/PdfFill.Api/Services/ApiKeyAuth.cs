using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PdfFill.Api.Data;

namespace PdfFill.Api.Services;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string KeyIdClaim = "api_key_id";
    public const string ApplicationClaim = "api_key_application";
    public const string HeaderName = "X-Api-Key";

    /// <summary>Any secret starting with this belongs to the key scheme, never to a portal JWT.</summary>
    public const string KeyMarker = "pdfk_";
}

/// <summary>RFC 9457 problem-details bodies for the public render/validate surface (standards/security.md rule 15).
/// `detail` is always a fixed, safe sentence - never SQL, a file path, or a library message.</summary>
public static class Problems
{
    public static IResult Of(string code, string title, int status, string detail) => Results.Json(
        new { type = $"https://docs.pdftemplate.example/errors/{code}", title, status, detail, code },
        statusCode: status, contentType: "application/problem+json");
}

/// <summary>
/// Resolves an API key secret to a principal carrying the key id and the applications it may be used against.
/// Accepts "Authorization: Bearer pdfk_…" or "X-Api-Key: pdfk_…". A bearer value that is not a key is left to the
/// JWT handler, so portal tokens and keys share the header without ambiguity.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ApiKeyRepository keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Set on the HttpContext when authentication fails, so the challenge response can distinguish
    /// key_revoked from key_invalid - AuthenticateResult.Fail's reason is not otherwise visible to the challenge.</summary>
    private const string FailureCodeItem = "ApiKeyAuth.FailureCode";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var secret = Presented();
        if (secret is null) return AuthenticateResult.NoResult();

        var row = await keys.FindBySecretAsync(secret);
        if (row is null)
        {
            Context.Items[FailureCodeItem] = "key_invalid";
            return AuthenticateResult.Fail("key_invalid");
        }
        if (row.RevokedAt is not null)
        {
            Context.Items[FailureCodeItem] = "key_revoked";
            return AuthenticateResult.Fail("key_revoked");
        }

        var claims = new List<Claim> { new(ApiKeyDefaults.KeyIdClaim, row.ApiKeyId), new(ClaimTypes.Name, row.Name) };
        foreach (var id in await keys.ApplicationIdsAsync(row.ApiKeyId))
            claims.Add(new Claim(ApiKeyDefaults.ApplicationClaim, id));

        await keys.TouchAsync(row.ApiKeyId);
        var identity = new ClaimsIdentity(claims, ApiKeyDefaults.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme));
    }

    /// <summary>The key secret from either accepted header, or null when this request carries no key.</summary>
    private string? Presented()
    {
        if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header) &&
            header.ToString() is { Length: > 0 } direct) return direct;

        var auth = Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
               auth[7..].StartsWith(ApiKeyDefaults.KeyMarker, StringComparison.Ordinal) ? auth[7..] : null;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // A public endpoint accepts two schemes; when auth fails, ASP.NET Core challenges every scheme in the
        // policy in turn. Whichever handler runs first (ApiKey, per PublicApi's scheme order) writes the body;
        // the other must no-op rather than try to set headers on an already-started response.
        if (Response.HasStarted) return Task.CompletedTask;

        var code = Context.Items.TryGetValue(FailureCodeItem, out var stored) && stored is string s
            ? s
            : Presented() is null ? "unauthorized" : "key_invalid";
        var detail = code switch
        {
            "key_revoked" => "This API key has been revoked.",
            "key_invalid" => "The API key was not recognized.",
            _ => "Send a valid API key as 'Authorization: Bearer pdfk_live_…' or 'X-Api-Key: pdfk_live_…'.",
        };
        return Problems.Of(code, "Unauthorized", 401, detail).ExecuteAsync(Context);
    }
}

/// <summary>Who is calling: a portal user, or an API key with a fixed set of applications.</summary>
public sealed record Caller(string? UserId, string? ApiKeyId, IReadOnlySet<string> KeyApplicationIds, bool IsKey)
{
    public static Caller From(ClaimsPrincipal u)
    {
        var keyId = u.FindFirst(ApiKeyDefaults.KeyIdClaim)?.Value;
        if (keyId is not null)
            return new Caller(null, keyId,
                u.FindAll(ApiKeyDefaults.ApplicationClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal), true);

        var userId = u.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? u.FindFirst("sub")?.Value;
        return new Caller(userId, null, new HashSet<string>(), false);
    }
}

public enum AccessResult { Ok, ApplicationNotFound, KeyNotAllocated, NotOwned }

/// <summary>May this caller act on this application? A key must be assigned to it; a user must own it.</summary>
public static class AccessCheck
{
    public static async Task<AccessResult> EvaluateAsync(Caller caller, string applicationId, ApplicationsRepository apps)
    {
        var app = await apps.GetAsync(applicationId);
        if (app is null) return AccessResult.ApplicationNotFound;
        if (caller.IsKey) return caller.KeyApplicationIds.Contains(applicationId) ? AccessResult.Ok : AccessResult.KeyNotAllocated;
        return app.OwnerUserId == caller.UserId ? AccessResult.Ok : AccessResult.NotOwned;
    }

    /// <summary>The HTTP result for a failed check, or null when access is allowed.</summary>
    public static IResult? ToResult(AccessResult r) => r switch
    {
        AccessResult.Ok => null,
        AccessResult.ApplicationNotFound => Problems.Of("application_not_found", "Not Found", 404, "No such application."),
        AccessResult.KeyNotAllocated => Problems.Of("key_not_allocated", "Forbidden", 403, "This key is not assigned to that application."),
        _ => Problems.Of("application_not_owned", "Forbidden", 403, "That application belongs to another user."),
    };
}
