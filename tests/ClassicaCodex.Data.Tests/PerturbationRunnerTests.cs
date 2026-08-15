using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Controlled contamination: how much deliberate stylistic disturbance does the
/// method absorb before it stops recognising a text whose authorship is not in
/// question?
///
/// WHY THIS EXPERIMENT AND NOT ANOTHER. Every margin the bench produces
/// correlates with text length - rho +0.42 to +0.73 across forty parameter
/// configurations, with no region escaping it - so margins of different works
/// cannot be compared. Perturbation sidesteps that entirely: one work, held
/// fixed, with only the contamination varying. In Replace mode the token count
/// does not move either, so the confound is held constant rather than argued
/// away.
///
/// WHAT THE ANSWER MEANS. That the METHOD absorbs a given amount of
/// disturbance. Not that any real text contains that proportion of anyone
/// else's writing - genre, chronology, transmission and a bad edition all
/// produce disturbance too, and nothing here tells them apart from a second
/// hand.
/// </summary>
public class PerturbationRunnerTests
{
    private static readonly string[] Vocab =
        { "men", "de", "kai", "ho", "to", "gar", "oun", "te", "alla", "hos" };

    private static readonly double[] AlphaBias = { 2.0, 2.0, 1.0, 1.0, 1.0, 0.2, 0.2, 0.5, 0.3, 0.3 };
    private static readonly double[] BetaBias = { 0.2, 0.2, 1.0, 1.0, 1.0, 2.0, 2.0, 0.5, 0.3, 0.3 };

    private static List<string> Draw(int seed, int count, double[] bias)
    {
        var rng = new Random(seed);
        var weights = Vocab.Select((_, i) => bias[i] + rng.NextDouble() * 0.2).ToArray();
        var total = weights.Sum();
        var tokens = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var pick = rng.NextDouble() * total;
            var w = 0;
            while (w < Vocab.Length - 1 && pick > weights[w]) pick -= weights[w++];
            tokens.Add(Vocab[w]);
        }

