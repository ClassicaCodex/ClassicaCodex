using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Lining two editions of one work up by citation reference.
///
/// Aligning on the reference rather than by sequence is the whole reason this
/// is tractable: the two printings have different line counts - one Aeschylus
/// in this library has 1,884 lines against the other's 1,881 - so walking them
/// in parallel would slip somewhere early and report everything after that as a
/// variant.
/// </summary>
public class CollationTests
{
    private static CollationResult Compare(
        (string, string)[] left, (string, string)[] right, string? language = null) =>
        Collation.Compare(left, right, language);

    [Fact]
    public void EachPassageIsClassifiedByHowFarItHasToBeFoldedToAgree()
    {
        var result = Compare(
            [
                ("1.1", "μῆνιν ἄειδε θεά"),
                ("1.2", "οὐλομένην, ἣ μυρί᾽"),
                ("1.3", "μῆνιν ἄειδε"),
                ("1.4", "ἦλθε πρῶτος")
            ],
            [
                ("1.1", "μῆνιν ἄειδε θεά"),
                ("1.2", "οὐλομένην ἣ μυρί᾽"),
                ("1.3", "μηνιν αειδε"),
                ("1.4", "ἦλθε πρῶτον")
            ]);

        Assert.Equal(CollationStatus.Identical, result.Rows[0].Status);
        Assert.Equal(CollationStatus.PresentationDiffers, result.Rows[1].Status);
        Assert.Equal(CollationStatus.OrthographyDiffers, result.Rows[2].Status);
        Assert.Equal(CollationStatus.TextDiffers, result.Rows[3].Status);

        Assert.Equal(1, result.Identical);
        Assert.Equal(1, result.PresentationDiffers);
        Assert.Equal(1, result.OrthographyDiffers);
        Assert.Equal(1, result.TextDiffers);
        Assert.Equal(4, result.Shared);
    }

    /// <summary>
    /// The counts are the point. A collation reporting two thousand
    /// differences says nothing; one reporting two thousand of which forty are
    /// in the words says where to look.
    /// </summary>
    [Fact]
    public void SubstantiveDifferencesAreCountedApartFromTheRest()
    {
        var left = Enumerable.Range(1, 100)
            .Select(i => ($"1.{i}", i == 42 ? "θεά" : "ἄειδε,")).ToArray();
        var right = Enumerable.Range(1, 100)
            .Select(i => ($"1.{i}", i == 42 ? "μοῦσα" : "ἄειδε")).ToArray();

        var result = Compare(left, right);

        Assert.Equal(1, result.TextDiffers);
        Assert.Equal(99, result.PresentationDiffers);
        Assert.Equal(0, result.Identical);
    }

    /// <summary>
    /// Line counts differ between printings, and that is normal rather than an
    /// error. Each side keeps what the other lacks, named as such.
    /// </summary>
    [Fact]
    public void PassagesOnlyOneEditionHasAreKeptAndAttributed()
    {
        var result = Compare(
            [("1.1", "alpha"), ("1.2", "beta")],
            [("1.2", "beta"), ("1.3", "gamma")]);

        Assert.Equal(CollationStatus.OnlyInLeft, result.Rows[0].Status);
        Assert.Null(result.Rows[0].Right);

        Assert.Equal(CollationStatus.Identical, result.Rows[1].Status);

        Assert.Equal(CollationStatus.OnlyInRight, result.Rows[2].Status);
        Assert.Null(result.Rows[2].Left);
        Assert.Equal("gamma", result.Rows[2].Right);

        Assert.Equal(1, result.OnlyInLeft);
        Assert.Equal(1, result.OnlyInRight);
        Assert.Equal(1, result.Shared);
    }

    /// <summary>
    /// The left edition's own sequence is the spine of the list, whatever
    /// order the references would sort in - "10.1" belongs after "9.1", which
    /// neither a string nor a number comparison gives.
    /// </summary>
    [Fact]
    public void RowsFollowTheLeftEditionsOwnOrder()
    {
        var result = Compare(
            [("9.1", "nine"), ("10.1", "ten"), ("11.1", "eleven")],
            [("11.1", "eleven"), ("10.1", "ten"), ("9.1", "nine")]);

        Assert.Equal(["9.1", "10.1", "11.1"], result.Rows.Select(r => r.PassageRef));
    }

    /// <summary>
    /// Two editions dividing a work differently share no references at all,
    /// and the honest answer is that the pairing cannot be collated - not a
    /// list of several thousand passages each missing from the other side.
    /// </summary>
    [Fact]
    public void TwoEditionsThatDivideTheWorkDifferentlyAreNotAlignable()
    {
        var result = Compare(
            [("1.1", "alpha"), ("1.2", "beta")],
            [("praef.1", "alpha"), ("praef.2", "beta")]);

        Assert.False(result.IsAlignable);
        Assert.Equal(0, result.Shared);
        Assert.Equal(2, result.OnlyInLeft);
        Assert.Equal(2, result.OnlyInRight);
    }

