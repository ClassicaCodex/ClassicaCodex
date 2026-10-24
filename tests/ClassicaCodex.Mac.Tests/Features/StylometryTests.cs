using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.Stylometry;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;
using StylometryFeature = ClassicaCodex.Mac.Features.Stylometry.StylometryFeature;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Stylometry and its validation benches, driven against the sample library:
/// Aeschylus' seven plays and Sophocles' eight are a small enough tragic pool
/// to run real Delta comparisons, validation sweeps and perturbation series
/// in a test, and Plato and Homer fill out the Greek pool a single run
/// compares against.
///
/// Every test that saves a run or an experiment does it in a copy of the
/// library, so the sample stays as it was built.
/// </summary>
public class StylometryTests
{
    [AvaloniaFact]
    public async Task A_Delta_run_on_Prometheus_Bound_ranks_its_neighbours_saves_and_opens_in_Compare_Saved_Runs()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;
        var main = session.Main;

        StylometryFeature.Open(main);
        var window = await Opened<StylometryWindow>(main);
        await window.WhenIdleAsync();

        // Every original-language work, and nothing to save before a run.
        Assert.Contains(window.Works, w => w.AuthorName == "Plato");
        Assert.All(window.Works, w => Assert.NotEqual("eng", w.Language));
        Assert.False(window.CanSave);

        await Screens.Settle(4);
        Screens.Save(window, "stylometry-01-before-a-run");

        // The filter narrows the list, never the pool.
        window.FilterBox.Text = "Prometheus";
        await Screens.Settle(2);
        Assert.Single(window.WorkList.ItemsSource!.Cast<object>());

        var prometheus = window.Works.First(w => w.WorkTitle == "Prometheus Bound");
        window.Select(prometheus);
        await window.AnalyzeAsync();
        await window.WhenIdleAsync();

        // Neighbours by Delta, nearest first, the target's own samples left out.
        var results = window.Results;
        Assert.True(results.Count > 20, $"only {results.Count} neighbours");
        Assert.All(results, r => Assert.True(r.Delta > 0));
        Assert.Equal(results.OrderBy(r => r.Delta).Select(r => r.Delta), results.Select(r => r.Delta));
        Assert.DoesNotContain(results, r => r.WorkId == prometheus.WorkId);
        Assert.Contains(results, r => r.AuthorName == "Sophocles");
        Assert.Equal(20, window.ResultsGrid.ItemsSource!.Cast<object>().Count());

        // The settings, the pool and the sampling are echoed with the result.
        Assert.Contains("Compared Aeschylus, Prometheus Bound against", window.StatusText);
        Assert.Contains("Settings: 150 features, accents folded, elision marks stripped.", window.StatusText);
        Assert.Contains("works in pool", window.StatusText);
        Assert.Contains("samples of 3,000 tokens", window.StatusText);

        // Fifteen words in the fingerprint, commonest first.
        Assert.Equal(15, window.Fingerprint.Words.Count);
        Assert.Equal(window.Fingerprint.Words.OrderByDescending(w => w.Frequency), window.Fingerprint.Words);
        Assert.True(window.CanSave);

        await Screens.Settle(4);
        Screens.Save(window, "stylometry-02-prometheus-light");

        // A header click sorts by the number behind the cell, not its text.
        window.ResultsGrid.Columns[1].Sort(System.ComponentModel.ListSortDirection.Descending);
        await Screens.Settle(2);
        var shown = window.ResultsGrid.CollectionView!.Cast<StylometryWindow.NeighbourRow>().Select(r => r.Delta).ToList();
        Assert.Equal(shown.OrderByDescending(d => d), shown);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(window, "stylometry-03-prometheus-dark-sorted");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window.ResultsGrid.Columns[0].Sort(System.ComponentModel.ListSortDirection.Ascending);

        // Saved once, with the whole neighbour list and the settings it ran at.
        await window.SaveRunAsync();
        Assert.NotNull(window.LastSavedRunId);
        Assert.False(window.CanSave);
        Assert.StartsWith("Saved: Aeschylus, Prometheus Bound (v1/150mfw/fold/elide/3000tok)", window.StatusText);

        var runRepo = new StylometryRunRepository();
        var runs = await runRepo.GetAllRunsAsync();
        var saved = Assert.Single(runs);
        Assert.Equal(window.LastSavedRunId, saved.RunId);
        Assert.Equal("Prometheus Bound", saved.TargetWorkTitle);
        Assert.Equal(new StylometrySettings(150, true, true, StylometryRunRepository.CurrentAlgorithmVersion, 3000), saved.Settings);
        Assert.Equal(results.Count, (await runRepo.GetNeighborsAsync(saved.RunId)).Count);

