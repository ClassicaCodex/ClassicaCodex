using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The third hole in the descend-or-emit decision, and the one that got
/// furthest before being noticed.
///
/// The family branch in WalkDiv emits an element whole when nothing inside it
/// can be reached, and descends otherwise. Both answers are wrong for an
/// element that has BOTH its own words and a reachable child: descending loses
/// the words, because WalkDiv reads child elements and nothing else, and
/// emitting whole would swallow the child's citation.
///
/// Petronius is the case in the corpus. The Satyricon encodes each section as
/// an &lt;ab&gt; of prose with its verse quoted inline in &lt;lg&gt;, so the
/// &lt;lg&gt; sent every one of them down the descent branch: 34,097 letters -
/// the narrative, a fifth of the work - went nowhere, and what survived was
/// the poems, which is the one part of the Satyricon nobody reads it for.
///
/// Measured over canonical-greekLit and canonical-latinLit, three files reach
/// this branch at all: that one, one &lt;quote&gt; in Cicero, and one &lt;q&gt;
/// inside the Petronius. Everything else carrying text alongside handled
/// children already sits inside a leaf, where FlattenText takes the lot.
/// After the fix the corpus-wide loss measured against a full text extraction
/// is zero letters, and no edition gains a character it did not have.
/// </summary>
public class MixedContentBlockTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    /// <summary>
    /// Petronius 14, cut down. Before the fix this returned the two verse
    /// lines and nothing else.
    /// </summary>
    [Fact]
    public void ProseAroundQuotedVerseIsRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<ab>Contra Ascyltos leges timebat et
                <lg><l>Quid faciant leges, ubi sola pecunia regnat,</l></lg>
                Sed praeter unum dipondium</ab>"));

        var texts = nodes.Select(n => n.Text).ToList();

        Assert.Contains(texts, t => t.Contains("Contra Ascyltos leges timebat"));
        Assert.Contains(texts, t => t.Contains("Quid faciant leges"));
        Assert.Contains(texts, t => t.Contains("Sed praeter unum dipondium"));
    }

    /// <summary>
    /// The prose comes back in the order it was written, not gathered into one
    /// node ahead of the verse.
    ///
    /// This is why the block is walked node by node rather than flattened and
    /// then descended into. Petronius alternates prose and verse within a
    /// single &lt;ab&gt;, so taking all the prose first would hand back the
    /// section rearranged - present, in the wrong sequence, and silently so,
    /// which is the failure that is hardest to notice afterwards.
    /// </summary>
    [Fact]
    public void SourceOrderIsPreserved()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<ab>first prose
                <lg><l>quoted verse</l></lg>
                second prose</ab>"));

        var ordered = nodes.OrderBy(n => n.SortOrder).Select(n => n.Text).ToList();

        var first = ordered.FindIndex(t => t.Contains("first prose"));
        var verse = ordered.FindIndex(t => t.Contains("quoted verse"));
        var second = ordered.FindIndex(t => t.Contains("second prose"));

        Assert.True(first >= 0 && verse >= 0 && second >= 0);
        Assert.True(first < verse, "prose before the quotation should come first");
        Assert.True(verse < second, "prose after the quotation should come last");
    }

    /// <summary>
    /// The verse keeps its own node, and its own verse flag.
    ///
    /// Adding &lt;ab&gt; to LeafElements would have fixed the loss in one line
    /// and cost this: the whole section would flatten to a single prose node
    /// with the poetry inside it, unmarked and uncitable.
    /// </summary>
    [Fact]
    public void QuotedVerseStaysItsOwnLine()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<ab>prose <lg><l>quoted verse</l></lg> more prose</ab>"));

        var verse = Assert.Single(nodes, n => n.Text.Contains("quoted verse"));
        Assert.True(verse.IsVerse);
        Assert.DoesNotContain("prose", verse.Text);
    }

    /// <summary>
    /// A footnote inside the prose goes to the apparatus, and does not split
    /// the sentence around it.
    ///
    /// Flushing at every child would have been the simpler rule and would have
    /// broken one paragraph into a node per note - 224 of them in the
    /// Satyricon. Only block-level children interrupt the text; a note, a page
    /// break or a &lt;hi&gt; sits within it.
    /// </summary>
    [Fact]
    public void AnInlineNoteNeitherSplitsTheProseNorEntersIt()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(
            @"<ab>Sed praeter unum dipondium<note>dupondium MSS.</note> quo cicer emeramus</ab>"));

        var prose = Assert.Single(nodes);
        Assert.Contains("Sed praeter unum dipondium", prose.Text);
        Assert.Contains("quo cicer emeramus", prose.Text);
        Assert.DoesNotContain("dupondium MSS.", prose.Text);

        Assert.Contains(parser.LastApparatus, a => a.Content.Contains("dupondium MSS."));
    }

    /// <summary>
    /// The note is keyed to the line it sits in, rather than to a reference of
    /// its own that nothing resolves.
    /// </summary>
    [Fact]
    public void AnInlineNoteIsKeyedToTheLineItSitsIn()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(
            @"<ab>Sed praeter unum dipondium<note>dupondium MSS.</note> quo cicer emeramus</ab>"));

        var prose = Assert.Single(nodes);
        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal(prose.CitationRef, entry.CitationRef);
    }

    /// <summary>
    /// An element with children and no words of its own is still descended
    /// into, unchanged. &lt;sp&gt; and &lt;castGroup&gt; are the common ones,
    /// and neither should acquire a node of its own now that the branch beside
    /// them emits one.
    /// </summary>
    [Fact]
    public void AWrapperWithNoWordsOfItsOwnIsStillJustDescendedInto()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<sp><speaker>ΣΩ.</speaker><p>ἐξ ἀγορᾶς</p></sp>"));

        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.Text == "ΣΩ.");
        Assert.Contains(nodes, n => n.Text == "ἐξ ἀγορᾶς");
    }

    /// <summary>
    /// Whitespace between two quoted stanzas is not a passage.
    ///
    /// The gap between structural children is flushed like any other run, and
    /// an empty one has to emit nothing - or a section of quoted verse would
    /// come back interleaved with blank nodes carrying real citation
    /// references, which every passage list in the application would then show.
    /// </summary>
    [Fact]
    public void TheGapBetweenTwoQuotationsIsNotAPassage()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<ab>opening
                <lg><l>first stanza</l></lg>
                <lg><l>second stanza</l></lg></ab>"));

        Assert.All(nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Text)));
        Assert.Equal(3, nodes.Count);
    }
}
