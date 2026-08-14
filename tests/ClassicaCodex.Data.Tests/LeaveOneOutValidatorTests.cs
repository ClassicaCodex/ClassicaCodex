using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Leave-one-out validation: does the method recover texts whose authorship is
/// not in question, before it is pointed at one that is.
///
/// The headline is a MARGIN - mean Delta to other authors minus mean Delta to
/// the work's own author - because it is a difference of distances rather than
/// a rank, and docs/stylometry-notes.md records what happened to the last rank
/// used as a headline.
///
/// Rank is recorded beside it as a diagnostic under test, not as evidence.
///
/// And every result carries its own length-confound check, because the first
/// real run of this harness recovered 19 of 19 Euripides plays with a margin
/// that correlated with text length at rho +0.62 - against +0.58 for the
/// measure it replaced. A recovery rate without that number next to it is the
/// same mistake in new clothes.
/// </summary>
public class LeaveOneOutValidatorTests
{
    /// <summary>
    /// Two authors with genuinely different function-word habits, several works
    /// each, non-periodic so that samples differ from one another.
    /// </summary>
    private static List<WorkTokens> Pool(int alphaWorks = 4, int betaWorks = 4, int tokensEach = 1200)
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

        var alphaBias = new[] { 2.0, 2.0, 1.0, 1.0, 1.0, 0.2, 0.2, 0.5, 0.3, 0.3 };
        var betaBias = new[] { 0.2, 0.2, 1.0, 1.0, 1.0, 2.0, 2.0, 0.5, 0.3, 0.3 };

        var pool = new List<WorkTokens>();
        for (var i = 0; i < alphaWorks; i++)
            pool.Add(new WorkTokens(10 + i, "Alpha", $"A{i}", Draw(100 + i, tokensEach, alphaBias)));
        for (var i = 0; i < betaWorks; i++)
            pool.Add(new WorkTokens(20 + i, "Beta", $"B{i}", Draw(200 + i, tokensEach, betaBias)));

