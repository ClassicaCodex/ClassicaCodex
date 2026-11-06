using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac;

/// <summary>
/// Writes what went wrong to errors.log beside the other preferences - the
/// same file name and place the Windows CrashReporter uses, so a report from
/// either machine is found in the same folder.
/// </summary>
public static class CrashLog
{
    public static string LogFile => Path.Combine(PreferencesFolder.Root, "errors.log");

    /// <summary>The details and where to send them, which end every report.</summary>
    private static string Details =>
        "Details were written to:\n" + LogFile + "\n\n"
        + "If this keeps happening, please report it with that file attached at " + ProjectLinks.Issues;

    private static readonly object FatalLock = new();

    private static bool _reporting;

    /// <summary>
    /// Does what the Windows CrashReporter does with an error in an action:
    /// logs it, says so, and leaves the app running. Without this an
    /// exception from a click handler would close the app. Disposing the
    /// result takes the handler off again, which only the tests need.
    /// </summary>
    public static IDisposable Install()
    {
        void Handle(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            Write(e.Exception, "running (unhandled)");
            _ = ReportAsync(e.Exception);
        }

        Dispatcher.UIThread.UnhandledException += Handle;
        return new Removal(() => Dispatcher.UIThread.UnhandledException -= Handle);
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }

    /// <summary>
    /// Does what the Windows CrashReporter does with an error nothing caught
    /// on a background thread, an ingest task, say. The runtime ends the
    /// process when this returns, so the most it can do is leave a log and say
    /// so, and the saying is the UI thread's. This thread waits for the reader
    /// to answer, and that wait is all that keeps the app there.
    ///
    /// It has no one to wait for if this is the UI thread, or if that thread
    /// is stuck, perhaps on the very work that failed: the log is all those
    /// leave. Nearly everything on the UI thread reaches Install's handler.
    /// </summary>
    public static void ReportFatal(Exception ex, int waitForUiThreadMilliseconds = 10_000)
    {
        Write(ex, "running (fatal)");

        if (Dispatcher.UIThread.CheckAccess()) return;

        // A second thread failing beside this one waits here too. If it went
        // on and returned, the process would end under the first one's dialog.
        lock (FatalLock)
        {
            var showing = new ManualResetEventSlim();
            var answered = new ManualResetEventSlim();

            Dispatcher.UIThread.Post(async () =>
            {
                showing.Set();

                try
                {
                    await Alert.Error(ActiveWindow, "Classica Codex - closing",
                        "Classica Codex hit an error it can't recover from and has to close.\n\n"
                        + Innermost(ex).Message + "\n\n"
                        + Details);
                }
                catch (Exception)
                {
                    // The log is already written; there is nothing sensible left to try.
                }
                finally
                {
                    answered.Set();
                }
            });

            if (showing.Wait(waitForUiThreadMilliseconds)) answered.Wait();
        }
    }

    private static Window? ActiveWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
            .Windows.FirstOrDefault(window => window.IsActive);

    /// <summary>The innermost message, which is nearly always the useful one.</summary>
    private static Exception Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    private static async Task ReportAsync(Exception ex)
    {
        // One box at a time: a failure that repeats on every redraw should
        // not bury the screen in them.
        if (_reporting) return;
        _reporting = true;

        try
        {
            await Alert.Warn(ActiveWindow, "Classica Codex",
                "Something went wrong with that action.\n\n"
                + Innermost(ex).Message + "\n\n"
                + "The rest of the app is still running - your library, tags and bookmarks are untouched.\n\n"
                + Details);
        }
        catch (Exception)
        {
            // The log is already written; there is nothing sensible left to try.
        }
        finally
        {
            _reporting = false;
        }
    }

    /// <summary>
    /// Said in full on Windows, where the log is the one the Windows app
    /// writes to, so that a report shows which of the two it came from.
    /// </summary>
    private static string SystemName =>
        OperatingSystem.IsWindows() ? "Avalonia on Windows" : OperatingSystem.IsLinux() ? "Linux" : "Mac";

    public static void Write(Exception ex, string context)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFile);
            if (directory != null) Directory.CreateDirectory(directory);

            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({SystemName}) while {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A log that cannot be written is not worth a second failure.
        }
    }
}
