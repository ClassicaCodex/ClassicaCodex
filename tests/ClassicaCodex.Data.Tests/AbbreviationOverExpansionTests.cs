using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// An abbreviation and the editor's expansion of it are alternatives, and
/// only one belongs in the reading text. Both were being taken, so Nepos read
/// "affinitatem Publii P. Sulpicii" - the praenomen twice over, once expanded
/// and once as the manuscripts print it.
///
/// The text now carries the abbreviation and the expansion goes to the
/// Editor's Notes, keyed to the abbreviation as its lemma. Same shape as
/// &lt;app&gt;: the adopted reading in the text, the alternative beside it.
///
/// Four encodings, all in canonical-latinLit, and the abbreviation sits
/// somewhere different in each - inside the &lt;expan&gt;, before it, after
/// it. One rule covers all four: an &lt;expan&gt; contributes its
/// &lt;abbr&gt; if it holds one and otherwise nothing, which leaves the
/// abbreviation to be read from wherever else it sits.
///
/// 690 elements across the corpora: 498 bare &lt;expan&gt; and 192
/// &lt;choice&gt;-wrapped pairs, the latter previously resolving the other way
/// round, so the same abbreviation read two different ways within one edition
/// depending only on how it happened to be marked up.
/// </summary>
public class AbbreviationOverExpansionTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    private static string TextOf(string body) =>
        Assert.Single(new TeiParser().ParseXml(Wrap(body))).Text;

    // ------------------------------------------------ the four bare shapes

    /// <summary>
    /// The abbreviation follows the expansion as the &lt;expan&gt;'s tail.
    /// Nepos, opening sentence. 79 of these.
    /// </summary>
    [Fact]
    public void ExpansionBeforeTheAbbreviationIsNotRead()
    {
        Assert.Equal(
            "T. Pomponius Atticus",
            TextOf(@"<p><abbr><expan><ex>Titus</ex></expan>T.</abbr> Pomponius Atticus</p>"));
    }

    /// <summary>
    /// The abbreviation comes first, as the enclosing &lt;abbr&gt;'s own text.
    /// 98 of these.
    /// </summary>
    [Fact]
    public void ExpansionAfterTheAbbreviationIsNotRead()
    {
        Assert.Equal(
            "M. Tvlli Ciceronis",
            TextOf(@"<p><abbr>M.<expan><ex>Marci</ex></expan></abbr> Tvlli Ciceronis</p>"));
    }

    /// <summary>
    /// The abbreviation sits inside the &lt;expan&gt;, which is the one shape
    /// where taking nothing at all would lose it. 304 of these, the commonest.
    /// </summary>
    [Fact]
    public void AnAbbreviationInsideTheExpansionIsStillRead()
    {
        Assert.Equal(
            "acturu's age.",
            TextOf(@"<p><expan><abbr>acturu's</abbr><ex>acturus es</ex></expan> age.</p>"));
    }

    /// <summary>
    /// No &lt;ex&gt; at all - the expansion is the &lt;expan&gt;'s own text.
    /// 15 of these.
    /// </summary>
    [Fact]
    public void AnExpansionWithNoSuppliedLetteringIsStillDropped()
    {
        Assert.Equal(
            "commentu's?",
            TextOf(@"<p><abbr><expan>commentus es</expan>commentu's</abbr>?</p>"));
    }

    // ------------------------------------------------- the apparatus entry

    /// <summary>
    /// The expansion is not discarded. It reaches the Editor's Notes against
    /// the same citation, with the abbreviation as its lemma so the entry says
    /// what it is about.
    /// </summary>
    [Fact]
    public void TheExpansionBecomesAnEditorsNote()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(
            @"<p><abbr><expan><ex>Titus</ex></expan>T.</abbr> Pomponius Atticus</p>"));

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal("note", entry.Kind);
        Assert.Equal("Titus", entry.Content);
        Assert.Equal("T.", entry.Lemma);
        Assert.Equal(Assert.Single(nodes).CitationRef, entry.CitationRef);
    }

    /// <summary>
    /// And where the abbreviation is inside the expansion, the note carries
    /// the expansion alone rather than both readings run together.
    /// </summary>
    [Fact]
    public void TheNoteDoesNotRepeatTheAbbreviation()
    {
        var parser = new TeiParser();
        parser.ParseXml(Wrap(
            @"<p><expan><abbr>acturu's</abbr><ex>acturus es</ex></expan> age.</p>"));

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal("acturus es", entry.Content);
        Assert.Equal("acturu's", entry.Lemma);
    }

    // ------------------------------------------------------ inside <choice>

    /// <summary>
    /// The same pairing wrapped in a &lt;choice&gt;, which used to resolve the
    /// other way. 184 in canonical-latinLit and 8 in canonical-greekLit.
    /// </summary>
    [Fact]
    public void AChoiceBetweenAbbreviationAndExpansionTakesTheAbbreviation()
    {
        Assert.Equal(
            "Ti. Gracchum",
            TextOf(@"<p><choice><abbr>Ti.</abbr><expan>T<ex>itum</ex></expan></choice> Gracchum</p>"));
    }

    /// <summary>
    /// A &lt;choice&gt; whose only child is the &lt;expan&gt;, so the
    /// preference lists resolve to it and the abbreviation has to be found
    /// within. 9 of these.
    /// </summary>
    [Fact]
    public void AChoiceHoldingOnlyAnExpansionStillYieldsTheAbbreviation()
    {
        Assert.Equal(
            "acturu's age.",
            TextOf(@"<p><choice><expan><abbr>acturu's</abbr><ex>acturus es</ex></expan></choice> age.</p>"));
    }

    // ---------------------------------------------- the other choice pairs

    /// <summary>
    /// Only the abbr/expan pair is reversed. &lt;reg&gt; is still preferred
    /// over &lt;orig&gt; and &lt;corr&gt; over &lt;sic&gt; - 448 corr/sic and
    /// 114 reg/orig pairs in the corpora depend on it.
    /// </summary>
    [Fact]
    public void TheOtherChoicePairsAreUnchanged()
    {
        Assert.Equal(
            "the regularised form",
            TextOf(@"<p>the <choice><orig>regularysed</orig><reg>regularised</reg></choice> form</p>"));

        Assert.Equal(
            "gestare Latinis.",
            TextOf(@"<p><choice><sic>gesture</sic><corr>gestare</corr></choice> Latinis.</p>"));
    }
}
