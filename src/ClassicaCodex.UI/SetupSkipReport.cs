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

    public static void ShowIfAny(IWin32Window owner, string stepTitle, IngestOutcome outcome)
    {
        if (!outcome.HasAnythingToReport) return;

        var logged = TryWriteLog(stepTitle, outcome);
        var message = new StringBuilder();

        if (outcome.HasSkippedFiles)
        {
            message
                .AppendLine($"{stepTitle} finished, but {outcome.SkippedCount:N0} file(s) couldn't be read and were skipped.")
                .AppendLine()
                .AppendLine("Everything else was ingested normally. These are usually malformed or unusual")
                .AppendLine("source files rather than a problem with your setup - but the works they contain")
                .AppendLine("won't be in your library.")
                .AppendLine();

            foreach (var (filePath, error) in outcome.SkippedFiles.Take(MaxShown))
            {
                message.AppendLine($"  {Path.GetFileName(filePath)} - {error}");
            }

            if (outcome.SkippedCount > MaxShown)
            {
                message.AppendLine($"  ...and {outcome.SkippedCount - MaxShown:N0} more.");
            }
        }

        // Deliberately worded as news rather than as a warning. These folders
        // ARE in the library; what is uncertain is only whether their author
        // and title read the way the catalogue would have named them.
        if (outcome.HasRecoveredFolders)
        {
            if (message.Length > 0) message.AppendLine();

            message
                .AppendLine($"{outcome.RecoveredCount:N0} folder(s) had no catalogue file, so the author and title")
                .AppendLine("were read from the texts themselves. Those works ARE in your library - the only")
                .AppendLine("thing to watch is that a title may not be the standard one.")
                .AppendLine();

            foreach (var (filePath, note) in outcome.RecoveredFolders.Take(MaxShown))
            {
                message.AppendLine($"  {Path.GetFileName(filePath.TrimEnd(Path.DirectorySeparatorChar))} - {note}");
            }

            if (outcome.RecoveredCount > MaxShown)
            {
                message.AppendLine($"  ...and {outcome.RecoveredCount - MaxShown:N0} more.");
            }
        }

        if (logged)
        {
            message.AppendLine().Append("The full list is in:").AppendLine().Append(LogPath);
        }

        var caption = outcome.HasSkippedFiles
            ? $"{stepTitle} - files skipped"
            : $"{stepTitle} - names read from the texts";

        MessageBox.Show(owner, message.ToString(), caption,
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
