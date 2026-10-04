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

    // ---- identity: an ISBN or ISSN names the container, not the part ------

    [Fact]
    public void TwoArticlesFromOneJournalAreNotOneSource()
    {
        // RIS puts a journal's ISSN in SN; neither article has a DOI.
        const string ris = """
            TY  - JOUR
            AU  - Smith, Jane
            TI  - The Rhesus Scholia
            JO  - Hermes
            SN  - 0018-0777
            ER  -
            TY  - JOUR
            AU  - Jones, Alex
            TI  - Euripides and the Night Watch
            JO  - Hermes
            SN  - 0018-0777
            ER  -
            """;

        var records = BibliographyImport.Parse(ris, "hermes.ris");

        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.DoesNotContain("isbn:", r.StableIdentifier));
        Assert.Equal("0018-0777", records[0].Isbn); // still kept, and still exported
    }

    [Fact]
    public void TwoChaptersOfOneVolumeAreNotOneSourceButTheVolumeIsIdentifiedByItsIsbn()
    {
        const string bib = """
            @incollection{a, author = {Smith, Jane}, title = {Staging}, booktitle = {A Companion to Euripides}, isbn = {978-1-4051-9}}
            @incollection{b, author = {Jones, Alex}, title = {Music}, booktitle = {A Companion to Euripides}, isbn = {978-1-4051-9}}
            @book{c, editor = {McClure, Laura}, title = {A Companion to Euripides}, isbn = {978-1-4051-9}}
            """;

        var records = BibliographyImport.Parse(bib, "companion.bib");

        Assert.Equal(["", "", "isbn:978-1-4051-9"], records.Select(r => r.StableIdentifier));
    }

    // ---- BibTeX as real files write it -----------------------------------

    [Fact]
    public void AQuotationMarkInsideBracesDoesNotSwallowTheRestOfTheFile()
    {
        const string bib = """
            @article{one, author = {M"uller, Hans}, title = {Zum Rhesos}}
            @misc{two, title = {The 12" record}}
            @book{three, author = "Doe, Dana", title = "A {"}quoted{"} word"}
            """;

        var records = BibliographyImport.Parse(bib, "sources.bib");

        Assert.Equal(["Zum Rhesos", "The 12\" record", "A \"quoted\" word"], records.Select(r => r.Title));
        Assert.Equal("M\"uller, Hans", Assert.Single(records[0].Authors));
    }

    [Fact]
    public void AuthorsSplitWhereBibTeXSplitsThem()
    {
        const string bib = """
            @book{x,
              author = {Smith, John and
                        Doe, Jane AND {Barnes and Noble}},
              title  = {A Title Wrapped
                        Across Two Lines}
            }
            """;

        var record = Assert.Single(BibliographyImport.Parse(bib, "sources.bib"));

        Assert.Equal(["Smith, John", "Doe, Jane", "Barnes and Noble"], record.Authors);
        Assert.Equal("A Title Wrapped Across Two Lines", record.Title);
    }

    [Fact]
    public void LatexAccentsAndEscapesArriveAsTheCharactersTheyStandFor()
    {
        const string bib = """
            @article{x,
              author = {M{\"u}ller, Karl Otfried and {\'E}tienne, Robert and Wilamowitz-M\"{o}llendorff, Ulrich von and Kova\v{c}, Ana and Stra{\ss}er, Jens},
              title = {Gods \& Heroes: 50\% of \#1, \emph{Il.}~1.1--7 and the {\AE}gean},
              journal = {Studi Italiani}
            }
            """;

        var record = Assert.Single(BibliographyImport.Parse(bib, "sources.bib"));

        Assert.Equal(["Müller, Karl Otfried", "Étienne, Robert", "Wilamowitz-Möllendorff, Ulrich von", "Kovač, Ana", "Straßer, Jens"],
            record.Authors);
        Assert.Equal("Gods & Heroes: 50% of #1, Il. 1.1–7 and the Ægean", record.Title);
    }

    [Fact]
    public void StringMacrosExpandAndConcatenate()
    {
        const string bib = """
            @string{jhs = "Journal of Hellenic Studies"}
            @comment{ written by hand }
            @article{x, title = {Night}, journal = jhs # { Supplement}, year = 1990, month = jan}
            """;

        var record = Assert.Single(BibliographyImport.Parse(bib, "sources.bib"));

        Assert.Equal("Journal of Hellenic Studies Supplement", record.ContainerTitle);
        Assert.Equal("1990", record.Year);
    }

    [Fact]
    public void AUrlKeepsItsDoubleHyphensAndAPageRangeLosesItsDash()
    {
        const string bib = """
            @misc{x, title = {Page}, url = {https://example.org/a--b?x=1\_2}, pages = {12 -- 15}}
            """;

        var record = Assert.Single(BibliographyImport.Parse(bib, "sources.bib"));

        Assert.Equal("https://example.org/a--b?x=1_2", record.Url);
        Assert.Equal("12-15", record.Pages);
    }

    [Fact]
    public void ACitationHasOneFullStopBetweenEachPart()
    {
        var book = new BibliographyRecord("BibTeX", "BOOK", null, "Greek Tragedy", ["Roe, R."], "2010",
            null, null, null, null, "Example Press", null, null, null, null, []);
        var question = book with { Title = "Who Wrote the Rhesus?", Publisher = null, Year = null };

        Assert.Equal("Roe, R. (2010). Greek Tragedy. Example Press.", book.FormatCitation());
        Assert.Equal("Roe, R. Who Wrote the Rhesus?", question.FormatCitation());
    }

    // ---- what goes out comes back ---------------------------------------

    [Fact]
    public void LatexSpecialCharactersAreEscapedOnExportAndReadBack()
    {
        var source = new BibliographyRecord("Manual", "ARTICLE", "x",
            @"Gods & Heroes: 50% of #1_draft, {braces} and a back\slash, $5", ["Müller, Hans"], "2024",
            "Studi & Saggi", null, null, "12–15", null, null, "https://example.org/a%20b_c", null, null, []);

        var text = BibliographyExport.ToBibTeX([source]);
        var reopened = Assert.Single(BibliographyImport.Parse(text, "export.bib"));

        Assert.Contains(@"Gods \& Heroes: 50\% of \#1\_draft, \{braces\} and a back\textbackslash{}slash, \$5", text);
        Assert.Contains("url = {https://example.org/a%20b_c}", text);
        Assert.Contains("pages = {12--15}", text);
        Assert.Equal(source.Title, reopened.Title);
        Assert.Equal(source.Authors, reopened.Authors);
        Assert.Equal(source.ContainerTitle, reopened.ContainerTitle);
        Assert.Equal(source.Url, reopened.Url);
        Assert.Equal("12-15", reopened.Pages);
    }

    [Fact]
    public void ABookSectionKeepsItsTypeAndItsVolume()
    {
        var chapter = Assert.Single(BibliographyImport.Parse(
            "@incollection{x, title = {Staging}, booktitle = {A Companion to Euripides}, pages = {1--20}}", "x.bib"));

        var bib = BibliographyExport.ToBibTeX([chapter]);
        var ris = BibliographyExport.ToRis([chapter]);

        Assert.Contains("@incollection{x,", bib);
        Assert.Contains("booktitle = {A Companion to Euripides}", bib);
        Assert.Contains("TY  - CHAP", ris);
        Assert.Contains("T2  - A Companion to Euripides", ris);
        Assert.Equal("A Companion to Euripides", Assert.Single(BibliographyImport.Parse(bib, "x.bib")).ContainerTitle);
    }

    [Theory]
    [InlineData("doi:10.1000/ABC", "10.1000/abc")]
    [InlineData("https://doi.org/10.1000/ABC.", "10.1000/abc")]
    [InlineData("http://dx.doi.org/10.1000/ABC", "10.1000/abc")]
    [InlineData("https://dx.doi.org/10.1000/ABC", "10.1000/abc")]
    [InlineData("doi.org/10.1000/ABC", "10.1000/abc")]
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
