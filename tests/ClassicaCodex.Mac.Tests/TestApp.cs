using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using ClassicaCodex.Data;
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
    /// <summary>
    /// A module initializer rather than a static constructor, so that it runs
    /// before any test does. A static constructor waits for the first
    /// [AvaloniaFact] to start the headless platform, and a plain [Fact] that
    /// ran first and touched a setting would have found the real folder.
    /// </summary>
    [ModuleInitializer]
    internal static void KeepToScratchFolders()
    {
        // Preferences and settings go to a scratch folder, never the real
        // one: a test that saved a reading position or a window size would
        // otherwise leave it behind for the reader to find. PreferencesFolder
        // is the folder every settings class asks for, and the one redirect
        // that works on Windows, where nothing outside the process can move
        // LocalApplicationData. The environment variables move what the
        // operating system itself is asked for - on a Mac, Documents too.
        var scratch = Path.Combine(Path.GetTempPath(), "classicacodex-mac-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        PreferencesFolder.UseInstead(Path.Combine(scratch, "ClassicaCodex"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", scratch);
        if (OperatingSystem.IsMacOS()) UseScratchHomeOnMac(scratch);
        Environment.SetEnvironmentVariable("CC_MAC_LOOK", "1");
    }

    /// <summary>
    /// XDG_DATA_HOME is how Linux is told; a Mac takes no notice of it. .NET
    /// asks macOS itself where Application Support and Documents are, and
    /// macOS answers from CFFIXED_USER_HOME when that is set - set in the
    /// process's real environment, which Environment.SetEnvironmentVariable
    /// never reaches on a Mac, hence setenv. If it does not take, the tests
    /// stop here rather than reach into the reader's own Documents.
    /// </summary>
    private static void UseScratchHomeOnMac(string scratch)
    {
        var home = Path.Combine(scratch, "home");
        Directory.CreateDirectory(Path.Combine(home, "Library", "Application Support"));
        Directory.CreateDirectory(Path.Combine(home, "Documents"));

        var real = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        setenv("CFFIXED_USER_HOME", home, 1);

        if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) == real)
            throw new InvalidOperationException($"The tests would use the reader's own folders: macOS did not take up CFFIXED_USER_HOME, and {real} did not move.");
    }

    [DllImport("libc")]
    private static extern int setenv(string name, string value, int overwrite);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
