using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PdfFill.Api.Services;

namespace PdfFill.Api.Tests;

public class ApiKeyTests : IClassFixture<TestHost>
{
    private readonly TestHost _host;
    public ApiKeyTests(TestHost host) => _host = host;

    [Fact]
    public async Task Secret_is_returned_once_and_never_again()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Keyed");

        var created = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "CI", application_ids = new[] { appId } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var secret = body!["secret"].GetString()!;
        Assert.StartsWith("pdfk_live_", secret);
        var keyId = body["api_key_id"].GetString()!;

        var list = await portal.GetStringAsync("/api/v1/keys");
        Assert.DoesNotContain(secret, list);
        Assert.Contains(secret[..ApiKeyService.PrefixLength], list);                       // the prefix is shown, the secret is not
        Assert.Contains(keyId, list);
    }

    [Fact]
    public async Task Created_key_lists_its_applications()
    {
        var portal = await _host.PortalClientAsync();
        var a = await TestHost.CreateApplicationAsync(portal, "Alpha");
        var b = await TestHost.CreateApplicationAsync(portal, "Beta");

        var created = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "Both", application_ids = new[] { a, b } });
        var body = await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var apps = body!["applications"].EnumerateArray().Select(x => x.GetProperty("application_id").GetString()).ToList();
        Assert.Equal(2, apps.Count);
        Assert.Contains(a, apps);
        Assert.Contains(b, apps);
    }

    [Fact]
    public async Task Assigning_an_unknown_application_is_rejected()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "Bad", application_ids = new[] { Guid.NewGuid().ToString() } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("application_not_owned", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Assignments_can_be_replaced()
    {
        var portal = await _host.PortalClientAsync();
        var a = await TestHost.CreateApplicationAsync(portal, "First");
        var b = await TestHost.CreateApplicationAsync(portal, "Second");
        var created = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "Movable", application_ids = new[] { a } });
        var keyId = (await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["api_key_id"].GetString()!;

        var r = await portal.PutAsJsonAsync($"/api/v1/keys/{keyId}/applications", new { application_ids = new[] { b } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        var apps = body!["applications"].EnumerateArray().Select(x => x.GetProperty("application_id").GetString()).ToList();
        Assert.Equal([b], apps);
    }

    [Fact]
    public async Task Revoking_marks_the_key()
    {
        var portal = await _host.PortalClientAsync();
        var appId = await TestHost.CreateApplicationAsync(portal, "Doomed");
        var created = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "Temp", application_ids = new[] { appId } });
        var keyId = (await created.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["api_key_id"].GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await portal.DeleteAsync($"/api/v1/keys/{keyId}")).StatusCode);

        var list = await portal.GetFromJsonAsync<List<Dictionary<string, JsonElement>>>("/api/v1/keys");
        var row = list!.Single(k => k["api_key_id"].GetString() == keyId);
        Assert.NotEqual(JsonValueKind.Null, row["revoked_at"].ValueKind);
    }

    [Fact]
    public async Task Name_is_required()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "   ", application_ids = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.Equal("name_required", body!["code"].GetString());
        Assert.Equal(400, body["status"].GetInt32());
    }

    [Fact]
    public async Task Error_responses_are_problem_details()
    {
        var portal = await _host.PortalClientAsync();
        var r = await portal.PostAsJsonAsync("/api/v1/keys", new { name = "Bad", application_ids = new[] { Guid.NewGuid().ToString() } });
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.True(body!.ContainsKey("type"));
        Assert.True(body.ContainsKey("title"));
        Assert.True(body.ContainsKey("detail"));
        Assert.Equal("application_not_owned", body["code"].GetString());
    }
}
