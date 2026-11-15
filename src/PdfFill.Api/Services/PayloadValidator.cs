using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;

namespace PdfFill.Api.Services;

public sealed record ValidationProblem(string Key, string Code, string Message, string Severity = "error");

/// <summary>
/// Validates a render payload against the template mapping BEFORE any PDF work happens.
/// Everything is collected (not fail-fast) so the caller gets one complete 400 body.
/// Codes: required, unknown_key, type, max_length, format, not_allowed, image_format, image_size,
///        image_decode, table_shape, table_rows, table_column.
/// </summary>
public sealed partial class PayloadValidator
{
    public IReadOnlyList<ValidationProblem> Validate(FieldMapping mapping, JsonElement payload, bool rejectUnknownKeys)
    {
        var problems = new List<ValidationProblem>();
        if (payload.ValueKind != JsonValueKind.Object)
            return [new("$", "type", "Payload must be a JSON object of key/value pairs.")];

        var present = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payload.EnumerateObject()) present[p.Name] = p.Value;

        foreach (var (key, e) in mapping.Entries)
        {
            if (!present.TryGetValue(key, out var v) || IsEmpty(v))
            {
                if (e.Required) problems.Add(new(key, "required", "Value is required."));
                continue;
            }
            switch (e.Kind)
            {
                case FieldKind.Text:     ValidateText(key, e, v, problems); break;
                case FieldKind.Checkbox: if (!IsBoolLike(v)) problems.Add(new(key, "type", "Expected true/false, 1/0 or \"Yes\"/\"No\".")); break;
                case FieldKind.Radio:
                case FieldKind.Choice:   ValidateChoice(key, e, v, problems); break;
                case FieldKind.Image:    ValidateImage(key, e, v, problems); break;
                case FieldKind.Table:    ValidateTable(key, e, v, mapping, problems); break;
            }
        }

        foreach (var key in present.Keys.Where(k => !mapping.Entries.ContainsKey(k)))
            problems.Add(rejectUnknownKeys
                ? new(key, "unknown_key", "Key is not defined in the template mapping.")
                : new(key, "unknown_key", "Key is not in the mapping and will be ignored.", "warning"));

        return problems;
    }

    // ---- rules --------------------------------------------------------------

    private static void ValidateText(string key, MappingEntry e, JsonElement v, List<ValidationProblem> p)
    {
        if (v.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
        { p.Add(new(key, "type", "Expected a string or number.")); return; }
        var s = v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText();
        if (e.MaxLength is int max && s.Length > max) p.Add(new(key, "max_length", $"{s.Length} characters; maximum is {max}."));
        if (e.Format is not null && e.Format.StartsWith("date", StringComparison.OrdinalIgnoreCase)
            && !DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && !DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            p.Add(new(key, "format", "Expected an ISO date (yyyy-MM-dd)."));
        if (e.Format is not null && (e.Format.StartsWith("number") || e.Format.StartsWith("currency")) && !double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            p.Add(new(key, "format", "Expected a numeric value."));
    }

    private static void ValidateChoice(string key, MappingEntry e, JsonElement v, List<ValidationProblem> p)
    {
        if (v.ValueKind != JsonValueKind.String) { p.Add(new(key, "type", "Expected a string.")); return; }
        if (e.Values.Length > 0 && !e.Values.Contains(v.GetString()!, StringComparer.OrdinalIgnoreCase))
            p.Add(new(key, "not_allowed", $"\"{v.GetString()}\" is not one of: {string.Join(", ", e.Values)}."));
    }

    private static void ValidateImage(string key, MappingEntry e, JsonElement v, List<ValidationProblem> p)
    {
        if (v.ValueKind != JsonValueKind.String) { p.Add(new(key, "type", "Expected a base64 data URL (data:image/png;base64,...).")); return; }
        var m = DataUrl().Match(v.GetString()!);
        if (!m.Success) { p.Add(new(key, "image_format", "Expected a data URL of type image/png or image/jpeg.")); return; }
        var type = m.Groups["type"].Value;
        if (!e.ImageTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) p.Add(new(key, "image_format", $"{type} is not allowed; accepted: {string.Join(", ", e.ImageTypes)}."));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(m.Groups["data"].Value); }
        catch { p.Add(new(key, "image_decode", "Base64 content is not valid.")); return; }
        if (e.MaxKb is int kb && bytes.Length > kb * 1024) p.Add(new(key, "image_size", $"{bytes.Length / 1024} KB; maximum is {kb} KB."));
        try { using var img = XImage.FromStream(new MemoryStream(bytes)); if (img.PixelWidth <= 0) throw new InvalidDataException(); }
        catch { p.Add(new(key, "image_decode", "Image data cannot be decoded and would not render.")); }
    }

    private static void ValidateTable(string key, MappingEntry e, JsonElement v, FieldMapping mapping, List<ValidationProblem> p)
    {
        if (v.ValueKind != JsonValueKind.Array) { p.Add(new(key, "table_shape", "Expected an array of row objects.")); return; }
        var rows = v.EnumerateArray().ToList();
        var capacity = e.Box is not null ? (int)Math.Floor((e.Box.Rect[3] - e.Box.Rect[1]) / e.RowHeight) - (e.Header ? 1 : 0) : e.Rows;
        var columns = e.Box is not null ? e.BoxColumns.ToHashSet(StringComparer.OrdinalIgnoreCase) : e.Columns.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (rows.Count > capacity) p.Add(new(key, "table_rows", $"{rows.Count} rows; the template has room for {capacity}."));
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].ValueKind != JsonValueKind.Object) { p.Add(new($"{key}[{i}]", "table_shape", "Row must be an object.")); continue; }
            foreach (var col in rows[i].EnumerateObject())
                if (!columns.Contains(col.Name))
                    p.Add(new($"{key}[{i}].{col.Name}", "table_column", $"Unknown column; accepted: {string.Join(", ", columns)}."));
                else if (col.Value.ValueKind == JsonValueKind.String && col.Value.GetString()!.Length > 500)
                    p.Add(new($"{key}[{i}].{col.Name}", "max_length", "Cell exceeds 500 characters."));
        }
    }

    // ---- helpers ------------------------------------------------------------
    private static bool IsEmpty(JsonElement v) => v.ValueKind == JsonValueKind.Null || (v.ValueKind == JsonValueKind.String && v.GetString() == "");
    private static bool IsBoolLike(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False
        || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is 0 or 1)
        || (v.ValueKind == JsonValueKind.String && Regex.IsMatch(v.GetString()!, "^(yes|no|true|false|on|off|1|0)$", RegexOptions.IgnoreCase));

    [GeneratedRegex(@"^data:image/(?<type>png|jpeg);base64,(?<data>[A-Za-z0-9+/=\s]+)$")]
    private static partial Regex DataUrl();
}
