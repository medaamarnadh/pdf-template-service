using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PdfFill.Api.Tests;

public class ApplicationsTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public ApplicationsTests(TestHost host) => _host = host;

    [Fact]
    public async Task Create_returns_a_uuid_and_appears_in_the_list()
    {
        var portal = await _host.PortalClientAsync();

        var created = await portal.PostAsJsonAsync("/api/v1/applications", new { name = "Payroll" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var id = body!["application_id"].GetString()!;
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal("Payroll", body["name"].GetString());

        var list = await portal.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/v1/applications");
        Assert.Contains(list!, a => a["application_id"].GetString() == id && a["template_count"].GetInt32() == 0);
    }

    [Fact]
    public async Task Create_rejects_an_empty_name()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.PostAsJsonAsync("/api/v1/applications", new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("name_required", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rename_changes_the_name()
    {
        var portal = await _host.PortalClientAsync();
        var id = await TestHost.CreateApplicationAsync(portal, "Before");

        var r = await portal.PutAsJsonAsync($"/api/v1/applications/{id}", new { name = "After" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var list = await portal.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/v1/applications");
        Assert.Contains(list!, a => a["application_id"].GetString() == id && a["name"].GetString() == "After");
    }

    [Fact]
    public async Task Delete_removes_an_empty_application()
    {
        var portal = await _host.PortalClientAsync();
        var id = await TestHost.CreateApplicationAsync(portal, "Temporary");

        Assert.Equal(HttpStatusCode.NoContent, (await portal.DeleteAsync($"/api/v1/applications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.DeleteAsync($"/api/v1/applications/{id}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_application_is_not_found()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.PutAsJsonAsync($"/api/v1/applications/{Guid.NewGuid()}", new { name = "Nope" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Applications_require_a_token()
    {
        var anonymous = _host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/applications")).StatusCode);
    }
}
