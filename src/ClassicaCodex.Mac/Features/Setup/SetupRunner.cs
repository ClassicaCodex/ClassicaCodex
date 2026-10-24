using Avalonia.Controls;
using Avalonia.Threading;
using ClassicaCodex.Ingestion;
using ClassicaCodex.UI;
using LibGit2Sharp;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// The Mac's answer to the one WinForms type the shared catalogue names: a
/// step that has to ask something before it runs is handed the window that
/// owns its dialogs, typed as IWin32Window. On the Mac that is an Avalonia
/// window, carried through the shim interface.
/// </summary>
internal sealed class MacWindowHandle : System.Windows.Forms.IWin32Window
{
    public MacWindowHandle(Window window) => Window = window;

    public Window Window { get; }
}

/// <summary>
/// Runs one catalogue step the way both setup windows run it: fetch it as its
/// FetchMode says, then hand the folder to the step's own action on a
/// background thread.
///
/// One method rather than a copy in each window, which is the catalogue's own
/// reason for existing one level up - the guided and the advanced views are
/// two ways of presenting the same work, and must not come to differ about
/// how a corpus is fetched. The services are the ones the Windows forms call,
/// called the same way; LibGit2Sharp clones and plain HTTPS downloads behave
/// the same on macOS.
/// </summary>
internal static class SetupRunner
{
    /// <param name="fetch">
    /// False for a secondary action, which works on files already sitting in
    /// the destination folder and must not re-download anything to do it.
    /// </param>
    /// <param name="progress">Where the download and the ingest report, line by line.</param>
    /// <param name="status">Where the phase and the clone's own messages go - one line, overwritten.</param>
    public static async Task<IngestOutcome> RunAsync(
        SetupDataSource source,
        string destination,
        Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>> action,
        bool fetch,
        IProgress<string> progress,
        Action<string> status,
        string fetchingLabel,
        string installingLabel,
        CancellationToken cancellationToken)
    {
        status(fetch ? fetchingLabel : "Working...");

        if (fetch)
        {
            switch (source.FetchMode)
            {
                case SetupFetchMode.SelfManaged:
                    // Nothing to do here - the action below does its own
                    // fetching, however many files or requests that takes.
                    break;

                case SetupFetchMode.DirectDownload:
                    await new FileDownloadService().DownloadAsync(
                        source.RepoUrl, Path.Combine(destination, source.DownloadFileName!), progress, cancellationToken);
                    break;

                default:
                    // The clone goes through the system temporary folder and is
                    // copied out from there, as on Windows - which is why the
                    // download folder step names the startup disk's free space
                    // when the chosen folder is on another volume.
                    await new GitCorpusFetchService().FetchAsync(
                        source.RepoUrl, destination,
                        new Progress<FetchProgress>(p => status(p.Message)), cancellationToken);
                    break;
            }

            status(installingLabel);
        }

        return await Task.Run(() => action(destination, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Turns a caught exception into something a non-developer can act on - a
    /// plain-language headline and what to try next, with the real message
    /// kept below it, marked as the technical detail rather than hidden.
    ///
    /// Classified by exception type, never by matching message text, which is
    /// not a stable contract. The raw message is exactly right in Advanced
    /// Setup, for the audience that already knows what an HttpRequestException
    /// is; it is the wrong first thing to show in the guided one.
    /// </summary>
    public static string DescribeError(Exception ex, string stepTitle)
    {
        var headline = ex switch
        {
            LibGit2SharpException =>
                $"Couldn't download {stepTitle} - check your internet connection, then try again.",

            // macOS answers a folder it is protecting - Desktop, Documents,
            // Downloads, a removable disk - with "Operation not permitted",
            // which arrives here as this. The fix is a switch in System
            // Settings, and nothing in the raw message says so.
            UnauthorizedAccessException =>
                $"macOS wouldn't let Classica Codex save files for {stepTitle}. Choose another download " +
                "folder (Advanced Setup, or the Download Folder step), or allow access to this one in " +
                "System Settings → Privacy & Security → Files and Folders.",

            IOException =>
                $"Ran into a problem saving {stepTitle} to disk - check you have enough free space, " +
                "then try again.",

            // SQLite's disk-full is a SqliteException, not an IOException, so
            // the arm above never caught it, and the longest step in setup
            // answered a full disk with "trying again sometimes clears it" -
            // which it does not.
            Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 13 } =>
                $"Ran out of disk space while saving {stepTitle}. Free some space on the disk holding " +
                "your library, then run this step again - it will pick up where it left off.",

            // 5 is SQLITE_BUSY and 6 SQLITE_LOCKED. Two copies of the app open
            // at once is an ordinary thing to do while a long import runs, and
            // "database is locked" reads as corruption rather than a queue.
            Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 5 or 6 } =>
                "Another copy of Classica Codex is using the library right now - most likely one that is "
                + "still downloading or importing. Wait for it to finish, or quit the other copy, then "
                + "try this step again.",

            _ =>
                $"Something unexpected went wrong installing {stepTitle}. Trying again sometimes clears " +
                "it; Advanced Setup has more detail if it keeps happening."
        };

        return $"{headline}\n\nTechnical detail (useful if you ask for help): {ex.GetType().Name}: {ex.Message}";
    }
}

/// <summary>
/// The elapsed-time line every long step shows. It ticks on its own whatever
/// the work is doing, which is the point: a clock that keeps counting says
/// the window is alive and the work simply has nothing new to report; a clock
/// that stops says something is genuinely stuck.
/// </summary>
internal sealed class Heartbeat
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _start;

    public Heartbeat(Action<TimeSpan> tick) => _timer.Tick += (_, _) => tick(Elapsed);

    public TimeSpan Elapsed => DateTime.UtcNow - _start;

    public void Start()
    {
        _start = DateTime.UtcNow;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
