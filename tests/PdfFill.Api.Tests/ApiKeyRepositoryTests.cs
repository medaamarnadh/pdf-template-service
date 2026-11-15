using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PdfFill.Api.Data;
using PdfFill.Api.Services;

namespace PdfFill.Api.Tests;

/// <summary>
/// Exercises ApiKeyRepository.FindBySecretAsync directly against a real SQLite file (built the same way
/// MigrationTests does), rather than through TestHost, since it is not wired to an HTTP route yet (Task 5).
/// </summary>
public class ApiKeyRepositoryTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"apikeys-{Guid.NewGuid():N}.db");
    private const string OwnerUserId = "208bc8f0-91b8-415a-a835-91bd22bf1dbf"; // seeded in seed-users.sql

    // Windows keeps a pooled Sqlite connection's native handle (and file lock) open past Dispose;
    // clear the pool first so the temp file can actually be deleted.
    public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_file)) File.Delete(_file); }

    private Database OpenDatabase() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:File"] = _file }).Build(),
        new TestEnv(AppContext.BaseDirectory));

    private ApiKeyRepository OpenRepository()
    {
        var db = OpenDatabase();
        db.EnsureCreated();
        return new ApiKeyRepository(db);
    }

    [Fact]
    public async Task A_secret_that_matches_no_stored_key_resolves_to_null()
    {
        var repo = OpenRepository();
        var generated = ApiKeyService.Generate();
        await repo.CreateAsync(OwnerUserId, "Real key", generated, []);

        var result = await repo.FindBySecretAsync("pdfk_live_" + new string('x', 32));

        Assert.Null(result);
    }

    [Fact]
    public async Task A_key_whose_prefix_matches_but_whose_secret_differs_resolves_to_null()
    {
        var repo = OpenRepository();
        var generated = ApiKeyService.Generate();
        var sharedPrefix = generated.Prefix; // "pdfk_live_" + 4 random chars

        // Insert a second row directly whose prefix collides with `generated`'s but whose hash is of a
        // different secret entirely, simulating the prefix collision that the 4-random-character prefix
        // space makes possible. If the lookup trusted the SQL filter on `prefix` instead of deciding the
        // match with the constant-time hash comparison, this row could be mistaken for a match.
        var otherSecret = ApiKeyService.LivePrefix + new string('9', 32);
        var otherHash = ApiKeyService.HashOf(otherSecret);
        using (var c = OpenDatabase().Open())
        {
            await c.ExecuteAsync(
                "INSERT INTO api_keys (api_key_id, owner_user_id, name, prefix, hash, created_at, last_used_at, revoked_at) " +
                "VALUES (@id, @ownerUserId, @name, @prefix, @hash, @now, NULL, NULL)",
                new { id = Guid.NewGuid().ToString(), ownerUserId = OwnerUserId, name = "Colliding prefix", prefix = sharedPrefix, hash = otherHash, now = DateTime.UtcNow.ToString("o") });
        }

        // A different secret again, sharing the same prefix as both `generated` and the row
        // just inserted, but matching the hash of neither.
        var probeSecret = sharedPrefix + new string('7', ApiKeyService.LivePrefix.Length + 32 - sharedPrefix.Length);

        var result = await repo.FindBySecretAsync(probeSecret);

        Assert.Null(result);
    }

    [Fact]
    public async Task A_secret_that_matches_a_stored_key_resolves_to_that_key()
    {
        var repo = OpenRepository();
        var generated = ApiKeyService.Generate();
        var created = await repo.CreateAsync(OwnerUserId, "Real key", generated, []);

        var result = await repo.FindBySecretAsync(generated.Secret);

        Assert.NotNull(result);
        Assert.Equal(created.ApiKeyId, result!.ApiKeyId);
    }

    [Fact]
    public async Task TouchAsync_does_not_throw_for_an_unknown_key_id()
    {
        var repo = OpenRepository();

        // No matching row, so the UPDATE affects zero rows; TouchAsync's contract is that it never throws.
        var exception = await Record.ExceptionAsync(() => repo.TouchAsync(Guid.NewGuid().ToString()));

        Assert.Null(exception);
    }
}
