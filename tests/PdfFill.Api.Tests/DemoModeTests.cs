using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PdfFill.Api.Tests;

/// <summary>
/// A public demo shares a single seeded account, so that account has to survive contact with visitors:
/// no forced rotation to walk into, and no way for one visitor to change the password out from under the next.
/// </summary>
public class DemoModeTests
{
    private static async Task<JsonElement> LoginUserAsync(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = TestAuth.Email, password = TestAuth.Password });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        return body!["user"];
    }

    [Fact]
    public async Task Demo_instance_does_not_force_a_password_change()
    {
        using var host = new TestHost { Demo = true };
        var user = await LoginUserAsync(host.CreateClient());
        Assert.False(user.GetProperty("must_change_password").GetBoolean());
    }

    [Fact]
    public async Task Demo_instance_refuses_a_password_change_so_one_visitor_cannot_lock_out_the_rest()
    {
        using var host = new TestHost { Demo = true };
        var c = await host.PortalClientAsync();

        var r = await c.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = TestAuth.Password, newPassword = "a-valid-replacement-password" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.Equal("demo_disabled", body!["code"].GetString());

        // the shared password still works for the next visitor
        await LoginUserAsync(host.CreateClient());
    }

    [Fact]
    public async Task Without_demo_mode_the_seeded_account_still_forces_a_rotation()
    {
        using var host = new TestHost();
        var user = await LoginUserAsync(host.CreateClient());
        Assert.True(user.GetProperty("must_change_password").GetBoolean());
    }
}
