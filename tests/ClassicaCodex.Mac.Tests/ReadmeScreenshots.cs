using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Features.MythNetwork;
using ClassicaCodex.Mac.Features.Search;
using ClassicaCodex.Mac.Features.Concordance;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Features.Stylometry;
using ClassicaCodex.Mac.Features.Timeline;
using ClassicaCodex.Mac.Features.Translation;
using ClassicaCodex.Mac.Features.Vocabulary;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Microsoft.Data.Sqlite;
using Xunit;
using StylometryFeature = ClassicaCodex.Mac.Features.Stylometry.StylometryFeature;

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// The README's screenshots, taken against a full library. Off unless
/// CC_README_LIBRARY names a library and CC_SCREENSHOT_DIR a folder for the
/// pictures; nothing here checks anything a test needs. Each picture is
/// named as its file in docs/images:
///
///   CC_README_LIBRARY=%LOCALAPPDATA%/ClassicaCodex/classicacodex.db CC_SCREENSHOT_DIR=shots
///   dotnet test tests/ClassicaCodex.Mac.Tests --filter "FullyQualifiedName~ReadmeScreenshots"
///
/// The windows draw this machine's layout rather than the Mac look the other
/// tests use; a headless frame has no system title bar, so none is drawn.
///
/// The library is opened read-only, through a SQLite URI with mode=ro, and
/// a write is tried first to prove it: the reader's own library is never
/// written to, and never copied. Where a window would have written - Search
/// filing a query in its Recent list - the failure it reports is taken back
/// out of the status line before the picture, since it is the photographer's
/// doing and not the app's.
/// </summary>
public class ReadmeScreenshots
{
    private static string? Library
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("CC_README_LIBRARY");
            return path != null && File.Exists(path) && Screens.Directory != null ? Path.GetFullPath(path) : null;
        }
    }

    private const string AeneidUrn = "urn:cts:latinLit:phi0690.phi003";
    private const string OdysseyUrn = "urn:cts:greekLit:tlg0012.tlg002";

    [AvaloniaFact]
    public async Task Word_study_on_Aeneid_6_851_with_regere()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        var passage = await shoot.PassageAsync(AeneidUrn, "6.851");
        WordStudyFeature.Open(shoot.Main, passage, "regere");
        var study = await shoot.OwnedAsync<WordStudyWindow>(1220, 840);
        await study.WhenIdleAsync();

        var rego = study.Headwords.ToList().FindIndex(h => h.Headword == "rego");
        if (rego >= 0) study.HeadwordList.SelectedIndex = rego;
        await study.WhenIdleAsync();
        await study.ApplyScopeAsync(Array.Empty<int>());
        await study.WhenIdleAsync();

        await shoot.SaveAsync(study, "word-study");
    }

    [AvaloniaFact]
    public async Task Core_vocabulary_for_the_Aeneid()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        await VocabularyFeature.OpenAsync(shoot.Main, shoot.Work(AeneidUrn));
        var vocabulary = await shoot.OwnedAsync<VocabularyWindow>(1000, 760);
        await vocabulary.Building;

        await shoot.SaveAsync(vocabulary, "core-vocabulary");
    }

    [AvaloniaFact]
    public async Task Search_for_uirtus_in_Latin_one_row_per_document()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        shoot.Main.OpenTool("search");
        var search = await shoot.OwnedAsync<SearchWindow>(1180, 760);
        search.LatinCheck.IsChecked = true;
        search.QueryBox.Text = "uirtus";
        await search.RunSearchAsync();
        await Screens.Settle(4);
        search.SetView(1);
        await Screens.Settle(4);
        ForgiveRecent(search);

        await shoot.SaveAsync(search, "search");
    }

    [AvaloniaFact]
    public async Task Concordance_for_uirtus()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        shoot.Main.OpenTool("concordance");
        var concordance = await shoot.OwnedAsync<ConcordanceWindow>(1220, 760);
        concordance.WordBox.Text = "uirtus";
        await concordance.RunConcordanceAsync();
        await Screens.Settle(6);

        await shoot.SaveAsync(concordance, "concordance");
    }

    [AvaloniaFact]
    public async Task Echoes_of_Aeneid_6_851()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        var passage = await shoot.PassageAsync(AeneidUrn, "6.851");
        EchoFeature.FindEchoes(shoot.Main, passage);
        var echoes = await shoot.OwnedAsync<EchoResultsWindow>(1180, 760);
        await echoes.LoadTask!;
        await Screens.Settle(6);

        await shoot.SaveAsync(echoes, "intertextual-echoes");
    }

    [AvaloniaFact]
    public async Task Timeline_with_an_author_chosen()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        shoot.Main.OpenTool("timeline");
        var timeline = await shoot.OwnedAsync<TimelineWindow>(1300, 760);
        await timeline.PendingLoad;
        await Screens.Settle(4);

        var author = Environment.GetEnvironmentVariable("CC_README_TIMELINE_AUTHOR") ?? "Homer";
        var entry = timeline.Canvas.Entries.First(e => e.Name == author);
        var row = timeline.Canvas.TranslatePoint(timeline.Canvas.RowCentreOf(entry), timeline) ?? default;
        timeline.MouseMove(row);
        timeline.MouseDown(row, MouseButton.Left);
        timeline.MouseUp(row, MouseButton.Left);
        await Screens.Settle(2);
        await timeline.PendingLoad;
        await Screens.Settle(4);

        await shoot.SaveAsync(timeline, "timeline");
    }

    [AvaloniaFact]
    public async Task Stylometry_on_the_Rhesus()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        StylometryFeature.Open(shoot.Main);
        var stylometry = await shoot.OwnedAsync<StylometryWindow>(1180, 900);
        await stylometry.WhenIdleAsync();

        var rhesus = stylometry.Works.First(w => w.AuthorName == "Euripides" && w.WorkTitle == "Rhesus");
        stylometry.FilterBox.Text = "Euripides";
        await Screens.Settle(2);
        stylometry.Select(rhesus);
        await stylometry.AnalyzeAsync();
        await stylometry.WhenIdleAsync();
        await Screens.Settle(4);

        await shoot.SaveAsync(stylometry, "stylometry");
    }

    [AvaloniaFact]
    public async Task Translation_workbench_on_the_Odyssey()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        var odyssey = shoot.Work(OdysseyUrn);
        await TranslationFeature.OpenWorkbenchAsync(shoot.Main, odyssey);
        await Screens.Settle(6);
        var workbench = TranslationFeature.WorkbenchFor(odyssey.WorkId)!;
        workbench.Width = 1180;
        workbench.Height = 780;
        await Screens.Settle(6);

        // The crib - every word's headword and parse - rather than one word's
        // dictionary entry: the workbench prints LSJ's entries in their Beta
        // Code, undecoded, where Word Study decodes them.
        await workbench.BuildCribAsync();
        await Screens.Settle(10);

        await shoot.SaveAsync(workbench, "translation-workbench");

        // Nothing was typed, so closing has nothing to save; and the library
        // would refuse it if there were.
        workbench.TranslationBox.Text = string.Empty;
    }

    [AvaloniaFact]
    public async Task Guided_setup_on_a_loaded_step()
    {
        await using var shoot = await Shoot.StartAsync();
        if (shoot == null) return;

        var guided = new GuidedSetupWindow(shoot.Main);
        guided.Show(shoot.Main);
        await guided.OpeningRefresh;
        await guided.RefreshAllCompletionAsync();
        var title = Environment.GetEnvironmentVariable("CC_README_SETUP_STEP") ?? "Ancient Greek Texts";
        var step = guided.FirstSourceStepIndex + guided.Sources.ToList().FindIndex(s => s.Title.StartsWith(title, StringComparison.Ordinal));
        await guided.GoToStepAsync(step);
        await Screens.Settle(6);

        await shoot.SaveAsync(guided, "guided-setup");
    }

    [AvaloniaFact]
    public async Task Dark_mode_reading_the_Aeneid()
    {
        await using var shoot = await Shoot.StartAsync(width: 1200, height: 800);
        if (shoot == null) return;

        await shoot.PassageAsync(AeneidUrn, Environment.GetEnvironmentVariable("CC_README_DARK_LINE") ?? "1.1");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(10);

        await shoot.SaveAsync(shoot.Main, "dark-mode");
    }

    /// <summary>
    /// The myth network is drawn from the reader's tags, and the library has
    /// two. Auto-Tag's own search finds each name across the whole library,
    /// read-only; the lines it finds, and the real tags' lines, are written
    /// with their editions, works and authors into a scratch library made
    /// for the picture and deleted after it - what the network would show
    /// had the reader auto-tagged these names. Three stories, the Iliad's,
    /// the Odyssey's and the Aeneid's, so the graph has a shape: names that
    /// all co-occur with each other only draw a knot. The layout starts from
    /// random positions, so each run draws it differently.
    /// </summary>
    [AvaloniaFact]
    public async Task Myth_network_from_auto_tagged_names()
    {
        if (Library is not { } library) return;
        UseReadOnly(library);

        var names = (Environment.GetEnvironmentVariable("CC_README_NETWORK_NAMES")
                     ?? "Achilles:hero,Hector:hero,Odysseus/Ulysses:hero,Penelope:hero,Ithaca:place,Aeneas:hero,Dido:hero,Carthage/Carthago:place")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tagRepo = new TagRepository();
        var lemmas = new LemmaRepository();
        var nodes = new TextNodeRepository();
        var tags = new List<(string Name, string? Category, List<long> Ids)>();

        foreach (var tag in await tagRepo.GetAllTagsAsync())
            tags.Add((tag.Name, tag.Category, (await tagRepo.GetByTagAsync(tag.Name)).Select(p => p.TextNodeId).ToList()));

        foreach (var spec in names)
        {
            var (spelling, category) = spec.Split(':') is var parts ? (parts[0], parts.Length > 1 ? parts[1] : null) : default;
            var terms = spelling.Split('/');
            var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var term in terms)
                foreach (var form in await lemmas.ExpandFormAsync(term))
                    forms.Add(form);
            var hits = await nodes.SearchByFormsAsync(forms.ToList());
            tags.Add((terms[0], category, hits.Rows.Select(r => r.TextNodeId).ToList()));
        }

        var scratch = Path.Combine(Path.GetTempPath(), $"cc-readme-network-{Guid.NewGuid():N}.db");
        try
        {
            DbConnectionFactory.Configure(scratch, remember: false);
            await SchemaInitializer.EnsureSchemaAsync();
            await CopyLinesAsync(library, scratch, tags.SelectMany(t => t.Ids).Distinct().ToList());

            foreach (var (name, category, ids) in tags)
            {
                var (tagId, _) = await tagRepo.GetOrCreateAsync(name, category);
                await tagRepo.BulkTagTextNodesAsync(tagId, ids);
            }

            CategoryShapes.Set("hero", NodeShape.Hexagon);
            CategoryShapes.Set("place", NodeShape.Triangle);
            CategoryShapes.Set("god", NodeShape.Circle);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            WindowsLook();
            var main = new MainWindow { Width = 1360, Height = 860 };
            main.Show();
            await Screens.Settle(10);
            try
            {
                MythNetworkFeature.Open(main);
                await Screens.Settle(6);
                var network = main.OwnedWindows.OfType<MythNetworkWindow>().Last();
                network.Width = 1180;
                network.Height = 780;
                await Screens.Settle(12);

                // The layout pulls harder the more editions two tags share, and
                // on a whole library that is dozens, so at one shared edition
                // the graph draws as a knot. A quarter of the heaviest link
                // keeps the strong ones, and the layout is run again at full size.
                var min = int.TryParse(Environment.GetEnvironmentVariable("CC_README_NETWORK_MIN"), out var m) ? m : Math.Max(1, (int)network.ThresholdMaximum / 4);
                network.SetThreshold(min);
                await Screens.Settle(4);
                network.Canvas.Relayout();
                await Screens.Settle(8);

                var edge = Environment.GetEnvironmentVariable("CC_README_NETWORK_EDGE") ?? "Achilles|Hector";
                var ends = edge.Split('|');
                network.Canvas.ClickEdge(ends[0], ends[1]);
                await Screens.Settle(8);

                Screens.Save(network, "myth-network");
                network.Close();
            }
            finally
            {
                main.Close();
                await Screens.Settle(2);
            }
        }
        finally
        {
            MacLook();
            Screens.ForgetLibrary();
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { scratch, scratch + "-wal", scratch + "-shm" })
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// The lines, and the editions, works and authors above them, from the
    /// library (attached read-only) into the scratch one, column by column
    /// where both have the column, since a migrated table and a fresh one
    /// need not list them in the same order.
    /// </summary>
    private static async Task CopyLinesAsync(string library, string scratch, IReadOnlyList<long> ids)
    {
        // A URI for the scratch file too: SQLite reads the attached library's
        // URI, and so its mode=ro, only on a connection opened with one.
        await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = "file:" + scratch.Replace('\\', '/') }.ConnectionString);
        await conn.OpenAsync();

        async Task Run(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 600;
            await cmd.ExecuteNonQueryAsync();
        }

        async Task<List<string>> Columns(string schema, string table)
        {
            var columns = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA {schema}.table_info({table});";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            return columns;
        }

        await Run($"ATTACH DATABASE '{ReadOnlyUri(library)}' AS src;");
        await Run("CREATE TEMP TABLE wanted (TextNodeId INTEGER PRIMARY KEY);");
        foreach (var chunk in ids.Chunk(500))
            await Run($"INSERT OR IGNORE INTO temp.wanted VALUES {string.Join(",", chunk.Select(id => $"({id})"))};");

        await Run("PRAGMA foreign_keys = OFF;");
        var wanted = new Dictionary<string, string>
        {
            ["Authors"] = "AuthorId IN (SELECT w.AuthorId FROM src.Works w WHERE w.WorkId IN (SELECT e.WorkId FROM src.Editions e WHERE e.EditionId IN (SELECT tn.EditionId FROM src.TextNodes tn WHERE tn.TextNodeId IN (SELECT TextNodeId FROM temp.wanted))))",
            ["Works"] = "WorkId IN (SELECT e.WorkId FROM src.Editions e WHERE e.EditionId IN (SELECT tn.EditionId FROM src.TextNodes tn WHERE tn.TextNodeId IN (SELECT TextNodeId FROM temp.wanted)))",
            ["Editions"] = "EditionId IN (SELECT tn.EditionId FROM src.TextNodes tn WHERE tn.TextNodeId IN (SELECT TextNodeId FROM temp.wanted))",
            ["TextNodes"] = "TextNodeId IN (SELECT TextNodeId FROM temp.wanted)",
        };

        foreach (var (table, where) in wanted)
        {
            var source = await Columns("src", table);
            var shared = (await Columns("main", table)).Where(source.Contains).ToList();
            var list = string.Join(", ", shared.Select(c => $"\"{c}\""));
            await Run($"INSERT OR IGNORE INTO main.{table} ({list}) SELECT {list} FROM src.{table} WHERE {where};");
        }

        await Run("DETACH DATABASE src;");
    }

    /// <summary>
    /// The layout this machine's app draws, not the Mac's: TestApp turns the
    /// Mac look on for every test, and on Windows that would put a Mac
    /// toolbar and Command-key hints into a Windows README. Without it the
    /// main window draws its own menu bar, as on Windows; only the system's
    /// title bar, which a headless frame has none of, is missing.
    /// </summary>
    private static void WindowsLook() => Environment.SetEnvironmentVariable("CC_MAC_LOOK", null);

    private static void MacLook() => Environment.SetEnvironmentVariable("CC_MAC_LOOK", "1");

    private static string ReadOnlyUri(string path) => "file:" + path.Replace('\\', '/') + "?mode=ro";

    /// <summary>
    /// Points the connection factory at the library through a read-only URI,
    /// and proves it by trying a write that would change nothing even if it
    /// were allowed. Stops the test if the write goes through.
    /// </summary>
    private static void UseReadOnly(string library)
    {
        typeof(DbConnectionFactory)
            .GetField("_databasePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, ReadOnlyUri(library));

        using var conn = DbConnectionFactory.OpenConnectionAsync().GetAwaiter().GetResult();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Tags WHERE 0;";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8)
        {
            return;
        }

        Screens.ForgetLibrary();
        throw new InvalidOperationException("The library opened writable; stopping before anything else touches it.");
    }

    /// <summary>Search's note that it could not file the query in Recent, which only the read-only library caused.</summary>
    private static void ForgiveRecent(SearchWindow search)
    {
        var label = (TextBlock)typeof(SearchWindow)
            .GetField("_statusLabel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(search)!;
        var text = label.Text ?? string.Empty;
        var at = text.IndexOf("  (couldn't add to Recent:", StringComparison.Ordinal);
        if (at >= 0) label.Text = text[..at];
    }

    /// <summary>A main window over the read-only library, closed with everything it owns.</summary>
    private sealed class Shoot : IAsyncDisposable
    {
        private Shoot(MainWindow main) => Main = main;

        public MainWindow Main { get; }

        public static async Task<Shoot?> StartAsync(double width = 1360, double height = 860)
        {
            if (Library is not { } library) return null;
            UseReadOnly(library);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            WindowsLook();
            var main = new MainWindow { Width = width, Height = height };
            main.Show();
            await Screens.Settle(12);
            return new Shoot(main);
        }

        public Work Work(string ctsUrn) => Main.LibraryWorks.First(w => w.CtsUrn == ctsUrn);

        /// <summary>A line as the reader's line menu would hand it to a tool, with the work open on it.</summary>
        public async Task<PassageContext> PassageAsync(string workUrn, string reference)
        {
            var work = Work(workUrn);
            var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
            var edition = editions.First(e => e.Kind == EditionKind.Original);
            var counterpart = editions.FirstOrDefault(e => e.Kind == EditionKind.Translation && !e.CtsUrn.Contains(".mine-", StringComparison.Ordinal));
            var node = (await new TextNodeRepository().GetByEditionAsync(edition.EditionId))
                .First(n => PassageCitation.Display(n.CitationRef, n.Milestone) == reference);

            await Main.NavigateToPassageAsync(work.WorkId, node.TextNodeId);
            await Screens.Settle(10);

            var font = Main.GetVisualDescendants().OfType<ReaderPane>().FirstOrDefault()?.List.FontFamily.Name ?? "Palatino";
            return new PassageContext(
                node, edition, counterpart, FromOriginal: true, font,
                EditionLabels.Descriptor(edition), counterpart == null ? null : EditionLabels.Descriptor(counterpart));
        }

        public async Task<T> OwnedAsync<T>(double width, double height) where T : Window
        {
            await Screens.Settle(8);
            var window = Main.OwnedWindows.OfType<T>().Last();
            window.Width = width;
            window.Height = height;
            await Screens.Settle(8);
            return window;
        }

        public async Task SaveAsync(Window window, string name)
        {
            await Screens.Settle(8);
            Screens.Save(window, name);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var owned in Main.OwnedWindows.ToList()) owned.Close();
            Main.Close();
            await Screens.Settle(2);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            MacLook();
            Screens.ForgetLibrary();
            SqliteConnection.ClearAllPools();
        }
    }
}
