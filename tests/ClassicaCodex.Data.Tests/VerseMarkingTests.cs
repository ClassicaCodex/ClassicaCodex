using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Which lines are verse.
///
/// TEI has said so all along - a verse line is &lt;l&gt; and a prose
/// paragraph is &lt;p&gt; - and the parser threw it away. Both are leaves,
/// both became a node of kind 'line', and nothing downstream could tell an
/// epic from a treatise. It is not recoverable afterwards either: the Latin
/// and Greek in this library carry no vowel-length marks, so a line's shape
/// lives in the markup and nowhere else.
///
/// The trap this guards is the one that made it a column of its own rather
/// than a NodeKind. Verse and prose are a different axis from line and
/// speaker, and a node has both: a chorus line is a Line and is verse, a
/// speech attribution in the same play is a Speaker and is not. Had verse
/// become a kind, every line of poetry in the library would have stopped
/// being <see cref="TextNodeKinds.Line"/> - which is the exact value the
/// word counts, core vocabulary and Burrows's Delta filter to - and Homer
/// would have vanished from all three without a single test failing.
/// </summary>
public class VerseMarkingTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    [Fact]
    public void AVerseLineIsMarkedAsVerse()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l n=""1"">Arma virumque cano, Troiae qui primus ab oris</l>"));

        Assert.Single(nodes);
        Assert.True(nodes[0].IsVerse);
    }

    [Fact]
    public void AProseParagraphIsNot()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<p n=""1"">Gallia est omnis divisa in partes tres.</p>"));

        Assert.Single(nodes);
        Assert.False(nodes[0].IsVerse);
    }

    /// <summary>
    /// The load-bearing one. Verse is an extra fact about a line, not a
    /// different sort of thing from one - anything that counts words has to
    /// go on counting these.
    /// </summary>
    [Fact]
    public void AVerseLineIsStillACountableLine()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"<l n=""1"">μῆνιν ἄειδε θεά</l>"));

        Assert.Equal(TextNodeKinds.Line, nodes[0].NodeKind);
    }

    /// <summary>
    /// A verse group is descended into, so each line inside answers for
    /// itself - and a group holding its text directly, with no lines inside,
    /// is a leaf and is verse in its own right.
    /// </summary>
    [Fact]
    public void EveryLineOfAStanzaIsVerse()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<lg><l n=""1"">Vivamus, mea Lesbia, atque amemus</l>
               <l n=""2"">rumoresque senum severiorum</l></lg>"));

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.True(n.IsVerse));
    }

    [Fact]
    public void AStanzaHoldingItsOwnTextIsVerse()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"<lg n=""1"">Miser Catulle, desinas ineptire</lg>"));

        Assert.Single(nodes);
        Assert.True(nodes[0].IsVerse);
    }

    /// <summary>
    /// A play is verse and its apparatus of speakers and staging is not.
    /// Counting a speaker attribution as a line of poetry would put "ΣΩ." in
    /// a metrical profile the same way it once put it in a word-frequency
    /// table.
    /// </summary>
    [Fact]
    public void SpeakersAndStagingInAVersePlayAreNotVerse()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<sp><speaker>ΑΦΡΟΔΙΤΗ</speaker><l n=""1"">πολλὴ μὲν ἐν βροτοῖσι</l></sp>
              <stage>The Chorus enters</stage>"));

        Assert.Equal(3, nodes.Count);
        Assert.False(nodes[0].IsVerse);
        Assert.True(nodes[1].IsVerse);
        Assert.False(nodes[2].IsVerse);
    }

    /// <summary>
    /// A heading inside a book of verse is printed on the page and is not a
    /// line of it.
    /// </summary>
    [Fact]
    public void AHeadingIsNotVerse()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<head>LIBER PRIMVS</head><l n=""1"">Arma virumque cano</l>"));

        Assert.Equal(2, nodes.Count);
        Assert.False(nodes[0].IsVerse);
        Assert.True(nodes[1].IsVerse);
    }
}
