using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Choosing which rows to measure when sizing a list's horizontal scrollbar.
///
/// The scrollbar itself is a WinForms property no test can reach, and the
/// measuring is a GDI call. What is testable, and what actually decides
/// whether the bar reaches the end of the widest row, is which rows get
/// measured at all - so that part is here rather than in the form.
/// </summary>
public class WidestRowsTests
{
    [Fact]
    public void TheLongestRowComesFirst()
    {
        var picked = WidestRows.Candidates(new[] { "ab", "abcd", "a", "abc" });

        Assert.Equal(new[] { "abcd", "abc", "ab", "a" }, picked);
    }

    /// <summary>
    /// The one that matters: whatever the sample size, the single widest row
    /// has to survive the cut, or the scrollbar stops before the end of it.
    /// </summary>
    [Fact]
    public void TheWidestRowSurvivesEvenWhenItArrivesLast()
    {
        var rows = new List<string>();
        for (var i = 0; i < 5_000; i++) rows.Add(new string('x', 10));
        rows.Add(new string('x', 900));

        Assert.Equal(900, WidestRows.Candidates(rows, sampleSize: 8)[0].Length);
    }

    [Fact]
    public void TheWidestRowSurvivesWhenItArrivesFirst()
    {
        var rows = new List<string> { new string('x', 900) };
        for (var i = 0; i < 5_000; i++) rows.Add(new string('x', 10));

        Assert.Equal(900, WidestRows.Candidates(rows, sampleSize: 8)[0].Length);
    }

    [Fact]
    public void NoMoreThanTheSampleSizeComesBack() =>
        Assert.Equal(3, WidestRows.Candidates(new[] { "aaaa", "bbb", "cc", "d", "eeeee" }, 3).Count);

    /// <summary>
    /// Fewer rows than the sample size is the ordinary case - most of these
    /// lists hold a handful of entries.
    /// </summary>
    [Fact]
    public void FewerRowsThanTheSampleSizeAllComeBack() =>
        Assert.Equal(2, WidestRows.Candidates(new[] { "aa", "b" }, 64).Count);

    /// <summary>
    /// Empty rows have no width to contribute and would otherwise take a slot
    /// in the sample away from a row that does.
    /// </summary>
    [Fact]
    public void EmptyAndMissingRowsAreSkipped() =>
        Assert.Equal(new[] { "aa", "b" },
            WidestRows.Candidates(new[] { null, "aa", "", "b", null }));

    [Fact]
    public void NothingToMeasureIsNotAnError()
    {
        Assert.Empty(WidestRows.Candidates(Array.Empty<string>()));
        Assert.Empty(WidestRows.Candidates(new string?[] { null, "" }));
        Assert.Empty(WidestRows.Candidates(null!));
    }

    /// <summary>
    /// A caller asking for no candidates gets none rather than an exception,
    /// which in the form means an extent of zero and no scrollbar.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveSampleSizeAsksForNothing(int sampleSize) =>
        Assert.Empty(WidestRows.Candidates(new[] { "aaaa", "b" }, sampleSize));

    /// <summary>
    /// The passage rows these lists hold are one line each, so ties on length
    /// are common; every tied row is still a candidate, since which of them is
    /// widest in pixels is exactly what character count cannot decide.
    /// </summary>
    [Fact]
    public void RowsOfEqualLengthAreAllKept() =>
        Assert.Equal(3, WidestRows.Candidates(new[] { "iii", "MMM", "www" }).Count);
}
