using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.Bookmarks;
using ClassicaCodex.Mac.Features.MythNetwork;
using ClassicaCodex.Mac.Features.Tags;
using ClassicaCodex.Mac.Features.WorkInfo;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// These tests write to a library, and the connection factory is static, so
/// they run on their own after everything that runs in parallel - a test
/// elsewhere pointing the factory at another file halfway through one of
/// these would otherwise tag the wrong library.
/// </summary>
[CollectionDefinition(nameof(TagsAndNetworkCollection), DisableParallelization = true)]
public sealed class TagsAndNetworkCollection : ICollectionFixture<TaggedLibrary>
{
}

/// <summary>
/// A copy of the sample library with tags and categories on lines of the
/// Iliad, the Odyssey and the Aeneid, so the browser, the network and the
/// comparisons have something real to show. Copied once for the collection
/// and deleted afterwards; the sample itself is never written to.
/// </summary>
public sealed class TaggedLibrary : IDisposable
{
    private bool _seeded;

    public TaggedLibrary()
    {
        if (Screens.SampleLibrary is not { } sample) return;

        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cc-mac-tags-{Guid.NewGuid():N}.db");
        File.Copy(sample, Path);

        // The sample may be read-only, and a copy keeps its permissions.
        File.SetAttributes(Path, FileAttributes.Normal);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public string? Path { get; }

    /// <summary>Points the factory at the copy - every test, since another may have moved it - and seeds it once.</summary>
    public async Task<bool> UseAsync()
    {
        if (Path == null) return false;

        DbConnectionFactory.Configure(Path, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        if (!_seeded)
        {
            await SeedAsync();
            _seeded = true;
        }

        return true;
    }

    private const string Iliad = "tlg0012.tlg001.perseus-eng3";
    private const string Odyssey = "tlg0012.tlg002.perseus-eng3";
    private const string Aeneid = "phi0690.phi003.perseus-eng2";

    private static async Task SeedAsync()
    {
        var tags = new TagRepository();

        async Task Tag(string name, string? category, params (string Edition, string Word, int Count)[] places)
        {
            var (tagId, _) = await tags.GetOrCreateAsync(name, category);
            var ids = new List<long>();
            foreach (var (edition, word, count) in places) ids.AddRange(await NodesAsync(edition, word, count));
            await tags.BulkTagTextNodesAsync(tagId, ids);
        }

        await Tag("Achilles", "hero", (Iliad, "Achilles", 14), (Aeneid, "Achilles", 4));
        await Tag("Hector", "hero", (Iliad, "Hector", 14), (Aeneid, "Hector", 3));
        await Tag("Troy", "place", (Iliad, "Troy", 8), (Odyssey, "Troy", 4), (Aeneid, "Troy", 6));
        await Tag("Zeus", "god", (Iliad, "Zeus", 10), (Odyssey, "Zeus", 5));
        await Tag("Athena", "god", (Odyssey, "Athena", 8), (Iliad, "Athena", 1));
        await Tag("Agamemnon", "hero", (Iliad, "Agamemnon", 8));
        await Tag("Priam", null, (Iliad, "Priam", 6), (Aeneid, "Priam", 3));
        await Tag("Odysseus", "hero", (Odyssey, "Odysseus", 10), (Iliad, "Odysseus", 3));
        await Tag("Aeneas", "hero", (Aeneid, "Aeneas", 10), (Iliad, "Aeneas", 2));

        CategoryShapes.Set("hero", NodeShape.Hexagon);
        CategoryShapes.Set("place", NodeShape.Triangle);
        CategoryShapes.Set("god", NodeShape.Circle);
    }

    /// <summary>The first lines of an edition that mention a word, in reading order.</summary>
    public static async Task<List<long>> NodesAsync(string editionUrn, string word, int count)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT tn.TextNodeId FROM TextNodes tn JOIN Editions e ON e.EditionId = tn.EditionId
            WHERE e.CtsUrn = @Urn AND tn.Text LIKE @Like
            ORDER BY tn.SortOrder LIMIT @Count;";
        cmd.Parameters.AddWithValue("@Urn", editionUrn);
        cmd.Parameters.AddWithValue("@Like", $"%{word}%");
        cmd.Parameters.AddWithValue("@Count", count);

        var ids = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
        return ids;
    }

    /// <summary>A line as the reader would hand it to a tool.</summary>
    public static async Task<TextNode> NodeAsync(long textNodeId)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text, Milestone FROM TextNodes WHERE TextNodeId = @Id;";
        cmd.Parameters.AddWithValue("@Id", textNodeId);

        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TextNode
        {
            TextNodeId = reader.GetInt64(0),
            EditionId = reader.GetInt32(1),
            CitationRef = reader.GetString(2),
            SortOrder = reader.GetInt32(3),
            Text = reader.GetString(4),
            Milestone = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    public void Dispose()
    {
        if (Path == null) return;

        // The pool may still hold the file open; let it go before deleting.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { Path, Path + "-wal", Path + "-shm" })
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}

[Collection(nameof(TagsAndNetworkCollection))]
public class TagsAndNetworkTests
{
    private readonly TaggedLibrary _library;

    public TagsAndNetworkTests(TaggedLibrary library) => _library = library;

    private static async Task<MainWindow> OpenReaderAsync(string? workTitle = "Iliad")
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(10);

        if (workTitle != null)
        {
            var work = window.LibraryWorks.First(w => w.Title == workTitle);
            Assert.True(await window.OpenWorkAsync(work.WorkId));
            await Screens.Settle(6);
        }

        return window;
    }

    /// <summary>Waits for a window the code under test opened, owned by <paramref name="owner"/>.</summary>
    private static async Task<T> OwnedAsync<T>(Window owner, Func<T, bool>? match = null) where T : Window
    {
        for (var i = 0; i < 60; i++)
        {
            var found = owner.OwnedWindows.OfType<T>().FirstOrDefault(w => match == null || match(w));
            if (found != null)
            {
                await Screens.Settle(4);
                return found;
            }

            await Screens.Settle(1);
        }

        throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} opened.");
    }

    /// <summary>Waits for an alert (they have no title) and presses its default button.</summary>
    private static async Task<string> AcceptAlertAsync(Window owner, string? screenshot = null)
    {
        var alert = await OwnedAsync<Window>(owner, w => string.IsNullOrEmpty(w.Title) && w.IsVisible);
        var text = string.Join(" | ", alert.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        if (screenshot != null) Screens.Save(alert, screenshot);
        Press(alert.GetVisualDescendants().OfType<Button>().Last());
        await Screens.Settle(2);
        return text;
    }

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonWith(Window window, string text) =>
        window.GetVisualDescendants().OfType<Button>().First(b =>
            b.Content as string == text ||
            b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    private static async Task ShootBothAsync(Window window, string name)
    {
        Screens.Save(window, name + "-light");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(4);
        Screens.Save(window, name + "-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await Screens.Settle(2);
    }

    [AvaloniaFact]
    public async Task The_tag_browser_lists_every_tag_and_the_passages_behind_one()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync();

        TagsFeature.OpenBrowser(main);
        var browser = await OwnedAsync<TagBrowserWindow>(main);

        Assert.Contains(browser.Tags, t => t.Name == "Achilles" && t.Category == "hero");
        Assert.Contains(browser.Tags, t => t.Name == "Priam" && t.Category == null);

        // A second request brings the open browser forward rather than opening another.
        TagsFeature.OpenBrowser(main);
        await Screens.Settle(2);
        Assert.Single(main.OwnedWindows.OfType<TagBrowserWindow>());

        browser.SelectTag("Achilles");
        await Screens.Settle(6);

        var expected = (await new TagRepository().GetByTagAsync("Achilles")).Count;
        Assert.True(expected >= 14);
        Assert.Equal(expected, browser.Results.Count);
        Assert.Contains(browser.Results, r => r.WorkTitle == "Aeneid");

        await ShootBothAsync(browser, "tags-browser");

        // Compare Sources: Troy is tagged in all three epics.
        browser.SelectTag("Troy");
        await Screens.Settle(4);
        await browser.CompareSelectedAsync();
        var compare = await OwnedAsync<CompareSourcesWindow>(browser);
        Assert.Equal(3, compare.SourceNames.Count);
        Assert.Contains("Homer, Iliad", compare.SourceNames);

        for (var i = 0; i < compare.SourceNames.Count; i++) compare.Check(i);
        await compare.RenderColumnsAsync();
        await Screens.Settle(6);
        Assert.Equal(3, compare.ColumnCount);
        await ShootBothAsync(compare, "tags-compare-sources");
        compare.Close();

        browser.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Tagging_a_line_from_the_reader_creates_the_tag_and_says_so()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync();

        var nodeId = (await TaggedLibrary.NodesAsync("tlg0012.tlg001.perseus-grc2", "Ἀχιλ", 1)).Single();
        var node = await TaggedLibrary.NodeAsync(nodeId);
        var passage = new PassageContext(node, null, null, true, "Palatino", null, null);

        var tagging = TagsFeature.TagPassageAsync(main, passage);
        var prompt = await OwnedAsync<Window>(main, w => w.Title == "Tag This Line");

        var boxes = prompt.GetVisualDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "Wrath";
        boxes[1].Text = "theme";
        await Screens.Settle(2);
        Screens.Save(prompt, "tags-prompt");

        Press(ButtonWith(prompt, "Tag It"));
        var message = await AcceptAlertAsync(main, "tags-tagged-alert");
        Assert.Contains("Tagged [1.1] with “Wrath”", message);

        Assert.True(await tagging);

        var tagged = await new TagRepository().GetByTagAsync("Wrath");
        Assert.Contains(tagged, t => t.TextNodeId == nodeId);

        // Tagging the same line again under another category keeps the first,
        // and the confirmation says so rather than leaving the box to look as
        // if it took.
        var again = TagsFeature.TagPassageAsync(main, passage);
        prompt = await OwnedAsync<Window>(main, w => w.Title == "Tag This Line" && w.IsVisible);
        boxes = prompt.GetVisualDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "Wrath";
        boxes[1].Text = "god";
        Press(ButtonWith(prompt, "Tag It"));
        message = await AcceptAlertAsync(main);
        Assert.Contains("kept that rather than taking “god”", message);
        Assert.True(await again);

        main.Close();
    }

    [AvaloniaFact]
    public async Task Bookmarks_are_saved_with_their_note_and_listed()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync();

        var ids = await TaggedLibrary.NodesAsync("tlg0012.tlg001.perseus-eng3", "Patroclus", 2);

        foreach (var (id, note) in new[] { (ids[0], "Patroclus first asks for the armour - compare 16.40."), (ids[1], (string?)null) })
        {
            var node = await TaggedLibrary.NodeAsync(id);
            var bookmarking = BookmarksFeature.BookmarkPassageAsync(main, new PassageContext(node, null, null, false, "Georgia", null, null));
            var prompt = await OwnedAsync<Window>(main, w => w.Title == "Bookmark This Line" && w.IsVisible);
            if (note != null)
            {
                prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = note;
                await Screens.Settle(2);
                Screens.Save(prompt, "bookmarks-prompt");
            }

            Press(ButtonWith(prompt, "Bookmark It"));
            var message = await AcceptAlertAsync(main);
            Assert.StartsWith("Bookmarked", message);
            Assert.True(await bookmarking);
        }

        BookmarksFeature.Open(main);
        var window = await OwnedAsync<BookmarksWindow>(main);
        await Screens.Settle(4);

        Assert.True(window.Listed.Count >= 2);
        Assert.Contains(window.Listed, b => b.Note?.StartsWith("Patroclus first") == true);
        Assert.Equal(string.Empty, window.DormantNotice);

        await ShootBothAsync(window, "bookmarks");
        window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task The_myth_network_draws_the_tags_and_explains_its_nodes_and_edges()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync();

        MythNetworkFeature.Open(main);
        var network = await OwnedAsync<MythNetworkWindow>(main);
        await Screens.Settle(8);

        Assert.True(network.Canvas.NodeCount >= 9);
        Assert.Contains("Achilles", network.Canvas.NodeNames);
        Assert.True(network.Canvas.VisibleEdgeCount > 0);

        // Every node inside the canvas, after the layout settled and the
        // window gave the canvas its real size.
        foreach (var name in network.Canvas.NodeNames)
        {
            var p = network.Canvas.PositionOf(name)!.Value;
            Assert.InRange(p.X, 0, Math.Max(network.Canvas.Bounds.Width, 400));
            Assert.InRange(p.Y, 0, Math.Max(network.Canvas.Bounds.Height, 400));
        }

        await ShootBothAsync(network, "myth-network");

        network.Canvas.ClickNode("Achilles");
        await Screens.Settle(6);
        Assert.Equal((await new TagRepository().GetByTagAsync("Achilles")).Count, network.Passages.Count);
        Assert.StartsWith("“Achilles”", network.SelectedLabel);
        Screens.Save(network, "myth-network-node");

        network.Canvas.ClickEdge("Achilles", "Hector");
        await Screens.Settle(6);
        Assert.NotEmpty(network.Passages);
        Assert.All(network.Passages, p => Assert.Contains(p.TagName, new[] { "Achilles", "Hector" }));
        Screens.Save(network, "myth-network-edge");

        // Whole-work co-occurrence links more, and the slider's range follows
        // the heaviest edge so it can reach the real signal.
        network.SetMode(proximity: false);
        await Screens.Settle(6);
        Assert.Equal(network.Canvas.MaxEdgeWeight, (int)network.ThresholdMaximum);
        var allEdges = network.Canvas.VisibleEdgeCount;
        network.SetThreshold(2);
        await Screens.Settle(4);
        Assert.Equal(2, network.Threshold);
        Assert.True(network.Canvas.VisibleEdgeCount <= allEdges);

        // Back to proximity, whose heaviest edge is lighter: the canvas must
        // filter at what the slider now says, not at what it said before.
        network.SetThreshold((int)network.ThresholdMaximum);
        network.SetMode(proximity: true);
        await Screens.Settle(6);
        Assert.True(network.Threshold <= network.ThresholdMaximum);
        Assert.True(network.Canvas.VisibleEdgeCount > 0);

        network.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Auto_tag_finds_a_name_across_the_corpus_and_tags_what_is_checked()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync();

        var autoTag = new AutoTagWindow { OnNavigate = main.NavigateToPassageAsync, Width = 1100, Height = 720 };
        autoTag.Show(main);
        await Screens.Settle(4);

        autoTag.SetQuery("Patroclus", "hero", "Patroklos");
        await autoTag.RunSearchAsync();
        await Screens.Settle(6);

        Assert.NotEmpty(autoTag.Matches);
        Assert.All(autoTag.Matches, m => Assert.True(m.IsChecked));
        Assert.All(autoTag.Matches, m => Assert.NotEmpty(m.Highlight));
        Assert.Contains("all checked by default", autoTag.Status);

        // A long passage shows the stretch around the match, not its opening,
        // and what the row sets off is one of the forms searched for - Murray
        // writes Patroclus, Butler Patroklos.
        Assert.All(autoTag.Matches, m =>
        {
            var (start, length) = m.Highlight[0];
            Assert.Contains(m.Shown.Substring(start, length), new[] { "Patroclus", "Patroklos" }, StringComparer.OrdinalIgnoreCase);
        });
        Assert.Contains(autoTag.Matches, m => m.Shown.StartsWith('…'));

        autoTag.Matches[1].IsChecked = false;
        await Screens.Settle(2);
        await ShootBothAsync(autoTag, "auto-tag");

        var ids = autoTag.Matches.Where(m => m.IsChecked).Select(m => m.Passage.TextNodeId).ToList();
        var changed = false;
        autoTag.TagsChanged += () => changed = true;
        var tagged = await autoTag.TagCoreAsync("Patroclus", ids, announce: false);

        Assert.True(changed);
        Assert.True(tagged > 0);
        var stored = await new TagRepository().GetByTagAsync("Patroclus");
        Assert.DoesNotContain(stored, s => s.TextNodeId == autoTag.Matches[1].Passage.TextNodeId);
        Assert.Contains("Tagged", autoTag.Status);

        autoTag.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Category_shapes_offers_each_category_in_use()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync(workTitle: null);

        var changes = 0;
        var showing = CategoryShapesWindow.ShowAsync(main, () => changes++);
        var dialog = await OwnedAsync<Window>(main, w => w.Title == "Category Shapes");

        var combos = dialog.GetVisualDescendants().OfType<ComboBox>().ToList();
        Assert.True(combos.Count >= 3); // god, hero, place - and theme, if the tagging test ran first
        Assert.Equal(NodeShape.Hexagon, combos.Select(c => c.SelectedItem).OfType<NodeShape>().ElementAt(1));

        Screens.Save(dialog, "category-shapes-light");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(4);
        Screens.Save(dialog, "category-shapes-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        combos[0].SelectedItem = NodeShape.Star;
        await Screens.Settle(2);
        Assert.Equal(1, changes);
        Assert.Equal(NodeShape.Star, CategoryShapes.For("god"));
        CategoryShapes.Set("god", NodeShape.Circle);

        dialog.Close();
        await showing;
        main.Close();
    }

    [AvaloniaFact]
    public async Task An_empty_network_says_how_to_start_one()
    {
        var canvas = new GraphCanvas();
        var window = new Window { Width = 640, Height = 360, Content = new Border { Classes = { "page" }, Margin = new Thickness(16), Child = canvas } };
        window.Show();
        canvas.SetData(new(), new());
        await Screens.Settle(4);

        Assert.Equal(0, canvas.NodeCount);
        Assert.Equal(1, canvas.MaxEdgeWeight);
        Screens.Save(window, "myth-network-empty");
        window.Close();
    }

    [Fact]
    public void Dragging_a_node_survives_a_canvas_too_small_to_hold_it()
    {
        // Narrower than the node is wide: it sits in the middle of what there
        // is, where two Math.Clamp calls once threw.
        Assert.Equal(new Point(20, 15), GraphCanvas.KeepInside(new Point(100, -40), 30, 40, 30));
        Assert.Equal(new Point(30, 170), GraphCanvas.KeepInside(new Point(-5, 999), 30, 400, 200));
    }

    [Fact]
    public void A_category_keeps_its_colour_whatever_its_case()
    {
        var page = Avalonia.Media.Color.Parse("#FBF8F1");
        var hues = new[] { Avalonia.Media.Color.Parse("#3F7F74"), Avalonia.Media.Color.Parse("#2F4A7A"), Avalonia.Media.Color.Parse("#A0612B") };
        var neutral = Avalonia.Media.Color.Parse("#9A8E7A");

        Assert.Equal(GraphCanvas.ColorForCategory("hero", page, hues, neutral), GraphCanvas.ColorForCategory(" Hero ", page, hues, neutral));
        Assert.NotEqual(GraphCanvas.ColorForCategory("hero", page, hues, neutral), GraphCanvas.ColorForCategory(null, page, hues, neutral));
    }

    [Fact]
    public void A_result_row_keeps_the_match_in_view_and_never_splits_a_character()
    {
        var text = new string('a', 1000) + "Patroclus" + new string('b', 1000);
        var shown = PassageResults.RowTextAround(text, 1000, 9);
        Assert.StartsWith("…", shown);
        Assert.EndsWith("…", shown);
        Assert.Contains("Patroclus", shown);

        // A cut that would land between the halves of a surrogate pair moves back.
        var cuneiform = new string('x', 399) + "\U00012000" + "tail";
        Assert.Equal(new string('x', 399) + "…", PassageResults.RowText(cuneiform));
    }

    [AvaloniaFact]
    public async Task Work_details_report_the_editions_and_the_attribution_can_be_changed_and_handed_back()
    {
        if (!await _library.UseAsync()) return;
        var main = await OpenReaderAsync();

        var iliad = main.LibraryWorks.First(w => w.Title == "Iliad");
        WorkInfoFeature.ShowDetails(main, iliad);
        var details = await OwnedAsync<WorkDetailsWindow>(main);
        for (var i = 0; i < 40 && details.Report.StartsWith("Loading", StringComparison.Ordinal); i++) await Screens.Settle(1);

        Assert.Contains("EDITION - original language", details.Report);
        Assert.Contains("Augustus Taber Murray", details.Report);
        Assert.Contains("Publication details:", details.Report);
        Assert.StartsWith("3 edition(s)", details.Status);
        await ShootBothAsync(details, "work-details");
        details.Close();

        // A work the catalog has a view on: the sheet says where the value came from.
        var alcibiades = main.LibraryWorks.First(w => w.Title == "Alcibiades 1");
        var editing = WorkInfoFeature.EditAttributionAsync(main, alcibiades);
        var sheet = await OwnedAsync<AttributionWindow>(main);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("From the library's catalog:") == true);
        Assert.False(ButtonWith(sheet, "Use the library's answer").IsEnabled);
        await ShootBothAsync(sheet, "attribution");

        sheet.Choose(AttributionStatus.Spurious, "Unlike the dialogues of any period.");
        Assert.True(await editing);
        Assert.Equal(AttributionStatus.Spurious, alcibiades.AttributionStatus);
        Assert.True(alcibiades.AttributionSetByUser);

        // The reader's note now shows in Work Details, as the sheet promises.
        var (report, _) = await WorkDetailsWindow.BuildReportAsync(alcibiades);
        Assert.Contains("not by this author (your judgement)", report);
        Assert.Contains("Unlike the dialogues of any period.", report);

        // And handed back to the catalog.
        editing = WorkInfoFeature.EditAttributionAsync(main, alcibiades);
        sheet = await OwnedAsync<AttributionWindow>(main, w => w.IsVisible);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.StartsWith("You set this.") == true);
        Press(ButtonWith(sheet, "Use the library's answer"));
        Assert.True(await editing);
        Assert.False(alcibiades.AttributionSetByUser);
        Assert.Equal(AttributionStatus.Disputed, alcibiades.AttributionStatus);

        // Cancelling changes nothing and says so.
        editing = WorkInfoFeature.EditAttributionAsync(main, iliad);
        sheet = await OwnedAsync<AttributionWindow>(main, w => w.IsVisible);
        Press(ButtonWith(sheet, "Cancel"));
        Assert.False(await editing);

        main.Close();
    }
}
