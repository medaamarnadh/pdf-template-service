using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using PdfFill.Api.Data;
using PdfFill.Api.Models;
using PdfFill.Api.Services;

if (args.Length == 2 && args[0] == "hash-password") { Console.WriteLine(PasswordHasher.Hash(args[1])); return; }

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApiDocs();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddProblemDetails();      // amendment 1: framework-generated errors (e.g. malformed JSON) get a problem document too
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddPortalAuth(builder.Configuration);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<PdfFormFiller>();
builder.Services.AddSingleton<PayloadValidator>();
builder.Services.AddScoped<TemplateRepository>();
builder.Services.AddScoped<GrantApplicationRepository>();
builder.Services.AddScoped<ApplicationsRepository>();
builder.Services.AddScoped<ApiKeyRepository>();


var app = builder.Build();
app.Services.GetRequiredService<Database>().EnsureCreated();
app.Services.GetRequiredService<PdfFormFiller>();          // registers the PDFsharp font resolver before any PDF is opened
await SeedTemplatesAsync(app.Services, builder.Configuration, app.Environment);
app.UseCors();

// standards/security.md rule 10: render/validate payloads are capped at 5 MB, checked from Content-Length before
// any JSON parsing happens, and reinforced against a body that lies about its length (chunked transfer etc).
// Placed ahead of authentication and rate limiting so an oversized body is rejected as cheaply as possible.
// Gated on the resolved endpoint's PublicPayloadCapMarker metadata (added to the `pub` group below), not on the
// raw request path: routing has already run by this point (WebApplication inserts it implicitly before any
// app.Use middleware), and matching on the endpoint means a trailing slash (which routing still resolves to the
// same endpoint) can't bypass the cap the way a literal path string comparison could.
const long MaxPublicPayloadBytes = 5 * 1024 * 1024;
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsPost(ctx.Request.Method) && ctx.GetEndpoint()?.Metadata.GetMetadata<PublicPayloadCapMarker>() is not null)
    {
        if (ctx.Request.ContentLength is > MaxPublicPayloadBytes)
        {
            await Problems.Of("payload_too_large", "Payload Too Large", 413,
                "Request body exceeds the 5 MB limit for render and validate.").ExecuteAsync(ctx);
            return;
        }
        var sizeFeature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaxPublicPayloadBytes;
    }
    await next();
});

// standards/security.md rule 13a: every /api endpoint is rate-limited per API key or client IP. The limiter must
// run ahead of authentication - AuthorizationMiddleware short-circuits (never calls next()) on a 401/403, so if
// it ran first, a flood of requests with a bogus or missing key would never reach the limiter at all, and a
// valid-but-over-limit key would complete its lookup and write-lock-taking TouchAsync before being rejected.
// RateLimiting.PartitionKey reads the presented credential straight off the request (X-Api-Key, then an
// Authorization bearer value) and hashes it, so this needs no authentication to have run first.
app.UseRateLimiter();
app.UseRateLimitHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles(); app.UseStaticFiles();     // serves the built portal from wwwroot when present
app.MapApiDocs();                                    // GET /openapi/public.json (+ /docs UI); internal doc only when Docs:ExposeInternal

// Container healthcheck. Deliberately outside the /api/v1 groups so it carries no auth or rate-limit policy and
// is never tagged Public (standards/security.md rule 17a; ApiDocsTests guards that). Runs every 30s from
// deploy/oracle/docker-compose.yml, so the check itself stays a single cheap scalar query rather than anything
// that touches application data.
app.MapGet("/health", async (Database db) =>
{
    try
    {
        using var c = db.Open();
        var version = await c.ExecuteScalarAsync<long>("PRAGMA user_version");
        await c.ExecuteScalarAsync<long>("SELECT 1");
        return Results.Ok(new { status = "healthy", database = "reachable", schema_version = version });
    }
    catch (Exception)
    {
        // standards/security.md rule 15: no raw exception text in the body, just a fixed, safe sentence.
        return Problems.Of("database_unreachable", "Service Unavailable", 503, "The database could not be read.");
    }
});

var api = app.MapGroup("/api/v1").RequireRateLimiting(RateLimiting.ApiPolicy);

// ============================================================================================
// Authentication (portal users). Management endpoints require a bearer JWT from /auth/login.
// Render and validate use the same token until API keys (spec 01) exist.
// ============================================================================================
var auth = api.MapGroup("/auth").RequireRateLimiting(RateLimiting.AuthPolicy);

