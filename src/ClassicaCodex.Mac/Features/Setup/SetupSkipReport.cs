using System.Text;
using Avalonia.Controls;
using ClassicaCodex.Data;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Tells the reader which source files a setup step couldn't parse, and
/// writes the full list somewhere they can go back to - the Windows
/// SetupSkipReport, writing the same log under the same name.
///
/// A corpus is tens of thousands of files and a handful failing is ordinary,
/// so this is not an error and says so. What it replaces is worse than an
/// error: setup used to report "ready" while discarding the list, so those
/// editions were simply absent from the library with nothing anywhere saying
/// why, and running setup again reproduced the same silence.
///
/// Only the first few are shown. A bad clone can fail thousands of files, and
/// an alert with thousands of lines in it is no more use than none - the log
/// is there for the whole list.
/// </summary>
internal static class SetupSkipReport
{
    private const int MaxShown = 12;

    public static string LogPath => Path.Combine(PreferencesFolder.Root, "ingest-skipped.log");

    /// <summary>
    /// Says what a step could not read, and stops there.
    ///
    /// A recovered folder does NOT open this alert, and that distinction is
    /// the whole design. A skipped file is a work that is not in the library,
    /// worth interrupting someone for; a recovered folder is a work that IS,
    /// under a name taken from the text rather than the catalogue, worth
    /// recording and not worth a modal. Reporting both in one box made the
    /// Latin corpus - 71 recoveries, 3 skips - read as a failure on a first
    /// run. So the recoveries go to the log and the step's status line, and
    /// get one sentence here when the alert is opening anyway.
    /// </summary>
    public static async Task ShowIfAnyAsync(Window owner, string stepTitle, IngestOutcome outcome)
    {
        // Always logged, whether or not anything is shown - the log is what
        // makes this recoverable later, and it costs nothing.
        var logged = outcome.HasAnythingToReport && TryWriteLog(stepTitle, outcome);

        if (!outcome.SkipsAreWorthInterrupting) return;

        var message = new StringBuilder()
            .Append(outcome.FilesAttempted > 0
                ? $"{stepTitle} installed {outcome.InstalledCount:N0} of {outcome.FilesAttempted:N0} source files."
                : $"{stepTitle} finished.")
            .Append("\n\n")
            .Append($"{outcome.SkippedCount:N0} file(s) couldn't be read. These are malformed in the source ")
            .Append("repository rather than a problem with your setup - there is nothing to fix at this end - ")
            .Append("but the works they contain won't be in your library.\n");

        foreach (var (filePath, error) in outcome.SkippedFiles.Take(MaxShown))
        {
            message.Append($"\n  {Path.GetFileName(filePath)} - {error}");
        }

        if (outcome.SkippedCount > MaxShown)
        {
            message.Append($"\n  ...and {outcome.SkippedCount - MaxShown:N0} more.");
        }

        // One sentence, no list. The reader is here about the skips.
        if (outcome.HasRecoveredFolders)
        {
            message.Append($"\n\nSeparately, and not a problem: {outcome.RecoveredCount:N0} folder(s) had no ")
                .Append("catalogue file, so their author and title were read from the texts instead. Those ")
                .Append("works are in your library; a title may just not be the standard one.");
        }

        if (logged)
        {
            message.Append("\n\nThe full list of both is in:\n").Append(LogPath);
        }

        await Alert.Info(owner, $"{stepTitle} - files skipped", message.ToString());
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reporting the skipped files matters more than logging them; if
            // the log can't be written the alert still lists the first few,
            // which is the part that stops this being silent.
            return false;
        }
    }
}
