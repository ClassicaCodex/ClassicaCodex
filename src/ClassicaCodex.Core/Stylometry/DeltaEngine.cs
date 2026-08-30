namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// Burrows' Delta over a pool of works, optionally at fixed sample size.
///
/// Moved out of StylometryForm without changing what it computes. It was a
/// private method on a WinForms class, which meant the only way to run it was
/// to open a window and press a button - workable for one run at a time, and
/// not workable for leave-one-out validation, a parameter grid, or a
/// sensitivity series, all of which call it hundreds to thousands of times
/// with no window in sight.
///
/// The comments below are the original ones. They record decisions that are
/// easy to undo by accident, and every one of them is load-bearing.
/// </summary>
public static class DeltaEngine
{
    /// <summary>
    /// Splits a token list into equal-size samples.
    ///
    /// WHY SAMPLE RATHER THAN SLICE. Eder found randomly drawn bags of words
    /// outperform contiguous passages of the same size for attribution: a
    /// contiguous passage carries local subject matter - one episode, one
    /// speaker, one messenger speech - and that shows up in word frequencies as
    /// though it were style. Drawing across the whole work averages the topic
    /// out and leaves the habitual vocabulary, which is what Delta is for.
    ///
    /// The shuffle is seeded from the work id, so a given work always yields
    /// the same chunks. Reproducibility matters more here than fresh randomness:
    /// a run that cannot be repeated cannot be compared against.
    ///
    /// Remainder tokens are discarded. Keeping a short final chunk would put a
    /// noisier sample into the same pool as full ones and reintroduce exactly
    /// the length effect this is meant to remove. The cost is real - Rhesus at
    /// 5,431 tokens yields one 3,000-token chunk and 2,431 tokens go unused -
    /// and it is reported so the discard is visible rather than silent.
    ///
    /// A CONSEQUENCE WORTH KNOWING BEFORE BUILDING EXPERIMENTS ON IT. Because
    /// the seed is the work id, a bag is a fixed function of (work, chunk size,
    /// accent folding). Two runs at the same settings draw the same bags. That
    /// is what makes runs comparable, and it also means a perturbation
    /// experiment cannot get variation by re-running: the variation has to come
    /// from the injection's own seed.
    /// </summary>
    public static List<List<string>> SplitIntoChunks(IReadOnlyList<string> tokens, int chunkSize, int seed)
    {
        var chunkCount = tokens.Count / chunkSize;
        if (chunkCount < 1) return new List<List<string>>();

        var order = Enumerable.Range(0, tokens.Count).ToArray();
        var rng = new Random(seed);
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var result = new List<List<string>>(chunkCount);
        for (var c = 0; c < chunkCount; c++)
        {
            var bag = new List<string>(chunkSize);
            for (var k = c * chunkSize; k < (c + 1) * chunkSize; k++) bag.Add(tokens[order[k]]);
            result.Add(bag);
        }
        return result;
    }

    /// <summary>
    /// Splits a set of works into comparison units once, for reuse.
    ///
    /// A perturbation series calls Compute once per sample per iteration per
    /// level - around 375 times for one work at the coarse preset - and every
    /// one of those calls re-splits the ENTIRE pool, though only the target's
    /// tokens have changed. On a 34-work tragic pool that is a quarter of a
    /// million tokens shuffled and bagged per call, thrown away, and done
    /// again. Across nineteen works it is billions of token copies to answer a
    /// question about nineteen texts.
    ///
    /// Caching is behaviour-preserving by construction rather than by care:
    /// SplitIntoChunks is deterministic in (tokens, chunk size, work id), so a
    /// cached bag is the same bag it would have recomputed. The target must NOT
    /// be cached - its tokens are what the experiment changes.
    /// </summary>
    public static Dictionary<int, List<StylometryChunk>> ChunkWorks(
        IReadOnlyList<WorkTokens> works, int chunkSize)
    {
        var cache = new Dictionary<int, List<StylometryChunk>>();

        foreach (var work in works.GroupBy(w => w.WorkId).Select(g => g.First()))
            cache[work.WorkId] = ChunksFor(work, chunkSize);

        return cache;
    }

