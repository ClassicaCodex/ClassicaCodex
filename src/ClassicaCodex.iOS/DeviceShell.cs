using ClassicaCodex.Mac;
using Foundation;
using UIKit;

namespace ClassicaCodex.iOS;

/// <summary>
/// What <c>open</c> does on a Mac, for a device where no process can be started.
/// A web page goes to the browser. A file or folder can only be shown by the
/// Files app, which sees this app's Documents folder, and that is where the
/// library, the downloads and the error log all are.
/// </summary>
internal static class DeviceShell
{
    public static bool Open(string pathOrUrl)
    {
        if (Address(pathOrUrl) is not { } address) return false;
        if (!UIApplication.SharedApplication.CanOpenUrl(address)) return false;

        UIApplication.SharedApplication.OpenUrl(address, new UIApplicationOpenUrlOptions(), opened =>
        {
            if (!opened) CrashLog.Write(new InvalidOperationException($"iOS did not open {pathOrUrl}."), "opening a link");
        });

        return true;
    }

    /// <summary>
    /// The address itself for a page, and the Files app's own scheme for a path.
    /// Info.plist has to name that scheme, or CanOpenUrl says no to it.
    /// </summary>
    private static NSUrl? Address(string pathOrUrl) =>
        Path.IsPathRooted(pathOrUrl)
            ? NSUrl.FromString(NSUrl.FromFilename(pathOrUrl).AbsoluteString!.Replace("file://", "shareddocuments://"))
            : NSUrl.FromString(pathOrUrl);
}
