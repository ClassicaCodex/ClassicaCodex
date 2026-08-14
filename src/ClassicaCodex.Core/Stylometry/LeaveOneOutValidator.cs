namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// One work's result in a validation sweep.
/// </summary>
/// <param name="Margin">
/// Mean Delta from this work's samples to samples by OTHER authors, minus mean
/// Delta to samples by the SAME author. Positive means the work sits closer to
/// its own author's company than to everyone else's - which is what "recovered"
/// means here.
///
/// This is the headline, and it is a difference of distances rather than a
/// rank, which is the whole point. See <see cref="LeaveOneOutValidator"/>.
/// </param>
/// <param name="CorrectAuthorRank">
/// Where the first same-author sample appears in the ranked neighbour list.
/// 1 means the nearest neighbour is by the right author.
///
/// RECORDED AS A DIAGNOSTIC, NOT AS EVIDENCE. Rank measures on this corpus have
/// a history: depth to first outsider looked stable across feature counts and
/// then moved twenty ranks for a single work on a 500-token change of sample
/// size, because a rank position depends on what else is in the pool. This may
/// behave the same way. It is kept so the grid can find out rather than so the
/// question can be settled by assertion - if it tracks sample count the way
/// depth did, that will show up as a correlation and the column can go.
/// </param>
public sealed record WorkValidation(
    int WorkId,
    string AuthorName,
    string WorkTitle,
    double Margin,
    double MeanDeltaSameAuthor,
    double MeanDeltaOtherAuthor,
    double DeltaFloor,
    string NearestAuthor,
    string NearestLabel,
    int? CorrectAuthorRank,
    int SamplesMeasured,
    int TokenCount)
{
    /// <summary>Whether the work sat closer to its own author than to the rest.</summary>
    public bool Recovered => Margin > 0;
}

/// <summary>
/// How hard the pool was.
///
/// A recovery rate means nothing on its own. Measured against Greek prose,
/// every tragedy recovers and the harness reports 100% - a validation that
/// cannot fail has not tested anything. Measured against Aeschylus, the
/// between-author signal is roughly a tenth of ordinary within-Euripides
/// variation and the same harness struggles.
///
/// So the difficulty travels with the result. Mean within-author Delta and
/// mean cross-author Delta, and the gap between them, say what the recovery
/// rate was earned against.
/// </summary>
public sealed record PoolDifficulty(
    double MeanWithinAuthorDelta,
    double MeanCrossAuthorDelta,
    int AuthorCount,
    int SampleCount,
    string LargestAuthor,
    double LargestAuthorSampleShare)
{
    public double Separation => MeanCrossAuthorDelta - MeanWithinAuthorDelta;

    /// <summary>
    /// Whether one author supplies enough of the pool to define "other" by
    /// itself.
    ///
    /// Margin is a difference of means over the outsider samples. An author
    /// contributing most of them is not one voice among many in that mean, it
    /// is most of it - and it also dominates the z-score normalisation every
    /// other sample is expressed in. Half is an arbitrary line for an
    /// arbitrary warning; it is there to prompt a look, not to block a run.
    /// </summary>
    public bool IsImbalanced => LargestAuthorSampleShare > 0.5;
}

