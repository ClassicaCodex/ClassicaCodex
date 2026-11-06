using Avalonia.iOS;
using ClassicaCodex.Mac;
using Foundation;
using UIKit;

namespace ClassicaCodex.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<MobileApp>
{
}

public static class Program
{
    public static void Main(string[] args)
    {
        CrashLog.WatchBackgroundWork();
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
