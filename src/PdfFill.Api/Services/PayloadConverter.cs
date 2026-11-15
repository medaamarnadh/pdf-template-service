using System.Text.Json;
using System.Text.RegularExpressions;

namespace PdfFill.Api.Services;

/// <summary>Turns a validated JSON payload into the plain dictionary the filler consumes (images -> byte[], tables -> row list).</summary>
public static partial class PayloadConverter
{
    public static Dictionary<string, object?> ToData(FieldMapping mapping, JsonElement payload)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in payload.EnumerateObject())
        {
            var kind = mapping.Entries.TryGetValue(prop.Name, out var e) ? e.Kind : FieldKind.Text;
            data[prop.Name] = Convert(prop.Value, kind);
        }
        return data;
    }

    private static object? Convert(JsonElement v, FieldKind kind) => v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
        JsonValueKind.String when kind == FieldKind.Image => DecodeImage(v.GetString()!),
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Array when kind == FieldKind.Table => v.EnumerateArray()
            .Select(row => row.EnumerateObject().ToDictionary(c => c.Name, c => Convert(c.Value, FieldKind.Text), StringComparer.OrdinalIgnoreCase))
            .ToList(),
        _ => v.GetRawText()
    };

    private static byte[]? DecodeImage(string s)
    {
        var m = DataUrl().Match(s);
        return m.Success ? System.Convert.FromBase64String(m.Groups["data"].Value) : null;
    }

    [GeneratedRegex(@"^data:image/(png|jpeg);base64,(?<data>.+)$", RegexOptions.Singleline)]
    private static partial Regex DataUrl();
}
