using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (OperatingSystem.IsWindows()) GiveWindowsTheirIcon();
    }

    /// <summary>
    /// The icon in the executable reaches the taskbar, but a title bar shows
    /// what its window was given. Every window has one, so a style gives it.
    /// </summary>
    private void GiveWindowsTheirIcon()
    {
        using var file = AssetLoader.Open(new Uri("avares://ClassicaCodex.Mac/Assets/app.ico"));
        var icon = new WindowIcon(file);
        Styles.Add(new Style(x => x.Is<Window>()) { Setters = { new Setter(Window.IconProperty, icon) } });
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Before any window reads from it. What stops it is for the main
            // window to say: an alert here, with no window yet, would end the
            // app when it was closed.
            var problem = await LibraryFile.OpenRememberedAsync();

            // The PDF writer's fonts, found once in the background: the shared
            // writer looks them up in the Windows registry, and the Mac's own
            // resolver reads a few hundred font files the first time it is
            // asked. Warmed here so the first PDF export does not wait on it.
            _ = Task.Run(() =>
            {
                try { Features.Export.PdfFonts.EnsureInstalled(); }
                catch (Exception ex) { CrashLog.Write(ex, "finding fonts for PDF export"); }
            });

            desktop.MainWindow = new MainWindow { LibraryProblem = problem };
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnAboutClicked(object? sender, EventArgs e) => MainWindow.Current?.OpenTool("about");

    private void OnSettingsClicked(object? sender, EventArgs e) => MainWindow.Current?.OpenTool("settings");
}
