using Avalonia.Controls;
using Avalonia.Input;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// The base for every tool window: Search, Word Study, the map, the benches.
///
/// It carries the two callbacks the Windows forms carry - OnNavigate, which
/// puts the reader on a passage, and OnOpenWork, which opens a work - so a
/// ported window jumps to a result the same way its Windows original does,
/// and the main window stays the only thing that knows how to read.
///
/// Escape closes it, as every window on Windows does, and so does ⌘W, which
/// is what a Mac user will press first.
/// </summary>
public class FeatureWindow : Window
{
    /// <summary>Puts the main reader on a passage: work id, text node id.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    /// <summary>Opens a work in the main reader, without choosing a line.</summary>
    public Func<int, Task<bool>>? OnOpenWork { get; set; }

    public FeatureWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 980;
        Height = 680;
        MinWidth = 520;
        MinHeight = 360;
        KeyDown += OnFeatureKeyDown;
    }

    private void OnFeatureKeyDown(object? sender, KeyEventArgs e)
    {
        var closeChord = e.Key == Key.W && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
        if (e.Key == Key.Escape || closeChord)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>Jumps the reader to a passage, if this window was given a way to.</summary>
    protected async Task NavigateAsync(int workId, long textNodeId)
    {
        if (OnNavigate != null) await OnNavigate(workId, textNodeId);
    }

    protected Task ShowErrorAsync(string title, Exception ex) => Alert.Error(this, title, ex.Message);

    protected Task ShowInfoAsync(string title, string message) => Alert.Info(this, title, message);
}
