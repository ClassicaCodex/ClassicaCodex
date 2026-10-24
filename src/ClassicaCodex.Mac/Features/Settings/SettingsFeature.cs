using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Settings;

/// <summary>Opens the Settings window (⌘,).</summary>
public static class SettingsFeature
{
    public static void Open(IReaderHost host) =>
        ComingSoon.Show(host, "Settings", "settings", "Settings");
}
