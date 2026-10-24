using ClassicaCodex.Ingestion.Oracc;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Reading an Oracc text's English from its page on the website.
///
/// The rows are cut from real pages - Maqlû I (cmawro/maqlu Q002705), CMAwR
/// text 1.2 (cmawro/cmawr1 Q004186) and a Šurpu tablet from Uruk (cams/gkab
/// P348620) - with the word markup shortened but the table shape kept, since
/// the table shape is what this reads.
/// </summary>
public class OraccWebTranslationTests
{
    /// <summary>A tablet's line, sign by sign: ma-mit, "curse", from the Uruk Šurpu tablet.</summary>
    private const string OneTabletLine = """
        { "textid": "P348620", "cdl": [
          { "node": "d", "type": "line-start", "ref": "P348620.11", "label": "r i 1" },
          { "node": "l", "frag": "ma-mit", "f": { "lang": "akk-x-stdbab", "form": "ma-mit", "norm": "māmīt", "sense": "curse" } }
        ] }
        """;

    private static string Page(string rows) =>
        "<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title>"
        + "<script type=\"text/javascript\" src=\"/js/p4.js\">&#160;</script></head><body><table>"
        + rows + "</table></body></html>";

    /// <summary>
    /// <b>One line, one translation</b>, keyed by the row's id - the same
    /// reference the download gives the line - and without the "(1)" label
    /// Oracc prints at the head of the cell.
    /// </summary>
    [Fact]
    public void ALineTranslationIsKeyedByItsRow()
    {
        var english = OraccWebTranslation.ReadLineTranslations(Page(
            "<tr id=\"Q002705.1l\" class=\"l\"><td class=\"lnum \"><span class=\"xlabel \">1</span></td>"
            + "<td class=\"tlit\"><p class=\"tt\"><span class=\"w akk NOUN \">ÉN</span> alsīkunūši</p></td>"
            + "<td class=\"t1 xtr\" data-tlit-id=\"Q002705.1l\"><p class=\"tr\"><span class=\"xtr-label\">(1)</span>"
            + "<span class=\"cell \"><span class=\"w\">Incantation</span>. <span class=\"w\">I</span> <span class=\"w\">call</span> "
            + "<span class=\"w\">upon</span> <span class=\"w\">you</span>, <span class=\"w\">Gods</span> <span class=\"w\">of</span> "
            + "<span class=\"w\">the</span> <span class=\"w\">Night</span>,</span></p></td></tr>"));

        Assert.Equal("Incantation. I call upon you, Gods of the Night,", english["Q002705.1l"]);
    }

    /// <summary>
    /// <b>A line printed in two columns has two cells</b>, read in order - the
    /// lists of Šurpu, where GKAB leaves data-tlit-id empty and only the row
    /// says which line it is.
    /// </summary>
    [Fact]
    public void TwoColumnCellsAreReadInOrder()
    {
        var english = OraccWebTranslation.ReadLineTranslations(Page(
            "<tr id=\"P348620.11\" class=\"l\"><td class=\"lnum \">r i 1</td><td class=\"tlit\">ma-mit</td>"
            + "<td class=\"t1 xtr\" data-tlit-id=\"\" colspan=\"1\"><p class=\"tr\">With the curse (related to)</p></td>"
            + "<td class=\"t2 xtr\" data-tlit-id=\"\" colspan=\"1\"><p class=\"tr\">field, garden, house, street, alley, cult niche, [ ... ];</p></td></tr>"));

        Assert.Equal("With the curse (related to) field, garden, house, street, alley, cult niche, [...];", english["P348620.11"]);
    }

    /// <summary>
    /// <b>A translation over several lines sits on the first</b> (it has a
    /// rowspan), and a ruling - a row that is not a line - gives nothing.
    /// </summary>
    [Fact]
    public void ASpanningTranslationSitsOnItsFirstLineAndRulingsGiveNothing()
    {
        var english = OraccWebTranslation.ReadLineTranslations(Page(
            "<tr id=\"Q004186.1l\" class=\"l\"><td class=\"tlit\">x</td>"
            + "<td class=\"t1 xtr\" data-tlit-id=\"Q004186.1l\" rowspan=\"8\"><p class=\"tr\"><span class=\"xtr-label\">(1.2:1′)</span> ( Too fragmentary for translation )</p></td></tr>"
            + "<tr id=\"Q004186.2l\" class=\"l\"><td class=\"tlit\">y</td></tr>"
            + "<tr id=\"Q004186.9\" class=\"nonl\"><td class=\"nonlbody\">(ruling)</td>"
            + "<td class=\"t1 xtr\" data-tlit-id=\"Q004186.9\"><p class=\"tr\">((ruling))</p></td></tr>"));

        Assert.Equal("(Too fragmentary for translation)", english["Q004186.1l"]);
        Assert.False(english.ContainsKey("Q004186.2l"));
        Assert.False(english.ContainsKey("Q004186.9"));
    }

