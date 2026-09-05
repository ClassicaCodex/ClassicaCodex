using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Stephanus and Bekker pagination, read off the inline markers Perseus uses
/// to carry it.
///
/// The fixtures below are shortened from the real files - the Euthyphro and
/// Republic markers are Perseus's own, and so is the shape of the Nicomachean
/// Ethics, where the page is "1094a" and the line restarts inside it.
///
/// What is being tested is a judgement rather than an extraction: a marker
/// says where a section starts, and every passage after it and before the
/// next one belongs to that section. Getting a reference off a paragraph on
/// its own is not possible, which is why these run whole documents through the
/// parser rather than calling anything directly.
/// </summary>
public class CanonicalMilestoneTests
{
    private static List<TeiParser.ParsedNode> Parse(string body) =>
        new TeiParser().ParseXml($"<TEI><text><body>{body}</body></text></TEI>");

    private static string?[] References(string body) =>
        Parse(body).Select(n => n.Milestone).ToArray();

    // ---- the great majority of the corpus, which has none of this ---------

    /// <summary>
    /// Homer is cited by book and line, and the citation already says so.
    /// A text with no markers must come through exactly as it did before.
    /// </summary>
    [Fact]
    public void ATextWithNoMarkersRecordsNothing()
    {
        var nodes = Parse("""
            <div n="1" type="textpart"><l n="1">μῆνιν ἄειδε θεά</l><l n="2">οὐλομένην</l></div>
            """);

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.Null(n.Milestone));
    }

    /// <summary>
    /// A milestone is not automatically a citation. Most of them in this
    /// corpus mark card divisions and manuscript pages, which nobody cites -
    /// and a card number sitting where a Stephanus letter should be would look
    /// exactly like a real reference.
    /// </summary>
    [Fact]
    public void AMarkerFromNoNamedAuthorityIsIgnored()
    {
        var references = References("""
            <div n="1" type="textpart">
              <milestone n="14" unit="card"/><p>text</p>
              <milestone n="7" unit="page" resp="Dindorf"/><p>more</p>
            </div>
            """);

        Assert.All(references, Assert.Null);
    }

    // ---- Plato ------------------------------------------------------------

    /// <summary>
    /// The section marker is already page and column together, so it is taken
    /// as it stands. The page marker beside it says the same thing less
    /// precisely and must not overwrite it.
    /// </summary>
    [Fact]
    public void AStephanusSectionIsTakenWhole()
    {
        var references = References("""
            <div n="327" type="textpart">
              <p><milestone n="327" unit="page" resp="Stephanus"/><milestone n="327a" unit="section" resp="Stephanus"/>κατέβην χθὲς εἰς Πειραιᾶ</p>
            </div>
            """);

        Assert.Equal("327a", Assert.Single(references));
    }

    /// <summary>
    /// The rule that cannot be got right by looking at one paragraph: 2c opens
    /// three words before the end of a speech in the real Euthyphro. That
    /// speech is cited where the reader began it, and 2c governs the next one.
    /// </summary>
    [Fact]
    public void APassageIsCitedWhereItBeginsNotWhereItEnds()
    {
        var references = References("""
            <div n="2" type="textpart">
              <p><milestone n="2b" unit="section" resp="Stephanus"/>ἀλλὰ δὴ τίνα γραφήν<milestone n="2c" unit="section" resp="Stephanus"/>σε γέγραπται;</p>
              <p>ἥντινα; οὐκ ἀγεννῆ</p>
            </div>
            """);

        Assert.Equal(new[] { "2b–c", "2c" }, references);
    }

    /// <summary>
    /// Several speeches sit inside one Stephanus section, and all of them are
    /// cited by it. This is the reason the reference is kept beside the
    /// citation rather than becoming it - a citation has to stay unique
    /// because bookmarks resolve through it, and 2a here does not.
    /// </summary>
    [Fact]
    public void ConsecutivePassagesShareTheSectionTheySitIn()
    {
        var references = References("""
            <div n="2" type="textpart">
              <p><milestone n="2a" unit="section" resp="Stephanus"/>τί νεώτερον;</p>
              <p>οὔτοι δὴ Ἀθηναῖοι</p>
              <p>τί φῄς;</p>
            </div>
            """);

        Assert.Equal(new[] { "2a", "2a", "2a" }, references);
    }

    /// <summary>
    /// Perseus puts Plato's speech attribution inside the speech, after the
    /// marker, and the parser lifts it out to sit above the words. It has to
    /// carry the same reference: the attribution and what is said are the same
    /// moment in the dialogue.
    /// </summary>
    [Fact]
    public void ASpeakerLabelCarriesTheReferenceOfItsSpeech()
    {
        var nodes = Parse("""
            <div n="2" type="textpart">
              <p><said who="#Εὐθύφρων"><milestone n="2a" unit="section" resp="Stephanus"/><label>ΕΥΘ.</label>τί νεώτερον;</said></p>
            </div>
            """);

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.Equal("2a", n.Milestone));
    }

    /// <summary>
    /// Perseus divides the Republic one Stephanus page to a paragraph, so a
    /// single node runs across five sections. Naming it by the first would be
    /// true of its opening line and of nothing else in it.
    /// </summary>
    [Fact]
    public void APassageSpanningSectionsReportsTheRange()
    {
        var references = References("""
            <div n="328" type="textpart">
              <p><milestone n="328a" unit="section" resp="Stephanus"/>καὶ ὁ Ἀδείμαντος
                 <milestone n="328b" unit="section" resp="Stephanus"/>ἔφη
                 <milestone n="328e" unit="section" resp="Stephanus"/>δεῦρο</p>
            </div>
            """);

        Assert.Equal("328a–e", Assert.Single(references));
    }

    /// <summary>
    /// A range across two pages keeps both in full, because the two share
    /// nothing a reader would drop.
    /// </summary>
    [Fact]
    public void ARangeAcrossTwoPagesIsWrittenOut()
    {
        var references = References("""
            <div n="330" type="textpart">
              <p><milestone n="329e" unit="section" resp="Stephanus"/>ἀληθῆ
                 <milestone n="330e" unit="section" resp="Stephanus"/>λέγεις</p>
            </div>
            """);

        Assert.Equal("329e–330e", Assert.Single(references));
    }

    // ---- Aristotle --------------------------------------------------------

    /// <summary>
    /// Bekker's "a" and "b" are columns and belong to the page; the line
    /// numbers restart in each column and mean nothing apart from it. Composed
    /// as they are written in print: NE 1094a1.
    /// </summary>
    [Fact]
    public void ABekkerLineIsComposedOntoItsPage()
    {
        var references = References("""
            <div n="1" type="textpart">
              <p><milestone n="1094a" unit="page" resp="Bekker"/><milestone n="1" unit="line" resp="Bekker"/>πᾶσα τέχνη</p>
            </div>
            """);

        Assert.Equal("1094a1", Assert.Single(references));
    }

    /// <summary>
    /// The line number restarts at every column, so carrying the old one
    /// across a page boundary would not merely be stale - composed with the
    /// new page it names a line that exists, somewhere else.
    /// </summary>
    [Fact]
    public void ABekkerLineDoesNotSurviveIntoTheNextColumn()
    {
        var references = References("""
            <div n="1" type="textpart">
              <p><milestone n="1094a" unit="page" resp="Bekker"/><milestone n="25" unit="line" resp="Bekker"/>first</p>
              <p><milestone n="1094b" unit="page" resp="Bekker"/>second</p>
            </div>
            """);

        Assert.Equal(new[] { "1094a25", "1094b" }, references);
    }

    /// <summary>A Bekker range drops whatever the two halves share, as in print.</summary>
    [Theory]
    [InlineData("1", "15", "1094a1–15")]              // same page and column
    [InlineData("1094a15", "1094b10", "1094a15–b10")] // same page, next column
    public void ABekkerRangeIsAbbreviatedTheWayItIsPrinted(string second, string third, string expected)
    {
        var body = second.StartsWith("1094", StringComparison.Ordinal)
            ? $"""
              <div n="1" type="textpart"><p>
                <milestone n="1094a" unit="page" resp="Bekker"/><milestone n="15" unit="line" resp="Bekker"/>first
                <milestone n="1094b" unit="page" resp="Bekker"/><milestone n="10" unit="line" resp="Bekker"/>second</p></div>
              """
            : $"""
              <div n="1" type="textpart"><p>
                <milestone n="1094a" unit="page" resp="Bekker"/><milestone n="{second}" unit="line" resp="Bekker"/>first
                <milestone n="{third}" unit="line" resp="Bekker"/>second</p></div>
              """;

        Assert.Equal(expected, Assert.Single(References(body)));
    }

    // ---- what must not be read --------------------------------------------

    /// <summary>
    /// Apparatus is commentary about the text, not the text. A marker quoted
    /// inside a note is not the reader's position in the work, and following
    /// it would move the citation to wherever an editor happened to point.
    /// </summary>
    [Fact]
    public void AMarkerInsideANoteIsNotThePositionInTheText()
    {
        var references = References("""
            <div n="2" type="textpart">
              <p><milestone n="2a" unit="section" resp="Stephanus"/>τί νεώτερον;</p>
              <p><note>compare <milestone n="9c" unit="section" resp="Stephanus"/>below</note>οὔτοι δή</p>
            </div>
            """);

        Assert.Equal(new[] { "2a", "2a" }, references);
    }

    /// <summary>
    /// The structural citation is untouched by any of this. It is the identity
    /// a bookmark, tag or apparatus entry resolves through, and a reference a
    /// reader cites by cannot also be that - see TextNode.Milestone.
    /// </summary>
    [Fact]
    public void TheStructuralCitationIsLeftAlone()
    {
        var nodes = Parse("""
            <div n="327" type="textpart">
              <p><milestone n="327a" unit="section" resp="Stephanus"/>κατέβην</p>
              <p><milestone n="327b" unit="section" resp="Stephanus"/>ἔπειτα</p>
            </div>
            """);

        Assert.Equal(new[] { "327.1", "327.2" }, nodes.Select(n => n.CitationRef).ToArray());
    }
}
