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

        message.Append("Details were written to:").AppendLine().Append(LogPath).AppendLine().AppendLine();

        // Where to send it. The path alone is not much use to someone who has
        // never typed %LocalAppData% into an address bar, and the app names
        // its issue tracker nowhere else - About and Help both point only at
        // the releases page, so a reader who hit a real bug had the evidence
        // and nowhere to take it.
        message.Append(
            "If this keeps happening, please report it with that file attached at "
            + "https://github.com/ClassicaCodex/ClassicaCodex/issues");

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

    /// <summary>
    /// Records a failure that was caught and shown to the reader, so that it
    /// leaves a trace behind the dialog.
    ///
    /// The three handlers above only ever see what nothing else caught, and
    /// almost everything a stranger actually meets IS caught - a download
    /// that died, an unpack that ran out of disk, an ingest that hit a locked
    /// database, an AI call that was refused. Each of those ended at a
    /// message box and nothing else, so errors.log stayed empty through
    /// exactly the failures worth diagnosing. Asked for the log after a bug
    /// report, a reader would send a file with nothing in it, and the report
    /// reduced to a paraphrase of a dialog.
    ///
    /// The caller still owns what the reader sees. This only writes the file.
    /// </summary>
    internal static void LogHandled(Exception? ex, string context)
    {
        if (ex == null) return;
        WriteLog(ex, string.IsNullOrWhiteSpace(context) ? "handled" : $"handled - {context}");
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
