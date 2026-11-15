using PdfFill.Api.Services;
using PdfSharp.Pdf;

namespace PdfFill.Api.Tests;

public class FieldDiscoveryTests
{
    /// <summary>A perfectly valid PDF that simply has no AcroForm - what most uploads are.</summary>
    private static byte[] PdfWithoutAcroForm(int pages)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++) doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    [Fact]
    public void Pdf_without_a_form_is_read_as_zero_fields()
    {
        var (pages, fields) = FieldDiscovery.Discover(PdfWithoutAcroForm(2));
        Assert.Equal(2, pages);
        Assert.Empty(fields);
    }
}