        // Compare Saved Runs, focused on that run as the research bench opens it.
        StylometryFeature.OpenCompareRuns(main, saved.RunId);
        var compare = await Opened<StylometryAnalysisWindow>(main);
        await compare.WhenIdleAsync();
        await Screens.Settle(4);

        Assert.EndsWith($"Saved run #{saved.RunId}", compare.Title);
        Assert.Equal("Aeschylus", compare.AuthorCombo.SelectedItem);
        var row = Assert.Single(compare.MetricRows);
        Assert.Equal(saved.RunId, row.Metrics.RunId);
        Assert.Same(row, compare.MetricsGrid.SelectedItem);
        Assert.Contains("At least three runs for one author are needed", compare.SummaryText);

        Screens.Save(compare, "stylometry-04-compare-one-run");

        // A run deleted since is said to be gone.
        StylometryFeature.OpenCompareRuns(main, saved.RunId + 999);
        var missing = await Opened<StylometryAnalysisWindow>(main);
        await missing.WhenIdleAsync();
        Assert.Contains($"Saved run #{saved.RunId + 999} is no longer available", missing.SummaryText);

        missing.Close();
        compare.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_batch_over_Aeschylus_builds_a_reference_distribution_Compare_Saved_Runs_can_read()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;
        var main = session.Main;

        StylometryFeature.Open(main);
        var window = await Opened<StylometryWindow>(main);
        await window.WhenIdleAsync();

        var asked = string.Empty;
        window.Confirm = (_, message) =>
        {
            asked = message;
            return Task.FromResult(true);
        };

        window.Select(window.Works.First(w => w.WorkTitle == "Agamemnon"));
        await window.BatchAsync();
        await window.WhenIdleAsync();

        Assert.Contains("Run and save all 7 works by Aeschylus", asked);
        Assert.Contains("will be replaced", asked);
        Assert.StartsWith("Batch complete - 7 runs saved for Aeschylus (v1/150mfw/fold/elide/3000tok)", window.StatusText);

        // Run twice, it replaces rather than doubles.
        await window.BatchAsync();
        await window.WhenIdleAsync();
        var runs = await new StylometryRunRepository().GetAllRunsAsync();
        Assert.Equal(7, runs.Count);
        Assert.All(runs, r => Assert.Equal("Aeschylus", r.TargetAuthorName));

        StylometryFeature.OpenCompareRuns(main);
        var compare = await Opened<StylometryAnalysisWindow>(main);
        await compare.WhenIdleAsync();

        compare.AuthorCombo.SelectedItem = "Aeschylus";
        await Screens.Settle(4);

        Assert.Equal(7, compare.MetricRows.Count);
        Assert.StartsWith("Reference distribution over 7 runs for Aeschylus:", compare.SummaryText);
        Assert.Contains("Read the Delta floor row instead", compare.SummaryText);

        // Depth first, shallowest at the top; z-scores are leave-one-out.
        Assert.Equal(compare.MetricRows.OrderBy(r => r.Metrics.DepthToFirstOutsider), compare.MetricRows);
        Assert.Contains(compare.MetricRows, r => r.Z.HasValue);

        Screens.Save(compare, "stylometry-05-compare-distribution");

        compare.Tabs.SelectedIndex = 1;
        await Screens.Settle(4);
        Assert.Equal(7, compare.StabilityRows.Count);
        Screens.Save(compare, "stylometry-06-compare-stability");

