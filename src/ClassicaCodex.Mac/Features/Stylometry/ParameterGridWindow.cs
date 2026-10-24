using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Runs the validation sweep across a grid of preprocessing settings, so that
/// settings can be chosen for behaving well on texts whose authorship is known.
///
/// THE ORDER MATTERS AND IT IS THE WHOLE IDEA. Validate, then choose
/// parameters, then look at the disputed work. Run the other way round - try
/// settings until Rhesus looks Aeschylean - and a grid is a machine for
/// manufacturing the result you went looking for. A large enough one always
/// contains it.
///
/// WHAT TO READ. Not the recovery column. Recovery saturates on a tragic pool -
/// nineteen of nineteen at nearly every setting - so it discriminates nothing
/// and a grid sorted by it is a list of ties. The informative column is rho:
/// the cells worth using recover everything AND do not sort the works by length
/// while doing it.
///
/// The table is sorted by |rho| ascending for that reason, and the summary
/// deliberately reports a REGION rather than a winning row. A single best cell
/// in a grid of forty is usually noise, and treating one as a discovery is the
/// specific failure this bench exists to make harder.
///
/// The Mac counterpart of ParameterGridForm. The export carries every cell at
/// full precision, with the 95% band's two ends as numbers - Windows exported
/// the rounded cells.
/// </summary>
public sealed class ParameterGridWindow : FeatureWindow
{
    private readonly string _targetAuthor;
    private readonly string _poolSummary;
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<WorkTokens>>> _poolFor;

    private readonly CheckList _chunkSizes;
    private readonly CheckList _featureCounts;
    private readonly CheckBox _bothFoldings;
    private readonly CheckBox _excludeHeldOut;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly SelectableTextBlock _summary;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly DataGrid _cells;

