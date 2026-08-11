using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A citation reference is an identity, not a label. Annotations, bookmarks,
/// tags, apparatus entries and bilingual pairing all resolve through
/// (EditionId, CitationRef), so a reference that points at two nodes, or at a
/// whole poem when the source numbered its lines, is a broken identity rather
/// than a cosmetic problem.
///
/// Two faults, measured across the corpora and fixed together because the
/// first changes the count of the second:
///
///   &lt;lg&gt; was tested as a leaf before anything else, so a verse group was
///   flattened whole and the numbered lines inside it never became nodes.
///   11,224 lines across 108 editions.
///
///   Sparse @n numbering collided with the positional counter, and nothing
///   caught it because IX_TextNodes_Edition_Citation is not a unique index.
///   502 references pointed at two nodes each; after the verse-group change,
///   506.
/// </summary>
public class CitationIdentityTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""poem"" n=""1"">{body}</div1></body></text></TEI.2>";

    // ------------------------------------------------------- verse groups

    /// <summary>
    /// Theocritus' Idylls are &lt;lg&gt; wrapping numbered lines. All 1,142 of
    /// those numbers were being discarded, so the whole of Idyll 1 was one
    /// node and Theocritus 1.1 could not be cited.
    /// </summary>
    [Fact]
    public void NumberedLinesInsideAVerseGroupAreCitable()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <lg>
              <l n=""1"">Ἁδύ τι τὸ ψιθύρισμα καὶ ἁ πίτυς αἰπόλε τήνα,</l>
              <l n=""2"">ἃ ποτὶ ταῖς παγαῖσι μελίσδεται, ἁδὺ δὲ καὶ τὺ</l>
            </lg>"));

        Assert.Equal(new[] { "1.1", "1.2" }, nodes.Select(n => n.CitationRef));
        Assert.Equal("Ἁδύ τι τὸ ψιθύρισμα καὶ ἁ πίτυς αἰπόλε τήνα,", nodes[0].Text);
    }

    /// <summary>
    /// The counterpart. A verse group whose text sits directly in it has no
    /// lines to descend to, so it stays a leaf and keeps the reference it has
    /// always had.
    /// </summary>
    [Fact]
    public void VerseGroupWithoutLinesIsStillALeaf()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"<lg>A stanza with no line elements.</lg>"));

        Assert.Equal("1.1", Assert.Single(nodes).CitationRef);
        Assert.Equal(TextNodeKinds.Line, nodes[0].NodeKind);
    }

    /// <summary>
    /// 26 verse groups in the corpora nest a stanza inside a poem - Ophelia's
    /// song in Hamlet, Holinshed's pageant verses, Cleanthes' hymn in
    /// Epictetus. Descending handles the nesting without a branch of its own,
    /// because the citable unit is the line at either depth.
    ///
    /// Decided by contents rather than by @type="stanza", which appears on
    /// some of them and not others.
    /// </summary>
    [Fact]
    public void NestedStanzasFlattenToTheirLines()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <lg type=""song"">
              <lg type=""stanza""><l>And will he not come again?</l><l>No, no, he is dead:</l></lg>
              <lg type=""stanza""><l>His beard was as white as snow,</l></lg>
            </lg>"));

        Assert.Equal(new[] { "1.1", "1.2", "1.3" }, nodes.Select(n => n.CitationRef));
    }

    // ---------------------------------------------------- disambiguation

    /// <summary>
    /// The collision that produced 216 duplicate references in Troilus alone.
    /// Shakespeare numbers every tenth line, so the positional counter
    /// reaches 10 one line after @n="10" already claimed it.
    ///
    /// The first use keeps the plain reference, so the line a reader is most
    /// likely to cite is the one without a letter.
    /// </summary>
    [Fact]
    public void SparseNumberingColldingWithTheCounterIsDisambiguated()
    {
        var body = string.Concat(Enumerable.Range(1, 9).Select(_ => "<l>unnumbered</l>"))
                   + @"<l n=""10"">numbered ten</l>"
                   + "<l>unnumbered</l>";

        var nodes = new TeiParser().ParseXml(Wrap(body));

        Assert.Equal("numbered ten", nodes.Single(n => n.CitationRef == "1.10").Text);
        Assert.Equal("unnumbered", nodes.Single(n => n.CitationRef == "1.10a").Text);
        Assert.Equal(nodes.Count, nodes.Select(n => n.CitationRef).Distinct().Count());
    }

    /// <summary>
    /// Editors number lines with letters themselves - Aeschylus has a real
    /// 1407b in the Agamemnon - so a minted suffix must not land on a
    /// reference the edition already holds. Nothing in the present corpora
    /// makes this happen, which is exactly why it would go unnoticed.
    /// </summary>
    [Fact]
    public void MintedSuffixSkipsAReferenceTheEditionAlreadyUses()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l n=""1407"">first</l><l n=""1407a"">editor's a</l><l n=""1407"">collides</l>"));

        Assert.Equal(new[] { "1.1407", "1.1407a", "1.1407b" },
            nodes.Select(n => n.CitationRef));
        Assert.Equal("editor's a", nodes.Single(n => n.CitationRef == "1.1407a").Text);
    }

    /// <summary>
    /// An apparatus entry is keyed by citation, so it must be keyed to the
    /// disambiguated reference - otherwise a note on the second line attaches
    /// to both.
    /// </summary>
    [Fact]
    public void ApparatusFollowsTheDisambiguatedReference()
    {
        var parser = new TeiParser();
        parser.ParseXml(Wrap(
            @"<l n=""5"">first</l><l n=""5"">second<note>seclusit Pauw</note></l>"));

        Assert.Equal("1.5a", Assert.Single(parser.LastApparatus).CitationRef);
    }

    /// <summary>
    /// References are minted per edition, not per process. A second parse must
    /// not inherit the first one's occupied references and start suffixing
    /// from the wrong place.
    /// </summary>
    [Fact]
    public void ReferencesResetBetweenEditions()
    {
        var parser = new TeiParser();
        var first = parser.ParseXml(Wrap(@"<l n=""1"">alpha</l>"));
        var second = parser.ParseXml(Wrap(@"<l n=""1"">beta</l>"));

        Assert.Equal("1.1", Assert.Single(first).CitationRef);
        Assert.Equal("1.1", Assert.Single(second).CitationRef);
    }
}
