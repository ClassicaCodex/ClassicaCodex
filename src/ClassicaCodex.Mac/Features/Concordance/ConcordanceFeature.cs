using Avalonia.Controls;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Concordance;

/// <summary>
/// Entry points for the Concordance, called from the main window's menus,
/// toolbar and context menus.
/// </summary>
public static class ConcordanceFeature
{
    private static ConcordanceWindow? _window;

    /// <summary>
    /// Opens the concordance beside the reader, or brings the open one
    /// forward. Windows shows it modally, which made it one at a time without
    /// saying so; modeless here, it stays one at a time on purpose, so ⌘⇧K
    /// finds the concordance already built rather than stacking another.
    /// </summary>
    public static void Open(IReaderHost host)
    {
        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            open.FocusWord();
            return;
        }

        var window = new ConcordanceWindow
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window)) _window = null;
        };

        _window = window;
        window.Show(host.Window);
    }
}
