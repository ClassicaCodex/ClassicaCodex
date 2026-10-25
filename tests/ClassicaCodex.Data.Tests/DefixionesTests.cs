using System.Xml.Linq;
using ClassicaCodex.Ingestion.Defixiones;
using ClassicaCodex.Ingestion.Epigraphy;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading curse tablets from their EpiDoc sources, and the Leiden rendering
/// that every EpiDoc collection shares.
///
/// The fragments are taken from the real files - IG II/III³ 8, 1, the
/// Heidelberg database and I.Sicily - shortened but not reshaped, and most
/// cases were found by running the loader over all 867 files.
/// </summary>
public class DefixionesTests
{
    private const string Ns = "http://www.tei-c.org/ns/1.0";

    // Parsed keeping whitespace, as DefixionesLoader.LoadXml's reader does: the
    // space between two bracketed words is a text node of its own, and the
    // default parse drops it.
    private static XElement Div(string inner, string type = "edition") =>
        XElement.Parse($"<div xmlns=\"{Ns}\" type=\"{type}\">{inner}</div>", LoadOptions.PreserveWhitespace);

    private static XDocument Doc(string body) =>
        XDocument.Parse($"<TEI xmlns=\"{Ns}\"><teiHeader/><text><body>{body}</body></text></TEI>", LoadOptions.PreserveWhitespace);

    /// <summary>
    /// <b>A line is a line of the tablet, even inside a container.</b> The
    /// lines sit inside an <c>ab</c>; rendering the <c>ab</c> as one element
    /// put a whole tablet on one line, which is what the first run did.
    /// </summary>
    [Fact]
    public void EachLineBreakStartsALineEvenInsideAContainer()
    {
        var lines = EpiDocText.Lines(Div("<ab><lb n=\"1\"/>δεομαι σο<lb n=\"2\"/>το ερμη</ab>"));

        Assert.Equal(new[] { ("1", "δεομαι σο"), ("2", "το ερμη") }, lines.Select(l => (l.Line, l.Text)));
    }

    /// <summary>
    /// <b>The Leiden brackets.</b> Restored, lost, uncertain, expanded, erased,
    /// superfluous, omitted, and the editor's (!) for a reading he confirms.
    /// </summary>
    [Fact]
    public void LeidenConventions()
    {
        var text = EpiDocText.Lines(Div(
            "<ab><lb n=\"1\"/>κα<supplied reason=\"lost\">τοχ</supplied> <gap reason=\"lost\" extent=\"unknown\"/> "
            + "<unclear>ων</unclear> m<expan><ex>e</ex><abbr>dius</abbr></expan> <del>ab</del> "
            + "<surplus>c</surplus> <supplied reason=\"omitted\">d</supplied> anilum <note>sic</note> "
            + "<gap reason=\"illegible\" quantity=\"2\" unit=\"character\"/></ab>"))[0].Text;

        Assert.Equal("κα[τοχ] [- - -] ω̣ν̣ m(e)dius ⟦ab⟧ {c} ⟨d⟩ anilum (!) ..", text);
    }

    /// <summary>
    /// <b>An apparatus inside the text prints its lemma.</b> PGM I gives
    /// Preisendanz's and Wünsch's readings beside the one it prints; the
    /// printed one is the text, and a lemma that runs on to the next line still
    /// breaks there.
    /// </summary>
    [Fact]
    public void AnInlineApparatusPrintsItsLemma()
    {
        var lines = EpiDocText.Lines(Div(
            "<ab><lb n=\"1\"/>παρεδρικῶς <app type=\"editorial\"><lem>προσγ<supplied reason=\"lost\">ενήσεται</supplied></lem>"
            + "<rdg resp=\"Wünsch\">προσ<supplied reason=\"lost\">λαμβάνεται</supplied></rdg></app> δαίμων"
            + "<lb n=\"2\"/><app><lem>ἀνα<lb n=\"3\" break=\"no\"/>θείς</lem><rdg>ἀναθεὶς</rdg></app></ab>"));

        Assert.Equal(new[] { "παρεδρικῶς προσγ[ενήσεται] δαίμων", "ἀνα-", "θείς" }, lines.Select(l => l.Text));
    }

    /// <summary>
    /// <b>A line break inside the reading not printed still ends the line.</b>
    /// Suppl. Mag. 42 writes χαρχαρό-|στομα across lines 1 and 2; the text
    /// prints the regularised καρχαρόστομε, which has no break, and the first
    /// run merged the two lines and lost line 2.
    /// </summary>
    [Fact]
    public void ABreakInsideTheUnprintedReadingStillEndsTheLine()
    {
        var lines = EpiDocText.Lines(Div(
            "<ab><lb n=\"1\"/>στυγνοῦ <choice><reg>καρχαρόστομε</reg><orig>χαρχαρό<lb n=\"2\" break=\"no\"/>στομα</orig></choice> σκύλαξ"
            + "<lb n=\"3\"/>κευθμωνοδῖτα</ab>"));

        Assert.Equal(new[] { ("1", "στυγνοῦ καρχαρόστομε"), ("2", "σκύλαξ"), ("3", "κευθμωνοδῖτα") },
            lines.Select(l => (l.Line, l.Text)));
    }

