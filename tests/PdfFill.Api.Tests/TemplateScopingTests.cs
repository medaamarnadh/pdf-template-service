using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PdfFill.Api.Tests;

public class TemplateScopingTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public TemplateScopingTests(TestHost host) => _host = host;

    /// <summary>A one-page PDF with no form fields - enough for upload and discovery.</summary>
    private static byte[] BlankPdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] pdf, string? applicationId, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", $"{name}.pdf");
        form.Add(new StringContent(name), "name");
        if (applicationId is not null) form.Add(new StringContent(applicationId), "applicationId");
        return form;
    }

    [Fact]
    public async Task Upload_requires_an_application()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.PostAsync("/api/v1/templates", Upload(BlankPdf(), null, "no-app"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("application_required", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Uploaded_template_gets_a_uuid_and_belongs_to_the_application()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Scoped");

        var r = await portal.PostAsync("/api/v1/templates", Upload(BlankPdf(), appId, "scoped"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.True(Guid.TryParse(body!["id"].GetString(), out _));
        Assert.Equal(appId, body["applicationId"].GetString());
    }

    [Fact]
    public async Task List_is_filtered_by_application()
    {
        var portal = await _host.PortalClientAsync();
        var a = await TestHost.CreateApplicationAsync(portal, "App A");
        var b = await TestHost.CreateApplicationAsync(portal, "App B");
        await portal.PostAsync("/api/v1/templates", Upload(BlankPdf(), a, "in-a"));

        var inA = await portal.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>($"/api/v1/templates?applicationId={a}");
        var inB = await portal.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>($"/api/v1/templates?applicationId={b}");
        Assert.Single(inA!);
        Assert.Empty(inB!);
    }

    [Fact]
    public async Task Application_holding_templates_cannot_be_deleted()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Occupied");
        await portal.PostAsync("/api/v1/templates", Upload(BlankPdf(), appId, "occupier"));

        var r = await portal.DeleteAsync($"/api/v1/applications/{appId}");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("application_not_empty", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_invalid_pdf_returns_422_with_safe_message()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Test App");

        // Bytes that look like a PDF header but are not a valid PDF
        var invalidPdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x00, 0x00, 0x00, 0x00 };
        var r = await portal.PostAsync("/api/v1/templates", Upload(invalidPdf, appId, "invalid"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();

        // Must have RFC 9457 code field
        Assert.Contains("\"code\":\"pdf_unreadable\"", body);

        // Must NOT contain parser-specific text
        Assert.DoesNotContain("PdfSharp", body);
        Assert.DoesNotContain("not a valid PDF document", body);
    }
}
