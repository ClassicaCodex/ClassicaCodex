namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// One combination of preprocessing settings to validate at.
///
/// Accent folding is here rather than in <see cref="DeltaSettings"/> because it
/// applies during tokenisation, before the engine sees anything - which is also
/// why a grid cannot be handed a single pre-tokenised pool and has to be handed
/// something that can produce one per folding setting.
/// </summary>
public sealed record GridPoint(
    int ChunkSize,
    int FeatureWordCount,
    bool FoldAccents,
    bool ExcludeHeldOutFromNormalisation)
{
    public string Describe() =>
        $"{ChunkSize:N0} tokens, {FeatureWordCount} MFW, " +
        (FoldAccents ? "folded" : "unfolded") +
        (ExcludeHeldOutFromNormalisation ? ", held-out excluded" : "");
}

/// <summary>
/// What one configuration produced.
/// </summary>
/// <param name="WorksValidated">
/// How many works the cell could actually test.
///
/// THIS IS NOT BOOKKEEPING. It varies across the grid and it varies in a
/// direction that will mislead anyone who searches for a low length
/// correlation without watching it. A work shorter than the sample size yields
/// no samples and is skipped, so raising the sample size quietly drops the
/// shortest works - and dropping the shortest works compresses the length
/// spread, and a compressed spread lowers rho whatever the method is doing.
///
/// Euripides at 2,500 tokens tests all nineteen plays, spanning 4,141 to
/// 10,060 tokens. At 5,000 Cyclops is gone and the span starts at 6,248. A
/// grid that reported only rho would show the confound melting away as the
/// sample size rose, and the melting would be an artefact of the corpus
/// getting more uniform rather than of the measure getting cleaner.
/// </param>
public sealed record GridCell(
    GridPoint Point,
    int Recovered,
    int WorksValidated,
    double RecoveryRate,
    double MeanMargin,
    double MarginLengthCorrelation,
    double MarginSampleCountCorrelation,
    double LengthSpread,
    double PoolSeparation,
    int SampleCount,
    IReadOnlyList<string> Skipped,
    string? Error = null)
{
    public bool Failed => Error != null;

    /// <summary>
    /// Whether this cell tested fewer works than the best cell in its grid.
    ///
    /// Set by the runner rather than computed here, because it is a statement
    /// about the cell's neighbours rather than about the cell.
    /// </summary>
    public bool DroppedWorks { get; init; }

    /// <summary>
    /// Whether the cell is worth believing: it recovered nearly everything, the
    /// length correlation is weak, and there was enough spread in work length
    /// for a correlation to have shown up had there been one.
    ///
    /// The last clause is the one that is easy to leave out and fatal to leave
    /// out. A cell where every work is the same length reports a low rho by
    /// construction, and a grid sorted on rho alone will put those cells at the
    /// top and call them the best settings.
    ///
    /// This is a filter, not a score. It deliberately does not combine the
    /// numbers into a single figure of merit - a rank order over settings
    /// invites picking the top row, and picking settings because they scored
    /// well is how the last set of promising results happened.
    /// </summary>
    public bool Trustworthy =>
        !Failed
        && !DroppedWorks
        && RecoveryRate >= 0.9
        && Math.Abs(MarginLengthCorrelation) < 0.3
        && LengthSpread >= 1.8;
}

/// <summary>
/// Validates one author repeatedly across a grid of preprocessing settings, so
/// that settings can be chosen for behaving well on known texts rather than for
/// producing an interesting answer about a disputed one.
///
/// THE POINT OF THE ORDERING. Run validation first, then choose parameters,
/// then look at the disputed work. Done the other way round - try settings
/// until Rhesus looks Aeschylean - a grid is just a machine for generating the
/// result you went looking for, and a large enough grid will always contain
/// one.
///
/// WHAT TO SEARCH FOR. Not the highest recovery rate. Recovery is close to
/// saturated on a tragic pool: nineteen of nineteen at every setting tried so
/// far, which discriminates nothing. The informative axis is the length
/// correlation - the cells worth using are the ones that recover everything
/// AND do not sort the works by size while doing it. See
/// <see cref="GridCell.Trustworthy"/>, and note the spread condition in it.
/// </summary>
public static class ParameterGridRunner
{
    /// <summary>
    /// Sample sizes worth trying by default.
    ///
    /// 2,500 is the lower bound Eder found for stable attribution and the point
    /// below which a sample says more about its length than its author. 5,000
    /// is where several Euripides plays stop yielding two samples and the
    /// shortest stop yielding any, which is a real cost rather than a
    /// conservative choice - hence WorksValidated being reported per cell.
    /// </summary>
    public static readonly int[] DefaultChunkSizes = { 2000, 2500, 3000, 3500, 5000 };

