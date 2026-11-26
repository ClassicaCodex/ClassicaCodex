using Avalonia;

namespace ClassicaCodex.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;

            CrashLog.Write(ex, "running (unhandled)");

            // A background thread failing outside the UI thread ends the
            // process whatever is done here. The classic Windows app says so
            // before it goes, rather than vanishing; this one does too.
            if (e.IsTerminating && OperatingSystem.IsWindows()) CrashLog.SayClosingOnWindows(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "running a background task");
            e.SetObserved();
        };

        CrashLog.Install();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true })
            .With(new X11PlatformOptions { WmClass = "classica-codex" });
}
