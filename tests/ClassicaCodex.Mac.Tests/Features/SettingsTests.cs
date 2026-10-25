using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

public class SettingsTests
{
    [AvaloniaFact]
    public async Task Every_pane_opens_and_a_reading_choice_reaches_the_shared_preference()
    {
        if (Screens.SampleLibrary is { } library)
        {
            DbConnectionFactory.Configure(library, remember: false);
            await SchemaInitializer.EnsureSchemaAsync();
        }

        var main = new MainWindow { Width = 1200, Height = 800 };
        main.Show();
        await Screens.Settle();

        var settings = new SettingsWindow(main);
        settings.Show(main);

        foreach (var pane in new[] { SettingsWindow.General, SettingsWindow.Reading, SettingsWindow.Ai, SettingsWindow.Voice, SettingsWindow.Library })
        {
            settings.ShowPane(pane);
            await Screens.Settle(3);
            Screens.Save(settings, $"settings-{pane}");
        }

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        settings.ShowPane(SettingsWindow.Ai);
        await Screens.Settle(3);
        Screens.Save(settings, "settings-ai-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        // The window writes the same file the Windows app reads.
        var before = PaneSyncSettings.Enabled;
        main.SetPanesLinkedFromSettings(!before);
        Assert.Equal(!before, PaneSyncSettings.Enabled);
        main.SetPanesLinkedFromSettings(before);

        settings.Close();
        main.Close();
    }
}
