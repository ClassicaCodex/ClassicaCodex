using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Settings;

/// <summary>Opens the Settings window (⌘,).</summary>
public static class SettingsFeature
{
    public static void Open(IReaderHost host) =>
        ComingSoon.Show(host, "Settings", "settings", "Settings");

    /// <summary>
    /// The AI keys and the confirm-before-sending switch, as a dialog - the
    /// Windows "AI Translation Settings" form, which every AI feature opens
    /// when there is no key yet. Returns when it is closed.
    /// </summary>
    public static Task OpenAiSettingsAsync(Avalonia.Controls.Window owner)
    {
        if (MainWindow.Current is { } host) ComingSoon.Show(host, "AI Settings", "settings", "Key");
        return Task.CompletedTask;
    }
}