    private static List<StylometryChunk> ChunksFor(WorkTokens work, int chunkSize)
    {
        var chunks = new List<StylometryChunk>();

        if (chunkSize <= 0)
        {
            var counts = new Dictionary<string, int>();
            foreach (var t in work.Tokens) counts[t] = counts.GetValueOrDefault(t) + 1;
            chunks.Add(new StylometryChunk
            {
                WorkId = work.WorkId,
                AuthorName = work.AuthorName,
                WorkTitle = work.WorkTitle,
                ChunkIndex = 0,
                ChunkCount = 1,
                Counts = counts,
                TotalTokens = Math.Max(work.Tokens.Count, 1)
            });
            return chunks;
        }

        var bags = SplitIntoChunks(work.Tokens, chunkSize, work.WorkId);

        for (var i = 0; i < bags.Count; i++)
        {
            var counts = new Dictionary<string, int>();
            foreach (var t in bags[i]) counts[t] = counts.GetValueOrDefault(t) + 1;
            chunks.Add(new StylometryChunk
            {
                WorkId = work.WorkId,
                AuthorName = work.AuthorName,
                WorkTitle = work.WorkTitle,
                ChunkIndex = i + 1,
                ChunkCount = bags.Count,
                Counts = counts,
                TotalTokens = chunkSize
            });
        }

        return chunks;
    }

