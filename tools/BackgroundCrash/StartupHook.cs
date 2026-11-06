/// <summary>
/// Crashes a finished app on a background thread, which nothing inside a test
/// host can do and live. DOTNET_STARTUP_HOOKS loads this into the app before
/// its Main, so the app itself carries no way of crashing on request. The
/// Windows workflow's smoke test uses it to see the crash dialog on a real
/// desktop, and to see the process held open until the dialog is answered.
/// </summary>
internal static class StartupHook
{
    private const string SecondsVariable = "CC_CRASH_AFTER_SECONDS";

    public static void Initialize()
    {
        var seconds = int.TryParse(Environment.GetEnvironmentVariable(SecondsVariable), out var chosen) ? chosen : 20;

        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            throw new InvalidOperationException("A crash on a background thread, asked for by a test.");
        })
        {
            IsBackground = true
        }.Start();
    }
}
