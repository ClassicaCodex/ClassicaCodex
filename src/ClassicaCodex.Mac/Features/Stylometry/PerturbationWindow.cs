using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Contaminates a work whose authorship is not in question, and measures what
/// it takes to stop the method recognising it.
///
/// WHY THIS EXPERIMENT AND NOT ANOTHER COMPARISON OF WORKS. Every margin this
/// bench produces correlates with text length - rho +0.42 to +0.73 across forty
/// parameter configurations, with no region escaping it - so the margins of two
/// different works cannot be compared. This holds ONE work fixed and varies
/// only the contamination, and in Replace mode the token count does not move
/// either. The confound is held constant rather than argued away.
///
/// THE CONTROL IS NOT OPTIONAL AND IT IS ON BY DEFAULT. A falling curve on its
/// own proves nothing: any large change to a text might reduce a margin simply
/// by being a change. Running the same series with the work's OWN AUTHOR as
/// donor answers that - on real text the cross-author curve fell to 29% of
/// baseline at 50% injection while the same-author curve ROSE to 137%, because
/// the work was being pulled towards its author's centre and away from its own
/// idiosyncrasy. The two curves diverging is what shows the measure responds to
/// whose material it is rather than to how much of it moved.
///
/// WHAT A RESULT HERE MEANS. That the METHOD absorbs a given amount of
/// disturbance on a text of known authorship. It is not an estimate of how much
/// of any real text somebody else wrote, and the window says so in the place a
/// reader is most likely to stop reading.
///
/// The Mac counterpart of PerturbationForm. A saved experiment records the
/// accent folding the pool was actually read at, and its language; Windows
/// wrote "folded" whatever the validation bench had been set to.
/// </summary>
public sealed class PerturbationWindow : FeatureWindow
{
    private readonly string _targetAuthor;
    private readonly IReadOnlyList<WorkTokens> _pool;
    private readonly bool _foldAccents;
    private readonly string? _language;

    private readonly ComboBox _targetWork;
    private readonly CheckList _donorAuthors;
    private readonly CheckBox _sameAuthorControl;
    private readonly ComboBox _levelPreset;
    private readonly ComboBox _mode;
    private readonly ComboBox _donorScope;
    private readonly NumericUpDown _seed;
    private readonly NumericUpDown _iterations;
    private readonly NumericUpDown _sampleSize;
    private readonly NumericUpDown _featureCount;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly Button _runAllButton;
    private readonly Button _saveButton;
    private readonly Button _loadButton;
    private readonly SelectableTextBlock _summary;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly DataGrid _levels;

    private readonly StylometryExperimentRepository _experiments = new();
    private CancellationTokenSource? _cancellation;
    private PerturbationLevel? _finalLevel;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    /// <summary>The rows the table shows, computed, loaded or failed, in order.</summary>
    private readonly List<LevelRow> _rows = new();

    /// <summary>
    /// Every level produced so far, with the work and donor it belongs to.
    ///
    /// Kept because the table holds strings that were rounded for a screen -
    /// margins to three decimals, correlations to two - and an export built
    /// from those hands an analyst the rounded numbers. Several rounds of this
    /// project were spent recomputing statistics from three-decimal screen
    /// reads and getting answers that differed from the application's own.
    /// </summary>
    private readonly List<(string Work, string Donor, PerturbationLevel Level)> _rawLevels = new();

    /// <summary>
    /// Rows from a loaded experiment.
    ///
    /// A load cannot rebuild <see cref="_rawLevels"/>: those hold every
    /// individual trial, and the database stores the aggregates rather than
    /// thousands of per-mixture margins. So the export reads from here instead
    /// when a run was loaded rather than computed - without it, exporting after
    /// a load produced a file with a header, column names, and no data.
    /// </summary>
    private readonly List<ExperimentRow> _loadedRows = new();

    private string _runNotes = string.Empty;
    private string _powerNotes = string.Empty;

    /// <summary>
    /// The settings a finished run was made with, captured when it started.
    /// What Save stores and what the summary's "Reproduce" line reads - the
    /// controls keep moving after a run, and an experiment saved with the
    /// settings the window shows now rather than the ones that produced the
    /// rows would be a record of something that never ran.
    /// </summary>
    private RunSettings? _ranWith;

    /// <summary>
    /// The header written above an exported table, captured when the rows were
    /// produced rather than read from the controls when the file is written.
    ///
    /// THE WINDOWS FORM GOT THIS WRONG THREE TIMES. Anything describing a run
    /// has to be captured with the run: the controls keep moving afterwards,
    /// and a header taken from them at export time describes the form rather
    /// than the data. The observed failure was a file headed "Target author:
    /// Aelian, pool Aelian and Aelius Herodianus" whose every row was Plato's
    /// Republic against Polybius - the settings had been changed after loading
    /// an older experiment, and nothing had cleared it.
    ///
    /// A header that contradicts its own table is worse than no header, because
    /// it will be believed.
    /// </summary>
    private IReadOnlyList<string> _exportNotes = Array.Empty<string>();

    /// <summary>
    /// Whose experiment the table currently holds - the window's own author
    /// after a run, the saved experiment's after a load. Names the exported
    /// file: the name follows the DATA, not the window.
    /// </summary>
    private string _exportAuthor;

    private static readonly (string Label, double[] Levels)[] LevelPresets =
    {
        ("Coarse - 0, 1, 2, 5, 10, 20%", new[] { 0.00, 0.01, 0.02, 0.05, 0.10, 0.20 }),
        ("Wide - 0 to 50% in tens", new[] { 0.00, 0.10, 0.20, 0.30, 0.40, 0.50 }),
        ("Sensitivity - 0 to 25% in fives", new[] { 0.00, 0.05, 0.10, 0.15, 0.20, 0.25 }),
        ("Fine - 0 to 10% in ones", new[] { 0.00, 0.01, 0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.08, 0.09, 0.10 })
    };

