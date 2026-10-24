using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

public class CrashLogTests
{
    [AvaloniaFact]
    public async Task An_error_in_an_action_is_reported_and_the_app_keeps_running()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        await Screens.Settle();

        using (CrashLog.Install())
        {
            // Twice, as a failure that repeats would: one box, not two.
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("outer", new IOException("The disk is full.")));
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("again"));
            await Screens.Settle();

            var alert = Assert.Single(window.OwnedWindows);
            var message = alert.GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text!;
            Assert.StartsWith("Something went wrong with that action.", message);
            Assert.Contains("The disk is full.", message);
            Assert.Contains(CrashLog.LogFile, message);

            Screens.Save(alert, "crash-report");
            alert.Close();
            await Screens.Settle();
        }

        Assert.Contains("The disk is full.", await File.ReadAllTextAsync(CrashLog.LogFile));
        Assert.True(window.IsVisible);
        window.Close();
    }
}
