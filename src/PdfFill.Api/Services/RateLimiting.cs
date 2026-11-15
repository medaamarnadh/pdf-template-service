using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PdfFill.Api.Services;

/// <summary>
/// Rate limiting with the built-in ASP.NET Core limiter (no extra dependency).
///  - Partition key: a hash of the presented credential (X-Api-Key, or an Authorization bearer value - API key
///    secret or portal JWT alike), read straight off the request before authentication runs, otherwise the
///    client IP. Never the raw secret/token itself.
///  - "api"    : sliding window for every /api endpoint (default 300 requests / minute).
///  - "render" : tighter sliding window for render + validate (default 60 / minute) plus a concurrency cap,
///               because rendering is the expensive path.
/// Responses: 429 with RFC 9457 problem body and RateLimit-Limit / RateLimit-Remaining / Retry-After headers.
/// </summary>
public static class RateLimiting
{
    public const string ApiPolicy = "api";
    public const string RenderPolicy = "render";
    public const string AuthPolicy = "auth";

    public sealed class Options
    {
        public int ApiPerMinute { get; set; } = 300;
        public int RenderPerMinute { get; set; } = 60;
        public int AuthPerMinute { get; set; } = 10;      // login attempts per IP
        public int RenderConcurrency { get; set; } = 8;
        public int RenderQueue { get; set; } = 16;
        public bool Enabled { get; set; } = true;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration cfg)
    {
        var o = cfg.GetSection("RateLimiting").Get<Options>() ?? new Options();
        services.AddSingleton(o);
        services.AddRateLimiter(rl =>
        {
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rl.OnRejected = async (ctx, ct) =>
            {
                var retry = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? (int)Math.Ceiling(ra.TotalSeconds) : 60;
                ctx.HttpContext.Response.Headers.RetryAfter = retry.ToString();
                ctx.HttpContext.Response.ContentType = "application/problem+json";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://docs.pdftemplate.example/errors/rate_limited",
                    title = "Too many requests", status = 429, code = "rate_limited",
                    detail = $"Rate limit exceeded for this key or client. Retry after {retry} seconds.",
                    retry_after = retry, instance = ctx.HttpContext.Request.Path.Value
                }, ct);
            };

            rl.AddPolicy(ApiPolicy, http => o.Enabled
                ? RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(http), _ => new SlidingWindowRateLimiterOptions
                    { PermitLimit = o.ApiPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter("off"));

            rl.AddPolicy(RenderPolicy, http => o.Enabled
                ? RateLimitPartition.GetSlidingWindowLimiter("render:" + PartitionKey(http), _ => new SlidingWindowRateLimiterOptions
                    { PermitLimit = o.RenderPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter("off"));

            rl.AddPolicy(AuthPolicy, http => o.Enabled
                ? RateLimitPartition.GetSlidingWindowLimiter("auth:ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _ => new SlidingWindowRateLimiterOptions
                    { PermitLimit = o.AuthPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 })
                : RateLimitPartition.GetNoLimiter("off"));

            // Global concurrency cap on rendering across all tenants (protects CPU/memory), queued briefly rather than rejected.
            rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                o.Enabled && IsRenderPath(http.Request.Path)
                    ? RateLimitPartition.GetConcurrencyLimiter("render-global", _ => new ConcurrencyLimiterOptions
                        { PermitLimit = o.RenderConcurrency, QueueLimit = o.RenderQueue, QueueProcessingOrder = QueueProcessingOrder.OldestFirst })
                    : RateLimitPartition.GetNoLimiter("none"));
        });
        return services;
    }

    /// <summary>Adds informational RateLimit-* headers to successful responses (limit and policy; remaining is approximate).</summary>
    public static IApplicationBuilder UseRateLimitHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var o = ctx.RequestServices.GetRequiredService<Options>();
            var limit = IsRenderPath(ctx.Request.Path) ? o.RenderPerMinute : o.ApiPerMinute;
            ctx.Response.Headers["RateLimit-Limit"] = limit.ToString();
            ctx.Response.Headers["RateLimit-Policy"] = $"{limit};w=60";
            return Task.CompletedTask;
        });
        await next();
    });

    private static bool IsRenderPath(PathString p) =>
        p.Value is { } v && (v.EndsWith("/render", StringComparison.OrdinalIgnoreCase) || v.EndsWith("/validate", StringComparison.OrdinalIgnoreCase) || v.EndsWith("/renders", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The rate limiter runs before authentication (see Program.cs), by design: it must throttle unauthenticated
    /// and rejected requests too, not just ones that pass auth. So this reads the presented credential straight
    /// off the request the same way ApiKeyAuthenticationHandler does - X-Api-Key first, then an Authorization
    /// bearer value - rather than an authenticated claim, which would not exist yet at this point in the
    /// pipeline. Using the bearer value verbatim (not just ones that look like an API key) means a portal JWT
    /// gets its own partition too, instead of every session behind one IP sharing a single bucket.
    /// Hashed so the secret/token itself is never held as a partition key or, via the limiter's diagnostics, ever
    /// logged.
    /// </summary>
    private static string PartitionKey(HttpContext http)
    {
        string? presented = null;
        if (http.Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header) && header.ToString() is { Length: > 0 } direct)
            presented = direct;
        else
        {
            var auth = http.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && auth.Length > 7)
                presented = auth[7..];
        }

        if (presented is null) return "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presented)))[..16];
        return "key:" + hash;
    }
}
