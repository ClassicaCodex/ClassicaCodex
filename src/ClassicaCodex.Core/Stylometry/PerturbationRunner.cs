namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// How donor material is mixed into the target.
/// </summary>
public enum InjectionMode
{
    /// <summary>
    /// Donor tokens take the place of target tokens, leaving the total
    /// unchanged.
    ///
    /// THE DEFAULT, AND FOR A SPECIFIC REASON. Every margin this bench produces
    /// correlates with text length - rho +0.42 to +0.73 across forty
    /// configurations on the Euripides pool, with no parameter region escaping
    /// it. Any experiment that changes a work's length while measuring its
    /// margin is therefore reading two things at once and cannot separate them.
    ///
    /// Replacement holds the token count exactly constant across every
    /// injection level in a series, so the confound is held constant with it.
    /// Whatever moves as contamination rises is not length.
    /// </summary>
    Replace,

    /// <summary>
    /// Donor tokens are added, growing the work.
    ///
    /// Closer to what a real interpolation does to a text, and for that reason
    /// worth having. But it changes length as it changes composition, and on
    /// this corpus those are exactly the two things that must not move
    /// together. Results from this mode cannot be read as a contamination
    /// curve without an argument about length that the bench cannot supply.
    /// </summary>
    Add
}

/// <summary>
/// Where each iteration's donor material comes from.
/// </summary>
public enum DonorScope
{
    /// <summary>
    /// Every word drawn independently from the donor author's entire surviving
    /// corpus. The original behaviour, and an IDEALISED donor: expected
    /// frequencies exactly matching that author's overall profile, with only
    /// multinomial noise around them.
    /// </summary>
    WholeCorpus,

    /// <summary>
    /// Each iteration draws from ONE donor work, chosen deterministically from
    /// the seed and the iteration number.
    ///
    /// Closer to a real interpolation, which is one passage by one author on
    /// one topic in one register rather than a sample of everything they wrote.
    /// A single work's frequency profile can sit some way from its author's
    /// average, so this should produce more variance between iterations - and
    /// if the whole-corpus figures really are an upper bound on detection
    /// power, a lower mean effect too.
    ///
    /// Contiguity is still not modelled and does not need to be: the engine
    /// shuffles token positions into bags before counting, so a spliced passage
    /// and the same words scattered arrive as nearly the same profile. What
    /// changes here is WHICH words are available to draw, not where they land.
    /// </summary>
    SingleWork
}

/// <summary>
/// One perturbation configuration, and everything needed to rebuild it.
/// </summary>
/// <param name="TargetWorkId">The work being contaminated.</param>
/// <param name="DonorWorkIds">Works the injected material is drawn from.</param>
/// <param name="InjectionFraction">Proportion of the target replaced or added.</param>
/// <param name="Mode">Whether donor tokens replace the target's or are added to them.</param>
/// <param name="Seed">
/// The random seed. Stored because a synthetic text that cannot be regenerated
/// cannot be checked by anyone, including its author six months later.
/// </param>
/// <param name="Scope">
/// Whether each mixture draws from the donor's whole corpus or from a single
/// work. See <see cref="DonorScope"/>.
/// </param>
/// <param name="Iterations">
/// How many independent mixtures to draw at this level.
///
/// One mixture is one draw from a distribution and says almost nothing: the
/// question is how often contamination at this level breaks recovery, not
/// whether it broke it on the one occasion tried. Reporting a single mixture as
/// though it were the answer is the same error as reading one Delta run.
/// </param>
public sealed record PerturbationConfig(
    int TargetWorkId,
    IReadOnlyList<int> DonorWorkIds,
    double InjectionFraction,
    InjectionMode Mode,
    int Seed,
    int Iterations,
    DonorScope Scope = DonorScope.WholeCorpus)
{
    public string Describe() =>
        $"{InjectionFraction:P0} {(Mode == InjectionMode.Replace ? "replaced" : "added")}, " +
        $"{Iterations} iterations, seed {Seed}, " +
        $"{(Scope == DonorScope.SingleWork ? "one donor work per mixture" : "whole donor corpus")}";
}

/// <summary>One synthetic mixture, measured.</summary>
public sealed record PerturbationTrial(
    int Iteration,
    double Margin,
    double DeltaFloor,
    string NearestAuthor,
    string NearestLabel,
    bool Recovered,
    int TokenCount);

