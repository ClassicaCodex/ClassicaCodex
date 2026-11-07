using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

public class SettingsTests
{
    /// <summary>
    /// The table only matches a sentence word for word, so this reads what the
    /// pane really shows. Here that is the Mac text, which is what an iPad rewords.
    /// </summary>
    [AvaloniaFact]
    public async Task The_AI_pane_on_an_iPad_gives_Googles_terms_and_no_other_company()
    {
        var main = new MainWindow { Width = 1200, Height = 800 };
        main.Show();
        await Screens.Settle();

        MacDataFolder.OnDevice = true;
        var settings = new SettingsWindow(main, SettingsWindow.Ai);
        settings.Show(main);
        await Screens.Settle(3);

        var shown = settings.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)
            .Concat(settings.GetVisualDescendants().OfType<Button>().Select(button => button.Content as string))
            .OfType<string>()
            .Select(SystemWords.ForDevice)
            .ToList();

        settings.Close();
        MacDataFolder.OnDevice = false;
        main.Close();

        Assert.DoesNotContain(shown, line => line.Contains("genuinely free", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(shown, line => line.Contains("no card required", StringComparison.Ordinal));
        Assert.Contains("Get an API key →", shown);
        Assert.Contains(shown, line => line.Contains("18 or older", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, line => line.Contains("Anthropic", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, line => line.Contains("either", StringComparison.Ordinal));
    }

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
