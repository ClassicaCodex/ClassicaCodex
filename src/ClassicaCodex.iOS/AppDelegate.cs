using Avalonia;
using Avalonia.iOS;
using ClassicaCodex.Mac;
using Foundation;
using UIKit;

namespace ClassicaCodex.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<MobileApp>
{
    // Junicode for the medieval texts, as on the Mac. See BundledFonts.
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).With(ClassicaCodex.Mac.Platform.BundledFonts.Options());
}

public static class Program
{
    public static void Main(string[] args)
    {
        CrashLog.WatchBackgroundWork();
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
