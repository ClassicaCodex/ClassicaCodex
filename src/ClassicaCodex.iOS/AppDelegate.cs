using Avalonia.iOS;
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
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine($"UNHANDLED: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"UNOBSERVED: {e.Exception}");
            e.SetObserved();
        };

        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
