using ClassicaCodex.Core.Stylometry;

namespace ClassicaCodex.UI;

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
/// </summary>
public class ParameterGridForm : Form
{
    private readonly string _targetAuthor;
    private readonly string _poolSummary;
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<WorkTokens>>> _poolFor;

    private readonly CheckedListBox _chunkSizes;
    private readonly CheckedListBox _featureCounts;
    private readonly CheckBox _bothFoldings;
    private readonly CheckBox _excludeHeldOut;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly Label _summary;
    private readonly Label _status;
    private readonly ListView _cells;

    private CancellationTokenSource? _cancellation;

    public ParameterGridForm(
        string targetAuthor,
        string poolSummary,
        Func<bool, CancellationToken, Task<IReadOnlyList<WorkTokens>>> poolFor)
    {
        _targetAuthor = targetAuthor;
        _poolSummary = poolSummary;
        _poolFor = poolFor;

        Text = $"Stability - does the result survive a change of settings? ({targetAuthor})";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 1060;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;

        const int LeftCol = 12;
        const int LeftWidth = 250;
        const int RightCol = 274;
        const int RightWidth = 760;

        var sizeGroup = new GroupBox
        {
            Text = "Sample sizes (tokens)",
            Left = LeftCol, Top = 10, Width = LeftWidth, Height = 168
        };

        _chunkSizes = new CheckedListBox
        {
            Left = 12, Top = 20, Width = LeftWidth - 30, Height = 136,
            CheckOnClick = true, IntegralHeight = false
        };
        foreach (var size in ParameterGridRunner.DefaultChunkSizes)
            _chunkSizes.Items.Add(size.ToString("N0"), true);
        sizeGroup.Controls.Add(_chunkSizes);

        var featureGroup = new GroupBox
        {
            Text = "Most frequent words",
            Left = LeftCol, Top = 186, Width = LeftWidth, Height = 148
        };

        _featureCounts = new CheckedListBox
        {
            Left = 12, Top = 20, Width = LeftWidth - 30, Height = 116,
            CheckOnClick = true, IntegralHeight = false
        };
        foreach (var count in ParameterGridRunner.DefaultFeatureCounts)
            _featureCounts.Items.Add(count.ToString(), true);
        featureGroup.Controls.Add(_featureCounts);

        var optionGroup = new GroupBox
        {
            Text = "Also vary",
            Left = LeftCol, Top = 342, Width = LeftWidth, Height = 96
        };

        // On by default. Accent folding is the setting most likely to be an
        // editorial artefact rather than an authorial one - Perseus is not
        // consistent across its editions - so a result that flips when folding
        // flips was measuring orthography, and the grid should be able to say
        // so.
        _bothFoldings = new CheckBox
        {
            Text = "Accent folding on and off",
            Left = 12, Top = 22, Width = 210, Checked = true
        };

        _excludeHeldOut = new CheckBox
        {
            Text = "Held-out work excluded",
            Left = 12, Top = 52, Width = 210
        };

        optionGroup.Controls.Add(_bothFoldings);
        optionGroup.Controls.Add(_excludeHeldOut);

        _runButton = new Button
        {
            Text = "Run grid", Left = LeftCol, Top = 452, Width = 150, Height = 32
        };

        _cancelButton = new Button
        {
            Text = "Stop", Left = LeftCol + 158, Top = 452, Width = 84, Height = 32,
            Enabled = false
        };

        _summary = new Label
        {
            Left = RightCol, Top = 10, Width = RightWidth, Height = 76,
            Text = $"Pool: {poolSummary}. Tick the settings to sweep, then run.",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _cells = new ListView
        {
            Left = RightCol, Top = 92, Width = RightWidth, Height = 526,
            View = View.Details, FullRowSelect = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _cells.Columns.Add("Sample", 70, HorizontalAlignment.Right);
        _cells.Columns.Add("MFW", 55, HorizontalAlignment.Right);
        _cells.Columns.Add("Accents", 70);
        _cells.Columns.Add("Recovered", 80, HorizontalAlignment.Right);
        _cells.Columns.Add("Mean margin", 90, HorizontalAlignment.Right);
        _cells.Columns.Add("rho length", 80, HorizontalAlignment.Right);
        _cells.Columns.Add("95% band", 110, HorizontalAlignment.Right);
        _cells.Columns.Add("rho samples", 85, HorizontalAlignment.Right);
        _cells.Columns.Add("Spread", 60, HorizontalAlignment.Right);
        _cells.Columns.Add("Separation", 75, HorizontalAlignment.Right);
        _cells.Columns.Add("Note", 130);
        ReadingTheme.EnableThemedHeader(_cells);

        _status = new Label
        {
            Left = LeftCol, Top = 630, Width = 1020, Height = 40,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(sizeGroup);
        Controls.Add(featureGroup);
        Controls.Add(optionGroup);
        Controls.Add(_runButton);
        Controls.Add(_cancelButton);
        Controls.Add(_summary);
        Controls.Add(_cells);
        Controls.Add(_status);

        // Same right-click export as the other benches. The grid's own cells
        // are used rather than a raw provider: every value in it is already a
        // rounded summary of a whole validation sweep, so there is no fuller
        // precision behind them to lose.
        ResultExport.AttachTo(_cells, () => $"stability-{targetAuthor}", notes: () => new[]
        {
            $"Classica Codex parameter grid - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Target author: {targetAuthor}",
            $"Pool: {poolSummary}",
            "Sorted by length correlation, weakest first. Read the 95% band before treating that " +
            "order as a gradient - on a nineteen-work corpus the whole visible spread can sit " +
            "inside the estimation error of one cell."
        });

        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();

        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private static List<int> Checked(CheckedListBox list) => list.CheckedItems
        .Cast<string>()
        .Select(s => int.Parse(s.Replace(",", string.Empty)))
        .ToList();

    private async Task RunAsync()
    {
        var sizes = Checked(_chunkSizes);
        var features = Checked(_featureCounts);

        if (sizes.Count == 0 || features.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one sample size and one feature count.",
                "Nothing to sweep", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var foldings = _bothFoldings.Checked ? new[] { true, false } : new[] { true };
        var points = ParameterGridRunner.Build(sizes, features, foldings, _excludeHeldOut.Checked);

        _runButton.Enabled = false;
        _cancelButton.Enabled = true;
        _cells.Items.Clear();
        _cancellation = new CancellationTokenSource();

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
                    ? "Reading the pool (accents folded)..."
                    : "Reading the pool (accents unfolded)...";
                await Task.Yield();
                pools[fold] = await _poolFor(fold, _cancellation.Token);
            }

            var token = _cancellation.Token;
            var progress = new Progress<(int Done, int Total, GridPoint Point)>(p =>
                _status.Text = $"Configuration {p.Done + 1} of {p.Total}: {p.Point.Describe()}...");

            var cells = await Task.Run(() => ParameterGridRunner.Run(
                fold => pools[fold],
                _targetAuthor,
                points,
                (done, total, point) => ((IProgress<(int, int, GridPoint)>)progress).Report((done, total, point)),
                token), token);

            Show(cells);
            _status.Text = $"{cells.Count} configurations. Sorted by length correlation, weakest first.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            _status.Text = "Grid failed - see message.";
            MessageBox.Show(this, ex.Message, "Grid failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _runButton.Enabled = true;
            _cancelButton.Enabled = false;
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
        foreach (var c in cells
                     .OrderBy(c => c.Failed)
                     .ThenBy(c => Math.Abs(c.MarginLengthCorrelation)))
        {
            var row = new ListViewItem(c.Point.ChunkSize.ToString("N0"));
            row.SubItems.Add(c.Point.FeatureWordCount.ToString());
            row.SubItems.Add(c.Point.FoldAccents ? "folded" : "unfolded");

            if (c.Failed)
            {
                for (var i = 0; i < 7; i++) row.SubItems.Add("-");
                row.SubItems.Add(c.Error!.Length > 60 ? c.Error[..60] + "..." : c.Error);
                row.ForeColor = ReadingTheme.MutedText;
                _cells.Items.Add(row);
                continue;
            }

            row.SubItems.Add($"{c.Recovered}/{c.WorksValidated}");
            row.SubItems.Add(StatFormat.Signed3(c.MeanMargin));
            row.SubItems.Add(StatFormat.Signed(c.MarginLengthCorrelation));

            // The band, next to the value, because forty of these in a sorted
            // column look like a gradient and are not one. Over nineteen works
            // a rho of +0.42 spans roughly [-0.04, +0.73] - wider than the
            // whole range this grid displays.
            var band = ValidationResult.FisherInterval(c.MarginLengthCorrelation, c.WorksValidated);
            row.SubItems.Add(StatFormat.Band(band));

            row.SubItems.Add(StatFormat.Signed(c.MarginSampleCountCorrelation));
            row.SubItems.Add(c.LengthSpread.ToString("0.00"));
            row.SubItems.Add(StatFormat.Signed3(c.PoolSeparation));

            // The note column exists so that a low rho cannot be read as good
            // news without its caveat sitting on the same row.
            var note =
                c.DroppedWorks ? $"tested {c.WorksValidated} works - shortest dropped"
                : c.LengthSpread < 1.8 ? "little length spread - rho uninformative"
                : c.Trustworthy ? "recovers, no length effect"
                : string.Empty;

            row.SubItems.Add(note);

            if (c.Trustworthy)
                row.ForeColor = ReadingTheme.IsDark ? Color.FromArgb(140, 210, 140) : Color.DarkGreen;
            else if (c.DroppedWorks || c.LengthSpread < 1.8)
                row.ForeColor = ReadingTheme.MutedText;

            _cells.Items.Add(row);
        }

        _summary.Text =
            $"Pool: {_poolSummary}." + Environment.NewLine +
            ParameterGridRunner.Summarise(cells) + Environment.NewLine +
            "Sorted by length correlation, weakest first - but read the band beside it before " +
            "treating that order as a gradient.";
    }
}
