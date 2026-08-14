using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// The parameter grid runs the validation sweep at many settings, so that
/// settings can be chosen for behaving well on texts whose authorship is known
/// rather than for producing an interesting answer about one that is disputed.
///
/// WHAT IT IS SEARCHED FOR. Not the highest recovery rate. On a same-genre pool
/// recovery saturates - nineteen of nineteen Euripides plays at nearly every
/// setting tried - so it discriminates nothing. The informative axis is the
/// length correlation.
///
/// AND WHAT THAT SEARCH WOULD FIND WITHOUT A GUARD. Raising the sample size
/// drops works too short to yield a sample, which compresses the spread of work
/// lengths, and a compressed spread lowers rho whatever the method is doing.
/// Euripides at 2,500 tests all nineteen plays at a spread of 2.43; at 5,000 it
/// tests eighteen at 1.85. A grid reporting rho alone would show the confound
/// melting away as the sample size rose and it would be an artefact of the
/// corpus getting more uniform. Hence WorksValidated and LengthSpread on every
/// cell, and the spread condition inside Trustworthy.
/// </summary>
public class ParameterGridRunnerTests
{
    private static List<WorkTokens> Pool(int alphaWorks = 4, int betaWorks = 4)
    {
        var vocab = new[] { "men", "de", "kai", "ho", "to", "gar", "oun", "te", "alla", "hos" };

        List<string> Draw(int seed, int count, double[] bias)
        {
            var rng = new Random(seed);
            var weights = vocab.Select((_, i) => bias[i] + rng.NextDouble() * 0.2).ToArray();
            var total = weights.Sum();
            var tokens = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var pick = rng.NextDouble() * total;
                var w = 0;
                while (w < vocab.Length - 1 && pick > weights[w]) pick -= weights[w++];
                tokens.Add(vocab[w]);
            }
            return tokens;
        }

        var alpha = new[] { 2.0, 2.0, 1.0, 1.0, 1.0, 0.2, 0.2, 0.5, 0.3, 0.3 };
        var beta = new[] { 0.2, 0.2, 1.0, 1.0, 1.0, 2.0, 2.0, 0.5, 0.3, 0.3 };

        var pool = new List<WorkTokens>();

        // Deliberately uneven lengths, so length spread and the drop-works
        // effect both have something to act on.
        var lengths = new[] { 900, 1500, 2100, 2700, 3300, 3900 };

        for (var i = 0; i < alphaWorks; i++)
            pool.Add(new WorkTokens(10 + i, "Alpha", $"A{i}", Draw(100 + i, lengths[i % lengths.Length], alpha)));
        for (var i = 0; i < betaWorks; i++)
            pool.Add(new WorkTokens(20 + i, "Beta", $"B{i}", Draw(200 + i, lengths[i % lengths.Length], beta)));

