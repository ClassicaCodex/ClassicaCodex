using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// How much of a work goes into one translation request.
///
/// It was a fixed count of lines, and a line is not a fixed amount of text.
/// In verse it is about forty characters; in sectioned prose, which is most
/// of this corpus, it is a whole section - Julian averages a thousand
/// characters and his longest is 5,726. So twenty-five lines was a thousand
/// characters of Homer and 22,812 of Julian, from the same constant, and the
/// second one timed out. Since a failed batch ended the whole run, the
/// feature did not work at all for those works.
///
/// Measured across the library: 25 lines is over 12,000 characters for 1,182
/// of 2,922 works and under 2,000 for only 566. The shape it was tuned for
/// is the minority.
/// </summary>
public class TranslationBatchTests
{
    private static List<List<string>> Plan(IEnumerable<string> lines, int maxLines = 25, int maxCharacters = 6000) =>
        TranslationBatches.Plan(lines, l => l.Length, maxLines, maxCharacters);

    private static IEnumerable<string> Lines(int count, int each) =>
        Enumerable.Range(0, count).Select(_ => new string('x', each));

    /// <summary>
    /// Verse, the case the line count was tuned on: twenty-five short lines
    /// are nowhere near the character budget, so nothing changes for it.
    /// </summary>
    [Fact]
    public void ShortLinesStillBatchTwentyFiveAtATime()
    {
        var batches = Plan(Lines(100, 40));

        Assert.Equal(4, batches.Count);
        Assert.All(batches, b => Assert.Equal(25, b.Count));
    }

    /// <summary>
    /// Julian: sections of about a thousand characters. Twenty-five of them
    /// is 22,812 characters, which does not come back.
    /// </summary>
    [Fact]
    public void LongSectionsAreCutByCharactersInsteadOfLines()
    {
        var batches = Plan(Lines(25, 1000));

        Assert.True(batches.Count > 1, "twenty-five thousand-character sections must not be one request");
        Assert.All(batches, b => Assert.True(
            b.Sum(l => l.Length) <= 6000, $"a batch carried {b.Sum(l => l.Length)} characters"));
    }

    [Fact]
    public void NoBatchExceedsEitherLimit()
    {
        var mixed = new List<string>();
        for (var i = 0; i < 60; i++) mixed.Add(new string('x', i % 7 == 0 ? 2200 : 90));

        foreach (var batch in Plan(mixed))
        {
            Assert.True(batch.Count <= 25);
            Assert.True(batch.Sum(l => l.Length) <= 6000);
        }
    }

    /// <summary>
    /// Nothing may be dropped or reordered - a batch plan that loses a line
    /// loses a passage from the finished translation, and the dialog would
    /// report it as one the model never returned.
    /// </summary>
    [Fact]
    public void EveryLineTravelsExactlyOnceAndInOrder()
    {
        var lines = Enumerable.Range(0, 200).Select(i => $"{i}:{new string('x', i % 500)}").ToList();

        var flattened = Plan(lines).SelectMany(b => b).ToList();

        Assert.Equal(lines, flattened);
    }

    /// <summary>
    /// One line longer than the whole budget still goes, alone. Splitting it
    /// would hand the model half a sentence; dropping it would silently lose
    /// a passage. This corpus has 3,418 lines over 6,000 characters.
    /// </summary>
    [Fact]
    public void ALineBiggerThanTheBudgetGetsItsOwnRequest()
    {
        var lines = new List<string> { "short", new('x', 40000), "short" };

        var batches = Plan(lines);

        var alone = Assert.Single(batches.Where(b => b.Count == 1 && b[0].Length == 40000));
        Assert.Single(alone);
        Assert.Equal(3, batches.SelectMany(b => b).Count());
    }

    [Fact]
    public void ConsecutiveOversizedLinesEachGetTheirOwnRequest()
    {
        var batches = Plan(new List<string> { new('x', 9000), new('x', 9000) });

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void NothingToSendIsNoBatches() => Assert.Empty(Plan(new List<string>()));

    [Fact]
    public void ASingleLineIsASingleBatch() => Assert.Single(Plan(new List<string> { "one" }));

    /// <summary>
    /// Empty lines cannot spin the planner or produce empty requests.
    /// </summary>
    [Fact]
    public void EmptyLinesAreCarriedNormally()
    {
        var batches = Plan(new List<string> { "", "", "" });

        Assert.Single(batches);
        Assert.Equal(3, batches[0].Count);
    }

    /// <summary>
    /// The real constants, not just the ones a test passes in.
    /// </summary>
    [Fact]
    public void TheDefaultsAreTheMeasuredOnes()
    {
        Assert.Equal(25, TranslationBatches.MaxLines);
        Assert.Equal(6000, TranslationBatches.MaxCharacters);

        var batches = TranslationBatches.Plan(Lines(25, 1000), l => l.Length);
        Assert.All(batches, b => Assert.True(b.Sum(l => l.Length) <= TranslationBatches.MaxCharacters));
    }
}
