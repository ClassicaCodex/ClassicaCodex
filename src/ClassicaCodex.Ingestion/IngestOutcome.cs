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
/// <param name="FilesAttempted">
/// How many source files the step opened, so a skip can be reported against
/// the whole rather than on its own.
///
/// "3 files couldn't be read" and "684 of 687 files installed" describe the
/// same run and land completely differently, and the second is the true one:
/// the step worked. Zero means the step does not count files - most do not -
/// and the wording falls back to the bare number.
/// </param>
public sealed record IngestOutcome(
    IReadOnlyList<(string FilePath, string Error)> SkippedFiles,
    IReadOnlyList<(string FilePath, string Error)> RecoveredFolders,
    int FilesAttempted = 0)
{
    /// <summary>For steps that either succeed wholesale or throw - most of them.</summary>
    public static IngestOutcome Clean { get; } =
        new(Array.Empty<(string, string)>(), Array.Empty<(string, string)>());

    public static IngestOutcome From(
        IReadOnlyList<(string FilePath, string Error)> skipped,
        IReadOnlyList<(string FilePath, string Error)>? recovered = null,
        int attempted = 0)
    {
        var recoveredList = recovered ?? Array.Empty<(string, string)>();
        return skipped.Count == 0 && recoveredList.Count == 0 && attempted == 0
            ? Clean
            : new IngestOutcome(skipped.ToList(), recoveredList.ToList(), attempted);
    }

    /// <summary>Combines the results of several passes within one setup step.</summary>
    public static IngestOutcome Combine(params IngestOutcome[] outcomes)
    {
        var skipped = outcomes.SelectMany(o => o.SkippedFiles).ToList();
        var recovered = outcomes.SelectMany(o => o.RecoveredFolders).ToList();
        var attempted = outcomes.Sum(o => o.FilesAttempted);

        return skipped.Count == 0 && recovered.Count == 0 && attempted == 0
            ? Clean
            : new IngestOutcome(skipped, recovered, attempted);
    }

    /// <summary>Files that went in - what the step is actually for.</summary>
    public int InstalledCount => Math.Max(0, FilesAttempted - SkippedCount);

    /// <summary>
    /// Whether the skips are worth stopping the reader to say.
    ///
    /// They are not, usually. The three files the Latin corpus refuses are
    /// malformed in the Perseus repository itself - the same three every run,
    /// on every machine, until somebody upstream fixes the XML - so a modal
    /// about them is a modal about somebody else's typo, shown forever. Three
    /// files in 687 is a fact about the data, not a problem with the setup,
    /// and reporting it as though the reader could act on it is what made a
    /// step that worked read as a failure.
    ///
    /// A clone that went wrong looks nothing like that. It fails in bulk -
    /// hundreds of files, or a large share of a small corpus - and that IS
    /// worth interrupting for, because re-running fixes it.
    ///
    /// So: one in twenty, or twenty-five files, whichever comes first. Below
    /// that the status line and the log carry it, which is not silence - the
    /// thing this whole type exists to prevent - just proportion. A step that
    /// does not count its files keeps the old behaviour and always shows.
    /// </summary>
    public bool SkipsAreWorthInterrupting =>
        HasSkippedFiles
        && (FilesAttempted == 0 || SkippedCount >= 25 || SkippedCount * 20 >= FilesAttempted);

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
        var parts = new List<string>(2);

        // Led with the count that went in, not the count that did not. Both
        // numbers are true and only one of them is what the reader wanted to
        // know.
        if (FilesAttempted > 0)
        {
            parts.Add(HasSkippedFiles
                ? $"{InstalledCount:N0} of {FilesAttempted:N0} files installed"
                : $"{InstalledCount:N0} files installed");
        }
        else if (HasSkippedFiles)
        {
            parts.Add($"{SkippedCount:N0} file(s) skipped");
        }

        if (HasRecoveredFolders) parts.Add($"{RecoveredCount:N0} named from their texts");

        return parts.Count == 0
            ? $"{stepTitle} is ready."
            : $"{stepTitle} is ready - {string.Join(", ", parts)}.";
    }
}
