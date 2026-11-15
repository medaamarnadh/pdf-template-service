using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;

namespace PdfFill.Api.Services;

public sealed record FillResult(byte[] Pdf, IReadOnlyList<string> Filled, IReadOnlyList<string> MissingInPdf, IReadOnlyList<string> MissingInData);

/// <summary>
/// Fills an AcroForm template with data using PDFsharp (MIT).
/// Field type is taken from the PDF itself, so the mapping JSON stays plain key -> field id.
///   text field     : any scalar (dates/numbers formatted), or byte[] => image stamped over the field (signatures)
///   check box      : bool / 0-1 / "yes"/"true"/"on"
///   radio group    : string equal to one of the export values (e.g. "Yes"/"No")
/// </summary>
public sealed class PdfFormFiller
{
    public PdfFormFiller(IConfiguration cfg, IWebHostEnvironment env)
    {
        // PDFsharp needs a font resolver on Linux/containers; we bundle one TTF so output is identical everywhere.
        GlobalFontSettings.FontResolver ??= new SingleFontResolver(Path.Combine(env.ContentRootPath, cfg["Pdf:FontFile"] ?? "Fonts/DejaVuSans.ttf"));
    }

    public FillResult Fill(string templatePath, FieldMapping mapping, IReadOnlyDictionary<string, object?> data, bool readOnly = false)
    {
        using var doc = PdfReader.Open(templatePath, PdfDocumentOpenMode.Modify);
        // doc.AcroForm throws (rather than returning null) when the document has no form dictionary at all, so a
        // template with nothing mapped yet (freshly uploaded, mapping_json = {"fields":{}}) must be checked via
        // the raw catalog first - only a mapping that actually names form fields requires one to write into.
        var hasForm = doc.Internals.Catalog.Elements.ContainsKey("/AcroForm");
        // An entry with a Box draws directly on the page and never touches the form, so it works on a form-less
        // (flat/scanned) PDF; only an entry that actually names PDF fields requires an AcroForm to write into.
        if (!hasForm && mapping.Entries.Values.Any(e => e.Box is null)) throw new InvalidOperationException("Template has no AcroForm.");
        var form = hasForm ? doc.AcroForm : null;
        form?.Elements.SetBoolean("/NeedAppearances", true);   // viewers regenerate field appearances

        var filled = new List<string>();
        var missingInPdf = new List<string>();
        var missingInData = new List<string>();

        foreach (var (key, entry) in mapping.Entries)
        {
            if (!data.TryGetValue(key, out var value)) { missingInData.Add(key); continue; }

            if (entry.Box is not null)                  // box drawn on a flat PDF: no form field, we draw the content
            {
                DrawBox(doc, entry, value); filled.Add(key); continue;
            }

            if (entry.Kind == FieldKind.Table)
            {
                var rows = value as List<Dictionary<string, object?>> ?? [];
                for (var n = 1; n <= entry.Rows; n++)
                {
                    var row = n <= rows.Count ? rows[n - 1] : null;                  // rows without data are blanked
                    foreach (var (col, pattern) in entry.Columns)
                    {
                        var id = pattern.Replace("{n}", n.ToString());
                        var cell = row is not null && row.TryGetValue(col, out var cv) ? cv : null;
                        // form is non-null here: the guard above throws before this loop when entries exist but the form does not.
                        if (Apply(form!, doc, id, cell, filled)) continue;
                        missingInPdf.Add(id);
                    }
                }
                continue;
            }

            foreach (var fieldId in entry.FieldIds)
                if (!Apply(form!, doc, fieldId, value, filled)) missingInPdf.Add(fieldId);
        }

        if (readOnly && form is not null)
            foreach (var name in form.Fields.Names)
                form.Fields[name]!.ReadOnly = true;

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return new FillResult(ms.ToArray(), filled, missingInPdf, missingInData);
    }

    /// <summary>Lists the fields of a template with their PDF type (for validation endpoints).</summary>
    public IEnumerable<(string Name, string Type)> ListFields(string templatePath)
    {
        using var doc = PdfReader.Open(templatePath, PdfDocumentOpenMode.Import);
        var form = doc.AcroForm;
        foreach (var name in form.Fields.Names)
            yield return (name, form.Fields[name]!.GetType().Name.Replace("Pdf", "").Replace("Field", "").ToLowerInvariant());
    }

    private static readonly XFont BoxFont = new("Any", 9);
    private static readonly XFont BoxFontBold = new("Any", 9, XFontStyleEx.Bold);