    /// <summary>
    /// Burrows' Delta from a target work to every other unit in the pool.
    ///
    /// WHY CHUNKING EXISTS. Without it, depth to first outsider tracks text
    /// length: across the Euripides corpus the four shortest works are the four
    /// shallowest, and Rhesus - second shortest - sits exactly where its length
    /// predicts. A shorter text gives noisier relative-frequency estimates,
    /// which inflates its Delta against everything and lets other authors rise
    /// earlier in its ranking. That is a property of the sample, not the author.
    ///
    /// With chunkSize > 0 every comparison unit holds the same number of tokens,
    /// so that particular explanation is removed rather than measured. If a work
    /// still ranks shallow at equal length, length is no longer available as the
    /// reason.
    ///
    /// WHY NO AGGREGATION BACK TO WORKS. Chunks stay chunks all the way through.
    /// Collapsing them into a per-work distance requires choosing between the
    /// mean of pairwise chunk distances - which rewards works with more chunks,
    /// i.e. longer works - and the minimum, which rewards them differently by
    /// giving them more chances at a close match. Both smuggle length back in.
    /// Leaving the unit as the chunk avoids the choice: every chunk is one
    /// observation of equal size, and a long work simply contributes more
    /// observations.
    ///
    /// The consequence for reading results: a work with three chunks appears
    /// three times, and the target work's own chunks are excluded so it cannot
    /// match itself.
    ///
    /// AND THE CONSEQUENCE FOR VALIDATION, which matters now that experiments
    /// are being built on this. There is no per-author model here to hold a
    /// work out of - the comparison is chunk against chunk. A leave-one-out
    /// procedure therefore cannot mean "rebuild the author's profile without
    /// this work"; it means "ask where this work's chunks land among everyone
    /// else's", and its result has to be read as a distance rather than a rank.
    /// See docs/stylometry-notes.md for what happened the last time a rank was
    /// used as the headline.
    /// </summary>
    /// <param name="targetWorkId">
    /// Which work in the pool is the target. It must be present in
    /// <paramref name="pool"/>: its own chunks contribute to the feature set
    /// and to every z-score, and are then excluded from the results.
    /// </param>
    /// <param name="targetChunkIndex">
    /// Which of the target's chunks to measure from, zero-based.
    ///
    /// The form has always used the first, and that is still the default. It is
    /// a parameter now because validation should not judge a three-chunk work
    /// on one of its chunks and discard the other two - and because reading the
    /// same work from each of its chunks in turn is the cheapest available
    /// estimate of how much a single run's answer depends on which sample it
    /// happened to draw.
    /// </param>
    /// <param name="excludeTargetFromNormalisation">
    /// When true, the target's own samples are left out of the feature-set
    /// selection and out of every feature's mean and standard deviation. The
    /// target is still measured - it is scored against statistics computed
    /// without it.
    ///
    /// The form has never done this and every saved run assumes it was not
    /// done, which is why it is off by default. It exists because a validation
    /// harness has a stricter standard than an exploratory run: with a
    /// nineteen-work Euripides pool at 2,500 tokens the held-out work supplies
    /// about 5% of the normalisation it is then scored against, and a work
    /// helping to define the scale it is measured on is leakage however small
    /// the share.
    ///
    /// Whether 5% moves the answer is a measurable question and not one to
    /// settle by argument, which is the whole reason this is a parameter and
    /// the validator reports which setting produced a result.
    /// </param>
    /// <param name="cachedChunks">
    /// Chunks already computed for works whose tokens have not changed, from
    /// <see cref="ChunkWorks"/>. Optional, and never required for correctness -
    /// a cached entry is exactly what would have been recomputed. Omit the
    /// target: its tokens are the thing under experiment.
    /// </param>
    public static DeltaResult Compute(
        IReadOnlyList<WorkTokens> pool,
        int targetWorkId,
        DeltaSettings settings,
        int targetChunkIndex = 0,
        bool excludeTargetFromNormalisation = false,
        IReadOnlyDictionary<int, List<StylometryChunk>>? cachedChunks = null)
    {
        // 0. One entry per WORK, not per EDITION.
        //
        // A work can carry several editions. Left unhandled, a work with three
        // editions contributes its values three times to every feature's mean
        // and standard deviation. The multi-edition works cluster in Aeschylus
        // and Sophocles while most Euripides plays carry one, so the
        // normalisation ends up weighted toward exactly the authors a Euripides
        // comparison is measured against.
        var distinctPool = pool
            .GroupBy(w => w.WorkId)
            .Select(g => g.First())
            .ToList();

        var chunkSize = settings.ChunkSize;

        // 1. Split into comparison units, reusing any already computed for a
        //    work whose tokens have not changed.
        var chunks = new List<StylometryChunk>();
        var targetTotalTokens = 0;
        var droppedShort = new List<string>();
        var discardedTokens = 0;

        foreach (var work in distinctPool)
        {
            if (work.WorkId == targetWorkId) targetTotalTokens = work.Tokens.Count;

            // Never take the target from the cache: a perturbation experiment
            // changes its tokens on every iteration, and a stale bag there
            // would silently measure the uncontaminated text.
            var forWork = work.WorkId != targetWorkId
                          && cachedChunks != null
                          && cachedChunks.TryGetValue(work.WorkId, out var cached)
                ? cached
                : ChunksFor(work, chunkSize);

            if (forWork.Count == 0)
            {
                // Shorter than one chunk. Dropped rather than padded or kept
                // whole - a short unit in a pool of full-size ones is the
                // problem chunking exists to solve.
                droppedShort.Add(work.WorkTitle);
                continue;
            }

            if (chunkSize > 0)
                discardedTokens += work.Tokens.Count - forWork.Count * chunkSize;

            chunks.AddRange(forWork);
        }

        var targetChunks = chunks.Where(c => c.WorkId == targetWorkId).ToList();
        if (targetChunks.Count == 0)
        {
            var title = distinctPool.FirstOrDefault(w => w.WorkId == targetWorkId)?.WorkTitle
                        ?? $"work {targetWorkId}";
            throw new InvalidOperationException(
                $"{title} has fewer than {chunkSize:N0} tokens, so it cannot be compared at " +
                "this sample size. Lower the sample size or turn chunking off.");
        }

        if (targetChunkIndex < 0 || targetChunkIndex >= targetChunks.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetChunkIndex),
                $"{targetChunks[0].WorkTitle} yielded {targetChunks.Count} sample(s) at this size; " +
                $"sample {targetChunkIndex} was asked for.");
        }

        // 2. Feature set: the N most frequent words across the normalisation
        //    basis - every unit, or every unit but the target's own.
        var basis = excludeTargetFromNormalisation
            ? chunks.Where(c => c.WorkId != targetWorkId).ToList()
            : chunks;

        if (basis.Count == 0)
        {
            throw new InvalidOperationException(
                "Excluding the target leaves nothing to normalise against - the pool holds only the " +
                "target's own work.");
        }

        var aggregate = new Dictionary<string, int>();
        foreach (var c in basis)
            foreach (var (word, count) in c.Counts)
                aggregate[word] = aggregate.GetValueOrDefault(word) + count;

        var featureWords = aggregate
            .OrderByDescending(kv => kv.Value)
            .Take(settings.FeatureWordCount)
            .Select(kv => kv.Key)
            .ToList();

        // 3. Relative frequency per unit. Computed for every unit including the
        //    target's, whether or not the target helped choose the features -
        //    it still has to be placed on the scale to be measured against it.
        var relFreq = new List<Dictionary<string, double>>(chunks.Count);
        foreach (var c in chunks)
            relFreq.Add(featureWords.ToDictionary(w => w, w => (double)c.Counts.GetValueOrDefault(w) / c.TotalTokens));

        var basisIndices = excludeTargetFromNormalisation
            ? Enumerable.Range(0, chunks.Count).Where(i => chunks[i].WorkId != targetWorkId).ToList()
            : Enumerable.Range(0, chunks.Count).ToList();

        // 4. Z-score each feature across the normalisation basis.
        var zScores = Enumerable.Range(0, chunks.Count)
            .Select(_ => new Dictionary<string, double>())
            .ToList();

        foreach (var word in featureWords)
        {
            var values = basisIndices.Select(i => relFreq[i][word]).ToList();
            var mean = values.Average();
            var stdev = Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
            if (stdev < 1e-9) stdev = 1e-9; // a word every unit uses identically

            for (var i = 0; i < chunks.Count; i++)
                zScores[i][word] = (relFreq[i][word] - mean) / stdev;
        }

        // 5. Delta from the target's chosen unit to every unit of every OTHER work.
        //
        // Other chunks of the target's own work are excluded. They would
        // otherwise dominate the top of the ranking - two samples of one text
        // are about as close as two samples get - and would push the first
        // work by another author further down, inflating depth to first
        // outsider by however many chunks the target happens to have. Which is
        // the length effect, back again by another route.
        var targetIndex = chunks.IndexOf(targetChunks[targetChunkIndex]);
        var targetZ = zScores[targetIndex];

        var results = new List<DeltaNeighbor>();
        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].WorkId == targetWorkId) continue;
            var otherZ = zScores[i];
            var delta = featureWords.Average(word => Math.Abs(targetZ[word] - otherZ[word]));
            results.Add(new DeltaNeighbor(chunks[i].WorkId, chunks[i].AuthorName, chunks[i].Label, delta));
        }
        results = results.OrderBy(r => r.Delta).ToList();

        var fingerprint = relFreq[targetIndex]
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        // Token count is the work's full length, not the chunk's - it is what
        // the length-confound analysis needs, and at fixed chunk size the chunk
        // length is a constant and tells you nothing.
        return new DeltaResult(
            results,
            fingerprint,
            targetTotalTokens,
            chunks.Count,
            chunks.Select(c => c.WorkId).Distinct().Count(),
            discardedTokens,
            droppedShort);
    }

    /// <summary>How many comparison units a work yields at a given sample size.</summary>
    public static int ChunkCountFor(int tokenCount, int chunkSize) =>
        chunkSize <= 0 ? 1 : tokenCount / chunkSize;
}
