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
/// <param name="RecoveredFolders">
/// Folders that were ingested despite a missing or unreadable CTS catalogue,
/// with their author and work names read out of the TEI headers instead.
///
/// Separate from SkippedFiles because it is a different piece of news: nothing
/// was lost, but a title here came from the file rather than the catalogue and
/// may not be the canonical one. Reporting it as a skip would be alarming and
/// wrong; not reporting it at all would repeat the mistake this whole type
/// exists to correct, one step down.
/// </param>
public sealed record IngestOutcome(
    IReadOnlyList<(string FilePath, string Error)> SkippedFiles,
    IReadOnlyList<(string FilePath, string Error)> RecoveredFolders)
{
    /// <summary>For steps that either succeed wholesale or throw - most of them.</summary>
    public static IngestOutcome Clean { get; } =
        new(Array.Empty<(string, string)>(), Array.Empty<(string, string)>());

    public static IngestOutcome From(
        IReadOnlyList<(string FilePath, string Error)> skipped,
        IReadOnlyList<(string FilePath, string Error)>? recovered = null)
    {
        var recoveredList = recovered ?? Array.Empty<(string, string)>();
        return skipped.Count == 0 && recoveredList.Count == 0
            ? Clean
            : new IngestOutcome(skipped.ToList(), recoveredList.ToList());
    }

    /// <summary>Combines the results of several passes within one setup step.</summary>
    public static IngestOutcome Combine(params IngestOutcome[] outcomes)
    {
        var skipped = outcomes.SelectMany(o => o.SkippedFiles).ToList();
        var recovered = outcomes.SelectMany(o => o.RecoveredFolders).ToList();

        return skipped.Count == 0 && recovered.Count == 0
            ? Clean
            : new IngestOutcome(skipped, recovered);
    }

    public int SkippedCount => SkippedFiles.Count;

    public bool HasSkippedFiles => SkippedFiles.Count > 0;

    public int RecoveredCount => RecoveredFolders.Count;

    public bool HasRecoveredFolders => RecoveredFolders.Count > 0;

    /// <summary>Whether there is anything at all worth telling the reader.</summary>
    public bool HasAnythingToReport => HasSkippedFiles || HasRecoveredFolders;

    /// <summary>
    /// The step's one-line summary, naming both outcomes.
    ///
    /// This is where a recovery gets said out loud, because it does not open a
    /// dialog - see SetupSkipReport. The two are different news: a skipped
    /// file is a work that is NOT in the library and is worth interrupting
    /// someone for; a recovered folder is a work that IS, under a name read
    /// from the text rather than the catalogue, and is worth a line and a log
    /// entry.
    ///
    /// Lives on the outcome rather than on the reporter because it is a
    /// question about the outcome, and because the reporter is a WinForms type
    /// that no test can reach.
    /// </summary>
    public string Describe(string stepTitle)
    {
        if (!HasAnythingToReport) return $"{stepTitle} is ready.";

        var parts = new List<string>(2);
        if (HasSkippedFiles) parts.Add($"{SkippedCount:N0} file(s) skipped");
        if (HasRecoveredFolders) parts.Add($"{RecoveredCount:N0} named from their texts");

        return $"{stepTitle} is ready - {string.Join(", ", parts)}.";
    }
}