auth.MapPost("/login", async (LoginRequest req, AuthService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password)) return Results.BadRequest(new { error = "email and password are required." });
    var r = await svc.LoginAsync(req.Email, req.Password);
    return r is null
        ? Results.Json(new { type = "https://docs.pdftemplate.example/errors/invalid_credentials", title = "Invalid credentials", status = 401, code = "invalid_credentials" }, statusCode: 401)
        : Results.Ok(new { token = r.Value.token, expires_at = r.Value.expires, user = new { r.Value.user.Id, r.Value.user.Email, r.Value.user.Role, must_change_password = r.Value.user.MustChangePassword == 1 } });
});

auth.MapGet("/me", (System.Security.Claims.ClaimsPrincipal u) => Results.Ok(new
{
    id = u.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? u.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
    email = u.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value ?? u.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
    role = u.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
    must_change_password = u.FindFirst("pwd_change")?.Value == "1",
})).RequireAuthorization("Portal");

auth.MapPost("/change-password", async (ChangePasswordRequest req, System.Security.Claims.ClaimsPrincipal u, AuthService svc) =>
{
    var id = u.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? u.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    try { return await svc.ChangePasswordAsync(id!, req.CurrentPassword, req.NewPassword) ? Results.NoContent() : Results.Json(new { code = "invalid_credentials", title = "Current password is wrong", status = 401 }, statusCode: 401); }
    catch (ArgumentException ex) { return Results.BadRequest(new { code = "weak_password", error = ex.Message }); }
}).RequireAuthorization("Portal");

// ============================================================================================
// Public API - API key or portal token. The only endpoints customers integrate with.
// ============================================================================================
var pub = api.MapGroup("").RequireAuthorization("PublicApi").WithMetadata(new PublicPayloadCapMarker());

pub.MapPost("/validate", async (RenderRequest body, System.Security.Claims.ClaimsPrincipal u,
    ApplicationsRepository apps, TemplateRepository repo, PayloadValidator validator) =>
{
    var caller = Caller.From(u);
    if (AccessCheck.ToResult(await AccessCheck.EvaluateAsync(caller, body.ApplicationId, apps)) is { } denied) return denied;

    var t = await repo.GetInApplicationAsync(body.TemplateId, body.ApplicationId);
    if (t is null) return Problems.Of("template_not_found", "Not Found", 404, "No such template in that application.");

    var problems = validator.Validate(FieldMapping.LoadJson(t.MappingJson), Payload(body), body.Options?.Strict ?? false);
    return Results.Ok(new { application_id = body.ApplicationId, template_id = body.TemplateId, valid = problems.All(p => p.Severity != "error"), problems });
}).RequireRateLimiting(RateLimiting.RenderPolicy)
  .WithTags(ApiDocs.PublicTag).WithName("ValidatePayload")
  .WithSummary("Validate a render payload (dry run)")
  .WithDescription("Checks the payload against the template's mapping and returns every problem without producing a PDF. Never billed.");

pub.MapPost("/render", async (RenderRequest body, System.Security.Claims.ClaimsPrincipal u,
    ApplicationsRepository apps, TemplateRepository repo, PayloadValidator validator, PdfFormFiller filler) =>
{
    var caller = Caller.From(u);
    if (AccessCheck.ToResult(await AccessCheck.EvaluateAsync(caller, body.ApplicationId, apps)) is { } denied) return denied;

    var t = await repo.GetInApplicationAsync(body.TemplateId, body.ApplicationId);
    if (t is null) return Problems.Of("template_not_found", "Not Found", 404, "No such template in that application.");

    var mapping = FieldMapping.LoadJson(t.MappingJson);
    var payload = Payload(body);
    var problems = validator.Validate(mapping, payload, body.Options?.Strict ?? false);
    var errors = problems.Where(p => p.Severity == "error").ToList();
    if (errors.Count > 0)
        // standards/security.md rule 15 exception: the portal and the existing validation contract depend on
        // this exact shape (problems, error_count), so it keeps its own fields rather than the RFC 9457 ones -
        // but is still served as application/problem+json.
        return Results.Json(new { title = "Payload rejected", status = 400, application_id = body.ApplicationId, template_id = body.TemplateId, error_count = errors.Count, problems },
            statusCode: 400, contentType: "application/problem+json");

    var pdf = (await repo.GetPdfAsync(body.TemplateId))!;
    var tmp = Path.GetTempFileName();
    try
    {
        await File.WriteAllBytesAsync(tmp, pdf);
        var result = filler.Fill(tmp, mapping, PayloadConverter.ToData(mapping, payload), body.Options?.ReadOnly ?? false);
        return Results.File(result.Pdf, "application/pdf", $"{t.Name}.pdf");
    }
    finally { File.Delete(tmp); }
}).RequireRateLimiting(RateLimiting.RenderPolicy)
  .WithTags(ApiDocs.PublicTag).WithName("RenderPdf")
  .WithSummary("Render a PDF from a template")
  .WithDescription("Validates the JSON payload against the template mapping, then fills the PDF. Returns application/pdf on success, or 400 with a list of problems { key, code, message, severity }. options.strict rejects keys not in the mapping; options.read_only locks every field.")
  .Produces(200, contentType: "application/pdf").ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(429);

