using Dapper;
using PdfFill.Api.Data;

namespace PdfFill.Api.Services;

public sealed record TemplateRow(string TemplateId, string ApplicationId, string Name, string FileName, long PageCount, string MappingJson, string CreatedAt, string UpdatedAt);

/// <summary>SQLite-backed store for uploaded templates. Every template belongs to exactly one application.</summary>
public sealed class TemplateRepository(Database db)
{
    private const string Columns =
        "template_id AS TemplateId, application_id AS ApplicationId, name AS Name, file_name AS FileName, " +
        "page_count AS PageCount, mapping_json AS MappingJson, created_at AS CreatedAt, updated_at AS UpdatedAt";

    public async Task<IReadOnlyList<TemplateRow>> ListAsync(string applicationId)
    {
        using var c = db.Open();
        return (await c.QueryAsync<TemplateRow>(
            $"SELECT {Columns} FROM templates WHERE application_id = @applicationId ORDER BY updated_at DESC",
            new { applicationId })).ToList();
    }

    public async Task<TemplateRow?> GetAsync(string templateId)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<TemplateRow>(
            $"SELECT {Columns} FROM templates WHERE template_id = @templateId", new { templateId });
    }

    /// <summary>Used by render and validate: the template must live in the application the caller named.</summary>
    public async Task<TemplateRow?> GetInApplicationAsync(string templateId, string applicationId)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<TemplateRow>(
            $"SELECT {Columns} FROM templates WHERE template_id = @templateId AND application_id = @applicationId",
            new { templateId, applicationId });
    }

    /// <summary>Used by portal endpoints: the template's application must be owned by the caller.</summary>
    public async Task<TemplateRow?> GetForOwnerAsync(string templateId, string ownerUserId)
    {
        using var c = db.Open();
        // Columns is qualified per-table here (not reused as-is) because application_id exists on both
        // sides of the join and is otherwise ambiguous to SQLite.
        return await c.QuerySingleOrDefaultAsync<TemplateRow>(
            """
             SELECT t.template_id AS TemplateId, t.application_id AS ApplicationId, t.name AS Name, t.file_name AS FileName,
                    t.page_count AS PageCount, t.mapping_json AS MappingJson, t.created_at AS CreatedAt, t.updated_at AS UpdatedAt
             FROM templates t
             JOIN applications a ON a.application_id = t.application_id
             WHERE t.template_id = @templateId AND a.owner_user_id = @ownerUserId
             """, new { templateId, ownerUserId });
    }

    public async Task<byte[]?> GetPdfAsync(string templateId)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<byte[]>("SELECT pdf FROM templates WHERE template_id = @templateId", new { templateId });
    }

    public async Task<TemplateRow> CreateAsync(string applicationId, string name, string fileName, byte[] pdf, int pageCount, string mappingJson)
    {
        var id = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("o");
        using var c = db.Open();
        await c.ExecuteAsync(
            """
            INSERT INTO templates (template_id, application_id, name, file_name, pdf, page_count, mapping_json, created_at, updated_at)
            VALUES (@id, @applicationId, @name, @fileName, @pdf, @pageCount, @mappingJson, @now, @now)
            """,
            new { id, applicationId, name, fileName, pdf, pageCount, mappingJson, now });
        return (await GetAsync(id))!;
    }

    public async Task<bool> UpdateMappingAsync(string templateId, string mappingJson)
    {
        using var c = db.Open();
        return await c.ExecuteAsync(
            "UPDATE templates SET mapping_json = @mappingJson, updated_at = @now WHERE template_id = @templateId",
            new { templateId, mappingJson, now = DateTime.UtcNow.ToString("o") }) == 1;
    }

    public async Task<bool> DeleteAsync(string templateId)
    {
        using var c = db.Open();
        return await c.ExecuteAsync("DELETE FROM templates WHERE template_id = @templateId", new { templateId }) == 1;
    }

    public async Task<int> CountAsync()
    {
        using var c = db.Open();
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM templates");
    }
}
