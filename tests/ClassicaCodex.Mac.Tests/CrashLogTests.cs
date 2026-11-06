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
        // No library, so the window loads nothing: a library another test
        // left configured, and has perhaps deleted, would put up an alert of
        // its own beside the one this test counts.
        Screens.ForgetLibrary();

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

    /// <summary>
    /// The real thing ends the process, which a test can't survive, so
    /// smoke-test.ps1 crashes the finished app. This is what happens up to
    /// that point: the failing thread is held until the dialog is answered.
    /// </summary>
    [AvaloniaFact]
    public async Task A_crash_on_another_thread_says_so_and_waits_for_an_answer()
    {
        Screens.ForgetLibrary();

        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        await Screens.Settle();

        var crash = new Thread(() => CrashLog.ReportFatal(new InvalidOperationException("outer", new IOException("The drive went away."))))
        {
            IsBackground = true
        };
        crash.Start();

        for (var i = 0; i < 200 && window.OwnedWindows.Count == 0; i++) await Screens.Settle(1);

        var alert = Assert.Single(window.OwnedWindows);
        var message = alert.GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text!;
        Assert.StartsWith("Classica Codex hit an error it can't recover from and has to close.", message);
        Assert.Contains("The drive went away.", message);
        Assert.Contains(CrashLog.LogFile, message);
        Assert.Contains("The drive went away.", await File.ReadAllTextAsync(CrashLog.LogFile));

        await Screens.Settle();
        Assert.True(crash.IsAlive);

        Screens.Save(alert, "crash-closing");
        alert.Close();
        for (var i = 0; i < 200 && crash.IsAlive; i++) await Screens.Settle(1);

        Assert.False(crash.IsAlive);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_crash_does_not_wait_on_a_UI_thread_that_cannot_answer()
    {
        Screens.ForgetLibrary();

        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        await Screens.Settle();

        // Stuck, as the UI thread is here while it waits for this thread.
        var crash = new Thread(() => CrashLog.ReportFatal(new InvalidOperationException("Stuck."), waitForUiThreadMilliseconds: 100))
        {
            IsBackground = true
        };
        crash.Start();
        Assert.True(crash.Join(TimeSpan.FromSeconds(10)));

        // The UI thread itself, which can't wait for its own dialog.
        CrashLog.ReportFatal(new InvalidOperationException("On the UI thread."));
        Assert.Contains("On the UI thread.", await File.ReadAllTextAsync(CrashLog.LogFile));

        // Only the one from the stuck thread, which arrives once it isn't.
        await Screens.Settle();
        foreach (var alert in window.OwnedWindows.ToList()) alert.Close();
        await Screens.Settle();
        window.Close();
    }
}
