using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Formatting the signed statistics the validation bench reports.
///
/// This exists because of a real defect on screen. The grid rendered a
/// correlation band as "-+0.00 to +0.75", which is not a number.
///
/// The cause is that "+0.00;-0.00" does not do what it looks like it does.
/// .NET's rule for a two-section custom numeric format is that the second
/// section applies to negative values - except that a negative value which
/// rounds to zero under that section is formatted using the FIRST section
/// instead, while the sign it already carries is kept. Section one is "+0.00",
/// so the output is a minus followed by a plus.
///
/// The band on screen came from a rho displayed as +0.45; the lower end of a
/// 95% interval there sits between -0.005 and 0, which is exactly the window
/// that rounds to zero while still being negative. Values a little either side
/// of that window - -0.0053, or +0.002 - never showed the fault, which is why
/// it survived a review of the format string.
///
/// The fix is not a better format string. A sign asserted at a magnitude below
/// the displayed precision carries no information either way, so there is no
/// correct sign to print and the honest output has none.
/// </summary>
public class StatFormatTests
{
    /// <summary>
    /// The window that produced "-+0.00" on screen: negative, but not by enough
    /// to survive rounding to two places.
    /// </summary>
    [Theory]
    [InlineData(-0.000273)]
    [InlineData(-0.000902)]
    [InlineData(-0.002789)]
    [InlineData(-0.0049)]
    public void ANegativeThatRoundsToZeroPrintsWithoutASign(double value)
    {
        Assert.Equal("0.00", StatFormat.Signed(value));
    }

    /// <summary>
    /// And a negative just outside that window keeps its sign and its
    /// magnitude, rather than being swallowed by an over-eager threshold. This
    /// is the boundary the first draft of the fix got wrong in the other
    /// direction.
    /// </summary>
    [Fact]
    public void ANegativeLargeEnoughToShowIsNotSuppressed()
    {
        Assert.Equal("-0.01", StatFormat.Signed(-0.0053));
    }

    /// <summary>And so does a positive one, for the same reason.</summary>
    [Fact]
    public void APositiveThatRoundsToZeroPrintsWithoutASign()
    {
        Assert.Equal("0.00", StatFormat.Signed(0.0021));
    }

    /// <summary>
    /// Everything large enough for the sign to mean something keeps it.
    /// </summary>
    [Theory]
    [InlineData(0.42, "+0.42")]
    [InlineData(-0.042, "-0.04")]
    [InlineData(-0.07, "-0.07")]
    [InlineData(1.0, "+1.00")]
    public void RealValuesKeepTheirSign(double value, string expected)
    {
        Assert.Equal(expected, StatFormat.Signed(value));
    }

    /// <summary>
    /// The three-decimal version, for margins and separations, applies the same
    /// rule one decimal further down.
    /// </summary>
    [Theory]
    [InlineData(0.154, "+0.154")]
    [InlineData(-0.0002, "0.000")]
    [InlineData(0.0004, "0.000")]
    [InlineData(-0.045, "-0.045")]
    public void TheThreeDecimalVersionBehavesTheSameWay(double value, string expected)
    {
        Assert.Equal(expected, StatFormat.Signed3(value));
    }

    /// <summary>
    /// The band as the grid renders it, at a rho inside the window that broke.
    /// Over nineteen works rho +0.454 gives a lower end of -0.00027 - negative,
    /// and far below the precision at which a sign says anything.
    /// </summary>
    [Fact]
    public void TheBandThatRenderedWronglyNowReadsAsANumber()
    {
        var band = ValidationResult.FisherInterval(0.454, 19);

        Assert.True(band.Low < 0, "the case under test needs a negative lower end");
        Assert.Equal("0.00 to +0.75", StatFormat.Band(band));
    }

    /// <summary>And a band that legitimately starts negative still says so.</summary>
    [Fact]
    public void ABandThatReachesBelowZeroKeepsItsSign()
    {
        var band = ValidationResult.FisherInterval(0.42, 19);

        Assert.Equal("-0.04 to +0.73", StatFormat.Band(band));
    }
}