    /// <summary>
    /// Burrows used 150. Below about 60 the rank order among close neighbours
    /// shifts on its own; above a few hundred the tail fills with content words
    /// and the measure drifts towards topic.
    /// </summary>
    public static readonly int[] DefaultFeatureCounts = { 100, 150, 300, 500 };

    /// <summary>
    /// Every combination, in a stable order.
    ///
    /// Accent folding varies by default because it is the setting most likely
    /// to be an editorial artefact rather than an authorial one: Perseus is not
    /// consistent about accentuation across its editions, and a result that
    /// flips when folding flips was measuring orthography.
    /// </summary>
    public static List<GridPoint> Build(
        IEnumerable<int>? chunkSizes = null,
        IEnumerable<int>? featureCounts = null,
        IEnumerable<bool>? accentFolding = null,
        bool excludeHeldOutFromNormalisation = false)
    {
        var sizes = (chunkSizes ?? DefaultChunkSizes).Distinct().OrderBy(x => x).ToList();
        var features = (featureCounts ?? DefaultFeatureCounts).Distinct().OrderBy(x => x).ToList();
        var folds = (accentFolding ?? new[] { true, false }).Distinct().ToList();

        var points = new List<GridPoint>(sizes.Count * features.Count * folds.Count);

        foreach (var fold in folds)
            foreach (var size in sizes)
                foreach (var featureCount in features)
                    points.Add(new GridPoint(size, featureCount, fold, excludeHeldOutFromNormalisation));

        return points;
    }

    /// <summary>
    /// Runs every point and reports one row each.
    /// </summary>
    /// <param name="poolFor">
    /// Produces the tokenised pool for a given accent-folding setting. A
    /// delegate rather than a list because folding happens at tokenisation, so
    /// the two settings are two different token streams over the same rows -
    /// and because whoever supplies it can cache, which matters: a five-by-four
    /// grid over both folding settings is forty validations of the same pool,
    /// and tokenising is the expensive half.
    /// </param>
    /// <param name="progress">Called with (completed, total, point) before each cell.</param>
    public static List<GridCell> Run(
        Func<bool, IReadOnlyList<WorkTokens>> poolFor,
        string targetAuthor,
        IReadOnlyList<GridPoint> points,
        Action<int, int, GridPoint>? progress = null,
        CancellationToken cancellation = default)
    {
        var cells = new List<GridCell>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();

            var point = points[i];
            progress?.Invoke(i, points.Count, point);

            var pool = poolFor(point.FoldAccents);
            var settings = new DeltaSettings(point.FeatureWordCount, point.ChunkSize);

            try
            {
                var result = LeaveOneOutValidator.Validate(
                    pool, targetAuthor, settings,
                    point.ExcludeHeldOutFromNormalisation,
                    progress: null,
                    cancellation: cancellation);

                cells.Add(new GridCell(
                    point,
                    result.RecoveredCount,
                    result.Works.Count,
                    result.RecoveryRate,
                    result.MeanMargin,
                    result.MarginLengthCorrelation,
                    result.MarginSampleCountCorrelation,
                    result.LengthSpread,
                    result.Difficulty.Separation,
                    result.Difficulty.SampleCount,
                    result.Skipped));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A cell that cannot run is reported as a cell that cannot run.
                // Dropping it would leave a gap in the grid that reads as an
                // untried combination rather than an impossible one - and the
                // commonest cause, a sample size larger than every work the
                // author has, is worth seeing.
                cells.Add(new GridCell(
                    point, 0, 0, 0, 0, 0, 0, 1, 0, 0,
                    Array.Empty<string>(), ex.Message));
            }
        }

