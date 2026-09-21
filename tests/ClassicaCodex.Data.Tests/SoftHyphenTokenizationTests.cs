using ClassicaCodex.Core;
using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A word broken across a printed line break is one word.
///
/// Migne was digitised with the line breaks of the printed page left in the
/// text, as a soft hyphen (U+00AD) and the newline that followed it. Measured
/// against the library on 2026-09-19: 85,026 of the 286,531 Latin lines in
/// patrologia-latina carry one - 29.7% of the collection - and 86,106 of the
/// 86,188 occurrences corpus-wide (99.9%) are a soft hyphen followed by a
/// space. No other collection has more than 120 affected lines.
///
/// The tokenizer split on whitespace, and U+00AD is Unicode category Cf, so
/// neither the whitespace split nor \p{L}+ nor char.IsLetter saw anything to
/// hold the two halves together. The damage ran both ways: a search for the
/// whole word missed every broken occurrence of it in the whole of Migne,
/// and the halves became live index entries in its place - 'tur' 12,692 rows,
/// 'rum' 12,284, 'bus' 8,198, 'runt' 3,672, 'tione' 3,365, 'tatem' 1,911,
/// 'niam' 1,674. Those are Latin word endings, not words.
///
/// Nothing here throws if it regresses. The index would simply fill up with
/// fragments again and searches would quietly come back short, which is why
/// the rule is pinned rather than trusted.
/// </summary>
public class SoftHyphenTokenizationTests
{
    /// <summary>
    /// U+00AD SOFT HYPHEN, written as its code point rather than as the
    /// character, so that every test below says on its face what is being
    /// tested. The character itself is invisible in an editor, which is half
    /// of why this went unnoticed in the corpus for as long as it did.
    /// </summary>
    private const char Shy = (char)0x00AD;

    /// <summary>
    /// The shape 99.9% of them take: hyphen, then the page's line break.
    /// </summary>
    [Fact]
    public void AWordBrokenAtALineBreakIsOneWord() =>
        Assert.Equal(new[] { "gratiam" }, WordNormalizer.TokenizeLine($"gra{Shy} tiam"));

    /// <summary>
    /// Whatever the digitisation left in place of the printed line break -
    /// a space, a newline, several of either - is part of the break and not
    /// part of either half.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("   ")]
    [InlineData(" \n  ")]
    public void TheBreakItselfIsNotPartOfEitherHalf(string lineBreak) =>
        Assert.Equal(new[] { "gratiam" }, WordNormalizer.TokenizeLine($"gra{Shy}{lineBreak}tiam"));

    /// <summary>
    /// The other 0.1%, where nothing was left between the halves. Normalize
    /// already dropped the hyphen as a non-letter, so this spelling was
    /// always joined - and the two spellings have to agree, or the same word
    /// indexes differently depending on which one an edition happens to use.
    /// </summary>
    [Fact]
    public void AHyphenWithNoBreakAfterItJoinsTheSameWay() =>
        Assert.Equal(
            WordNormalizer.TokenizeLine($"gra{Shy} tiam"),
            WordNormalizer.TokenizeLine($"gra{Shy}tiam"));

    /// <summary>
    /// The endings that were filling the index. Each of these is the tail of
    /// a real word, and none of them may survive as a token of its own.
    /// </summary>
    [Theory]
    [InlineData("dice", "tur", "dicetur")]
    [InlineData("vero", "rum", "verorum")]
    [InlineData("omni", "bus", "omnibus")]
    [InlineData("fece", "runt", "fecerunt")]
    [InlineData("ora", "tione", "oratione")]
    [InlineData("civi", "tatem", "civitatem")]
    [InlineData("quo", "niam", "quoniam")]
    public void NoWordEndingSurvivesAsAWordOfItsOwn(string head, string tail, string whole)
    {
        var tokens = WordNormalizer.TokenizeLine($"{head}{Shy} {tail}");

        Assert.Equal(new[] { whole }, tokens);
        Assert.DoesNotContain(tail, tokens);
    }

    /// <summary>
    /// Only the break is closed up. Ordinary spacing on either side of it
    /// still separates words, or the fix would fuse the line into one token.
    /// </summary>
    [Fact]
    public void TheWordsAroundTheBreakAreStillSeparateWords() =>
        Assert.Equal(
            new[] { "per", "gratiam", "dei" },
            WordNormalizer.TokenizeLine($"per gra{Shy} tiam dei"));

    /// <summary>
    /// Several breaks in one line - the norm in Migne, where the passage is
    /// a whole section of printed page.
    /// </summary>
    [Fact]
    public void EveryBreakInALineIsClosed() =>
        Assert.Equal(
            new[] { "sapientia", "aedificavit", "sibi", "domum" },
            WordNormalizer.TokenizeLine($"sapien{Shy} tia aedifi{Shy} cavit sibi do{Shy} mum"));

    /// <summary>
    /// A break at the very end of what was stored: there is no second half
    /// to join, and the first half must still be indexed rather than lost.
    /// </summary>
    [Fact]
    public void AHalfWordAtTheEndOfTheLineIsStillIndexed() =>
        Assert.Equal(new[] { "per", "gra" }, WordNormalizer.TokenizeLine($"per gra{Shy} "));

    /// <summary>
    /// A line with no soft hyphen in it - 70% of the corpus - has to come
    /// back exactly as it was.
    /// </summary>
    [Theory]
    [InlineData("arma virumque cano")]
    [InlineData("μῆνιν ἄειδε θεά")]
    [InlineData("")]
    [InlineData("   ")]
    public void TextWithoutASoftHyphenIsUntouched(string text) =>
        Assert.Same(text, WordNormalizer.JoinSoftHyphenBreaks(text));

    /// <summary>
    /// Delta weights by frequency over the commonest words, so invented
    /// function-word-shaped tokens do not merely add noise - they go to the
    /// top of the feature list, and the analysis starts measuring which
    /// corpus a text was digitised from instead of who wrote it. Same
    /// confound StripElisionMarks exists to remove, one layer coarser.
    /// </summary>
    [Fact]
    public void StylometryCountsTheWholeWordAndNotItsEnding()
    {
        var tokens = StylometryTokenizer.Tokenize($"dice{Shy} tur omni{Shy} bus", foldAccents: true);

        Assert.Equal(new[] { "dicetur", "omnibus" }, tokens);
    }

    /// <summary>
    /// A vocabulary profile is a frequency count over the same text. Left
    /// alone it ranks 'tur' and 'rum' among the commonest words in Migne and
    /// counts every broken word short.
    /// </summary>
    [Fact]
    public void AVocabularyProfileCountsTheWholeWord()
    {
        var counts = VocabularyProfile.CountForms(new[] { $"gra{Shy} tiam per gratiam" });

        Assert.Equal(2, counts["gratiam"]);
        Assert.DoesNotContain("tiam", counts.Keys);
    }

    /// <summary>
    /// A phrase pasted out of a Migne passage carries the printed page's
    /// hyphens with it. The query has to be read into words the same way the
    /// passage was, or the reader's own copy of the text fails to find it.
    /// </summary>
    [Fact]
    public void AQueryPastedOutOfThePassageAsksForTheSameWord() =>
        Assert.True(WordOccurrences.TargetsFor($"gra{Shy} tiam")
            .SetEquals(WordOccurrences.TargetsFor("gratiam")));
}
