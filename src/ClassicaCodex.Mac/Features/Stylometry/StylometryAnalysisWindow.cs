using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Compares saved stylometric runs against each other.
///
/// A single Delta run says very little. The useful questions are comparative:
/// does this work sit where an author's undisputed works sit, and does that
/// answer survive a change of preprocessing? Both need many runs side by side,
/// which is what this window is for.
///
/// DEPTH TO FIRST OUTSIDER IS NOT AN ATTRIBUTION MEASURE, and the Windows
/// comment on this form used to say it was - that it "proved far more stable"
/// than nearest-neighbour identity, on the strength of holding its band across
/// feature counts and accent folding. Held against sample size instead, it
/// collapsed. Across the eighteen undisputed Euripides plays plus Rhesus,
/// depth moved by up to twenty ranks for a single work on a 500-token change
/// in sample size: Heracleidae read 4, 24 and 10; Hippolytus 13, 21, 8.
///
/// The reason is structural. Depth is a rank position, and rank positions
/// depend on what else is in the pool. On whole works it tracked text length
/// at rho about 0.58 - the four shortest works were the four shallowest. Made
/// to use equal-size samples, it tracked sample COUNT instead: mean depth 6.5
/// for one-sample works, 12.9 for two, 20.0 for three. A longer work
/// contributes more samples, those samples pad the top of everyone's ranking,
/// and the confound returns wearing a different hat. The quantity being
/// measured is length either way.
///
/// It is still shown, because watching it move is instructive and because the
/// columns below are what demonstrated the problem. Do not read it as evidence
/// about authorship. Delta floor - the distance to the nearest neighbour - is
/// the better measure and does not inherit pool composition: at fixed sample
/// size rho(tokens, floor) is -0.21 against 0.58 for depth.
///
/// docs/stylometry-notes.md has the full working.
///
/// The Mac counterpart of StylometryAnalysisForm. Every tab's table exports,
/// at full precision; on Windows only the first does. The length tab follows
/// the author filter as the first tab does, where on Windows it kept the
/// author chosen when the profile was last changed.
/// </summary>
public sealed class StylometryAnalysisWindow : FeatureWindow
{
    private const string AllAuthors = "(all authors)";

    private readonly StylometryRunRepository _runRepo = new();
    private readonly long? _focusRunId;

    private readonly ComboBox _profileCombo;
    private readonly ComboBox _authorCombo;
    private readonly TabControl _tabs;
    private readonly DataGrid _metricsGrid;
    private readonly SelectableTextBlock _summaryLabel;
    private readonly DataGrid _stabilityGrid;
    private readonly DataGrid _lengthGrid;
    private readonly SelectableTextBlock _lengthSummary;

    private List<StylometrySettings> _profiles = new();
    private List<StylometryRunMetrics> _currentMetrics = new();
    private List<StylometryRunMetrics> _allMetrics = new();
    private List<StylometrySettings> _stabilityProfiles = new();
    private List<MetricRow> _metricRows = new();
    private List<StabilityRow> _stabilityRows = new();
    private List<LengthRow> _lengthRows = new();
    private double? _lengthRho;

    /// <summary>
    /// The rows as rendered, at full precision, captured while they are built.
    ///
    /// Recomputing them for the export would mean a second copy of the z-score
    /// logic, which depends on the author filter and would quietly diverge from
    /// the screen the first time either changed. Filling it here costs nothing
    /// and cannot drift.
    /// </summary>
    private readonly List<IReadOnlyList<string>> _metricExport = new();

    private StylometryRunMetrics? _focusRun;
    private bool _loadingProfiles;
    private bool _fillingAuthors;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    public StylometryAnalysisWindow(long? focusRunId = null)
    {
        _focusRunId = focusRunId;
        Title = "Stylometry - Compare Saved Runs";
        Width = 1180;
        Height = 780;
        MinWidth = 900;
        MinHeight = 560;

        _profileCombo = new ComboBox { MinWidth = 420, MaxDropDownHeight = 420 };
        _profileCombo.SelectionChanged += (_, _) =>
        {
            if (!_loadingProfiles) _pending = RefreshAsync();
        };

        _authorCombo = new ComboBox { MinWidth = 220, MaxDropDownHeight = 420 };
        _authorCombo.SelectionChanged += (_, _) =>
        {
            if (_fillingAuthors) return;
            RenderMetrics();
            RenderLengthConfound();
        };

        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock { Text = "Settings profile:", VerticalAlignment = VerticalAlignment.Center },
                _profileCombo,
                new TextBlock { Text = "Author:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) },
                _authorCombo
            }
        };

