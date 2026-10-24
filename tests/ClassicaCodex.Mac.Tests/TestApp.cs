using Avalonia;
using Avalonia.Headless;
using ClassicaCodex.Mac.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

// One test at a time. The library connection is static - DbConnectionFactory
// holds one path for the whole process, as the app does - so two tests
// pointing it at different copies, or one clearing it to test the first-run
// page, would pull the library out from under each other. The headless
// platform runs every test on one UI thread anyway, so little is lost.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// Runs the real application - its theme, its styles, its windows - on
/// Avalonia's headless platform, drawing with Skia so a test can photograph
/// a window exactly as it renders. This is how the Mac front end is checked
/// on a machine that is not a Mac.
/// </summary>
public static class TestApp
{
    static TestApp()
    {
        // Preferences and settings go to a scratch folder, never the real
        // one: a test that saved a reading position or a window size would
        // otherwise leave it behind for the reader to find.
        var scratch = Path.Combine(Path.GetTempPath(), "classicacodex-mac-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", scratch);
        Environment.SetEnvironmentVariable("CC_MAC_LOOK", "1");
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
