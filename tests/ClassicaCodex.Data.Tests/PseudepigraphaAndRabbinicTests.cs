using ClassicaCodex.Ingestion.Pseudepigrapha;
using ClassicaCodex.Ingestion.Sefaria;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the pseudepigrapha and the rabbinic texts out of their three
/// sources: Sefaria's export, the Online Critical Pseudepigrapha's XML and
/// English Wikisource.
///
/// The samples are the sources' own shapes, cut down: Berakhot's folio array,
/// the Testaments' named nodes, McCown's Testament of Solomon with a recension
/// beside it, Charles's 1 Enoch and Conybeare's sections as transcribed.
/// </summary>
public class PseudepigraphaAndRabbinicTests
{
    /// <summary>
    /// Sefaria numbers the sides of a Talmud folio from 1a, though every
    /// tractate begins on 2a, so the third entry is 2a and the fourth 2b.
    /// </summary>
    [Fact]
    public void ATalmudArrayIsCitedByFolioSideAndSegment()
    {
        using var export = SefariaExportLoader.Parse("""
            {"versionTitle":"Wikisource Talmud Bavli","license":"CC-BY-SA",
             "text":[[],[],["מאימתי קורין את שמע","משעה שהכהנים"],["תנא היכא קאי"]]}
            """);

        var passages = SefariaExportLoader.FlattenTalmud(export.Text);

        Assert.Equal(new[] { "2a.1", "2a.2", "2b.1" }, passages.Select(p => p.Citation));
        Assert.Equal("CC-BY-SA", export.License);
    }

    /// <summary>
    /// A text with named parts keeps each part's name as a heading, numbers
    /// the parts by position, and leaves no heading for a part this version
    /// has nothing in - so the Hebrew and the English, which share a schema,
    /// put the Lilith story at the same citation even where one is emptier.
    /// </summary>
    [Fact]
    public void NamedPartsAreNumberedByPositionAndHeadedByName()
    {
        using var export = SefariaExportLoader.Parse("""
            {"versionTitle":"Sefaria Community Translation","license":"CC0",
             "text":{"The Aleph Bet of ben Sira":{"Introduction":["Ben Sira was born."],"":[],
               "The Alphabet of ben Sira, (alternative version)":["Aleph.","The angels appointed for healing: Sanoy, Sansanoy, Semangalof."]}}}
            """);

        var node = SefariaExportLoader.Node(export.Text, "The Aleph Bet of ben Sira");
        Assert.NotNull(node);

        var passages = SefariaExportLoader.Flatten(node!.Value);

        Assert.Equal(
            new[] { "1.head", "1.1", "3.head", "3.1", "3.2" },
            passages.Select(p => p.Citation));
        Assert.True(passages[0].IsHead);
        Assert.StartsWith("The angels appointed for healing", passages[^1].Text);
    }

    [Fact]
    public void AMissingNodeIsNull()
    {
        using var export = SefariaExportLoader.Parse("""{"text":{"Hekhalot":["x"]}}""");

        Assert.Null(SefariaExportLoader.Node(export.Text, "Book of Rezial the Angel"));
    }

