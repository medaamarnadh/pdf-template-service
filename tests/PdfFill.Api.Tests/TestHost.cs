using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace PdfFill.Api.Tests;

/// <summary>A disposable API host on its own database, with helpers for an authenticated portal client.</summary>
public sealed class TestHost : WebApplicationFactory<Program>
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid():N}.db");

    /// <summary>Email of the second seeded user, for tests that need to assert who does NOT see whose data.</summary>
    public const string SecondUserEmail = "other@example.com";
    private const string SecondUserPassword = "Other-User-Seed-2026!";

    /// <summary>Set when constructing the fixture to run this host as a public demo instance.</summary>
    public bool Demo { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseSetting("Demo:Enabled", Demo ? "true" : "false");
        b.UseSetting("Database:File", _dbFile);
        b.UseSetting("Database:RecreateOnStartup", "true");
        b.UseSetting("Auth:JwtKey", "unit-test-key-0123456789abcdef0123456789abcdef");
        b.UseSetting("RateLimiting:Enabled", "false");
    }

    /// <summary>A client carrying the seeded owner's portal token.</summary>
    public async Task<HttpClient> PortalClientAsync()
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TestAuth.LoginAsync(c));
        return c;
    }

    /// <summary>
    /// Seeds a second, distinct portal user directly into this host's database (there is no registration
    /// endpoint) and returns a client authenticated as them. Used for cross-tenant access tests. Idempotent,
    /// since a TestHost fixture is shared across every [Fact] in a test class.
    /// </summary>
    public async Task<HttpClient> SecondUserClientAsync()
    {
        var client = CreateClient(); // forces host startup, so the sqlite file already has schema + seed data

        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbFile }.ToString()))
        {
            await c.OpenAsync();
            var exists = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM users WHERE email = @email", new { email = SecondUserEmail });
            if (exists == 0)
            {
                await c.ExecuteAsync(
                    "INSERT INTO users (user_id, email, password_hash, role, must_change_password, created_at) " +
                    "VALUES (@id, @email, @hash, @role, 0, @now)",
                    new
                    {
                        id = Guid.NewGuid().ToString(),
                        email = SecondUserEmail,
                        hash = PdfFill.Api.Services.PasswordHasher.Hash(SecondUserPassword),
                        role = "owner",
                        now = DateTime.UtcNow.ToString("o"),
                    });
            }
        }

        var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = SecondUserEmail, password = SecondUserPassword });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!["token"].GetString()!);
        return client;
    }

    /// <summary>Creates an application and returns its id.</summary>
    public static async Task<string> CreateApplicationAsync(HttpClient portal, string name)
    {
        var r = await portal.PostAsJsonAsync("/api/v1/applications", new { name });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>())!["application_id"].GetString()!;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbFile)) { try { File.Delete(_dbFile); } catch (IOException) { } }
    }
}