    /// <summary>
    /// The harder version of the same problem, and the one the real library
    /// actually has. Two editions that divide a work differently still collide
    /// on plain numeric references - both number their passages 1, 2, 3 - so
    /// they appear to align perfectly and then disagree at every one. Several
    /// CSEL and Patrologia Latina pairings do exactly this, and reported every
    /// shared passage as a textual variant.
    /// </summary>
    [Fact]
    public void SharingReferenceNumbersIsNotEnoughToBeAlignable()
    {
        var result = Compare(
            Enumerable.Range(1, 30).Select(i => ($"{i}", $"left passage {i}")).ToArray(),
            Enumerable.Range(1, 30).Select(i => ($"{i}", $"right passage {i}")).ToArray());

        Assert.Equal(30, result.Shared);
        Assert.Equal(30, result.TextDiffers);
        Assert.False(result.IsAlignable);
    }

    /// <summary>
    /// And the guard has to leave genuinely divergent editions alone. It exists
    /// to catch two things that were never lined up, not to rule on how much
    /// two real editions may disagree.
    /// </summary>
    [Fact]
    public void AGenuinelyDivergentPairIsStillAlignable()
    {
        // Half the passages differ in the words - far more than any real pair
        // in this library - and this is still a collation worth reading.
        var result = Compare(
            Enumerable.Range(1, 30).Select(i => ($"{i}", i % 2 == 0 ? "shared" : $"left {i}")).ToArray(),
            Enumerable.Range(1, 30).Select(i => ($"{i}", i % 2 == 0 ? "shared" : $"right {i}")).ToArray());

        Assert.Equal(15, result.TextDiffers);
        Assert.Equal(15, result.Agreeing);
        Assert.True(result.IsAlignable);
    }

    /// <summary>
    /// One edition ending a line mid-word and hyphenating it makes two adjacent
    /// lines differ where the text does not. Measured against the real library
    /// this is the second-largest source of false variants after the elision
    /// mark, and unlike a real reading it always comes in pairs.
    /// </summary>
    [Fact]
    public void AWordHyphenatedAcrossALineBreakIsNotAVariant()
    {
        var result = Compare(
            [("108", "ὅπως Ἀχαιῶν"), ("109", "δίθρονον κράτος")],
            [("108", "ὅπως Ἀχαι-"), ("109", "ῶν δίθρονον κράτος")]);

        Assert.Equal(CollationStatus.LineationDiffers, result.Rows[0].Status);
        Assert.Equal(CollationStatus.LineationDiffers, result.Rows[1].Status);
        Assert.Equal(2, result.LineationDiffers);
        Assert.Equal(0, result.TextDiffers);

        // Same words, so it counts as agreement - but it is still shown as its
        // own kind, because where a line breaks is worth seeing.
        Assert.Equal(2, result.Agreeing);
    }

    /// <summary>
    /// A real variant sitting next to another real variant must not be folded
    /// away as lineation just because it has a neighbour.
    /// </summary>
    [Fact]
    public void TwoAdjacentRealVariantsAreNotMistakenForLineation()
    {
        var result = Compare(
            [("1", "λήμασιν ἴσους"), ("2", "ξύμφρονε ταγώ")],
            [("1", "λήμασι δισσοὺς"), ("2", "ξύμφρονα ταγάν")]);

        Assert.Equal(2, result.TextDiffers);
        Assert.Equal(0, result.LineationDiffers);
    }

    /// <summary>
    /// Three consecutive differing lines must not have the middle one claimed
    /// twice - once as the end of the first pair and again as the start of the
    /// next.
    /// </summary>
    [Fact]
    public void ALineIsClaimedByAtMostOneLineationPair()
    {
        var result = Compare(
            [("1", "alpha beta"), ("2", "gamma delta"), ("3", "epsilon")],
            [("1", "alpha"), ("2", "beta gamma delta"), ("3", "zeta")]);

        Assert.Equal(CollationStatus.LineationDiffers, result.Rows[0].Status);
        Assert.Equal(CollationStatus.LineationDiffers, result.Rows[1].Status);
        Assert.Equal(CollationStatus.TextDiffers, result.Rows[2].Status);
    }

    /// <summary>
    /// An edition splitting one citation across several elements has one
    /// passage there, not several rival readings of each other.
    /// </summary>
    [Fact]
    public void RepeatedReferencesWithinAnEditionAreJoinedNotCompared()
    {
        var result = Compare(
            [("1.1", "μῆνιν"), ("1.1", "ἄειδε")],
            [("1.1", "μῆνιν ἄειδε")]);

        var row = Assert.Single(result.Rows);

        Assert.Equal(CollationStatus.Identical, row.Status);
        Assert.Equal("μῆνιν ἄειδε", row.Left);
    }

    [Fact]
    public void AnEmptyEditionCollatesToNothingRatherThanFailing()
    {
        var result = Compare([], [("1.1", "alpha")]);

        Assert.False(result.IsAlignable);
        Assert.Equal(1, result.OnlyInRight);
        Assert.Single(result.Rows);
    }

    /// <summary>
    /// The Latin folds need the language, so it has to reach the comparison
    /// rather than stopping at the call.
    /// </summary>
    [Fact]
    public void TheEditionsLanguageReachesTheFold()
    {
        var passages = new[] { ("1.1", "uenit uidit uicit") };
        var printed = new[] { ("1.1", "venit vidit vicit") };

        Assert.Equal(CollationStatus.OrthographyDiffers,
            Compare(passages, printed, "lat").Rows[0].Status);
    }
}