    /// <summary>Draws a value into a drawn box: text, an X for checkboxes, an image, or a grid for tables.</summary>
    private static void DrawBox(PdfDocument doc, MappingEntry e, object? value)
    {
        var page = doc.Pages[e.Box!.Page - 1];
        var r = e.Box.Rect; double x1 = r[0], y1 = r[1], x2 = r[2], y2 = r[3];
        double top = page.Height.Point - y2, h = y2 - y1, w = x2 - x1;       // PDF -> XGraphics (top-left origin)
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        switch (e.Kind)
        {
            case FieldKind.Image when value is byte[] bytes:
                using (var img = XImage.FromStream(new MemoryStream(bytes)))
                {
                    var s = Math.Min((w - 4) / img.PointWidth, (h - 4) / img.PointHeight);
                    gfx.DrawImage(img, x1 + 2, top + (h - img.PointHeight * s) / 2, img.PointWidth * s, img.PointHeight * s);
                }
                break;
            case FieldKind.Checkbox:
                if (ToBool(value)) gfx.DrawString("X", BoxFontBold, XBrushes.Black, new XRect(x1, top, w, h), XStringFormats.Center);
                break;
            case FieldKind.Table:
                var rows = value as List<Dictionary<string, object?>> ?? [];
                var cols = e.BoxColumns; if (cols.Length == 0) break;
                double cw = w / cols.Length, rh = e.RowHeight, y = top; var pen = new XPen(XColors.Gray, 0.5);
                var all = new List<(IReadOnlyDictionary<string, object?> row, bool header)>();
                if (e.Header) all.Add((cols.ToDictionary(c => c, c => (object?)c), true));
                all.AddRange(rows.Select(rw => ((IReadOnlyDictionary<string, object?>)rw, false)));
                gfx.DrawLine(pen, x1, y, x2, y);
                foreach (var (row, header) in all)
                {
                    for (var i = 0; i < cols.Length; i++)
                    {
                        var cell = row.TryGetValue(cols[i], out var cv) ? FormatScalar(cv) : "";
                        gfx.DrawString(cell, header ? BoxFontBold : BoxFont, XBrushes.Black, new XRect(x1 + i * cw + 3, y, cw - 6, rh), XStringFormats.CenterLeft);
                    }
                    y += rh; gfx.DrawLine(pen, x1, y, x2, y);
                }
                for (var i = 0; i <= cols.Length; i++) gfx.DrawLine(pen, x1 + i * cw, top, x1 + i * cw, y);
                break;
            default:
                var tf = new XTextFormatter(gfx);
                tf.DrawString(FormatScalar(value), BoxFont, XBrushes.Black, new XRect(x1 + 2, top + 2, w - 4, h - 4), XStringFormats.TopLeft);
                break;
        }
    }

    /// <summary>Sets one PDF field from a value; the PDF's own field type decides how. Returns false if the field is absent.</summary>
    private static bool Apply(PdfAcroForm form, PdfDocument doc, string fieldId, object? value, List<string> filled)
    {
        var field = form.Fields[fieldId];
        if (field is null) return false;
        switch (field)
        {
            case PdfTextField tf when value is byte[] png:
                StampImage(doc, fieldId, png); HideField(tf); break;
            case PdfTextField tf:
                tf.Value = new PdfString(FormatScalar(value), PdfStringEncoding.Unicode); break;
            case PdfCheckBoxField cb:
                cb.Checked = ToBool(value); break;
            case PdfRadioButtonField rb:
                SelectRadio(rb, FormatScalar(value)); break;
            default:
                field.Elements.SetString("/V", FormatScalar(value)); break;
        }
        filled.Add(fieldId);
        return true;
    }

    // ---- helpers -------------------------------------------------------------

    private static string FormatScalar(object? v) => v switch
    {
        null => "",
        bool b => b ? "Yes" : "No",
        DateTime d => d.ToString("MM/dd/yyyy"),
        string s when DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) => d.ToString("MM/dd/yyyy"),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    private static bool ToBool(object? v) => v switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        string s => s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("on", StringComparison.OrdinalIgnoreCase),
        _ => Convert.ToBoolean(v)
    };

    /// <summary>Select a radio button by export value; sets /V on the group and /AS on every kid widget.</summary>
    private static void SelectRadio(PdfRadioButtonField group, string exportValue)
    {
        var kids = group.Elements.GetArray("/Kids");
        var selected = "/Off";
        if (kids is not null)
        {
            for (int i = 0; i < kids.Elements.Count; i++)
            {
                var kid = kids.Elements.GetDictionary(i)!;
                var states = kid.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N")?.Elements.Keys
                             ?? Enumerable.Empty<string>();
                var match = states.FirstOrDefault(s => s != "/Off" && s.TrimStart('/').Equals(exportValue, StringComparison.OrdinalIgnoreCase));
                kid.Elements.SetName("/AS", match ?? "/Off");
                if (match is not null) selected = match;
            }
        }
        group.Elements.SetName("/V", selected);
    }

    /// <summary>Draws a PNG over every widget of the given field (page found by widget /T).</summary>
    private static void StampImage(PdfDocument doc, string fieldId, byte[] png)
    {
        using var img = XImage.FromStream(new MemoryStream(png));
        foreach (var page in doc.Pages)
        {
            for (int i = 0; i < page.Annotations.Count; i++)
            {
                var a = page.Annotations[i];
                if (a.Elements.GetString("/T") != fieldId) continue;
                var r = a.Elements.GetRectangle("/Rect");
                // keep aspect ratio, fit inside the placeholder with 2pt padding
                double pad = 2, bw = r.Width - 2 * pad, bh = r.Height - 2 * pad;
                double scale = Math.Min(bw / img.PointWidth, bh / img.PointHeight);
                double w = img.PointWidth * scale, h = img.PointHeight * scale;
                double x = r.X1 + pad, yTop = page.Height.Point - r.Y2 + pad + (bh - h) / 2;   // PDF -> XGraphics coords
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                gfx.DrawImage(img, x, yTop, w, h);
            }
        }
    }

    private static void HideField(PdfAcroField field)
    {
        field.ReadOnly = true;
        field.Elements.SetInteger("/F", 2);   // widget flag: hidden
    }

    private sealed class SingleFontResolver(string ttfPath) : IFontResolver
    {
        private readonly byte[] _bytes = File.ReadAllBytes(ttfPath);
        public byte[] GetFont(string faceName) => _bytes;
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("poc-font");
    }
}
