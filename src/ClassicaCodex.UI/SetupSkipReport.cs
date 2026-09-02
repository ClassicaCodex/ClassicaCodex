using System.Text;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Tells the reader which source files a setup step couldn't parse, and
/// writes the full list somewhere they can go back to.
///
/// A corpus is tens of thousands of files and a handful failing is ordinary -
/// this isn't an error dialog, and it deliberately says so. What it replaces
/// is worse than an error: the wizard used to report "ready" while discarding
/// the list entirely, so those editions were simply absent from the library
/// with nothing anywhere explaining why, and re-running setup reproduced the
/// same silence.
///
/// Only the first few are shown. A bad clone can fail thousands of files, and
/// a message box with thousands of lines in it is no more useful than none -
/// the log is there for the whole list.
/// </summary>
internal static class SetupSkipReport
{
    private const int MaxShown = 12;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "ingest-skipped.log");

    /// <summary>
    /// Tells the reader what a setup step could not read, and stops there.
    ///
    /// A recovered folder does NOT open this box, and that distinction is the
    /// whole design. The two outcomes are not the same news: a skipped file is
    /// a work that is not in the library, which is worth interrupting someone
    /// for; a recovered folder is a work that IS in the library under a name
    /// taken from the text rather than the catalogue, which is worth recording
    /// and not worth a modal.
    ///
    /// Collapsing them was worse than either. The Latin corpus recovers 71
    /// folders and skips 3 files, and reporting both in one box - under the
    /// heading "files skipped", with the skips first and 71 lines of
    /// recoveries after them - made a step that had just worked correctly read
    /// as a failure on a first run. The Patrologia Latina would have made that
    /// several hundred lines.
    ///
    /// So the recoveries go to the log and to the step's own status line, and
    /// are summarised in one sentence here when this box is opening anyway.
    /// </summary>
    public static void ShowIfAny(IWin32Window owner, string stepTitle, IngestOutcome outcome)
    {
        // Always logged, whether or not anything is shown - the log is what
        // makes this recoverable later, and it costs nothing.
        var logged = outcome.HasAnythingToReport && TryWriteLog(stepTitle, outcome);

        if (!outcome.SkipsAreWorthInterrupting) return;

        var installed = outcome.FilesAttempted > 0
            ? $"{stepTitle} installed {outcome.InstalledCount:N0} of {outcome.FilesAttempted:N0} source files."
            : $"{stepTitle} finished.";

        var message = new StringBuilder()
            .AppendLine(installed)
            .AppendLine()
            .AppendLine($"{outcome.SkippedCount:N0} file(s) couldn't be read. These are malformed in the")
            .AppendLine("source repository rather than a problem with your setup - there is nothing to")
            .AppendLine("fix at this end - but the works they contain won't be in your library.")
            .AppendLine();

        foreach (var (filePath, error) in outcome.SkippedFiles.Take(MaxShown))
        {
            message.AppendLine($"  {Path.GetFileName(filePath)} - {error}");
        }

        if (outcome.SkippedCount > MaxShown)
        {
            message.AppendLine($"  ...and {outcome.SkippedCount - MaxShown:N0} more.");
        }

        // One sentence, no list. The reader is here about the skips.
        if (outcome.HasRecoveredFolders)
        {
            message
                .AppendLine()
                .AppendLine($"Separately, and not a problem: {outcome.RecoveredCount:N0} folder(s) had no catalogue")
                .AppendLine("file, so their author and title were read from the texts instead. Those works are")
                .AppendLine("in your library; a title may just not be the standard one.");
        }

        if (logged)
        {
            message.AppendLine().Append("The full list of both is in:").AppendLine().Append(LogPath);
        }

        MessageBox.Show(owner, message.ToString(), $"{stepTitle} - files skipped",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }


    private static bool TryWriteLog(string stepTitle, IngestOutcome outcome)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var entry = new StringBuilder()
                .AppendLine(new string('-', 72))
                .Append(DateTimeOffset.Now.ToString("u")).Append("  ").Append(stepTitle)
                .Append("  - ").Append(outcome.SkippedCount).Append(" file(s) skipped, ")
                .Append(outcome.RecoveredCount).AppendLine(" folder(s) named from their texts");

            foreach (var (filePath, error) in outcome.SkippedFiles)
            {
                entry.Append("SKIPPED\t").Append(filePath).Append('\t').AppendLine(error);
            }

            foreach (var (filePath, note) in outcome.RecoveredFolders)
            {
                entry.Append("NAMED\t").Append(filePath).Append('\t').AppendLine(note);
            }

            entry.AppendLine();
            File.AppendAllText(LogPath, entry.ToString());
            return true;
        }
        catch
        {
            // Reporting the skipped files matters more than logging them; if
            // the log can't be written the dialog above still lists the first
            // few, which is the part that stops this being silent.
            return false;
        }
    }
}