    private static string OcpFile(string body)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, $"""
            <book filename="TSol" title="Testament of Solomon">
              <version title="Greek" author="" language="Greek">
                <manuscripts><ms abbrev="McCown" language="Greek" show="yes"><name>McCown</name>
                  <bibliography>Text: C. C. McCown, The Testament of Solomon (Leipzig, 1922).</bibliography></ms></manuscripts>
                <text>{body}</text>
              </version>
              <version title="English" author="OCP" language="English"><text><div number="1"><div number="1">
                <unit id="1"><reading option="0" mss="OCP ">Machine English.</reading></unit></div></div></text></version>
            </book>
            """);
        return path;
    }

    /// <summary>
    /// McCown's text, not the recension printed beside it, and the version
    /// asked for by name, never OCP's own English.
    /// </summary>
    [Fact]
    public void TheEditorsReadingIsTakenAndTheRecensionLeft()
    {
        var path = OcpFile("""
            <div number="5"><div number="1"><unit id="40">
              <reading option="0" mss="McCown ">Καὶ ἤγαγέ μοι Ἀσμοδαῖον τὸν πονηρὸν δαίμονα δεδεμένον.</reading>
              <reading option="1" mss="HI ">Καὶ ἤγαγέν μοι τὸν Ἀσμοδαῖον.</reading>
            </unit></div></div>
            """);
        try
        {
            var (passages, bibliography) = OcpLoader.Load(path, "Greek", "McCown");

            var verse = Assert.Single(passages);
            Assert.Equal("5.1", verse.Citation);
            Assert.Equal("Καὶ ἤγαγέ μοι Ἀσμοδαῖον τὸν πονηρὸν δαίμονα δεδεμένον.", verse.Text);
            Assert.Contains("McCown", bibliography);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verse numbers are found only in order: a "9." inside verse 2 is not a
    /// verse, because 9 is not the next one, and a number that never appears
    /// (3, here) is stepped over rather than ending the chapter.
    /// </summary>
    [Fact]
    public void VerseNumbersAreTakenOnlyInOrder()
    {
        var runs = WikisourceTextLoader.NumberedRuns(
            "1. And it came to pass. 2. And the angels saw them, as in Ps. 9. 4. They were two hundred.", 1);

        Assert.Equal(new[] { 1, 2, 4 }, runs.Select(r => r.Number));
        Assert.Equal("And the angels saw them, as in Ps. 9.", runs[1].Text);
    }

    /// <summary>
    /// Conybeare's section 12 is printed "12, And the angel" - a comma for the
    /// full stop - and it is still section 12, at the start of a paragraph.
    /// </summary>
    [Fact]
    public void ASectionNumberPrintedWithACommaIsStillASection()
    {
        var runs = WikisourceTextLoader.NumberedRuns(
            "11. I Solomon beheld the archangel Uriel.\n\n12, And the angel bade the whales of the sea come out.\n\n13. So Ornias took the ring.", 11);

        Assert.Equal(new[] { 11, 12, 13 }, runs.Select(r => r.Number));
        Assert.StartsWith("And the angel bade the whales", runs[1].Text);
    }

    /// <summary>
    /// A chapter of Charles's Enoch: the section heading becomes a heading, the
    /// "CHAPTER VI." line goes, and the verses keep Charles's brackets.
    /// </summary>
    [Fact]
    public void AnEnochChapterGivesItsHeadingAndItsVerses()
    {
        var passages = WikisourceTextLoader.EnochChapter(6, """
            {{header
             | title = [[../]]
             | section = Chapter 06
            }}

            === VI-XI. The Fall of the Angels ===

            CHAPTER VI.

            1. And it came to pass when the children of men had multiplied. 2. And they were in all two hundred; who descended ⌈in the days⌉ of Jared.
            """);

        Assert.Equal(("6.head", "VI-XI. The Fall of the Angels", true), passages[0]);
        Assert.Equal(("6.1", "And it came to pass when the children of men had multiplied.", false), passages[1]);
        Assert.Equal(("6.2", "And they were in all two hundred; who descended ⌈in the days⌉ of Jared.", false), passages[2]);
        Assert.Equal(3, passages.Count);
    }

    [Fact]
    public void ChapterSectionsWithNumberedParagraphsAreChapterAndVerse()
    {
        var passages = WikisourceTextLoader.ChapterParagraphs("""
            ==Chapter 29==
            {{chapter|29}}

            1 And for all the heavenly troops I imaged the image and essence of fire.

            4 And I threw him out from the height with his angels.
            """);

        Assert.Equal(new[] { "29.1", "29.4" }, passages.Select(p => p.Citation));
    }

    [Fact]
    public void WikiMarkupIsReducedToItsWords() =>
        Assert.Equal(
            "the Watchers of heaven ",
            WikisourceTextLoader.CleanWikitext("''the'' [[Watcher|Watchers]] of '''heaven'''<ref>note {{sic}}</ref> {{c|x}}"));
}
