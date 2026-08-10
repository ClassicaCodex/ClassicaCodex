using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Who is speaking, and what sort of thing each node is.
///
/// Perseus encodes a speech attribution two ways, and the parser handled
/// neither correctly. Tragedy and comedy use &lt;sp&gt;&lt;speaker&gt;, which
/// is neither a division nor a leaf, so it fell into the branch that descends
/// looking for leaves, found none, and emitted nothing: 42,448 attributions
/// across the Greek corpus, every Terence comedy, all 37 Shakespeare plays.
/// The dialogues put the attribution in a &lt;label&gt; inside the
/// &lt;said&gt;, which flattened into the line and was tokenised as a word:
/// Gorgias is 4.1% speaker abbreviations by word count.
///
/// One case was dropped, the other counted as vocabulary. NodeKind is what
/// lets a speaker be shown without being counted.
///
/// Each test below is a counter-example that killed a simpler rule.
/// </summary>
public class SpeakerAndNodeKindTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""act"" n=""1"">{body}</div1></body></text></TEI.2>";

    // ---------------------------------------------------------------- <sp>

    [Fact]
    public void SpeakerElementBecomesItsOwnNode()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <sp><speaker>PHAEDRIA</speaker>
              <l n=""46"">Quid igitur faciam? non eam?</l>
            </sp>"));

        Assert.Equal(2, nodes.Count);
        Assert.Equal("PHAEDRIA", nodes[0].Text);
        Assert.Equal(TextNodeKinds.Speaker, nodes[0].NodeKind);
        Assert.Equal("Quid igitur faciam? non eam?", nodes[1].Text);
        Assert.Equal(TextNodeKinds.Line, nodes[1].NodeKind);
    }

    /// <summary>
    /// The speaker precedes the words. Emitting it after would put the
    /// attribution under the wrong speech in the reading view.
    /// </summary>
    [Fact]
    public void SpeakerSortsBeforeItsSpeech()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <sp><speaker>Ber.</speaker><l>Who's there?</l></sp>
            <sp><speaker>Fran.</speaker><l>Nay, answer me.</l></sp>"));

        Assert.Equal(
            new[] { "Ber.", "Who's there?", "Fran.", "Nay, answer me." },
            nodes.OrderBy(n => n.SortOrder).Select(n => n.Text));
    }

    /// <summary>
    /// A named segment, not a number from the leaf counter. Consuming a
    /// counter slot would renumber every line after it, and annotations
    /// resolve through (EditionId, CitationRef), so existing bookmarks would
    /// silently move to a different line.
    /// </summary>
    [Fact]
    public void SpeakerDoesNotConsumeALineNumber()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <sp><speaker>A.</speaker><p>first</p></sp>
            <sp><speaker>B.</speaker><p>second</p></sp>"));

        var lines = nodes.Where(n => n.NodeKind == TextNodeKinds.Line).ToList();
        Assert.Equal("1.1", lines[0].CitationRef);
        Assert.Equal("1.2", lines[1].CitationRef);
        Assert.Equal(new[] { "1.speaker1", "1.speaker2" },
            nodes.Where(n => n.NodeKind == TextNodeKinds.Speaker).Select(n => n.CitationRef));
    }

    /// <summary>
    /// Aeschylus and Euripides mark editorially supplied speaker names with
    /// &lt;add&gt;. The name is still the name.
    /// </summary>
    [Fact]
    public void SuppliedSpeakerNameIsRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<sp><speaker><add>Χορός</add></speaker><l>μῆνιν</l></sp>"));

        Assert.Equal("Χορός", nodes[0].Text);
        Assert.Equal(TextNodeKinds.Speaker, nodes[0].NodeKind);
    }

    // ------------------------------------------------- <label> in <said>

    [Fact]
    public void PlatoSpeakerLabelIsLiftedOutOfTheLine()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p><said who=""#Σωκράτης""><label>ΣΩ.</label> ἐξ ἀγορᾶς ἢ πόθεν;</said></p>"));

        var speaker = Assert.Single(nodes.Where(n => n.NodeKind == TextNodeKinds.Speaker));
        Assert.Equal("ΣΩ.", speaker.Text);

        var line = Assert.Single(nodes.Where(n => n.NodeKind == TextNodeKinds.Line));
        Assert.Equal("ἐξ ἀγορᾶς ἢ πόθεν;", line.Text);
        Assert.DoesNotContain("ΣΩ.", line.Text);
    }

    /// <summary>
    /// The @who half of the rule. Josephus numbers his paragraphs α. β. γ. in
    /// a &lt;label&gt; inside a &lt;p&gt;, which looks exactly like an
    /// abbreviated speaker name. 2,113 of them in the corpus, and none is a
    /// speaker - the shape test alone would have taken every one.
    /// </summary>
    [Fact]
    public void ParagraphNumberLabelIsNotASpeaker()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p><label>α.</label> Ἰουδαίοις πρὸς Ῥωμαίους</p>"));

        Assert.DoesNotContain(nodes, n => n.NodeKind == TextNodeKinds.Speaker);
        Assert.Contains("α.", Assert.Single(nodes).Text);
    }

    /// <summary>
    /// The shape half of the rule. The Symposium wraps its section summaries
    /// in a &lt;label&gt; inside a &lt;said who="..."&gt;, so @who alone
    /// would have turned "The Speech of Pausanias" into a speaker. Four in
    /// the corpus - small, and the reason the rule has two halves.
    /// </summary>
    [Fact]
    public void SectionSummaryInsideASpeechIsNotASpeaker()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p><said who=""#Ἀπολλόδωρος""><label>The Speech of Pausanias</label> δοκῶ μοι</said></p>"));

        Assert.DoesNotContain(nodes, n => n.NodeKind == TextNodeKinds.Speaker);
        Assert.Contains("The Speech of Pausanias", Assert.Single(nodes).Text);
    }

    /// <summary>
    /// "Hermogenes." is eleven characters and a speaker; "ΣΩ." is three. A
    /// length threshold alone cannot separate them from a section summary,
    /// which is why the rule tests shape rather than size.
    /// </summary>
    [Fact]
    public void UnabbreviatedSpeakerLabelIsStillASpeaker()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p><said who=""#Ἑρμογένης""><label>Hermogenes.</label> Here is Socrates.</said></p>"));

        Assert.Equal("Hermogenes.",
            Assert.Single(nodes.Where(n => n.NodeKind == TextNodeKinds.Speaker)).Text);
    }

    // ------------------------------------------- the block-element family

    /// <summary>
    /// An element with no branch of its own and nothing inside it that has
    /// one is the content. Holinshed's county lists, 99,505 characters, were
    /// lost this way.
    /// </summary>
    [Fact]
    public void ListItemsAreReadIndividually()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <list><label>Counties in Mounster.</label>
              <item>Kerrie</item><item>Corke</item></list>"));

        Assert.Equal(new[] { "Counties in Mounster.", "Kerrie", "Corke" },
            nodes.OrderBy(n => n.SortOrder).Select(n => n.Text));
        // Counted as text: a list of counties in the Description of Britain
        // is the work, not an annotation on it. Only the reference segment
        // records that it came from an <item>.
        Assert.Equal(new[] { "1.item1", "1.item2" },
            nodes.Where(n => n.Text != "Counties in Mounster.").Select(n => n.CitationRef));
        Assert.All(nodes.Where(n => n.CitationRef.Contains("item")),
            n => Assert.Equal(TextNodeKinds.Line, n.NodeKind));
    }

    /// <summary>
    /// The counterpart, and the reason the family is decided by contents
    /// rather than by a list of element names: a &lt;castGroup&gt; has no
    /// branch either, but it holds cast items, so emitting it whole would
    /// collapse every character in the group into one node.
    /// </summary>
    [Fact]
    public void ContainerHoldingHandledElementsIsStillDescendedInto()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <castList><castGroup><head>Daughters to Lear</head>
              <castItem><role>GONERIL</role></castItem>
              <castItem><role>REGAN</role></castItem>
            </castGroup></castList>"));

        Assert.Equal(2, nodes.Count(n => n.NodeKind == TextNodeKinds.Cast));
        Assert.Contains(nodes, n => n.Text == "GONERIL");
        Assert.Contains(nodes, n => n.Text == "REGAN");
    }

    [Fact]
    public void ParatextIsReadButNotCountedAsAuthorsWords()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<trailer>Thus farre the painefull industrie of Raphaell Hollinshed.</trailer>"));

        Assert.Equal(TextNodeKinds.Paratext, Assert.Single(nodes).NodeKind);
    }

    /// <summary>
    /// The Greek Anthology names each epigram's poet in a &lt;docAuthor&gt;.
    /// Without this the epigrams read without knowing who wrote them.
    /// </summary>
    [Fact]
    public void EpigramAttributionIsRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<docAuthor><persName>Φιλίππου</persName></docAuthor><l>τρὶς μάκαρ</l>"));

        Assert.Equal(TextNodeKinds.Attribution, nodes[0].NodeKind);
        Assert.Equal("Φιλίππου", nodes[0].Text);
    }

    /// <summary>
    /// An unrecognised block defaults to Line, not to an uncounted kind. An
    /// edition whose whole body is one unknown element - the Cypria's summary
    /// is a bare &lt;ab&gt; - would otherwise report zero countable words and
    /// hand the stylometry an empty text.
    /// </summary>
    [Fact]
    public void UnrecognisedBlockCountsAsText()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"<ab>Ἐπιβάλλει τούτοις τὰ λεγόμενα</ab>"));

        Assert.Equal(TextNodeKinds.Line, Assert.Single(nodes).NodeKind);
    }

    // ---------------------------------------------- apparatus on headings

    /// <summary>
    /// ExtractApparatus used to run only on leaves, so a note inside a
    /// heading reached neither the text - FlattenText skips notes, as it must
    /// - nor the Editor's Notes pane. 14,422 characters across the Greek
    /// corpus, 11,025 of them in three notes on headings in the German
    /// Thucydides.
    /// </summary>
    [Fact]
    public void NoteInsideAHeadingReachesTheApparatus()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(
            @"<head>Erstes Buch<note resp=""Widmann"">Vgl. die Einleitung.</note></head>"));

        Assert.Equal("Erstes Buch", Assert.Single(nodes).Text);

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal("1.head", entry.CitationRef);
        Assert.Equal("Widmann", entry.Witness);
        Assert.Contains("Einleitung", entry.Content);
    }

    /// <summary>
    /// The other side of that: a note inline within a line still stays out of
    /// the reading text. That skip is what keeps 17,000 characters of
    /// Agamemnon's apparatus out of the word counts.
    /// </summary>
    [Fact]
    public void NoteInsideALineStaysOutOfTheText()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l n=""7"">ἀστέρας<note>seclusit Pauw</note></l>"));

        Assert.Equal("ἀστέρας", Assert.Single(nodes).Text);
    }
}