// Everything below /api/v1 except /auth/login, /render and /validate requires a portal token.
var secured = api.MapGroup("").RequireAuthorization("Portal");

// ============================================================================================
// Templates resource
// ============================================================================================

// GET /api/v1/templates?applicationId=
secured.MapGet("/templates", async (string? applicationId, System.Security.Claims.ClaimsPrincipal u, TemplateRepository repo, ApplicationsRepository apps) =>
{
    if (string.IsNullOrWhiteSpace(applicationId))
        return Results.BadRequest(new { code = "application_required", error = "applicationId is required." });
    var app = await apps.GetAsync(applicationId);
    if (app is null || app.OwnerUserId != UserId(u)) return Results.NotFound();

    var rows = await repo.ListAsync(applicationId);
    return Results.Ok(rows.Select(t => new
    {
        id = t.TemplateId, applicationId = t.ApplicationId, t.Name, t.FileName, t.PageCount,
        mappedKeys = FieldMapping.LoadJson(t.MappingJson).Entries.Count, t.CreatedAt, t.UpdatedAt
    }));
});

// POST /api/v1/templates  (multipart: file, applicationId, name?) -> 201 with discovered fields
secured.MapPost("/templates", async (HttpRequest req, System.Security.Claims.ClaimsPrincipal u, TemplateRepository repo, ApplicationsRepository apps, ILogger<Program> logger) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "Send multipart/form-data with a 'file' part." });
    var form = await req.ReadFormAsync();

    var applicationId = form["applicationId"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(applicationId))
        return Results.BadRequest(new { code = "application_required", error = "An applicationId part is required." });
    var app = await apps.GetAsync(applicationId);
    if (app is null || app.OwnerUserId != UserId(u)) return Results.NotFound();

    var file = form.Files["file"];
    if (file is null || file.Length == 0) return Results.BadRequest(new { error = "'file' part is missing or empty." });
    if (file.Length > 20 * 1024 * 1024) return Results.BadRequest(new { error = "PDF exceeds 20 MB." });

    using var ms = new MemoryStream(); await file.CopyToAsync(ms); var bytes = ms.ToArray();
    (int pages, List<DiscoveredField> fields) discovered;
    try { discovered = FieldDiscovery.Discover(bytes); }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "PDF parse failed for file: {FileName}", file.FileName);
        return Results.Json(
            new { type = "https://docs.pdftemplate.example/errors/pdf_unreadable", title = "Unprocessable Entity", status = 422, code = "pdf_unreadable", error = "File is not a readable PDF.", detail = "The file could not be read as a PDF. It may be corrupt, encrypted, or not a PDF at all." },
            statusCode: 422, contentType: "application/problem+json");
    }

    var name = form["name"].FirstOrDefault() is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(file.FileName);
    var t = await repo.CreateAsync(applicationId, name, file.FileName, bytes, discovered.pages, """{"fields":{}}""");
    return Results.Created($"/api/v1/templates/{t.TemplateId}", TemplateDto(t, discovered.fields));
}).DisableAntiforgery();

// GET /api/v1/templates/{id}  -> metadata + discovered fields + mapping
secured.MapGet("/templates/{id}", async (string id, System.Security.Claims.ClaimsPrincipal u, TemplateRepository repo) =>
{
    var t = await repo.GetForOwnerAsync(id, UserId(u));
    if (t is null) return Results.NotFound();
    var pdf = (await repo.GetPdfAsync(id))!;
    return Results.Ok(TemplateDto(t, FieldDiscovery.Discover(pdf).Fields));
});

