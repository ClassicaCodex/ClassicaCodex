using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Two holes in the decision between descending into an element and emitting
/// it whole.
///
/// The family branch in WalkDiv exists because an element with no branch of
/// its own and nothing reachable inside it must be emitted, or its words go
/// nowhere - that is the rule that recovered King Lear's cast list and 42,448
/// speakers. HasHandledDescendant is what asks the question, and it was
/// answering yes in two cases where the answer is no: when the only thing
/// inside was a &lt;note&gt;, and when the only &lt;l&gt; or &lt;p&gt; inside
/// was itself sitting within a note. WalkDiv routes notes to the apparatus and
/// never makes a TextNode from one, so in both cases descending could only
/// reach a dead end.
///
/// 135 elements across the four corpora, most of them Lucian's speakers. The
/// full diff: 21 editions change, 101 nodes gained, none lost, 29,230
/// characters recovered, and the numbered-leaf sequence is identical in every
/// edition - so no existing citation reference moves.
/// </summary>
public class NotesDoNotHideContentTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    // ------------------------------------------- an element carrying a note

    /// <summary>
    /// Lucian, verbatim. The speaker vanished entirely: no text, and no
    /// Speaker node either, so the dialogue read as unattributed lines.
    /// 17 in canonical-greekLit, 12 in First1KGreek, 2 in canonical-latinLit.
    /// </summary>
    [Fact]
    public void ASpeakerCarryingAFootnoteIsStillRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<sp><speaker>Ἀφροδίτη<note n=""1"">ΑΦΡΟΔΙΤΗ vulg.: HPΑ MSS.</note></speaker>
              <l n=""1"">μῆνιν ἄειδε</l></sp>"));

        Assert.Equal(2, nodes.Count);
        Assert.Equal("Ἀφροδίτη", nodes[0].Text);
        Assert.Equal(TextNodeKinds.Speaker, nodes[0].NodeKind);
        Assert.Equal("μῆνιν ἄειδε", nodes[1].Text);
    }

    /// <summary>
    /// The note is not lost by being let out of HandledElements - EmitBlock
    /// collects the apparatus of the block it emits, so it lands in the
    /// Editor's Notes pane keyed to the speaker's own citation rather than to
    /// a "noteN" reference with no line behind it.
    /// </summary>
    [Fact]
    public void TheFootnoteStillReachesTheApparatus()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(
            @"<sp><speaker>Ἀφροδίτη<note n=""1"">ΑΦΡΟΔΙΤΗ vulg.: HPΑ MSS.</note></speaker>
              <l n=""1"">μῆνιν ἄειδε</l></sp>"));

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Contains("vulg.", entry.Content);
        Assert.Equal(nodes[0].CitationRef, entry.CitationRef);
    }

    /// <summary>
    /// The same shape one element out. A quotation carrying an editor's note
    /// was descended into, and the quotation itself - the thing being quoted -
    /// went nowhere.
    /// </summary>
    [Fact]
    public void AQuotationCarryingAFootnoteIsStillRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<quote>atqui si id crederemus<note>non eg. om. L</note> non egeremus</quote>"));

        Assert.Equal("atqui si id crederemus non egeremus", Assert.Single(nodes).Text);
    }

    // --------------------------------- a handled element inside a note

    /// <summary>
    /// The second hole, and the reason the &lt;note&gt; entry alone was not
    /// enough: an editor's note containing a &lt;p&gt; still made
    /// HasHandledDescendant say yes, so the element descended, WalkDiv skipped
    /// the note it found, and nothing was emitted.
    /// </summary>
    [Fact]
    public void AParagraphInsideANoteDoesNotCountAsReachableContent()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<trailer>ΤΕΛΟΣ<note><p>The colophon is Coleridge's.</p></note></trailer>"));

        Assert.Equal("ΤΕΛΟΣ", Assert.Single(nodes).Text);
    }

    /// <summary>
    /// And the case the descent branch exists for still descends. A
    /// &lt;castGroup&gt; must not collapse into one node.
    /// </summary>
    [Fact]
    public void AGroupOfRealEntriesIsStillDescendedInto()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<castList>
                <castItem><role>LEAR</role></castItem>
                <castItem><role>KING OF FRANCE</role></castItem>
              </castList>"));

        Assert.Equal(2, nodes.Count);
        Assert.Equal("LEAR", nodes[0].Text);
        Assert.Equal("KING OF FRANCE", nodes[1].Text);
    }

    // ------------------------------------------------- <choice> fallback

    /// <summary>
    /// A pairing this parser has not met. &lt;choice&gt; means "here are
    /// alternatives, pick one", so the first child is always A reading even
    /// when it is not the one a scholar would choose - and the alternative was
    /// dropping every word in the element.
    ///
    /// This is Menota's shape, 158,700 of them in the ten manuscripts.
    /// MenotaXmlLoader reads those levels properly and nothing routes a Menota
    /// file through TeiParser today, so this is insurance: if one ever
    /// arrives, it should come out at the wrong orthographic level rather than
    /// blank.
    /// </summary>
    [Fact]
    public void AnUnfamiliarChoiceTakesItsFirstReadingRatherThanNone()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p xmlns:me=""http://www.menota.org/ns/1.0"">konungr <choice><me:facs>hæꝩir</me:facs><me:dipl>hefir</me:dipl><me:norm>hefir</me:norm></choice> sagt</p>"));

        Assert.Equal("konungr hæꝩir sagt", Assert.Single(nodes).Text);
    }

    /// <summary>
    /// The familiar pairings are still resolved by preference, not by
    /// position: &lt;orig&gt; comes first in the source and &lt;reg&gt; is
    /// still the one taken.
    /// </summary>
    [Fact]
    public void AKnownChoiceStillPrefersTheRegularisedReading()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p>the <choice><orig>regularysed</orig><reg>regularised</reg></choice> form</p>"));

        Assert.Equal("the regularised form", Assert.Single(nodes).Text);
    }
}
