using Dapper;

namespace PdfFill.Api.Data;

public sealed record ApplicationRow(string ApplicationId, string OwnerUserId, string Name, string CreatedAt, string UpdatedAt);

/// <summary>Tenant applications: the unit that owns templates and that API keys are assigned to.</summary>
public sealed class ApplicationsRepository(Database db)
{
    private const string Columns =
        "application_id AS ApplicationId, owner_user_id AS OwnerUserId, name AS Name, created_at AS CreatedAt, updated_at AS UpdatedAt";

    public async Task<IReadOnlyList<ApplicationRow>> ListAsync(string ownerUserId)
    {
        using var c = db.Open();
        return (await c.QueryAsync<ApplicationRow>(
            $"SELECT {Columns} FROM applications WHERE owner_user_id = @ownerUserId ORDER BY name",
            new { ownerUserId })).ToList();
    }

    public async Task<ApplicationRow?> GetAsync(string applicationId)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<ApplicationRow>(
            $"SELECT {Columns} FROM applications WHERE application_id = @applicationId", new { applicationId });
    }

    public async Task<ApplicationRow> CreateAsync(string ownerUserId, string name)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");
        using var c = db.Open();
        await c.ExecuteAsync(
            "INSERT INTO applications (application_id, owner_user_id, name, created_at, updated_at) VALUES (@id, @ownerUserId, @name, @now, @now)",
            new { id, ownerUserId, name, now });
        return (await GetAsync(id))!;
    }

    public async Task<bool> RenameAsync(string applicationId, string ownerUserId, string name)
    {
        using var c = db.Open();
        return await c.ExecuteAsync(
            "UPDATE applications SET name = @name, updated_at = @now WHERE application_id = @applicationId AND owner_user_id = @ownerUserId",
            new { applicationId, ownerUserId, name, now = DateTime.UtcNow.ToString("o") }) == 1;
    }

    public async Task<bool> DeleteAsync(string applicationId, string ownerUserId)
    {
        using var c = db.Open();
        return await c.ExecuteAsync(
            "DELETE FROM applications WHERE application_id = @applicationId AND owner_user_id = @ownerUserId",
            new { applicationId, ownerUserId }) == 1;
    }

    public async Task<int> TemplateCountAsync(string applicationId)
    {
        using var c = db.Open();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM templates WHERE application_id = @applicationId", new { applicationId });
    }
}
