using Avalonia;

namespace ClassicaCodex.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLog.WatchBackgroundWork();
        CrashLog.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .With(new X11PlatformOptions { WmClass = "classica-codex" });
}
