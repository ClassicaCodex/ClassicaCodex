using Avalonia.Controls;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.MedievalHands;

/// <summary>
/// Entry points for Medieval Hands, called from the main window's menus and
/// the sidebar's Tools list.
/// </summary>
public static class MedievalHandsFeature
{
    private static MedievalHandsWindow? _window;

    /// <summary>
    /// Opens Medieval Hands, or brings the open one forward.
    ///
    /// One window, as the Windows toolbar button opens one form: the filters,
    /// the search and the manuscript someone is looking at are worth more
    /// than a fresh window, and a download running in it must not be
    /// duplicated by a second. Modeless, so the reader stays usable beside it.
    /// </summary>
    public static void Open(IReaderHost host)
    {
        if (_window is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        var window = new MedievalHandsWindow(openSetup: () => SetupFeature.Open(host))
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
