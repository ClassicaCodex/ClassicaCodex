using Avalonia.Controls;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Search;

/// <summary>
/// Entry points for Search, called from the main window's menus, toolbar
/// and context menus.
/// </summary>
public static class SearchFeature
{
    private static SearchWindow? _window;

    /// <summary>
    /// Opens the search window, or brings the open one forward.
    ///
    /// One window, kept while it is open, as MainForm keeps its SearchForm:
    /// the filters someone has set are worth more than a fresh window, and
    /// pressing ⌘F again should find the search they were in the middle of,
    /// not start another beside it. Modeless, so looking at a result in the
    /// reader never means closing the thing that found it.
    /// </summary>
    public static void Open(IReaderHost host)
    {
        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            open.FocusQuery();
            return;
        }

        var window = new SearchWindow
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
