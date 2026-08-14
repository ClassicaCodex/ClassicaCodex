namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// One work's reading text, already tokenised.
///
/// The engine takes tokens rather than reading the database itself, and that
/// seam is the point of it. Tokenising a nineteen-play pool takes long enough
/// that a sensitivity series - twenty-six injection levels times fifty
/// iterations is 1,300 runs over the same pool - is unusable if every run
/// re-reads SQLite. With the tokens supplied, the caller tokenises once and
/// reuses; the engine does not need to know whether they came from a
/// repository, a cache, or a synthetic mixture built for a perturbation
/// experiment.
/// </summary>
public sealed record WorkTokens(
    int WorkId,
    string AuthorName,
    string WorkTitle,
    IReadOnlyList<string> Tokens);

/// <summary>
/// The preprocessing choices that move a Delta result.
///
/// Grouped into one record because they travel together and because a
/// parameter grid is a list of these. Accent folding is not here: it applies
/// at tokenisation, before the engine sees anything, and belongs to whoever
/// produced the WorkTokens.
/// </summary>
/// <param name="FeatureWordCount">
/// How many of the most frequent words form the feature set. Burrows' original
/// used 150; 100-1000 is the usual range. Values near 60 are small enough that
/// rank order among close neighbours shifts on its own.
/// </param>
/// <param name="ChunkSize">
/// Tokens per comparison unit, or 0 for whole works. See
/// <see cref="DeltaEngine.SplitIntoChunks"/> for why sampling exists.
/// </param>
public sealed record DeltaSettings(int FeatureWordCount, int ChunkSize)
{
    public static DeltaSettings Default { get; } = new(150, 3000);
}

/// <summary>
/// One comparison unit: either a whole work, or a fixed-size sample drawn
/// from one.
/// </summary>
public sealed class StylometryChunk
{
    public int WorkId;
    public string AuthorName = string.Empty;
    public string WorkTitle = string.Empty;
    public int ChunkIndex;                       // 1-based; 0 when not chunking
    public int ChunkCount;                       // how many this work produced
    public Dictionary<string, int> Counts = new();
    public int TotalTokens;

    /// <summary>Label for the results list. Chunk numbers only appear when there is more than one.</summary>
    public string Label => ChunkCount > 1
        ? $"{AuthorName}, {WorkTitle} [{ChunkIndex}/{ChunkCount}]"
        : $"{AuthorName}, {WorkTitle}";
}

/// <summary>One neighbour of the target, at whatever unit the run used.</summary>
public sealed record DeltaNeighbor(int WorkId, string AuthorName, string Label, double Delta);

/// <summary>
/// Everything one Delta run produced.
///
/// The sampling counts are returned as numbers rather than as the sentence the
/// form displays, because the experiments need to compare them and a caller
/// that wants the sentence can build it.
/// </summary>
public sealed record DeltaResult(
    IReadOnlyList<DeltaNeighbor> Neighbors,
    IReadOnlyList<(string Word, double Frequency)> Fingerprint,
    int TargetTokenCount,
    int SampleCount,
    int WorkCount,
    int DiscardedTokens,
    IReadOnlyList<string> WorksTooShort);
