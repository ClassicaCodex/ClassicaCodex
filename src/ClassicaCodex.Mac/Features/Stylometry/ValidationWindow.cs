using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Asks whether the method recovers texts whose authorship is not in question,
/// before it is used on one that is.
///
/// The first of the validation and experiments benches - the parameter grid
/// and controlled perturbation open from it, on the same author and pool. All
/// the arithmetic is in ClassicaCodex.Core.Stylometry.LeaveOneOutValidator;
/// this window picks a pool, calls it, and lays the answer out.
///
/// WHAT THE NUMBERS MEAN, because a recovery rate on its own is close to
/// meaningless:
///
///   MARGIN is the headline. Mean Delta from the work's samples to samples by
///   other authors, minus mean Delta to samples by its own author. Positive
///   means the work sits nearer its own company than everyone else's. It is a
///   difference of distances rather than a rank, which was the reason for
///   choosing it over depth to first outsider.
///
///   POOL SEPARATION says how hard the question was. Against Greek prose every
///   tragedy recovers and the rate is 100%, which tests nothing; against
///   Aeschylus the between-author signal is roughly a tenth of ordinary
///   within-Euripides variation. Same rate, incomparable meanings.
///
///   LENGTH CORRELATION is the one to read first. On the first real run of this
///   harness - nineteen Euripides plays against Sophocles and Aeschylus,
///   2,500-token samples - all nineteen recovered and the margin correlated
///   with text length at rho +0.62. Depth to first outsider, the measure this
///   one replaced, correlated at +0.58. So the margin is not innocent of the
///   confound that discredited its predecessor, and a high value here means the
///   sweep may be sorting works by how much text they have rather than by who
///   wrote them.
///
/// That last paragraph is the reason this window exists. It is easier to build
/// a tool that produces encouraging numbers than one that catches itself.
///
/// The Mac counterpart of ValidationForm. Two things differ, both fixes: the
/// export header is captured with the run it describes rather than read off
/// the controls when the file is written, and moving a preset to "Custom" by
/// ticking an author keeps the ticks, where on Windows it cleared them.
/// </summary>
public sealed class ValidationWindow : FeatureWindow
{
    internal sealed class WorkItem
    {
        public int WorkId;
        public int EditionId;
        public string AuthorName = string.Empty;
        public string WorkTitle = string.Empty;
        public string Language = string.Empty;
        public AttributionStatus Attribution = AttributionStatus.Accepted;
    }

    private const int SameAuthorPresetIndex = 0;
    private const int EveryonePresetIndex = 1;
    private const int CustomPresetIndex = 2;

    private readonly EditionRepository _editionRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    private readonly ComboBox _targetAuthor;
    private readonly ComboBox _poolPreset;
    private readonly CheckList _poolAuthors;
    private readonly CheckBox _foldAccents;
    private readonly CheckBox _excludeHeldOut;
    private readonly CheckBox _excludeNonCompositions;
    private readonly ComboBox _disputedHandling;
    private readonly NumericUpDown _sampleSize;
    private readonly NumericUpDown _featureCount;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly Button _gridButton;
    private readonly Button _perturbButton;
    private readonly SelectableTextBlock _summary;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly DataGrid _results;

    private List<WorkItem> _allWorks = new();
    private List<string> _filteredOut = new();
    private ValidationResult? _lastResult;
    private List<WorkRow> _rows = new();
    private IReadOnlyList<string> _exportNotes = Array.Empty<string>();
    private string _exportAuthor = string.Empty;

    /// <summary>Set while a preset is setting the ticks, so it does not undo itself.</summary>
    private bool _suppressPresetReset;

    private CancellationTokenSource? _cancellation;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    /// <summary>
    /// Tokens by (workId, foldAccents), held for the lifetime of the window.
    ///
    /// Tokenising is the expensive half - it reads every reading line of every
    /// work in the pool out of SQLite - and a validation sweep runs the engine
    /// once per sample per work, which at nineteen plays is around fifty runs
    /// over the same text. Without this the window would re-read the corpus
    /// fifty times to answer one question, and the parameter grid would
    /// multiply that by the size of the grid.
    ///
    /// Keyed on accent folding as well as work id because folding happens
    /// during tokenisation, so the two settings produce genuinely different
    /// token streams from the same rows. Locked, because the grid and the
    /// perturbation bench read through it from worker threads, and either
    /// can be running while this window runs a sweep of its own.
    /// </summary>
    private readonly Dictionary<(int WorkId, bool Fold), IReadOnlyList<string>> _tokenCache = new();
    private readonly object _tokenCacheLock = new();

