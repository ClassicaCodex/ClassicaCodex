using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Timeline;

/// <summary>
/// Entry point for the author timeline, called from the toolbar, the Tools
/// menu and the gallery.
/// </summary>
public static class TimelineFeature
{
    public static void Open(IReaderHost host)
    {
        // One timeline beside the reader: a second click brings it forward
        // rather than stacking another copy of the same chart over it.
        var existing = host.Window.OwnedWindows.OfType<TimelineWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            return;
        }

        var window = new TimelineWindow { OnOpenWork = host.OpenWorkAsync };
        window.Show(host.Window);
    }
}