    /// <summary>
    /// <b>The editor's italic remarks are asides, not text.</b> IG 8, 1, 482 is
    /// a lost tablet the volume describes in italics, "defixio in lingua Dorica
    /// scripta"; printed plain, it read as words of a Greek tablet.
    /// </summary>
    [Fact]
    public void AnEditorsItalicRemarkIsBracketed()
    {
        var lines = EpiDocText.Lines(Div(
            "<ab><lb n=\"1\"/>καταδῶ <hi rend=\"italic\">vestigia</hi> Φίλωνα<lb n=\"2\"/><hi rend=\"smallit\">vacat</hi>"
            + "<lb n=\"3\"/>[- - - <hi rend=\"sup\">c.6</hi> - - -] <hi rend=\"underline\">ν</hi></ab>"));

        Assert.Equal(new[] { "καταδῶ (vestigia) Φίλωνα", "(vacat)", "[- - - (c.6) - - -] ν" }, lines.Select(l => l.Text));
    }

    /// <summary>
    /// <b>A word broken across lines</b> ends its first line with a hyphen, so
    /// the reader sees "ani-" and then "lum" rather than two words.
    /// </summary>
    [Fact]
    public void AWordBrokenAcrossLinesEndsWithAHyphen()
    {
        var lines = EpiDocText.Lines(Div("<ab><lb n=\"1\"/>Martis ani<lb break=\"no\" n=\"2\"/>lum argenteum</ab>"));

        Assert.Equal(new[] { "Martis ani-", "lum argenteum" }, lines.Select(l => l.Text));
    }

    /// <summary>
    /// <b>A restoration that runs on to the next line</b> opens its bracket on
    /// one line and closes it on the next, as Leiden prints it.
    /// </summary>
    [Fact]
    public void ABracketSpanningALineBreakClosesOnTheNextLine()
    {
        var lines = EpiDocText.Lines(Div("<ab><lb n=\"1\"/>α<supplied reason=\"lost\">βγ<lb n=\"2\"/>δε</supplied>ζ</ab>"));

        Assert.Equal(new[] { "α[βγ", "δε]ζ" }, lines.Select(l => l.Text));
    }

    /// <summary>
    /// <b>Faces and fragments that start again at line 1</b> are numbered as
    /// parts, so the caller can tell the two first lines apart (HD000012 has
    /// three fragments, each from line 1).
    /// </summary>
    [Fact]
    public void TextPartsAreNumberedInOrder()
    {
        var lines = EpiDocText.Lines(Div(
            "<div type=\"textpart\" n=\"1\"><ab><lb n=\"1\"/>Primurudem</ab></div>"
            + "<div type=\"textpart\" n=\"2\"><ab><lb n=\"1\"/>Basilia donat</ab></div>"));

        Assert.Equal(new[] { (1, "1", "Primurudem"), (2, "1", "Basilia donat") }, lines.Select(l => (l.Part, l.Line, l.Text)));
    }

    /// <summary>
    /// <b>Text before a part's first line break is line 0</b> - an editor's
    /// "vacat" in IG 8, 1, 458 - so it does not take line 1's number.
    /// </summary>
    [Fact]
    public void TextBeforeTheFirstLineBreakIsLineZero()
    {
        var lines = EpiDocText.Lines(Div("<ab>vacat<lb n=\"1\"/>Γαμέτην</ab>"));

        Assert.Equal(new[] { ("0", "vacat"), ("1", "Γαμέτην") }, lines.Select(l => (l.Line, l.Text)));
    }

    /// <summary>
    /// <b>An Attic tablet carries two translations, line for line.</b> The
    /// IG gives every tablet Curbera's English and Hallof's German, numbered
    /// with the Greek, so all three can stand side by side.
    /// </summary>
    [Fact]
    public void AnAtticTabletCarriesTwoTranslationsLineForLine()
    {
        var tablet = DefixionesLoader.ReadAttic("458", Doc(
            "<div type=\"edition\"><ab><lb n=\"1\"/>ἐνβάλετε πυρετοὺς</ab></div>"
            + "<div type=\"translation\" xml:lang=\"DE\" resp=\"Klaus Hallof\"><ab><lb n=\"1\"/>werft Fieber</ab></div>"
            + "<div type=\"translation\" xml:lang=\"EN\" resp=\"Jaime Curbera\"><ab><lb n=\"1\"/>cast fevers</ab></div>"));

        Assert.NotNull(tablet);
        Assert.Equal("IG II/III³ 8, 1, 458", tablet!.Label);
        Assert.Equal("cast fevers", tablet.Translations["eng"].Lines[0].Text);
        Assert.Equal("Jaime Curbera", tablet.Translations["eng"].Translator);
        Assert.Equal("werft Fieber", tablet.Translations["deu"].Lines[0].Text);
    }

