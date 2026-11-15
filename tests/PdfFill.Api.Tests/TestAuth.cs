using System.Net.Http.Json;

namespace PdfFill.Api.Tests;

/// <summary>Logs in with the seeded owner and returns a JWT. Seeds are the same as in Data/seed-users.sql.</summary>
public static class TestAuth
{
    public const string Email = "demo@example.com";
    public const string Password = "ChangeMe-Seed-2026!";

    public static async Task<string> LoginAsync(HttpClient client)
    {
        var r = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();
        return body!["token"].GetString()!;
    }
}
