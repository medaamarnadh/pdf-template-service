using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace PdfFill.Api.Tests;

public class RateLimitTests : IClassFixture<RateLimitTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("RateLimiting:RenderPerMinute", "3");
            b.UseSetting("RateLimiting:ApiPerMinute", "1000");
            b.UseSetting("Database:File", Path.Combine(Path.GetTempPath(), $"rl-{Guid.NewGuid():N}.db"));
            b.UseSetting("Database:RecreateOnStartup", "true");
            b.UseSetting("Auth:JwtKey", "unit-test-key-0123456789abcdef0123456789abcdef");
        }
    }
    private readonly Factory _f;
    public RateLimitTests(Factory f) => _f = f;

    private static async Task<string> CreateKeyAsync(HttpClient portal, string name, string applicationId)
    {
        var r = await portal.PostAsJsonAsync("/api/v1/keys", new { name, application_ids = new[] { applicationId } });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>())!["secret"].GetString()!;
    }

    [Fact]
    public async Task Render_policy_limits_per_key_and_returns_problem_body()
    {
        var portal = _f.CreateClient();
        portal.DefaultRequestHeaders.Authorization = new("Bearer", await TestAuth.LoginAsync(portal));
        var apps = await portal.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/v1/applications");
        var applicationId = apps![0]["application_id"].ToString()!;
        var templates = await portal.GetFromJsonAsync<List<Dictionary<string, object>>>($"/api/v1/templates?applicationId={applicationId}");
        var templateId = templates![0]["id"].ToString()!;

        // the rate-limit partition is the authenticated key's id (see RateLimiting.PartitionKey)
        var keyA = await CreateKeyAsync(portal, $"rate-a-{Guid.NewGuid():N}", applicationId);
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keyA);
        var body = new { application_id = applicationId, template_id = templateId, data = new { } };

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codes.Add((await client.PostAsJsonAsync("/api/v1/validate", body)).StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codes);

        var rejected = await client.PostAsJsonAsync("/api/v1/validate", body);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        var problem = await rejected.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("rate_limited", problem!["code"].ToString());

        // a different key is a different partition, even against the same application/template
        var keyB = await CreateKeyAsync(portal, $"rate-b-{Guid.NewGuid():N}", applicationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", keyB);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/validate", body)).StatusCode);
    }

    /// <summary>
    /// standards/security.md rule 13a: the limiter must run ahead of authentication, so a flood of requests
    /// carrying a bogus key is throttled the same as a flood of valid ones - not retried at 401 forever, which
    /// would be an unbounded, unthrottled online guessing channel against FindBySecretAsync.
    /// </summary>
    [Fact]
    public async Task Unauthenticated_flood_with_a_bad_key_is_rate_limited_not_endless_401()
    {
        var client = _f.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "pdfk_live_notARealKey00000000000000000000000000");
        var body = new { application_id = Guid.NewGuid().ToString(), template_id = Guid.NewGuid().ToString(), data = new { } };

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codes.Add((await client.PostAsJsonAsync("/api/v1/render", body)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, codes[^1]);
        Assert.All(codes.Take(3), c => Assert.Equal(HttpStatusCode.Unauthorized, c));
    }

    [Fact]
    public async Task Successful_responses_carry_ratelimit_headers()
    {
        var r = await _f.CreateClient().GetAsync("/openapi/public.json");
        Assert.True(r.Headers.Contains("RateLimit-Limit"));
    }
}
