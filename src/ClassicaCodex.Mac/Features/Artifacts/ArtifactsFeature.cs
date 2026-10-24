using Avalonia.Controls;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Artifacts;

/// <summary>
/// The Perseus artifact browser - photographs of the objects behind a name or
/// a place, fetched live when asked for. Opened from the Myth Network, from
/// result lists and (embedded) from the Places Map. A stub until ported.
/// </summary>
public static class ArtifactsFeature
{
    /// <summary>Opens the browser for one name: a figure, a place.</summary>
    public static void Open(Window owner, string title, string searchTerm)
    {
        if (MainWindow.Current is { } host) ComingSoon.Show(host, $"Artifacts - {title}", "places-map", "Image", "The artifact browser");
    }
}
