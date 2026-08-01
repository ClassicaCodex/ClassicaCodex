namespace ClassicaCodex.Ingestion;

/// <summary>
/// What a setup step actually did, beyond finishing - specifically, which
/// source files it couldn't parse and skipped.
///
/// This exists because the ingest services already collected that
/// information and nothing ever read it. PerseusIngestService deliberately
/// catches per file, so one malformed edition doesn't abandon a multi-hour
/// run, and records it in FailedFiles. IngestForm surfaces that. The Setup
/// Wizard and Guided Setup - the path the README actually tells people to
/// use - constructed the service inside a lambda, awaited it, and let the
/// service go out of scope with its failure list unread. Every skipped file
/// vanished, and the step reported "Done - ready."
///
/// A corpus is tens of thousands of files; a handful failing is normal and
/// not alarming. Silently losing the list of which ones is the problem,
/// because those editions are then missing from the library with nothing
/// anywhere to say so - and re-running ingest reproduces the same silence.
/// </summary>
public sealed record IngestOutcome(IReadOnlyList<(string FilePath, string Error)> SkippedFiles)
{
    /// <summary>For steps that either succeed wholesale or throw - most of them.</summary>
    public static IngestOutcome Clean { get; } = new(Array.Empty<(string, string)>());

    public static IngestOutcome From(IReadOnlyList<(string FilePath, string Error)> skipped) =>
        skipped.Count == 0 ? Clean : new IngestOutcome(skipped.ToList());

    /// <summary>Combines the results of several passes within one setup step.</summary>
    public static IngestOutcome Combine(params IngestOutcome[] outcomes)
    {
        var all = outcomes.SelectMany(o => o.SkippedFiles).ToList();
        return all.Count == 0 ? Clean : new IngestOutcome(all);
    }

    public int SkippedCount => SkippedFiles.Count;

    public bool HasSkippedFiles => SkippedFiles.Count > 0;
}