        return tokens;
    }

    private static List<WorkTokens> Pool()
    {
        var pool = new List<WorkTokens>();
        for (var i = 0; i < 4; i++)
            pool.Add(new WorkTokens(10 + i, "Alpha", $"A{i}", Draw(100 + i, 2400, AlphaBias)));
        for (var i = 0; i < 4; i++)
            pool.Add(new WorkTokens(20 + i, "Beta", $"B{i}", Draw(200 + i, 2400, BetaBias)));
        return pool;
    }

    private static DeltaSettings Settings => new(40, 600);
    private static int[] BetaDonors => new[] { 20, 21, 22, 23 };

    // ------------------------------------------------------------- mixing

    /// <summary>
    /// Replace holds the token count exactly constant, which is the whole
    /// reason it is the default: a margin that correlates with length cannot be
    /// read across an experiment that changes length.
    /// </summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.10)]
    [InlineData(0.50)]
    public void ReplacingLeavesTheTokenCountUntouched(double fraction)
    {
        var target = Draw(1, 1000, AlphaBias);
        var donor = Draw(2, 1000, BetaBias);

        var mixed = PerturbationRunner.Mix(target, donor, fraction, InjectionMode.Replace, 7, 0);

        Assert.Equal(target.Count, mixed.Count);
    }

    /// <summary>Exactly the requested proportion changes, not approximately.</summary>
    [Fact]
    public void ReplacingChangesExactlyTheRequestedProportion()
    {
        // A target and donor with disjoint vocabularies, so every changed
        // position is identifiable.
        var target = Enumerable.Repeat("alpha", 1000).ToList();
        var donor = Enumerable.Repeat("beta", 500).ToList();

        var mixed = PerturbationRunner.Mix(target, donor, 0.10, InjectionMode.Replace, 7, 0);

        Assert.Equal(100, mixed.Count(t => t == "beta"));
        Assert.Equal(900, mixed.Count(t => t == "alpha"));
    }

    /// <summary>And Add grows it by that proportion instead.</summary>
    [Fact]
    public void AddingGrowsTheWork()
    {
        var target = Enumerable.Repeat("alpha", 1000).ToList();
        var donor = Enumerable.Repeat("beta", 500).ToList();

        var mixed = PerturbationRunner.Mix(target, donor, 0.10, InjectionMode.Add, 7, 0);

        Assert.Equal(1100, mixed.Count);
        Assert.Equal(1000, mixed.Count(t => t == "alpha"));
    }

    /// <summary>
    /// A mixture is a deterministic function of its seed and its iteration
    /// number - not of a running generator. So trial 17 of a fifty-iteration
    /// series is the same text whether or not trials 1 to 16 were computed,
    /// which is what makes the one outlier in a series inspectable afterwards.
    /// </summary>
    [Fact]
    public void AMixtureCanBeRebuiltFromItsSeedAndIterationAlone()
    {
        var target = Draw(1, 800, AlphaBias);
        var donor = Draw(2, 800, BetaBias);

        var seventeenth = PerturbationRunner.Mix(target, donor, 0.1, InjectionMode.Replace, 42, 17);
        var again = PerturbationRunner.Mix(target, donor, 0.1, InjectionMode.Replace, 42, 17);
        var different = PerturbationRunner.Mix(target, donor, 0.1, InjectionMode.Replace, 42, 18);

        Assert.Equal(seventeenth, again);
        Assert.NotEqual(seventeenth, different);
    }

    /// <summary>
    /// AND ACROSS PROCESSES, which the test above cannot check.
    ///
    /// The first draft combined seed and iteration with HashCode.Combine, which
    /// is seeded from a per-process random value by design - so it gives
    /// different answers in different runs of the program. Mixtures would have
    /// been reproducible within a session and irreproducible across one, which
    /// is precisely the opposite of what a stored seed is for, and the test
    /// above would have passed the whole time.
    ///
    /// These are the literal outputs of the deterministic combiner. If someone
    /// swaps it for something process-seeded again, this fails on the second
    /// run rather than never.
    /// </summary>
    [Theory]
    [InlineData(42, 0, 42000143)]
    [InlineData(42, 17, 42000670)]
    [InlineData(0, 0, 17)]
    [InlineData(99, 3, 99000407)]
    public void TheSeedCombinerIsDeterministicAcrossProcesses(int seed, int iteration, int expected)
    {
        // Mix is the only caller, so the combiner is exercised through the
        // sequence it produces: a fixed seed must yield a fixed draw.
        var target = Enumerable.Repeat("alpha", 100).ToList();
        var donor = Enumerable.Range(0, 50).Select(i => $"d{i}").ToList();

        var mixed = PerturbationRunner.Mix(target, donor, 0.2, InjectionMode.Replace, seed, iteration);

        // Reproduce the expected draw independently of the runner.
        var rng = new Random(expected);
        var drawn = Enumerable.Range(0, 20).Select(_ => donor[rng.Next(donor.Count)]).ToList();

        foreach (var token in drawn.Distinct())
            Assert.Contains(token, mixed);
    }

    /// <summary>Zero injection is the untouched text, not a rounding of it.</summary>
    [Fact]
    public void ZeroInjectionLeavesTheTextAlone()
    {
        var target = Draw(1, 800, AlphaBias);

        Assert.Equal(target, PerturbationRunner.Mix(target, Draw(2, 800, BetaBias),
            0, InjectionMode.Replace, 42, 0));
    }

    // ------------------------------------------------------------ running

    /// <summary>
    /// Cross-author contamination pulls the margin down, and does so
    /// monotonically. On real text: 100% of baseline at 0% injection, 93% at
    /// 5%, 78% at 20%, 29% at 50%.
    /// </summary>
    [Fact]
    public void CrossAuthorContaminationReducesTheMargin()
    {
        var pool = Pool();
        var baseline = PerturbationRunner.Baseline(pool, 10, Settings);

        var heavy = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, BetaDonors, 0.40, InjectionMode.Replace, 42, 6),
            Settings, baseline);

        Assert.True(heavy.MeanMargin < baseline,
            $"expected contamination to reduce the margin: {heavy.MeanMargin:F4} vs {baseline:F4}");
        Assert.True(heavy.ProportionOfBaseline < 1);
    }

    /// <summary>
    /// THE CONTROL, and the result that makes the rest of it mean anything.
    ///
    /// Contaminating a work with MORE OF ITS OWN AUTHOR moves the margin the
    /// other way - on real text, up to 137% of baseline at 50% injection,
    /// against 29% for a cross-author donor. The work is pulled towards its
    /// author's centre and away from its own idiosyncrasy.
    ///
    /// Without this the falling curve would be uninterpretable: any large
    /// change to a text might reduce a margin simply by being a change. The two
    /// curves diverging is what shows the measure responds to whose material it
    /// is, not to how much of it moved.
    /// </summary>
    [Fact]
    public void SameAuthorContaminationDoesNotReduceTheMargin()
    {
        var pool = Pool();
        var baseline = PerturbationRunner.Baseline(pool, 10, Settings);

        var sameAuthor = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, new[] { 11, 12, 13 }, 0.40, InjectionMode.Replace, 42, 6),
            Settings, baseline);

        var crossAuthor = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, BetaDonors, 0.40, InjectionMode.Replace, 42, 6),
            Settings, baseline);

        Assert.True(sameAuthor.MeanMargin > crossAuthor.MeanMargin,
            $"same-author donor {sameAuthor.MeanMargin:F4} should not disturb more than " +
            $"cross-author {crossAuthor.MeanMargin:F4}");
    }

    /// <summary>
    /// Repeated sampling reports a distribution. One mixture is one draw and
    /// says almost nothing; the question is how often contamination at a level
    /// breaks recovery, not whether it broke it the once.
    /// </summary>
    [Fact]
    public void RepeatedIterationsProduceADistribution()
    {
        var level = PerturbationRunner.RunLevel(
            Pool(),
            new PerturbationConfig(10, BetaDonors, 0.20, InjectionMode.Replace, 42, 8),
            Settings, 0.2);

        Assert.Equal(8, level.Trials.Count);
        Assert.True(level.MarginStdDev > 0, "eight different mixtures should not all agree exactly");
        Assert.True(level.MarginPercentile(0.1) <= level.MarginPercentile(0.9));
    }

    /// <summary>
    /// At zero injection every iteration would build the same text, so one
    /// trial is the whole distribution. Running fifty identical mixtures would
    /// report a standard deviation of zero as though it had been measured.
    /// </summary>
    [Fact]
    public void ZeroInjectionRunsOnceHoweverManyIterationsWereAskedFor()
    {
        var level = PerturbationRunner.RunLevel(
            Pool(),
            new PerturbationConfig(10, BetaDonors, 0, InjectionMode.Replace, 42, 50),
            Settings, 0.2);

        Assert.Single(level.Trials);
    }

    /// <summary>
    /// The whole series is reproducible from its seed, which is the point of
    /// storing one: a synthetic text that cannot be regenerated cannot be
    /// checked by anybody, including whoever made it.
    /// </summary>
    [Fact]
    public void TheWholeSeriesIsReproducibleFromItsSeed()
    {
        var pool = Pool();
        var levels = new[] { 0.0, 0.1, 0.3 };

        var first = PerturbationRunner.RunSeries(pool, 10, BetaDonors, levels,
            InjectionMode.Replace, 99, 4, Settings);
        var second = PerturbationRunner.RunSeries(pool, 10, BetaDonors, levels,
            InjectionMode.Replace, 99, 4, Settings);

        Assert.Equal(
            first.Select(l => (l.InjectionFraction, l.MeanMargin)),
            second.Select(l => (l.InjectionFraction, l.MeanMargin)));
    }

    // ------------------------------------------------------------ reading

    /// <summary>
    /// The proportional threshold, which is the statistic a series is for.
    /// </summary>
    [Fact]
    public void TheLevelWhereHalfTheMarginIsGoneIsFound()
    {
        var levels = new[]
        {
            new PerturbationLevel(0.00, new[] { Trial(0.100) }, 0.100),
            new PerturbationLevel(0.10, new[] { Trial(0.080) }, 0.100),
            new PerturbationLevel(0.30, new[] { Trial(0.045) }, 0.100),
            new PerturbationLevel(0.50, new[] { Trial(0.020) }, 0.100)
        };

        Assert.Equal(0.30, PerturbationRunner.LevelWhereMarginFallsBelow(levels, 0.5));
    }

    /// <summary>
    /// And the sign test, which on real text does not fire at all - a Euripides
    /// play carrying 50% synthetic Sophocles stayed positive in twelve trials
    /// out of twelve, at 29% of its uncontaminated margin. A text can lose
    /// seven-tenths of its authorial signal and still count as recovered, which
    /// is why the proportional threshold is the headline and this is not.
    /// </summary>
    [Fact]
    public void RecoveryNeverBreakingIsReportedAsSuchRatherThanAsZero()
    {
        var levels = new[]
        {
            new PerturbationLevel(0.00, new[] { Trial(0.100) }, 0.100),
            new PerturbationLevel(0.50, new[] { Trial(0.029) }, 0.100)
        };

        Assert.Null(PerturbationRunner.BreakingPoint(levels));

        var summary = PerturbationRunner.Summarise(levels, "Medea");
        Assert.Contains("never became unreliable", summary);
        Assert.Contains("blunt test", summary);
    }

    /// <summary>
    /// The summary carries the caveat, because that sentence is the one most
    /// likely to be quoted away from the tool that produced it.
    /// </summary>
    [Fact]
    public void TheSummaryRefusesToReadTheNumberAsAnAuthorshipEstimate()
    {
        var levels = new[]
        {
            new PerturbationLevel(0.00, new[] { Trial(0.100) }, 0.100),
            new PerturbationLevel(0.20, new[] { Trial(0.078) }, 0.100)
        };

        var summary = PerturbationRunner.Summarise(levels, "Medea");

        Assert.Contains("not an estimate of how much of any real text", summary);
    }

    /// <summary>
    /// A rise is reported as a rise. The same-author control produces one, and
    /// a summary that described it as a fall of -37% would bury the control's
    /// entire point.
    /// </summary>
    [Fact]
    public void ARisingCurveIsDescribedAsRising()
    {
        var levels = new[]
        {
            new PerturbationLevel(0.00, new[] { Trial(0.082) }, 0.082),
            new PerturbationLevel(0.50, new[] { Trial(0.112) }, 0.082)
        };

        Assert.Contains("ROSE", PerturbationRunner.Summarise(levels, "Medea"));
    }

    /// <summary>
    /// The shift in standard deviations, which is what separates a small real
    /// effect from noise. The percentage column cannot: at 20% Aeschylus,
    /// Alcestis read 98% of baseline and Rhesus 80%, but Alcestis had moved
    /// -0.10 SD and Rhesus -2.27, so only one of them moved at all.
    /// </summary>
    [Theory]
    // Alcestis, 20% Aeschylus: 98% of baseline and no effect.
    [InlineData(0.061, 0.060, 0.0101, -0.10)]
    // Rhesus, 20% Aeschylus: 80% of baseline and a real fall.
    [InlineData(0.097, 0.077, 0.0088, -2.27)]
    // Rhesus, both donors at 20%: further still.
    [InlineData(0.097, 0.070, 0.0094, -2.87)]
    // Alcestis, 20% same-author control: a real RISE, so positive.
    [InlineData(0.061, 0.082, 0.0098, 2.14)]
    public void TheShiftIsMeasuredInTheMixingNoiseOfItsOwnLevel(
        double baseline, double mean, double stdDev, double expected)
    {
        // Two trials placed symmetrically about the mean reproduce that mean
        // and that population standard deviation exactly.
        var level = new PerturbationLevel(
            0.20,
            new[] { Trial(mean - stdDev), Trial(mean + stdDev) },
            baseline);

        Assert.Equal(mean, level.MeanMargin, 6);
        Assert.Equal(stdDev, level.MarginStdDev, 6);
        Assert.Equal(expected, level.ShiftInStdDevs, 2);
    }

    /// <summary>
    /// At zero injection there is one trial and no spread, so there is no
    /// noise to measure a shift against. Reported as zero rather than as an
    /// infinity or a divide-by-zero.
    /// </summary>
    [Fact]
    public void ALevelWithNoSpreadReportsNoShift()
    {
        var level = new PerturbationLevel(0, new[] { Trial(0.1) }, 0.1);

        Assert.Equal(0, level.ShiftInStdDevs);
    }

    /// <summary>
    /// The absolute drop, which is the only one of the three columns that
    /// compares across works.
    ///
    /// At 20% injection with the same donors and settings, four Euripides
    /// plays gave: Heracleidae -0.011, Rhesus -0.027, Hecuba -0.030, Helen
    /// -0.027. Read as percentages of their own baselines that is 60, 73, 76,
    /// 81 - Heracleidae responding most. Read in standard deviations it is
    /// -1.17, -2.84, -2.92, -3.39 - Heracleidae responding least. The orders
    /// are exactly reversed, because one divides by the baseline and the other
    /// by the noise. In Delta three of the four lose the same amount and
    /// Heracleidae loses a third of it, which is the actual result.
    /// </summary>
    [Theory]
    [InlineData(0.026, 0.015, -0.011)]
    [InlineData(0.097, 0.070, -0.027)]
    [InlineData(0.128, 0.098, -0.030)]
    [InlineData(0.142, 0.115, -0.027)]
    public void TheAbsoluteDropIsWhatComparesAcrossWorks(
        double baseline, double mean, double expected)
    {
        var level = new PerturbationLevel(0.20, new[] { Trial(mean) }, baseline);

        Assert.Equal(expected, level.AbsoluteShift, 3);
    }

    /// <summary>
    /// Headroom bounds the shift measure, which is why a fixed threshold on it
    /// is the wrong rule.
    ///
    /// A margin cannot fall past zero, so the largest shift a work can show is
    /// its baseline divided by its mixing noise. Heracleidae sits 3.0 SD above
    /// zero and Helen 17.7 - so "under two SD, nothing moved" is nearly
    /// unreachable for the first and trivially cleared by the second, and the
    /// summary now reports the ceiling next to the shift instead.
    /// </summary>
    [Theory]
    [InlineData(0.026, 0.0088, 3.0)]
    [InlineData(0.097, 0.0094, 10.3)]
    [InlineData(0.142, 0.0080, 17.7)]
    public void HeadroomBoundsHowFarTheShiftCanGo(
        double baseline, double stdDev, double expected)
    {
        var level = new PerturbationLevel(
            0.20, new[] { Trial(baseline - stdDev), Trial(baseline + stdDev) }, baseline);

        Assert.Equal(expected, level.HeadroomInStdDevs, 1);
    }

    /// <summary>
    /// The chunk cache must change speed and nothing else.
    ///
    /// It exists because a perturbation series calls Compute around 375 times
    /// for one work at the coarse preset, and each call re-split the entire
    /// pool though only the target's tokens had changed - a quarter of a
    /// million tokens shuffled, bagged, discarded and redone, per call. Across
    /// nineteen works that is billions of token copies.
    ///
    /// Safe by construction rather than by care: SplitIntoChunks is
    /// deterministic in (tokens, chunk size, work id), so a cached bag is the
    /// bag that would have been recomputed. This is the test that says so.
    /// </summary>
    [Fact]
    public void TheChunkCacheChangesNothingAboutTheResult()
    {
        var pool = Pool();
        var cache = DeltaEngine.ChunkWorks(pool.Where(w => w.WorkId != 10).ToList(), Settings.ChunkSize);

        var uncached = DeltaEngine.Compute(pool, 10, Settings);
        var cached = DeltaEngine.Compute(pool, 10, Settings, 0, false, cache);

        Assert.Equal(
            uncached.Neighbors.Select(n => (n.Label, n.Delta)),
            cached.Neighbors.Select(n => (n.Label, n.Delta)));
        Assert.Equal(uncached.SampleCount, cached.SampleCount);
        Assert.Equal(uncached.DiscardedTokens, cached.DiscardedTokens);
    }

    /// <summary>
    /// And the target is never taken from the cache, however it got in there.
    /// A perturbation experiment changes the target's tokens on every
    /// iteration, so a stale bag would silently measure the uncontaminated
    /// text - the whole series would come back flat and look like a finding.
    /// </summary>
    [Fact]
    public void TheTargetIsNeverReadFromTheCache()
    {
        var pool = Pool();

        // A cache built from the ORIGINAL pool, including the target.
        var staleCache = DeltaEngine.ChunkWorks(pool, Settings.ChunkSize);

        // Now change the target beyond recognition.
        var contaminated = pool
            .Select(w => w.WorkId == 10
                ? w with { Tokens = Enumerable.Repeat("zzz", w.Tokens.Count).ToList() }
                : w)
            .ToList();

        var withStale = DeltaEngine.Compute(contaminated, 10, Settings, 0, false, staleCache);
        var withNone = DeltaEngine.Compute(contaminated, 10, Settings);

        Assert.Equal(
            withNone.Neighbors.Select(n => n.Delta),
            withStale.Neighbors.Select(n => n.Delta));
    }

    private static PerturbationTrial Trial(double margin) =>
        new(0, margin, 0.8, "Alpha", "Alpha, A1", margin > 0, 2400);

    // ------------------------------------------------ comparing works

    /// <summary>
    /// The real sweep: nineteen Euripides plays contaminated with Sophocles at
    /// 20%, seed 42, 25 iterations. These are the measured drops.
    /// </summary>
    private static List<(string Title, double Baseline, double Drop)> TheSophoclesSweep() => new()
    {
        ("Alcestis", 0.061, -0.019), ("Andromache", 0.141, -0.035), ("Bacchae", 0.137, -0.031),
        ("Cyclops", 0.091, -0.026), ("Electra", 0.130, -0.032), ("Hecuba", 0.128, -0.036),
        ("Helen", 0.142, -0.033), ("Heracleidae", 0.026, -0.018), ("Heracles", 0.145, -0.034),
        ("Hippolytus", 0.084, -0.025), ("Ion", 0.144, -0.030), ("Iphigenia in Aulis", 0.130, -0.031),
        ("Iphigenia in Tauris", 0.139, -0.035), ("Medea", 0.085, -0.026), ("Orestes", 0.141, -0.035),
        ("Rhesus", 0.097, -0.030), ("Suppliants", 0.100, -0.027),
        ("Phoenician Women", 0.147, -0.035), ("Trojan Women", 0.130, -0.033)
    };

    /// <summary>
    /// Drop is not independent of baseline, which is the whole reason the
    /// residual ranking exists. Works with more margin to lose lose more of it.
    /// </summary>
    [Fact]
    public void DropTracksBaselineMarginAcrossWorks()
    {
        var comparison = PerturbationRunner.CompareWorks(TheSophoclesSweep());

        Assert.InRange(comparison.BaselineDropCorrelation, 0.70, 0.80);
    }

    /// <summary>
    /// THE RESULT THIS RANKING WAS BUILT TO CORRECT. On raw drop, Heracleidae is
    /// the furthest-out work at 3.3 median absolute deviations - and it has the
    /// lowest baseline margin in the corpus. Once the baseline effect is fitted
    /// out it sits at about 1.2, which is unremarkable.
    /// </summary>
    [Fact]
    public void HeracleidaeIsOrdinaryOnceBaselineIsAccountedFor()
    {
        var comparison = PerturbationRunner.CompareWorks(TheSophoclesSweep());

        var heracleidae = comparison.Works.Single(w => w.Title == "Heracleidae");

        Assert.InRange(heracleidae.DeviationsFromTypical, 0.8, 1.8);
        Assert.DoesNotContain(comparison.Outliers(), o => o.Title == "Heracleidae");
    }

    /// <summary>
    /// And the works the residual does put furthest out are different ones -
    /// Hecuba dropping more than its baseline predicts, Ion less. Both are
    /// around four MAD, which on nineteen points is not much: with the
    /// per-level mean carrying a standard error near 0.002, a residual of 0.004
    /// is about two standard errors and one or two of those are expected by
    /// chance.
    /// </summary>
    [Fact]
    public void TheResidualPutsDifferentWorksAtTheExtremes()
    {
        var comparison = PerturbationRunner.CompareWorks(TheSophoclesSweep());

        var furthest = comparison.Works
            .OrderByDescending(w => w.DeviationsFromTypical)
            .Take(2)
            .Select(w => w.Title)
            .ToList();

        Assert.Contains("Hecuba", furthest);
        Assert.Contains("Ion", furthest);
    }

    /// <summary>
    /// The disagreement between the two rankings is itself reported, because it
    /// is the signal that the raw column was reading baseline rather than
    /// response.
    /// </summary>
    [Fact]
    public void TheDisagreementBetweenRankingsIsFlagged()
    {
        Assert.True(PerturbationRunner.CompareWorks(TheSophoclesSweep()).RawRankingIsMisleading);
    }

    /// <summary>
    /// A sweep where every work responds in proportion to its baseline has no
    /// outliers, and says so rather than naming whichever work is nearest the
    /// end of the list. A rank always names somebody.
    /// </summary>
    [Fact]
    public void APerfectlyOrdinarySweepReportsNoOutliers()
    {
        var works = Enumerable.Range(1, 10)
            .Select(i => ($"W{i}", 0.02 * i, -(0.010 + 0.15 * 0.02 * i)))
            .ToList();

        Assert.Empty(PerturbationRunner.CompareWorks(works).Outliers());
    }

    /// <summary>
    /// Three median absolute deviations sounds like a strict threshold and is
    /// not: sigma is 1.4826 x MAD, so three MAD is about two sigma, which one
    /// work in twenty-three clears by chance.
    ///
    /// Over nineteen works that is 0.8 expected false flags per sweep. A sweep
    /// flagging one work has found nothing; the Aeschylus sweep flagged three,
    /// which against 0.8 is about p 0.05 for the sweep as a whole and is still
    /// not a licence to believe any one of them.
    ///
    /// Reported rather than used to move the threshold - a threshold tuned
    /// until the answer looks significant is the failure this bench exists to
    /// make harder.
    /// </summary>
    [Fact]
    public void TheThresholdReportsHowManyFlagsChanceAloneWouldProduce()
    {
        var comparison = PerturbationRunner.CompareWorks(TheSophoclesSweep());

        // 19 works, three MAD = 2.02 sigma, two-sided p 0.043.
        Assert.Equal(0.82, comparison.ExpectedFalseFlags(3), 2);

        // And a stricter threshold costs far fewer.
        Assert.True(comparison.ExpectedFalseFlags(5) < 0.02);
    }

    /// <summary>
    /// The robust sigma is the MAD scaled so it agrees with a standard
    /// deviation on normal data, which is what makes the conversion above
    /// meaningful.
    /// </summary>
    [Fact]
    public void RobustSigmaScalesTheMedianAbsoluteDeviation()
    {
        var comparison = PerturbationRunner.CompareWorks(TheSophoclesSweep());

        Assert.Equal(comparison.ResidualMad * 1.4826, comparison.RobustSigma, 10);
    }

    // ------------------------------------------------- detection power

    /// <summary>
    /// What a sweep could have found, which is what a null result is worthless
    /// without.
    ///
    /// The measured values from nineteen Euripides plays contaminated with
    /// Aeschylus and Sophocles: genuine works scatter around the length line by
    /// 0.031, and contamination moves them by the amounts below. At 20% that is
    /// three quarters of one deviation - an AUC of 0.70, meaning this method
    /// ranks a clean play above a heavily contaminated one seven times in ten.
    /// At 5% it is a coin flip.
    /// </summary>
    [Theory]
    [InlineData(0.01, -0.00104, 0.51)]
    [InlineData(0.05, -0.00542, 0.55)]
    [InlineData(0.10, -0.01106, 0.60)]
    [InlineData(0.20, -0.02338, 0.70)]
    public void DetectionPowerIsMeasuredAgainstHowMuchGenuineWorksVary(
        double level, double shift, double expectedAuc)
    {
        var power = PerturbationRunner.MeasurePower(0.0310, new[] { (level, shift) });

        Assert.Equal(expectedAuc, Assert.Single(power).Auc, 2);
    }

    /// <summary>
    /// Nothing in the tested range reaches a usable AUC, so the answer is null
    /// rather than the lowest level tried. A method that cannot discriminate
    /// should say so.
    /// </summary>
    [Fact]
    public void NoDetectableLevelIsReportedAsNullRatherThanTheSmallest()
    {
        var power = PerturbationRunner.MeasurePower(0.0310, new[]
        {
            (0.01, -0.00104), (0.05, -0.00542), (0.10, -0.01106), (0.20, -0.02338)
        });

        Assert.Null(PerturbationRunner.DetectableFrom(power));
    }

    /// <summary>
    /// And a corpus where contamination really does separate reports the level
    /// at which it starts to.
    /// </summary>
    [Fact]
    public void AShiftLargeEnoughToDiscriminateIsReported()
    {
        // Twice the reference scatter is an AUC of 0.92.
        var power = PerturbationRunner.MeasurePower(0.01, new[] { (0.05, -0.005), (0.10, -0.02) });

        Assert.Equal(0.10, PerturbationRunner.DetectableFrom(power));
    }

    /// <summary>
    /// A level of zero is not a detection question and is skipped.
    /// </summary>
    [Fact]
    public void TheUncontaminatedLevelIsNotGivenAPowerFigure()
    {
        var power = PerturbationRunner.MeasurePower(0.03, new[] { (0.0, 0.0), (0.20, -0.023) });

        Assert.Equal(0.20, Assert.Single(power).InjectionFraction);
    }

    /// <summary>
    /// The nineteen Euripides plays: token count and uncontaminated margin, at
    /// 2,500-token samples and 150 features.
    /// </summary>
    private static List<(double Length, double Margin)> TheEuripidesReference() => new()
    {
        (6595, 0.0612), (7406, 0.1410), (7665, 0.1370), (4141, 0.0910), (7715, 0.1300),
        (7306, 0.1280), (9944, 0.1420), (6248, 0.0260), (7938, 0.1450), (8208, 0.0840),
        (9273, 0.1440), (9450, 0.1300), (8430, 0.1390), (8028, 0.0850), (10060, 0.1410),
        (5440, 0.0970), (7090, 0.1000), (9910, 0.1470), (7199, 0.1300)
    };

    /// <summary>
    /// THE MISTAKE THIS EXISTS TO CATCH. The first wiring divided the shift by
    /// the scatter of the DROPS - how consistently works respond to
    /// contamination, about 0.0014 - instead of by the scatter of the WORKS
    /// around the length line, about 0.029. Twenty-one times too small, and it
    /// turned an AUC of 0.55 into 1.00 and a null result into a detection.
    ///
    /// The two quantities are both legitimate and answer different questions.
    /// Only the second bears on "can I tell a contaminated text from a clean
    /// one".
    /// </summary>
    [Fact]
    public void ReferenceScatterIsHowMuchWorksDifferNotHowConsistentlyTheyRespond()
    {
        var scatter = PerturbationRunner.ReferenceScatter(TheEuripidesReference());

        // Measured on the real corpus.
        Assert.InRange(scatter, 0.027, 0.032);

        // And nowhere near the scatter of the drops, which is what was passed
        // by mistake.
        Assert.True(scatter > 10 * 0.0014,
            $"reference scatter {scatter:F4} should dwarf the drop scatter of 0.0014");
    }

    /// <summary>
    /// With the right denominator, the measured shifts give the AUCs that say
    /// this method cannot discriminate at any level tried.
    /// </summary>
    [Fact]
    public void TheEuripidesSweepHasNoUsableDetectionPower()
    {
        var scatter = PerturbationRunner.ReferenceScatter(TheEuripidesReference());

        var power = PerturbationRunner.MeasurePower(scatter, new[]
        {
            (0.01, -0.00105), (0.02, -0.00216), (0.05, -0.00546),
            (0.10, -0.01111), (0.20, -0.02344)
        });

        Assert.Equal(0.51, power[0].Auc, 2);
        Assert.Equal(0.55, power[2].Auc, 2);
        Assert.InRange(power[4].Auc, 0.69, 0.72);

        Assert.Null(PerturbationRunner.DetectableFrom(power));
    }

    /// <summary>
    /// Leave-one-out, so each work is judged against a line fitted without it -
    /// the situation an unknown work is actually in. Fitting on everything and
    /// then measuring against it lets each work pull the line towards itself
    /// and understates the scatter.
    /// </summary>
    [Fact]
    public void ReferenceScatterLeavesEachWorkOutOfItsOwnLine()
    {
        var works = TheEuripidesReference();

        var loo = PerturbationRunner.ReferenceScatter(works);

        // The in-sample residual SD, for comparison.
        var meanX = works.Average(w => w.Length);
        var meanY = works.Average(w => w.Margin);
        var slope = works.Sum(w => (w.Length - meanX) * (w.Margin - meanY))
                    / works.Sum(w => (w.Length - meanX) * (w.Length - meanX));
        var intercept = meanY - slope * meanX;
        var inSample = Math.Sqrt(works.Average(w =>
            Math.Pow(w.Margin - (intercept + slope * w.Length), 2)));

        Assert.True(loo > inSample,
            $"leave-one-out {loo:F4} should exceed in-sample {inSample:F4}");
    }

    /// <summary>Too few works to fit a line through gives no scatter rather than a wild one.</summary>
    [Fact]
    public void ATinyCorpusGivesNoReferenceScatter()
    {
        Assert.Equal(0, PerturbationRunner.ReferenceScatter(
            new[] { (1000.0, 0.1), (2000.0, 0.2), (3000.0, 0.3) }));
    }

    /// <summary>
    /// Eight real Plato works and their uncontaminated margins, from the
    /// positive-control sweep against Homer.
    /// </summary>
    private static List<(double Length, double Margin)> ThePlatoReference() => new()
    {
        (2827, 0.3741),   // Minos
        (4177, 0.3306),   // Alcibiades 2
        (5179, 0.3626),   // Euthyphro
        (8321, 0.4115),   // Charmides
        (10272, 0.3898),  // Alcibiades 1
        (16618, 0.3159),  // Phaedrus
        (17545, 0.3536),  // Protagoras
        (22517, 0.4711)   // Theaetetus
    };

    /// <summary>
    /// A work too short to yield a single sample must not enter the cross-work
    /// statistics as a work of no length and no margin.
    ///
    /// THE POSITIVE CONTROL CAUGHT THIS. Four Platonic dialogues - Cleitophon,
    /// Definitiones, Hipparchus, Lovers - are under 2,500 tokens, so a sweep at
    /// that sample size measured nothing for them and reported zeros. Included
    /// as real works they sit at the origin, far from a cluster of texts three
    /// to twenty thousand tokens long, and drag the fitted line towards
    /// themselves: the reference scatter went from 0.082 to 0.137 across the
    /// full sweep.
    ///
    /// That pulled the detection AUC at 20% Homeric contamination down from
    /// 0.94 to 0.80 - the difference between a positive control that plainly
    /// passes and one that looks marginal, on a corpus where Delta ought to
    /// separate prose from epic verse trivially.
    ///
    /// A bug that makes a method look WEAKER than it is may be the hardest kind
    /// to notice in a project where every result so far has been negative. It
    /// was found only by running a case where the answer was known in advance.
    /// </summary>
    [Fact]
    public void WorksTooShortToSampleInflateTheReferenceScatter()
    {
        var measured = ThePlatoReference();

        var withUnmeasurable = measured
            .Concat(new[] { (0.0, 0.0), (0.0, 0.0) })
            .ToList();

        var clean = PerturbationRunner.ReferenceScatter(measured);
        var polluted = PerturbationRunner.ReferenceScatter(withUnmeasurable);

        Assert.True(polluted > clean * 1.5,
            $"zero-length works should badly inflate the scatter: {polluted:F4} against {clean:F4}");
    }

    /// <summary>
    /// And an inflated scatter makes real contamination look undetectable,
    /// which is the consequence that matters.
    /// </summary>
    [Fact]
    public void AnInflatedScatterUnderstatesDetectionPower()
    {
        var measured = ThePlatoReference();
        var polluted = measured.Concat(new[] { (0.0, 0.0), (0.0, 0.0) }).ToList();

        // The measured mean drop at 20% Homeric injection.
        var shifts = new[] { (0.20, -0.1858) };

        var honest = PerturbationRunner.MeasurePower(
            PerturbationRunner.ReferenceScatter(measured), shifts)[0].Auc;

        var understated = PerturbationRunner.MeasurePower(
            PerturbationRunner.ReferenceScatter(polluted), shifts)[0].Auc;

        Assert.True(honest > understated,
            $"AUC should fall when the scatter is inflated: {honest:F2} against {understated:F2}");
        Assert.True(honest > 0.9, $"the positive control should pass clearly, not marginally: {honest:F2}");
    }

    // ------------------------------------------------------ donor scope

    /// <summary>
    /// Whole-corpus draws pool every donor work; single-work draws use one.
    ///
    /// The point of the distinction is realism. A whole-corpus draw is an
    /// IDEALISED donor - expected frequencies exactly matching that author's
    /// overall profile. A real interpolation is one passage by one author on
    /// one topic, and a single work's profile can sit some way from its
    /// author's average, so the single-work mixtures should vary more between
    /// iterations.
    /// </summary>
    [Fact]
    public void SingleWorkScopeDrawsFromOneDonorAtATime()
    {
        var pool = Pool();

        var whole = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, BetaDonors, 0.30, InjectionMode.Replace, 42, 12),
            Settings, 0.2);

        var single = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, BetaDonors, 0.30, InjectionMode.Replace, 42, 12,
                DonorScope.SingleWork),
            Settings, 0.2);

        Assert.Equal(12, single.Trials.Count);

        // Different material, so different mixtures - the two must not be the
        // same run under another name.
        Assert.NotEqual(whole.MeanMargin, single.MeanMargin, 6);
    }

    /// <summary>
    /// Which work a mixture draws from is a function of the seed and the
    /// iteration, so a single-work series is as reproducible as any other.
    /// </summary>
    [Fact]
    public void SingleWorkScopeIsReproducibleFromItsSeed()
    {
        var pool = Pool();
        var config = new PerturbationConfig(10, BetaDonors, 0.20, InjectionMode.Replace, 7, 8,
            DonorScope.SingleWork);

        var first = PerturbationRunner.RunLevel(pool, config, Settings, 0.2);
        var again = PerturbationRunner.RunLevel(pool, config, Settings, 0.2);

        Assert.Equal(
            first.Trials.Select(t => t.Margin),
            again.Trials.Select(t => t.Margin));
    }

    /// <summary>
    /// Whole corpus stays the default, because every figure recorded so far was
    /// measured that way and a changed default would quietly make old and new
    /// runs incomparable.
    /// </summary>
    [Fact]
    public void WholeCorpusRemainsTheDefault()
    {
        var config = new PerturbationConfig(10, BetaDonors, 0.1, InjectionMode.Replace, 42, 5);

        Assert.Equal(DonorScope.WholeCorpus, config.Scope);
        Assert.Contains("whole donor corpus", config.Describe());
    }

    /// <summary>
    /// With one donor work available the two scopes coincide, and nothing
    /// should break at that boundary.
    /// </summary>
    [Fact]
    public void ASingleDonorWorkBehavesTheSameUnderBothScopes()
    {
        var pool = Pool();

        var whole = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, new[] { 20 }, 0.2, InjectionMode.Replace, 42, 5),
            Settings, 0.2);

        var single = PerturbationRunner.RunLevel(
            pool,
            new PerturbationConfig(10, new[] { 20 }, 0.2, InjectionMode.Replace, 42, 5,
                DonorScope.SingleWork),
            Settings, 0.2);

        Assert.Equal(whole.MeanMargin, single.MeanMargin, 9);
    }
}
