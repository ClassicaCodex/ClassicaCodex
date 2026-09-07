using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Excel will not open a workbook whose cell holds more than 32,767
/// characters. It reports the file as corrupt and offers to repair it, which
/// drops content.
///
/// The spreadsheet export writes whole passages into cells, and this corpus
/// has 78 passages past that limit - the longest 468,865 characters, fourteen
/// times over - across 35 editions. A search whose results happened to
/// include one produced a file that would not open, and the application
/// reported that it had exported successfully.
/// </summary>
public class ExcelCellLimitTests
{
    private const int ExcelCellLimit = 32767;

    [Fact]
    public void APassageTooLongForACellIsCutToFit()
    {
        var passage = new string('a', 468_865);

        var cell = ResultExport.WithinExcelsCellLimit(passage);

        Assert.True(cell.Length <= ExcelCellLimit,
            $"a cell of {cell.Length} characters is {cell.Length - ExcelCellLimit} over what Excel accepts");
    }

    [Fact]
    public void TheCutIsMarkedRatherThanSilent()
    {
        var cell = ResultExport.WithinExcelsCellLimit(new string('a', 40_000));

        Assert.Contains("too long for one spreadsheet cell", cell, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(ExcelCellLimit - 1)]
    [InlineData(ExcelCellLimit)]
    public void AnythingThatFitsIsLeftExactlyAsItWas(int length)
    {
        var passage = new string('a', length);

        Assert.Equal(passage, ResultExport.WithinExcelsCellLimit(passage));
    }

    [Fact]
    public void NullAndEmptySurvive()
    {
        Assert.Equal(string.Empty, ResultExport.WithinExcelsCellLimit(string.Empty));
    }

    /// <summary>
    /// The corpus contains astral-plane characters, so the cut must not land
    /// between the two halves of one.
    /// </summary>
    [Fact]
    public void TheCutNeverSplitsASurrogatePair()
    {
        for (var pad = 0; pad < 4; pad++)
        {
            var text = new string('a', pad) + string.Concat(Enumerable.Repeat("\U00010300", 20000));

            var cell = ResultExport.WithinExcelsCellLimit(text);
            var body = cell[..^"… [cut: too long for one spreadsheet cell]".Length];

            Assert.False(char.IsHighSurrogate(body[^1]),
                $"padding {pad} left a dangling high surrogate");
        }
    }
}
