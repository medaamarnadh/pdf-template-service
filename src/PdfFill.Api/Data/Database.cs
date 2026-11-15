using Dapper;
using Microsoft.Data.Sqlite;

namespace PdfFill.Api.Data;

/// <summary>Creates, seeds and migrates the SQLite database on startup.</summary>
public sealed class Database(IConfiguration cfg, IWebHostEnvironment env)
{
    private readonly string _file = Path.Combine(env.ContentRootPath, cfg["Database:File"] ?? "Data/poc.db");

    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _file }.ToString();

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(ConnectionString);
        c.Open();
        return c;
    }

    /// <summary>Foreign keys off: table rebuilds in migrations would otherwise trip referential checks.</summary>
    private SqliteConnection OpenForMigration()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _file, ForeignKeys = false }.ToString());
        c.Open();
        return c;
    }

    public void EnsureCreated()
    {
        if (File.Exists(_file) && cfg.GetValue("Database:RecreateOnStartup", false)) File.Delete(_file);

        if (!File.Exists(_file))
        {
            var dir = Path.Combine(env.ContentRootPath, "Data");   // SQL scripts ship with the app, not with the DB file
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            using var c = Open();
            foreach (var script in new[] { "schema.sql", "seed-users.sql", "seed.sql" })
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = File.ReadAllText(Path.Combine(dir, script));
                cmd.ExecuteNonQuery();
            }
            // Demo instances share the seeded account, and password changes are refused there, so the
            // forced-rotation flag would be a dead end for every visitor.
            if (cfg.GetValue("Demo:Enabled", false))
                c.Execute("UPDATE users SET must_change_password = 0");

            return;                                                 // schema.sql already declares the current user_version
        }

        Migrate();
    }

    /// <summary>Applies every migration newer than the file's PRAGMA user_version, each in its own transaction.</summary>
    public void Migrate()
    {
        var dir = Path.Combine(env.ContentRootPath, "Data", "migrations");
        if (!Directory.Exists(dir)) return;

        using var c = OpenForMigration();
        var current = c.ExecuteScalar<long>("PRAGMA user_version");

        foreach (var path in Directory.GetFiles(dir, "*.sql").OrderBy(p => p, StringComparer.Ordinal))
        {
            var version = long.Parse(Path.GetFileName(path).Split('_')[0]);
            if (version <= current) continue;

            using var tx = c.BeginTransaction();
            CheckPreconditions(c, tx, version);
            c.Execute(File.ReadAllText(path), transaction: tx);
            c.Execute($"PRAGMA user_version = {version}", transaction: tx);

            // Check foreign key integrity before commit so rollback happens automatically on failure.
            var broken = c.Query("PRAGMA foreign_key_check").ToList();
            if (broken.Count > 0) throw new InvalidOperationException($"Migration {version} left {broken.Count} broken foreign key rows.");

            tx.Commit();
        }
    }

    /// <summary>
    /// Checks that hold before a migration's SQL is allowed to run, so a bad precondition fails loudly and early
    /// instead of tripping a NOT NULL/constraint violation partway through a table rebuild. The transaction this
    /// runs in is never committed on failure, so `using var tx` rolls it back on the way out and the database is
    /// left exactly as it was - this can safely retry (and keep failing the same clear way) on every restart
    /// until an operator fixes the precondition.
    /// </summary>
    private static void CheckPreconditions(SqliteConnection c, SqliteTransaction tx, long version)
    {
        if (version == 1)
        {
            // 0001_tenancy.sql assigns every existing template to a "Default" application owned by the earliest
            // user row. With templates but no users, that INSERT selects no application, and the templates
            // rebuild then tries to write a NULL application_id into a NOT NULL column - a constraint violation
            // that doesn't say why. Catch the actual precondition instead.
            var templateCount = c.ExecuteScalar<long>("SELECT COUNT(*) FROM templates", transaction: tx);
            var userCount = c.ExecuteScalar<long>("SELECT COUNT(*) FROM users", transaction: tx);
            if (templateCount > 0 && userCount == 0)
                throw new InvalidOperationException(
                    "Migration 0001_tenancy.sql cannot run: this database has " + templateCount +
                    " template(s) but no user rows, so templates have no one to be assigned an owning " +
                    "application under. Create at least one user in this database (see seed-users.sql, or the " +
                    "'dotnet <api-dll> hash-password <password>' helper plus an INSERT into users) before " +
                    "starting this release again.");
        }
    }
}
