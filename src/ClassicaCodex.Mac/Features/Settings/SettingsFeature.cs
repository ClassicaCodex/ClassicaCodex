using Avalonia.Controls;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Settings;

/// <summary>Opens Settings (⌘,), and its AI pane on its own for the features that need a key.</summary>
public static class SettingsFeature
{
    private static SettingsWindow? _open;

    /// <summary>
    /// Opens Settings, or brings it forward if it is already open - one
    /// settings window, as every Mac app has.
    /// </summary>
    public static void Open(IReaderHost host) => Show(host, SettingsWindow.General);

    private static SettingsWindow Show(IReaderHost host, string pane)
    {
        if (_open is { IsVisible: true })
        {
            _open.ShowPane(pane);
            _open.Activate();
            return _open;
        }

        _open = new SettingsWindow(host, pane);
        _open.Closed += (_, _) => _open = null;
        _open.Show(host.Window);
        return _open;
    }

    /// <summary>
    /// The AI keys and the confirm-before-sending switch, as a dialog - the
    /// Windows "AI Translation Settings" form, which every AI feature opens
    /// when there is no key yet. Returns when it is closed, so the caller can
    /// look again for a key.
    /// </summary>
    public static async Task OpenAiSettingsAsync(Window owner)
    {
        if (MainWindow.Current is not { } host) return;

        _open?.Close();
        var window = new SettingsWindow(host, SettingsWindow.Ai) { Title = "AI Settings" };
        await window.ShowDialog(owner);
    }
}