    public PerturbationWindow(string targetAuthor, IReadOnlyList<WorkTokens> pool, bool foldAccents, string? language)
    {
        _targetAuthor = targetAuthor;
        _exportAuthor = targetAuthor;
        _pool = pool;
        _foldAccents = foldAccents;
        _language = language;

        Title = $"Perturbation - how much disturbance before the method stops recognising it? ({targetAuthor})";
        Width = 1400;
        Height = 840;
        MinWidth = 1100;
        MinHeight = 640;

        // --- Work to contaminate -------------------------------------------------
        _targetWork = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
        var choices = pool.Where(w => w.AuthorName == targetAuthor).OrderBy(w => w.WorkTitle).Select(w => new WorkChoice(w)).ToList();
        _targetWork.ItemsSource = choices;
        if (choices.Count > 0) _targetWork.SelectedIndex = 0;

        // --- Donor material --------------------------------------------------------
        _donorAuthors = new CheckList(96);
        var donors = pool.Select(w => w.AuthorName).Distinct().Where(a => a != targetAuthor).OrderBy(a => a).ToList();
        for (var i = 0; i < donors.Count; i++) _donorAuthors.Add(donors[i], donors[i], i == 0);

        // On by default. Without it a falling curve is uninterpretable - any
        // large change to a text might reduce a margin simply by being a
        // change. This is the comparison that separates "disturbed by someone
        // else's style" from "disturbed".
        _sameAuthorControl = new CheckBox { Content = $"Also run the {targetAuthor} control", IsChecked = true };

        // --- Series --------------------------------------------------------------
        _levelPreset = new ComboBox { ItemsSource = LevelPresets.Select(p => p.Label).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

        // Whole corpus first and default: it is what every result recorded so
        // far used, and changing the default would silently make old and new
        // runs incomparable.
        _donorScope = new ComboBox { ItemsSource = new[] { "Whole donor corpus", "One work per mixture" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

        // Replace first, and the default. It holds the token count constant, so
        // the length confound cannot move while the contamination does.
        _mode = new ComboBox { ItemsSource = new[] { "Replace (length held)", "Add (length grows)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

        _iterations = Bench.Number(1, 200, 1, 25);
        _seed = Bench.Number(0, 999999, 1, 42);

        // --- Delta settings ------------------------------------------------------
        _sampleSize = Bench.Number(500, 20000, 250, 2500);
        _featureCount = Bench.Number(20, 1000, 10, 150);

        _runButton = Bench.Button("Run Series", "Play", Bench.WithChord("Contaminate the chosen work at each level", "R"));
        _runButton.Classes.Add("accent");
        _runButton.IsDefault = true;
        _runButton.Click += (_, _) => _pending = RunAsync();

        _cancelButton = Bench.Button("Stop", "Stop", Bench.WithChord("Stop - whatever has finished is kept", "."));
        _cancelButton.IsEnabled = false;
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();

        // Four plays showed that three of them lose the same 0.020 of margin
        // and one does not. Four points cannot say what the normal range IS -
        // that needs the whole author, and running them one at a time is how a
        // question stops getting asked.
        _runAllButton = Bench.Button("Run Every Work by This Author…", "Users", "The same series on every work, with what the sweep could have detected");
        _runAllButton.Click += (_, _) => _pending = RunAllAsync();

        // Saving and loading sit next to each other because the pair is the
        // point: every run in this project so far existed only as a CSV
        // somebody remembered to export, and several conclusions in
        // docs/stylometry-notes.md rest on runs that are gone.
        _saveButton = Bench.Button("Save Experiment…", "Download", Bench.WithChord("Save the rows with their seed and pool", "S"));
        _saveButton.Click += (_, _) => _pending = SaveAsync();

        _loadButton = Bench.Button("Load…", "Upload", Bench.WithChord("Reload a saved experiment", "O"));
        _loadButton.Click += (_, _) => _pending = LoadAsync();

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto") };
        Bench.Place(actions, _runButton, 0, 0);
        Bench.Place(actions, _cancelButton, 0, 2);
        Bench.Place(actions, _runAllButton, 2, 0, 3);
        Bench.Place(actions, _saveButton, 4, 0);
        Bench.Place(actions, _loadButton, 4, 2);

        var settingsColumn = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Bench.Group("Work to contaminate", _targetWork),
                Bench.Group("Donor material", new StackPanel { Spacing = 8, Children = { _donorAuthors.View, _sameAuthorControl } }),
                Bench.Group("Series", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        _levelPreset,
                        Bench.Field("Donor draw:", _donorScope, 100),
                        Bench.Field("Mode:", _mode, 100),
                        Bench.Field("Iterations:", _iterations, 100),
                        Bench.Field("Seed:", _seed, 100)
                    }
                }),
                Bench.Group("Delta settings", new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Bench.Field("Sample size:", _sampleSize, 100),
                        Bench.Field("MFW:", _featureCount, 100),
                        new TextBlock
                        {
                            Text = $"Accents {(foldAccents ? "folded" : "unfolded")}, as the validation bench read the pool.",
                            Classes = { "caption" },
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    }
                })
            }
        };
        var leftScroll = Bench.SidePanel(settingsColumn, actions, 334);

        _summary = Bench.Report("Pick a work and a donor, then run. The control runs alongside by default.");
        _progress = Bench.Progress();
        var notice = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new ScrollViewer { MaxHeight = 230, Content = new StackPanel { Children = { _summary, _progress } } }
        };