/// <summary>
/// The distribution at one injection level.
///
/// A distribution rather than a value, because that is what repeated sampling
/// produces and collapsing it to a mean would hide the thing worth knowing -
/// whether contamination at this level breaks recovery sometimes, always, or
/// never.
/// </summary>
public sealed record PerturbationLevel(
    double InjectionFraction,
    IReadOnlyList<PerturbationTrial> Trials,
    double BaselineMargin)
{
    public int RecoveredCount => Trials.Count(t => t.Recovered);

    public double RecoveryRate => Trials.Count == 0 ? 0 : (double)RecoveredCount / Trials.Count;

    public double MeanMargin => Trials.Count == 0 ? 0 : Trials.Average(t => t.Margin);

    public double MarginStdDev
    {
        get
        {
            if (Trials.Count < 2) return 0;
            var mean = MeanMargin;
            return Math.Sqrt(Trials.Average(t => (t.Margin - mean) * (t.Margin - mean)));
        }
    }

    /// <summary>Margin at the given percentile, 0 to 1.</summary>
    public double MarginPercentile(double p)
    {
        if (Trials.Count == 0) return 0;
        var sorted = Trials.Select(t => t.Margin).OrderBy(m => m).ToList();
        var index = (int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[index];
    }

    /// <summary>
    /// How far the mean margin has moved from baseline, measured in the
    /// standard deviations of this level's own mixtures.
    ///
    /// A percentage cannot tell a small real effect from noise, and on real
    /// runs the difference matters. Alcestis contaminated with 20% Aeschylus
    /// reported 98% of baseline, which reads as a slight fall; the drop was
    /// 0.001 against a standard deviation of 0.0101, so it is a tenth of one
    /// SD and there is no effect there at all. Rhesus at the same level
    /// reported 80%, a drop of 0.020 against an SD of 0.0088 - about 2.3 SD,
    /// and real.
    ///
    /// Negative when the margin fell, positive when it rose, so the
    /// same-author control reads as a positive shift.
    ///
    /// This is a descriptive scale, not a test. The mixtures at one level are
    /// draws from the same synthetic process rather than an independent
    /// sample of anything, so the number says how large the shift is relative
    /// to how much the mixing alone moves it - which is the comparison worth
    /// making - and does not license a p-value.
    /// </summary>
    public double ShiftInStdDevs
    {
        get
        {
            if (Trials.Count < 2 || MarginStdDev < 1e-12) return 0;
            return (MeanMargin - BaselineMargin) / MarginStdDev;
        }
    }

    /// <summary>
    /// The change in mean margin from baseline, in Delta.
    ///
    /// THE ONE QUANTITY THAT COMPARES ACROSS WORKS, and neither of the other
    /// two does. Percentage divides by the baseline and SD divides by the
    /// mixing noise, so the same experiment ranks four plays differently
    /// depending which column is read. On real runs at 20% injection with the
    /// same donors:
    ///
    ///   Heracleidae  base +0.026  drop -0.011  60% of base  -1.17 SD
    ///   Rhesus       base +0.097  drop -0.027  73%          -2.84 SD
    ///   Hecuba       base +0.128  drop -0.030  76%          -2.92 SD
    ///   Helen        base +0.142  drop -0.027  81%          -3.39 SD
    ///
    /// Read as percentages, Heracleidae responds most and Helen least. Read as
    /// SDs, the order reverses exactly. Read as Delta, three of the four lose
    /// the same amount and Heracleidae loses a third of it - which is the
    /// finding, and neither of the other columns shows it.
    /// </summary>
    public double AbsoluteShift => MeanMargin - BaselineMargin;

    /// <summary>
    /// How many standard deviations of mixing noise separate the
    /// uncontaminated margin from zero.
    ///
    /// The ceiling on <see cref="ShiftInStdDevs"/>, and the reason that measure
    /// cannot be read against a fixed threshold. Heracleidae's baseline sits
    /// 3.0 SD above zero, so its shift cannot reach -3.0 however completely the
    /// contamination works; Helen's sits at 17.7. A rule of thumb like "under
    /// two SD the curve has not moved" is therefore nearly unreachable for one
    /// work and trivially passed by another, and a shift should be read as a
    /// fraction of this rather than against a constant.
    /// </summary>
    public double HeadroomInStdDevs =>
        MarginStdDev < 1e-12 ? 0 : Math.Abs(BaselineMargin) / MarginStdDev;

    /// <summary>
    /// How far the mean margin has fallen from the uncontaminated baseline, as
    /// a fraction of it.
    ///
    /// Reported as a proportion of the work's own starting margin rather than
    /// in absolute Delta, because absolute margin swings by a factor of four
    /// and a half on preprocessing alone - +0.045 to +0.205 across the
    /// parameter grid with the texts untouched. A drop of 0.05 means something
    /// different at each end of that range; a drop of 40% does not.
    /// </summary>
    public double ProportionOfBaseline =>
        Math.Abs(BaselineMargin) < 1e-9 ? 0 : MeanMargin / BaselineMargin;

    /// <summary>
    /// How many donor authors the mixtures drifted towards - the nearest
    /// neighbour flipping is a coarser signal than the margin but a more
    /// legible one.
    /// </summary>
    public IReadOnlyDictionary<string, int> NearestAuthorCounts =>
        Trials.GroupBy(t => t.NearestAuthor).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// What a sweep could have detected, had there been anything to detect.
/// </summary>
/// <param name="InjectionFraction">The contamination level this row describes.</param>
/// <param name="ReferenceScatter">
/// How much genuine works vary around the fitted line once length is
/// accounted for. The yardstick everything else is measured against.
/// </param>
/// <param name="Shift">Mean movement caused by contamination at this level.</param>
/// <param name="EffectSize">Shift divided by reference scatter.</param>
/// <param name="Auc">
/// The probability of correctly ranking one contaminated work above one clean
/// work. 0.5 is a coin flip; 0.8 is usually called the floor of usefulness for
/// a diagnostic.
/// </param>
public sealed record DetectionPower(
    double InjectionFraction,
    double ReferenceScatter,
    double Shift,
    double EffectSize,
    double Auc)
{
    /// <summary>Share of the two distributions that overlaps.</summary>
    public double Overlap => 2 * NormalCdf(-EffectSize / 2);

    internal static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2));

    internal static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + 0.5 * z);

        var ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
            t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
            t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));

        return x >= 0 ? ans : 2 - ans;
    }
}