// GET /api/v1/templates/{id}/file  -> the PDF (portal renders it with pdf.js)
secured.MapGet("/templates/{id}/file", async (string id, System.Security.Claims.ClaimsPrincipal u, TemplateRepository repo) =>
{
    var t = await repo.GetForOwnerAsync(id, UserId(u));
    if (t is null) return Results.NotFound();
    var pdf = await repo.GetPdfAsync(id);
    return pdf is null ? Results.NotFound() : Results.File(pdf, "application/pdf", t.FileName);
});

// PUT /api/v1/templates/{id}/mapping  body = { "fields": { ... } }
secured.MapPut("/templates/{id}/mapping", async (string id, JsonElement body, System.Security.Claims.ClaimsPrincipal u, TemplateRepository repo) =>
{
    if (await repo.GetForOwnerAsync(id, UserId(u)) is null) return Results.NotFound();
    string json;
    try { json = JsonSerializer.Serialize(body); _ = FieldMapping.LoadJson(json); }
    catch (Exception ex) { return Results.BadRequest(new { error = "Mapping document is invalid.", detail = ex.Message }); }
    await repo.UpdateMappingAsync(id, json);
    return Results.Ok(new { id, mappedKeys = FieldMapping.LoadJson(json).Entries.Count, updatedAt = DateTime.UtcNow });
});

// DELETE /api/v1/templates/{id}
secured.MapDelete("/templates/{id}", async (string id, System.Security.Claims.ClaimsPrincipal u, TemplateRepository repo) =>
{
    if (await repo.GetForOwnerAsync(id, UserId(u)) is null) return Results.NotFound();
    return await repo.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
});

// ============================================================================================
// Applications (tenant) - portal only
// ============================================================================================

secured.MapGet("/applications", async (System.Security.Claims.ClaimsPrincipal u, ApplicationsRepository apps) =>
{
    var rows = await apps.ListAsync(UserId(u));
    var result = new List<object>(rows.Count);
    foreach (var a in rows)
        result.Add(new { application_id = a.ApplicationId, name = a.Name, template_count = await apps.TemplateCountAsync(a.ApplicationId), created_at = a.CreatedAt, updated_at = a.UpdatedAt });
    return Results.Ok(result);
});

secured.MapPost("/applications", async (ApplicationName body, System.Security.Claims.ClaimsPrincipal u, ApplicationsRepository apps) =>
{
    if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest(new { code = "name_required", error = "A name is required." });
    var a = await apps.CreateAsync(UserId(u), body.Name.Trim());
    return Results.Created($"/api/v1/applications/{a.ApplicationId}",
        new { application_id = a.ApplicationId, name = a.Name, template_count = 0, created_at = a.CreatedAt, updated_at = a.UpdatedAt });
});

secured.MapPut("/applications/{id}", async (string id, ApplicationName body, System.Security.Claims.ClaimsPrincipal u, ApplicationsRepository apps) =>
{
    if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest(new { code = "name_required", error = "A name is required." });
    if (!await apps.RenameAsync(id, UserId(u), body.Name.Trim())) return Results.NotFound();
    var a = (await apps.GetAsync(id))!;
    return Results.Ok(new { application_id = a.ApplicationId, name = a.Name, template_count = await apps.TemplateCountAsync(id), created_at = a.CreatedAt, updated_at = a.UpdatedAt });
});