        return pool;
    }

    // ------------------------------------------------------------ building

    /// <summary>
    /// Every combination, once, in a stable order - a grid whose rows moved
    /// between runs could not be compared against a screenshot of itself.
    /// </summary>
    [Fact]
    public void TheGridIsEveryCombinationInAStableOrder()
    {
        var points = ParameterGridRunner.Build(
            new[] { 1000, 2000 }, new[] { 50, 100 }, new[] { true, false });

        Assert.Equal(8, points.Count);
        Assert.Equal(8, points.Distinct().Count());
        Assert.Equal(points, ParameterGridRunner.Build(
            new[] { 2000, 1000 }, new[] { 100, 50 }, new[] { true, false }));
    }

    /// <summary>Duplicates in the input do not become duplicate cells.</summary>
    [Fact]
    public void RepeatedSettingsAreCollapsed()
    {
        var points = ParameterGridRunner.Build(
            new[] { 1000, 1000, 2000 }, new[] { 50, 50 }, new[] { true });

        Assert.Equal(2, points.Count);
    }

    // ------------------------------------------------------------- running

    /// <summary>
    /// One row per configuration, each carrying the validation it came from.
    /// </summary>
    [Fact]
    public void EveryConfigurationProducesARow()
    {
        var pool = Pool();
        var points = ParameterGridRunner.Build(new[] { 300, 500 }, new[] { 40, 60 }, new[] { true });

        var cells = ParameterGridRunner.Run(_ => pool, "Alpha", points);

        Assert.Equal(4, cells.Count);
        Assert.All(cells, c => Assert.False(c.Failed));
        Assert.All(cells, c => Assert.True(c.WorksValidated > 0));
    }

    /// <summary>
    /// The pool delegate is called with the folding setting, because accent
    /// folding happens at tokenisation and the two settings are two different
    /// token streams over the same rows. A grid that ignored this would run
    /// half its cells against the wrong text.
    /// </summary>
    [Fact]
    public void TheFoldingSettingReachesThePoolProvider()
    {
        var asked = new List<bool>();
        var pool = Pool();

        ParameterGridRunner.Run(
            fold => { asked.Add(fold); return pool; },
            "Alpha",
            ParameterGridRunner.Build(new[] { 300 }, new[] { 40 }, new[] { true, false }));

        Assert.Equal(new[] { true, false }, asked);
    }

    /// <summary>
    /// A configuration that cannot run is reported as one, not dropped. A gap
    /// in a grid reads as an untried combination rather than an impossible one,
    /// and the commonest cause - a sample size larger than any work the author
    /// has - is worth seeing rather than hiding.
    /// </summary>
    [Fact]
    public void AConfigurationThatCannotRunIsReportedNotDropped()
    {
        var points = ParameterGridRunner.Build(new[] { 300, 100000 }, new[] { 40 }, new[] { true });

        var cells = ParameterGridRunner.Run(_ => Pool(), "Alpha", points);

        Assert.Equal(2, cells.Count);
        var failed = Assert.Single(cells, c => c.Failed);
        Assert.Equal(100000, failed.Point.ChunkSize);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
    }

    // --------------------------------------------------- the drop-works trap

    /// <summary>
    /// THE TRAP THIS GRID EXISTS TO NOT FALL INTO. A larger sample size drops
    /// the shortest works, which compresses the length spread, which lowers rho
    /// for a reason that has nothing to do with the method improving.
    ///
    /// The cell that tested fewer works than its neighbours is marked, so a low
    /// rho on that row cannot be read as good news.
    /// </summary>
    [Fact]
    public void ACellThatTestedFewerWorksIsMarked()
    {
        // 900 is the shortest work, so a 1,000-token sample cannot test it
        // while a 300-token sample can.
        var points = ParameterGridRunner.Build(new[] { 300, 1000 }, new[] { 40 }, new[] { true });

        var cells = ParameterGridRunner.Run(_ => Pool(), "Alpha", points);

        var small = cells.Single(c => c.Point.ChunkSize == 300);
        var large = cells.Single(c => c.Point.ChunkSize == 1000);

        Assert.False(small.DroppedWorks);
        Assert.True(large.DroppedWorks);
        Assert.True(large.WorksValidated < small.WorksValidated);
        Assert.True(large.LengthSpread < small.LengthSpread);
    }

    /// <summary>
    /// And a marked cell is never trustworthy, however good its numbers look -
    /// the numbers are over a different set of works from the rest of the grid.
    /// </summary>
    [Fact]
    public void ACellThatDroppedWorksIsNeverTrustworthy()
    {
        var cell = new GridCell(
            new GridPoint(5000, 150, true, false),
            Recovered: 18, WorksValidated: 18, RecoveryRate: 1.0,
            MeanMargin: 0.2, MarginLengthCorrelation: 0.05,
            MarginSampleCountCorrelation: 0.05, LengthSpread: 2.5,
            PoolSeparation: 0.3, SampleCount: 40,
            Skipped: Array.Empty<string>())
        { DroppedWorks = true };

        Assert.False(cell.Trustworthy);
    }

    /// <summary>
    /// Nor is one where the works are all a similar length. A correlation needs
    /// spread on both axes; without it a low rho is uninformative rather than
    /// reassuring, and a grid sorted on rho would put those cells at the top
    /// and call them the best settings.
    /// </summary>
    [Fact]
    public void ACellWithNoLengthSpreadIsNeverTrustworthy()
    {
        var cell = new GridCell(
            new GridPoint(2500, 150, true, false),
            Recovered: 19, WorksValidated: 19, RecoveryRate: 1.0,
            MeanMargin: 0.2, MarginLengthCorrelation: 0.02,
            MarginSampleCountCorrelation: 0.02, LengthSpread: 1.1,
            PoolSeparation: 0.3, SampleCount: 40,
            Skipped: Array.Empty<string>());

        Assert.False(cell.Trustworthy);
    }

    /// <summary>A cell meeting all four conditions is.</summary>
    [Fact]
    public void ACellMeetingEveryConditionIsTrustworthy()
    {
        var cell = new GridCell(
            new GridPoint(2500, 150, true, false),
            Recovered: 19, WorksValidated: 19, RecoveryRate: 1.0,
            MeanMargin: 0.2, MarginLengthCorrelation: 0.12,
            MarginSampleCountCorrelation: 0.10, LengthSpread: 2.4,
            PoolSeparation: 0.3, SampleCount: 40,
            Skipped: Array.Empty<string>());

        Assert.True(cell.Trustworthy);
    }

    // ------------------------------------------------------------- reading

    /// <summary>
    /// When nothing passes, the summary says so and names the weakest
    /// correlation rather than presenting the least-bad cell as a
    /// recommendation.
    ///
    /// This is the expected outcome on the tragic corpus: across sample sizes
    /// 2,000 to 5,000 and feature counts 100 to 300, rho stayed between +0.46
    /// and +0.64 for Euripides. The confound is not a parameter choice there,
    /// and the summary should not imply it can be tuned away.
    /// </summary>
    [Fact]
    public void ASummaryWithNothingTrustworthySaysSo()
    {
        var cells = new[]
        {
            new GridCell(new GridPoint(2500, 150, true, false), 19, 19, 1.0,
                0.12, 0.64, 0.52, 2.43, 0.13, 83, Array.Empty<string>()),
            new GridCell(new GridPoint(3000, 300, true, false), 19, 19, 1.0,
                0.09, 0.49, 0.44, 2.43, 0.14, 70, Array.Empty<string>())
        };

        var summary = ParameterGridRunner.Summarise(cells);

        Assert.Contains("No configuration met", summary);
        Assert.Contains("+0.49", summary);
        Assert.Contains("+0.64", summary);
    }

    /// <summary>
    /// When nothing passes, the summary says so - and, when the corpus is too
    /// small to tell the cells apart, says THAT rather than naming a weakest.
    ///
    /// This is the outcome on the tragic corpus. Forty configurations returned
    /// rho +0.42 to +0.73, every one recovering 19/19. Naming the +0.42 cell
    /// reads as a finding and is not one: over nineteen works it carries a 95%
    /// band of about [-0.04, +0.73] while +0.73 carries [+0.41, +0.89], and the
    /// two overlap across most of their range. The visible spread of the grid
    /// fits inside the estimation error of any single cell in it.
    /// </summary>
    [Fact]
    public void ASummaryOverIndistinguishableCellsRefusesToNameAWinner()
    {
        var cells = new[]
        {
            new GridCell(new GridPoint(3000, 100, false, false), 19, 19, 1.0,
                0.154, 0.42, 0.40, 2.43, 0.168, 76, Array.Empty<string>()),
            new GridCell(new GridPoint(2500, 300, false, false), 19, 19, 1.0,
                0.078, 0.73, 0.71, 2.43, 0.090, 83, Array.Empty<string>())
        };

        var summary = ParameterGridRunner.Summarise(cells);

        Assert.Contains("not distinguishable", summary);
        Assert.Contains("Do not pick the top row", summary);
        Assert.DoesNotContain("The weakest was", summary);
    }

    /// <summary>
    /// With enough works for the bands to separate, it names the weakest
    /// instead - and still asks for a second run before the difference is
    /// believed.
    ///
    /// Both cells here sit ABOVE the 0.3 line, deliberately. The first draft of
    /// this test gave the weaker cell rho +0.05, which made it Trustworthy, so
    /// Summarise took the there-is-a-good-region branch and never reached the
    /// code under test. The two conditions are independent - a correlation can
    /// be too strong to trust and still be distinguishable from a stronger one
    /// - and a fixture has to keep them apart to test either.
    ///
    /// At 120 works, rho +0.35 spans [+0.18, +0.50] and +0.80 spans
    /// [+0.72, +0.86]. No overlap, and neither is weak enough to use.
    /// </summary>
    [Fact]
    public void ASummaryOverSeparableCellsNamesTheWeakestWithACaveat()
    {
        var cells = new[]
        {
            new GridCell(new GridPoint(3000, 100, false, false), 120, 120, 1.0,
                0.15, 0.35, 0.30, 2.43, 0.17, 400, Array.Empty<string>()),
            new GridCell(new GridPoint(2500, 300, false, false), 120, 120, 1.0,
                0.08, 0.80, 0.75, 2.43, 0.09, 420, Array.Empty<string>())
        };

        var summary = ParameterGridRunner.Summarise(cells);

        Assert.Contains("The weakest was", summary);
        Assert.Contains("second run", summary);
        Assert.DoesNotContain("not distinguishable", summary);
    }

    /// <summary>
    /// The interval itself, against values worked out by hand. A sign or
    /// sample-size error here would make the bench confidently rank noise.
    /// </summary>
    [Theory]
    [InlineData(0.42, 19, -0.04, 0.73)]
    [InlineData(0.73, 19, 0.41, 0.89)]
    [InlineData(0.05, 120, -0.13, 0.23)]
    public void TheCorrelationBandMatchesFishersTransform(
        double rho, int n, double expectedLow, double expectedHigh)
    {
        var (low, high) = ValidationResult.FisherInterval(rho, n);

        Assert.Equal(expectedLow, low, 2);
        Assert.Equal(expectedHigh, high, 2);
    }

    /// <summary>
    /// Too few works for the transform to mean anything gives a zero-width
    /// band rather than an exception or a nonsense range.
    /// </summary>
    [Fact]
    public void ATinySampleGivesNoBandRatherThanAWildOne()
    {
        var (low, high) = ValidationResult.FisherInterval(0.5, 4);

        Assert.Equal(0.5, low, 6);
        Assert.Equal(0.5, high, 6);
    }

    /// <summary>
    /// And when several do, the summary describes the REGION rather than
    /// crowning a row. A single best cell in a grid of forty is usually noise,
    /// and treating one as a discovery is the failure this bench exists to make
    /// harder.
    /// </summary>
    [Fact]
    public void ASummaryWithSeveralGoodCellsDescribesTheRegion()
    {
        var cells = new[]
        {
            new GridCell(new GridPoint(2500, 150, true, false), 19, 19, 1.0,
                0.12, 0.10, 0.08, 2.43, 0.13, 83, Array.Empty<string>()),
            new GridCell(new GridPoint(3000, 150, true, false), 19, 19, 1.0,
                0.13, 0.14, 0.11, 2.43, 0.14, 70, Array.Empty<string>()),
            new GridCell(new GridPoint(3000, 300, true, false), 19, 19, 1.0,
                0.09, 0.20, 0.18, 2.43, 0.14, 70, Array.Empty<string>())
        };

        var summary = ParameterGridRunner.Summarise(cells);

        Assert.Contains("3 of 3", summary);
        Assert.Contains("2500", summary);
        Assert.Contains("middle of that region", summary);
    }
}
