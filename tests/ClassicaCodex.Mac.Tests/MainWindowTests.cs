using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Search;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Opens_on_the_no_library_page_when_there_is_no_library()
    {
        // The factory is static and another test may have pointed it at the
        // sample library; this one is about the state before any library.
        Screens.ForgetLibrary();

        var window = new MainWindow { Width = 1280, Height = 800 };
        window.Show();
        await Screens.Settle();

        Screens.Save(window, "00-no-library");
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_collection_filter_lights_its_button_in_either_appearance()
    {
        var previousLibrary = DbConnectionFactory.DatabasePath;
        var previousTheme = Avalonia.Application.Current!.RequestedThemeVariant;
        var folder = Path.Combine(Path.GetTempPath(), "classicacodex-mainwindow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        MainWindow? window = null;

        try
        {
            // Two collections, one work each: the least that shows the button.
            DbConnectionFactory.Configure(Path.Combine(folder, "library.db"), remember: false);
            await SchemaInitializer.EnsureSchemaAsync();
            await using (var conn = await DbConnectionFactory.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES
                        (1, 'urn:cts:greekLit:tlg0085', 'Aeschylus', 'greekLit'),
                        (2, 'urn:cts:latinLit:phi0448', 'Caesar', 'latinLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                        (1, 1, 'urn:cts:greekLit:tlg0085.tlg005', 'Agamemnon'),
                        (2, 2, 'urn:cts:latinLit:phi0448.phi002', 'Civil War');
                    INSERT INTO Editions (WorkId, CtsUrn, Kind, Collection) VALUES
                        (1, 'urn:cts:greekLit:tlg0085.tlg005.perseus-grc2', 'Original', 'perseus-greek'),
                        (2, 'urn:cts:latinLit:phi0448.phi002.perseus-lat2', 'Original', 'perseus-latin');";
                await cmd.ExecuteNonQueryAsync();
            }

            window = new MainWindow { Width = 1280, Height = 800 };
            window.Show();
            await Screens.Settle();

            IBrush Accent() =>
                window.TryFindResource("Cc.Accent", window.ActualThemeVariant, out var brush) ? (IBrush)brush! : throw new InvalidOperationException("No Cc.Accent");

            static List<CheckBox> Boxes(Button button)
            {
                Assert.True(button.IsVisible);
                var boxes = ((Panel)((Flyout)FlyoutBase.GetAttachedFlyout(button)!).Content!).Children.OfType<CheckBox>().ToList();
                Assert.Equal(2, boxes.Count);
                return boxes;
            }

            // The library sidebar's filter, which narrows the tree as it is ticked.
            var sidebar = window.GetVisualDescendants().OfType<LibrarySidebar>().Single();
            var boxes = Boxes(sidebar.CollectionsButton);

            boxes[0].IsChecked = true;
            await Screens.Settle();
            Assert.Single(sidebar.Tree.ItemsSource!.Cast<object>());
            Assert.Same(Accent(), sidebar.CollectionsButton.Foreground);

            // A filter left on when the Mac turns dark is still marked, in the dark accent.
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            await Screens.Settle();
            Assert.Same(Accent(), sidebar.CollectionsButton.Foreground);

            boxes[0].IsChecked = false;
            await Screens.Settle();
            Assert.Equal(2, sidebar.Tree.ItemsSource!.Cast<object>().Count());
            Assert.NotSame(Accent(), sidebar.CollectionsButton.Foreground);

            // Search's own filter, which waits for the next search to apply it.
            window.OpenTool("search");
            await Screens.Settle();
            var search = window.OwnedWindows.OfType<SearchWindow>().Single();
            boxes = Boxes(search.CollectionsButton);

            boxes[1].IsChecked = true;
            await Screens.Settle();
            Assert.Same(Accent(), search.CollectionsButton.Foreground);

            boxes[1].IsChecked = false;
            await Screens.Settle();
            Assert.NotSame(Accent(), search.CollectionsButton.Foreground);
        }
        finally
        {
            foreach (var owned in window?.OwnedWindows.ToList() ?? new()) owned.Close();
            window?.Close();
            Avalonia.Application.Current!.RequestedThemeVariant = previousTheme;
            SqliteConnection.ClearAllPools();
            if (previousLibrary != null) DbConnectionFactory.Configure(previousLibrary, remember: false);
            else Screens.ForgetLibrary();
            Directory.Delete(folder, recursive: true);
        }
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
