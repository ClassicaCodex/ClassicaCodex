using Avalonia;
using Avalonia.Controls;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// Bronze and Thunder, the hidden arcade, opened with ⌃⇧F12 from the reader
/// and fed with ⌃⇧Return - the Windows chords, in no menu, being a secret.
///
/// One game window at a time, as on Windows: opening it again brings the
/// running game forward rather than starting a second. The game reads the
/// library that was open when it started and is pinned to it; the reader's
/// selected passage and the jump that puts a clue at the top of its page are
/// the main window's (Shell/MainWindow.Arcade.cs).
/// </summary>
public static class ArcadeFeature
{
    private static BronzeArcadeWindow? _window;

    /// <summary>
    /// Where the arcade keeps its checkpoint and chronicle, or null for
    /// BronzeSaveStore's own choice - the application's data folder, one file
    /// per library. Tests point it at a scratch folder.
    /// </summary>
    internal static string? SaveDirectory { get; set; }

    /// <summary>The open game window, if any.</summary>
    internal static BronzeArcadeWindow? Window => _window;

    public static void Open(IReaderHost host)
    {
        if (_window != null)
        {
            _window.BringForward();
            return;
        }

        try
        {
            var library = host.Window;
            var window = new BronzeArcadeWindow(DbConnectionFactory.DatabasePath,
                () => MainWindow.Current?.SelectedPassageId,
                () => DbConnectionFactory.DatabasePath,
                async workId => { await host.OpenWorkAsync(workId); },
                SaveDirectory,
                openPassage: (workId, nodeId) => MainWindow.Current is { } main
                    ? main.NavigateToPassageAtTopAsync(workId, nodeId)
                    : host.NavigateToPassageAsync(workId, nodeId),
                activateLibrary: () =>
                {
                    if (!library.IsVisible) return;
                    if (library.WindowState == WindowState.Minimized) library.WindowState = WindowState.Normal;
                    library.Show();
                    library.Activate();
                });

            // The game does not outlive the reader it reads from: closing the
            // main window closes it, which saves its checkpoint on the way.
            void CloseWithLibrary(object? sender, EventArgs e) => window.Close();
            library.Closed += CloseWithLibrary;
            window.Closed += (_, _) =>
            {
                library.Closed -= CloseWithLibrary;
                if (ReferenceEquals(_window, window)) _window = null;
            };

            Place(window, library);
            _window = window;
            window.Show();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "opening the arcade");
            _ = Alert.Error(host.Window, "Bronze & Thunder", $"Couldn't open the arcade: {ex.Message}");
        }
    }

    /// <summary>Whether the arcade window is open, for the ⌃⇧Return chord.</summary>
    public static bool IsOpen => _window != null;

    /// <summary>Hands the reader's selected passage to the open arcade (⌃⇧Return).</summary>
    public static void SubmitReaderSelection()
    {
        if (_window is { } window) _ = window.SubmitReaderSelectionAsync();
    }

    /// <summary>
    /// Centres the game on the screen the reader is on, not on the reader -
    /// it is a window of its own, not one riding on the main window - and
    /// shortens it to fit a small display, where 860 points of game would run
    /// under the Dock.
    /// </summary>
    private static void Place(BronzeArcadeWindow window, Window library)
    {
        var screen = library.Screens.ScreenFromVisual(library) ?? library.Screens.Primary;
        if (screen == null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var area = screen.WorkingArea;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, area.Height / scaling - 24));

        var width = (int)(window.Width * scaling);
        var height = (int)(window.Height * scaling);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(
            Math.Max(area.X, area.X + (area.Width - width) / 2),
            Math.Max(area.Y, area.Y + (area.Height - height) / 2));
    }
}
