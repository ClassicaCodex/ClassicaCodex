using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The library the reader last used, if it is still where it was.
            // Its schema is brought up to date before any window reads from it -
            // the same migrations the Windows app runs, so a library copied from
            // the PC opens here as it is.
            if (DbConnectionFactory.TryConfigureFromPreferred())
            {
                try
                {
                    await SchemaInitializer.EnsureSchemaAsync();
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex, "upgrading the library at launch");
                }
            }

            // The PDF writer's fonts, found once in the background: the shared
            // writer looks them up in the Windows registry, and the Mac's own
            // resolver reads a few hundred font files the first time it is
            // asked. Warmed here so the first PDF export does not wait on it.
            _ = Task.Run(() =>
            {
                try { Features.Export.PdfFonts.EnsureInstalled(); }
                catch (Exception ex) { CrashLog.Write(ex, "finding fonts for PDF export"); }
            });

            desktop.MainWindow = new MainWindow();
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnAboutClicked(object? sender, EventArgs e) => MainWindow.Current?.OpenTool("about");

    private void OnSettingsClicked(object? sender, EventArgs e) => MainWindow.Current?.OpenTool("settings");
}
