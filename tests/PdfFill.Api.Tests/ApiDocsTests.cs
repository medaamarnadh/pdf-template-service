using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PdfFill.Api.Tests;

/// <summary>Guards the rule: only render/validate endpoints are published; management endpoints never leak into the public reference.</summary>
public class ApiDocsTests
{
    private sealed class Factory(bool exposeInternal) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("Docs:ExposeInternal", exposeInternal.ToString());
            b.UseSetting("Database:File", Path.Combine(Path.GetTempPath(), $"docs-{Guid.NewGuid():N}.db"));
            b.UseSetting("Database:RecreateOnStartup", "true");
            b.UseSetting("Auth:JwtKey", "unit-test-key-0123456789abcdef0123456789abcdef");
        }
    }

    [Fact]
    public async Task Public_document_contains_only_render_and_validate()
    {
        using var f = new Factory(exposeInternal: false);
        var doc = JsonDocument.Parse(await f.CreateClient().GetStringAsync("/openapi/public.json"));
        var ops = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => $"{m.Name.ToUpper()} {p.Name}")).Order().ToList();
        Assert.Equal(["POST /api/v1/render", "POST /api/v1/validate"], ops);
        Assert.DoesNotContain(ops, o => o.Contains("/mapping") || o.Contains("/applications") || o.Contains("/keys") || o.StartsWith("GET"));
    }

    [Fact]
    public async Task Internal_document_is_hidden_unless_enabled()
    {
        using var hidden = new Factory(exposeInternal: false);
        Assert.Equal(HttpStatusCode.NotFound, (await hidden.CreateClient().GetAsync("/openapi/internal.json")).StatusCode);
        using var shown = new Factory(exposeInternal: true);
        Assert.Equal(HttpStatusCode.OK, (await shown.CreateClient().GetAsync("/openapi/internal.json")).StatusCode);
    }
}
