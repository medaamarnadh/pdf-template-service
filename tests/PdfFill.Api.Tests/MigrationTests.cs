using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PdfFill.Api.Data;

namespace PdfFill.Api.Tests;

public class MigrationTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"mig-{Guid.NewGuid():N}.db");
    // Windows keeps a pooled Sqlite connection's native handle (and file lock) open past Dispose;
    // clear the pool first so the temp file can actually be deleted.
    public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_file)) File.Delete(_file); }

    /// <summary>Builds a database in the shape that shipped before tenancy existed.</summary>
    private void CreateLegacyDatabase()
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _file }.ToString());
        c.Open();
        c.Execute("""
            CREATE TABLE applications (application_id INTEGER PRIMARY KEY, application_no TEXT NOT NULL, project_title TEXT NOT NULL);
            CREATE TABLE signatures (application_id INTEGER NOT NULL REFERENCES applications(application_id), role TEXT NOT NULL, PRIMARY KEY (application_id, role));
            CREATE TABLE revenue_quarters (application_id INTEGER NOT NULL REFERENCES applications(application_id), row_no INTEGER NOT NULL, PRIMARY KEY (application_id, row_no));
            CREATE TABLE budget_lines (application_id INTEGER NOT NULL REFERENCES applications(application_id), row_no INTEGER NOT NULL, PRIMARY KEY (application_id, row_no));
            CREATE TABLE funding_sources (application_id INTEGER NOT NULL REFERENCES applications(application_id), row_no INTEGER NOT NULL, PRIMARY KEY (application_id, row_no));
            CREATE TABLE compliance_items (application_id INTEGER NOT NULL REFERENCES applications(application_id), code TEXT NOT NULL, PRIMARY KEY (application_id, code));
            CREATE TABLE templates (
              template_id TEXT PRIMARY KEY, name TEXT NOT NULL, file_name TEXT NOT NULL, pdf BLOB NOT NULL,
              page_count INTEGER NOT NULL, mapping_json TEXT NOT NULL DEFAULT '{"fields":{}}',
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE users (user_id TEXT PRIMARY KEY, email TEXT NOT NULL, password_hash TEXT NOT NULL,
              role TEXT NOT NULL, must_change_password INTEGER NOT NULL, created_at TEXT NOT NULL);
            INSERT INTO applications VALUES (5001, 'APP-5001', 'Demo');
            INSERT INTO signatures VALUES (5001, 'applicant');
            INSERT INTO revenue_quarters VALUES (5001, 1);
            INSERT INTO budget_lines VALUES (5001, 1);
            INSERT INTO funding_sources VALUES (5001, 1);
            INSERT INTO compliance_items VALUES (5001, 'ITEM-001');
            INSERT INTO templates VALUES ('tpl_abc123', 'grant', 'g.pdf', X'255044462D', 3, '{"fields":{"a":"A"}}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO users VALUES ('user-1', 'owner@example.com', 'hash', 'owner', 0, '2026-01-01T00:00:00Z');
            """);
    }

    private Database OpenDatabase() => new(
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:File"] = _file }).Build(),
        new TestEnv(AppContext.BaseDirectory));

    [Fact]
    public void Migration_reshapes_a_legacy_database()
    {
        CreateLegacyDatabase();
        OpenDatabase().Migrate();

        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _file }.ToString());
        c.Open();

        Assert.Equal(1L, c.ExecuteScalar<long>("PRAGMA user_version"));
        Assert.Equal("Demo", c.ExecuteScalar<string>("SELECT project_title FROM grant_applications WHERE application_id = 5001"));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM signatures WHERE application_id = 5001"));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM revenue_quarters WHERE application_id = 5001"));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM budget_lines WHERE application_id = 5001"));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM funding_sources WHERE application_id = 5001"));
        Assert.Equal(1L, c.ExecuteScalar<long>("SELECT COUNT(*) FROM compliance_items WHERE application_id = 5001"));

        var appId = c.ExecuteScalar<string>("SELECT application_id FROM applications WHERE name = 'Default'");
        Assert.True(Guid.TryParse(appId, out _));
        Assert.Equal("user-1", c.ExecuteScalar<string>("SELECT owner_user_id FROM applications WHERE name = 'Default'"));

        var templateId = c.ExecuteScalar<string>("SELECT template_id FROM templates");
        Assert.True(Guid.TryParse(templateId, out _));
        Assert.Equal(appId, c.ExecuteScalar<string>("SELECT application_id FROM templates"));
        Assert.Equal("""{"fields":{"a":"A"}}""", c.ExecuteScalar<string>("SELECT mapping_json FROM templates"));
        Assert.Equal(3L, c.ExecuteScalar<long>("SELECT page_count FROM templates"));
        Assert.Empty(c.Query("PRAGMA foreign_key_check"));
    }

    /// <summary>A legacy database that has a template but never had a portal user created in it.</summary>
    private void CreateLegacyDatabaseWithoutUsers()
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _file }.ToString());
        c.Open();
        c.Execute("""
            CREATE TABLE applications (application_id INTEGER PRIMARY KEY, application_no TEXT NOT NULL, project_title TEXT NOT NULL);
            CREATE TABLE templates (
              template_id TEXT PRIMARY KEY, name TEXT NOT NULL, file_name TEXT NOT NULL, pdf BLOB NOT NULL,
              page_count INTEGER NOT NULL, mapping_json TEXT NOT NULL DEFAULT '{"fields":{}}',
              created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE users (user_id TEXT PRIMARY KEY, email TEXT NOT NULL, password_hash TEXT NOT NULL,
              role TEXT NOT NULL, must_change_password INTEGER NOT NULL, created_at TEXT NOT NULL);
            INSERT INTO applications VALUES (5001, 'APP-5001', 'Demo');
            INSERT INTO templates VALUES ('tpl_abc123', 'grant', 'g.pdf', X'255044462D', 3, '{"fields":{"a":"A"}}', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            """);
    }

    /// <summary>
    /// 0001_tenancy.sql assigns every template to a "Default" application owned by the earliest user row. With
    /// templates but no users, that insert matches nothing and the templates rebuild would try to write a NULL
    /// application_id into a NOT NULL column - a bare constraint violation that doesn't say why, and that would
    /// repeat on every container restart. The migration must instead fail early with an actionable message, and
    /// must leave the database exactly as it was (rolled back cleanly, not left half-migrated).
    /// </summary>
    [Fact]
    public void Migration_fails_clearly_when_templates_exist_but_no_users_do()
    {
        CreateLegacyDatabaseWithoutUsers();

        var ex = Assert.Throws<InvalidOperationException>(() => OpenDatabase().Migrate());
        Assert.Contains("no user rows", ex.Message);
        Assert.Contains("0001_tenancy.sql", ex.Message);

        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _file }.ToString());
        c.Open();
        Assert.Equal(0L, c.ExecuteScalar<long>("PRAGMA user_version"));
        Assert.Equal(1L, c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'applications'"));
        Assert.Equal(0L, c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'grant_applications'"));
        Assert.Equal("Demo", c.ExecuteScalar<string>("SELECT project_title FROM applications WHERE application_id = 5001"));
        Assert.Equal("tpl_abc123", c.ExecuteScalar<string>("SELECT template_id FROM templates"));
    }

    [Fact]
    public void Migration_is_idempotent()
    {
        CreateLegacyDatabase();
        var db = OpenDatabase();
        db.Migrate();
        var idAfterFirst = ReadTemplateId();
        db.Migrate();
        Assert.Equal(idAfterFirst, ReadTemplateId());

        string? ReadTemplateId()
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _file }.ToString());
            c.Open();
            return c.ExecuteScalar<string>("SELECT template_id FROM templates");
        }
    }
}

/// <summary>Minimal IWebHostEnvironment so repositories can be built outside a host.</summary>
public sealed class TestEnv(string root) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string ContentRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "PdfFill.Api";
    public string WebRootPath { get; set; } = "";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}