/// <summary>One work's place in a cross-work sweep.</summary>
public sealed record WorkResponse(
    string Title,
    double BaselineMargin,
    double Drop,
    double Expected,
    double Residual,
    double DeviationsFromTypical);

/// <summary>
/// How the works in a sweep differ from each other, once the part of the
/// difference that is merely baseline has been taken out.
///
/// THE DROP IS NOT INDEPENDENT OF THE BASELINE, WHICH IS WHY THIS EXISTS. Drop
/// in Delta was introduced as the quantity that compares across works, because
/// a percentage divides by the baseline and an SD count divides by the noise
/// and neither is comparable. It is better than both and it is still not
/// clean: over nineteen Euripides plays contaminated with Sophocles at 20%,
/// rho between baseline margin and the size of the drop is +0.749. Works with
/// more margin to lose lose more of it.
///
/// Ranked on raw drop, Heracleidae is the outlier at 3.3 median absolute
/// deviations - and it has the lowest baseline in the corpus. Ranked on the
/// residual from a fit against baseline it sits at 1.2, which is nothing, and
/// the extremes become Hecuba and Ion. The raw ranking was mostly rediscovering
/// which play has the smallest margin.
///
/// This is the third disguise the same confound has worn: depth to first
/// outsider tracked length, margin tracked length, and now the response to
/// contamination tracks margin. Each time it was found by checking rather than
/// by suspecting, which is the argument for the check being automatic.
/// </summary>
public sealed record CrossWorkSummary(
    IReadOnlyList<WorkResponse> Works,
    double BaselineDropCorrelation,
    double Slope,
    double Intercept,
    double ResidualMedian,
    double ResidualMad)
{
    /// <summary>
    /// A robust standard deviation of the residuals, from the median absolute
    /// deviation.
    ///
    /// The constant is the one that makes MAD agree with the standard
    /// deviation for normally distributed data. It is here because "three
    /// median absolute deviations" sounds like a strict threshold and is not:
    /// three MAD is about two sigma, which one work in twenty-three clears by
    /// chance.
    /// </summary>
    public double RobustSigma => ResidualMad * 1.4826;

    /// <summary>
    /// How many works would be expected to clear a threshold by chance alone,
    /// if nothing were going on.
    ///
    /// THIS IS THE NUMBER THAT SAYS WHETHER A FLAG MEANS ANYTHING. Over
    /// nineteen works a three-MAD threshold expects 0.8 false flags per sweep,
    /// so a sweep that flags one work has found nothing. The Aeschylus sweep
    /// flagged three, which against an expectation of 0.8 is p about 0.05 -
    /// suggestive for the sweep as a whole, and not a licence to believe any
    /// one of the three.
    ///
    /// Reported rather than used to move the threshold, because a threshold
    /// tuned until the answer looks significant is the failure this whole bench
    /// exists to make harder.
    /// </summary>
    public double ExpectedFalseFlags(double deviations = 3)
    {
        if (Works.Count == 0 || ResidualMad < 1e-12) return 0;

        var z = deviations / 1.4826;
        return Works.Count * Erfc(z / Math.Sqrt(2));
    }

    /// <summary>
    /// Complementary error function, Abramowitz and Stegun 7.1.26. Accurate to
    /// about 1e-7, which is far beyond what nineteen data points support.
    /// </summary>
    private static double Erfc(double x)
    {
        var z = Math.Abs(x);
        var t = 1 / (1 + 0.5 * z);

        var ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
            t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
            t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));

        return x >= 0 ? ans : 2 - ans;
    }

    /// <summary>
    /// Works far enough from the fitted line to be worth reading, if any.
    ///
    /// Returns nothing when nothing qualifies, which is the common and correct
    /// outcome. A rank always names somebody; a threshold can report that the
    /// works behave alike.
    /// </summary>
    public IReadOnlyList<WorkResponse> Outliers(double deviations = 3) =>
        Works.Where(w => w.DeviationsFromTypical >= deviations)
             .OrderByDescending(w => w.DeviationsFromTypical)
             .ToList();

    /// <summary>
    /// Whether the raw drop and the residual disagree about which work is
    /// furthest out - the signal that the raw ranking was reading baseline.
    /// </summary>
    public bool RawRankingIsMisleading
    {
        get
        {
            if (Works.Count < 3) return false;

            var byRawDrop = Works.OrderBy(w => Math.Abs(w.Drop)).First().Title;
            var byResidual = Works.OrderByDescending(w => w.DeviationsFromTypical).First().Title;

            return byRawDrop != byResidual;
        }
    }
}

