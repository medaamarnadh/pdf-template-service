using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PdfFill.Api.Tests;

/// <summary>
/// standards/security.md rule 8: every new endpoint needs a cross-tenant read attempt. Owner A creates an
/// application and uploads a template into it; user B (a second, independently seeded portal user) then tries
/// every application and template endpoint against A's resources. Rule 6: a mismatch is 404, never 403 with a
/// hint that the resource exists elsewhere - so every assertion here is NotFound, and A's data must never appear
/// in anything B receives.
/// </summary>
public class CrossTenantTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public CrossTenantTests(TestHost host) => _host = host;

    /// <summary>A one-page PDF with no form fields - enough for upload and discovery.</summary>
    private static byte[] BlankPdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] pdf, string applicationId, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"{name}.pdf");
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(applicationId), "applicationId");
        return form;
    }

    /// <summary>Owner A's client, user B's client, A's application id, and a template A uploaded into it.</summary>
    private async Task<(HttpClient ownerA, HttpClient userB, string applicationId, string templateId)> SeedAsync()
    {
        var ownerA = await _host.PortalClientAsync();
        var userB = await _host.SecondUserClientAsync();
        var applicationId = await TestHost.CreateApplicationAsync(ownerA, $"Owner A's app {Guid.NewGuid():N}");

        var uploaded = await ownerA.PostAsync("/api/v1/templates", Upload(BlankPdf(), applicationId, "owner-a-template"));
        uploaded.EnsureSuccessStatusCode();
        var body = await uploaded.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var templateId = body!["id"].GetString()!;

        return (ownerA, userB, applicationId, templateId);
    }

    [Fact]
    public async Task List_applications_never_includes_another_owners_application()
    {
        var (ownerA, userB, applicationId, _) = await SeedAsync();

        var asB = await userB.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/v1/applications");
        Assert.DoesNotContain(asB!, a => a["application_id"].GetString() == applicationId);

        // Positive case: the check discriminates by owner rather than blocking everyone - A still sees it.
        var asA = await ownerA.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/v1/applications");
        Assert.Contains(asA!, a => a["application_id"].GetString() == applicationId);
    }

    [Fact]
    public async Task Rename_another_owners_application_is_not_found()
    {
        var (_, userB, applicationId, _) = await SeedAsync();
        var r = await userB.PutAsJsonAsync($"/api/v1/applications/{applicationId}", new { name = "Hijacked" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Delete_another_owners_application_is_not_found()
    {
        var (_, userB, applicationId, _) = await SeedAsync();
        var r = await userB.DeleteAsync($"/api/v1/applications/{applicationId}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task List_templates_in_another_owners_application_is_not_found()
    {
        var (_, userB, applicationId, _) = await SeedAsync();
        var r = await userB.GetAsync($"/api/v1/templates?applicationId={applicationId}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Uploading_into_another_owners_application_is_not_found()
    {
        var (_, userB, applicationId, _) = await SeedAsync();
        var r = await userB.PostAsync("/api/v1/templates", Upload(BlankPdf(), applicationId, "intruder"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Get_another_owners_template_is_not_found()
    {
        var (_, userB, _, templateId) = await SeedAsync();
        var r = await userB.GetAsync($"/api/v1/templates/{templateId}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Get_another_owners_template_file_is_not_found()
    {
        var (_, userB, _, templateId) = await SeedAsync();
        var r = await userB.GetAsync($"/api/v1/templates/{templateId}/file");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Updating_another_owners_template_mapping_is_not_found()
    {
        var (_, userB, _, templateId) = await SeedAsync();
        var r = await userB.PutAsJsonAsync($"/api/v1/templates/{templateId}/mapping", new { fields = new { } });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Delete_another_owners_template_is_not_found()
    {
        var (ownerA, userB, _, templateId) = await SeedAsync();

        var asB = await userB.DeleteAsync($"/api/v1/templates/{templateId}");
        Assert.Equal(HttpStatusCode.NotFound, asB.StatusCode);

        // Positive case: A, the actual owner, can still delete it.
        var asA = await ownerA.DeleteAsync($"/api/v1/templates/{templateId}");
        Assert.Equal(HttpStatusCode.NoContent, asA.StatusCode);
    }

    [Fact]
    public async Task Another_owners_api_key_is_not_found_and_not_listed()
    {
        var ownerA = await _host.PortalClientAsync();
        var userB = await _host.SecondUserClientAsync();
        var applicationId = await TestHost.CreateApplicationAsync(ownerA, $"Owner A's keyed app {Guid.NewGuid():N}");

        var created = await ownerA.PostAsJsonAsync("/api/v1/keys", new { name = $"Owner A's key {Guid.NewGuid():N}", application_ids = new[] { applicationId } });
        created.EnsureSuccessStatusCode();
        var keyId = (await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["api_key_id"].GetString()!;

        var putR = await userB.PutAsJsonAsync($"/api/v1/keys/{keyId}/applications", new { application_ids = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.NotFound, putR.StatusCode);

        var deleteR = await userB.DeleteAsync($"/api/v1/keys/{keyId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteR.StatusCode);

        var listAsB = await userB.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/v1/keys");
        Assert.DoesNotContain(listAsB!, k => k["api_key_id"].GetString() == keyId);
    }

    /// <summary>
    /// The public render/validate surface (task 5) needs its own cross-tenant coverage: a portal token from user
    /// B, used against an application owned by A, must be rejected as 403 application_not_owned - and, since the
    /// access check runs before the template is ever looked up (see AccessCheck in Program.cs), B's response must
    /// carry no trace of A's template.
    /// </summary>
    [Fact]
    public async Task Render_and_validate_reject_another_owners_application_without_leaking_template_data()
    {
        var (_, userB, applicationId, templateId) = await SeedAsync();
        var body = new { application_id = applicationId, template_id = templateId, data = new { } };

        var renderResponse = await userB.PostAsJsonAsync("/api/v1/render", body);
        Assert.Equal(HttpStatusCode.Forbidden, renderResponse.StatusCode);
        var renderBody = await renderResponse.Content.ReadAsStringAsync();
        Assert.Contains("application_not_owned", renderBody);
        Assert.DoesNotContain(templateId, renderBody);
        Assert.DoesNotContain("owner-a-template", renderBody);

        var validateResponse = await userB.PostAsJsonAsync("/api/v1/validate", body);
        Assert.Equal(HttpStatusCode.Forbidden, validateResponse.StatusCode);
        var validateBody = await validateResponse.Content.ReadAsStringAsync();
        Assert.Contains("application_not_owned", validateBody);
        Assert.DoesNotContain(templateId, validateBody);
        Assert.DoesNotContain("owner-a-template", validateBody);
    }

    /// <summary>
    /// The demo grant-application render endpoint (task 6) is portal-only and owner-checked on the template, not
    /// on the (unowned, int-keyed) demo data itself. User B must not be able to render owner A's template against
    /// the demo grant application just because the demo data has no owner of its own.
    /// </summary>
    [Fact]
    public async Task Rendering_demo_grant_data_through_another_owners_template_is_not_found()
    {
        var (_, userB, _, templateId) = await SeedAsync();
        var r = await userB.PostAsync($"/api/v1/grant-applications/5001/render?templateId={templateId}&readOnly=false", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Assigning_a_key_to_another_owners_application_is_rejected()
    {
        var ownerA = await _host.PortalClientAsync();
        var userB = await _host.SecondUserClientAsync();
        var aAppId = await TestHost.CreateApplicationAsync(ownerA, $"Owner A's app {Guid.NewGuid():N}");
        var bAppId = await TestHost.CreateApplicationAsync(userB, $"Owner B's app {Guid.NewGuid():N}");

        var created = await userB.PostAsJsonAsync("/api/v1/keys", new { name = $"Owner B's key {Guid.NewGuid():N}", application_ids = new[] { bAppId } });
        created.EnsureSuccessStatusCode();
        var keyId = (await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["api_key_id"].GetString()!;

        // B owns this key, but tries to assign it to A's application - must be rejected, never silently succeed.
        var r = await userB.PutAsJsonAsync($"/api/v1/keys/{keyId}/applications", new { application_ids = new[] { aAppId } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("application_not_owned", await r.Content.ReadAsStringAsync());
    }
}