        _levels = BenchTable.Create();
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Work", r => r.Work, width: 1.2, star: true, starMinimum: 110));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Donor", r => r.Donor, star: true, starMinimum: 110));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Injected", r => r.Injected, r => r.InjectedKey, 0, numeric: true));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Mean margin", r => r.MeanMargin, r => r.MeanMarginKey, 0, numeric: true));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("SD", r => r.Sd, r => r.SdKey, 70, numeric: true));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("% of baseline", r => r.OfBaseline, r => r.OfBaselineKey, 0, numeric: true));

        // The percentage cannot separate a small real effect from noise. At 20%
        // Aeschylus, Alcestis read 98% of baseline and Rhesus 80% - but
        // Alcestis had moved a tenth of one standard deviation and Rhesus 2.3,
        // so only one of those is an effect. This column is what says which.
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Shift (SD)", r => r.ShiftSd, r => r.ShiftSdKey, 0, numeric: true));

        // The only column that compares across works. Percentage divides by the
        // baseline and SD divides by the noise, and on four real plays those
        // two rank the same experiment in exactly opposite orders. In Delta,
        // Rhesus, Hecuba and Helen all lost about 0.028 at 20% injection while
        // Heracleidae lost 0.011 - which is the finding, and neither of the
        // other columns shows it.
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Drop (Δ)", r => r.Drop, r => r.DropKey, 0, numeric: true));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Recovered", r => r.Recovered, r => r.RecoveredKey, 0, numeric: true));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Nearest", r => r.Nearest, width: 140));
        _levels.Columns.Add(BenchTable.Column<LevelRow>("Tokens", r => r.Tokens, r => r.TokensKey, 70, numeric: true));

        TableExport.Attach(
            _levels,
            () => $"perturbation-{_exportAuthor}",
            ExportRows,
            notes: () => _exportNotes);

        _status = Bench.Status();
        var levelsPane = ToolPane.Build("LEVELS · CONTROL ROWS MUTED", _levels, footer: _status);

        var right = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        right.Children.Add(notice);
        right.Children.Add(levelsPane);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,20,*") };
        Grid.SetColumn(right, 2);
        columns.Children.Add(leftScroll);
        columns.Children.Add(right);

        var heading = Bench.Heading(
            "How much disturbance before the method stops recognising it?",
            "Another author's words are mixed into a work of known authorship, level by level, with the same series run on " +
            "the work's own author as the control. The contamination is synthetic: this measures what the method absorbs, " +
            "not how much of any real text somebody else wrote.");

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 20) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(columns);
        Content = layout;

        Bench.Chord(this, Key.R, _runButton, () => _pending = RunAsync());
        Bench.Chord(this, Key.OemPeriod, _cancelButton, () => _cancellation?.Cancel());
        Bench.Chord(this, Key.S, _saveButton, () => _pending = SaveAsync());
        Bench.Chord(this, Key.O, _loadButton, () => _pending = LoadAsync());

        Closed += (_, _) =>
        {
            _closed = true;
            _cancellation?.Cancel();
        };
    }

    // For the tests, which drive the window as a reader would.
    internal ComboBox TargetWork => _targetWork;
    internal CheckList DonorAuthors => _donorAuthors;
    internal ComboBox LevelPreset => _levelPreset;
    internal NumericUpDown Iterations => _iterations;
    internal NumericUpDown Seed => _seed;
    internal IReadOnlyList<LevelRow> Rows => _rows;
    internal int RawLevelCount => _rawLevels.Count;
    internal string SummaryText => _summary.Text ?? string.Empty;
    internal string StatusText => _status.Text ?? string.Empty;
    internal IReadOnlyList<string> ExportNotes => _exportNotes;
    internal IReadOnlyList<IReadOnlyList<string>> ExportTable => ExportRows();

    /// <summary>Asks before a whole-author run. The tests answer it themselves.</summary>
    internal Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>Asks for an experiment's label; null is a cancel. The tests answer it themselves.</summary>
    internal Func<Task<string?>>? AskLabel { get; set; }

    internal Task RunSeriesAsync() => _pending = RunAsync();

    internal Task RunEveryWorkAsync() => _pending = RunAllAsync();

    internal Task SaveExperimentAsync() => _pending = SaveAsync();

    internal Task LoadExperimentAsync(ExperimentSummary chosen) => _pending = LoadAsync(chosen);

    /// <summary>Waits until everything started so far has finished.</summary>
    internal async Task WhenIdleAsync()
    {
        Task current;
        do
        {
            current = _pending;
            await current;
        }
        while (!ReferenceEquals(current, _pending));
    }

    private sealed class WorkChoice
    {
        public readonly WorkTokens Work;
        public WorkChoice(WorkTokens work) => Work = work;
        public override string ToString() => $"{Work.WorkTitle}  ({Work.Tokens.Count:N0} tokens)";
    }

    /// <summary>Everything a run is made with, read from the controls once, before it starts.</summary>
    private sealed record RunSettings(
        double[] Levels, InjectionMode Mode, DonorScope Scope, DeltaSettings Delta, int Seed, int Iterations,
        IReadOnlyList<string> Donors, bool Control);

    private RunSettings ReadSettings() => new(
        LevelPresets[Math.Max(_levelPreset.SelectedIndex, 0)].Levels,
        _mode.SelectedIndex == 1 ? InjectionMode.Add : InjectionMode.Replace,
        _donorScope.SelectedIndex == 1 ? DonorScope.SingleWork : DonorScope.WholeCorpus,
        new DeltaSettings(Bench.Read(_featureCount), Bench.Read(_sampleSize)),
        Bench.Read(_seed),
        Bench.Read(_iterations),
        _donorAuthors.CheckedTags.OfType<string>().ToList(),
        _sameAuthorControl.IsChecked == true);

    private void SetRunning(bool running)
    {
        _runButton.IsEnabled = !running;
        _runAllButton.IsEnabled = !running;
        _saveButton.IsEnabled = !running;
        _loadButton.IsEnabled = !running;
        _cancelButton.IsEnabled = running;
        _progress.IsVisible = running;
        _progress.IsIndeterminate = true;
    }

    private void ClearTable()
    {
        _rows.Clear();
        _rawLevels.Clear();
        _loadedRows.Clear();
        _levels.ItemsSource = null;
        _exportNotes = Array.Empty<string>();
    }

    private void RefreshTable() => _levels.ItemsSource = _rows.ToList();

    private async Task RunAsync()
    {
        if (_cancellation != null) return;
        if (_targetWork.SelectedItem is not WorkChoice choice) return;

        var run = ReadSettings();
        if (run.Donors.Count == 0)
        {
            await Alert.Info(this, "No donor", "Tick at least one donor author.");
            return;
        }

        var target = choice.Work;

        var crossDonors = _pool
            .Where(w => run.Donors.Contains(w.AuthorName))
            .Select(w => w.WorkId)
            .ToList();

        // The control draws from the target's author EXCLUDING the target
        // itself - injecting a work into itself would measure nothing.
        var controlDonors = _pool
            .Where(w => w.AuthorName == _targetAuthor && w.WorkId != target.WorkId)
            .Select(w => w.WorkId)
            .ToList();

        SetRunning(true);
        ClearTable();
        _cancellation = new CancellationTokenSource();

        // Both of these describe a cross-work sweep. A single series has no
        // cross-work reading and no detection power, and leaving the previous
        // sweep's figures in place would put them in this run's export header -
        // an exported table whose header describes different data is worse than
        // one with no header at all.
        _runNotes = string.Empty;
        _powerNotes = string.Empty;
        _ranWith = null;

        try
        {
            var token = _cancellation.Token;

            // Progress<T> captures the context it is constructed on - the UI
            // thread, here - and marshals every report back to it. The series
            // runs on a worker, and a callback that wrote the status directly
            // from there was the whole of a Windows bug.
            var progress = new Progress<string>(text =>
            {
                if (!_closed) _status.Text = text;
            });
            IProgress<string> report = progress;

            var runs = new List<(string Donor, List<PerturbationLevel> Series)>();

            var crossLabel = string.Join(", ", run.Donors);
            _status.Text = $"Contaminating {target.WorkTitle} with {crossLabel}…";

            runs.Add((crossLabel, await Task.Run(() => PerturbationRunner.RunSeries(
                _pool, target.WorkId, crossDonors, run.Levels, run.Mode, run.Seed, run.Iterations, run.Delta,
                (i, n, level) => report.Report($"{crossLabel}: level {i + 1} of {n} ({level:P0})…"),
                token, run.Scope), token)));

            if (run.Control && controlDonors.Count == 0)
            {
                // The control needs other works by the same author, and the
                // pool may not have any - an author with one work, or a pool
                // filtered until only this work is left. Saying so beats
                // throwing "No donor material" from inside the engine.
                _status.Text = $"No control: the pool has no other {_targetAuthor} work to draw from.";
            }

            if (run.Control && controlDonors.Count > 0)
            {
                _status.Text = $"Control: contaminating with {_targetAuthor}…";

                runs.Add(($"{_targetAuthor} (control)", await Task.Run(() => PerturbationRunner.RunSeries(
                    _pool, target.WorkId, controlDonors, run.Levels, run.Mode, run.Seed, run.Iterations, run.Delta,
                    (i, n, level) => report.Report($"Control: level {i + 1} of {n} ({level:P0})…"),
                    token, run.Scope), token)));
            }

            if (_closed) return;

            _ranWith = run;
            Show(target, runs, run);
            _exportAuthor = _targetAuthor;
            _exportNotes = CaptureNotes(run);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _status.Text = "Series failed - see message.";
            SetRunning(false);
            await Alert.Error(this, "Series failed", ex.Message);
        }
        finally
        {
            if (!_closed) SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>
    /// Full-precision rows for export, header first.
    ///
    /// Deliberately wider than the table on screen: it carries the mixing
    /// standard error and the headroom as well, because those are what an
    /// analyst needs to turn a drop into a z-score and neither fits usefully
    /// in a column.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>> ExportRows()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Work", "Donor", "Injected", "MeanMargin", "SD", "StdErrorOfMean",
                "BaselineMargin", "ProportionOfBaseline", "ShiftInSDs", "HeadroomInSDs",
                "DropInDelta", "Recovered", "Trials", "NearestAuthor", "Tokens"
            }
        };

        string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        // A loaded experiment has no PerturbationLevel objects behind it - the
        // database stores aggregates, not individual mixtures - so its rows are
        // formatted straight from what was stored.
        if (_rawLevels.Count == 0 && _loadedRows.Count > 0)
        {
            foreach (var r in _loadedRows)
            {
                var se = r.Trials < 2 ? 0 : r.StdDev / Math.Sqrt(r.Trials);

                rows.Add(new[]
                {
                    r.WorkTitle,
                    r.Donor,
                    N(r.Level),
                    N(r.MeanMargin),
                    N(r.StdDev),
                    N(se),
                    N(r.BaselineMargin),
                    r.Level <= 0 || r.BaselineMargin == 0 ? "" : N(r.MeanMargin / r.BaselineMargin),
                    r.Level <= 0 || r.StdDev < 1e-12 ? "" : N((r.MeanMargin - r.BaselineMargin) / r.StdDev),
                    r.Level <= 0 || r.StdDev < 1e-12 ? "" : N(Math.Abs(r.BaselineMargin) / r.StdDev),
                    r.Level <= 0 ? "" : N(r.MeanMargin - r.BaselineMargin),
                    r.Recovered.ToString(CultureInfo.InvariantCulture),
                    r.Trials.ToString(CultureInfo.InvariantCulture),
                    r.NearestAuthor ?? "",
                    r.TokenCount.ToString(CultureInfo.InvariantCulture)
                });
            }

            return rows;
        }

        foreach (var (work, donor, level) in _rawLevels)
        {
            var nearest = level.NearestAuthorCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();
            var stdError = level.Trials.Count < 2
                ? 0
                : level.MarginStdDev / Math.Sqrt(level.Trials.Count);

            rows.Add(new[]
            {
                work,
                donor,
                N(level.InjectionFraction),
                N(level.MeanMargin),
                N(level.MarginStdDev),
                N(stdError),
                N(level.BaselineMargin),
                level.InjectionFraction <= 0 ? "" : N(level.ProportionOfBaseline),
                level.InjectionFraction <= 0 ? "" : N(level.ShiftInStdDevs),
                level.InjectionFraction <= 0 ? "" : N(level.HeadroomInStdDevs),
                level.InjectionFraction <= 0 ? "" : N(level.AbsoluteShift),
                level.RecoveredCount.ToString(CultureInfo.InvariantCulture),
                level.Trials.Count.ToString(CultureInfo.InvariantCulture),
                nearest.Key ?? "",
                level.Trials.Count == 0 ? "" : level.Trials[0].TokenCount.ToString(CultureInfo.InvariantCulture)
            });
        }

        return rows;
    }

    /// <summary>
    /// The lines written above the exported table, for whatever has just been
    /// produced. Called when a run finishes - never at export time.
    ///
    /// A table of numbers without its seed and settings is not reproducible,
    /// which is the one property a perturbation experiment is supposed to
    /// have. These go into the file so the numbers cannot be separated from
    /// what produced them.
    /// </summary>
    private IReadOnlyList<string> CaptureNotes(RunSettings run)
    {
        var notes = new List<string>
        {
            $"Classica Codex perturbation series - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Target author: {_targetAuthor}",
            $"Sample size: {run.Delta.ChunkSize} tokens, {run.Delta.FeatureWordCount} most frequent words, " +
            $"accents {(_foldAccents ? "folded" : "unfolded")}",
            $"Mode: {(run.Mode == InjectionMode.Replace ? "Replace (length held)" : "Add (length grows)")}, " +
            $"seed {run.Seed}, {run.Iterations} iterations per level",
            $"Donor draw: {(run.Scope == DonorScope.SingleWork ? "one work per mixture" : "whole donor corpus")}",
            $"Donors: {string.Join(", ", run.Donors)}; control {(run.Control ? "run" : "not run")}",
            $"Pool: {PoolSummary} ({_pool.Count} works)"
        };

        if (!string.IsNullOrEmpty(_runNotes)) notes.Add(_runNotes);
        if (!string.IsNullOrEmpty(_powerNotes)) notes.Add("Detection power - " + _powerNotes);

        notes.Add("Contamination is synthetic. This measures how much disturbance the method absorbs " +
                  "on texts of known authorship - it is not an estimate of how much of any real text " +
                  "somebody else wrote.");

        return notes;
    }

    private string PoolSummary => string.Join(", ", _pool.Select(w => w.AuthorName).Distinct().OrderBy(a => a));

    /// <summary>
    /// Writes the current table to the library, with everything needed to
    /// rebuild it: the seed, the exact works in the pool, the levels, the
    /// donors, and whether the control ran.
    ///
    /// The rows are stored at full precision from the underlying levels rather
    /// than from the table, for the same reason the export is: the table on
    /// screen is rounded for reading, and a stored run that carries only the
    /// rounded numbers cannot be re-analysed. The detection power was computed
    /// against the wrong scatter once, and the fix had to be checked against a
    /// stored run rather than a memory of one.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_rawLevels.Count == 0 || _ranWith is not { } run)
        {
            await Alert.Info(this, "Nothing to save",
                _loadedRows.Count > 0
                    ? "This experiment was loaded from the library and is already saved. Run it again to " +
                      "store a new copy."
                    : "Run something first.");
            return;
        }

        // Null only for a cancel. A blank label is a perfectly good
        // experiment, and saves unlabelled.
        var label = AskLabel != null
            ? await AskLabel()
            : await TextPrompt.Ask(this, "Save experiment",
                "Label for this experiment (optional) - what you were testing, so the list " +
                "is readable in six months:", string.Empty, "Save");
        if (label == null || _closed) return;

        var parameters = new Dictionary<string, string>
        {
            ["mode"] = run.Mode == InjectionMode.Add ? "add" : "replace",
            ["donorScope"] = run.Scope == DonorScope.SingleWork ? "singleWork" : "wholeCorpus",
            ["levels"] = string.Join(",", run.Levels.Select(l => l.ToString(CultureInfo.InvariantCulture))),
            ["donors"] = string.Join(", ", run.Donors),
            ["control"] = run.Control ? "yes" : "no"
        };

        var metrics = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(_runNotes)) metrics["crossWork"] = _runNotes;
        if (!string.IsNullOrEmpty(_powerNotes)) metrics["detectionPower"] = _powerNotes;

        var rows = _rawLevels.Select((r, i) =>
        {
            var nearest = r.Level.NearestAuthorCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();

            return new ExperimentRow(
                i,
                _pool.FirstOrDefault(w => w.WorkTitle == r.Work)?.WorkId,
                r.Work, r.Donor, r.Level.InjectionFraction,
                r.Level.MeanMargin, r.Level.MarginStdDev, r.Level.BaselineMargin,
                r.Level.RecoveredCount, r.Level.Trials.Count,
                nearest.Key, nearest.Value,
                r.Level.Trials.Count == 0 ? 0 : r.Level.Trials[0].TokenCount);
        }).ToList();

        var definition = new ExperimentDefinition(
            ExperimentKinds.Perturbation,
            _targetAuthor,
            _language,
            PoolSummary,
            _pool.Select(w => w.WorkId).Distinct().OrderBy(x => x).ToList(),
            run.Seed,
            run.Iterations,
            run.Delta.ChunkSize,
            run.Delta.FeatureWordCount,
            FoldAccents: _foldAccents,
            StylometryRunRepository.CurrentAlgorithmVersion,
            parameters,
            metrics,
            string.IsNullOrWhiteSpace(label) ? null : label.Trim());

        try
        {
            var id = await Task.Run(() => _experiments.SaveAsync(definition, rows));
            if (!_closed) _status.Text = $"Saved as experiment {id} - {rows.Count} rows, seed {run.Seed}.";
        }
        catch (Exception ex)
        {
            if (!_closed) await Alert.Error(this, "Save failed", ex.Message);
        }
    }

    private async Task LoadAsync()
    {
        List<ExperimentSummary> saved;
        try
        {
            saved = await Task.Run(() => _experiments.GetAllAsync(ExperimentKinds.Perturbation));
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Couldn't read saved experiments", ex.Message);
            return;
        }

        if (saved.Count == 0)
        {
            await Alert.Info(this, "Nothing to load", "No saved perturbation experiments yet.");
            return;
        }

        var chosen = await ExperimentPickerWindow.ChooseAsync(this, saved);
        if (chosen == null || _closed) return;

        await LoadAsync(chosen);
    }

    /// <summary>
    /// Reloads a saved experiment into the table.
    ///
    /// Shows the stored rows rather than re-running: a sweep is thousands of
    /// mixtures and several minutes, and the stored numbers ARE the result. The
    /// settings are put back on the controls so that re-running it is one
    /// click, which is the other thing a saved experiment is for.
    /// </summary>
    private async Task LoadAsync(ExperimentSummary chosen)
    {
        List<ExperimentRow> rows;
        ExperimentDefinition? definition;
        try
        {
            (rows, definition) = await Task.Run(async () =>
                (await _experiments.GetRowsAsync(chosen.ExperimentId), await _experiments.GetDefinitionAsync(chosen.ExperimentId)));
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Couldn't load the experiment", ex.Message);
            return;
        }

        if (_closed) return;

        ClearTable();
        _ranWith = null;
        _loadedRows.AddRange(rows);

        foreach (var r in rows)
        {
            // NearestCount is 0 on rows written before v16, which did not
            // record it - shown as a bare name rather than as "(0/25)", which
            // would read as unanimous disagreement.
            var nearest = r.NearestAuthor == null ? "-"
                : r.NearestCount > 0 ? $"{r.NearestAuthor} ({r.NearestCount}/{r.Trials})"
                : r.NearestAuthor;

            _rows.Add(LevelRow.FromStored(r, nearest));
        }

        RefreshTable();

        // Settings back onto the controls, so re-running really is one click.
        // The four numeric ones are on the summary; the rest live in
        // Parameters, and leaving them alone would have meant a re-run silently
        // using whatever the window happened to be showing - a different donor
        // or a different level series against the same seed, which would look
        // like a reproduction and not be one.
        Bench.Set(_seed, chosen.Seed);
        Bench.Set(_iterations, chosen.Iterations);
        Bench.Set(_sampleSize, chosen.ChunkSize);
        Bench.Set(_featureCount, chosen.FeatureWordCount);

        if (definition != null)
        {
            if (definition.Parameters.TryGetValue("mode", out var mode))
                _mode.SelectedIndex = mode == "add" ? 1 : 0;

            if (definition.Parameters.TryGetValue("donorScope", out var scope))
                _donorScope.SelectedIndex = scope == "singleWork" ? 1 : 0;

            if (definition.Parameters.TryGetValue("control", out var control))
                _sameAuthorControl.IsChecked = control == "yes";

            if (definition.Parameters.TryGetValue("donors", out var donors))
            {
                var wanted = donors.Split(',').Select(d => d.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _donorAuthors.SetWhere(tag => wanted.Contains((string)tag));
            }

            // Matched on the level list rather than on the preset's name, so a
            // preset that is later renamed or reordered still resolves.
            if (definition.Parameters.TryGetValue("levels", out var levels))
            {
                var wanted = levels.Split(',')
                    .Select(l => double.TryParse(l, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1)
                    .ToList();

                var match = Array.FindIndex(LevelPresets, pr => pr.Levels.SequenceEqual(wanted));
                if (match >= 0) _levelPreset.SelectedIndex = match;
            }
        }

        // The target work, when the loaded experiment ran on one work rather
        // than a whole author.
        var titles = rows.Select(r => r.WorkTitle).Distinct().ToList();
        if (titles.Count == 1 && _targetWork.ItemsSource is IEnumerable<WorkChoice> works &&
            works.FirstOrDefault(c => c.Work.WorkTitle == titles[0]) is { } match2)
        {
            _targetWork.SelectedItem = match2;
        }

        _summary.Text =
            $"Loaded experiment {chosen.ExperimentId}, run {chosen.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}." +
            Environment.NewLine +
            $"{chosen.TargetAuthor} against {chosen.PoolSummary}; {chosen.ProfileKey}." + Environment.NewLine +
            string.Join(Environment.NewLine, chosen.Metrics.Values);

        _runNotes = chosen.Metrics.GetValueOrDefault("crossWork", string.Empty);
        _powerNotes = chosen.Metrics.GetValueOrDefault("detectionPower", string.Empty);

        // Built from the SAVED experiment, not from the controls. The controls
        // have just been set from it, so the two agree now - but the user is
        // free to change them before exporting, and the file must describe the
        // rows it contains.
        var loadedNotes = new List<string>
        {
            $"Classica Codex perturbation series - loaded from experiment {chosen.ExperimentId}",
            $"Originally run {chosen.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
            $"Target author: {chosen.TargetAuthor}",
            $"Sample size: {chosen.ChunkSize} tokens, {chosen.FeatureWordCount} most frequent words, " +
            $"accents {(chosen.FoldAccents ? "folded" : "unfolded")}",
            $"Seed {chosen.Seed}, {chosen.Iterations} iterations per level",
            $"Pool: {chosen.PoolSummary}" +
            (definition == null ? "" : $" ({definition.PoolWorkIds.Count} works)")
        };

        if (definition != null && definition.Parameters.Count > 0)
            loadedNotes.Add("Parameters: " + string.Join("; ", definition.Parameters.Select(kv => $"{kv.Key} {kv.Value}")));

        if (!string.IsNullOrEmpty(_runNotes)) loadedNotes.Add(_runNotes);
        if (!string.IsNullOrEmpty(_powerNotes)) loadedNotes.Add("Detection power - " + _powerNotes);

        loadedNotes.Add("Contamination is synthetic. This measures how much disturbance the method absorbs " +
                        "on texts of known authorship - it is not an estimate of how much of any real text " +
                        "somebody else wrote.");

        _exportNotes = loadedNotes;
        _exportAuthor = chosen.TargetAuthor;

        // A margin is a property of the comparison, not of the text: the same
        // work at the same settings gave +0.097 against a 34-work pool and
        // +0.140 against a 27-work one. So a pool that has changed since the
        // run is worth saying out loud rather than leaving to be noticed.
        var currentPool = _pool.Select(w => w.WorkId).Distinct().OrderBy(x => x).ToList();

        var poolMatches = definition != null
                          && definition.PoolWorkIds.Count == currentPool.Count
                          && definition.PoolWorkIds.OrderBy(x => x).SequenceEqual(currentPool);

        _status.Text = definition == null
            ? $"{rows.Count} rows loaded."
            : poolMatches
                ? $"{rows.Count} rows loaded. Same {currentPool.Count}-work pool as now - re-running should reproduce these."
                : $"{rows.Count} rows loaded. Pool was {definition.PoolWorkIds.Count} works, this window has " +
                  $"{currentPool.Count} - margins are not comparable across pools, so re-running will not " +
                  "reproduce these numbers.";

        if (definition != null && definition.FoldAccents != _foldAccents)
        {
            _status.Text += $" It was also read with accents {(definition.FoldAccents ? "folded" : "unfolded")}, " +
                            $"and this pool has them {(_foldAccents ? "folded" : "unfolded")}.";
        }
    }

    /// <summary>
    /// Runs the series for every work by the target author, appending each to
    /// the table.
    ///
    /// The control is skipped here unless it is ticked, because it doubles a
    /// run that is already nineteen series long, and its purpose - showing the
    /// two curves diverge - is established once per author rather than once per
    /// work. Where it matters most is a work that behaves unlike the rest, and
    /// that one can be re-run singly.
    ///
    /// Cross-work comparison is what this is for, so the summary reports the
    /// spread of the absolute drop rather than any one work's curve. A
    /// percentage and an SD count each rank the same four plays in opposite
    /// orders; Delta is the quantity that compares.
    /// </summary>
    private async Task RunAllAsync()
    {
        if (_cancellation != null) return;

        var run = ReadSettings();
        if (run.Donors.Count == 0)
        {
            await Alert.Info(this, "No donor", "Tick at least one donor author.");
            return;
        }

        var works = _pool
            .Where(w => w.AuthorName == _targetAuthor)
            .OrderBy(w => w.WorkTitle)
            .ToList();

        var estimate = works.Count * run.Levels.Length * run.Iterations * (run.Control ? 2 : 1);
        var message =
            $"Run {works.Count} works x {run.Levels.Length} levels x {run.Iterations} iterations" +
            (run.Control ? " x 2 (with control)" : "") + $" = about {estimate:N0} mixtures." +
            Environment.NewLine + Environment.NewLine +
            "This takes a while. Stop is available throughout and keeps whatever has finished.";

        var confirmed = Confirm != null
            ? await Confirm("Run every work", message)
            : await Alert.Confirm(this, "Run every work", message, "Run");
        if (!confirmed || _closed) return;

        SetRunning(true);
        ClearTable();
        _runNotes = string.Empty;
        _powerNotes = string.Empty;
        _ranWith = run;
        _cancellation = new CancellationTokenSource();

        var finals = new List<(string Title, double Baseline, double Drop, double Shift)>();

        // Every level of every work, so the sweep can report what it could have
        // detected rather than only what it did.
        var byLevel = new Dictionary<double, List<double>>();

        // Length and uncontaminated margin per work, for the reference scatter
        // that every detection figure is divided by.
        var lengths = new List<(double Length, double Margin)>();

        // Works too short to sample at this size. Named in the status line
        // rather than silently dropped - a sweep that quietly measured fewer
        // works than the author has is the kind of thing that looks like a
        // result.
        var skipped = new List<string>();

        try
        {
            var token = _cancellation.Token;
            var progress = new Progress<string>(text =>
            {
                if (!_closed) _status.Text = text;
            });
            IProgress<string> report = progress;
            var crossLabel = string.Join(", ", run.Donors);

            var crossDonors = _pool
                .Where(w => run.Donors.Contains(w.AuthorName))
                .Select(w => w.WorkId)
                .ToList();

            for (var i = 0; i < works.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var work = works[i];
                var index = i;
                _status.Text = $"Work {i + 1} of {works.Count}: {work.WorkTitle}…";
                _progress.IsIndeterminate = false;
                _progress.Maximum = works.Count;
                _progress.Value = i;

                var runs = new List<(string Donor, List<PerturbationLevel> Series)>();

                try
                {
                    runs.Add((crossLabel, await Task.Run(() => PerturbationRunner.RunSeries(
                        _pool, work.WorkId, crossDonors, run.Levels, run.Mode, run.Seed, run.Iterations, run.Delta,
                        (l, n, level) => report.Report(
                            $"{work.WorkTitle} ({index + 1}/{works.Count}): level {l + 1} of {n} ({level:P0})…"),
                        token, run.Scope), token)));

                    var controlDonors = _pool
                        .Where(w => w.AuthorName == _targetAuthor && w.WorkId != work.WorkId)
                        .Select(w => w.WorkId)
                        .ToList();

                    // Skipped rather than attempted when there is nothing to
                    // draw from: an author with a single work in the pool has
                    // no same-author control available, and that is a fact
                    // about the pool rather than an error.
                    if (run.Control && controlDonors.Count > 0)
                    {
                        runs.Add(($"{_targetAuthor} (control)", await Task.Run(() => PerturbationRunner.RunSeries(
                            _pool, work.WorkId, controlDonors, run.Levels, run.Mode, run.Seed, run.Iterations, run.Delta,
                            (l, n, level) => report.Report(
                                $"{work.WorkTitle} control ({index + 1}/{works.Count}): level {l + 1} of {n}…"),
                            token, run.Scope), token)));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A work too short to sample at this size is skipped rather
                    // than aborting the other eighteen.
                    _rows.Add(LevelRow.Failed(work.WorkTitle, ex.Message.Length > 40 ? ex.Message[..40] + "..." : ex.Message));
                    RefreshTable();
                    continue;
                }

                if (_closed) return;
                Show(work, runs, run, append: true);

                foreach (var lvl in runs[0].Series.Where(l => l.InjectionFraction > 0))
                {
                    if (!byLevel.TryGetValue(lvl.InjectionFraction, out var list))
                        byLevel[lvl.InjectionFraction] = list = new List<double>();
                    list.Add(lvl.AbsoluteShift);
                }

                var last = runs[0].Series.OrderBy(l => l.InjectionFraction).Last();

                // A work shorter than one sample yields no trials at all, and
                // RunSeries reports that as a level of zeros rather than
                // throwing. Folding those into the cross-work statistics adds
                // fake works at the origin: four short Platonic dialogues
                // (Cleitophon, Definitiones, Hipparchus, Lovers) dragged the
                // fitted line and inflated the reference scatter from 0.082 to
                // 0.137, which pulled the detection AUC at 20% down from 0.94
                // to 0.80 - turning a clean positive control into a marginal
                // one.
                if (last.Trials.Count == 0)
                {
                    skipped.Add(work.WorkTitle);
                    continue;
                }

                finals.Add((work.WorkTitle, last.BaselineMargin, last.AbsoluteShift, last.ShiftInStdDevs));
                lengths.Add((work.Tokens.Count, last.BaselineMargin));
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closed) _status.Text = $"Stopped after {finals.Count} works - the rows below are complete.";
        }
        catch (Exception ex)
        {
            if (!_closed) await Alert.Error(this, "Run failed", ex.Message);
        }
        finally
        {
            if (!_closed) SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }

        if (_closed) return;

        if (skipped.Count > 0)
        {
            _runNotes = $"Skipped {skipped.Count} work(s) shorter than one {run.Delta.ChunkSize:N0}-token " +
                        $"sample: {string.Join(", ", skipped)}.";
        }

        if (finals.Count >= 3)
        {
            ShowSpread(finals, string.Join(", ", run.Donors), run, run.Levels[^1], byLevel, lengths);

            // Carried into the export so the file records the cross-work
            // reading, not just the rows it was derived from.
            var comparison = PerturbationRunner.CompareWorks(
                finals.Select(f => (f.Title, f.Baseline, f.Drop)).ToList());

            _runNotes = (_runNotes.Length > 0 ? _runNotes + " " : "") +
                $"Cross-work: rho(baseline, drop) {StatFormat.Signed(comparison.BaselineDropCorrelation)}; " +
                $"residual MAD {comparison.ResidualMad:0.0000}; " +
                $"{comparison.ExpectedFalseFlags():0.0} works expected to clear three MAD by chance.";
        }

        // Last, so the snapshot includes the cross-work reading and the
        // detection power that ShowSpread has just computed. Capturing before
        // them would have written a header describing less than the file holds.
        if (_rows.Count > 0)
        {
            _exportAuthor = _targetAuthor;
            _exportNotes = CaptureNotes(run);
        }
    }

    /// <summary>
    /// Reports how the works differ from each other, which is the point of
    /// running them together - and reports it on the residual rather than the
    /// raw drop.
    ///
    /// Ranking on the raw drop rediscovers which work has the smallest margin.
    /// Over nineteen Euripides plays contaminated with Sophocles, rho between
    /// baseline margin and the size of the drop is +0.749: works with more to
    /// lose lose more. Heracleidae ranks 3.3 median absolute deviations out on
    /// raw drop and 1.2 on the residual, and the extremes become Hecuba and
    /// Ion. The raw ranking was reading the baseline.
    /// </summary>
    private void ShowSpread(
        List<(string Title, double Baseline, double Drop, double Shift)> finals,
        string donor, RunSettings run, double topLevel,
        Dictionary<double, List<double>>? byLevel = null,
        List<(double Length, double Margin)>? lengths = null)
    {
        var comparison = PerturbationRunner.CompareWorks(
            finals.Select(f => (f.Title, f.Baseline, f.Drop)).ToList());

        var drops = finals.Select(f => f.Drop).OrderBy(d => d).ToList();
        var median = drops[drops.Count / 2];
        var outliers = comparison.Outliers();

        _summary.Text =
            $"{finals.Count} works contaminated with {donor} at {topLevel:P0}, seed {run.Seed}, " +
            $"{run.Iterations} iterations." + Environment.NewLine +
            $"Median drop {StatFormat.Signed3(median)} Delta, range {StatFormat.Signed3(drops[0])} to " +
            $"{StatFormat.Signed3(drops[^1])}. But drop is NOT independent of baseline margin here - " +
            $"rho {StatFormat.Signed(comparison.BaselineDropCorrelation)} - so works with more margin " +
            "to lose lose more, and a ranking on raw drop mostly rediscovers which work has the least." +
            Environment.NewLine +
            (outliers.Count == 0
                ? "After fitting drop against baseline, no work sits three median absolute deviations " +
                  "from the line. On this measure they behave alike."
                : string.Join("  ", outliers.Take(3).Select(o =>
                      $"{o.Title}: drop {StatFormat.Signed3(o.Drop)} against {StatFormat.Signed3(o.Expected)} " +
                      $"expected, {o.DeviationsFromTypical:0.0} MAD ({o.DeviationsFromTypical / 1.4826:0.0} sigma).")) +
                  $"  But {comparison.ExpectedFalseFlags():0.0} works would clear three MAD by chance in a " +
                  $"sweep this size - three MAD is only about two sigma - so {outliers.Count} flags is " +
                  (outliers.Count > 2 * comparison.ExpectedFalseFlags()
                      ? "more than chance for the sweep as a whole, and still not a licence to believe any one of them."
                      : "about what chance produces. Candidates for reading, not findings.") +
                  "  Length, genre, transmission, a lacuna or a bad edition all produce this.") +
            (comparison.RawRankingIsMisleading
                ? Environment.NewLine + "The smallest raw drop and the largest residual are different " +
                  "works, which is the sign that the raw column was reading baseline."
                : "");

        // What the sweep COULD have found. A null result is worthless without
        // it: "no anomaly" and "no anomaly, and anything under thirty percent
        // would have been invisible" are different statements.
        if (byLevel is { Count: > 0 })
        {
            // ReferenceScatter, NOT comparison.RobustSigma. The first is how
            // much genuine works differ from each other; the second is how
            // consistently they respond to contamination, which is twenty times
            // smaller and makes every level look perfectly detectable. Passing
            // the wrong one turned an AUC of 0.55 into 1.00.
            var scatter = PerturbationRunner.ReferenceScatter(lengths ?? new List<(double, double)>());

            var power = PerturbationRunner.MeasurePower(
                scatter,
                byLevel.Select(kv => (kv.Key, kv.Value.Average())).ToList());

            var detectable = PerturbationRunner.DetectableFrom(power);

            _powerNotes = string.Join("  ", power.Select(p =>
                $"{p.InjectionFraction:P0}: AUC {p.Auc:0.00}"));

            _summary.Text += Environment.NewLine +
                $"Detection power against a reference scatter of {scatter:0.0000} - {_powerNotes}. " +
                (detectable.HasValue
                    ? $"Contamination is reliably distinguishable from about {detectable.Value:P0}."
                    : "No level tested reaches an AUC of 0.80, so nothing in this range is reliably " +
                      "distinguishable - a null result here bounds what could have been found, and " +
                      "the bound is loose.");
        }

        _status.Text = $"{_rows.Count} rows. Contamination is synthetic - see the summary.";
    }

    private void Show(
        WorkTokens target,
        List<(string Donor, List<PerturbationLevel> Series)> runs,
        RunSettings run,
        bool append = false)
    {
        if (!append) ClearTable();

        var workTitle = target.WorkTitle;

        foreach (var (donor, series) in runs)
        {
            foreach (var level in series.OrderBy(l => l.InjectionFraction))
            {
                _rows.Add(LevelRow.FromLevel(workTitle, donor, level));
                _rawLevels.Add((workTitle, donor, level));
            }
        }

        RefreshTable();

        var main = runs[0].Series;
        _finalLevel = main.OrderBy(l => l.InjectionFraction).Last();

        _summary.Text =
            PerturbationRunner.Summarise(main, target.WorkTitle) + Environment.NewLine +
            $"That final shift is {StatFormat.Signed(_finalLevel!.ShiftInStdDevs)} standard deviations of " +
            $"the mixing noise, against a ceiling of {_finalLevel.HeadroomInStdDevs:0.0} - the baseline " +
            "margin cannot fall further than zero, so read the shift as a fraction of that headroom " +
            "rather than against a fixed threshold." + Environment.NewLine +
            (runs.Count > 1
                ? $"Control ({_targetAuthor} as donor) ended at " +
                  $"{runs[1].Series.OrderBy(l => l.InjectionFraction).Last().ProportionOfBaseline:P0} of baseline. " +
                  "The two curves diverging is what shows the measure responds to whose material it is, " +
                  "not to how much of it moved."
                : "No control run - a falling curve on its own cannot distinguish cross-author " +
                  "disturbance from disturbance.") + Environment.NewLine +
            $"Reproduce: seed {run.Seed}, {run.Iterations} iterations, " +
            $"{(run.Mode == InjectionMode.Replace ? "replace" : "add")}, " +
            $"{run.Delta.ChunkSize:N0}-token samples, {run.Delta.FeatureWordCount} MFW.";

        _status.Text = $"{_rows.Count} rows. Contamination is synthetic - see the summary before quoting a number.";
    }

    /// <summary>
    /// One row of the table: a level computed here, a level loaded from the
    /// library, or a work that could not be run. The display strings are the
    /// Windows table's; the keys behind them are what the columns sort by.
    /// </summary>
    internal sealed record LevelRow(
        string Work, string Donor,
        string Injected, double? InjectedKey,
        string MeanMargin, double? MeanMarginKey,
        string Sd, double? SdKey,
        string OfBaseline, double? OfBaselineKey,
        string ShiftSd, double? ShiftSdKey,
        string Drop, double? DropKey,
        string Recovered, double? RecoveredKey,
        string Nearest,
        string Tokens, int? TokensKey,
        bool Control, bool Alarming, bool FailedRow) : IMarkedRow
    {
        public string? InkKey => Control || FailedRow ? "Cc.InkMuted" : Alarming ? "Cc.Danger" : null;

        public string? ShadeKey => null;

        public static LevelRow FromLevel(string work, string donor, PerturbationLevel level)
        {
            var contaminated = level.InjectionFraction > 0;
            var nearest = level.NearestAuthorCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();

            return new LevelRow(
                work, donor,
                level.InjectionFraction.ToString("P0", CultureInfo.CurrentCulture), level.InjectionFraction,
                StatFormat.Signed3(level.MeanMargin), level.MeanMargin,
                level.MarginStdDev.ToString("0.0000", CultureInfo.CurrentCulture), level.MarginStdDev,
                contaminated ? level.ProportionOfBaseline.ToString("P0", CultureInfo.CurrentCulture) : "-",
                contaminated ? level.ProportionOfBaseline : null,
                contaminated ? StatFormat.Signed(level.ShiftInStdDevs) : "-",
                contaminated ? level.ShiftInStdDevs : null,
                contaminated ? StatFormat.Signed3(level.AbsoluteShift) : "-",
                contaminated ? level.AbsoluteShift : null,
                $"{level.RecoveredCount}/{level.Trials.Count}", level.RecoveryRate,
                nearest.Key == null ? "-" : $"{nearest.Key} ({nearest.Value}/{level.Trials.Count})",
                level.Trials.Count == 0 ? "-" : level.Trials[0].TokenCount.ToString("N0", CultureInfo.CurrentCulture),
                level.Trials.Count == 0 ? null : level.Trials[0].TokenCount,
                Control: donor.Contains("(control)"),
                Alarming: contaminated && level.ShiftInStdDevs < -2,
                FailedRow: false);
        }

        public static LevelRow FromStored(ExperimentRow r, string nearest)
        {
            var contaminated = r.Level > 0;
            double? ofBaseline = contaminated && r.BaselineMargin != 0 ? r.MeanMargin / r.BaselineMargin : null;

            // Recomputed rather than left blank. It is (mean - baseline) / SD,
            // and all three are stored, so there is no ambiguity for a dash to
            // protect against.
            double? shift = contaminated && r.StdDev >= 1e-12 ? (r.MeanMargin - r.BaselineMargin) / r.StdDev : null;
            double? drop = contaminated ? r.MeanMargin - r.BaselineMargin : null;

            return new LevelRow(
                r.WorkTitle, r.Donor,
                r.Level.ToString("P0", CultureInfo.CurrentCulture), r.Level,
                StatFormat.Signed3(r.MeanMargin), r.MeanMargin,
                r.StdDev.ToString("0.0000", CultureInfo.CurrentCulture), r.StdDev,
                ofBaseline?.ToString("P0", CultureInfo.CurrentCulture) ?? "-", ofBaseline,
                shift.HasValue ? StatFormat.Signed(shift.Value) : "-", shift,
                drop.HasValue ? StatFormat.Signed3(drop.Value) : "-", drop,
                $"{r.Recovered}/{r.Trials}", r.Trials == 0 ? null : (double)r.Recovered / r.Trials,
                nearest,
                r.TokenCount.ToString("N0", CultureInfo.CurrentCulture), r.TokenCount,
                Control: r.Donor.Contains("(control)"),
                Alarming: false,
                FailedRow: false);
        }

        public static LevelRow Failed(string work, string message) => new(
            work, "-", "-", null, "-", null, "-", null, "-", null, "-", null, "-", null, "-", null,
            message, "-", null, Control: false, Alarming: false, FailedRow: true);
    }
}
