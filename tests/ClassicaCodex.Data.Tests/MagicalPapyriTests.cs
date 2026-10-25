using ClassicaCodex.Ingestion.MagicalPapyri;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the magical papyri: the Kyprianos export of Coptic texts, and the
/// catalogue of Greek ones on papyri.info.
///
/// The Kyprianos fragments are the real export's shapes, shortened; the line
/// endings are its bare carriage returns.
/// </summary>
public class MagicalPapyriTests
{
    /// <summary>
    /// <b>A bare carriage return ends a row</b>, and one inside quotes is part
    /// of the field. The export uses nothing else, so a reader that waits for a
    /// newline sees one row the length of the file.
    /// </summary>
    [Fact]
    public void TheCsvReaderTakesBareCarriageReturns()
    {
        var rows = MagicalPapyriLoader.ReadCsv("text_id,text\r1,\"p. 2\r1. ⲙⲓⲭⲁⲏⲗ, \"\"hail\"\"\"\r2,x");

        Assert.Equal(3, rows.Count);
        Assert.Equal("p. 2\r1. ⲙⲓⲭⲁⲏⲗ, \"hail\"", rows[1][1]);
        Assert.Equal(new[] { "2", "x" }, rows[2]);
    }

    /// <summary>
    /// <b>Pages head the lines, arrows and rules are left out</b>, and a line
    /// the edition does not number - a drawing's caption - is kept without a
    /// number.
    /// </summary>
    [Fact]
    public void KyprianosLinesFollowThePageHeaders()
    {
        var (lines, sections) = MagicalPapyriLoader.ReadKyprianosLines(
            "p. 10 (P. Mich. Inv. 598 vo) \r↓\r______________________________\r11. ⲭⲏⲣⲉ̅ ⲡ̣̅ⲣⲏ\r12. ⲡⲟⲟϩ\r"
            + "p. 11 (P. Mich. Inv. 598 vo)\r[tableau 1]\r1. ⲁⲛⲟⲕ");

        Assert.Equal(new[] { "p. 10 (P. Mich. Inv. 598 vo)", "p. 11 (P. Mich. Inv. 598 vo)" }, sections);
        Assert.Equal(new[] { (1, "11"), (1, "12"), (2, (string?)null), (2, "1") }, lines.Select(l => (l.Section, l.Line)));
        Assert.Equal("ⲭⲏⲣⲉ̅ ⲡ̣̅ⲣⲏ", lines[0].Text);
        Assert.Equal("[tableau 1]", lines[2].Text);
    }

    /// <summary>
    /// <b>The translation is cut at its line markers</b> and each piece filed
    /// under the page and line it names, so it stands beside the Coptic. A
    /// marker without a page stays on the page before; the words before the
    /// first marker go with the first line; the translator's own brackets
    /// stay in the text.
    /// </summary>
    [Fact]
    public void KyprianosTranslationsAreCutAtTheirLineMarkers()
    {
        var pieces = MagicalPapyriLoader.ReadKyprianosTranslation(
            "Praise: [p. 2, 1] And Michael did obeisance [2] of the good Father [you]. [p. 3, 1] created us, [2] today",
            new[] { "p. 2 (= Kropp 1)", "p. 3 (= Kropp 18)" });

        Assert.Equal(new[] { (1, "1"), (1, "2"), (2, "1"), (2, "2") }, pieces.Select(p => (p.Section, p.Line!)));
        Assert.Equal("Praise: And Michael did obeisance", pieces[0].Text);
        Assert.Equal("of the good Father [you].", pieces[1].Text);
    }

    [Theory]
    [InlineData("p. 2 (= Kropp 1)", "p. 2, l. ", "p2")]
    [InlineData("1st page (p. 1)", "1st page, l. ", "page1")]
    [InlineData("Recto (hair)", "recto, ", "recto")]
    [InlineData("Col. 2", "col. 2 ", "col2")]
    public void APageHeaderAndAMarkerNamingItCompareEqual(string header, string marker, string key)
    {
        Assert.Equal(key, MagicalPapyriLoader.SectionKey(header));
        Assert.Equal(key, MagicalPapyriLoader.SectionKey(marker));
    }

    [Theory]
    [InlineData("Egyptian (Coptic)", "cop")]
    [InlineData("Greek\rEgyptian (Coptic)", "grc")]
    [InlineData("Egyptian (Coptic)\rArabic", "cop")]
    [InlineData("[non-linguistic]", null)]
    public void ATextIsFiledUnderTheLanguageNamedFirst(string languages, string? expected) =>
        Assert.Equal(expected, MagicalPapyriLoader.LanguageOf(languages));

    [Theory]
    [InlineData("Curse against virility", "urn:magic:kyp:curses")]
    [InlineData("Silencing curse", "urn:magic:kyp:curses")]
    [InlineData("Love spell (magical, formulary)", "urn:magic:kyp:love")]
    [InlineData("Favour before authority", "urn:magic:kyp:love")]
    [InlineData("Healing of spleen", "urn:magic:kyp:healing")]
    [InlineData("Protection of livestock", "urn:magic:kyp:healing")]
    [InlineData("Divination, dream oracle", "urn:magic:kyp:other")]
    public void CopticTextsAreGroupedByWhatTheyWereFor(string type, string urn)
    {
        var text = new KyprianosText(1, "t", type, "cop", null, "P. Mich. 1", 1,
            Array.Empty<KyprianosLine>(), Array.Empty<KyprianosLine>());

        Assert.Equal(urn, MagicalPapyriIngestService.CopticAuthor(text).Urn);
    }

    /// <summary>
    /// <b>"v" is a verso, unless the edition says it is a column</b> - PGM I's
    /// fifth column is "v", and so is the back of half the papyri in DCLP.
    /// </summary>
    [Theory]
    [InlineData("iii", null, "col. iii")]
    [InlineData("2", null, "col. 2")]
    [InlineData("v", "column", "col. v")]
    [InlineData("v", null, "verso")]
    [InlineData("r", null, "recto")]
    [InlineData("b", "fragment", "fragment b")]
    [InlineData("1a", null, "1a")]
    public void PapyrusPartsAreNamedAsAPapyrologistWould(string label, string? type, string expected) =>
        Assert.Equal(expected, MagicalPapyriIngestService.PartName(label, type));

    /// <summary>
    /// <b>The catalogue holds each papyrus once</b>, PGM I among them, and
    /// every file is fetched from the pinned commit.
    /// </summary>
    [Fact]
    public void TheCatalogueHoldsEachPapyrusOnce()
    {
        var papyri = MagicalPapyriCatalogue.Papyri;

        Assert.Equal(papyri.Count, papyri.Select(p => p.Path).Distinct().Count());
        Assert.Equal(papyri.Count, papyri.Select(p => p.Label).Distinct().Count());
        Assert.Contains(papyri, p => p.Label == "PGM I" && p.Path == "DCLP/88/88396.xml");
        Assert.All(papyri, p => Assert.Contains($"/idp.data/{MagicalPapyriCatalogue.Commit}/", MagicalPapyriCatalogue.Url(p)));
    }
}