    /// <summary>
    /// <b>A placeholder is not a tablet.</b> I.Sicily has curse-tablet files
    /// with no text yet, and empty translation divs in every file; neither
    /// becomes anything in the library.
    /// </summary>
    [Fact]
    public void AnEmptySicilianPlaceholderIsSkipped()
    {
        Assert.Null(DefixionesLoader.ReadSicily("ISic030099", Doc(
            "<div type=\"edition\" xml:lang=\"grc\"><ab><lb n=\"1\"/><gap reason=\"lost\" extent=\"unknown\"/></ab></div>"
            + "<div type=\"translation\" xml:lang=\"en\"><p/></div>")));
    }

    /// <summary>
    /// <b>A Heidelberg tablet is filed under its province</b> and named by its
    /// ancient findspot, so the Bath tablets sit under Britannia as Aquae Sulis.
    /// </summary>
    [Fact]
    public void AHeidelbergTabletIsFiledUnderItsProvince()
    {
        var tablet = DefixionesLoader.ReadHeidelberg("HD000012", XDocument.Parse(
            $"<TEI xmlns=\"{Ns}\"><teiHeader><origPlace><placeName type=\"province\">Britannia</placeName>"
            + "<placeName type=\"ancient\">Aquae Sulis</placeName></origPlace></teiHeader>"
            + "<text><body><div type=\"edition\"><ab><lb n=\"1\"/>Basilia donat in templum Martis</ab></div></body></text></TEI>"));

        Assert.NotNull(tablet);
        Assert.Equal("Britannia", tablet!.Group);
        Assert.Equal("Aquae Sulis", tablet.Place);
        Assert.Equal("lat", tablet.Language);
        Assert.Equal("12", tablet.Number);
    }

    /// <summary>
    /// <b>The repository's own markup.</b> The files the step downloads type
    /// the province "provinceItalicRegion" and leave the findspots untyped,
    /// and the first run filed every tablet under "Unknown province" for want
    /// of reading it. Uley has no ancient name, so the modern one stands in.
    /// </summary>
    [Theory]
    [InlineData("<placeName>Aquae Sulis</placeName>", "Bath", "Aquae Sulis")]
    [InlineData("<placeName ref=\"http://www.trismegistos.org/place/002036\"/>", "Uley", "Uley")]
    public void TheRepositoryMarkupGivesProvinceAndFindspot(string ancient, string modern, string expected)
    {
        var tablet = DefixionesLoader.ReadHeidelberg("HD015618", XDocument.Parse(
            $"<TEI xmlns=\"{Ns}\"><teiHeader><origPlace><placeName type=\"provinceItalicRegion\">Britannia</placeName>{ancient}</origPlace>"
            + $"<provenance type=\"found\"><placeName>{modern}</placeName><placeName type=\"modernCountry\">Großbritannien</placeName></provenance></teiHeader>"
            + "<text><body><div type=\"edition\"><ab><lb n=\"1\"/>deo Mercurio</ab></div></body></text></TEI>"));

        Assert.Equal("Britannia", tablet!.Group);
        Assert.Equal(expected, tablet.Place);
    }

    [Theory]
    [InlineData("HD000009", "1/1/HD000009")]
    [InlineData("HD012345", "2/3/HD012345")]
    [InlineData("HD010000", "2/1/HD010000")]
    [InlineData("HD036000", "4/7/HD036000")]
    [InlineData("HD000999", "1/1/HD000999")]
    public void HeidelbergFilesAreFoundByTheirFolderScheme(string id, string expected) =>
        Assert.EndsWith($"/inscriptions/{expected}.xml", DefixionesCatalogue.HeidelbergUrl(id));

    /// <summary>
    /// <b>A tablet in two parts</b> is "175 I" in the library and "175(I)" to
    /// the IG's API, which answers the spaced form with a server error.
    /// </summary>
    [Fact]
    public void AnAtticTabletInTwoPartsIsAskedForAsTheApiNumbersIt()
    {
        Assert.EndsWith("175%28I%29", DefixionesCatalogue.AtticUrl("175 I"));
        Assert.EndsWith("174", DefixionesCatalogue.AtticUrl("174"));
    }

    [Fact]
    public void TabletNumbersSortNumericallyWithTheirParts()
    {
        var sorted = new[] { "175 II", "10", "175 I", "2" }.OrderBy(DefixionesIngestService.NumberKey, StringComparer.Ordinal);

        Assert.Equal(new[] { "2", "10", "175 I", "175 II" }, sorted);
    }
}
