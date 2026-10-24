using ClassicaCodex.Mac.Features.Tags;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.MythNetwork;

/// <summary>
/// Entry point for the myth network, called from the main window's menus,
/// toolbar and tool list.
/// </summary>
public static class MythNetworkFeature
{
    /// <summary>
    /// Opens the network beside the reader, or brings the open one forward -
    /// its layout is the reader's own arrangement, and a second window would
    /// settle a different one.
    /// </summary>
    public static void Open(IReaderHost host)
    {
        if (TagsFeature.Find<MythNetworkWindow>(host) is { } open)
        {
            open.Activate();
            return;
        }

        var window = new MythNetworkWindow(host) { OnNavigate = host.NavigateToPassageAsync };
        window.Show(host.Window);
    }
}
