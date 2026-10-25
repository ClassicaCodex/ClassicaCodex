using Avalonia.Controls;

namespace ClassicaCodex.Mac.Features.Research;

/// <summary>
/// Keeps one window per job open at a time: one Research Bench per work, one
/// claims matrix per project, one notebook per evidence item.
///
/// On Windows each of these is a modal dialog, so a second copy could never
/// be opened. Here they are ordinary windows beside the reader, and two
/// claims matrices for the same project would each save over the other's
/// edits without either knowing; asking for one that is already open brings
/// it forward instead.
/// </summary>
internal static class ResearchWindows
{
    private static readonly Dictionary<string, Window> Open = new(StringComparer.Ordinal);

    /// <summary>The open window for <paramref name="key"/>, if there is one.</summary>
    public static T? Find<T>(string key) where T : Window =>
        Open.TryGetValue(key, out var window) && window.IsVisible ? window as T : null;

    /// <summary>
    /// Brings the window for <paramref name="key"/> forward, or makes it with
    /// <paramref name="create"/> and shows it beside <paramref name="owner"/>.
    /// </summary>
    /// <returns>The window, and whether it was made just now.</returns>
    public static (T Window, bool Created) Show<T>(string key, Window owner, Func<T> create) where T : Window
    {
        if (Find<T>(key) is { } existing)
        {
            existing.Activate();
            return (existing, false);
        }

        var window = create();
        Open[key] = window;
        window.Closed += (_, _) =>
        {
            if (Open.TryGetValue(key, out var current) && ReferenceEquals(current, window)) Open.Remove(key);
        };
        window.Show(owner);
        return (window, true);
    }

    /// <summary>Every open research window, for the tests.</summary>
    internal static IReadOnlyCollection<Window> All => Open.Values.Where(w => w.IsVisible).ToList();
}
