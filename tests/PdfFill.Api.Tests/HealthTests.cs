using System.Net;
using System.Text.Json;

namespace PdfFill.Api.Tests;

/// <summary>Guards the container healthcheck: GET /health must work with no credential (the docker-compose
/// healthcheck curls it anonymously) and must never appear in the published API reference.</summary>
public class HealthTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public HealthTests(TestHost host) => _host = host;

    [Fact]
    public async Task Health_reports_ok_and_schema_version_without_a_credential()
    {
        var r = await _host.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("healthy", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("schema_version").GetInt64() >= 0);
    }

    [Fact]
    public async Task Health_is_absent_from_the_published_public_document()
    {
        var doc = JsonDocument.Parse(await _host.CreateClient().GetStringAsync("/openapi/public.json"));
        var ops = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => $"{m.Name.ToUpper()} {p.Name}")).ToList();
        Assert.DoesNotContain(ops, o => o.Contains("/health"));
    }
}