/// <summary>Everything a validation sweep produced.</summary>
public sealed record ValidationResult(
    string TargetAuthor,
    IReadOnlyList<WorkValidation> Works,
    PoolDifficulty Difficulty,
    DeltaSettings Settings,
    bool HeldOutWorkExcludedFromNormalisation,
    IReadOnlyList<string> Skipped)
{
    public int RecoveredCount => Works.Count(w => w.Recovered);

    public double RecoveryRate => Works.Count == 0 ? 0 : (double)RecoveredCount / Works.Count;

    /// <summary>Mean margin across works - the sweep's single summary number.</summary>
    public double MeanMargin => Works.Count == 0 ? 0 : Works.Average(w => w.Margin);

    /// <summary>
    /// Longest work over shortest, in tokens.
    ///
    /// Reported next to <see cref="MarginLengthCorrelation"/> because that
    /// number cannot be read without it. A correlation needs spread on both
    /// axes, so an author whose works are all one size cannot produce a high
    /// rho whatever the method is doing - and a low rho then says nothing about
    /// the method at all.
    ///
    /// This is not hypothetical. At 2,500-token samples Euripides returns rho
    /// +0.64 and Aristophanes +0.36 on the same settings, the same pool
    /// structure and the same filters. Euripides spans 4,141 to 10,060 tokens,
    /// a ratio of 2.4; Aristophanes spans 7,213 to 10,750, a ratio of 1.5. The
    /// safer reading of that pair is not that the method behaves better on
    /// comedy, but that Aristophanes gives it less room to misbehave visibly.
    /// </summary>
    public double LengthSpread
    {
        get
        {
            if (Works.Count < 2) return 1;
            var shortest = Works.Min(w => w.TokenCount);
            return shortest <= 0 ? 1 : (double)Works.Max(w => w.TokenCount) / shortest;
        }
    }

    /// <summary>
    /// A 95% interval for <see cref="MarginLengthCorrelation"/>, by Fisher's z
    /// transform.
    ///
    /// THIS IS HERE BECAUSE THE NUMBER IS LESS PRECISE THAN IT LOOKS, AND A
    /// PARAMETER GRID PUTS FORTY OF THEM IN A SORTED COLUMN. Nineteen works is
    /// a small sample for a rank correlation: rho +0.42 over nineteen carries
    /// an interval of roughly [-0.04, +0.73], and rho +0.73 carries
    /// [+0.41, +0.89]. Those overlap almost entirely.
    ///
    /// Which means a grid spanning +0.42 to +0.73 has NOT found that some
    /// settings are better than others. The whole spread sits inside the
    /// estimation error of a single cell - best against worst is 1.36 standard
    /// errors, where about two would be needed, and telling those two values
    /// apart would take roughly fifty works rather than nineteen.
    ///
    /// So the honest reading of such a grid is "rho is around +0.55 everywhere
    /// and the corpus is too small to say more", not "3,000 tokens at 100 MFW
    /// is the best configuration". Picking the top row of a sorted column is
    /// how the last set of promising results happened.
    /// </summary>
    public (double Low, double High) MarginLengthCorrelationInterval =>
        FisherInterval(MarginLengthCorrelation, Works.Count);

    /// <summary>
    /// 95% interval for a correlation, via the transform that makes one
    /// approximately normal. Returns the point estimate itself when the sample
    /// is too small or the correlation is degenerate, so callers get a
    /// zero-width interval rather than an exception.
    ///
    /// Public because both forms display it and they live in another assembly.
    /// Spearman below stays internal - only the tests call it, and only to
    /// check that its sign has not been inverted.
    /// </summary>
    public static (double Low, double High) FisherInterval(double rho, int n)
    {
        if (n < 5 || Math.Abs(rho) >= 0.999) return (rho, rho);

        var z = Math.Atanh(rho);
        var se = 1.0 / Math.Sqrt(n - 3);

        return (Math.Tanh(z - 1.96 * se), Math.Tanh(z + 1.96 * se));
    }

    /// <summary>
    /// Spearman rho between a work's token count and its margin.
    ///
    /// THIS IS NOT AN OPTIONAL DIAGNOSTIC AND IT IS NOT ON A SEPARATE TAB. The
    /// first run of this harness - nineteen Euripides plays against Sophocles
    /// and Aeschylus, 2,500-token samples, 150 features - recovered 19 of 19,
    /// and the margin it recovered them by correlated with text length at
    /// rho +0.62. Depth to first outsider, the measure this one was built to
    /// replace, correlated at +0.58.
    ///
    /// A recovery rate reported without this number beside it is the same
    /// mistake in a new statistic, and the only reason it was caught is that
    /// somebody ran the check. So it runs every time.
    ///
    /// What a high value means: the sweep may be sorting works by how much text
    /// they have rather than by who wrote them. It does not mean the margin is
    /// worthless - it means a margin difference between two works of very
    /// different length is not interpretable on its own, and that the parameter
    /// grid should be searched for regions where this correlation is low rather
    /// than where the recovery rate is high.
    /// </summary>
    public double MarginLengthCorrelation =>
        Spearman(Works.Select(w => (double)w.TokenCount), Works.Select(w => w.Margin));

    /// <summary>
    /// The same check against sample count, which is where the depth confound
    /// went to hide when equal-size sampling removed the raw length effect.
    /// Length and sample count are near-collinear at fixed sample size, so
    /// these two usually agree; when they do not, the difference is the part of
    /// the effect that survives equalisation.
    /// </summary>
    public double MarginSampleCountCorrelation =>
        Spearman(Works.Select(w => (double)w.SamplesMeasured), Works.Select(w => w.Margin));

    /// <summary>
    /// Spearman rank correlation, ties averaged.
    ///
    /// Ranks rather than raw values because the question is monotonic - does
    /// margin rise with length - and because with nineteen points one long
    /// outlier would otherwise set a Pearson coefficient on its own.
    /// </summary>
    internal static double Spearman(IEnumerable<double> xs, IEnumerable<double> ys)
    {
        var x = xs.ToList();
        var y = ys.ToList();
        if (x.Count < 3 || x.Count != y.Count) return 0;

        var rx = AverageRanks(x);
        var ry = AverageRanks(y);

        var mx = rx.Average();
        var my = ry.Average();

        double num = 0, dx = 0, dy = 0;
        for (var i = 0; i < rx.Count; i++)
        {
            num += (rx[i] - mx) * (ry[i] - my);
            dx += (rx[i] - mx) * (rx[i] - mx);
            dy += (ry[i] - my) * (ry[i] - my);
        }

        var den = Math.Sqrt(dx * dy);
        return den < 1e-12 ? 0 : num / den;
    }

    private static List<double> AverageRanks(List<double> values)
    {
        var order = Enumerable.Range(0, values.Count).OrderBy(i => values[i]).ToList();
        var ranks = new double[values.Count];

        var i2 = 0;
        while (i2 < order.Count)
        {
            var j = i2;
            while (j + 1 < order.Count && values[order[j + 1]].Equals(values[order[i2]])) j++;

            var average = (i2 + j) / 2.0 + 1;
            for (var k = i2; k <= j; k++) ranks[order[k]] = average;
            i2 = j + 1;
        }

        return ranks.ToList();
    }
}

