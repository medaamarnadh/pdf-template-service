using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PdfFill.Api.Data;

namespace PdfFill.Api.Services;

public sealed record User(string Id, string Email, string PasswordHash, string Role, string CreatedAt, string? LastLoginAt, long MustChangePassword);
public sealed record LoginRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Email + password login issuing a signed JWT (HS256). Users live in the SQLite users table.</summary>
public sealed class AuthService(Database db, IConfiguration cfg)
{
    public static readonly string[] Roles = ["owner", "admin", "developer", "viewer"];
    private readonly string _issuer = cfg["Auth:Issuer"] ?? "pdf-template-service";
    private readonly int _hours = cfg.GetValue("Auth:TokenHours", 12);

    private byte[] Key()
    {
        var key = cfg["Auth:JwtKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("Auth:JwtKey must be set to a random string of at least 32 characters (env Auth__JwtKey).");
        return Encoding.UTF8.GetBytes(key);
    }

    public async Task<User?> FindByEmailAsync(string email)
    {
        using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<User>(
            "SELECT user_id AS Id, email, password_hash AS PasswordHash, role, created_at AS CreatedAt, last_login_at AS LastLoginAt, must_change_password AS MustChangePassword FROM users WHERE lower(email) = lower(@email)", new { email });
    }

    public async Task<(string token, DateTime expires, User user)?> LoginAsync(string email, string password)
    {
        var user = await FindByEmailAsync(email.Trim());
        // always run the hash to keep timing similar for unknown users
        var ok = user is not null ? PasswordHasher.Verify(password, user.PasswordHash) : PasswordHasher.Verify(password, DummyHash);
        if (user is null || !ok) return null;
        using var c = db.Open();
        await c.ExecuteAsync("UPDATE users SET last_login_at = @now WHERE user_id = @id", new { now = DateTime.UtcNow.ToString("o"), id = user.Id });
        var expires = DateTime.UtcNow.AddHours(_hours);
        var jwt = new JwtSecurityToken(_issuer, _issuer,
            [new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")), new Claim(JwtRegisteredClaimNames.Sub, user.Id), new Claim(JwtRegisteredClaimNames.Email, user.Email), new Claim(ClaimTypes.Role, user.Role), new Claim("pwd_change", user.MustChangePassword == 1 ? "1" : "0")],
            expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Key()), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires, user);
    }

    public async Task<bool> ChangePasswordAsync(string userId, string current, string next)
    {
        using var c = db.Open();
        var hash = await c.QuerySingleOrDefaultAsync<string>("SELECT password_hash FROM users WHERE user_id = @id", new { id = userId });
        if (hash is null || !PasswordHasher.Verify(current, hash)) return false;
        if (next.Length < 12) throw new ArgumentException("New password must be at least 12 characters.");
        await c.ExecuteAsync("UPDATE users SET password_hash = @h, must_change_password = 0 WHERE user_id = @id", new { h = PasswordHasher.Hash(next), id = userId });
        return true;
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = _issuer, ValidAudience = _issuer, IssuerSigningKey = new SymmetricSecurityKey(Key()),
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
    };

    private static readonly string DummyHash = PasswordHasher.Hash("dummy-password-for-timing");
}

public static class AuthSetup
{
    public static IServiceCollection AddPortalAuth(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddSingleton<AuthService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                var tmp = new AuthService(new Database(cfg, new DummyEnv()), cfg);
                o.TokenValidationParameters = tmp.ValidationParameters();
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = ctx =>
                    {
                        // The PublicApi policy accepts both the ApiKey and JWT schemes, and ASP.NET Core challenges
                        // every scheme in a policy's list in turn. If the ApiKey handler already wrote the 401 body
                        // (it runs first), this handler must not try to set headers on an already-started response.
                        if (ctx.Response.HasStarted) { ctx.HandleResponse(); return Task.CompletedTask; }
                        ctx.HandleResponse();
                        ctx.Response.StatusCode = 401; ctx.Response.ContentType = "application/problem+json";
                        return ctx.Response.WriteAsJsonAsync(new { type = "https://docs.pdftemplate.example/errors/unauthorized", title = "Unauthorized", status = 401, code = "unauthorized", detail = "A valid bearer token is required. POST /api/v1/auth/login to obtain one." });
                    }
                };
            })
            // Public endpoints (render/validate) accept this scheme too; keys never satisfy the Portal/Admin
            // policies below because those are pinned to the JWT scheme only.
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });

        services.AddAuthorization(o =>
        {
            o.AddPolicy("Portal", p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser());
            o.AddPolicy("Admin", p => p.RequireRole("owner", "admin"));
            // Public endpoints take either credential; the endpoint decides what each may reach.
            o.AddPolicy("PublicApi", p => p.AddAuthenticationSchemes(ApiKeyDefaults.Scheme, JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser());
        });
        return services;
    }

    private sealed class DummyEnv : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory; public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PdfFill.Api"; public string WebRootPath { get; set; } = ""; 
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
