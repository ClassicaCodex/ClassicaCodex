using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.PlacesMap;

/// <summary>
/// Entry point for the Places Map, called from the toolbar, the Tools menu
/// and the gallery.
/// </summary>
public static class PlacesMapFeature
{
    public static void Open(IReaderHost host)
    {
        // One map beside the reader. Modeless, so a second click on the tool
        // would otherwise stack a second map - with its own zoom and its own
        // list - over the first; it brings the open one forward instead.
        var existing = host.Window.OwnedWindows.OfType<PlacesMapWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            return;
        }

        var window = new PlacesMapWindow { OnNavigate = host.NavigateToPassageAsync };
        window.Show(host.Window);
    }
}
