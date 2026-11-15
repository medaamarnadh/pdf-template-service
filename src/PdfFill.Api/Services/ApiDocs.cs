using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PdfFill.Api.Services;

/// <summary>
/// Two OpenAPI documents:
///   /openapi/public.json   - ONLY the endpoints customers integrate with (render, validate, job status). This is what gets published.
///   /openapi/internal.json - everything (portal/management endpoints); served only when Docs:ExposeInternal = true.
/// An endpoint is public when it carries the "Public" tag. Anything else is stripped from the public document by a transformer.
/// </summary>
public static class ApiDocs
{
    public const string PublicTag = "Public";

    public static IServiceCollection AddApiDocs(this IServiceCollection services)
    {
        services.AddOpenApi("public", o =>
        {
            o.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo
                {
                    Title = "PDF Template Service — Render API",
                    Version = "v1",
                    Description = "Endpoints for rendering PDFs from a published template and validating payloads. " +
                                  "Authenticate with an API key: 'Authorization: Bearer pdfk_live_...' or 'X-Api-Key: pdfk_live_...'. " +
                                  "Send application_id and template_id in the request body. Management of templates, keys and " +
                                  "applications is done in the portal and is not part of this reference.",
                };
                // strip every operation that is not tagged Public; drop empty paths
                foreach (var (path, item) in doc.Paths.ToList())
                {
                    if (item.Operations is null) { doc.Paths.Remove(path); continue; }
                    foreach (var (method, op) in item.Operations.ToList())
                        if (op.Tags is null || !op.Tags.Any(t => t.Name == PublicTag)) item.Operations.Remove(method);
                    if (item.Operations.Count == 0) doc.Paths.Remove(path);
                }
                doc.Tags = new HashSet<OpenApiTag> { new() { Name = PublicTag, Description = "Render and validate" } };
                doc.Components ??= new OpenApiComponents();
                doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "pdfk_live_...", Description = "API key sent as 'Authorization: Bearer pdfk_live_...' (a portal JWT also works here, but customers should use a key)." };
                doc.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = ApiKeyDefaults.HeaderName, Description = "API key sent as 'X-Api-Key: pdfk_live_...'." };
                foreach (var op in doc.Paths.Values.SelectMany(p => p.Operations!.Values))
                    op.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] },
                                   new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", doc)] = [] }];
                return Task.CompletedTask;
            });
        });
        services.AddOpenApi("internal", o => o.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info = new OpenApiInfo { Title = "PDF Template Service — Internal API (portal)", Version = "v1" };
            return Task.CompletedTask;
        }));
        return services;
    }

    public static void MapApiDocs(this WebApplication app)
    {
        var exposeInternal = app.Configuration.GetValue("Docs:ExposeInternal", false);
        // /openapi/public.json is the published reference; /openapi/internal.json only when enabled (dev / portal).
        app.MapOpenApi("/openapi/{documentName}.json").AddEndpointFilter(async (ctx, next) =>
        {
            var name = ctx.HttpContext.GetRouteValue("documentName")?.ToString();
            if (name == "public" || (name == "internal" && exposeInternal)) return await next(ctx);
            return Results.NotFound();
        });
        // human-readable reference for the public doc (Scalar, single script from a CDN); disabled when internal docs are hidden in prod? No: this is the public one.
        app.MapGet("/docs", () => Results.Content("""
            <!doctype html><html><head><meta charset="utf-8"><title>PDF Template Service — Render API</title></head>
            <body><script id="api-reference" data-url="/openapi/public.json"></script>
            <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script></body></html>
            """, "text/html")).ExcludeFromDescription();
    }
}
