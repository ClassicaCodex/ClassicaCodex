using System.Text;

namespace ClassicaCodex.UI;

/// <summary>
/// Catches the exceptions nothing else did, and turns them into a message
/// instead of a vanished window.
///
/// Why this is needed: roughly two thirds of this app's event handlers are
/// async lambdas of the form <c>Click += async (_, _) =&gt; await DoThingAsync()</c>.
/// That's an async void continuation, and an exception escaping one doesn't
/// return to the caller - it gets posted to the message loop and, with no
/// handler installed, ends the process. No dialog, no log, nothing on screen:
/// the app is simply gone, mid-session, and whatever the reader was part-way
/// through goes with it.
///
/// Wrapping all forty-odd of those handlers individually would be the wrong
/// shape of fix - it's the same three lines repeated, and the next handler
/// anyone adds would still be unprotected by default. One place that catches
/// everything is both smaller and harder to forget.
///
/// The dialog deliberately doesn't pretend to know what went wrong. It says
/// what failed, where the details are, and that the app is still running -
/// which for anything short of a corrupted database is true, since a failed
/// tag lookup or export leaves the rest of the session perfectly usable.
/// </summary>
internal static class CrashReporter
{
    private static readonly object LogLock = new();

    /// <summary>
    /// Next to the database and the settings files, so everything the app
    /// writes for one user is in one place.
    /// </summary>
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "errors.log");

    public static void Install()
    {
        // Has to come before Application.Run. Without CatchException, WinForms
        // on .NET lets the exception escape the message loop and terminate the
        // process before ThreadException is ever raised.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) => Report(e.Exception, fatal: false);

        // A background thread (an ingest task, say) faulting outside the
        // message loop. The runtime is going to end the process either way -
        // the most that can be done is say so and leave a log behind.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception, fatal: true);

        // A faulted Task nobody awaited. Not fatal on modern .NET, but it
        // means something failed silently, which is worth a log line even
        // though it isn't worth interrupting the reader for.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteLog(e.Exception, "unobserved task");
            e.SetObserved();
        };
    }

    private static void Report(Exception? ex, bool fatal)
    {
        if (ex == null) return;

        WriteLog(ex, fatal ? "fatal" : "unhandled");

        var message = new StringBuilder()
            .AppendLine(fatal
                ? "Classica Codex hit an error it can't recover from and has to close."
                : "Something went wrong with that action.")
            .AppendLine()
            .AppendLine(Describe(ex))
            .AppendLine();

        if (!fatal)
        {
            message.AppendLine(
                "The rest of the app is still running - your library, tags and bookmarks are untouched.");
            message.AppendLine();
        }

        message.Append("Details were written to:").AppendLine().Append(LogPath);

        try
        {
            MessageBox.Show(
                message.ToString(),
                fatal ? "Classica Codex - closing" : "Classica Codex",
                MessageBoxButtons.OK,
                fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
        }
        catch
        {
            // If even showing a message box fails there's nothing sensible
            // left to try; the log is already written.
        }
    }

    /// <summary>
    /// The innermost message, which is nearly always the useful one - an
    /// outer "One or more errors occurred" wrapper tells the reader nothing
    /// that "no such column: TextNodeId" doesn't tell them better.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    private static void WriteLog(Exception ex, string kind)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var entry = new StringBuilder()
                .AppendLine(new string('-', 72))
                .Append(DateTimeOffset.Now.ToString("u")).Append("  ").AppendLine(kind)
                .AppendLine(ex.ToString())
                .AppendLine();

            lock (LogLock)
            {
                File.AppendAllText(LogPath, entry.ToString());
            }
        }
        catch
        {
            // A log that can't be written is not worth a second failure on
            // top of the one being reported.
        }
    }
}
