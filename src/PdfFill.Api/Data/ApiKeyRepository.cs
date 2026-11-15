using System.Security.Cryptography;
using System.Text;
using Dapper;
using PdfFill.Api.Services;

namespace PdfFill.Api.Data;

public sealed record ApiKeyRow(string ApiKeyId, string OwnerUserId, string Name, string Prefix, string CreatedAt, string? LastUsedAt, string? RevokedAt);

/// <summary>API keys and the applications each one may be used against.</summary>
public sealed class ApiKeyRepository(Database db)
{
    private const string Columns =
        "api_key_id AS ApiKeyId, owner_user_id AS OwnerUserId, name AS Name, prefix AS Prefix, " +
        "created_at AS CreatedAt, last_used_at AS LastUsedAt, revoked_at AS RevokedAt";

    public async Task<ApiKeyRow> CreateAsync(string ownerUserId, string name, GeneratedKey key, IReadOnlyList<string> applicationIds)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(
            """
            INSERT INTO api_keys (api_key_id, owner_user_id, name, prefix, hash, created_at, last_used_at, revoked_at)
            VALUES (@id, @ownerUserId, @name, @prefix, @hash, @now, NULL, NULL)
            """,
            new { id, ownerUserId, name, prefix = key.Prefix, hash = key.Hash, now }, tx);
        foreach (var applicationId in applicationIds.Distinct())
            await c.ExecuteAsync("INSERT INTO api_key_applications (api_key_id, application_id) VALUES (@id, @applicationId)",
                new { id, applicationId }, tx);
        tx.Commit();
        return (await GetAsync(id))!;
    }

    public async Task<IReadOnlyList<ApiKeyRow>> ListAsync(string ownerUserId)
    {
        using var c = db.Open();
        return (await c.QueryAsync<ApiKeyRow>(
            $"SELECT {Columns} FROM api_keys WHERE owner_user_id = @ownerUserId ORDER BY created_at DESC",
            new { ownerUserId })).ToList();
    }

    public async Task<ApiKeyRow?> GetAsync(string apiKeyId)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<ApiKeyRow>(
            $"SELECT {Columns} FROM api_keys WHERE api_key_id = @apiKeyId", new { apiKeyId });
    }

    /// <summary>Row shape used only internally by <see cref="FindBySecretAsync"/>, which needs the stored hash
    /// itself to re-verify the match in constant time; the public <see cref="ApiKeyRow"/> never exposes it.</summary>
    private sealed record ApiKeyRowWithHash(string ApiKeyId, string OwnerUserId, string Name, string Prefix, string Hash, string CreatedAt, string? LastUsedAt, string? RevokedAt);

    /// <summary>
    /// Resolves a presented secret to its key row. Returns revoked keys too, so the caller can answer key_revoked.
    ///
    /// standards/security.md rule 1 requires the secret comparison to be constant time. We deliberately do NOT
    /// look this up by `WHERE hash = @hash`: `hash` is UNIQUE, so SQLite would already have decided equality
    /// with its own (non-constant-time, short-circuiting) string comparison before ever returning a row, which
    /// would make any comparison we do afterwards dead code protecting nothing. Instead we look up candidates
    /// by `prefix` — the first ApiKeyService.PrefixLength characters of the secret, which is not itself secret and is safe to filter on
    /// directly. `prefix` is NOT unique (it's `pdfk_live_` plus only 4 random base62 characters, so collisions are
    /// expected), so this can return several rows; we must check all of them, and the constant-time comparison
    /// against the computed hash is the thing that actually decides the match, not the SQL filter. Do not
    /// "simplify" this back to a hash-equality WHERE clause — that's what made the fixed-time compare
    /// unreachable in the first place.
    /// </summary>
    public async Task<ApiKeyRow?> FindBySecretAsync(string secret)
    {
        var prefix = secret.Length >= ApiKeyService.PrefixLength ? secret[..ApiKeyService.PrefixLength] : secret;
        var hash = ApiKeyService.HashOf(secret);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        using var c = db.Open();
        // Column order here must match ApiKeyRowWithHash's constructor parameter order: Dapper materializes
        // records positionally via their constructor, not by name, so a mismatch throws at read time.
        var candidates = await c.QueryAsync<ApiKeyRowWithHash>(
            """
            SELECT api_key_id AS ApiKeyId, owner_user_id AS OwnerUserId, name AS Name, prefix AS Prefix, hash AS Hash,
                   created_at AS CreatedAt, last_used_at AS LastUsedAt, revoked_at AS RevokedAt
            FROM api_keys WHERE prefix = @prefix
            """, new { prefix });

        ApiKeyRowWithHash? match = null;
        foreach (var candidate in candidates)
        {
            // Compare every candidate rather than short-circuiting on the first match, so the work done
            // does not depend on a candidate's position among prefix collisions.
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate.Hash), hashBytes))
                match = candidate;
        }

        return match is null
            ? null
            : new ApiKeyRow(match.ApiKeyId, match.OwnerUserId, match.Name, match.Prefix, match.CreatedAt, match.LastUsedAt, match.RevokedAt);
    }

    public async Task<IReadOnlyList<string>> ApplicationIdsAsync(string apiKeyId)
    {
        using var c = db.Open();
        return (await c.QueryAsync<string>(
            "SELECT application_id FROM api_key_applications WHERE api_key_id = @apiKeyId", new { apiKeyId })).ToList();
    }

    public async Task SetApplicationsAsync(string apiKeyId, IReadOnlyList<string> applicationIds)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync("DELETE FROM api_key_applications WHERE api_key_id = @apiKeyId", new { apiKeyId }, tx);
        foreach (var applicationId in applicationIds.Distinct())
            await c.ExecuteAsync("INSERT INTO api_key_applications (api_key_id, application_id) VALUES (@apiKeyId, @applicationId)",
                new { apiKeyId, applicationId }, tx);
        tx.Commit();
    }

    public async Task<bool> RevokeAsync(string apiKeyId, string ownerUserId)
    {
        using var c = db.Open();
        return await c.ExecuteAsync(
            "UPDATE api_keys SET revoked_at = @now WHERE api_key_id = @apiKeyId AND owner_user_id = @ownerUserId AND revoked_at IS NULL",
            new { apiKeyId, ownerUserId, now = DateTime.UtcNow.ToString("o") }) == 1;
    }

    /// <summary>
    /// Best effort, deliberately: last_used_at is a diagnostic timestamp, not something a request's success
    /// should depend on, so this swallows every exception rather than only <see cref="Microsoft.Data.Sqlite.SqliteException"/>.
    /// In particular, on the render path a client disconnect surfaces here as an OperationCanceledException /
    /// TaskCanceledException, not a SqliteException — and a cancelled request must never fail an otherwise-
    /// successful render just because we couldn't record when the key was last used.
    /// </summary>
    public async Task TouchAsync(string apiKeyId)
    {
        try
        {
            using var c = db.Open();
            await c.ExecuteAsync("UPDATE api_keys SET last_used_at = @now WHERE api_key_id = @apiKeyId",
                new { apiKeyId, now = DateTime.UtcNow.ToString("o") });
        }
        catch { /* diagnostic only; never let this fail the caller's request */ }
    }
}