secured.MapDelete("/applications/{id}", async (string id, System.Security.Claims.ClaimsPrincipal u, ApplicationsRepository apps) =>
{
    var a = await apps.GetAsync(id);
    if (a is null || a.OwnerUserId != UserId(u)) return Results.NotFound();
    // Fast path: reject up front when we can already see templates.
    if (await apps.TemplateCountAsync(id) > 0) return ApplicationNotEmpty();
    try
    {
        return await apps.DeleteAsync(id, UserId(u)) ? Results.NoContent() : Results.NotFound();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT: a template was inserted between the count check and the delete.
    {
        return ApplicationNotEmpty();
    }
});

// ============================================================================================
// API keys - portal only. Keys themselves may never call these endpoints (standards/security.md rule 3).
// ============================================================================================

// Shared shape: a key as the portal sees it, never including the secret.
static async Task<List<object>> NamedApplicationsAsync(ApiKeyRepository keys, ApplicationsRepository apps, string apiKeyId)
{
    var ids = await keys.ApplicationIdsAsync(apiKeyId);
    var named = new List<object>(ids.Count);
    foreach (var id in ids)
        if (await apps.GetAsync(id) is { } a) named.Add(new { application_id = a.ApplicationId, name = a.Name });
    return named;
}

static async Task<object> KeyDto(ApiKeyRow k, ApiKeyRepository keys, ApplicationsRepository apps) => new
{
    api_key_id = k.ApiKeyId,
    name = k.Name,
    prefix = k.Prefix,
    applications = await NamedApplicationsAsync(keys, apps, k.ApiKeyId),
    created_at = k.CreatedAt,
    last_used_at = k.LastUsedAt,
    revoked_at = k.RevokedAt,
};

// Every named application must exist and belong to the caller.
static async Task<bool> OwnsAllAsync(IReadOnlyList<string> applicationIds, string userId, ApplicationsRepository apps)
{
    foreach (var id in applicationIds)
        if (await apps.GetAsync(id) is not { } a || a.OwnerUserId != userId) return false;
    return true;
}

// standards/security.md rule 15: error responses are RFC 9457 problem details, never a raw { code, error } body,
// and detail is a fixed, safe sentence - never SQL, a path, or a library message.
static IResult KeyProblem(string code, string detail) => Results.Json(
    new { type = $"https://docs.pdftemplate.example/errors/{code}", title = "Bad Request", status = 400, code, detail },
    statusCode: 400, contentType: "application/problem+json");

secured.MapGet("/keys", async (System.Security.Claims.ClaimsPrincipal u, ApiKeyRepository keys, ApplicationsRepository apps) =>
{
    var rows = await keys.ListAsync(UserId(u));
    var result = new List<object>(rows.Count);
    foreach (var k in rows) result.Add(await KeyDto(k, keys, apps));
    return Results.Ok(result);
});

secured.MapPost("/keys", async (CreateKeyRequest body, System.Security.Claims.ClaimsPrincipal u, ApiKeyRepository keys, ApplicationsRepository apps) =>
{
    if (string.IsNullOrWhiteSpace(body.Name)) return KeyProblem("name_required", "A name is required.");
    var ids = body.ApplicationIds ?? [];
    if (!await OwnsAllAsync(ids, UserId(u), apps))
        return KeyProblem("application_not_owned", "Every application id must be one of your own applications.");

    var generated = ApiKeyService.Generate();
    var row = await keys.CreateAsync(UserId(u), body.Name.Trim(), generated, ids);
    return Results.Created($"/api/v1/keys/{row.ApiKeyId}", new
    {
        api_key_id = row.ApiKeyId,
        name = row.Name,
        prefix = row.Prefix,
        applications = await NamedApplicationsAsync(keys, apps, row.ApiKeyId),
        created_at = row.CreatedAt,
        last_used_at = row.LastUsedAt,
        revoked_at = row.RevokedAt,
        secret = generated.Secret,                       // shown exactly once; only the hash is stored
    });
});

secured.MapPut("/keys/{id}/applications", async (string id, AssignApplicationsRequest body, System.Security.Claims.ClaimsPrincipal u, ApiKeyRepository keys, ApplicationsRepository apps) =>
{
    var k = await keys.GetAsync(id);
    if (k is null || k.OwnerUserId != UserId(u)) return Results.NotFound();
    var ids = body.ApplicationIds ?? [];
    if (!await OwnsAllAsync(ids, UserId(u), apps))
        return KeyProblem("application_not_owned", "Every application id must be one of your own applications.");

    await keys.SetApplicationsAsync(id, ids);
    return Results.Ok(await KeyDto((await keys.GetAsync(id))!, keys, apps));
});

secured.MapDelete("/keys/{id}", async (string id, System.Security.Claims.ClaimsPrincipal u, ApiKeyRepository keys) =>
{
    var k = await keys.GetAsync(id);
    if (k is null || k.OwnerUserId != UserId(u)) return Results.NotFound();
    await keys.RevokeAsync(id, UserId(u));               // idempotent: already-revoked still answers 204
    return Results.NoContent();
});

// ============================================================================================
// POC business data (SQLite sample) -> render through the same engine. Portal only.
// ============================================================================================
secured.MapGet("/grant-applications/{applicationId:int}", async (int applicationId, GrantApplicationRepository repo) =>
{
    var data = await repo.LoadAsync(applicationId);
    return data is null ? Results.NotFound() : Results.Ok(data.ToDictionary(k => k.Key, k => k.Value is byte[] b ? $"<{b.Length} bytes png>" : k.Value));
});

secured.MapPost("/grant-applications/{applicationId:int}/render", async (int applicationId, string templateId, bool readOnly,
    System.Security.Claims.ClaimsPrincipal u, GrantApplicationRepository apps, TemplateRepository repo, PdfFormFiller filler) =>
{
    var data = await apps.LoadAsync(applicationId); if (data is null) return Results.NotFound(new { error = "Application not found." });
    var t = await repo.GetForOwnerAsync(templateId, UserId(u)); if (t is null) return Results.NotFound(new { error = "Template not found." });
    var tmp = Path.GetTempFileName();
    try
    {
        await File.WriteAllBytesAsync(tmp, (await repo.GetPdfAsync(templateId))!);
        var result = filler.Fill(tmp, FieldMapping.LoadJson(t.MappingJson), data, readOnly);
        return Results.File(result.Pdf, "application/pdf", $"{t.Name}_{applicationId}.pdf");
    }
    finally { File.Delete(tmp); }
}).RequireRateLimiting(RateLimiting.RenderPolicy);

app.MapFallbackToFile("index.html");           // SPA fallback (no-op when wwwroot is absent)
app.Run();

// ---- helpers --------------------------------------------------------------------------------
static object TemplateDto(TemplateRow t, List<DiscoveredField> fields) => new
{
    id = t.TemplateId,
    applicationId = t.ApplicationId,
    t.Name, t.FileName, t.PageCount, t.CreatedAt, t.UpdatedAt,
    fields,
    mapping = JsonSerializer.Deserialize<JsonElement>(t.MappingJson),
};

// The portal token's subject claim is the user id.
static string UserId(System.Security.Claims.ClaimsPrincipal u) =>
    u.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? u.FindFirst("sub")?.Value
    ?? throw new InvalidOperationException("Authenticated principal has no subject.");

// A missing `data` object is treated as empty, so required-field problems are reported normally.
static JsonElement Payload(RenderRequest body) =>
    body.Data.ValueKind == JsonValueKind.Undefined
        ? JsonDocument.Parse("{}").RootElement
        : body.Data;

// Same 409 body whether we saw the templates in the up-front count check or the delete itself hit the
// foreign key (a concurrent upload landed between the two) - the caller can't tell which path was taken,
// and never sees the raw constraint text (standards/security.md rule 15).
static IResult ApplicationNotEmpty() =>
    Results.Json(new { code = "application_not_empty", error = "Delete or move its templates first." }, statusCode: 409);

/// <summary>Seed sample templates into the Default application the first time the table is empty.</summary>
static async Task SeedTemplatesAsync(IServiceProvider services, IConfiguration cfg, IWebHostEnvironment env)
{
    using var scope = services.CreateScope();
    var repo = scope.ServiceProvider.GetRequiredService<TemplateRepository>();
    if (await repo.CountAsync() > 0) return;

    using var c = scope.ServiceProvider.GetRequiredService<Database>().Open();
    var applicationId = await c.ExecuteScalarAsync<string?>("SELECT application_id FROM applications ORDER BY created_at LIMIT 1");
    if (applicationId is null) return;                                   // no user, no application: nothing to seed into

    var dir = Path.Combine(env.ContentRootPath, cfg["Pdf:TemplatesDir"] ?? "Templates");
    if (!Directory.Exists(dir)) return;
    foreach (var pdf in Directory.GetFiles(dir, "*.pdf"))
    {
        var bytes = await File.ReadAllBytesAsync(pdf);
        var (pages, _) = FieldDiscovery.Discover(bytes);
        var mappingPath = Path.ChangeExtension(pdf, ".mapping.json");
        var mapping = File.Exists(mappingPath) ? await File.ReadAllTextAsync(mappingPath) : """{"fields":{}}""";
        await repo.CreateAsync(applicationId, Path.GetFileNameWithoutExtension(pdf).Replace('_', ' '), Path.GetFileName(pdf), bytes, pages, mapping);
    }
}
public sealed record ApplicationName(string Name);

// System.Text.Json's camelCase policy cannot match snake_case, so the public names are explicit.
public sealed record CreateKeyRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("application_ids")] string[]? ApplicationIds);

public sealed record AssignApplicationsRequest(
    [property: JsonPropertyName("application_ids")] string[]? ApplicationIds);

/// <summary>Marker endpoint metadata: this endpoint is subject to the 5 MB public payload cap. A dedicated
/// marker (rather than matching on .WithName(...) or the route path) can't silently stop applying if an
/// endpoint is later renamed or given an extra route.</summary>
public sealed class PublicPayloadCapMarker;
public partial class Program { }   // exposes the entry point to WebApplicationFactory in tests