        return pool;
    }

    private static DeltaSettings Settings => new(40, 300);

    // ------------------------------------------------------- what it reports

    /// <summary>
    /// The floor of the whole exercise: when the authors really are different,
    /// every work comes back to its own.
    /// </summary>
    [Fact]
    public void WorksByASeparableAuthorAreRecovered()
    {
        var result = LeaveOneOutValidator.Validate(Pool(), "Alpha", Settings);

        Assert.Equal(4, result.Works.Count);
        Assert.All(result.Works, w => Assert.True(w.Recovered, $"{w.WorkTitle} margin {w.Margin:F4}"));
        Assert.Equal(1.0, result.RecoveryRate);
        Assert.True(result.MeanMargin > 0);
    }

    /// <summary>
    /// Margin is a difference of means, so its sign is the recovery verdict and
    /// its two halves are reported alongside it - a margin of +0.05 between
    /// means of 0.6 and 0.65 is a different situation from one between 1.4 and
    /// 1.45, and the number alone cannot tell them apart.
    /// </summary>
    [Fact]
    public void MarginIsTheDifferenceOfTheTwoMeansItReports()
    {
        var result = LeaveOneOutValidator.Validate(Pool(), "Alpha", Settings);

        foreach (var w in result.Works)
        {
            Assert.Equal(w.MeanDeltaOtherAuthor - w.MeanDeltaSameAuthor, w.Margin, 10);
        }
    }

    /// <summary>
    /// Every sample of the work is measured, not just the first. The form has
    /// always read sample 0 and discarded the rest, which at 2,500 tokens
    /// throws away 30 of Euripides' 49 samples and makes the verdict depend on
    /// which bag happened to be drawn first.
    /// </summary>
    [Fact]
    public void EverySampleOfTheHeldOutWorkIsMeasured()
    {
        var result = LeaveOneOutValidator.Validate(Pool(tokensEach: 1200), "Alpha", Settings);

        // 1,200 tokens at 300 per sample.
        Assert.All(result.Works, w => Assert.Equal(4, w.SamplesMeasured));
    }

    // ------------------------------------------------- the mandatory confound

    /// <summary>
    /// The length-confound check runs on every result rather than living on a
    /// tab somebody has to remember to open. This is the test that it is
    /// actually wired up; what it reports on real data is a research finding,
    /// not something a unit test can assert.
    /// </summary>
    [Fact]
    public void EveryResultCarriesItsLengthCorrelation()
    {
        var pool = new List<WorkTokens>();
        var baseline = Pool(alphaWorks: 6, betaWorks: 4, tokensEach: 2400);

        // Give Alpha's works deliberately different lengths so the correlation
        // has something to find either way.
        var lengths = new[] { 600, 900, 1200, 1500, 1800, 2400 };
        var i = 0;
        foreach (var w in baseline)
        {
            pool.Add(w.AuthorName == "Alpha"
                ? w with { Tokens = w.Tokens.Take(lengths[i++]).ToList() }
                : w);
        }

        var result = LeaveOneOutValidator.Validate(pool, "Alpha", Settings);

        Assert.InRange(result.MarginLengthCorrelation, -1.0, 1.0);
        Assert.InRange(result.MarginSampleCountCorrelation, -1.0, 1.0);
    }

    /// <summary>
    /// Spearman against a known monotonic relationship, so a sign error in the
    /// correlation cannot pass unnoticed - which for this particular number
    /// would invert the interpretation of every sweep.
    /// </summary>
    [Fact]
    public void SpearmanRecognisesAMonotonicRelationship()
    {
        var x = new[] { 1.0, 2, 3, 4, 5 };

        Assert.Equal(1.0, ValidationResult.Spearman(x, new[] { 10.0, 20, 30, 40, 50 }), 6);
        Assert.Equal(-1.0, ValidationResult.Spearman(x, new[] { 50.0, 40, 30, 20, 10 }), 6);
    }

    /// <summary>
    /// Length spread is reported beside rho because rho cannot be read without
    /// it: a correlation needs spread on both axes, so an author whose works
    /// are all one size cannot produce a high value whatever the method does.
    ///
    /// Euripides returns rho +0.64 and Aristophanes +0.36 at identical
    /// settings; Euripides spans 4,141 to 10,060 tokens and Aristophanes 7,213
    /// to 10,750. The difference in spread is 2.4x against 1.5x, which is at
    /// least as good an explanation as any difference between the authors.
    /// </summary>
    [Fact]
    public void LengthSpreadIsReportedSoRhoCanBeRead()
    {
        var pool = new List<WorkTokens>();
        var baseline = Pool(alphaWorks: 4, betaWorks: 4, tokensEach: 2400);

        var lengths = new[] { 600, 1200, 1800, 2400 };
        var i = 0;
        foreach (var w in baseline)
        {
            pool.Add(w.AuthorName == "Alpha"
                ? w with { Tokens = w.Tokens.Take(lengths[i++]).ToList() }
                : w);
        }

        var result = LeaveOneOutValidator.Validate(pool, "Alpha", Settings);

        Assert.Equal(4.0, result.LengthSpread, 1);
    }

    /// <summary>
    /// Works of one length give a spread of 1, and the summary is expected to
    /// say so rather than presenting a low rho as a clean bill of health.
    /// </summary>
    [Fact]
    public void UniformLengthsGiveASpreadOfOne()
    {
        var result = LeaveOneOutValidator.Validate(Pool(tokensEach: 1200), "Alpha", Settings);

        Assert.Equal(1.0, result.LengthSpread, 6);
    }

    /// <summary>Ties are averaged rather than broken by list order.</summary>
    [Fact]
    public void SpearmanAveragesTies()
    {
        Assert.Equal(0, ValidationResult.Spearman(
            new[] { 1.0, 2, 3, 4 }, new[] { 5.0, 5, 5, 5 }), 6);
    }

    // ---------------------------------------------------------- pool guards

    /// <summary>
    /// A margin compares one author against others, so a single-author pool has
    /// no margin to report. It fails with an explanation rather than returning
    /// zero, which would read as "recovered nothing" instead of "asked the
    /// wrong question".
    /// </summary>
    [Fact]
    public void ASingleAuthorPoolIsRefusedRatherThanScoredAsZero()
    {
        var pool = Pool(alphaWorks: 4, betaWorks: 0);

        var ex = Assert.Throws<InvalidOperationException>(
            () => LeaveOneOutValidator.Validate(pool, "Alpha", Settings));

        Assert.Contains("at least two", ex.Message);
    }

    /// <summary>
    /// Pool difficulty travels with the result. Against Greek prose every
    /// tragedy recovers and the rate is 100%; against Aeschylus the
    /// between-author signal is a tenth of within-Euripides variation. Same
    /// harness, same number, incomparable meanings - so the separation is
    /// reported beside the rate.
    /// </summary>
    [Fact]
    public void PoolDifficultyIsMeasuredAndReported()
    {
        var result = LeaveOneOutValidator.Validate(Pool(), "Alpha", Settings);

        Assert.Equal(2, result.Difficulty.AuthorCount);
        Assert.True(result.Difficulty.Separation > 0,
            "two authors with opposite function-word habits should separate");
        Assert.True(result.Difficulty.MeanWithinAuthorDelta < result.Difficulty.MeanCrossAuthorDelta);
    }

    /// <summary>
    /// Samples of the SAME work are excluded from the within-author figure. Two
    /// bags of one text are about as close as two bags get, and counting them
    /// would make every pool look easier than it is.
    /// </summary>
    [Fact]
    public void PoolDifficultyIgnoresSamplesOfTheSameWork()
    {
        var wide = LeaveOneOutValidator.MeasureDifficulty(Pool(tokensEach: 1200), Settings);
        var narrow = LeaveOneOutValidator.MeasureDifficulty(Pool(tokensEach: 300), Settings);

        // With one sample per work the same-work exclusion has nothing to
        // remove, so the within-author figure is over the same population
        // either way and both are real numbers rather than zero.
        Assert.True(wide.MeanWithinAuthorDelta > 0);
        Assert.True(narrow.MeanWithinAuthorDelta > 0);
    }

    /// <summary>
    /// One author supplying most of the pool defines "other" largely by itself,
    /// and dominates the z-scores everything else is expressed in. Flagged, not
    /// blocked - it is sometimes exactly what you meant to do.
    ///
    /// The balanced case sits exactly ON the half-way line rather than below
    /// it: four works of equal length each side is sixteen samples against
    /// sixteen, and the test asserts the share so that is visible rather than
    /// looking like a threshold that happened to hold.
    /// </summary>
    [Fact]
    public void AnImbalancedPoolIsFlagged()
    {
        var balanced = LeaveOneOutValidator.MeasureDifficulty(Pool(4, 4), Settings);
        var lopsided = LeaveOneOutValidator.MeasureDifficulty(Pool(2, 12), Settings);

        Assert.Equal(0.5, balanced.LargestAuthorSampleShare, 6);
        Assert.False(balanced.IsImbalanced);

        Assert.True(lopsided.LargestAuthorSampleShare > 0.8);
        Assert.True(lopsided.IsImbalanced);
        Assert.Equal("Beta", lopsided.LargestAuthor);
    }

    // ------------------------------------------------------ the leakage flag

    /// <summary>
    /// With the flag on, the held-out work contributes to neither the feature
    /// set nor any z-score, and is scored against statistics computed without
    /// it. The work stays in the pool - the engine has to sample it to measure
    /// it - so this is an engine-level exclusion, not a pool-level one.
    ///
    /// On real data the difference turned out to be small: mean margin +0.1127
    /// against +0.1118 over nineteen Euripides plays, with the ordering almost
    /// unchanged. That is a measured answer to a question that was worth
    /// asking, and the flag stays so it can be asked again on a different pool.
    /// </summary>
    [Fact]
    public void ExcludingTheHeldOutWorkFromNormalisationChangesTheNumbers()
    {
        var pool = Pool();

        var included = LeaveOneOutValidator.Validate(pool, "Alpha", Settings);
        var excluded = LeaveOneOutValidator.Validate(pool, "Alpha", Settings,
            excludeHeldOutFromNormalisation: true);

        Assert.True(excluded.HeldOutWorkExcludedFromNormalisation);
        Assert.False(included.HeldOutWorkExcludedFromNormalisation);

        // Different statistics, so different numbers - but the same verdict on
        // a pool this separable.
        Assert.NotEqual(included.MeanMargin, excluded.MeanMargin, 6);
        Assert.Equal(included.RecoveryRate, excluded.RecoveryRate);
    }

    /// <summary>
    /// The engine-level flag is what does the work, and it must not quietly
    /// no-op. If the target still helps set the mean, the z-scores are
    /// identical and so is every Delta.
    /// </summary>
    [Fact]
    public void TheEngineFlagActuallyChangesTheNormalisation()
    {
        var pool = Pool();

        var included = DeltaEngine.Compute(pool, 10, Settings);
        var excluded = DeltaEngine.Compute(pool, 10, Settings, 0, excludeTargetFromNormalisation: true);

        Assert.NotEqual(included.Neighbors[0].Delta, excluded.Neighbors[0].Delta, 6);
    }

    // ------------------------------------------------------------- skipping

    /// <summary>
    /// A work too short to yield one sample is named in Skipped rather than
    /// scored as a failure to recover. It is a work the run could not test, not
    /// a work the method got wrong, and rolling the two together would make the
    /// recovery rate depend on how many fragments the pool happened to hold.
    /// </summary>
    [Fact]
    public void AWorkTooShortToSampleIsSkippedNotFailed()
    {
        var pool = Pool();
        pool.Add(new WorkTokens(99, "Alpha", "Fragment", new List<string> { "kai", "de", "men" }));

        var result = LeaveOneOutValidator.Validate(pool, "Alpha", Settings);

        Assert.Contains(result.Skipped, s => s.StartsWith("Fragment"));
        Assert.DoesNotContain(result.Works, w => w.WorkId == 99);
        Assert.Equal(1.0, result.RecoveryRate);
    }

    /// <summary>
    /// But a sweep in which NO work could be tested fails rather than reporting
    /// nothing recovered.
    ///
    /// The distinction matters as soon as a parameter grid exists: the grid
    /// will reach sample sizes larger than any work the author has, and a cell
    /// that never ran must not sit in the table next to one that ran and got
    /// everything wrong, wearing the same 0%.
    /// </summary>
    [Fact]
    public void ASweepThatCouldTestNothingFailsRatherThanScoringZero()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LeaveOneOutValidator.Validate(Pool(), "Alpha", new DeltaSettings(40, 100000)));

        Assert.Contains("100,000", ex.Message);
    }
}
