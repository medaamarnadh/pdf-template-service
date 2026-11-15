using System.Text.Json;
using PdfFill.Api.Services;

namespace PdfFill.Api.Tests;

public class PayloadValidatorTests
{
    private static readonly string MappingPath = Path.Combine(AppContext.BaseDirectory, "grant_application.mapping.json");
    private static FieldMapping Mapping => FieldMapping.Load(MappingPath);
    private static IReadOnlyList<ValidationProblem> Run(string json, bool strict = false) =>
        new PayloadValidator().Validate(Mapping, JsonDocument.Parse(json).RootElement, strict);

    [Fact]
    public void Missing_required_keys_are_all_reported()
    {
        var p = Run("""{ "city": "Portland" }""");
        Assert.Contains(p, x => x.Key == "business_name" && x.Code == "required");
        Assert.Contains(p, x => x.Key == "sig_applicant" && x.Code == "required");
        Assert.Equal(8, p.Count(x => x.Code == "required"));
    }

    [Fact]
    public void Text_over_max_length_is_rejected()
    {
        var p = Run($$"""{ "business_name": "{{new string('x', 120)}}" }""");
        Assert.Contains(p, x => x.Key == "business_name" && x.Code == "max_length");
    }

    [Fact]
    public void Radio_value_must_be_an_export_value()
    {
        Assert.Contains(Run("""{ "project_type": "Maybe" }"""), x => x.Key == "project_type" && x.Code == "not_allowed");
        Assert.DoesNotContain(Run("""{ "project_type": "expansion" }"""), x => x.Key == "project_type");
    }

    [Fact]
    public void Undecodable_image_is_rejected()
    {
        var p = Run("""{ "sig_applicant": "data:image/png;base64,AAAA" }""");
        Assert.Contains(p, x => x.Key == "sig_applicant" && x.Code == "image_decode");
    }

    [Fact]
    public void Table_rows_and_columns_are_checked()
    {
        var rows = string.Join(",", Enumerable.Repeat("""{ "category": "a" }""", 9));
        var p = Run($$"""{ "budget": [{{rows}}], "source": [ { "bogus": 1 } ] }""");
        Assert.Contains(p, x => x.Key == "budget" && x.Code == "table_rows");
        Assert.Contains(p, x => x.Key == "source[0].bogus" && x.Code == "table_column");
    }

    [Fact]
    public void Unknown_keys_are_warnings_unless_strict()
    {
        Assert.Equal("warning", Run("""{ "extra": 1 }""").Single(x => x.Key == "extra").Severity);
        Assert.Equal("error",   Run("""{ "extra": 1 }""", strict: true).Single(x => x.Key == "extra").Severity);
    }
}