        // --- Tab 1: reference distribution -----------------------------------
        _metricsGrid = BenchTable.Create(framed: true);
        _metricsGrid.Columns.Add(BenchTable.Column<MetricRow>("Work", r => r.Work, star: true));
        _metricsGrid.Columns.Add(BenchTable.Column<MetricRow>("Depth to first outsider", r => r.DepthText, r => r.Metrics.DepthToFirstOutsider ?? int.MaxValue, 170, numeric: true));
        _metricsGrid.Columns.Add(BenchTable.Column<MetricRow>("z-score", r => r.Z.HasValue ? r.Z.Value.ToString("F2", CultureInfo.CurrentCulture) : "-", r => r.Z, 84, numeric: true));
        _metricsGrid.Columns.Add(BenchTable.Column<MetricRow>("Delta floor", r => r.Metrics.DeltaFloor.ToString("F3", CultureInfo.CurrentCulture), r => r.Metrics.DeltaFloor, 96, numeric: true));
        _metricsGrid.Columns.Add(BenchTable.Column<MetricRow>("Purity @10", r => r.Metrics.AuthorPurityAt10.ToString("P0", CultureInfo.CurrentCulture), r => r.Metrics.AuthorPurityAt10, 92, numeric: true));
        _metricsGrid.Columns.Add(BenchTable.Column<MetricRow>("Nearest neighbour", r => $"{r.Metrics.NearestAuthor}, {Bench.WithoutAuthor(r.Metrics.NearestAuthor, r.Metrics.NearestTitle)}", width: 1.6, star: true));

        // The saved runs as a table. Every validation bench could already
        // export and this could not - the one screen showing the results those
        // benches exist to check.
        //
        // From the metrics rather than the cells, because the screen rounds
        // hard: the Delta floor to three decimals, purity to a whole percent,
        // z to two. Handing an analyst those and letting them recompute is how
        // two slightly different answers to the same question get into
        // circulation.
        TableExport.Attach(
            _metricsGrid,
            () => $"stylometry-{SelectedAuthor ?? "all"}",
            () => MetricTable(_metricExport),
            () => MetricTable(_metricsGrid.SelectedItems.OfType<MetricRow>().Select(r => r.Export)),
            () => new[]
            {
                $"Classica Codex stylometry runs - {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"Settings profile: {SelectedProfile?.Describe() ?? "(none)"}",
                $"Author filter: {SelectedAuthor ?? "(all)"}",
                "z is the work's depth against the mean depth of the other works shown, so it " +
                "depends on the filter above - a different selection gives a different z for the " +
                "same run. Empty where a depth could not be computed.",
                "See docs/stylometry-notes.md for what Delta can and cannot tell you; on this " +
                "corpus it cannot reliably detect a second hand contributing less than about a " +
                "third of a work."
            });

        _summaryLabel = Bench.Report();
        var distributionTab = TabPage(null, _metricsGrid, _summaryLabel);

        // --- Tab 2: stability across settings --------------------------------
        _stabilityGrid = BenchTable.Create(framed: true);
        TableExport.Attach(
            _stabilityGrid,
            () => "stylometry-stability",
            () => StabilityTable(_stabilityRows),
            () => StabilityTable(_stabilityGrid.SelectedItems.OfType<StabilityRow>()),
            () => new[]
            {
                $"Classica Codex stylometry runs, stability across settings - {DateTime.Now:yyyy-MM-dd HH:mm}",
                "Depth to first outsider for each work under every saved settings profile; Spread is the " +
                "largest less the smallest. Empty where a work has no run at a profile.",
                "Depth is a rank and inherits the composition of the pool - see docs/stylometry-notes.md."
            });