        compare.Tabs.SelectedIndex = 2;
        await Screens.Settle(4);
        Assert.Equal(7, compare.LengthRows.Count);
        Assert.StartsWith("Spearman's rho between length and depth =", compare.LengthSummaryText);
        Assert.Equal(compare.LengthRows.OrderBy(r => r.Tokens), compare.LengthRows);
        Screens.Save(compare, "stylometry-07-compare-length");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        compare.Tabs.SelectedIndex = 0;
        await Screens.Settle(6);
        Screens.Save(compare, "stylometry-08-compare-distribution-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        compare.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Ranks_are_averaged_over_ties()
    {
        Assert.Equal(new[] { 1.0, 2.5, 2.5, 4.0 }, StylometryAnalysisWindow.AverageRanks(new List<double> { 1, 5, 5, 9 }));
        Assert.Equal(new[] { 2.0, 2.0, 2.0 }, StylometryAnalysisWindow.AverageRanks(new List<double> { 3, 3, 3 }));
    }

    [AvaloniaFact]
    public async Task The_validation_bench_validates_Aeschylus_against_Sophocles_and_opens_its_grid_and_perturbation_series()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;
        var main = session.Main;

        var bench = new ValidationWindow { OnOpenWork = main.OpenWorkAsync };
        bench.Show(main);
        await bench.WhenIdleAsync();
        await Screens.Settle(4);

        bench.TargetAuthor.SelectedItem = "Aeschylus";
        await Screens.Settle(2);

        // The tick list is the other Greek authors; a preset sets it, a tick
        // by hand makes it Custom - and keeps the other ticks.
        Assert.Equal(new object[] { "Homer", "Plato", "Sophocles" }, bench.PoolAuthors.Tags);
        bench.PoolPreset.SelectedIndex = 1;
        Assert.Equal(3, bench.PoolAuthors.CheckedTags.Count);
        bench.PoolAuthors.Click("Homer");
        Assert.Equal(2, bench.PoolPreset.SelectedIndex);
        Assert.Equal(new object[] { "Plato", "Sophocles" }, bench.PoolAuthors.CheckedTags);

        bench.TickPoolAuthors("Sophocles");
        Assert.Equal(new object[] { "Sophocles" }, bench.PoolAuthors.CheckedTags);

        await bench.ValidateAsync();
        await bench.WhenIdleAsync();

        var result = bench.LastResult;
        Assert.NotNull(result);
        Assert.Equal("Aeschylus", result.TargetAuthor);
        Assert.Equal(7, result.Works.Count + result.Skipped.Count);
        Assert.Contains("recovered", bench.SummaryText);
        Assert.Contains("Margin vs length: rho", bench.SummaryText);
        Assert.Contains("Held-out work contributed to normalisation", bench.StatusText);

        // The export header is the run's, captured when it finished.
        Assert.Contains("Author: Aeschylus", bench.ExportNotes);
        Assert.Contains(bench.ExportNotes, n => n.StartsWith("Pool: Aeschylus against Sophocles (15 works)"));
        Assert.Contains(bench.ExportNotes, n => n.StartsWith("Sample size: 2500 tokens, 150 most frequent words, accents folded"));

        await Screens.Settle(4);
        Screens.Save(bench, "stylometry-09-validation");

        // The grid inherits the author and the pool. One sample size, two
        // feature counts, one folding: two cells.
        var grid = bench.OpenGrid();
        Assert.NotNull(grid);
        foreach (var size in ParameterGridRunner.DefaultChunkSizes.Where(s => s != 2500)) grid.ChunkSizes.SetChecked(size, false);
        foreach (var count in ParameterGridRunner.DefaultFeatureCounts.Where(c => c is not (100 or 150))) grid.FeatureCounts.SetChecked(count, false);
        grid.BothFoldings.IsChecked = false;

        await grid.RunGridAsync();
        await grid.WhenIdleAsync();

        Assert.Equal(2, grid.Cells.Count);
        Assert.All(grid.Cells, c => Assert.False(c.Failed));
        Assert.Equal(grid.Cells.OrderBy(c => Math.Abs(c.MarginLengthCorrelation)), grid.Cells);
        Assert.StartsWith("Pool: Aeschylus against Sophocles (15 works).", grid.SummaryText);
        Assert.Contains("weakest first", grid.StatusText);

        await Screens.Settle(4);
        Screens.Save(grid, "stylometry-10-grid");
        grid.Close();

        // The perturbation series on Prometheus Bound, with Sophocles as donor
        // and Aeschylus as the control.
        var series = await bench.OpenPerturbationForTestsAsync();
        Assert.NotNull(series);
        await Screens.Settle(4);

        series.TargetWork.SelectedItem = series.TargetWork.ItemsSource!.Cast<object>().First(o => o.ToString()!.StartsWith("Prometheus Bound"));
        Assert.Equal(new object[] { "Sophocles" }, series.DonorAuthors.CheckedTags);
        series.Iterations.Value = 2;

        await series.RunSeriesAsync();
        await series.WhenIdleAsync();

        // Six levels, crossed and control.
        Assert.Equal(12, series.Rows.Count);
        Assert.Equal(6, series.Rows.Count(r => r.Control));
        Assert.Contains("Reproduce: seed 42, 2 iterations, replace, 2,500-token samples, 150 MFW.", series.SummaryText);
        Assert.Contains("It is not an estimate of how much of any real text somebody else wrote.", series.SummaryText);
        Assert.Equal(13, series.ExportTable.Count);
        Assert.Contains(series.ExportNotes, n => n.StartsWith("Mode: Replace (length held), seed 42, 2 iterations per level"));

        await Screens.Settle(4);
        Screens.Save(series, "stylometry-11-perturbation");

        // Saved with its seed, its pool and its settings, then loaded back.
        series.AskLabel = () => Task.FromResult<string?>("Prometheus against Sophocles");
        await series.SaveExperimentAsync();
        Assert.StartsWith("Saved as experiment", series.StatusText);

        var experiments = new StylometryExperimentRepository();
        var savedExperiment = Assert.Single(await experiments.GetAllAsync(ExperimentKinds.Perturbation));
        Assert.Equal(42, savedExperiment.Seed);
        Assert.Equal(2, savedExperiment.Iterations);
        Assert.Equal("Prometheus against Sophocles", savedExperiment.Label);
        Assert.Equal("Aeschylus, Sophocles", savedExperiment.PoolSummary);

        var definition = await experiments.GetDefinitionAsync(savedExperiment.ExperimentId);
        Assert.NotNull(definition);
        Assert.Equal(15, definition.PoolWorkIds.Count);
        Assert.True(definition.FoldAccents);
        Assert.Equal("grc", definition.Language);
        Assert.Equal("Sophocles", definition.Parameters["donors"]);
        Assert.Equal(12, (await experiments.GetRowsAsync(savedExperiment.ExperimentId)).Count);

        series.Seed.Value = 7;
        await series.LoadExperimentAsync(savedExperiment);
        Assert.Equal(12, series.Rows.Count);
        Assert.Equal(42, (int)series.Seed.Value!);
        Assert.Contains("Same 15-work pool as now", series.StatusText);
        Assert.Contains(series.ExportNotes, n => n.StartsWith($"Classica Codex perturbation series - loaded from experiment {savedExperiment.ExperimentId}"));
        Assert.Equal(13, series.ExportTable.Count);

        var picker = new ExperimentPickerWindow(new[] { savedExperiment });
        picker.Show(series);
        await Screens.Settle(4);
        Screens.Save(picker, "stylometry-12-experiment-picker");
        picker.Choose(savedExperiment);
        Assert.Same(savedExperiment, picker.Selected);

        // Every work by the author, with what the sweep could have detected.
        series.Iterations.Value = 2;
        series.Confirm = (_, _) => Task.FromResult(true);
        await series.RunEveryWorkAsync();
        await series.WhenIdleAsync();

        Assert.Equal(7 * 12, series.Rows.Count);
        Assert.Contains("works contaminated with Sophocles at", series.SummaryText);
        Assert.Contains(", seed 42, 2 iterations.", series.SummaryText);
        Assert.Contains("Detection power against a reference scatter of", series.SummaryText);
        Assert.Contains(series.ExportNotes, n => n.StartsWith("Detection power - "));

        await Screens.Settle(4);
        Screens.Save(series, "stylometry-13-perturbation-every-work");

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(6);
        Screens.Save(series, "stylometry-14-perturbation-dark");
        Screens.Save(bench, "stylometry-15-validation-dark");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

        series.Close();
        bench.Close();
    }

