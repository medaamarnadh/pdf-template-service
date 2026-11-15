using System.Text.Json;

namespace PdfFill.Api.Services;

public enum FieldKind { Text, Checkbox, Radio, Choice, Image, Table }

/// <summary>One mapping entry: a data key bound to one or more PDF fields, with validation rules.</summary>
public sealed class MappingEntry
{
    public required string Key { get; init; }
    public string[] FieldIds { get; init; } = [];
    public FieldKind Kind { get; init; }
    public bool Required { get; init; }
    public int? MaxLength { get; init; }
    public string? Format { get; init; }
    public string[] Values { get; init; } = [];          // radio / choice export values
    public string[] ImageTypes { get; init; } = ["png", "jpeg"];
    public int? MaxKb { get; init; }
    // tables built from repeating template fields
    public int Rows { get; init; }
    public Dictionary<string, string> Columns { get; init; } = new(); // column key -> field pattern with {n}
    // boxes drawn on flat PDFs (no form field): page + rect in PDF points [x1,y1,x2,y2]
    public DrawnBox? Box { get; init; }
    public string[] BoxColumns { get; init; } = [];   // drawn table column keys
    public double RowHeight { get; init; } = 18;
    public bool Header { get; init; } = true;
}

public sealed record DrawnBox(int Page, double[] Rect);

/// <summary>
/// Loads "&lt;template&gt;.mapping.json". Accepts the simple form { "key": "FieldId" } as well as the
/// typed contract { "fields": { "key": { "field": ..., "type": ..., "maxLength": ..., "required": ... } } }.
/// </summary>
public sealed class FieldMapping
{
    private readonly Dictionary<string, MappingEntry> _entries;
    private FieldMapping(Dictionary<string, MappingEntry> e) => _entries = e;

    public IReadOnlyDictionary<string, MappingEntry> Entries => _entries;
    public IEnumerable<string> AllFieldIds => _entries.Values.SelectMany(e =>
        e.Kind == FieldKind.Table
            ? Enumerable.Range(1, e.Rows).SelectMany(n => e.Columns.Values.Select(p => p.Replace("{n}", n.ToString())))
            : e.FieldIds);

    public static FieldMapping Load(string path) => LoadJson(File.ReadAllText(path));

    public static FieldMapping LoadJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var fields = root.TryGetProperty("fields", out var f) ? f : root;
        var map = new Dictionary<string, MappingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in fields.EnumerateObject())
            map[prop.Name] = Parse(prop.Name, prop.Value);
        return new FieldMapping(map);
    }

    private static MappingEntry Parse(string key, JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String) return new MappingEntry { Key = key, FieldIds = [v.GetString()!], Kind = FieldKind.Text, MaxLength = 1000 };
        if (v.ValueKind == JsonValueKind.Array) return new MappingEntry { Key = key, FieldIds = v.EnumerateArray().Select(x => x.GetString()!).ToArray(), Kind = FieldKind.Text, MaxLength = 1000 };
        if (v.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Mapping entry '{key}' must be a string, array or object.");

        var kind = v.TryGetProperty("type", out var t) ? Enum.Parse<FieldKind>(t.GetString()!, true) : FieldKind.Text;
        var ids = v.TryGetProperty("field", out var fi)
            ? fi.ValueKind == JsonValueKind.Array ? fi.EnumerateArray().Select(x => x.GetString()!).ToArray() : [fi.GetString()!]
            : [];
        return new MappingEntry
        {
            Key = key, FieldIds = ids, Kind = kind,
            Required = v.TryGetProperty("required", out var r) && r.GetBoolean(),
            MaxLength = v.TryGetProperty("maxLength", out var ml) ? ml.GetInt32() : kind == FieldKind.Text ? 1000 : null,
            Format = v.TryGetProperty("format", out var fm) ? fm.GetString() : null,
            Values = v.TryGetProperty("values", out var vals) ? vals.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            ImageTypes = v.TryGetProperty("imageTypes", out var it) ? it.EnumerateArray().Select(x => x.GetString()!).ToArray() : ["png", "jpeg"],
            MaxKb = v.TryGetProperty("maxKb", out var kb) ? kb.GetInt32() : null,
            Rows = v.TryGetProperty("rows", out var rows) ? rows.GetInt32() : 0,
            Columns = v.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Object ? cols.EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetString()!, StringComparer.OrdinalIgnoreCase) : new(),
            BoxColumns = v.TryGetProperty("columns", out var bc) && bc.ValueKind == JsonValueKind.Array ? bc.EnumerateArray().Select(x => x.GetString()!).ToArray() : [],
            Box = v.TryGetProperty("box", out var bx) ? new DrawnBox(bx.GetProperty("page").GetInt32(), bx.GetProperty("rect").EnumerateArray().Select(x => x.GetDouble()).ToArray()) : null,
            RowHeight = v.TryGetProperty("rowHeight", out var rh) ? rh.GetDouble() : 18,
            Header = !v.TryGetProperty("header", out var hd) || hd.GetBoolean(),
        };
    }
}
