using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Core.Tests;

public class BibliographyImportTests
{
    [Fact]
    public void RisRetainsAuthorsAbstractKeywordsAndDurableIdentifier()
    {
        const string ris = """
            TY  - JOUR
            AU  - Smith, Jane
            AU  - Jones, Alex
            TI  - Rhesus and the Problem of Attribution
            JO  - Classical Quarterly
            PY  - 2024/01/01
            VL  - 74
            IS  - 2
            SP  - 100
            EP  - 119
            DO  - https://doi.org/10.1234/TEST.1
            AB  - First line
                  continued abstract
            KW  - stylometry
            KW  - tragedy
            ER  -
            """;

        var record = Assert.Single(BibliographyImport.Parse(ris, "sources.ris"));

        Assert.Equal("Rhesus and the Problem of Attribution", record.Title);
        Assert.Equal(new[] { "Smith, Jane", "Jones, Alex" }, record.Authors);
        Assert.Equal("2024", record.Year);
        Assert.Equal("100-119", record.Pages);
        Assert.Equal("First line continued abstract", record.Abstract);
        Assert.Equal("https://doi.org/10.1234/test.1", record.StableIdentifier);
        Assert.Contains("Classical Quarterly", record.FormatCitation());
    }

    [Fact]
    public void BibTeXHandlesNestedBracesQuotedFieldsAndMultipleEntries()
    {
        const string bib = """
            @article{smith2024,
              author = {Smith, Jane and Jones, Alex},
              title = {The {Rhesus} Question Reconsidered},
              journal = "Classical Quarterly",
              year = {2024},
              volume = {74},
              number = {2},
              pages = {100--119},
              doi = {10.1234/Test.2},
              keywords = {authorship; tragedy}
            }
            @book{doe2020,
              author = "Doe, Dana",
              title = {Greek Tragedy},
              date = {2020-05},
              publisher = {Example Press},
              isbn = {978-0-00-000000-0}
            }
            """;

        var records = BibliographyImport.Parse(bib, "sources.bib");

        Assert.Equal(2, records.Count);
        Assert.Equal("The Rhesus Question Reconsidered", records[0].Title);
        Assert.Equal("100-119", records[0].Pages);
        Assert.Equal("https://doi.org/10.1234/test.2", records[0].StableIdentifier);
        Assert.Equal("2020", records[1].Year);
        Assert.Equal("isbn:978-0-00-000000-0", records[1].StableIdentifier);
    }

    [Theory]
    [InlineData("doi:10.1000/ABC", "10.1000/abc")]
    [InlineData("https://doi.org/10.1000/ABC.", "10.1000/abc")]
    [InlineData("http://dx.doi.org/10.1000/ABC", "10.1000/abc")]
    public void DoiNormalizationRemovesResolverNoise(string input, string expected) =>
        Assert.Equal(expected, BibliographyImport.NormalizeDoi(input));

    [Fact]
    public void BibTeXExportRoundTripsStructuredCitationMetadata()
    {
        var source = new BibliographyRecord("RIS", "JOUR", "smith2024rhesus",
            "Rhesus and the Problem of Attribution", ["Smith, Jane", "Jones, Alex"],
            "2024", "Classical Quarterly", "74", "2", "100-119", null,
            "10.1234/TEST.1", "https://example.org/article", null, "A useful abstract.",
            ["stylometry", "tragedy"]);

        var text = BibliographyExport.ToBibTeX([source]);
        var reopened = Assert.Single(BibliographyImport.Parse(text, "export.bib"));

        Assert.Contains("@article{smith2024rhesus,", text);
        Assert.DoesNotContain("keywords = {stylometry, tragedy},", text);
        Assert.Equal(source.Title, reopened.Title);
        Assert.Equal(source.Authors, reopened.Authors);
        Assert.Equal("100-119", reopened.Pages);
        Assert.Equal("https://doi.org/10.1234/test.1", reopened.StableIdentifier);
    }

    [Fact]
    public void RisExportRoundTripsAndMakesGeneratedCiteKeysUnique()
    {
        var first = new BibliographyRecord("Manual", "ARTICLE", null, "Rhesus Reconsidered",
            ["Smith, Jane"], "2024", "Classical Review", null, null, "12-18", null,
            null, null, null, null, []);
        var second = first with { Title = "Rhesus Reconsidered Again" };

        var text = BibliographyExport.ToRis([first, second]);
        var reopened = BibliographyImport.Parse(text, "export.ris");

        Assert.Equal(2, reopened.Count);
        Assert.NotEqual(reopened[0].CiteKey, reopened[1].CiteKey);
        Assert.Equal("12-18", reopened[0].Pages);
        Assert.Equal("Smith2024Rhesus", reopened[0].CiteKey);
    }
}