        // DroppedWorks is relative to the rest of the grid, so it can only be
        // decided once every cell has run.
        var mostTested = cells.Where(c => !c.Failed).Select(c => c.WorksValidated).DefaultIfEmpty(0).Max();

        return cells
            .Select(c => c with { DroppedWorks = !c.Failed && c.WorksValidated < mostTested })
            .ToList();
    }

    /// <summary>
    /// A one-line reading of a finished grid, for the summary above the table.
    ///
    /// Deliberately not a recommendation of one row, and after the first real
    /// run, deliberately not a recommendation at all when the cells cannot be
    /// told apart.
    ///
    /// Euripides against Aeschylus and Sophocles returned forty configurations
    /// spanning rho +0.42 to +0.73, every one recovering 19/19. Naming the
    /// +0.42 cell reads as a finding and is not one: over nineteen works that
    /// value carries a 95% interval of about [-0.04, +0.73] and the +0.73 cell
    /// carries [+0.41, +0.89]. Best against worst is 1.36 standard errors. The
    /// entire visible spread of the grid fits inside the estimation error of
    /// any single cell in it.
    ///
    /// So when the intervals overlap, this says the settings are
    /// indistinguishable rather than naming a winner. A sorted column is very
    /// good at making noise look like a gradient.
    /// </summary>
    public static string Summarise(IReadOnlyList<GridCell> cells)
    {
        var ran = cells.Where(c => !c.Failed).ToList();
        if (ran.Count == 0) return "No configuration completed.";

        var good = ran.Where(c => c.Trustworthy).ToList();

        if (good.Count == 0)
        {
            var best = ran.OrderBy(c => Math.Abs(c.MarginLengthCorrelation)).First();
            var worst = ran.OrderByDescending(c => Math.Abs(c.MarginLengthCorrelation)).First();

            var bestBand = ValidationResult.FisherInterval(
                best.MarginLengthCorrelation, best.WorksValidated);
            var worstBand = ValidationResult.FisherInterval(
                worst.MarginLengthCorrelation, worst.WorksValidated);

            var indistinguishable = bestBand.High >= worstBand.Low;

            return
                $"No configuration met all four conditions: the length correlation ran from " +
                $"{StatFormat.Signed(best.MarginLengthCorrelation)} to " +
                $"{StatFormat.Signed(worst.MarginLengthCorrelation)} " +
                $"across {ran.Count} settings, and every one recovered " +
                $"{best.RecoveryRate:P0}. " +
                (indistinguishable
                    ? $"Those two are not distinguishable at this corpus size - over " +
                      $"{best.WorksValidated} works the weakest carries a 95% band of " +
                      $"[{StatFormat.Signed(bestBand.Low)}, {StatFormat.Signed(bestBand.High)}], which overlaps the " +
                      "strongest. Read this as one correlation present at every setting, not as a " +
                      "gradient with a best end. Do not pick the top row."
                    : $"The weakest was {best.Point.Describe()}, and its band does not reach the " +
                      "strongest - which is worth a second run before believing.") +
                " Treat margin differences between works of different length as uninterpretable here.";
        }

        var sizes = good.Select(c => c.Point.ChunkSize).Distinct().OrderBy(x => x).ToList();
        var features = good.Select(c => c.Point.FeatureWordCount).Distinct().OrderBy(x => x).ToList();

        return
            $"{good.Count} of {ran.Count} configurations recovered at least 90% with a weak length " +
            $"correlation at genuine spread. Sample sizes {string.Join(", ", sizes)}; " +
            $"feature counts {string.Join(", ", features)}. " +
            (good.Count < 3
                ? "That is few enough to be chance - prefer a region that behaves over a single cell."
                : "Prefer a setting from the middle of that region rather than its best-scoring cell.");
    }
}
