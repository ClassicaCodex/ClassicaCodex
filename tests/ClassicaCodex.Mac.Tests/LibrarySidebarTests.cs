using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// The library tree's filters with a work open. A filter says what the reader
/// wants to look through, so typing one must not be undone by the work that
/// happens to be selected; a jump to a work is the opposite case, where the
/// filter gives way.
///
/// On a small library of their own, so these run everywhere.
/// </summary>
public class LibrarySidebarTests
{
    private const int Agamemnon = 1;
    private const int Iliad = 3;

    private sealed class Library : IAsyncDisposable
    {
        private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-sidebar-tests", Guid.NewGuid().ToString("N"));

        public MainWindow? Window { get; private set; }

        public async Task<LibrarySidebar> OpenAsync()
        {
            Directory.CreateDirectory(_folder);
            DbConnectionFactory.Configure(Path.Combine(_folder, "library.db"), remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            // Two collections, to bring the collections button out, and one
            // favourite, which is not the work the tests open.
            await using (var conn = await DbConnectionFactory.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES
                        (1, 'urn:cts:greekLit:tlg0085', 'Aeschylus', 'greekLit'),
                        (2, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit'),
                        (3, 'urn:cts:latinLit:phi0448', 'Caesar', 'latinLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                        (1, 1, 'urn:cts:greekLit:tlg0085.tlg005', 'Agamemnon'),
                        (2, 1, 'urn:cts:greekLit:tlg0085.tlg007', 'Eumenides'),
                        (3, 2, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad'),
                        (4, 3, 'urn:cts:latinLit:phi0448.phi002', 'Civil War');
                    INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                        (1, 1, 'urn:cts:greekLit:tlg0085.tlg005.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                        (2, 2, 'urn:cts:greekLit:tlg0085.tlg007.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                        (3, 3, 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                        (4, 4, 'urn:cts:latinLit:phi0448.phi002.perseus-lat2', 'Original', 'lat', 'perseus-latin');
                    INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES
                        (1, 'urn:cts:greekLit:tlg0085.tlg005.perseus-grc2:1', 1, 'θεοὺς μὲν αἰτῶ τῶνδ᾽ ἀπαλλαγὴν πόνων'),
                        (2, 'urn:cts:greekLit:tlg0085.tlg007.perseus-grc2:1', 1, 'πρῶτον μὲν εὐχῇ τῇδε πρεσβεύω θεῶν'),
                        (3, 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2:1.1', 1, 'μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος'),
                        (4, 'urn:cts:latinLit:phi0448.phi002.perseus-lat2:1.1', 1, 'litteris a Fabio C. Caesaris consulibus redditis');
                    INSERT INTO FavoriteWorks (CtsUrn) VALUES ('urn:cts:greekLit:tlg0012.tlg001');";
                await cmd.ExecuteNonQueryAsync();
            }

            Window = new MainWindow { Width = 1280, Height = 800 };
            Window.Show();
            await Screens.Settle();
            return Window.GetVisualDescendants().OfType<LibrarySidebar>().Single();
        }

        public ValueTask DisposeAsync()
        {
            foreach (var owned in Window?.OwnedWindows.ToList() ?? new()) owned.Close();
            Window?.Close();
            SqliteConnection.ClearAllPools();
            if (_previousLibrary != null) DbConnectionFactory.Configure(_previousLibrary, remember: false);
            else Screens.ForgetLibrary();

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open is the temporary folder's to clear.
            }

            return ValueTask.CompletedTask;
        }
    }

    private static TextBox FilterBox(LibrarySidebar sidebar) =>
        sidebar.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == "Filter authors");

    private static ToggleButton FavouritesOnly(LibrarySidebar sidebar) =>
        sidebar.GetVisualDescendants().OfType<ToggleButton>().Single(b => ToolTip.GetTip(b) as string == "Show favourites only");

    /// <summary>The tick box for the Latin collection, in the collections menu.</summary>
    private static CheckBox LatinCollection(LibrarySidebar sidebar) =>
        ((Panel)((Flyout)FlyoutBase.GetAttachedFlyout(sidebar.CollectionsButton)!).Content!).Children
        .OfType<CheckBox>()
        .Single(b => (b.Content as string)!.Contains("Latin", StringComparison.Ordinal));

    /// <summary>Waits out the pause the tree leaves for typing to settle, which the star shares.</summary>
    private static Task RebuildAsync() => Screens.Settle(10);

    private static async Task FilterForAsync(LibrarySidebar sidebar, string text)
    {
        FilterBox(sidebar).Text = text;
        await RebuildAsync();
    }

    private static List<AuthorNode> Authors(LibrarySidebar sidebar) =>
        sidebar.Tree.ItemsSource!.Cast<object>().OfType<AuthorNode>().ToList();

    private static List<string> AuthorNames(LibrarySidebar sidebar) =>
        Authors(sidebar).Select(a => a.Author.Name).ToList();

    private static WorkNode Row(LibrarySidebar sidebar, int workId) =>
        Authors(sidebar).SelectMany(a => a.Works).Single(w => w.Work.WorkId == workId);

    /// <summary>
    /// That the work's own row is selected and on show: the row in the tree
    /// as it stands, not one left over from before a rebuild, under an author
    /// opened to show it.
    /// </summary>
    private static void AssertSelected(LibrarySidebar sidebar, int workId)
    {
        var row = Row(sidebar, workId);
        Assert.Same(row, sidebar.Tree.SelectedItem);
        Assert.True(Assert.IsType<TreeViewItem>(sidebar.Tree.TreeContainerFromItem(row.Parent!)).IsExpanded);
    }

    /// <summary>Counts what would reload the reader, which a filter should never do.</summary>
    private static List<string> WatchSelections(LibrarySidebar sidebar)
    {
        var picked = new List<string>();
        sidebar.WorkSelected += work => picked.Add(work.Title);
        return picked;
    }

    private static async Task<(MainWindow Window, LibrarySidebar Sidebar)> OpenAsync(Library library, int workId)
    {
        var sidebar = await library.OpenAsync();
        Assert.True(await library.Window!.OpenWorkAsync(workId));
        await Screens.Settle();
        return (library.Window, sidebar);
    }

    [AvaloniaFact]
    public async Task Filtering_for_another_author_stands_with_a_work_open()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);

        await FilterForAsync(sidebar, "Homer");

        Assert.Equal("Homer", FilterBox(sidebar).Text);
        Assert.Equal(new[] { "Homer" }, AuthorNames(sidebar));
    }

    [AvaloniaFact]
    public async Task A_filter_that_finds_nobody_says_so_with_a_work_open()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);

        await FilterForAsync(sidebar, "quran");

        Assert.Equal("quran", FilterBox(sidebar).Text);
        var message = Assert.IsType<SidebarMessage>(Assert.Single(sidebar.Tree.ItemsSource!.Cast<object>()));
        Assert.Equal("No author matching “quran”", message.Text);
    }