    public ValidationWindow()
    {
        Title = "Validation - can these settings recover known texts?";
        Width = 1340;
        Height = 840;
        MinWidth = 1000;
        MinHeight = 660;

        // --- What is being validated -------------------------------------------
        _targetAuthor = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
        _targetAuthor.SelectionChanged += (_, _) => ApplyPreset(rebuild: true);
        var targetGroup = Bench.Group("Author to validate", _targetAuthor);

        // --- The pool ------------------------------------------------------------
        //
        // Chosen rather than fixed, because which authors are in the pool is
        // not a detail - it moved the margin by more than twelvefold in
        // testing. A preset sets the ticks; the ticks are what actually runs,
        // so a preset can be adjusted rather than being a mode to fight.
        _poolPreset = new ComboBox
        {
            ItemsSource = new[]
            {
                "Same author only (no margin - dispersion instead)",
                "Everyone in this language",
                "Custom - tick below"
            },
            SelectedIndex = CustomPresetIndex,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _poolPreset.SelectionChanged += (_, _) =>
        {
            if (!_suppressPresetReset) ApplyPreset(rebuild: false);
        };

        _poolAuthors = new CheckList(150);

        // Ticking anything by hand makes the run custom, whatever the menu
        // last said. Without this the header reads "Same author only" over a
        // pool holding three authors, which is worse than no label at all -
        // the pool is the single biggest lever on the margin, and a screenshot
        // of a result should say what it was measured against.
        _poolAuthors.Changed += _ =>
        {
            _suppressPresetReset = true;
            _poolPreset.SelectedIndex = CustomPresetIndex;
            _suppressPresetReset = false;
        };

        var poolGroup = Bench.Group("Compare against", new StackPanel { Spacing = 8, Children = { _poolPreset, _poolAuthors.View } });

        // --- Settings ------------------------------------------------------------
        _foldAccents = new CheckBox { Content = "Fold accents (ἦ ἥ ᾗ -> η)", IsChecked = true };
        _sampleSize = Bench.Number(500, 20000, 250, 2500);
        _featureCount = Bench.Number(20, 1000, 10, 150);

        // Off by default because every saved run in the database was produced
        // with the held-out work contributing to the normalisation, and a
        // default that silently disagreed with the archive would make old and
        // new results quietly incomparable. Measured at under 1% on a
        // nineteen-work Euripides pool - worth having, not worth assuming.
        _excludeHeldOut = new CheckBox { Content = "Held-out work excluded from normalisation" };

        _excludeNonCompositions = new CheckBox { Content = "Skip fragment collections and indices", IsChecked = true };

        // Excluding is the default for the LEAVE-ONE-OUT SWEEP, where every
        // work in the pool contributes to every other work's own-author mean,
        // so a doubted work quietly shapes the reference set it is meant to be
        // tested against.
        //
        // There used to be a third option, "Only doubted works", and it was
        // incoherent. It kept the target author's doubted works and dropped the
        // accepted ones, which left the pool with a single Euripides play: the
        // same-author control had nothing to draw from, and the margin had no
        // same-author neighbours to be measured against either.
        //
        // The experiment it was meant to enable does not need it. DeltaEngine
        // already excludes a target's own chunks from its neighbour list, so
        // selecting Rhesus as a perturbation target measures it against the
        // other plays whether or not it is in the pool. "Include" is the right
        // setting there, and always was.
        _disputedHandling = new ComboBox
        {
            ItemsSource = new[] { "Include in the pool", "Exclude from the pool" },
            SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var settingsGroup = Bench.Group("Settings", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _foldAccents,
                Bench.Field("Sample size (tokens):", _sampleSize, 160),
                Bench.Field("Most frequent words:", _featureCount, 160),
                _excludeHeldOut,
                _excludeNonCompositions,
                Bench.Field("Doubted works:", _disputedHandling, 120)
            }
        });

        _runButton = Bench.Button("Run Validation", "Play", Bench.WithChord("Hold out every work by the author in turn", "R"));
        _runButton.Classes.Add("accent");
        _runButton.IsDefault = true;
        _runButton.Click += (_, _) => _pending = RunAsync();

        _cancelButton = Bench.Button("Stop", "Stop", Bench.WithChord("Stop the sweep", "."));
        _cancelButton.IsEnabled = false;
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();

        // The grid inherits the author and pool chosen here rather than asking
        // again. Two windows offering the same pool picker would be two ways to
        // build a pool, and a stability sweep run against a different pool from
        // the validation it is meant to be testing would be quietly worthless.
        _gridButton = Bench.Button("Test Parameter Stability…", "Sliders", "The same validation across a grid of settings, on this author and pool");
        _gridButton.Click += (_, _) => _pending = OpenGridAsync();

        _perturbButton = Bench.Button("Perturbation Series…", "Lightning", "Contaminate a work of known authorship, on this author and pool");
        _perturbButton.Click += (_, _) => _pending = OpenPerturbationAsync();

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto") };
        Bench.Place(actions, _runButton, 0, 0);
        Bench.Place(actions, _cancelButton, 0, 2);
        Bench.Place(actions, _gridButton, 2, 0, 3);
        Bench.Place(actions, _perturbButton, 4, 0, 3);

        var settingsColumn = new StackPanel { Spacing = 10, Children = { targetGroup, poolGroup, settingsGroup } };
        var leftScroll = Bench.SidePanel(settingsColumn, actions, 374);

        // --- Results -------------------------------------------------------------
        _summary = Bench.Report("Pick an author and a comparison pool, then run.");
        _progress = Bench.Progress();
        var notice = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel { Children = { _summary, _progress } }
        };