        var stabilityHelp =
            "Depth to first outsider for each work under every saved settings profile. " +
            "A row that holds steady across columns survived THIS change; a row that swings " +
            "was about the preprocessing. Steadiness here is necessary and not sufficient - " +
            "depth holds its band across feature counts and still moves by up to twenty ranks " +
            "on a 500-token change of sample size, so vary sample size too before believing a row.";
        var stabilityTab = TabPage(stabilityHelp, _stabilityGrid, null);

        // --- Tab 3: length confound ------------------------------------------
        _lengthGrid = BenchTable.Create(framed: true);
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Work", r => r.Work, star: true));
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Tokens", r => r.Tokens.ToString("N0", CultureInfo.CurrentCulture), r => r.Tokens, 96, numeric: true));
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Depth", r => r.Depth.ToString(CultureInfo.CurrentCulture), r => r.Depth, 76, numeric: true));
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Length rank", r => r.LengthRank.ToString("F1", CultureInfo.CurrentCulture), r => r.LengthRank, 100, numeric: true));
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Depth rank", r => r.DepthRank.ToString("F1", CultureInfo.CurrentCulture), r => r.DepthRank, 100, numeric: true));
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Rank gap", r => r.Gap.ToString("F1", CultureInfo.CurrentCulture), r => r.Gap, 90, numeric: true));
        _lengthGrid.Columns.Add(BenchTable.Column<LengthRow>("Delta floor", r => r.DeltaFloor.ToString("F3", CultureInfo.CurrentCulture), r => r.DeltaFloor, 96, numeric: true));
        TableExport.Attach(
            _lengthGrid,
            () => $"stylometry-length-{SelectedAuthor ?? "all"}",
            () => LengthTable(_lengthRows),
            () => LengthTable(_lengthGrid.SelectedItems.OfType<LengthRow>()),
            () => new[]
            {
                $"Classica Codex stylometry runs, length confound - {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"Settings profile: {SelectedProfile?.Describe() ?? "(none)"}",
                $"Author filter: {SelectedAuthor ?? "(all)"}",
                _lengthRho.HasValue
                    ? $"Spearman's rho between length and depth = {_lengthRho.Value.ToString("R", CultureInfo.InvariantCulture)} (n = {_lengthRows.Count}). " +
                      "Ranks are averaged over ties. Positive rho = longer texts rank deeper - the confound."
                    : "Too few runs carry a token count for a correlation.",
                "A correlation over a couple of dozen works can tell you a confound is present; it cannot " +
                "tell you it is absent."
            });

        var lengthHelp =
            "Depth to first outsider against text length. Shorter texts give noisier " +
            "relative-frequency estimates, which inflates Delta against everything and lets " +
            "other authors rise earlier in the ranking. On this corpus depth DOES correlate " +
            "with length - rho about 0.58 on whole works, and with sample count once samples " +
            "are equalised - so treat a correlation here as confirmation rather than news, " +
            "and treat its absence as the surprising result worth checking.";
        _lengthSummary = Bench.Report();
        var lengthTab = TabPage(lengthHelp, _lengthGrid, _lengthSummary);

