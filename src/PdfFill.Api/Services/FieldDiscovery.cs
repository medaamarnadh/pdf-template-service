using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace PdfFill.Api.Services;

public sealed record DiscoveredField(string Id, string Type, int Page, double[] Rect, string Tooltip, string[] Options, int? MaxLength, bool Multiline, int WidgetCount);

/// <summary>Reads the AcroForm fields of a template: id, type, first widget page/rect, tooltip, export values.</summary>
public static class FieldDiscovery
{
    public static (int Pages, List<DiscoveredField> Fields) Discover(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var byName = new Dictionary<string, (int page, double[] rect, int count)>();
        for (var p = 0; p < doc.Pages.Count; p++)
        {
            var page = doc.Pages[p];
            for (var i = 0; i < page.Annotations.Count; i++)
            {
                var a = page.Annotations[i];
                if (a.Elements.GetString("/Subtype") != "/Widget") continue;
                var name = a.Elements.GetString("/T");
                if (string.IsNullOrEmpty(name)) name = a.Elements.GetDictionary("/Parent")?.Elements.GetString("/T") ?? "";
                if (name == "") continue;
                var r = a.Elements.GetRectangle("/Rect");
                if (byName.TryGetValue(name, out var cur)) byName[name] = (cur.page, cur.rect, cur.count + 1);
                else byName[name] = (p + 1, [r.X1, r.Y1, r.X2, r.Y2], 1);
            }
        }
        var fields = new List<DiscoveredField>();
        // PDFsharp's AcroForm getter throws rather than returning null when the catalog has no /AcroForm,
        // so ask the catalog first: a PDF with no form is readable, it simply has no fields to map.
        var form = doc.Internals.Catalog.Elements.ContainsKey("/AcroForm") ? doc.AcroForm : null;
        if (form is not null)
            foreach (var name in form.Fields.Names)
            {
                var f = form.Fields[name]!;
                var (type, options) = f switch
                {
                    PdfTextField => ("text", Array.Empty<string>()),
                    PdfCheckBoxField cb => ("checkbox", Array.Empty<string>()),
                    PdfRadioButtonField rb => ("radio", RadioValues(rb)),
                    PdfComboBoxField or PdfListBoxField => ("choice", Array.Empty<string>()),
                    _ => ("text", Array.Empty<string>())
                };
                var flags = f.Elements.GetInteger("/Ff");
                var maxLen = f.Elements.GetInteger("/MaxLen");
                (int page, double[] rect, int count) loc = byName.TryGetValue(name, out var l) ? l : (1, new double[4], 1);
                fields.Add(new DiscoveredField(name, type, loc.page, loc.rect, f.Elements.GetString("/TU") ?? "", options, maxLen > 0 ? maxLen : null, (flags & 4096) != 0, loc.count));
            }
        return (doc.Pages.Count, fields.OrderBy(f => f.Page).ThenBy(f => f.Id).ToList());
    }

    private static string[] RadioValues(PdfRadioButtonField rb)
    {
        var kids = rb.Elements.GetArray("/Kids"); if (kids is null) return [];
        var vals = new List<string>();
        for (var i = 0; i < kids.Elements.Count; i++)
        {
            var n = kids.Elements.GetDictionary(i)?.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
            if (n is null) continue;
            foreach (var k in n.Elements.Keys) if (k != "/Off" && !vals.Contains(k.TrimStart('/'))) vals.Add(k.TrimStart('/'));
        }
        return vals.ToArray();
    }
}