        _results = BenchTable.Create();
        _results.Columns.Add(BenchTable.Column<WorkRow>("Work", r => r.Work.WorkTitle, star: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Margin", r => StatFormat.Signed3(r.Work.Margin), r => r.Work.Margin, 84, numeric: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Own author", r => r.Work.MeanDeltaSameAuthor.ToString("0.000", CultureInfo.CurrentCulture), r => r.Work.MeanDeltaSameAuthor, 96, numeric: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Others", r => r.Work.MeanDeltaOtherAuthor.ToString("0.000", CultureInfo.CurrentCulture), r => r.Work.MeanDeltaOtherAuthor, 84, numeric: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Floor", r => r.Work.DeltaFloor.ToString("0.000", CultureInfo.CurrentCulture), r => r.Work.DeltaFloor, 76, numeric: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Nearest", r => r.Work.NearestAuthor, width: 120));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Rank", r => r.Work.CorrectAuthorRank?.ToString(CultureInfo.CurrentCulture) ?? "-", r => r.Work.CorrectAuthorRank, 64, numeric: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Samples", r => r.Work.SamplesMeasured.ToString(CultureInfo.CurrentCulture), r => r.Work.SamplesMeasured, 80, numeric: true));
        _results.Columns.Add(BenchTable.Column<WorkRow>("Tokens", r => r.Work.TokenCount.ToString("N0", CultureInfo.CurrentCulture), r => r.Work.TokenCount, 84, numeric: true));
        _results.DoubleTapped += async (_, _) => await OpenSelectedAsync();
        _results.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await OpenSelectedAsync();
        };

        // Exported from the results, not the cells: the table rounds margins to
        // three places, and the notes are the run's own, captured when it
        // finished - a header read off the controls at export time describes
        // the window, not the data, the moment a setting is touched after the
        // run.
        TableExport.Attach(
            _results,
            () => $"validation-{_exportAuthor}",
            () => ExportTable(_rows),
            () => ExportTable(_results.SelectedItems.OfType<WorkRow>()),
            () => _exportNotes);

        _status = Bench.Status();
        var resultsPane = ToolPane.Build("WORKS HELD OUT · WORST MARGIN FIRST · DOUBLE-CLICK TO OPEN", _results, footer: _status);

        var right = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        right.Children.Add(notice);
        right.Children.Add(resultsPane);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,20,*") };
        Grid.SetColumn(right, 2);
        columns.Children.Add(leftScroll);
        columns.Children.Add(right);

        var heading = Bench.Heading(
            "Can these settings recover known texts?",
            "Every work by one author is held out in turn and asked whether it sits nearer its own author than the " +
            "rest of the pool. Ask this before using the settings on a disputed work - and read the length " +
            "correlation first: a sweep that sorts works by size is not measuring authorship.");

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 20) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(columns);
        Content = layout;

        Bench.Chord(this, Key.R, _runButton, () => _pending = RunAsync());
        Bench.Chord(this, Key.OemPeriod, _cancelButton, () => _cancellation?.Cancel());

