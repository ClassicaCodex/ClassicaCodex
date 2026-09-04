using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reaching both spellings of a word whose u/v or i/j an editor chose.
///
/// The measurements this exists for are in SpellingVariants itself. The short
/// version: searching a full library for "iustitia" found 1,425 lines and hid
/// 2,742, because the other half of the corpus prints "justitia", and the
/// half hidden was the one a reader typing the spelling they were taught in
/// a textbook would have wanted.
/// </summary>
public class SpellingVariantsTests
{
    [Fact]
    public void BothSpellingsOfConsonantalIAreReached()
    {
        var variants = SpellingVariants.Of("iam");

        Assert.Contains("iam", variants);
        Assert.Contains("jam", variants);
    }

    [Fact]
    public void BothSpellingsOfConsonantalUAreReached()
    {
        var variants = SpellingVariants.Of("uita");

        Assert.Contains("uita", variants);
        Assert.Contains("vita", variants);
    }

    /// <summary>
    /// The letters vary one position at a time, not as a house style applied
    /// to the whole word: "iuvenis" really is printed "iuuenis", "juvenis"
    /// and "iuvenis" in this corpus, and only the middle letter moves.
    /// </summary>
    [Fact]
    public void EachPositionVariesIndependently()
    {
        var variants = SpellingVariants.Of("iuvenis");

        Assert.Contains("iuvenis", variants);
        Assert.Contains("iuuenis", variants);
        Assert.Contains("juvenis", variants);
        Assert.Contains("juuenis", variants);
    }

    /// <summary>
    /// The word as given comes back first, so a caller that has to cut the
    /// list short loses invented spellings and never the typed one.
    /// </summary>
    [Fact]
    public void TheWordItselfComesFirst()
    {
        Assert.Equal("justitia", SpellingVariants.Of("justitia")[0]);
        Assert.Equal("iustitia", SpellingVariants.Of("iustitia")[0]);
    }

    [Fact]
    public void EverySpellingIsDistinctAndTheRightLength()
    {
        var variants = SpellingVariants.Of("iudicium");

        Assert.Equal(variants.Count, variants.Distinct().Count());
        Assert.All(variants, v => Assert.Equal("iudicium".Length, v.Length));
    }

    /// <summary>
    /// One ambiguous letter is two spellings, four is sixteen. Worth pinning:
    /// the cost of the whole thing is a database probe per spelling.
    /// </summary>
    [Theory]
    [InlineData("roma", 1)]        // none of these letters at all
    [InlineData("iam", 2)]         // i
    [InlineData("uita", 4)]        // u i
    [InlineData("iuvenis", 16)]    // i u v i
    [InlineData("iudicium", 32)]   // i u i i u
    public void TheCountIsTwoToThePowerOfTheAmbiguousLetters(string word, int expected) =>
        Assert.Equal(expected, SpellingVariants.Of(word).Count);

    /// <summary>
    /// A Greek word with no sigma has none of these letters once normalized,
    /// so it passes through untouched and costs that search nothing.
    /// </summary>
    [Fact]
    public void AGreekWordWithoutASigmaIsUntouched()
    {
        var normalized = WordNormalizer.Normalize("μῆνιν");

        Assert.Equal(new[] { normalized }, SpellingVariants.Of(normalized));
    }

    /// <summary>
    /// A Greek word with one does get the lunate spelling, so that an index
    /// built before the fold existed is still reachable. 87 editions in this
    /// corpus are set in lunate sigma.
    /// </summary>
    [Fact]
    public void AGreekWordWithASigmaAlsoGetsTheLunateSpelling()
    {
        var variants = SpellingVariants.Of(WordNormalizer.Normalize("λόγος"));

        Assert.Contains("λογοσ", variants);
        Assert.Contains("λογοϲ", variants);
    }

    /// <summary>
    /// Every sigma of the word, since a word normalized before the fold
    /// carries the lunate form in all of its positions at once.
    /// </summary>
    [Fact]
    public void EverySigmaOfTheWordVaries()
    {
        var variants = SpellingVariants.Of("σοφιστησ");

        Assert.Contains("σοφιστησ", variants);
        Assert.Contains("ϲοφιϲτηϲ", variants);
        Assert.Equal(8, variants.Count); // three sigmas
    }

    /// <summary>
    /// Past the cap the word is not expanded in full - the alternative is
    /// hundreds of index probes for a word almost nobody searches for. It
    /// still comes back with the two uniform spellings an edition following
    /// one house style would print.
    /// </summary>
    [Fact]
    public void AWordPastTheCapFallsBackToTheTwoUniformSpellings()
    {
        const string word = "iniuriosissimusviuit"; // eleven of these letters

        var variants = SpellingVariants.Of(word);

        Assert.Equal(3, variants.Count);
        Assert.Equal(word, variants[0]);
        Assert.Contains("iniuriosissimusuiuit", variants);   // all u and i
        Assert.Contains("jnjvrjosjssjmvsvjvjt", variants);   // all v and j
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NothingInIsNothingOut(string? word) => Assert.Empty(SpellingVariants.Of(word));

    // ---- ExpandAll -------------------------------------------------------

    [Fact]
    public void EveryWordOfAPhraseIsExpanded()
    {
        var expanded = SpellingVariants.ExpandAll(new[] { "iam", "uita" });

        Assert.Contains("jam", expanded);
        Assert.Contains("vita", expanded);
    }

    [Fact]
    public void ExpandAllDoesNotRepeatASpelling()
    {
        // "iam" and "jam" expand to the same pair.
        var expanded = SpellingVariants.ExpandAll(new[] { "iam", "jam" });

        Assert.Equal(expanded.Count, expanded.Distinct().Count());
        Assert.Equal(2, expanded.Count);
    }

    /// <summary>
    /// Reaching the cap costs variants, never a word the reader typed - a
    /// query that quietly dropped one of its own words would return rows that
    /// do not answer it.
    /// </summary>
    [Fact]
    public void TheCapNeverCostsATypedWord()
    {
        var typed = new[] { "iudicium", "iustitia", "adiuuare", "roma" };

        var expanded = SpellingVariants.ExpandAll(typed, maxTargets: 8);

        Assert.All(typed, w => Assert.Contains(w, expanded));
    }

    [Fact]
    public void TheCapIsHonoured()
    {
        var expanded = SpellingVariants.ExpandAll(
            new[] { "iudicium", "iustitia", "adiuuare" }, maxTargets: 20);

        Assert.True(expanded.Count <= 20, $"expanded to {expanded.Count} targets");
    }

    [Fact]
    public void ExpandAllIgnoresEmptyWords() =>
        Assert.Equal(new[] { "roma" }, SpellingVariants.ExpandAll(new[] { "", "roma" }));
}
