using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ClassicaCodex.Data;

namespace ClassicaCodex.Mac.Tests;

/// <summary>Photographs windows to PNG, for looking at what the tests drove.</summary>
public static class Screens
{
    /// <summary>
    /// Where screenshots go: CC_SCREENSHOT_DIR if set, otherwise nowhere -
    /// taking them is for a person to look at, not something a test needs.
    /// </summary>
    public static string? Directory => Environment.GetEnvironmentVariable("CC_SCREENSHOT_DIR");

    /// <summary>The sample library to drive the windows against, if one has been built.</summary>
    public static string? SampleLibrary
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("CC_SAMPLE_LIBRARY");
            return path != null && File.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Points the library connection at nothing, as on a first run. It is
    /// static, so without this a new MainWindow loads whatever library the
    /// last test left it on - or fails to, if that test has since deleted it.
    /// </summary>
    public static void ForgetLibrary() =>
        typeof(DbConnectionFactory)
            .GetField("_databasePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);

    /// <summary>Lets queued layout, data loading and rendering finish.</summary>
    public static async Task Settle(int rounds = 6)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(40);
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static void Save(TopLevel window, string name)
    {
        if (Directory == null) return;

        System.IO.Directory.CreateDirectory(Directory);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(Directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