    private List<CellRow> _rows = new();
    private IReadOnlyList<string> _exportNotes = Array.Empty<string>();
    private CancellationTokenSource? _cancellation;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    public ParameterGridWindow(
        string targetAuthor,
        string poolSummary,
        Func<bool, CancellationToken, Task<IReadOnlyList<WorkTokens>>> poolFor)
    {
        _targetAuthor = targetAuthor;
        _poolSummary = poolSummary;
        _poolFor = poolFor;

        Title = $"Stability - does the result survive a change of settings? ({targetAuthor})";
        Width = 1380;
        Height = 760;
        MinWidth = 1000;
        MinHeight = 560;

        _chunkSizes = new CheckList(200);
        foreach (var size in ParameterGridRunner.DefaultChunkSizes)
            _chunkSizes.Add(size.ToString("N0", CultureInfo.CurrentCulture), size, true);

        _featureCounts = new CheckList(170);
        foreach (var count in ParameterGridRunner.DefaultFeatureCounts)
            _featureCounts.Add(count.ToString(CultureInfo.CurrentCulture), count, true);

        // On by default. Accent folding is the setting most likely to be an
        // editorial artefact rather than an authorial one - Perseus is not
        // consistent across its editions - so a result that flips when folding
        // flips was measuring orthography, and the grid should be able to say
        // so.
        _bothFoldings = new CheckBox { Content = "Accent folding on and off", IsChecked = true };
        _excludeHeldOut = new CheckBox { Content = "Held-out work excluded" };

        _runButton = Bench.Button("Run Grid", "Play", Bench.WithChord("Validate at every ticked combination", "R"));
        _runButton.Classes.Add("accent");
        _runButton.IsDefault = true;
        _runButton.Click += (_, _) => _pending = RunAsync();

        _cancelButton = Bench.Button("Stop", "Stop", Bench.WithChord("Stop the grid", "."));
        _cancelButton.IsEnabled = false;
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        _runButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _cancelButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(_cancelButton, 2);
        buttons.Children.Add(_runButton);
        buttons.Children.Add(_cancelButton);

        var settingsColumn = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Bench.Group("Sample sizes (tokens)", _chunkSizes.View),
                Bench.Group("Most frequent words", _featureCounts.View),
                Bench.Group("Also vary", new StackPanel { Spacing = 4, Children = { _bothFoldings, _excludeHeldOut } })
            }
        };
        var leftScroll = Bench.SidePanel(settingsColumn, buttons, 284);

        _summary = Bench.Report($"Pool: {poolSummary}. Tick the settings to sweep, then run.");
        _progress = Bench.Progress();
        var notice = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel { Children = { _summary, _progress } }
        };

        _cells = BenchTable.Create();
        _cells.Columns.Add(BenchTable.Column<CellRow>("Sample", r => r.Cell.Point.ChunkSize.ToString("N0", CultureInfo.CurrentCulture), r => r.Cell.Point.ChunkSize, 70, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("MFW", r => r.Cell.Point.FeatureWordCount.ToString(CultureInfo.CurrentCulture), r => r.Cell.Point.FeatureWordCount, 56, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("Accents", r => r.Cell.Point.FoldAccents ? "folded" : "unfolded", width: 80));
        _cells.Columns.Add(BenchTable.Column<CellRow>("Recovered", r => r.Cell.Failed ? "-" : $"{r.Cell.Recovered}/{r.Cell.WorksValidated}", r => r.Cell.Failed ? null : r.Cell.RecoveryRate, 0, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("Mean margin", r => r.Cell.Failed ? "-" : StatFormat.Signed3(r.Cell.MeanMargin), r => r.Cell.Failed ? null : r.Cell.MeanMargin, 0, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("rho length", r => r.Cell.Failed ? "-" : StatFormat.Signed(r.Cell.MarginLengthCorrelation), r => r.Cell.Failed ? null : r.Cell.MarginLengthCorrelation, 0, numeric: true));

        // The band, next to the value, because forty of these in a sorted
        // column look like a gradient and are not one. Over nineteen works a
        // rho of +0.42 spans roughly [-0.04, +0.73] - wider than the whole
        // range this grid displays.
        _cells.Columns.Add(BenchTable.Column<CellRow>("95% band", r => r.Cell.Failed ? "-" : StatFormat.Band(r.Band), r => r.Cell.Failed ? null : r.Band.Low, 120, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("rho samples", r => r.Cell.Failed ? "-" : StatFormat.Signed(r.Cell.MarginSampleCountCorrelation), r => r.Cell.Failed ? null : r.Cell.MarginSampleCountCorrelation, 0, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("Spread", r => r.Cell.Failed ? "-" : r.Cell.LengthSpread.ToString("0.00", CultureInfo.CurrentCulture), r => r.Cell.Failed ? null : r.Cell.LengthSpread, 0, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("Separation", r => r.Cell.Failed ? "-" : StatFormat.Signed3(r.Cell.PoolSeparation), r => r.Cell.Failed ? null : r.Cell.PoolSeparation, 0, numeric: true));
        _cells.Columns.Add(BenchTable.Column<CellRow>("Note", r => r.Note, star: true));

        // Every cell at full precision, with the run's own notes: the band's
        // ends, the flags, and why a cell failed, so the file can be read
        // without the window.
        TableExport.Attach(
            _cells,
            () => $"stability-{targetAuthor}",
            () => ExportTable(_rows),
            () => ExportTable(_cells.SelectedItems.OfType<CellRow>()),
            () => _exportNotes);

        _status = Bench.Status();
        var cellsPane = ToolPane.Build("CONFIGURATIONS · WEAKEST LENGTH CORRELATION FIRST", _cells, footer: _status);

        var right = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        right.Children.Add(notice);
        right.Children.Add(cellsPane);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,20,*") };
        Grid.SetColumn(right, 2);
        columns.Children.Add(leftScroll);
        columns.Children.Add(right);

        var heading = Bench.Heading(
            "Does the result survive a change of settings?",
            "Validate, then choose settings, then look at the disputed work. The other way round, a grid is a machine " +
            "for manufacturing the answer you went looking for. Read rho and its band, not recovery - and prefer a " +
            "region that behaves over any single best cell.");

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 20) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(columns);
        Content = layout;

        Bench.Chord(this, Key.R, _runButton, () => _pending = RunAsync());
        Bench.Chord(this, Key.OemPeriod, _cancelButton, () => _cancellation?.Cancel());

        Closed += (_, _) =>
        {
            _closed = true;
            _cancellation?.Cancel();
        };
    }

    // For the tests.
    internal CheckList ChunkSizes => _chunkSizes;
    internal CheckList FeatureCounts => _featureCounts;
    internal CheckBox BothFoldings => _bothFoldings;
    internal IReadOnlyList<GridCell> Cells => _rows.Select(r => r.Cell).ToList();
    internal string SummaryText => _summary.Text ?? string.Empty;
    internal string StatusText => _status.Text ?? string.Empty;

    internal Task RunGridAsync() => _pending = RunAsync();

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

    private async Task RunAsync()
    {
        if (_cancellation != null) return;

        var sizes = _chunkSizes.CheckedTags.Cast<int>().ToList();
        var features = _featureCounts.CheckedTags.Cast<int>().ToList();

        if (sizes.Count == 0 || features.Count == 0)
        {
            await Alert.Info(this, "Nothing to sweep", "Tick at least one sample size and one feature count.");
            return;
        }

        var foldings = _bothFoldings.IsChecked == true ? new[] { true, false } : new[] { true };
        var points = ParameterGridRunner.Build(sizes, features, foldings, _excludeHeldOut.IsChecked == true);

        _runButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = true;
        _cells.ItemsSource = null;
        _rows = new List<CellRow>();
        _exportNotes = Array.Empty<string>();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        try
        {
            // Tokenise once per folding setting rather than once per cell. A
            // five-by-four grid over both settings is forty validations of the
            // same text; tokenising is the expensive half, and forty passes
            // through SQLite to answer one question would make the grid
            // unusable rather than merely slow.
            var pools = new Dictionary<bool, IReadOnlyList<WorkTokens>>();
            foreach (var fold in foldings)
            {
                _status.Text = fold
                    ? "Reading the pool (accents folded)…"
                    : "Reading the pool (accents unfolded)…";
                pools[fold] = await _poolFor(fold, token);
                if (_closed) return;
            }

            var progress = new Progress<(int Done, int Total, GridPoint Point)>(p =>
            {
                if (_closed) return;
                _status.Text = $"Configuration {p.Done + 1} of {p.Total}: {p.Point.Describe()}…";
                _progress.IsIndeterminate = false;
                _progress.Maximum = p.Total;
                _progress.Value = p.Done;
            });
            IProgress<(int, int, GridPoint)> report = progress;

            var cells = await Task.Run(() => ParameterGridRunner.Run(
                fold => pools[fold],
                _targetAuthor,
                points,
                (done, total, point) => report.Report((done, total, point)),
                token), token);

            if (_closed) return;

            Show(cells);
            _status.Text = $"{cells.Count} configurations. Sorted by length correlation, weakest first.";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _status.Text = "Grid failed - see message.";
            await Alert.Error(this, "Grid failed", ex.Message);
        }
        finally
        {
            if (!_closed)
            {
                _runButton.IsEnabled = true;
                _cancelButton.IsEnabled = false;
                _progress.IsVisible = false;
            }

            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Show(IReadOnlyList<GridCell> cells)
    {
        // Sorted by |rho| ascending, not by recovery. Recovery saturates and
        // sorting on it produces a list of ties; the question the grid is here
        // to answer is which settings measure the author rather than the
        // length.
        _rows = cells
            .OrderBy(c => c.Failed)
            .ThenBy(c => Math.Abs(c.MarginLengthCorrelation))
            .Select(c => new CellRow(c, ValidationResult.FisherInterval(c.MarginLengthCorrelation, c.WorksValidated)))
            .ToList();
        _cells.ItemsSource = _rows;

        var summary = ParameterGridRunner.Summarise(cells);
        _summary.Text =
            $"Pool: {_poolSummary}." + Environment.NewLine +
            summary + Environment.NewLine +
            "Sorted by length correlation, weakest first - but read the band beside it before " +
            "treating that order as a gradient.";

        _exportNotes = new[]
        {
            $"Classica Codex parameter grid - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Target author: {_targetAuthor}",
            $"Pool: {_poolSummary}",
            summary,
            "Sorted by length correlation, weakest first. Read the 95% band before treating that " +
            "order as a gradient - on a nineteen-work corpus the whole visible spread can sit " +
            "inside the estimation error of one cell."
        };
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExportTable(IEnumerable<CellRow> rows)
    {
        string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        var table = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "SampleSize", "MFW", "Accents", "HeldOutExcluded", "Recovered", "WorksValidated", "RecoveryRate",
                "MeanMargin", "RhoLength", "RhoLengthBandLow", "RhoLengthBandHigh", "RhoSampleCount",
                "LengthSpread", "PoolSeparation", "SampleCount", "DroppedWorks", "Trustworthy", "Note", "Error"
            }
        };

        foreach (var r in rows)
        {
            var c = r.Cell;
            table.Add(c.Failed
                ? new[]
                {
                    c.Point.ChunkSize.ToString(CultureInfo.InvariantCulture),
                    c.Point.FeatureWordCount.ToString(CultureInfo.InvariantCulture),
                    c.Point.FoldAccents ? "folded" : "unfolded",
                    c.Point.ExcludeHeldOutFromNormalisation ? "yes" : "no",
                    "", "", "", "", "", "", "", "", "", "", "", "", "no", "", c.Error ?? ""
                }
                : new[]
                {
                    c.Point.ChunkSize.ToString(CultureInfo.InvariantCulture),
                    c.Point.FeatureWordCount.ToString(CultureInfo.InvariantCulture),
                    c.Point.FoldAccents ? "folded" : "unfolded",
                    c.Point.ExcludeHeldOutFromNormalisation ? "yes" : "no",
                    c.Recovered.ToString(CultureInfo.InvariantCulture),
                    c.WorksValidated.ToString(CultureInfo.InvariantCulture),
                    N(c.RecoveryRate),
                    N(c.MeanMargin),
                    N(c.MarginLengthCorrelation),
                    N(r.Band.Low),
                    N(r.Band.High),
                    N(c.MarginSampleCountCorrelation),
                    N(c.LengthSpread),
                    N(c.PoolSeparation),
                    c.SampleCount.ToString(CultureInfo.InvariantCulture),
                    c.DroppedWorks ? "yes" : "no",
                    c.Trustworthy ? "yes" : "no",
                    r.Note,
                    ""
                });
        }

        return table;
    }

    /// <summary>
    /// One configuration. Trustworthy cells draw in verdigris; cells that
    /// dropped works or had too little length spread for rho to mean anything
    /// draw muted, as failed ones do.
    /// </summary>
    internal sealed record CellRow(GridCell Cell, (double Low, double High) Band) : IMarkedRow
    {
        /// <summary>
        /// The note column exists so that a low rho cannot be read as good
        /// news without its caveat sitting on the same row.
        /// </summary>
        public string Note =>
            Cell.Failed ? (Cell.Error!.Length > 60 ? Cell.Error[..60] + "..." : Cell.Error)
            : Cell.DroppedWorks ? $"tested {Cell.WorksValidated} works - shortest dropped"
            : Cell.LengthSpread < 1.8 ? "little length spread - rho uninformative"
            : Cell.Trustworthy ? "recovers, no length effect"
            : string.Empty;

        public string? InkKey =>
            Cell.Failed ? "Cc.InkMuted"
            : Cell.Trustworthy ? "Cc.Verdigris"
            : Cell.DroppedWorks || Cell.LengthSpread < 1.8 ? "Cc.InkMuted"
            : null;

        public string? ShadeKey => null;
    }
}
