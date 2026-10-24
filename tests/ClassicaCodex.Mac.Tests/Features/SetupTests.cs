using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Setup points the one static library connection at file after file, so
/// these run on their own rather than beside tests reading the sample library.
/// </summary>
[CollectionDefinition("Setup", DisableParallelization = true)]
public sealed class SetupCollection
{
}

[Collection("Setup")]
public class SetupTests
{
    private const string SparseCloneRoot =
        "/tmp/claude-0/-home-user-ClassicaCodex/1abf1c29-957a-5f3b-874f-f652d21d2070/scratchpad/corpus";

    [AvaloniaFact]
    public async Task Guided_steps_come_from_the_catalogue_including_the_newest_collections()
    {
        using var scratch = new Scratch();
        await scratch.FreshLibraryAsync();

        var main = await scratch.MainAsync();
        var guided = new GuidedSetupWindow(main);
        guided.Show(main);
        await Screens.Settle();

        var catalogue = SetupDataSourceCatalog.Build(
            new AuthorRepository(), new LemmaRepository(), new DefinitionRepository(), new ArtifactRepository(), new EditionRepository());

        // Every source, in the catalogue's order, and nothing else - the
        // window presents the catalogue rather than a list of its own.
        Assert.Equal(catalogue.Select(s => s.Title), guided.Sources.Select(s => s.Title));
        Assert.Equal(catalogue.Count + 5, guided.TotalSteps);
        Assert.Equal(guided.FirstSourceStepIndex + catalogue.Count, guided.WordIndexStepIndex);

        // The angels-and-demons collections, the most recent the catalogue
        // has gained, are steps here without anything on the Mac naming them.
        foreach (var newest in new[] { "The Goetia", "Pseudo-Dionysius", "Aquinas", "The Quran", "Rabbinic texts", "Pseudepigrapha", "Early English Books", "The Hebrew Bible" })
            Assert.Contains(guided.Sources, s => s.Title.StartsWith(newest, StringComparison.Ordinal));

        Screens.Save(guided, "setup-guided-01-welcome");

        await guided.GoToStepAsync(GuidedSetupWindow.DatabaseStepIndex);
        Assert.Equal(GuidedSetupWindow.DatabaseStepIndex, guided.CurrentStep);
        Assert.Equal(StepState.Complete, guided.StateOfStep(GuidedSetupWindow.DatabaseStepIndex));
        Assert.StartsWith("Ready:", guided.StatusText);
        Screens.Save(guided, "setup-guided-02-library-file");

        // The Menota step, which is the one with links, a readiness check, a
        // folder on screen and a second button.
        var menota = guided.Sources.ToList().FindIndex(s => MenotaReview.IsReviewStep(s));
        Assert.True(menota >= 0);
        await guided.GoToStepAsync(guided.FirstSourceStepIndex + menota);
        Assert.Equal("Open Folder", guided.ActionText);
        Screens.Save(guided, "setup-guided-04-menota");

        await guided.GoToStepAsync(guided.FirstSourceStepIndex + guided.Sources.ToList().FindIndex(s => s.Title.StartsWith("Patrologia", StringComparison.Ordinal)));
        Screens.Save(guided, "setup-guided-05-longest-description");

        await guided.GoToStepAsync(guided.FinishStepIndex);
        Screens.Save(guided, "setup-guided-07-finish");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await guided.GoToStepAsync(guided.FirstSourceStepIndex + menota);
        await Screens.Settle(3);
        Screens.Save(guided, "setup-guided-04-menota-dark");
        await guided.GoToStepAsync(0);
        await Screens.Settle(3);
        Screens.Save(guided, "setup-guided-01-welcome-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        guided.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Readiness_reflects_an_empty_library_and_the_sample_one()
    {
        using var scratch = new Scratch();

        // An empty library: every collection still to do, nothing partly
        // done, and the index not built - rings, not crosses.
        await scratch.FreshLibraryAsync();
        var main = await scratch.MainAsync();
        var guided = new GuidedSetupWindow(main);
        guided.Show(main);
        await guided.RefreshAllCompletionAsync();
        await guided.GoToStepAsync(guided.FirstSourceStepIndex);

        for (var i = 0; i < guided.Sources.Count; i++)
            Assert.Equal(StepState.Pending, guided.StateOfStep(guided.FirstSourceStepIndex + i));
        Assert.Equal("Not loaded yet.", guided.StatusText);
        Assert.Equal("Download & Install", guided.ActionText);

        await guided.GoToStepAsync(guided.WordIndexStepIndex);
        Assert.Equal(StepState.Pending, guided.StateOfStep(guided.WordIndexStepIndex));
        Assert.Equal("Not built yet.", guided.StatusText);
        Assert.Equal("Build Word Index", guided.ActionText);
        Screens.Save(guided, "setup-guided-06-word-index-empty");
        guided.Close();
        main.Close();

        if (scratch.SampleCopy() is not { } sample) return;

        // The sample library has both dictionaries, Greek and Latin lemma data
        // and a finished word index - and texts that were ingested without a
        // collection stamp, which is what an install that predates the stamp
        // looks like, so those steps rightly still offer to install.
        DbConnectionFactory.Configure(sample, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();
        main = await scratch.MainAsync();
        guided = new GuidedSetupWindow(main);
        guided.Show(main);
        await guided.RefreshAllCompletionAsync();
        await guided.GoToStepAsync(guided.CurrentStep);

        int Step(string title) => guided.FirstSourceStepIndex + guided.Sources.ToList().FindIndex(s => s.Title.StartsWith(title, StringComparison.Ordinal));

        Assert.Equal(StepState.Complete, guided.StateOfStep(Step("Dictionaries")));
        Assert.Equal(StepState.Complete, guided.StateOfStep(Step("Greek Lemma Data")));
        Assert.Equal(StepState.Complete, guided.StateOfStep(Step("Latin Lemma Data")));
        // The sample's Perseus texts are a sparse clone, but the tool that
        // builds it stamps their collection as setup does once an ingest has
        // finished - so setup counts the step done, as it would for any
        // library whose Greek ingest completed. A collection never installed
        // is still waiting.
        Assert.Equal(StepState.Complete, guided.StateOfStep(Step("Ancient Greek Texts")));
        Assert.Equal(StepState.Pending, guided.StateOfStep(Step("The Goetia")));

        await guided.GoToStepAsync(Step("Dictionaries"));
        Assert.Equal("Already loaded.", guided.StatusText);
        Assert.Equal("Re-download & Re-install", guided.ActionText);
        Screens.Save(guided, "setup-guided-03-source-loaded");

        await guided.GoToStepAsync(guided.WordIndexStepIndex);
        Assert.Equal(StepState.Complete, guided.StateOfStep(guided.WordIndexStepIndex));
        Assert.StartsWith("Up to date - ", guided.StatusText);
        Assert.Equal("Rebuild Index", guided.ActionText);
        Screens.Save(guided, "setup-guided-06-word-index-sample");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await guided.GoToStepAsync(Step("Dictionaries"));
        await Screens.Settle(3);
        Screens.Save(guided, "setup-guided-03-source-loaded-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        guided.Close();

        // Advanced Setup reads the same answers from the same catalogue.
        var advanced = new AdvancedSetupWindow(main);
        advanced.Show(main);
        await Screens.Settle(10);
        var titles = Enumerable.Range(0, advanced.RowCount).Select(advanced.RowTitle).ToList();
        Assert.Equal(StepState.Complete, advanced.RowState(titles.FindIndex(t => t.StartsWith("Dictionaries", StringComparison.Ordinal))));
        Assert.Equal(StepState.Complete, advanced.RowState(titles.FindIndex(t => t.StartsWith("Ancient Greek Texts", StringComparison.Ordinal))));
        Assert.Equal(StepState.Pending, advanced.RowState(titles.FindIndex(t => t.StartsWith("The Goetia", StringComparison.Ordinal))));
        Screens.Save(advanced, "setup-advanced-sample");
        advanced.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
        await Screens.Settle(3);
        Screens.Save(advanced, "setup-advanced-sample-bottom");
        advanced.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToHome();
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(3);
        Screens.Save(advanced, "setup-advanced-sample-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        advanced.Close();

        // The word index on its own, with the out-of-date logic.
        var index = new WordIndexWindow();
        index.Show(main);
        await index.RefreshStatusAsync();
        Assert.Equal(StepState.Complete, index.State);
        Assert.StartsWith("Up to date - ", index.StatusText);
        Screens.Save(index, "setup-word-index");
        index.Close();

        main.Close();
    }

    [AvaloniaFact]
    public void Data_folder_proposal_avoids_synced_folders()
    {
        using var scratch = new Scratch();
        var home = scratch.Home;
        var cloudDocs = Path.Combine(home, "Library", "Mobile Documents", "com~apple~CloudDocs");
        var inDocuments = Path.Combine(home, "Documents", "ClassicaCodexData");
        var onDesktop = Path.Combine(home, "Desktop", "Downloads for Codex");

        // Desktop & Documents off: an ordinary Documents folder is just a folder.
        Assert.Null(MacDataFolder.SyncedBy(inDocuments, home));
        Assert.Null(MacDataFolder.SyncedBy(onDesktop, home));

        // On: iCloud Drive has grown a Documents and a Desktop of its own.
        Directory.CreateDirectory(Path.Combine(cloudDocs, "Documents"));
        Directory.CreateDirectory(Path.Combine(cloudDocs, "Desktop"));
        Assert.Equal("iCloud Drive", MacDataFolder.SyncedBy(inDocuments, home)?.Service);
        Assert.Contains("Desktop & Documents", MacDataFolder.SyncedBy(inDocuments, home)?.Reason);
        Assert.Equal("iCloud Drive", MacDataFolder.SyncedBy(onDesktop, home)?.Service);
        Assert.Null(MacDataFolder.SyncedBy(Path.Combine(home, "DocumentsOld", "x"), home));

        // iCloud Drive itself, and the File Provider folders other services use.
        Assert.Equal("iCloud Drive", MacDataFolder.SyncedBy(Path.Combine(cloudDocs, "Codex"), home)?.Service);
        Assert.Equal("Google Drive", MacDataFolder.SyncedBy(Path.Combine(home, "Library", "CloudStorage", "GoogleDrive-reader@example.com", "My Drive", "x"), home)?.Service);
        Assert.Equal("Dropbox", MacDataFolder.SyncedBy(Path.Combine(home, "Library", "CloudStorage", "Dropbox", "x"), home)?.Service);

        // What the Mac proposes is never synced, whatever iCloud is doing.
        var proposal = MacDataFolder.Unsynced(home);
        Assert.Equal(Path.Combine(home, "ClassicaCodexData"), proposal);
        Assert.Null(MacDataFolder.SyncedBy(proposal, home));

        // With nothing chosen, that is what setup adopts - unless the shared
        // default already holds downloads, which are not stranded.
        DataFolderSettings.Root = DataFolderSettings.DefaultRoot;
        var defaultHoldsDownloads = Directory.Exists(DataFolderSettings.DefaultRoot)
                                    && Directory.EnumerateFileSystemEntries(DataFolderSettings.DefaultRoot).Any();
        Assert.Equal(defaultHoldsDownloads ? DataFolderSettings.DefaultRoot : proposal, MacDataFolder.Proposed(home));

        MacDataFolder.AdoptProposalIfUnchosen();
        Assert.Equal(defaultHoldsDownloads ? DataFolderSettings.DefaultRoot : proposal, DataFolderSettings.Root);

        // A folder the reader chose is kept, synced or not: it is warned about,
        // not overridden.
        DataFolderSettings.Root = inDocuments;
        Assert.Equal(inDocuments, MacDataFolder.Proposed(home));
        MacDataFolder.AdoptProposalIfUnchosen();
        Assert.Equal(inDocuments, DataFolderSettings.Root);
    }

    [AvaloniaFact]
    public async Task The_download_folder_step_warns_about_icloud_and_takes_an_unsynced_folder()
    {
        using var scratch = new Scratch();
        await scratch.FreshLibraryAsync();
        Directory.CreateDirectory(Path.Combine(scratch.Home, "Library", "Mobile Documents", "com~apple~CloudDocs", "Documents"));

        var synced = Path.Combine(scratch.Home, "Documents", "ClassicaCodexData");
        DataFolderSettings.Root = synced;

        var main = await scratch.MainAsync();
        var guided = new GuidedSetupWindow(main);
        guided.Show(main);
        await guided.RefreshAllCompletionAsync();
        await guided.GoToStepAsync(GuidedSetupWindow.DataFolderStepIndex);

        Assert.Equal(StepState.Attention, guided.StateOfStep(GuidedSetupWindow.DataFolderStepIndex));
        Assert.Contains("is synced by iCloud Drive", guided.StatusText);
        Assert.Equal(synced, guided.PathText);
        Screens.Save(guided, "setup-guided-02b-download-folder-icloud");

        // The sources are aimed at the folder in force.
        Assert.StartsWith(synced, guided.Sources[0].DefaultDestination);

        // Choosing a folder outside it moves every step with it.
        var unsynced = MacDataFolder.Unsynced(scratch.Home);
        SetPath(guided, unsynced);
        await DismissingAlerts(guided, guided.ApplyDataFolderAsync());

        Assert.Equal(unsynced, DataFolderSettings.Root);
        Assert.True(Directory.Exists(unsynced));
        Assert.DoesNotContain("iCloud", guided.StatusText);
        Assert.NotEqual(StepState.Problem, guided.StateOfStep(GuidedSetupWindow.DataFolderStepIndex));
        Assert.StartsWith(unsynced, guided.Sources[0].DefaultDestination);
        Screens.Save(guided, "setup-guided-02c-download-folder-chosen");

        guided.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Ingesting_a_local_Perseus_folder_through_the_guided_step_produces_works()
    {
        var aeschylus = Path.Combine(SparseCloneRoot, "greek", "data", "tlg0085");
        if (!Directory.Exists(aeschylus)) return;

        using var scratch = new Scratch();
        await scratch.FreshLibraryAsync();
        DataFolderSettings.Root = Path.Combine(scratch.Root, "downloads");

        var main = await scratch.MainAsync();
        Assert.Empty(main.LibraryWorks);

        var guided = new GuidedSetupWindow(main);
        guided.Show(main);
        await guided.RefreshAllCompletionAsync();

        var index = guided.Sources.ToList().FindIndex(s => s.Title == "Ancient Greek Texts");
        var greek = guided.Sources[index];
        await guided.GoToStepAsync(guided.FirstSourceStepIndex + index);

        // What the clone would have left: the repository's data folder, here
        // one author from the sparse checkout. The step's own action then runs
        // exactly as after a download - off the UI thread, reporting as it goes.
        CopyDirectory(aeschylus, Path.Combine(greek.DefaultDestination, "data", "tlg0085"));
        await guided.RunSourceActionAsync(greek, greek.RunIngest, fetch: false);

        Assert.Equal(StepState.Complete, guided.StateOfStep(guided.FirstSourceStepIndex + index));
        Assert.StartsWith("Ancient Greek Texts is ready", guided.StatusText);
        Assert.Equal("Re-download & Re-install", guided.ActionText);
        Screens.Save(guided, "setup-guided-08-ingested");

        // The reader's library has the works now, without setup being closed.
        await Screens.Settle(4);
        Assert.Contains(main.LibraryWorks, w => w.Title.Contains("Agamemnon", StringComparison.OrdinalIgnoreCase));
        Assert.True(await new EditionRepository().IsCollectionCompleteAsync(SetupDataSourceCatalog.CollectionKeys.PerseusGreek));

        // And the index, never built, says so - then builds.
        await guided.GoToStepAsync(guided.WordIndexStepIndex);
        Assert.Equal("Not built yet.", guided.StatusText);
        await guided.RunWordIndexAsync();
        Assert.Equal(StepState.Complete, guided.StateOfStep(guided.WordIndexStepIndex));
        Assert.Equal("Word index built.", guided.StatusText);
        await guided.GoToStepAsync(guided.FinishStepIndex);
        await guided.GoToStepAsync(guided.WordIndexStepIndex);
        Assert.StartsWith("Up to date - ", guided.StatusText);

        guided.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Menota_review_merges_splits_and_confirms_a_plan()
    {
        var plan = new MenotaIngestPlan
        {
            FileName = "AM-619-4to.xml",
            ManuscriptId = "AM 619 4to",
            Language = "non",
            Orthography = "diplomatic",
            DeclaredAuthors = { "Alcuin" },
            Notes = { "3 catalogue item(s); 4 division(s) at depth 1.", "Item 2 has no matching heading; matched by order." },
            Works =
            {
                Work("De virtutibus et vitiis", "head-text", "1", 4120, "Alcuin", "body/div[1]"),
                Work("De virtutibus (cont.)", "position", "1", 980, "", "body/div[2]"),
                Work("Gamal norsk homiliebok", "unmatched", "", 28750, "", "body/div[3]", "body/div[4]")
            }
        };
        plan.Works[2].Section = "Homilies";

        using var scratch = new Scratch();
        var main = await scratch.MainAsync();
        var review = new MenotaPlanWindow(plan);
        review.Show(main);
        await Screens.Settle();

        Assert.Equal(3, review.Rows.Count);
        Assert.Contains("3 of 3 work(s) selected", review.Summary);
        Screens.Save(review, "setup-menota-review");

        review.Rows[1].Include = false;
        Assert.Contains("2 of 3 work(s) selected", review.Summary);

        review.Select(0, 1);
        await review.MergeSelectedAsync();
        Assert.Equal(2, plan.Works.Count);
        Assert.Equal("merged", plan.Works[0].MatchBasis);
        Assert.Equal(new[] { "body/div[1]", "body/div[2]" }, plan.Works[0].DivPaths);

        review.Select(1);
        await review.SplitSelectedAsync();
        Assert.Equal(3, plan.Works.Count);
        Assert.Equal("Gamal norsk homiliebok (2)", plan.Works[2].Title);
        Assert.NotEqual(plan.Works[1].UrnSlug, plan.Works[2].UrnSlug);

        // Only a row still unmatched is flagged - not one that inherited the
        // flagged row's control when the table redrew after the split.
        await Screens.Settle(3);
        foreach (var row in review.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.DataContext != null))
            Assert.Equal(((MenotaPlanWindow.PlanRow)row.DataContext!).Unmatched, row.Classes.Contains("unmatched"));

        // Three names for one man is three authors in the library.
        Assert.Single(MenotaPlanWindow.AuthorFamilies(new[] { "Alcuin", "Alcuin (homilies)", "Snorri" }));
        Assert.Empty(MenotaPlanWindow.AuthorFamilies(new[] { "Alcuin", "Snorri" }));

        review.SetAuthor(review.Rows.Skip(1), "Anonymous homilist");
        review.Commit();
        Assert.True(plan.Confirmed);
        Assert.Equal("Anonymous homilist", plan.Works[2].Author);

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(3);
        Screens.Save(review, "setup-menota-review-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        review.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task Menota_import_waits_for_the_entity_file()
    {
        using var scratch = new Scratch();
        var main = await scratch.MainAsync();

        var catalogue = SetupDataSourceCatalog.Build(
            new AuthorRepository(), new LemmaRepository(), new DefinitionRepository(), new ArtifactRepository(), new EditionRepository());
        var menota = Assert.Single(catalogue, MenotaReview.IsReviewStep);

        var folder = Path.Combine(scratch.Root, "menota");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "AM-28-8vo.xml"), "<TEI xmlns=\"http://www.tei-c.org/ns/1.0\"/>");

        Assert.Equal(SetupReadinessState.Missing, menota.CheckReadiness!(folder).State);

        // No entity table, no review and no import: plans made without it
        // would carry replacement characters into the library for good.
        var proceed = await DismissingAlerts(main, MenotaReview.PrepareSecondaryAsync(menota, main, folder));
        Assert.False(proceed);
        Assert.False(File.Exists(MenotaIngestPlan.PlanPathFor(Path.Combine(folder, "AM-28-8vo.xml"))));

        main.Close();
    }

    [AvaloniaFact]
    public async Task The_synchronous_review_for_the_shim_waits_for_the_reader()
    {
        using var scratch = new Scratch();
        var main = await scratch.MainAsync();

        var folder = Path.Combine(scratch.Root, "menota");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "AM-28-8vo.xml"), "<TEI xmlns=\"http://www.tei-c.org/ns/1.0\"/>");

        // The reader pressing OK on the entity-file alert, from inside the
        // nested frame the synchronous call waits in.
        var dismiss = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        dismiss.Tick += (_, _) =>
        {
            foreach (var alert in main.OwnedWindows.ToList()) alert.Close();
        };
        dismiss.Start();

        var proceed = MenotaReview.Run(new MacWindowHandle(main), folder);
        dismiss.Stop();

        // The answer is the review's - no entity file, no import - not the
        // shim's unconditional "proceed": the alert was shown and answered
        // while the call waited.
        Assert.True(Avalonia.Threading.Dispatcher.UIThread.SupportsRunLoops);
        Assert.False(proceed);

        await Screens.Settle(2);
        foreach (var alert in main.OwnedWindows.ToList()) alert.Close();
        main.Close();
    }

    [AvaloniaFact]
    public async Task The_choice_and_the_manual_windows_open()
    {
        using var scratch = new Scratch();
        await scratch.FreshLibraryAsync();
        var main = await scratch.MainAsync();

        var choice = new SetupChoiceWindow(libraryIsEmpty: true);
        choice.Show(main);
        await Screens.Settle();
        Screens.Save(choice, "setup-choice");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(3);
        Screens.Save(choice, "setup-choice-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        choice.Choose(SetupMode.Guided);

        var ingest = new IngestWindow(main);
        ingest.Show(main);
        await Screens.Settle();
        Screens.Save(ingest, "setup-ingest-corpus");
        ingest.Close();

        var lemmas = new LemmaIngestWindow();
        lemmas.Show(main);
        await lemmas.ShowExistingCountAsync();
        Assert.Equal("Currently loaded: 0 lemma mapping(s), 0 dictionary entr(ies).", lemmas.StatusText);
        Screens.Save(lemmas, "setup-load-lemmas");
        lemmas.Close();

        var advanced = new AdvancedSetupWindow(main);
        advanced.Show(main);
        await Screens.Settle(8);
        for (var i = 0; i < advanced.RowCount; i++) Assert.Equal(StepState.Pending, advanced.RowState(i));
        Screens.Save(advanced, "setup-advanced-empty");
        advanced.Close();

        main.Close();
    }

    [Fact]
    public void A_new_library_gets_one_library_extension()
    {
        // The save panel is told no file types - told them, macOS added a
        // second .db to "classicacodex.db" - so the extension is seen to here.
        Assert.Equal("/Users/reader/classicacodex.db", Files.WithLibraryExtension("/Users/reader/classicacodex.db"));
        Assert.Equal("/Users/reader/Greek.SQLite", Files.WithLibraryExtension("/Users/reader/Greek.SQLite"));
        Assert.Equal("/Users/reader/Latin.sqlite3", Files.WithLibraryExtension("/Users/reader/Latin.sqlite3"));
        Assert.Equal("/Users/reader/classicacodex.db", Files.WithLibraryExtension("/Users/reader/classicacodex"));
        Assert.Equal("/Users/reader/notes.txt.db", Files.WithLibraryExtension("/Users/reader/notes.txt"));
    }

    private static MenotaWorkPlan Work(string title, string basis, string item, int words, string author, params string[] divs) => new()
    {
        Title = title,
        MatchBasis = basis,
        MsItemN = item,
        WordCount = words,
        Author = author,
        UrnSlug = MenotaIngestPlanner.Slug(title),
        DivPaths = divs.ToList(),
        Section = "Alcuin"
    };

    private static void SetPath(GuidedSetupWindow window, string path)
    {
        var box = window.GetVisualDescendants().OfType<TextBox>().First(b => b.IsVisible && !b.IsReadOnly);
        box.Text = path;
    }

    /// <summary>
    /// Awaits work that may raise alerts, closing each one as it appears - the
    /// reader pressing OK.
    /// </summary>
    private static async Task<T> DismissingAlerts<T>(Window owner, Task<T> work)
    {
        while (!work.IsCompleted)
        {
            await Screens.Settle(2);
            foreach (var alert in owner.OwnedWindows.ToList()) alert.Close();
        }

        return await work;
    }

    private static async Task DismissingAlerts(Window owner, Task work) =>
        await DismissingAlerts(owner, work.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.FromCurrentSynchronizationContext()));

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>
    /// A folder of the test's own - a made-up home for the iCloud checks, room
    /// for a library and its downloads - removed afterwards, with the download
    /// folder preference, the home override and the library connection put
    /// back.
    /// </summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string _previousRoot = DataFolderSettings.Root;
        private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;

        public Scratch()
        {
            Root = Path.Combine(Path.GetTempPath(), "classicacodex-setup-tests", Guid.NewGuid().ToString("N"));
            Home = Path.Combine(Root, "home");
            Directory.CreateDirectory(Home);
            MacDataFolder.HomeOverride = Home;
        }

        public string Root { get; }

        public string Home { get; }

        public async Task FreshLibraryAsync()
        {
            DbConnectionFactory.Configure(Path.Combine(Root, "library.db"), remember: false);
            await SchemaInitializer.EnsureSchemaAsync();
        }

        /// <summary>A copy of the sample library, or null if none has been built.</summary>
        public string? SampleCopy()
        {
            if (Screens.SampleLibrary is not { } sample) return null;

            var copy = Path.Combine(Root, "sample.db");
            File.Copy(sample, copy);
            return copy;
        }

        public async Task<MainWindow> MainAsync()
        {
            var main = new MainWindow { Width = 1200, Height = 800 };
            main.Show();
            await Screens.Settle();
            return main;
        }

        public void Dispose()
        {
            MacDataFolder.HomeOverride = null;
            DataFolderSettings.Root = _previousRoot;

            // Left on the library in this folder, every later test's
            // MainWindow would try to open it from a deleted folder and put
            // up "Couldn't load the library".
            if (_previousLibrary != null) DbConnectionFactory.Configure(_previousLibrary, remember: false);
            else Screens.ForgetLibrary();

            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open is the temporary folder's to clear.
            }
        }
    }
}
