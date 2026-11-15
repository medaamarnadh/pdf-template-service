using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PdfFill.Api.Services;

namespace PdfFill.Api.Tests;

public class AuthTests : IClassFixture<AuthTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseSetting("Database:File", Path.Combine(Path.GetTempPath(), $"auth-{Guid.NewGuid():N}.db"));
            b.UseSetting("Database:RecreateOnStartup", "true");
            b.UseSetting("Auth:JwtKey", "unit-test-key-0123456789abcdef0123456789abcdef");
            b.UseSetting("RateLimiting:AuthPerMinute", "100");
        }
    }
    private readonly Factory _f;
    public AuthTests(Factory f) => _f = f;

    [Fact]
    public void Password_hash_verifies_and_rejects()
    {
        var h = PasswordHasher.Hash("correct horse battery staple");
        Assert.StartsWith("pbkdf2$", h);
        Assert.True(PasswordHasher.Verify("correct horse battery staple", h));
        Assert.False(PasswordHasher.Verify("wrong", h));
    }

    [Fact]
    public async Task Management_and_render_endpoints_require_a_token()
    {
        var c = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/templates")).StatusCode);
        var body = await (await c.GetAsync("/api/v1/templates")).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("unauthorized", body!["code"].ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/render",
            new { application_id = "any", template_id = "any", data = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/openapi/public.json")).StatusCode);   // docs stay public
    }

    [Fact]
    public async Task Seeded_owner_can_login_and_wrong_password_is_rejected()
    {
        var c = _f.CreateClient();
        var bad = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = TestAuth.Email, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var token = await TestAuth.LoginAsync(c);
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var me = await c.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/auth/me");
        Assert.Equal(TestAuth.Email, me!["email"].GetString());
        Assert.Equal("owner", me["role"].GetString());
        Assert.True(me["must_change_password"].GetBoolean());
        var apps = await c.GetFromJsonAsync<List<Dictionary<string, System.Text.Json.JsonElement>>>("/api/v1/applications");
        var applicationId = apps![0]["application_id"].GetString();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/templates?applicationId={applicationId}")).StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var c = _f.CreateClient();
        var token = await TestAuth.LoginAsync(c);
        c.DefaultRequestHeaders.Authorization = new("Bearer", token[..^4] + "AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Weak_new_password_is_rejected()
    {
        var c = _f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new("Bearer", await TestAuth.LoginAsync(c));
        var r = await c.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = TestAuth.Password, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
