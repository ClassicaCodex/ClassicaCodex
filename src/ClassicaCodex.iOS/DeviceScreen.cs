using UIKit;

namespace ClassicaCodex.iOS;

/// <summary>
/// Keeps the screen on while something long is running. iOS suspends an app when
/// the screen locks, which ends a download part of the way through, and a large
/// setup step takes far longer than an iPad's auto-lock.
/// </summary>
internal static class DeviceScreen
{
    private static int _holds;

    public static IDisposable StayAwake()
    {
        Change(+1);
        return new Hold();
    }

    private static void Change(int by) => UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
    {
        _holds += by;
        UIApplication.SharedApplication.IdleTimerDisabled = _holds > 0;
    });

    private sealed class Hold : IDisposable
    {
        public void Dispose() => Change(-1);
    }
}
