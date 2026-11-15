using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfFill.Api.Models;

/// <summary>Options for a render call. Both default to false, matching the previous query parameters.</summary>
public sealed record RenderOptions(
    [property: JsonPropertyName("strict")] bool Strict = false,
    [property: JsonPropertyName("read_only")] bool ReadOnly = false);

/// <summary>
/// The public render/validate body. The ids identify what to render; `data` holds the field values, kept in their
/// own object so a mapping key can never collide with application_id or template_id.
/// </summary>
public sealed record RenderRequest(
    [property: JsonPropertyName("application_id")] string ApplicationId,
    [property: JsonPropertyName("template_id")] string TemplateId,
    [property: JsonPropertyName("options")] RenderOptions? Options,
    [property: JsonPropertyName("data")] JsonElement Data);
