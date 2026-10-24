using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Opens_on_the_no_library_page_when_there_is_no_library()
    {
        // The factory is static and another test may have pointed it at the
        // sample library; this one is about the state before any library.
        typeof(DbConnectionFactory)
            .GetField("_databasePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);

        var window = new MainWindow { Width = 1280, Height = 800 };
        window.Show();
        await Screens.Settle();

        Screens.Save(window, "00-no-library");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Reads_a_work_side_by_side_from_the_sample_library()
    {
        if (Screens.SampleLibrary is not { } library) return;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(10);
        Screens.Save(window, "01-welcome-light");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var sidebar = window.GetVisualDescendants().OfType<LibrarySidebar>().Single();
        sidebar.ShowMode("tools");
        await Screens.Settle(6);
        Screens.Save(window, "01b-welcome-dark-tools");
        sidebar.ShowMode("library");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        var agamemnon = window.LibraryWorks.First(w => w.Title.Contains("Agamemnon"));
        Assert.True(await window.OpenWorkAsync(agamemnon.WorkId));
        await Screens.Settle(10);
        Screens.Save(window, "02-reader-light");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(window, "03-reader-dark");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window.OpenTool("gallery");
        await Screens.Settle(6);
        foreach (var gallery in window.OwnedWindows.OfType<ToolGalleryWindow>().ToList())
        {
            Screens.Save(gallery, "04-gallery");
            gallery.Close();
        }

        window.Close();
    }
}