/// <summary>
/// Asks whether the method recovers texts whose authorship is not in question,
/// before it is pointed at one that is.
///
/// WHAT "LEAVE ONE OUT" CAN AND CANNOT MEAN HERE. The usual procedure builds a
/// profile per author, removes a work from its author's profile, and asks
/// whether the classifier puts it back. DeltaEngine has no per-author profile
/// to remove anything from - it compares sample against sample and never
/// aggregates, deliberately, because aggregating reintroduces the length
/// confound (see the comment on DeltaEngine.Compute). So there is no author
/// model here to hold a work out of.
///
/// What is left out is the work's own samples from its own neighbour list,
/// which DeltaEngine already does, and - optionally - the work's contribution
/// to the feature set and the z-scores. That second one is the only real
/// leakage available to remove, and it is a flag rather than a default because
/// its size is a measurable question rather than a known quantity. With a
/// nineteen-work Euripides pool at 2,500 tokens the held-out work is about 5%
/// of the normalisation it is then scored against. Whether 5% moves the answer
/// is exactly the kind of thing this harness exists to find out.
///
/// WHY MARGIN AND NOT RANK. docs/stylometry-notes.md records what happened the
/// last time a rank was the headline: depth to first outsider tracked text
/// length at rho 0.58, and once samples were equalised it tracked sample count
/// instead - 6.5 mean depth for one-sample works, 12.9 for two, 20.0 for
/// three. A rank position is a statement about pool composition wearing the
/// clothes of a statement about the text. A difference of mean distances is
/// not: it does not care how many other samples happen to sit between.
///
/// Rank is recorded anyway, per work, so the parameter grid can test whether
/// it behaves any better here than it did there. Recording it costs nothing;
/// believing it in advance costs everything.
/// </summary>
public static class LeaveOneOutValidator
{
    /// <summary>
    /// Runs every eligible work by one author against the pool.
    /// </summary>
    /// <param name="pool">
    /// The comparison set, already tokenised. Must contain the target author's
    /// works and at least one other author's - a margin is a comparison and
    /// there is nothing to compare a single author against.
    /// </param>
    /// <param name="targetAuthor">Whose works are held out, one at a time.</param>
    /// <param name="excludeHeldOutFromNormalisation">
    /// When true, the held-out work is removed from the pool entirely for its
    /// own run, so it contributes to neither the feature set nor any z-score.
    /// Costs one extra pass per work and removes the only leakage available.
    /// </param>
    /// <param name="progress">Called with (workIndex, workCount) before each work.</param>
    public static ValidationResult Validate(
        IReadOnlyList<WorkTokens> pool,
        string targetAuthor,
        DeltaSettings settings,
        bool excludeHeldOutFromNormalisation = false,
        Action<int, int>? progress = null,
        CancellationToken cancellation = default)
    {
        var distinct = pool.GroupBy(w => w.WorkId).Select(g => g.First()).ToList();

        var authors = distinct.Select(w => w.AuthorName).Distinct().Count();
        if (authors < 2)
        {
            throw new InvalidOperationException(
                "A margin compares a work's own author against other authors, so the pool needs at " +
                "least two. With one author the useful measures are Delta floor and dispersion, " +
                "which are a different question.");
        }

        var targets = distinct
            .Where(w => string.Equals(w.AuthorName, targetAuthor, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.WorkTitle)
            .ToList();

        if (targets.Count == 0)
            throw new InvalidOperationException($"No works by {targetAuthor} in the pool.");

        var works = new List<WorkValidation>();
        var skipped = new List<string>();

        for (var i = 0; i < targets.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(i, targets.Count);

            var target = targets[i];

            var sampleCount = DeltaEngine.ChunkCountFor(target.Tokens.Count, settings.ChunkSize);
            if (sampleCount < 1)
            {
                skipped.Add($"{target.WorkTitle} (under {settings.ChunkSize:N0} tokens)");
                continue;
            }

            // The held-out work stays IN the pool - the engine has to sample it
            // to measure it. What excludeHeldOutFromNormalisation does is stop
            // it contributing to the feature set and the z-scores it is then
            // scored against. Removing it from the pool list instead would
            // simply remove the target, which is not the same thing and is the
            // mistake this comment exists to prevent someone repeating.
            var validation = ValidateOne(
                distinct, target, settings, sampleCount, excludeHeldOutFromNormalisation);

            if (validation == null) skipped.Add($"{target.WorkTitle} (no comparable samples)");
            else works.Add(validation);
        }

        if (works.Count == 0)
        {
            // Not a recovery rate of zero. A sweep in which no work could be
            // tested has not failed to recover anything - it has not asked
            // anything, and reporting 0% would put a configuration that never
            // ran alongside one that ran and got everything wrong.
            //
            // The usual cause is a sample size larger than any work the author
            // has, which a parameter grid will reach on its own and should show
            // as an impossible cell rather than a bad score.
            throw new InvalidOperationException(
                $"No work by {targetAuthor} yields a sample of {settings.ChunkSize:N0} tokens" +
                (skipped.Count > 0 ? $" ({string.Join("; ", skipped)})" : "") +
                ". Lower the sample size.");
        }

        return new ValidationResult(
            targetAuthor,
            works,
            MeasureDifficulty(distinct, settings),
            settings,
            excludeHeldOutFromNormalisation,
            skipped);
    }

    /// <summary>
    /// One work, measured from every sample it yields rather than only its
    /// first.
    ///
    /// The form has always read the first sample and discarded the rest, which
    /// at 2,500 tokens throws away 30 of Euripides' 49 samples. For a single
    /// exploratory run that is defensible; for a statistic meant to say whether
    /// a work was recovered it is a coin flip on which bag was drawn first.
    /// Averaging over the samples is the cheapest variance reduction available
    /// and needs no new machinery.
    /// </summary>
    private static WorkValidation? ValidateOne(
        IReadOnlyList<WorkTokens> pool,
        WorkTokens target,
        DeltaSettings settings,
        int sampleCount,
        bool excluded)
    {
        var sameTotals = new List<double>();
        var otherTotals = new List<double>();
        var floors = new List<double>();
        var ranks = new List<int>();
        string nearestAuthor = string.Empty;
        string nearestLabel = string.Empty;
        var bestFloor = double.MaxValue;
        var measured = 0;

        for (var s = 0; s < sampleCount; s++)
        {
            DeltaResult run;
            try
            {
                run = DeltaEngine.Compute(pool, target.WorkId, settings, s, excluded);
            }
            catch (InvalidOperationException)
            {
                // Too short at this sample size once the pool is assembled.
                break;
            }

            // With the held-out work excluded from normalisation its own
            // samples are the only ones by it in the pool, so "same author"
            // means its author's OTHER works either way.
            var same = run.Neighbors
                .Where(n => string.Equals(n.AuthorName, target.AuthorName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var other = run.Neighbors
                .Where(n => !string.Equals(n.AuthorName, target.AuthorName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (same.Count == 0 || other.Count == 0) continue;

            measured++;
            sameTotals.Add(same.Average(n => n.Delta));
            otherTotals.Add(other.Average(n => n.Delta));

            var nearest = run.Neighbors[0];
            floors.Add(nearest.Delta);
            if (nearest.Delta < bestFloor)
            {
                bestFloor = nearest.Delta;
                nearestAuthor = nearest.AuthorName;
                nearestLabel = nearest.Label;
            }

            var rank = run.Neighbors
                .Select((n, index) => (n, index))
                .Where(x => string.Equals(x.n.AuthorName, target.AuthorName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index + 1)
                .FirstOrDefault();
            if (rank > 0) ranks.Add(rank);
        }

        if (measured == 0) return null;

        var meanSame = sameTotals.Average();
        var meanOther = otherTotals.Average();

        return new WorkValidation(
            target.WorkId,
            target.AuthorName,
            target.WorkTitle,
            meanOther - meanSame,
            meanSame,
            meanOther,
            floors.Average(),
            nearestAuthor,
            nearestLabel,
            ranks.Count == 0 ? null : (int)Math.Round(ranks.Average()),
            measured,
            target.Tokens.Count);
    }

    /// <summary>
    /// How separable the pool's authors are before any work is held out.
    ///
    /// Computed once over the whole pool rather than per work, because it
    /// describes the pool rather than any run. Every sample is compared to
    /// every other, which is O(n^2) in samples - fine at the few hundred a
    /// research pool holds, and the reason this is not offered corpus-wide.
    /// </summary>
    public static PoolDifficulty MeasureDifficulty(
        IReadOnlyList<WorkTokens> pool, DeltaSettings settings)
    {
        var distinct = pool.GroupBy(w => w.WorkId).Select(g => g.First()).ToList();

        var samples = new List<(string Author, int WorkId, Dictionary<string, int> Counts, int Total)>();

        foreach (var work in distinct)
        {
            if (settings.ChunkSize <= 0)
            {
                var counts = new Dictionary<string, int>();
                foreach (var t in work.Tokens) counts[t] = counts.GetValueOrDefault(t) + 1;
                samples.Add((work.AuthorName, work.WorkId, counts, Math.Max(work.Tokens.Count, 1)));
                continue;
            }

            foreach (var bag in DeltaEngine.SplitIntoChunks(work.Tokens, settings.ChunkSize, work.WorkId))
            {
                var counts = new Dictionary<string, int>();
                foreach (var t in bag) counts[t] = counts.GetValueOrDefault(t) + 1;
                samples.Add((work.AuthorName, work.WorkId, counts, settings.ChunkSize));
            }
        }

        var byAuthor = samples.GroupBy(s => s.Author).ToList();
        var largest = byAuthor.OrderByDescending(g => g.Count()).FirstOrDefault();

        if (samples.Count < 2)
        {
            return new PoolDifficulty(0, 0, byAuthor.Count, samples.Count,
                largest?.Key ?? string.Empty, largest == null ? 0 : 1);
        }

        var aggregate = new Dictionary<string, int>();
        foreach (var s in samples)
            foreach (var (word, count) in s.Counts)
                aggregate[word] = aggregate.GetValueOrDefault(word) + count;

        var featureWords = aggregate
            .OrderByDescending(kv => kv.Value)
            .Take(settings.FeatureWordCount)
            .Select(kv => kv.Key)
            .ToList();

        var relFreq = samples
            .Select(s => featureWords.ToDictionary(w => w, w => (double)s.Counts.GetValueOrDefault(w) / s.Total))
            .ToList();

        var z = Enumerable.Range(0, samples.Count).Select(_ => new Dictionary<string, double>()).ToList();
        foreach (var word in featureWords)
        {
            var values = relFreq.Select(r => r[word]).ToList();
            var mean = values.Average();
            var stdev = Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
            if (stdev < 1e-9) stdev = 1e-9;
            for (var i = 0; i < samples.Count; i++) z[i][word] = (relFreq[i][word] - mean) / stdev;
        }

        double withinSum = 0, crossSum = 0;
        var withinCount = 0;
        var crossCount = 0;

        for (var i = 0; i < samples.Count; i++)
        {
            for (var j = i + 1; j < samples.Count; j++)
            {
                // Samples of the SAME work are not a within-author observation
                // in any useful sense - two bags of one text are about as close
                // as two bags get, and including them would make every pool
                // look easier than it is.
                if (samples[i].WorkId == samples[j].WorkId) continue;

                var d = featureWords.Average(w => Math.Abs(z[i][w] - z[j][w]));

                if (string.Equals(samples[i].Author, samples[j].Author, StringComparison.OrdinalIgnoreCase))
                {
                    withinSum += d;
                    withinCount++;
                }
                else
                {
                    crossSum += d;
                    crossCount++;
                }
            }
        }

        return new PoolDifficulty(
            withinCount == 0 ? 0 : withinSum / withinCount,
            crossCount == 0 ? 0 : crossSum / crossCount,
            byAuthor.Count,
            samples.Count,
            largest?.Key ?? string.Empty,
            largest == null ? 0 : (double)largest.Count() / samples.Count);
    }
}
