using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// What a value has to look like before the spreadsheet export is allowed to
/// call it a number.
///
/// The export wrote anything that parsed as a double into a numeric cell.
/// That is right for a count or a score and wrong for a passage reference:
/// "1.10" is book 1, section 10, but as a number it is 1.1 - and so is
/// "1.100". A spreadsheet of search results showed both as 1.1, so two
/// different passages became indistinguishable in the file, and sorting the
/// column put section 10 between sections 1 and 2.
/// </summary>
public class SpreadsheetCellTypeTests
{
    [Theory]
    [InlineData("1.10")]     // book 1, section 10 - would collide with 1.1
    [InlineData("1.100")]    // and with 1.10
    [InlineData("2.20")]
    [InlineData("10.010")]
    public void APassageReferenceIsNeverWrittenAsANumber(string reference)
    {
        Assert.False(ResultExport.IsSafelyANumber(reference, out _),
            $"\"{reference}\" would be stored as {double.Parse(reference)} and stop being itself");
    }

    [Theory]
    [InlineData("42", 42d)]
    [InlineData("0", 0d)]
    [InlineData("-3", -3d)]
    [InlineData("0.5", 0.5d)]
    [InlineData("1.25", 1.25d)]
    [InlineData("1000", 1000d)]
    public void APlainNumberStillIsOne(string value, double expected)
    {
        Assert.True(ResultExport.IsSafelyANumber(value, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("25/25")]
    [InlineData("Euripides (23/25)")]
    [InlineData("Iliad")]
    [InlineData("")]
    [InlineData("   ")]
    public void TextIsStillText(string value)
    {
        Assert.False(ResultExport.IsSafelyANumber(value, out _));
    }

    /// <summary>
    /// The cost of the rule, recorded on purpose rather than discovered later:
    /// a value written to a fixed number of decimal places is no longer
    /// numeric, so a stylometry column of "0.020" exports as text and cannot
    /// be charted without retyping it. That is the trade for never turning
    /// one passage reference into another.
    /// </summary>
    [Fact]
    public void ATrailingZeroCostsTheNumericCell()
    {
        Assert.False(ResultExport.IsSafelyANumber("0.020", out _));
        Assert.True(ResultExport.IsSafelyANumber("0.02", out _));
    }
}
