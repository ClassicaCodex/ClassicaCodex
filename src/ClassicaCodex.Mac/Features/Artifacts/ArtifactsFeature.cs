using Avalonia.Controls;

namespace ClassicaCodex.Mac.Features.Artifacts;

/// <summary>
/// The Perseus artifact browser - photographs of the objects behind a name or
/// a place, fetched live when asked for. Opened from the Myth Network and from
/// result lists; the Places Map holds its own browser in its layout.
/// </summary>
public static class ArtifactsFeature
{
    /// <summary>Opens the browser for one name: a figure, a place.</summary>
    public static void Open(Window owner, string title, string searchTerm)
    {
        // Asking twice for the same name brings the open window forward rather
        // than stacking a second copy of the same search on top of it.
        var existing = owner.OwnedWindows.OfType<ArtifactBrowserWindow>()
            .FirstOrDefault(w => string.Equals(w.SearchTerm, searchTerm, StringComparison.Ordinal));
        if (existing != null)
        {
            existing.Activate();
            return;
        }

        var window = new ArtifactBrowserWindow(title, searchTerm);
        window.Show(owner);
    }
}