    [AvaloniaFact]
    public async Task Filtering_for_the_open_works_author_keeps_it_selected_and_does_not_reopen_it()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);
        var picked = WatchSelections(sidebar);

        await FilterForAsync(sidebar, "Aesch");

        Assert.Equal("Aesch", FilterBox(sidebar).Text);
        Assert.Equal(new[] { "Aeschylus" }, AuthorNames(sidebar));
        AssertSelected(sidebar, Agamemnon);
        Assert.Empty(picked);
    }

    // The tree's own habit of keeping a selection that has left its items is
    // what this rests on, so a new Avalonia that dropped it would stop here.
    [AvaloniaFact]
    public async Task A_work_filtered_out_of_sight_is_selected_again_when_the_filter_clears()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);
        var picked = WatchSelections(sidebar);

        await FilterForAsync(sidebar, "Homer");
        Assert.Equal(Agamemnon, sidebar.SelectedWork?.WorkId);

        await FilterForAsync(sidebar, string.Empty);

        AssertSelected(sidebar, Agamemnon);
        Assert.Empty(picked);
    }

    [AvaloniaFact]
    public async Task Picking_a_work_under_a_filter_replaces_the_one_it_hid()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);
        var picked = WatchSelections(sidebar);

        await FilterForAsync(sidebar, "Homer");
        sidebar.Tree.SelectedItem = Row(sidebar, Iliad);
        await Screens.Settle();
        Assert.Equal(new[] { "Iliad" }, picked);

        await FilterForAsync(sidebar, string.Empty);

        AssertSelected(sidebar, Iliad);
        Assert.Equal(new[] { "Iliad" }, picked);
    }

    [AvaloniaFact]
    public async Task Favourites_only_stands_with_a_work_open_that_is_not_a_favourite()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);
        var picked = WatchSelections(sidebar);

        FavouritesOnly(sidebar).IsChecked = true;
        await RebuildAsync();

        Assert.True(FavouritesOnly(sidebar).IsChecked);
        Assert.Equal(new[] { "Homer" }, AuthorNames(sidebar));

        FavouritesOnly(sidebar).IsChecked = false;
        await RebuildAsync();

        AssertSelected(sidebar, Agamemnon);
        Assert.Empty(picked);
    }

    [AvaloniaFact]
    public async Task A_collection_filter_stands_with_a_work_open_outside_it()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Agamemnon);
        var picked = WatchSelections(sidebar);
        var latin = LatinCollection(sidebar);

        latin.IsChecked = true;
        await Screens.Settle();

        Assert.True(latin.IsChecked);
        Assert.Equal(new[] { "Caesar" }, AuthorNames(sidebar));

        latin.IsChecked = false;
        await Screens.Settle();

        AssertSelected(sidebar, Agamemnon);
        Assert.Empty(picked);
    }

    [AvaloniaFact]
    public async Task A_jump_to_a_work_still_clears_whichever_filter_hides_it()
    {
        await using var library = new Library();
        var (window, sidebar) = await OpenAsync(library, Agamemnon);

        await FilterForAsync(sidebar, "Homer");
        Assert.True(await window.OpenWorkAsync(Agamemnon));
        await RebuildAsync();
        Assert.Equal(string.Empty, FilterBox(sidebar).Text);
        AssertSelected(sidebar, Agamemnon);

        FavouritesOnly(sidebar).IsChecked = true;
        await RebuildAsync();
        Assert.True(await window.OpenWorkAsync(Agamemnon));
        await RebuildAsync();
        Assert.False(FavouritesOnly(sidebar).IsChecked);
        AssertSelected(sidebar, Agamemnon);

        var latin = LatinCollection(sidebar);
        latin.IsChecked = true;
        await Screens.Settle();
        Assert.True(await window.OpenWorkAsync(Agamemnon));
        await RebuildAsync();
        Assert.False(latin.IsChecked);
        AssertSelected(sidebar, Agamemnon);
    }

    [AvaloniaFact]
    public async Task Removing_the_open_work_from_favourites_under_favourites_only_turns_the_switch_off()
    {
        await using var library = new Library();
        var (_, sidebar) = await OpenAsync(library, Iliad);

        FavouritesOnly(sidebar).IsChecked = true;
        await RebuildAsync();
        Assert.True(FavouritesOnly(sidebar).IsChecked);

        await sidebar.ToggleFavoriteAsync(sidebar.FindWork(Iliad)!);
        await RebuildAsync();

        Assert.False(FavouritesOnly(sidebar).IsChecked);
        Assert.False(sidebar.IsFavorite(sidebar.FindWork(Iliad)!));
        Assert.Equal(new[] { "Aeschylus", "Caesar", "Homer" }, AuthorNames(sidebar));
        AssertSelected(sidebar, Iliad);
    }
}