/// <summary>
/// Contaminates a work whose authorship is not in question, and measures what
/// it takes to stop the method recognising it.
///
/// THE QUESTION THIS ANSWERS, AND THE ONE IT DOES NOT. It answers: how much
/// deliberate stylistic disturbance does this methodology absorb before it
/// stops returning a known text to its known author? That is a statement about
/// the METHOD's sensitivity, measured on texts where the truth is not in doubt.
///
/// It does not answer what proportion of a disputed text was written by someone
/// else. If Rhesus behaves like a Euripidean play carrying 10% synthetic
/// Sophocles, the only thing established is that the magnitude of its
/// stylometric disturbance is comparable to that experiment's. Genre,
/// chronology, transmission, a copyist's habits and a bad edition all produce
/// disturbance too, and nothing here distinguishes them from a second hand.
/// Any reading of the number as "10% of Rhesus is by Sophocles" is unsupported
/// by anything in this file.
///
/// WHAT IS BEING SAMPLED. Individual tokens, drawn independently and with
/// replacement from the donor's entire corpus, landing at random positions.
///
/// Contiguity is not what is being given up. DeltaEngine's comparison unit is a
/// randomly drawn bag of words with word order destroyed, so a spliced passage
/// and the same words scattered reach it as nearly the same frequency profile.
/// A vivid picture of interpolation would change almost nothing the measure can
/// see.
///
/// WHAT IS GIVEN UP IS THAT THIS IS AN IDEALISED DONOR. Independent draws from
/// a whole corpus have expected frequencies exactly matching that author's
/// overall profile, with only multinomial noise around them. A real
/// interpolation is one passage by one author on one topic in one register, and
/// its profile can sit some way from that author's average. So the signal here
/// is the cleanest version of the donor's style that could be built, and every
/// detection figure derived from it is an UPPER BOUND on what the method could
/// find in a real text.
///
/// MEASURED, not assumed. Nineteen Euripides plays against Sophocles, run both
/// ways: the mean effect is the same to within about 6%, but the variance
/// between mixtures is 1.43x higher when drawing from one work. Most of that is
/// the smaller draw pool rather than style - the same-author control inflates
/// 1.30x - leaving about 1.10x from genuine heterogeneity between the donor's
/// plays, which is real (15 works of 19, sign test p = 0.010) and small.
///
/// The detection figures move from AUC 0.76 to 0.74 at 20% injection. So the
/// idealisation buys precision rather than power, and a null result from it is
/// not meaningfully flattered. See docs/stylometry-notes.md section 6.
/// </summary>
public static class PerturbationRunner
{
    /// <summary>Injection levels worth trying by default.</summary>
    public static readonly double[] DefaultLevels = { 0.00, 0.01, 0.02, 0.05, 0.10, 0.20 };

    /// <summary>
    /// Combines the configured seed with the iteration number, deterministically.
    ///
    /// NOT HashCode.Combine, which was the first thing written here and is
    /// wrong for this. System.HashCode is seeded from a per-process random
    /// value, deliberately, to make hash-flooding attacks impractical - so it
    /// returns different results for the same inputs in different runs of the
    /// program. Every mixture would have been reproducible within a session and
    /// irreproducible across one, which is the exact opposite of the property a
    /// stored seed exists to provide, and it would have gone unnoticed because
    /// a test that mixes twice and compares passes inside a single process.
    ///
    /// The constants are the usual small primes. Nothing here needs a good
    /// hash - it needs the same answer every time.
    /// </summary>
    private static int SeedFor(int seed, int iteration) =>
        unchecked(seed * 1_000_003 + iteration * 31 + 17) & 0x7FFFFFFF;

