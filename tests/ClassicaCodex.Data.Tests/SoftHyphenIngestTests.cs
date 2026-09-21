using ClassicaCodex.Core;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A word the printed page broke across two lines is stored as one word.
///
/// <b>What was in the library.</b> Several of these corpora were digitised
/// with the line breaks of the printed page left in the text, as U+00AD SOFT
/// HYPHEN followed by the space that stood at the end of the line. Measured
/// against a full library, 86,188 of 2,340,260 passages carry one:
///
/// <code>
/// patrologia-latina  Original     85,026 of 286,531   29.7%
/// perseus-greek      Translation   1,036 of 223,602    0.5%
/// csel               Original        120 of  92,147    0.1%
/// first1k-greek, perseus-latin           6
/// </code>
///
/// <b>The soft hyphen has no glyph.</b> So this was never a stray hyphen on
/// screen - it was a gap. The reader was shown "ließ sich unter den
/// Lakedämoniern also ver nehmen" and "die Athener und ihre Bundes genossen",
/// and the index held 'tur' (12,692 rows), 'rum', 'bus', 'tione' - Latin word
/// endings, sitting among the words.
///
/// <b>Why at ingest.</b> The tokenizer can rejoin the halves when it counts
/// words, and does - see WordNormalizer.JoinSoftHyphenBreaks - but that leaves
/// the stored text wrong and every reader of it inheriting the fault, and it
/// puts the same obligation on every future piece of code that looks at a
/// passage. Nine places already had to know; four of them did not. Joining
/// where the text is first read means the reader, the search index, the
/// concordance, word study, stylometry, the vocabulary profile and every
/// export are correct without any of them having to remember.
///
/// This is why the fix needs a re-ingest and not only an index rebuild: it
/// changes what is stored, not just what is counted.
/// </summary>
public class SoftHyphenIngestTests
{
    /// <summary>U+00AD, written as its code point because it is invisible.</summary>
    private const string Shy = "­";

    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    private static string TextOf(string body) =>
        Assert.Single(new TeiParser().ParseXml(Wrap(body))).Text;

    /// <summary>
    /// The shape 99.9% of them take, and the one that was on screen: hyphen,
    /// then the space that was the end of the printed line.
    /// </summary>
    [Fact]
    public void AWordBrokenAcrossAPrintedLineIsStoredWhole()
    {
        Assert.Equal(
            "non eguerit adjutorio",
            TextOf($"<p>non eguerit adju{Shy} torio</p>"));
    }

    /// <summary>
    /// Migne's own shape, with the citation-bearing markup around it that the
    /// real files carry - so this is the parser's whole path and not just the
    /// whitespace helper.
    /// </summary>
    [Fact]
    public void TheJoinSurvivesTheMarkupAroundIt()
    {
        Assert.Equal(
            "qui gratiam Dei accipit",
            TextOf($"<p>qui gra{Shy} tiam <hi rend=\"italic\">Dei</hi> accipit</p>"));
    }

    /// <summary>
    /// The German Thucydides, which is where the second-largest block of these
    /// lives and which the first survey of this missed by counting only
    /// Original editions.
    /// </summary>
    [Fact]
    public void TheSameHoldsForATranslation()
    {
        Assert.Equal(
            "die Athener und ihre Bundesgenossen",
            TextOf($"<p>die Athener und ihre Bundes{Shy} genossen</p>"));
    }

    /// <summary>
    /// More than one in a line, which is ordinary in Migne - a page of it is
    /// mostly broken words.
    /// </summary>
    [Fact]
    public void SeveralBreaksInOneLineAllClose()
    {
        Assert.Equal(
            "sacramentum unitatis ecclesiae",
            TextOf($"<p>sacra{Shy} mentum uni{Shy} tatis eccle{Shy} siae</p>"));
    }

    /// <summary>
    /// A soft hyphen with no space after it - the other 0.1% - closes up the
    /// same way rather than leaving the invisible character sitting inside a
    /// stored word where nothing can show it and no typed search can match it.
    /// </summary>
    [Fact]
    public void AHyphenWithNoSpaceAfterItIsDroppedToo()
    {
        var text = TextOf($"<p>non eguerit adju{Shy}torio</p>");

        Assert.Equal("non eguerit adjutorio", text);
        Assert.DoesNotContain('­', text);
    }

    /// <summary>
    /// Ordinary text is untouched, including a real hyphen, which is a
    /// different character and belongs to the word.
    /// </summary>
    [Theory]
    [InlineData("arma uirumque cano")]
    [InlineData("a well-known example")]
    [InlineData("μῆνιν ἄειδε θεά")]
    public void TextWithoutASoftHyphenIsUnchanged(string body)
    {
        Assert.Equal(body, TextOf($"<p>{body}</p>"));
    }

    /// <summary>
    /// The whitespace collapse still happens. It has to come after the join,
    /// not before: once the spaces are reduced the hyphen and its space are
    /// indistinguishable from a word that simply ends there, and the halves
    /// are two words for good.
    /// </summary>
    [Fact]
    public void WhitespaceIsStillCollapsedAroundTheJoin()
    {
        Assert.Equal(
            "uni tatis ecclesiae",
            TextOf("<p>uni    tatis\n\n   ecclesiae</p>"));
    }

    /// <summary>
    /// And the tokenizer keeps its own join, because it has a different job:
    /// text ingested before this change, and text typed into the translation
    /// workbench by hand, never passed through the parser at all.
    /// </summary>
    [Fact]
    public void TheTokenizerStillClosesTheBreakOnItsOwn()
    {
        Assert.Equal(
            new[] { "adjutorio" },
            WordNormalizer.TokenizeLine($"adju{Shy} torio").ToArray());
    }
}
