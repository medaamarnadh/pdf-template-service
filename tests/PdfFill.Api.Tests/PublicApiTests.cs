using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PdfFill.Api.Tests;

public class PublicApiTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public PublicApiTests(TestHost host) => _host = host;

    private static byte[] BlankPdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static async Task<string> UploadTemplateAsync(HttpClient portal, string applicationId, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(BlankPdf());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"{name}.pdf");
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(applicationId), "applicationId");
        var r = await portal.PostAsync("/api/v1/templates", form);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["id"].GetString()!;
    }

    private static async Task<string> CreateKeyAsync(HttpClient portal, string name, params string[] applicationIds)
    {
        var r = await portal.PostAsJsonAsync("/api/v1/keys", new { name, application_ids = applicationIds });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["secret"].GetString()!;
    }

    private HttpClient KeyClient(string secret)
    {
        var c = _host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return c;
    }

    [Fact]
    public async Task Key_renders_a_template_in_an_assigned_application()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Renderer");
        var templateId = await UploadTemplateAsync(portal, appId, "blank");
        var secret = await CreateKeyAsync(portal, "render-key", appId);

        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = appId, template_id = templateId, data = new { } });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await r.Content.ReadAsByteArrayAsync(), 0, 4));
    }

    [Fact]
    public async Task Key_may_also_use_the_x_api_key_header()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "HeaderApp");
        var templateId = await UploadTemplateAsync(portal, appId, "hdr");
        var secret = await CreateKeyAsync(portal, "hdr-key", appId);

        var c = _host.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", secret);
        var r = await c.PostAsJsonAsync("/api/v1/render", new { application_id = appId, template_id = templateId, data = new { } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Key_not_assigned_to_the_application_is_forbidden()
    {
        var portal = await _host.PortalClientAsync();
        var allowed = await TestHost.CreateApplicationAsync(portal, "Allowed");
        var other = await TestHost.CreateApplicationAsync(portal, "Other");
        var templateId = await UploadTemplateAsync(portal, other, "elsewhere");
        var secret = await CreateKeyAsync(portal, "narrow-key", allowed);

        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = other, template_id = templateId, data = new { } });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("key_not_allocated", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Revoked_key_is_unauthorized()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Revoking");
        var templateId = await UploadTemplateAsync(portal, appId, "rev");
        var created = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "doomed", application_ids = new[] { appId } });
        var body = await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var secret = body!["secret"].GetString()!;
        await portal.DeleteAsync($"/api/v1/keys/{body["api_key_id"].GetString()}");

        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = appId, template_id = templateId, data = new { } });

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("key_revoked", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_secret_is_unauthorized()
    {
        var r = await KeyClient("pdfk_live_thisIsNotARealKey000000000000000000").PostAsJsonAsync("/api/v1/render",
            new { application_id = Guid.NewGuid().ToString(), template_id = Guid.NewGuid().ToString(), data = new { } });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Template_from_another_application_is_not_found()
    {
        var portal = await _host.PortalClientAsync();
        var a = await TestHost.CreateApplicationAsync(portal, "Home");
        var b = await TestHost.CreateApplicationAsync(portal, "Away");
        var templateInB = await UploadTemplateAsync(portal, b, "away-template");
        var secret = await CreateKeyAsync(portal, "both-key", a, b);

        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = a, template_id = templateInB, data = new { } });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("template_not_found", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_application_is_not_found()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Known");
        var templateId = await UploadTemplateAsync(portal, appId, "known");
        var secret = await CreateKeyAsync(portal, "known-key", appId);

        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = Guid.NewGuid().ToString(), template_id = templateId, data = new { } });

        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("application_not_found", await r.Content.ReadAsStringAsync());
    }

    /// <summary>An entry with a Box is drawn directly on the page and never touches the AcroForm, so it must
    /// keep working on a form-less (flat/scanned) PDF - PdfFormFiller's "no AcroForm" guard should only trip for
    /// an entry that actually names PDF fields.</summary>
    [Fact]
    public async Task Box_mapping_renders_on_a_form_less_pdf()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "BoxOnFlat");
        var templateId = await UploadTemplateAsync(portal, appId, "flat"); // BlankPdf() has no AcroForm

        var mapping = new { fields = new { note = new { type = "text", box = new { page = 1, rect = new[] { 10.0, 10.0, 200.0, 40.0 } } } } };
        var put = await portal.PutAsJsonAsync($"/api/v1/templates/{templateId}/mapping", mapping);
        put.EnsureSuccessStatusCode();

        var secret = await CreateKeyAsync(portal, "box-key", appId);
        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = appId, template_id = templateId, data = new { note = "hello" } });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await r.Content.ReadAsByteArrayAsync(), 0, 4));
    }

    [Fact]
    public async Task Portal_token_may_render_its_own_application()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "PortalRender");
        var templateId = await UploadTemplateAsync(portal, appId, "portal");

        var r = await portal.PostAsJsonAsync("/api/v1/render", new { application_id = appId, template_id = templateId, data = new { } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Key_cannot_reach_management_endpoints()
    {
        // The Portal policy is pinned to AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme), but
        // that pinning is defence in depth: the primary defence is that UseAuthentication() only ever runs the
        // *default* scheme (also JWT), so an `pdfk_` bearer value is handed to the JWT handler regardless of
        // policy config and fails to parse as a JWT either way. Removing the pinning alone would not turn this
        // test red - so it must exercise both credential forms the public API accepts against every management
        // endpoint, not just an Authorization: Bearer pdfk_ that the JWT handler was always going to reject.
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Fenced");
        var secret = await CreateKeyAsync(portal, "fenced-key", appId);

        string[] managementPaths = ["/api/v1/applications", "/api/v1/keys", $"/api/v1/templates?applicationId={appId}"];
        foreach (var path in managementPaths)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await KeyClient(secret).GetAsync(path)).StatusCode);

            var viaHeader = _host.CreateClient();
            viaHeader.DefaultRequestHeaders.Add("X-Api-Key", secret);
            Assert.Equal(HttpStatusCode.Unauthorized, (await viaHeader.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task Validate_reports_problems_without_rendering()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Validating");
        var templateId = await UploadTemplateAsync(portal, appId, "validating");
        var secret = await CreateKeyAsync(portal, "validate-key", appId);

        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/validate",
            new { application_id = appId, template_id = templateId, options = new { strict = true }, data = new { unmapped_key = "x" } });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.False(body!["valid"].GetBoolean());
        Assert.Contains("unknown_key", body["problems"].ToString());
    }

    /// <summary>standards/security.md rule 10: the body is size-checked before it is ever parsed as JSON.</summary>
    [Fact]
    public async Task Oversized_payload_is_rejected_before_parsing()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "BigPayload");
        var templateId = await UploadTemplateAsync(portal, appId, "big");
        var secret = await CreateKeyAsync(portal, "big-key", appId);

        var big = new string('a', 6 * 1024 * 1024); // 6 MB of filler, past the 5 MB cap
        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render",
            new { application_id = appId, template_id = templateId, data = new { blob = big } });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("payload_too_large", await r.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Routing resolves a trailing slash to the same RenderPdf endpoint, but a literal string comparison against
    /// "/api/v1/render" does not - so the size cap must be gated on the resolved endpoint (a PublicPayloadCapMarker
    /// in Program.cs), not the raw path, or this canonical-looking variant skips the cap entirely.
    /// </summary>
    [Fact]
    public async Task Oversized_payload_is_rejected_even_with_a_trailing_slash()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "BigPayloadSlash");
        var templateId = await UploadTemplateAsync(portal, appId, "big-slash");
        var secret = await CreateKeyAsync(portal, "big-slash-key", appId);

        var big = new string('a', 6 * 1024 * 1024); // 6 MB of filler, past the 5 MB cap
        var r = await KeyClient(secret).PostAsJsonAsync("/api/v1/render/",
            new { application_id = appId, template_id = templateId, data = new { blob = big } });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("payload_too_large", await r.Content.ReadAsStringAsync());
    }

    /// <summary>Amendment 1: every new error response carries the problem document shape, including ones the
    /// framework generates itself (a truncated body never reaches our own JSON-parsing code at all).</summary>
    [Fact]
    public async Task Malformed_json_body_is_a_problem_document()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "MalformedJson");
        var templateId = await UploadTemplateAsync(portal, appId, "malformed");
        var secret = await CreateKeyAsync(portal, "malformed-key", appId);

        var truncated = $$"""{"application_id":"{{appId}}","template_id":"{{templateId}}","data":{""";
        using var content = new StringContent(truncated, System.Text.Encoding.UTF8, "application/json");
        var r = await KeyClient(secret).PostAsync("/api/v1/render", content);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
    }
}