    /// <summary>
    /// Builds one synthetic token list.
    ///
    /// Deterministic in the seed and the iteration, so any mixture in any
    /// reported series can be rebuilt exactly - in this session or next year.
    /// The iteration is folded into the seed rather than drawn from a running
    /// generator, so that trial 17 of a 50-iteration run is the same text
    /// whether or not trials 1 to 16 were also computed, which matters the
    /// first time somebody wants to look at the one outlier in a series.
    /// </summary>
    public static List<string> Mix(
        IReadOnlyList<string> targetTokens,
        IReadOnlyList<string> donorTokens,
        double fraction,
        InjectionMode mode,
        int seed,
        int iteration)
    {
        if (fraction <= 0 || donorTokens.Count == 0) return targetTokens.ToList();
        if (fraction > 1) throw new ArgumentOutOfRangeException(nameof(fraction), "Injection above 100% is not a mixture.");

        var rng = new Random(SeedFor(seed, iteration));
        var count = (int)Math.Round(targetTokens.Count * fraction);
        if (count == 0) return targetTokens.ToList();

        var drawn = new List<string>(count);
        for (var i = 0; i < count; i++) drawn.Add(donorTokens[rng.Next(donorTokens.Count)]);

        if (mode == InjectionMode.Add)
        {
            var grown = targetTokens.ToList();
            grown.AddRange(drawn);
            return grown;
        }

        // Replace: choose which positions give way, without replacement, so
        // exactly the requested proportion changes.
        var mixed = targetTokens.ToList();
        var positions = Enumerable.Range(0, mixed.Count).ToArray();
        for (var i = positions.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        for (var i = 0; i < count; i++) mixed[positions[i]] = drawn[i];
        return mixed;
    }

    /// <summary>
    /// Runs every iteration at one injection level.
    /// </summary>
    /// <param name="pool">
    /// The comparison pool, including the target and the donors. The target's
    /// real tokens are REPLACED by each synthetic mixture rather than added
    /// alongside, so the pool's composition is identical at every level and the
    /// synthetic never meets its own uncontaminated self.
    /// </param>
    public static PerturbationLevel RunLevel(
        IReadOnlyList<WorkTokens> pool,
        PerturbationConfig config,
        DeltaSettings settings,
        double baselineMargin,
        Action<int, int>? progress = null,
        CancellationToken cancellation = default,
        IReadOnlyDictionary<int, List<StylometryChunk>>? cachedChunks = null)
    {
        var distinct = pool.GroupBy(w => w.WorkId).Select(g => g.First()).ToList();

        var target = distinct.FirstOrDefault(w => w.WorkId == config.TargetWorkId)
            ?? throw new InvalidOperationException("The target work is not in the pool.");

        // Kept per work, not flattened, so a single one can be chosen per
        // iteration. Ordered by work id rather than by pool order, so which
        // work a given seed and iteration selects does not depend on how the
        // pool happened to be assembled.
        var donorWorks = distinct
            .Where(w => config.DonorWorkIds.Contains(w.WorkId))
            .OrderBy(w => w.WorkId)
            .Select(w => (IReadOnlyList<string>)w.Tokens)
            .Where(t => t.Count > 0)
            .ToList();

        var wholeCorpus = donorWorks.SelectMany(t => t).ToList();

        if (wholeCorpus.Count == 0 && config.InjectionFraction > 0)
            throw new InvalidOperationException("No donor material - pick at least one donor work.");

        var trials = new List<PerturbationTrial>(config.Iterations);

        // At zero injection every iteration produces the same text, so one
        // trial is the whole distribution. Running fifty identical mixtures
        // would report a standard deviation of zero as though it had been
        // measured.
        var iterations = config.InjectionFraction <= 0 ? 1 : config.Iterations;

        for (var i = 0; i < iterations; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(i, iterations);

            // Which work this mixture draws from is a function of the seed and
            // the iteration, like everything else here, so a series can be
            // rebuilt exactly.
            var donor = config.Scope == DonorScope.SingleWork && donorWorks.Count > 0
                ? donorWorks[SeedFor(config.Seed, i) % donorWorks.Count]
                : wholeCorpus;

            var mixed = Mix(target.Tokens, donor, config.InjectionFraction,
                            config.Mode, config.Seed, i);

            var synthetic = target with { Tokens = mixed };
            var runPool = distinct
                .Select(w => w.WorkId == target.WorkId ? synthetic : w)
                .ToList();

            var sampleCount = DeltaEngine.ChunkCountFor(mixed.Count, settings.ChunkSize);
            if (sampleCount < 1) continue;

            var sames = new List<double>();
            var others = new List<double>();
            var floors = new List<double>();
            var nearest = ("", "", double.MaxValue);

            for (var s = 0; s < sampleCount; s++)
            {
                var run = DeltaEngine.Compute(
                    runPool, target.WorkId, settings, s,
                    excludeTargetFromNormalisation: false,
                    cachedChunks: cachedChunks);

                var same = run.Neighbors
                    .Where(n => string.Equals(n.AuthorName, target.AuthorName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var other = run.Neighbors
                    .Where(n => !string.Equals(n.AuthorName, target.AuthorName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (same.Count == 0 || other.Count == 0) continue;

                sames.Add(same.Average(n => n.Delta));
                others.Add(other.Average(n => n.Delta));
                floors.Add(run.Neighbors[0].Delta);

                if (run.Neighbors[0].Delta < nearest.Item3)
                    nearest = (run.Neighbors[0].AuthorName, run.Neighbors[0].Label, run.Neighbors[0].Delta);
            }

            if (sames.Count == 0) continue;

            var margin = others.Average() - sames.Average();

            trials.Add(new PerturbationTrial(
                i, margin, floors.Average(), nearest.Item1, nearest.Item2,
                margin > 0, mixed.Count));
        }

        return new PerturbationLevel(config.InjectionFraction, trials, baselineMargin);
    }

    /// <summary>
    /// The uncontaminated margin, which every level is read against.
    ///
    /// Measured rather than assumed, and measured through exactly the same code
    /// path as the contaminated levels, so that a difference between them is a
    /// difference in the text rather than in how it was measured.
    /// </summary>
    public static double Baseline(
        IReadOnlyList<WorkTokens> pool, int targetWorkId, DeltaSettings settings,
        IReadOnlyDictionary<int, List<StylometryChunk>>? cachedChunks = null)
    {
        var config = new PerturbationConfig(targetWorkId, Array.Empty<int>(), 0, InjectionMode.Replace, 0, 1);
        var level = RunLevel(pool, config, settings, 0, cachedChunks: cachedChunks);

        return level.Trials.Count == 0 ? 0 : level.Trials[0].Margin;
    }

    /// <summary>
    /// A sensitivity series: the same experiment at rising contamination.
    ///
    /// The output is a curve, and the useful reading of it is where the curve
    /// crosses - the level at which recovery stops being reliable - rather than
    /// any single point on it.
    /// </summary>
    public static List<PerturbationLevel> RunSeries(
        IReadOnlyList<WorkTokens> pool,
        int targetWorkId,
        IReadOnlyList<int> donorWorkIds,
        IReadOnlyList<double> levels,
        InjectionMode mode,
        int seed,
        int iterations,
        DeltaSettings settings,
        Action<int, int, double>? progress = null,
        CancellationToken cancellation = default,
        DonorScope scope = DonorScope.WholeCorpus)
    {
        // Chunk every work but the target once, and reuse it for every level
        // and every iteration. The target is deliberately absent - its tokens
        // are what the experiment changes.
        var cache = DeltaEngine.ChunkWorks(
            pool.Where(w => w.WorkId != targetWorkId).ToList(), settings.ChunkSize);

        var baseline = Baseline(pool, targetWorkId, settings, cache);
        var results = new List<PerturbationLevel>(levels.Count);

        for (var i = 0; i < levels.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(i, levels.Count, levels[i]);

            var config = new PerturbationConfig(
                targetWorkId, donorWorkIds, levels[i], mode, seed, iterations, scope);

            results.Add(RunLevel(pool, config, settings, baseline, null, cancellation, cache));
        }

        return results;
    }

    /// <summary>
    /// How much contamination this method could actually find, on this corpus.
    ///
    /// THE QUESTION A NULL RESULT IS WORTHLESS WITHOUT. A sweep that finds no
    /// anomaly has said nothing until it also says what it could have found.
    /// "This work shows no sign of foreign material" and "this work shows no
    /// sign of foreign material, and material below thirty percent would have
    /// been invisible" are different statements, and only the second is worth
    /// reporting.
    ///
    /// The comparison is between two spreads. Genuine works scatter around the
    /// length line for reasons that have nothing to do with authorship - date,
    /// genre, subject, transmission, the editor. That scatter is the noise any
    /// real signal has to clear. Contamination moves a work by some other
    /// amount. If the movement is small against the scatter, nothing can be
    /// distinguished however precisely each work is measured.
    ///
    /// On nineteen Euripides plays contaminated with Aeschylus and Sophocles,
    /// the reference scatter is 0.031 and 20% injection moves a work 0.023 -
    /// three quarters of one deviation, an AUC of 0.70. Pick one clean and one
    /// heavily contaminated play and this method ranks them correctly seven
    /// times in ten. At 10% it is six. At 5% it is a coin flip.
    ///
    /// AUC rather than a p-value because the question is discrimination, not
    /// significance: with enough iterations a mean shift of any size becomes
    /// significant, and none of that helps identify which text is which.
    /// </summary>
    /// <param name="referenceScatter">
    /// From <see cref="ReferenceScatter"/>: how much the UNCONTAMINATED works
    /// differ from each other. Not the scatter of their drops, which is twenty
    /// times smaller and makes every level look detectable.
    /// </param>
    /// <summary>
    /// How much genuine works differ from each other, once length is accounted
    /// for. The denominator of every detection figure.
    ///
    /// THIS IS NOT THE SCATTER OF THE DROPS, AND THE DIFFERENCE IS THE WHOLE
    /// CALCULATION. Works respond to contamination remarkably consistently: the
    /// residuals of drop against baseline scatter by about 0.0014 on the
    /// Euripides corpus. Works themselves scatter around the margin-against-
    /// length line by 0.0295 - twenty-one times as much. Divide a shift by the
    /// first and every level looks perfectly detectable; divide it by the
    /// second and almost nothing is.
    ///
    /// The second is the right one, because the detection question is "given an
    /// unknown work's margin, can I tell whether it has been contaminated?" -
    /// and the noise that has to be cleared is how much undisputed works vary
    /// for reasons that have nothing to do with authorship. How consistently
    /// they RESPOND to contamination is a different question and does not bear
    /// on it.
    ///
    /// Leave-one-out: each work is measured against a line fitted without it,
    /// which is the situation a genuinely unknown work is in. Fitting on all of
    /// them and then measuring them against it understates the scatter by
    /// letting each work pull the line towards itself.
    /// </summary>
    public static double ReferenceScatter(IReadOnlyList<(double Length, double Margin)> works)
    {
        if (works.Count < 4) return 0;

        var residuals = new List<double>(works.Count);

        for (var i = 0; i < works.Count; i++)
        {
            var others = works.Where((_, j) => j != i).ToList();

            var meanX = others.Average(o => o.Length);
            var meanY = others.Average(o => o.Margin);
            var sxx = others.Sum(o => (o.Length - meanX) * (o.Length - meanX));
            if (sxx < 1e-12) return 0;

            var slope = others.Sum(o => (o.Length - meanX) * (o.Margin - meanY)) / sxx;
            var intercept = meanY - slope * meanX;

            residuals.Add(works[i].Margin - (intercept + slope * works[i].Length));
        }

        var mean = residuals.Average();
        return Math.Sqrt(residuals.Average(r => (r - mean) * (r - mean)));
    }

    public static List<DetectionPower> MeasurePower(
        double referenceScatter,
        IReadOnlyList<(double Level, double MeanShift)> shifts)
    {
        var power = new List<DetectionPower>();

        foreach (var (level, shift) in shifts.OrderBy(s => s.Level))
        {
            if (level <= 0) continue;

            var effect = referenceScatter < 1e-12 ? 0 : Math.Abs(shift) / referenceScatter;

            // The probability that a random contaminated work ranks above a
            // random clean one, for two normal distributions a given number of
            // deviations apart.
            var auc = DetectionPower.NormalCdf(effect / Math.Sqrt(2));

            power.Add(new DetectionPower(level, referenceScatter, shift, effect, auc));
        }

        return power;
    }

    /// <summary>
    /// The lowest level at which the two distributions separate enough to call
    /// it a detection, or null if none does.
    ///
    /// 0.8 is the conventional floor for a useful diagnostic. The honest output
    /// on the tragic corpus is null at every level tried.
    /// </summary>
    public static double? DetectableFrom(IReadOnlyList<DetectionPower> power, double auc = 0.8) =>
        power.OrderBy(p => p.InjectionFraction).FirstOrDefault(p => p.Auc >= auc)?.InjectionFraction;

    /// <summary>
    /// Compares works to each other after removing the part of their response
    /// that is predictable from their baseline margin.
    ///
    /// A straight line rather than anything cleverer: nineteen points cannot
    /// support a curve, and the correlation is strong and monotonic. The
    /// residual is what is left when "this work had more margin to lose" has
    /// been accounted for, and the ranking is on that.
    ///
    /// Median and median absolute deviation rather than mean and standard
    /// deviation, because the case of interest is one work far from the rest
    /// and that is exactly what would drag a mean and inflate an SD until the
    /// outlier no longer looked like one.
    /// </summary>
    public static CrossWorkSummary CompareWorks(
        IReadOnlyList<(string Title, double Baseline, double Drop)> works)
    {
        if (works.Count < 3)
        {
            return new CrossWorkSummary(
                works.Select(w => new WorkResponse(w.Title, w.Baseline, w.Drop, w.Drop, 0, 0)).ToList(),
                0, 0, 0, 0, 0);
        }

        var baselines = works.Select(w => w.Baseline).ToList();
        var magnitudes = works.Select(w => Math.Abs(w.Drop)).ToList();

        var meanBase = baselines.Average();
        var meanDrop = magnitudes.Average();

        var variance = baselines.Sum(b => (b - meanBase) * (b - meanBase));
        var slope = variance < 1e-12
            ? 0
            : baselines.Zip(magnitudes, (b, d) => (b - meanBase) * (d - meanDrop)).Sum() / variance;
        var intercept = meanDrop - slope * meanBase;

        var residuals = works
            .Select((w, i) => magnitudes[i] - (intercept + slope * baselines[i]))
            .ToList();

        var sorted = residuals.OrderBy(r => r).ToList();
        var median = sorted[sorted.Count / 2];

        var deviations = residuals.Select(r => Math.Abs(r - median)).OrderBy(d => d).ToList();
        var mad = deviations[deviations.Count / 2];

        var responses = works
            .Select((w, i) => new WorkResponse(
                w.Title,
                w.Baseline,
                w.Drop,
                -(intercept + slope * baselines[i]),
                residuals[i],
                mad < 1e-12 ? 0 : Math.Abs(residuals[i] - median) / mad))
            .ToList();

        return new CrossWorkSummary(
            responses,
            ValidationResult.Spearman(baselines, magnitudes),
            slope, intercept, median, mad);
    }

    /// <summary>
    /// The first level at which recovery drops below the given reliability,
    /// or null if it never does.
    ///
    /// EXPECT NULL. Recovery is a sign test on the margin, and on real text the
    /// sign is remarkably hard to flip: contaminating a Euripides play with 50%
    /// synthetic Sophocles left the margin positive in twelve trials out of
    /// twelve, at 29% of its uncontaminated value. A text can lose
    /// seven-tenths of its authorial signal and still be "recovered".
    ///
    /// Which makes this the wrong headline for a sensitivity series, and it is
    /// kept only because a series that DID break somewhere would be worth
    /// knowing about. Use <see cref="LevelWhereMarginFallsBelow"/> instead:
    /// the informative quantity is how much of the margin survives, not whether
    /// what survives is still above zero.
    /// </summary>
    public static double? BreakingPoint(
        IReadOnlyList<PerturbationLevel> series, double reliability = 0.5) =>
        series
            .OrderBy(l => l.InjectionFraction)
            .FirstOrDefault(l => l.InjectionFraction > 0 && l.RecoveryRate < reliability)
            ?.InjectionFraction;

    /// <summary>
    /// The first injection level whose mean margin falls below the given
    /// proportion of the uncontaminated baseline.
    ///
    /// This is the statistic a sensitivity series is actually for. On the
    /// Euripides pool the decay is close to linear - roughly a percent of the
    /// baseline margin lost per percent of Sophoclean material up to about 20%,
    /// steepening after - so a proportion maps onto a contamination level
    /// legibly, in a way that a sign test does not.
    ///
    /// Expressed as a proportion rather than in absolute Delta because absolute
    /// margin moves by a factor of four and a half on preprocessing alone.
    /// </summary>
    public static double? LevelWhereMarginFallsBelow(
        IReadOnlyList<PerturbationLevel> series, double proportion) =>
        series
            .OrderBy(l => l.InjectionFraction)
            .FirstOrDefault(l => l.InjectionFraction > 0 && l.ProportionOfBaseline < proportion)
            ?.InjectionFraction;

    /// <summary>
    /// Reads a finished series back in the only terms it supports.
    ///
    /// The wording is deliberate and the caveat is not decoration. A series
    /// establishes how much synthetic disturbance the METHOD absorbs on a text
    /// whose authorship is not in doubt. It does not establish what proportion
    /// of any real text was written by somebody else, and the sentence that
    /// would say so is the one most likely to be quoted out of this tool.
    /// </summary>
    public static string Summarise(IReadOnlyList<PerturbationLevel> series, string targetTitle)
    {
        var run = series.Where(l => l.Trials.Count > 0).OrderBy(l => l.InjectionFraction).ToList();
        if (run.Count < 2) return "Not enough levels to read a curve.";

        var baseline = run[0].BaselineMargin;
        var last = run[^1];
        var half = LevelWhereMarginFallsBelow(run, 0.5);
        var broke = BreakingPoint(run);

        var direction = last.MeanMargin > baseline
            ? "ROSE"
            : "fell";

        return
            $"{targetTitle}: baseline margin {StatFormat.Signed3(baseline)}. " +
            $"At {last.InjectionFraction:P0} injection the mean margin {direction} to " +
            $"{StatFormat.Signed3(last.MeanMargin)} ({last.ProportionOfBaseline:P0} of baseline), " +
            $"recovering in {last.RecoveredCount} of {last.Trials.Count} mixtures. " +
            $"That is {StatFormat.Signed3(last.AbsoluteShift)} in Delta - the quantity that compares " +
            $"across works, where a percentage and an SD count do not. " +
            (half.HasValue
                ? $"Half the margin was gone by {half.Value:P0}. "
                : "The margin never fell by half. ") +
            (broke.HasValue
                ? $"Recovery became unreliable at {broke.Value:P0}. "
                : "Recovery never became unreliable - the sign of the margin is a blunt test. ") +
            "This measures how much disturbance the method absorbs on a text of known authorship. " +
            "It is not an estimate of how much of any real text somebody else wrote.";
    }
}
