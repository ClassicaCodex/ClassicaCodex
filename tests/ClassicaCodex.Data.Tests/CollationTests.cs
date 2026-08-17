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