        Opened += (_, _) => _pending = LoadWorksAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _cancellation?.Cancel();
        };
    }

    // For the tests, which drive the window as a reader would.
    internal ComboBox TargetAuthor => _targetAuthor;
    internal ComboBox PoolPreset => _poolPreset;
    internal CheckList PoolAuthors => _poolAuthors;
    internal NumericUpDown SampleSize => _sampleSize;
    internal NumericUpDown FeatureCount => _featureCount;
    internal ValidationResult? LastResult => _lastResult;
    internal string SummaryText => _summary.Text ?? string.Empty;
    internal string StatusText => _status.Text ?? string.Empty;
    internal IReadOnlyList<string> ExportNotes => _exportNotes;

    internal Task ValidateAsync() => _pending = RunAsync();

    /// <summary>Opens the grid on this author and pool and returns it, for the tests.</summary>
    internal ParameterGridWindow? OpenGrid()
    {
        var grid = CreateGrid();
        grid?.Show(this);
        return grid;
    }

    /// <summary>Reads the pool and opens the perturbation bench on it, for the tests.</summary>
    internal async Task<PerturbationWindow?> OpenPerturbationForTestsAsync()
    {
        var bench = await CreatePerturbationAsync();
        bench?.Show(this);
        return bench;
    }

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

    private async Task LoadWorksAsync()
    {
        try
        {
            var (works, eligible) = await Task.Run(async () =>
            {
                var editions = await _editionRepo.GetAllOriginalEditionsAsync();

                var all = editions
                    .Where(e => !string.IsNullOrEmpty(e.Language))
                    .GroupBy(e => e.WorkId)
                    .Select(g => g.First())
                    .Select(e => new WorkItem
                    {
                        WorkId = e.WorkId,
                        EditionId = e.EditionId,
                        AuthorName = e.AuthorName,
                        WorkTitle = e.WorkTitle,
                        Language = e.Language!
                    })
                    .ToList();

                // Attribution comes from the works table, not the edition rows,
                // so it is a second read. Worth it: a reference set that
                // includes works its own editors reject is partly measuring the
                // thing being tested.
                var attributions = await _workRepo.GetAttributionsAsync(all.Select(w => w.WorkId).ToList());
                foreach (var work in all)
                {
                    if (attributions.TryGetValue(work.WorkId, out var status)) work.Attribution = status;
                }

                // Only authors with enough works to hold one out and still have
                // company. Two works means every run compares a work against
                // one other, which is a number rather than a distribution.
                var authors = all
                    .GroupBy(w => w.AuthorName)
                    .Where(g => g.Count() >= 3)
                    .Select(g => g.Key)
                    .OrderBy(a => a)
                    .ToList();

                return (all, authors);
            });

            if (_closed) return;

            _allWorks = works;
            _targetAuthor.ItemsSource = eligible;
            if (eligible.Count > 0) _targetAuthor.SelectedIndex = 0;

            _status.Text = $"{_allWorks.Count} original-language works, {eligible.Count} authors with three or more.";
        }
        catch (Exception ex)
        {
            if (!_closed) _status.Text = $"Couldn't load the works: {ex.Message}";
        }
    }

    /// <summary>
    /// Fills the tick list for the chosen target, and applies whatever the
    /// preset says. The list is always the authors sharing the target's
    /// language - comparing Greek against Latin measures the alphabet.
    ///
    /// On a new target the list is rebuilt, and a custom choice keeps whichever
    /// of its authors the new list still has.
    /// </summary>
    private void ApplyPreset(bool rebuild)
    {
        if (_targetAuthor.SelectedItem is not string target) return;

        if (rebuild)
        {
            var language = _allWorks.FirstOrDefault(w => w.AuthorName == target)?.Language;
            if (language == null) return;

            var kept = _poolAuthors.CheckedTags.OfType<string>().ToHashSet();

            var authors = _allWorks
                .Where(w => w.Language == language && w.AuthorName != target)
                .GroupBy(w => w.AuthorName)
                .Select(g => (Author: g.Key, Works: g.Count()))
                .OrderBy(a => a.Author)
                .ToList();

            _poolAuthors.Clear();
            foreach (var (author, works) in authors)
                _poolAuthors.Add($"{author}  ({works})", author, kept.Contains(author));
        }

        switch (_poolPreset.SelectedIndex)
        {
            case SameAuthorPresetIndex:
                _poolAuthors.SetAll(false);
                break;
            case EveryonePresetIndex:
                _poolAuthors.SetAll(true);
                break;
        }
    }

    /// <summary>Ticks the named authors in the pool, as a reader would - the preset becomes Custom.</summary>
    internal void TickPoolAuthors(params string[] authors)
    {
        _poolAuthors.SetWhere(tag => authors.Contains((string)tag));
        _suppressPresetReset = true;
        _poolPreset.SelectedIndex = CustomPresetIndex;
        _suppressPresetReset = false;
    }

    private List<WorkItem> BuildPool(string targetAuthor)
    {
        var language = _allWorks.First(w => w.AuthorName == targetAuthor).Language;
        var chosen = new HashSet<string>(_poolAuthors.CheckedTags.OfType<string>(), StringComparer.OrdinalIgnoreCase);

        var pool = _allWorks
            .Where(w => w.Language == language)
            .Where(w => w.AuthorName == targetAuthor || chosen.Contains(w.AuthorName))
            .ToList();

        // No exemption for the target author, which is the one place this
        // differs from the Stylometry window. There, analysing Fragmenta
        // deliberately is a reasonable thing to ask for, so the filter spares
        // the work the user selected. Here nobody selects a work - the sweep
        // holds out every work the author has - so an exemption would only mean
        // silently validating against a compilation.
        //
        // In a single run a fragment collection merely distorts the variance;
        // in a validation sweep it becomes a WORK BEING VALIDATED - the harness
        // holds out Euripides' Fragmenta, asks whether the method recovers it
        // as Euripidean, and counts the answer towards a recovery rate. That
        // question has no correct answer: Fragmenta is a modern editor's
        // gathering of lines quoted by Athenaeus, Stobaeus and the scholiasts,
        // each excerpted for being quotable.
        if (_excludeNonCompositions.IsChecked == true)
            pool = pool.Where(w => !Compilations.IsNonComposition(w.WorkTitle)).ToList();

        // EXCLUDING DOUBTED WORKS IS THE DEFAULT AND IT IS NOT A DETAIL. A
        // pool is meant to establish what an author's own writing looks like,
        // and in a leave-one-out sweep every work in it contributes to every
        // other work's own-author mean. A work whose attribution its editors
        // reject therefore shapes the reference set it is supposed to be tested
        // against. The Plato runs in this project were done before this
        // existed, with Definitiones, Hipparchus and Lovers in the baseline.
        //
        // Only the TARGET author's doubted works are dropped. A doubted work by
        // another author is still a perfectly good example of that other author
        // for the purpose of being unlike this one.
        if (_disputedHandling.SelectedIndex == 1)
        {
            pool = pool
                .Where(w => w.AuthorName != targetAuthor || w.Attribution == AttributionStatus.Accepted)
                .ToList();
        }

        return pool;
    }

    /// <summary>One work's tokens, from the cache or the library. Called on a worker thread.</summary>
    private IReadOnlyList<string> TokensFor(WorkItem work, bool fold)
    {
        lock (_tokenCacheLock)
        {
            if (_tokenCache.TryGetValue((work.WorkId, fold), out var cached)) return cached;
        }

        var nodes = _textNodeRepo.GetByEditionAsync(work.EditionId, readingLinesOnly: true).GetAwaiter().GetResult();
        var text = string.Join(' ', nodes.Select(n => n.Text));
        var tokens = StylometryTokenizer.Tokenize(text, fold);

        lock (_tokenCacheLock)
        {
            _tokenCache[(work.WorkId, fold)] = tokens;
        }

        return tokens;
    }

    /// <summary>Tokenises a pool through the cache, off the UI thread, reporting each work as it is read.</summary>
    private async Task<List<WorkTokens>> TokenizeAsync(IReadOnlyList<WorkItem> pool, bool fold, CancellationToken cancellation)
    {
        var progress = new Progress<(int Done, int Total, string Title)>(p =>
        {
            if (_closed) return;
            _status.Text = $"Reading {p.Done + 1} of {p.Total}: {p.Title}…";
            _progress.IsIndeterminate = false;
            _progress.Maximum = p.Total;
            _progress.Value = p.Done;
        });
        IProgress<(int, int, string)> report = progress;

        return await Task.Run(() =>
        {
            var tokenized = new List<WorkTokens>(pool.Count);
            for (var i = 0; i < pool.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                report.Report((i, pool.Count, pool[i].WorkTitle));
                tokenized.Add(new WorkTokens(pool[i].WorkId, pool[i].AuthorName, pool[i].WorkTitle, TokensFor(pool[i], fold)));
            }

            return tokenized;
        }, cancellation);
    }

    private void SetRunning(bool running)
    {
        _runButton.IsEnabled = !running;
        _cancelButton.IsEnabled = running;
        _progress.IsVisible = running;
        if (running) _progress.IsIndeterminate = true;
    }

    private async Task RunAsync()
    {
        if (_targetAuthor.SelectedItem is not string target || _cancellation != null) return;

        var pool = BuildPool(target);
        var otherAuthors = pool.Select(w => w.AuthorName).Distinct().Count() - 1;

        // Named rather than merely absent. A work silently missing from a
        // recovery rate is the kind of thing that makes two runs
        // incomparable for a reason nobody can see afterwards.
        //
        // Language comes from the target rather than from pool[0]: an author
        // all of whose works match the filter leaves the pool empty, and that
        // case should reach the message below rather than throw here.
        var language = _allWorks.First(w => w.AuthorName == target).Language;

        var filteredOut = _excludeNonCompositions.IsChecked == true
            ? _allWorks
                .Where(w => w.Language == language && Compilations.IsNonComposition(w.WorkTitle))
                .Where(w => w.AuthorName == target || pool.Any(p => p.AuthorName == w.AuthorName))
                .Select(w => w.WorkTitle)
                .OrderBy(t => t)
                .ToList()
            : new List<string>();

        if (!pool.Any(w => w.AuthorName == target))
        {
            await Alert.Info(this, "Nothing to validate",
                $"Every work by {target} is filtered out as a compilation, so there is nothing to " +
                "hold out." + Environment.NewLine + Environment.NewLine +
                "Untick \"Skip fragment collections and indices\" to validate against them anyway - " +
                "but a recovery rate over fragment collections measures anthologising practice " +
                "rather than authorship.");
            return;
        }

        if (otherAuthors < 1)
        {
            await Alert.Info(this, "Pool needs a second author",
                "A margin compares a work's own author against other authors, so at least one other " +
                "author has to be ticked." + Environment.NewLine + Environment.NewLine +
                "A single-author pool is a legitimate question - how far apart an author's own works " +
                "sit - but it is dispersion rather than recovery, and it is not what this bench " +
                "measures.");
            return;
        }

        var fold = _foldAccents.IsChecked == true;
        var settings = new DeltaSettings(Bench.Read(_featureCount), Bench.Read(_sampleSize));
        var excludeHeldOut = _excludeHeldOut.IsChecked == true;
        var poolAuthors = string.Join(", ", pool.Select(w => w.AuthorName).Distinct().Where(a => a != target).OrderBy(a => a));

        SetRunning(true);
        _results.ItemsSource = null;
        _rows = new List<WorkRow>();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        try
        {
            var tokenized = await TokenizeAsync(pool, fold, token);
            if (_closed) return;

            _status.Text = "Validating…";

            var progress = new Progress<(int Index, int Count)>(p =>
            {
                if (_closed) return;
                _status.Text = $"Validating: holding out work {p.Index + 1} of {p.Count}…";
                _progress.IsIndeterminate = false;
                _progress.Maximum = p.Count;
                _progress.Value = p.Index;
            });
            IProgress<(int, int)> report = progress;

            var result = await Task.Run(() => LeaveOneOutValidator.Validate(
                tokenized, target, settings,
                excludeHeldOut,
                progress: (i, n) => report.Report((i, n)),
                cancellation: token), token);

            if (_closed) return;

            _filteredOut = filteredOut;
            Show(result);

            _exportAuthor = target;
            _exportNotes = new[]
            {
                $"Classica Codex leave-one-out validation - {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"Author: {target}",
                $"Pool: {target} against {poolAuthors} ({pool.Count} works)",
                $"Sample size: {settings.ChunkSize} tokens, {settings.FeatureWordCount} most frequent words, " +
                $"accents {(fold ? "folded" : "unfolded")}",
                result.HeldOutWorkExcludedFromNormalisation
                    ? "Held-out work excluded from normalisation."
                    : "Held-out work contributed to normalisation, as saved runs do.",
                $"Doubted works: {(_disputedHandling.SelectedIndex == 1 ? "excluded from" : "included in")} the pool; " +
                $"compilations {(_excludeNonCompositions.IsChecked == true ? "skipped" : "kept")}.",
                $"Margin vs length: rho {result.MarginLengthCorrelation.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"longest work {result.LengthSpread.ToString("R", CultureInfo.InvariantCulture)}x the shortest. " +
                "Read this before the margins - a sweep that sorts by length is not measuring authorship."
            };
        }
        catch (OperationCanceledException)
        {
            if (!_closed) _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _status.Text = "Validation failed - see message.";
            SetRunning(false);
            await Alert.Error(this, "Validation failed", ex.Message);
        }
        finally
        {
            if (!_closed) SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Show(ValidationResult result)
    {
        _lastResult = result;

        // Worst margin first. The works that nearly failed are the ones worth
        // reading, and a list sorted best-first buries them under eighteen
        // rows of agreement.
        _rows = result.Works
            .OrderBy(w => w.Margin)
            .Select(w => new WorkRow(w, NearestIsOther: !string.Equals(w.NearestAuthor, result.TargetAuthor, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _results.ItemsSource = _rows;

        var d = result.Difficulty;
        var rho = result.MarginLengthCorrelation;

        // The verdict is hedged by the length spread, because rho cannot be
        // read without it. An author whose works are all one size cannot
        // produce a high correlation whatever the method is doing, so a low
        // number there is uninformative rather than reassuring.
        var spread = result.LengthSpread;

        var confound = Math.Abs(rho) switch
        {
            >= 0.5 => "STRONG - the sweep may be sorting by length rather than by author",
            >= 0.3 => "moderate - interpret margin differences between long and short works cautiously",
            _ when spread < 1.8 => "weak, but these works are all a similar length, so there is little " +
                                   "spread for a length effect to show up in",
            _ => "weak"
        };

        _summary.Text =
            $"{result.RecoveredCount} of {result.Works.Count} recovered ({result.RecoveryRate:P0}), " +
            $"mean margin {StatFormat.Signed3(result.MeanMargin)}." +
            Environment.NewLine +
            $"Pool: {d.AuthorCount} authors, {d.SampleCount} samples. Within-author Δ {d.MeanWithinAuthorDelta:0.000}, " +
            $"cross-author Δ {d.MeanCrossAuthorDelta:0.000}, separation {StatFormat.Signed3(d.Separation)}." +
            Environment.NewLine +
            $"Margin vs length: rho {StatFormat.Signed(rho)} " +
            $"[95% {StatFormat.Band(result.MarginLengthCorrelationInterval)}] ({confound}). " +
            $"Margin vs sample count: rho {StatFormat.Signed(result.MarginSampleCountCorrelation)}. " +
            $"Longest work is {spread:0.0}x the shortest." +
            (d.IsImbalanced || result.Skipped.Count > 0 || _filteredOut.Count > 0 ? Environment.NewLine : "") +
            (d.IsImbalanced
                ? $"⚠ {d.LargestAuthor} supplies {d.LargestAuthorSampleShare:P0} of the samples, so it largely " +
                  "defines both \"other\" and the scale everything is measured on. "
                : "") +
            (result.Skipped.Count > 0 ? $"Skipped: {string.Join("; ", result.Skipped)}. " : "") +
            (_filteredOut.Count > 0
                ? $"Filtered out as compilations: {string.Join(", ", _filteredOut.Take(6))}" +
                  (_filteredOut.Count > 6 ? $" and {_filteredOut.Count - 6} more." : ".")
                : "");

        _status.Text = result.HeldOutWorkExcludedFromNormalisation
            ? "Held-out work excluded from normalisation. Double-click a row to open the text."
            : "Held-out work contributed to normalisation, as saved runs do. Double-click a row to open the text.";
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExportTable(IEnumerable<WorkRow> rows)
    {
        string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        var table = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Work", "Margin", "MeanDeltaSameAuthor", "MeanDeltaOtherAuthor", "DeltaFloor", "NearestAuthor",
                "NearestSample", "CorrectAuthorRank", "SamplesMeasured", "Tokens", "Recovered"
            }
        };

        table.AddRange(rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Work.WorkTitle,
            N(r.Work.Margin),
            N(r.Work.MeanDeltaSameAuthor),
            N(r.Work.MeanDeltaOtherAuthor),
            N(r.Work.DeltaFloor),
            r.Work.NearestAuthor,
            r.Work.NearestLabel,
            r.Work.CorrectAuthorRank?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            r.Work.SamplesMeasured.ToString(CultureInfo.InvariantCulture),
            r.Work.TokenCount.ToString(CultureInfo.InvariantCulture),
            r.Work.Recovered ? "yes" : "no"
        }));

        return table;
    }

    private bool PoolHasSecondAuthor(List<WorkItem> pool) => pool.Select(w => w.AuthorName).Distinct().Count() >= 2;

    private async Task OpenGridAsync()
    {
        if (_targetAuthor.SelectedItem is not string) return;

        var grid = CreateGrid();
        if (grid == null)
        {
            await Alert.Info(this, "Pool needs a second author",
                "Tick at least one other author first - the grid runs the same validation at " +
                "different settings, and a margin needs a second author.");
            return;
        }

        grid.Show(this);
    }

    /// <summary>
    /// The grid on the same author, the same pool and the same token cache.
    ///
    /// The delegate is what matters: the grid tokenises once per accent-folding
    /// setting rather than once per cell, and because it goes through this
    /// window's cache, a grid run straight after a validation at the same
    /// folding reads nothing from the database at all.
    /// </summary>
    private ParameterGridWindow? CreateGrid()
    {
        if (_targetAuthor.SelectedItem is not string target) return null;

        var pool = BuildPool(target);
        if (!PoolHasSecondAuthor(pool)) return null;

        var summary =
            $"{target} against {string.Join(", ", pool.Select(w => w.AuthorName).Distinct().Where(a => a != target))} " +
            $"({pool.Count} works)";

        return new ParameterGridWindow(target, summary, (fold, cancellation) => Task.Run(() =>
        {
            var tokenized = new List<WorkTokens>(pool.Count);
            foreach (var w in pool)
            {
                cancellation.ThrowIfCancellationRequested();
                tokenized.Add(new WorkTokens(w.WorkId, w.AuthorName, w.WorkTitle, TokensFor(w, fold)));
            }

            return (IReadOnlyList<WorkTokens>)tokenized;
        }, cancellation));
    }

    private async Task OpenPerturbationAsync()
    {
        if (_targetAuthor.SelectedItem is not string target) return;

        if (!PoolHasSecondAuthor(BuildPool(target)))
        {
            await Alert.Info(this, "Pool needs a second author",
                "Tick at least one other author first - a perturbation series needs donor material " +
                "and something to measure the contaminated work against.");
            return;
        }

        var bench = await CreatePerturbationAsync();
        bench?.Show(this);
    }

    /// <summary>
    /// The perturbation bench on the same author and pool.
    ///
    /// Tokenises here rather than passing a delegate, because unlike the grid
    /// the perturbation series never varies accent folding - it holds every
    /// preprocessing choice fixed and moves only the contamination, which is
    /// the entire reason it can be read when a comparison of works cannot.
    /// The folding it was read at goes with it, so a saved experiment records
    /// the setting that produced it.
    /// </summary>
    private async Task<PerturbationWindow?> CreatePerturbationAsync()
    {
        if (_targetAuthor.SelectedItem is not string target) return null;

        var pool = BuildPool(target);
        if (!PoolHasSecondAuthor(pool)) return null;

        var fold = _foldAccents.IsChecked == true;
        var language = _allWorks.First(w => w.AuthorName == target).Language;

        _perturbButton.IsEnabled = false;
        _progress.IsVisible = true;
        try
        {
            var tokenized = await TokenizeAsync(pool, fold, CancellationToken.None);
            if (_closed) return null;

            _status.Text = "Ready.";
            return new PerturbationWindow(target, tokenized, fold, language);
        }
        catch (Exception ex)
        {
            if (!_closed) await Alert.Error(this, "Couldn't read the pool", ex.Message);
            return null;
        }
        finally
        {
            if (!_closed)
            {
                _perturbButton.IsEnabled = true;
                _progress.IsVisible = _cancellation != null;
            }
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (_results.SelectedItem is not WorkRow row || OnOpenWork == null) return;

        await OnOpenWork(row.Work.WorkId);
        Close();
    }

    /// <summary>
    /// A held-out work. Not recovered draws in the danger colour; recovered
    /// with its nearest sample by another author draws muted - the margin
    /// says yes and the nearest neighbour says no, and that disagreement is
    /// worth seeing.
    /// </summary>
    internal sealed record WorkRow(WorkValidation Work, bool NearestIsOther) : IMarkedRow
    {
        public string? InkKey => !Work.Recovered ? "Cc.Danger" : NearestIsOther ? "Cc.InkMuted" : null;

        public string? ShadeKey => null;
    }
}