    private static async Task<T> Opened<T>(MainWindow main) where T : Window
    {
        await Screens.Settle(6);
        return main.OwnedWindows.OfType<T>().Last(w => w.IsVisible);
    }

    /// <summary>A main window over a copy of the sample library, deleted afterwards.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly string _copy;

        private Session(string copy, MainWindow main)
        {
            _copy = copy;
            Main = main;
        }

        public MainWindow Main { get; }

        public static async Task<Session?> StartAsync()
        {
            if (Screens.SampleLibrary is not { } library) return null;

            var folder = Path.Combine(Path.GetTempPath(), "classicacodex-mac-stylometry-tests");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".db");
            File.Copy(library, copy);
            File.SetAttributes(copy, FileAttributes.Normal);

            DbConnectionFactory.Configure(copy, remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            var main = new MainWindow { Width = 1360, Height = 860 };
            main.Show();
            await Screens.Settle(10);
            return new Session(copy, main);
        }

        public ValueTask DisposeAsync()
        {
            Main.Close();

            // Back to the library everything else is written against, so a
            // test that runs after this one does not find its copy gone.
            if (Screens.SampleLibrary is { } library) DbConnectionFactory.Configure(library, remember: false);

            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(_copy);
            }
            catch (IOException)
            {
                // Left for the temp folder's own clean-up.
            }

            return ValueTask.CompletedTask;
        }
    }
}