    /// <summary>A page that is not XML - an error page, a truncated download - gives no English rather than throwing.</summary>
    [Fact]
    public void APageThatIsNotXmlGivesNothing() =>
        Assert.Empty(OraccWebTranslation.ReadLineTranslations("<html><body><p>404</body>"));

    /// <summary>
    /// <b>The English keeps the rows level.</b> The Maqlû download opens each
    /// line twice - an unlabelled line-start, then the labelled one the words
    /// hang on - and the translation must land on the labelled one, with a
    /// blank row wherever the page gave no English, so that all the editions
    /// still scroll together.
    /// </summary>
    [Fact]
    public void TheTranslationKeepsTheRowsLevel()
    {
        const string maqlu = """
        { "textid": "Q002705", "cdl": [{ "node": "c", "cdl": [
          { "node": "d", "type": "line-start", "ref": "Q002705.1" },
          { "node": "d", "type": "line-start", "ref": "Q002705.1l", "n": "1", "label": "1" },
          { "node": "l", "frag": "alsīkunūši", "f": { "lang": "akk-949", "form": "*", "norm": "alsīkunūši", "sense": "call upon" } },
          { "node": "d", "type": "line-start", "ref": "Q002705.2" },
          { "node": "d", "type": "line-start", "ref": "Q002705.2l", "n": "2", "label": "2" },
          { "node": "l", "frag": "ittīkunu", "f": { "lang": "akk-949", "form": "*", "norm": "ittīkunu", "sense": "with" } }
        ]}]}
        """;

        var text = OraccProjectReader.ReadText(maqlu, "Q002705", null);
        var built = OraccIngestService.BuildNodes(text, new Dictionary<string, string>
        {
            ["Q002705.1l"] = "Incantation. I call upon you, Gods of the Night,"
        });

        // The composite's words are normalised Akkadian, not an asterisk, and
        // the text knows it is a composite, so it gets no sign pane.
        Assert.Equal(new[] { "alsīkunūši", "ittīkunu" }, built.Transliteration.Select(n => n.Text));
        Assert.True(text.IsNormalisedComposite);
        Assert.False(OraccProjectReader.ReadText(OneTabletLine, "P348620", null).IsNormalisedComposite);
        Assert.Equal(built.Transliteration.Count, built.Translation.Count);
        Assert.Equal(built.Transliteration.Select(n => n.CitationRef), built.Translation.Select(n => n.CitationRef));
        Assert.Equal(new[] { "Incantation. I call upon you, Gods of the Night,", "" }, built.Translation.Select(n => n.Text));
    }

    /// <summary>
    /// <b>The catalogue lists translation languages as an array.</b> Read as a
    /// string it came back empty, every text looked untranslated, and the
    /// first import of Maqlû fetched no English at all.
    /// </summary>
    [Fact]
    public void TranslationLanguagesAreReadFromTheCatalogueArray()
    {
        var root = Path.Combine(Path.GetTempPath(), "oracc-trans-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "corpusjson"));

        try
        {
            File.WriteAllText(Path.Combine(root, "metadata.json"), """{ "project": "cmawro/maqlu", "config": { "abbrev": "Maqlû" } }""");
            File.WriteAllText(Path.Combine(root, "catalogue.json"), """
            { "members": {
                "Q002705": { "designation": "Maqlû 1", "trans": ["en"] },
                "Q002713": { "designation": "Maqlû Ritual Tablet", "trans": "en" },
                "Q002799": { "designation": "untranslated" }
            } }
            """);
            foreach (var id in new[] { "Q002705", "Q002713", "Q002799" })
                File.WriteAllText(Path.Combine(root, "corpusjson", id + ".json"),
                    $$"""{ "textid": "{{id}}", "cdl": [{ "node": "d", "type": "line-start", "label": "1" }, { "node": "l", "frag": "a", "f": { "form": "a" } }] }""");

            var texts = OraccProjectReader.Read(root).Texts.ToDictionary(t => t.TextId);

            Assert.Equal("en", texts["Q002705"].TranslationLanguages);
            Assert.Equal("en", texts["Q002713"].TranslationLanguages);
            Assert.Null(texts["Q002799"].TranslationLanguages);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WithoutEnglishThereIsNoTranslationRow() =>
        Assert.Empty(OraccIngestService.BuildNodes(OraccProjectReader.ReadText(
            """{ "textid": "P1", "cdl": [{ "node": "d", "type": "line-start", "label": "1" }, { "node": "l", "frag": "a", "f": { "form": "a" } }] }""",
            "P1", null)).Translation);
}