        _tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Reference distribution", Content = distributionTab },
                new TabItem { Header = "Stability across settings", Content = stabilityTab },
                new TabItem { Header = "Length confound", Content = lengthTab }
            }
        };

        var heading = Bench.Heading(
            "Compare saved runs",
            "A single Delta run says very little. Does a work sit where its author's undisputed works sit, and " +
            "does that survive a change of preprocessing? Depth to first outsider is shown because watching it " +
            "move is instructive - it is not evidence about authorship. Read the Delta floor.");

        var top = new StackPanel { Children = { heading, filters } };
        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 20) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(_tabs);
        Content = layout;

        // ⌘1, ⌘2, ⌘3 for the tabs, as a Mac preferences window has them.
        KeyDown += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(WordStudyWindow.CommandKey)) return;
            var index = e.Key switch { Avalonia.Input.Key.D1 => 0, Avalonia.Input.Key.D2 => 1, Avalonia.Input.Key.D3 => 2, _ => -1 };
            if (index < 0) return;
            _tabs.SelectedIndex = index;
            e.Handled = true;
        };

        Opened += (_, _) => _pending = LoadProfilesAsync();
        Closed += (_, _) => _closed = true;
    }

    // For the tests.
    internal IReadOnlyList<StylometrySettings> Profiles => _profiles;
    internal ComboBox ProfileCombo => _profileCombo;
    internal ComboBox AuthorCombo => _authorCombo;
    internal TabControl Tabs => _tabs;
    internal IReadOnlyList<MetricRow> MetricRows => _metricRows;
    internal IReadOnlyList<StabilityRow> StabilityRows => _stabilityRows;
    internal IReadOnlyList<LengthRow> LengthRows => _lengthRows;
    internal DataGrid MetricsGrid => _metricsGrid;
    internal string SummaryText => _summaryLabel.Text ?? string.Empty;
    internal string LengthSummaryText => _lengthSummary.Text ?? string.Empty;

    /// <summary>Waits until every load started so far has finished.</summary>
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

    private StylometrySettings? SelectedProfile =>
        _profileCombo.SelectedIndex >= 0 && _profileCombo.SelectedIndex < _profiles.Count ? _profiles[_profileCombo.SelectedIndex] : null;

    /// <summary>The author chosen, or null for all of them.</summary>
    private string? SelectedAuthor =>
        _authorCombo.SelectedItem is string a && a != AllAuthors ? a : null;

    /// <summary>A tab's page: an optional help line, the table, an optional report under it.</summary>
    private static Control TabPage(string? help, DataGrid grid, SelectableTextBlock? report)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };

        if (help != null)
        {
            var helpText = new TextBlock { Text = help, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 10) };
            DockPanel.SetDock(helpText, Dock.Top);
            dock.Children.Add(helpText);
        }

        if (report != null)
        {
            var reportBox = new Border
            {
                Classes = { "notice" },
                Padding = new Thickness(14, 10),
                Margin = new Thickness(0, 12, 0, 0),
                Child = new ScrollViewer { Content = report, MaxHeight = 210 }
            };
            DockPanel.SetDock(reportBox, Dock.Bottom);
            dock.Children.Add(reportBox);
        }

        dock.Children.Add(grid);
        return dock;
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            _profiles = await Task.Run(() => _runRepo.GetSettingsProfilesAsync());

            if (_focusRunId.HasValue)
            {
                _focusRun = (await Task.Run(() => _runRepo.GetRunMetricsAsync()))
                    .FirstOrDefault(m => m.RunId == _focusRunId.Value);
                Title += $" — Saved run #{_focusRunId.Value}";
            }

            if (_closed) return;

            _loadingProfiles = true;
            _profileCombo.ItemsSource = _profiles.Select(p => p.Describe()).ToList();

            if (_profiles.Count > 0)
            {
                var profileIndex = _focusRun == null ? -1 : _profiles.IndexOf(_focusRun.Settings);
                _profileCombo.SelectedIndex = profileIndex >= 0 ? profileIndex : 0;
                _loadingProfiles = false;
                await RefreshAsync();
            }
            else
            {
                _loadingProfiles = false;
                _summaryLabel.Text = "No saved runs yet. Run an analysis in the Stylometry window and save it, " +
                                     "or use Run Whole Author to batch an author's works.";
            }

            await BuildStabilityMatrixAsync();

            if (_focusRunId.HasValue && _focusRun == null)
                _summaryLabel.Text = $"Saved run #{_focusRunId.Value} is no longer available. " +
                                     "The remaining saved runs are shown instead.";
        }
        catch (Exception ex)
        {
            _loadingProfiles = false;
            if (!_closed) _summaryLabel.Text = $"Couldn't read the saved runs: {ex.Message}";
        }
    }

    private async Task RefreshAsync()
    {
        if (SelectedProfile is not { } settings) return;

        _currentMetrics = await Task.Run(() => _runRepo.GetRunMetricsAsync(settingsFilter: settings));
        if (_closed) return;

        var authors = _currentMetrics
            .Select(m => m.TargetAuthorName)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        var previous = _focusRun != null && _currentMetrics.Any(m => m.RunId == _focusRun.RunId)
            ? _focusRun.TargetAuthorName
            : _authorCombo.SelectedItem as string;

        var items = new List<string> { AllAuthors };
        items.AddRange(authors);

        _fillingAuthors = true;
        _authorCombo.ItemsSource = items;
        var restored = previous != null ? items.IndexOf(previous) : -1;
        _authorCombo.SelectedIndex = restored >= 0 ? restored : 0;
        _fillingAuthors = false;

        RenderMetrics();
        RenderLengthConfound();
    }

    /// <summary>
    /// Renders the per-work metrics and, when a single author is selected,
    /// the reference distribution over that author's works.
    ///
    /// The z-score for each work is computed leave-one-out: the mean and
    /// standard deviation come from the OTHER works by that author, not from
    /// all of them. Including a suspected outlier in the distribution it is
    /// being tested against pulls the mean toward it and shrinks its own
    /// z-score - the test would partly hide what it is looking for.
    /// </summary>
    private void RenderMetrics()
    {
        _metricExport.Clear();
        _metricRows = new List<MetricRow>();
        _metricsGrid.ItemsSource = null;

        var selectedAuthor = SelectedAuthor;
        var filtered = selectedAuthor == null
            ? _currentMetrics
            : _currentMetrics.Where(m => m.TargetAuthorName == selectedAuthor).ToList();

        if (filtered.Count == 0)
        {
            _summaryLabel.Text = "No runs for this selection.";
            return;
        }

        var depths = filtered
            .Where(m => m.DepthToFirstOutsider.HasValue)
            .Select(m => (double)m.DepthToFirstOutsider!.Value)
            .ToList();

        MetricRow? focus = null;

        foreach (var m in filtered.OrderBy(m => m.DepthToFirstOutsider ?? int.MaxValue))
        {
            double? z = null;

            if (m.DepthToFirstOutsider.HasValue && depths.Count >= 3)
            {
                // Leave-one-out: exclude this work from its own reference set.
                var others = filtered
                    .Where(o => o.RunId != m.RunId && o.DepthToFirstOutsider.HasValue)
                    .Select(o => (double)o.DepthToFirstOutsider!.Value)
                    .ToList();

                if (others.Count >= 2)
                {
                    var mean = others.Average();
                    var sd = Math.Sqrt(others.Select(v => (v - mean) * (v - mean)).Average());
                    if (sd > 1e-9) z = (m.DepthToFirstOutsider.Value - mean) / sd;
                }
            }

            var export = new[]
            {
                m.TargetAuthorName,
                m.TargetWorkTitle,
                m.DepthToFirstOutsider?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                z?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
                m.DeltaFloor.ToString("R", CultureInfo.InvariantCulture),
                m.AuthorPurityAt10.ToString("R", CultureInfo.InvariantCulture),
                m.NearestAuthor,
                m.NearestTitle
            };

            // Two standard deviations below the leave-one-out mean is a
            // conventional flag, not a verdict. It marks a work worth looking
            // at, and with a dozen or so works one flag is expected by chance.
            var row = new MetricRow(m, z, export, Flagged: z.HasValue && z.Value <= -2.0);
            _metricRows.Add(row);
            _metricExport.Add(export);

            if (_focusRunId == m.RunId) focus = row;
        }

        _metricsGrid.ItemsSource = _metricRows;
        if (focus != null)
        {
            _metricsGrid.SelectedItem = focus;
            _metricsGrid.ScrollIntoView(focus, null);
        }

        if (depths.Count >= 3)
        {
            var mean = depths.Average();
            var sd = Math.Sqrt(depths.Select(v => (v - mean) * (v - mean)).Average());
            var floors = filtered.Select(m => m.DeltaFloor).ToList();

            _summaryLabel.Text =
                $"Reference distribution over {depths.Count} runs" +
                (selectedAuthor != null ? $" for {selectedAuthor}" : "") + ":" +
                Environment.NewLine +
                $"  Depth to first outsider - mean {mean:F1}, sd {sd:F1}, range {depths.Min():F0} to {depths.Max():F0}" +
                Environment.NewLine +
                $"  Delta floor - mean {floors.Average():F3}, range {floors.Min():F3} to {floors.Max():F3}" +
                Environment.NewLine + Environment.NewLine +
                "z-scores are leave-one-out: each work is scored against the others, not against a " +
                "distribution it is itself part of. Rows shaded pink sit two or more standard deviations " +
                "below the mean - worth a closer look, not a conclusion. With a dozen works, one flag is " +
                "roughly what chance alone produces." +
                Environment.NewLine + Environment.NewLine +
                "A shallow depth means the analysis stopped agreeing with the attribution early. It does " +
                "not distinguish a different author from a short text, an unusual genre, or an edition " +
                "prepared to different conventions - and on this corpus, length is what it usually turns " +
                "out to be. Depth moved by up to twenty ranks for one work on a 500-token change of sample " +
                "size. Read the Delta floor row instead: it is a distance rather than a rank, so it does " +
                "not inherit the composition of the pool. See docs/stylometry-notes.md.";
        }
        else
        {
            _summaryLabel.Text =
                $"{filtered.Count} run(s) shown. At least three runs for one author are needed before a " +
                "reference distribution means anything - batch that author's works from the Stylometry window.";
        }
    }

    /// <summary>
    /// Tests whether depth to first outsider is tracking text length.
    ///
    /// Spearman's rho rather than Pearson's r: the relationship is expected to
    /// be monotonic but not linear (doubling a short text helps its frequency
    /// estimates far more than doubling an already-long one), and rho does not
    /// assume a straight line. It is also unbothered by the one or two extreme
    /// lengths that a small corpus of plays inevitably contains.
    ///
    /// Ranks are averaged over ties, which matters here: depth is a small
    /// integer and ties are common.
    ///
    /// A strong POSITIVE rho means long texts get deep rankings and short texts
    /// shallow ones - the confound. Sign is easy to get backwards, so it is
    /// spelled out in the summary rather than left to the reader.
    /// </summary>
    private void RenderLengthConfound()
    {
        _lengthRows = new List<LengthRow>();
        _lengthGrid.ItemsSource = null;
        _lengthRho = null;

        var usable = _currentMetrics
            .Where(m => m.TargetTokenCount.HasValue && m.DepthToFirstOutsider.HasValue)
            .ToList();

        var selectedAuthor = SelectedAuthor;
        if (selectedAuthor != null)
            usable = usable.Where(m => m.TargetAuthorName == selectedAuthor).ToList();

        if (usable.Count < 5)
        {
            _lengthSummary.Text =
                $"{usable.Count} run(s) carry a token count. Runs saved before the token-count column " +
                "was added have none and are excluded rather than counted as zero - re-run the batch " +
                "to populate them. At least five are needed before a correlation means anything.";
            return;
        }

        var lengthRank = AverageRanks(usable.Select(m => (double)m.TargetTokenCount!.Value).ToList());
        var depthRank = AverageRanks(usable.Select(m => (double)m.DepthToFirstOutsider!.Value).ToList());

        var n = usable.Count;
        var meanL = lengthRank.Average();
        var meanD = depthRank.Average();
        var cov = 0d; var varL = 0d; var varD = 0d;
        for (var i = 0; i < n; i++)
        {
            var dl = lengthRank[i] - meanL;
            var dd = depthRank[i] - meanD;
            cov += dl * dd; varL += dl * dl; varD += dd * dd;
        }
        var rho = (varL > 1e-9 && varD > 1e-9) ? cov / Math.Sqrt(varL * varD) : 0d;
        _lengthRho = rho;

        var order = Enumerable.Range(0, n).OrderBy(i => usable[i].TargetTokenCount).ToList();
        foreach (var i in order)
        {
            var m = usable[i];
            var gap = Math.Abs(lengthRank[i] - depthRank[i]);

            // Works whose depth rank is far from their length rank are the ones
            // length does NOT explain - the residuals, and the only rows where
            // an authorship reading is even available.
            _lengthRows.Add(new LengthRow(
                $"{m.TargetAuthorName}, {m.TargetWorkTitle}",
                m.TargetTokenCount!.Value,
                m.DepthToFirstOutsider!.Value,
                lengthRank[i],
                depthRank[i],
                gap,
                m.DeltaFloor,
                Residual: gap >= n / 3.0));
        }

        _lengthGrid.ItemsSource = _lengthRows;

        string verdict;
        if (rho >= 0.7)
            verdict = "STRONG. Depth is largely a function of text length. A shallow depth is what a " +
                      "short text looks like, and reading it as weak authorial signal is not supported.";
        else if (rho >= 0.4)
            verdict = "MODERATE. Length explains part of the variation. Depth cannot be read directly - " +
                      "length has to be controlled for first, by chunking every text to a common size " +
                      "and re-running.";
        else if (rho > -0.4)
            verdict = "WEAK. Length does not account for the variation, so depth is measuring something " +
                      "else. Whether that something is authorship is a separate question this does not " +
                      "settle.";
        else
            verdict = "NEGATIVE, which is the wrong direction for the length confound and suggests a " +
                      "different effect. Check the token counts before trusting this.";

        _lengthSummary.Text =
            $"Spearman's rho between length and depth = {rho:F3}  (n = {n})" +
            Environment.NewLine + Environment.NewLine +
            verdict +
            Environment.NewLine + Environment.NewLine +
            "Positive rho = longer texts rank deeper, shorter texts shallower - the confound." +
            Environment.NewLine +
            "Green rows are works whose depth rank is far from their length rank. Those are the " +
            "residuals: the cases length does not explain, and the only ones where an authorship " +
            "reading is available at all." +
            Environment.NewLine + Environment.NewLine +
            "This is a correlation over a couple of dozen works. It can tell you a confound is " +
            "present; it cannot tell you it is absent.";
    }

    /// <summary>
    /// Ranks smallest to largest, averaging over ties.
    ///
    /// Tie handling is not optional here - depth is a small integer over twenty
    /// works, so ties are the normal case. Assigning them arbitrary distinct
    /// ranks would manufacture correlation out of list order.
    /// </summary>
    internal static List<double> AverageRanks(List<double> values)
    {
        var n = values.Count;
        var idx = Enumerable.Range(0, n).OrderBy(i => values[i]).ToList();
        var ranks = new double[n];

        var i2 = 0;
        while (i2 < n)
        {
            var j = i2;
            while (j + 1 < n && Math.Abs(values[idx[j + 1]] - values[idx[i2]]) < 1e-9) j++;

            var avg = (i2 + j) / 2.0 + 1;
            for (var k = i2; k <= j; k++) ranks[idx[k]] = avg;

            i2 = j + 1;
        }

        return ranks.ToList();
    }

    /// <summary>
    /// Builds the works-by-settings matrix of depth-to-first-outsider.
    ///
    /// Runs are grouped by settings profile across the columns, so a work's row
    /// reads as its behaviour under each preprocessing choice. Spread is
    /// reported per row: a low spread means the reading is a property of the
    /// text, a high one means it is a property of the tokeniser.
    /// </summary>
    private async Task BuildStabilityMatrixAsync()
    {
        _stabilityGrid.Columns.Clear();
        _stabilityRows = new List<StabilityRow>();
        _stabilityGrid.ItemsSource = null;

        _allMetrics = await Task.Run(() => _runRepo.GetRunMetricsAsync());
        if (_closed || _allMetrics.Count == 0) return;

        _stabilityProfiles = _allMetrics
            .Select(m => m.Settings)
            .DistinctBy(s => s.ProfileKey)
            .OrderBy(s => s.AlgorithmVersion)
            .ThenBy(s => s.FeatureWordCount)
            .ToList();

        _stabilityGrid.Columns.Add(BenchTable.Column<StabilityRow>("Work", r => r.Work, width: 280));
        for (var p = 0; p < _stabilityProfiles.Count; p++)
        {
            var index = p;
            _stabilityGrid.Columns.Add(BenchTable.Column<StabilityRow>(
                _stabilityProfiles[p].ProfileKey,
                r => r.Depths[index]?.ToString(CultureInfo.CurrentCulture) ?? "-",
                r => r.Depths[index],
                190, numeric: true));
        }
        _stabilityGrid.Columns.Add(BenchTable.Column<StabilityRow>("Spread", r => r.Spread?.ToString("F0", CultureInfo.CurrentCulture) ?? "-", r => r.Spread, 80, numeric: true));

        var byWork = _allMetrics
            .GroupBy(m => $"{m.TargetAuthorName}, {m.TargetWorkTitle}")
            .OrderBy(g => g.Key);

        foreach (var group in byWork)
        {
            var depths = _stabilityProfiles
                .Select(p => group.FirstOrDefault(m => m.Settings.ProfileKey == p.ProfileKey)?.DepthToFirstOutsider)
                .ToList();
            var values = depths.Where(d => d.HasValue).Select(d => (double)d!.Value).ToList();

            // Spread only means something with at least two profiles to compare.
            double? spread = values.Count >= 2 ? values.Max() - values.Min() : null;

            // A work whose depth doubles between settings has not been measured
            // yet - it has been measured twice, differently.
            var unstable = values.Count >= 2 && values.Max() - values.Min() > values.Min();

            _stabilityRows.Add(new StabilityRow(group.Key, depths, spread, unstable));
        }

        _stabilityGrid.ItemsSource = _stabilityRows;
    }

    private static IReadOnlyList<IReadOnlyList<string>> MetricTable(IEnumerable<IReadOnlyList<string>> rows)
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Author", "Work", "DepthToFirstOutsider", "z", "DeltaFloor", "AuthorPurityAt10", "NearestAuthor", "NearestWork" }
        };
        table.AddRange(rows);
        return table;
    }

    private IReadOnlyList<IReadOnlyList<string>> StabilityTable(IEnumerable<StabilityRow> rows)
    {
        var header = new List<string> { "Work" };
        header.AddRange(_stabilityProfiles.Select(p => p.ProfileKey));
        header.Add("Spread");

        var table = new List<IReadOnlyList<string>> { header };
        foreach (var r in rows)
        {
            var line = new List<string> { r.Work };
            line.AddRange(r.Depths.Select(d => d?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            line.Add(r.Spread?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty);
            table.Add(line);
        }

        return table;
    }

    private static IReadOnlyList<IReadOnlyList<string>> LengthTable(IEnumerable<LengthRow> rows)
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Work", "Tokens", "DepthToFirstOutsider", "LengthRank", "DepthRank", "RankGap", "DeltaFloor" }
        };

        table.AddRange(rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Work,
            r.Tokens.ToString(CultureInfo.InvariantCulture),
            r.Depth.ToString(CultureInfo.InvariantCulture),
            r.LengthRank.ToString("R", CultureInfo.InvariantCulture),
            r.DepthRank.ToString("R", CultureInfo.InvariantCulture),
            r.Gap.ToString("R", CultureInfo.InvariantCulture),
            r.DeltaFloor.ToString("R", CultureInfo.InvariantCulture)
        }));

        return table;
    }

    internal sealed record MetricRow(StylometryRunMetrics Metrics, double? Z, IReadOnlyList<string> Export, bool Flagged) : IMarkedRow
    {
        public string Work => $"{Metrics.TargetAuthorName}, {Metrics.TargetWorkTitle}";

        public string DepthText => Metrics.DepthToFirstOutsider?.ToString(CultureInfo.CurrentCulture) ?? "(all same author)";

        public string? InkKey => null;

        public string? ShadeKey => Flagged ? "Cc.Danger" : null;
    }

    internal sealed record StabilityRow(string Work, IReadOnlyList<int?> Depths, double? Spread, bool Unstable) : IMarkedRow
    {
        public string? InkKey => null;

        public string? ShadeKey => Unstable ? "Cc.Gold" : null;
    }

    internal sealed record LengthRow(
        string Work, int Tokens, int Depth, double LengthRank, double DepthRank, double Gap, double DeltaFloor, bool Residual) : IMarkedRow
    {
        public string? InkKey => null;

        public string? ShadeKey => Residual ? "Cc.Verdigris" : null;
    }
}
