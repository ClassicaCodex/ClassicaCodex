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

    private static async Task ReportAsync(Exception ex)
    {
        // One box at a time: a failure that repeats on every redraw should
        // not bury the screen in them.
        if (_reporting) return;
        _reporting = true;

        try
        {
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;

            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
                .Windows.FirstOrDefault(window => window.IsActive);

            await Alert.Warn(owner, "Classica Codex",
                "Something went wrong with that action.\n\n"
                + inner.Message + "\n\n"
                + "The rest of the app is still running - your library, tags and bookmarks are untouched.\n\n"
                + "Details were written to:\n" + LogFile + "\n\n"
                + "If this keeps happening, please report it with that file attached at " + ProjectLinks.Issues);
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
    /// The classic Windows app's last word when a background thread fails and
    /// the runtime is about to end the process. Shown by Windows itself, on
    /// the failing thread, because by now the UI thread may be the thing that
    /// is stuck, and an Avalonia window waits on it.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void SayClosingOnWindows(Exception ex)
    {
        try
        {
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;

            var message = "Classica Codex hit an error it can't recover from and has to close.\n\n"
                + inner.Message + "\n\n"
                + "Details were written to:\n" + LogFile + "\n\n"
                + "If this keeps happening, please report it with that file attached at " + ProjectLinks.Issues;

            MessageBoxW(IntPtr.Zero, message, "Classica Codex - closing", MbOk | MbIconError | MbSetForeground | MbTopMost);
        }
        catch (Exception)
        {
            // The log is already written; there is nothing sensible left to try.
        }
    }

    private const uint MbOk = 0x0, MbIconError = 0x10, MbSetForeground = 0x10000, MbTopMost = 0x40000;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

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
