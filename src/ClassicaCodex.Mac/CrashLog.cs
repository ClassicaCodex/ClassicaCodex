using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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
    public static string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "errors.log");

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

    public static void Write(Exception ex, string context)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFile);
            if (directory != null) Directory.CreateDirectory(directory);

            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] (Mac) while {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A log that cannot be written is not worth a second failure.
        }
    }
}
