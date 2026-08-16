using System.Globalization;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

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
/// of any real text somebody else wrote, and the form says so in the place a
/// reader is most likely to stop reading.
/// </summary>
public class PerturbationForm : ScaledForm
{
    private readonly string _targetAuthor;
    private readonly IReadOnlyList<WorkTokens> _pool;

    private readonly ComboBox _targetWork;
    private readonly CheckedListBox _donorAuthors;
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
    private readonly Label _summary;
    private readonly Label _status;
    private readonly ListView _levels;

    private CancellationTokenSource? _cancellation;
    private readonly StylometryExperimentRepository _experiments = new();
    private PerturbationLevel? _finalLevel;

    /// <summary>
    /// Every level produced so far, with the work and donor it belongs to.
    ///
    /// Kept because the ListView holds strings that were rounded for a screen -
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
    /// a load produced a file with a header, column names, and no data, in all
    /// three formats at once.
    /// </summary>
    private readonly List<ExperimentRow> _loadedRows = new();

    private string _runNotes = string.Empty;

    /// <summary>
    /// The header written above an exported table, captured when the rows were
    /// produced rather than read from the controls when the file is written.
    ///
    /// THIS IS THE THIRD TIME THIS FORM HAS GOT THIS WRONG. Anything describing
    /// a run has to be captured with the run: the controls keep moving
    /// afterwards, and a header taken from them at export time describes the
    /// form rather than the data. The observed failure was a file headed
    /// "Target author: Aelian, pool Aelian and Aelius Herodianus" whose every
    /// row was Plato's Republic against Polybius - the settings had been
    /// changed after loading an older experiment, and nothing had cleared it.
    ///
    /// A header that contradicts its own table is worse than no header, because
    /// it will be believed.
    /// </summary>
    private IReadOnlyList<string> _exportNotes = Array.Empty<string>();

    /// <summary>
    /// Whose experiment the table currently holds - the form's own author after
    /// a run, the saved experiment's after a load. Names the exported file.
    /// </summary>
    private string _exportAuthor;
    private string _powerNotes = string.Empty;

    private static readonly (string Label, double[] Levels)[] LevelPresets =
    {
        ("Coarse - 0, 1, 2, 5, 10, 20%", new[] { 0.00, 0.01, 0.02, 0.05, 0.10, 0.20 }),
        ("Wide - 0 to 50% in tens", new[] { 0.00, 0.10, 0.20, 0.30, 0.40, 0.50 }),
        ("Sensitivity - 0 to 25% in fives", new[] { 0.00, 0.05, 0.10, 0.15, 0.20, 0.25 }),
        ("Fine - 0 to 10% in ones", new[] { 0.00, 0.01, 0.02, 0.03, 0.04, 0.05, 0.06, 0.07, 0.08, 0.09, 0.10 })
    };

    public PerturbationForm(string targetAuthor, IReadOnlyList<WorkTokens> pool)
    {
        _targetAuthor = targetAuthor;
        _exportAuthor = targetAuthor;
        _pool = pool;

        Text = $"Perturbation - how much disturbance before the method stops recognising it? ({targetAuthor})";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 1400;
        Height = 832;   // two more button rows than it started with
        StartPosition = FormStartPosition.CenterParent;

        const int LeftCol = 12;
        const int LeftWidth = 320;
        const int RightCol = 344;
        const int RightWidth = 1030;

        var targetGroup = new GroupBox
        {
            Text = "Work to contaminate", Left = LeftCol, Top = 10, Width = LeftWidth, Height = 62
        };

        _targetWork = new ComboBox
        {
            Left = 12, Top = 24, Width = LeftWidth - 30, DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var w in pool.Where(w => w.AuthorName == targetAuthor).OrderBy(w => w.WorkTitle))
            _targetWork.Items.Add(new WorkChoice(w));
        if (_targetWork.Items.Count > 0) _targetWork.SelectedIndex = 0;
        targetGroup.Controls.Add(_targetWork);

        var donorGroup = new GroupBox
        {
            Text = "Donor material", Left = LeftCol, Top = 80, Width = LeftWidth, Height = 200
        };

        _donorAuthors = new CheckedListBox
        {
            Left = 12, Top = 20, Width = LeftWidth - 30, Height = 116,
            CheckOnClick = true, IntegralHeight = false
        };
        foreach (var author in pool.Select(w => w.AuthorName).Distinct()
                     .Where(a => a != targetAuthor).OrderBy(a => a))
            _donorAuthors.Items.Add(author);
        if (_donorAuthors.Items.Count > 0) _donorAuthors.SetItemChecked(0, true);

        // On by default. Without it a falling curve is uninterpretable - any
        // large change to a text might reduce a margin simply by being a
        // change. This is the comparison that separates "disturbed by someone
        // else's style" from "disturbed".
        _sameAuthorControl = new CheckBox
        {
            Text = $"Also run the {targetAuthor} control",
            Left = 12, Top = 144, Width = LeftWidth - 30, Checked = true
        };

        donorGroup.Controls.Add(_donorAuthors);
        donorGroup.Controls.Add(_sameAuthorControl);

        var seriesGroup = new GroupBox
        {
            Text = "Series", Left = LeftCol, Top = 288, Width = LeftWidth, Height = 208
        };

        _levelPreset = new ComboBox
        {
            Left = 12, Top = 22, Width = LeftWidth - 30, DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var (label, _) in LevelPresets) _levelPreset.Items.Add(label);
        _levelPreset.SelectedIndex = 0;

        var scopeLabel = new Label { Text = "Donor draw:", Left = 12, Top = 56, Width = 100 };
        _donorScope = new ComboBox
        {
            Left = 118, Top = 52, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList
        };
        // Whole corpus first and default: it is what every result recorded so
        // far used, and changing the default would silently make old and new
        // runs incomparable.
        _donorScope.Items.AddRange(new object[] { "Whole donor corpus", "One work per mixture" });
        _donorScope.SelectedIndex = 0;

        var modeLabel = new Label { Text = "Mode:", Left = 12, Top = 88, Width = 100 };
        _mode = new ComboBox
        {
            Left = 118, Top = 84, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList
        };
        // Replace first, and the default. It holds the token count constant, so
        // the length confound cannot move while the contamination does.
        _mode.Items.AddRange(new object[] { "Replace (length held)", "Add (length grows)" });
        _mode.SelectedIndex = 0;

        var iterLabel = new Label { Text = "Iterations:", Left = 12, Top = 120, Width = 100 };
        _iterations = new NumericUpDown
        {
            Left = 118, Top = 116, Width = 80, Minimum = 1, Maximum = 200, Value = 25
        };

        var seedLabel = new Label { Text = "Seed:", Left = 12, Top = 150, Width = 100 };
        _seed = new NumericUpDown
        {
            Left = 118, Top = 146, Width = 80, Minimum = 0, Maximum = 999999, Value = 42
        };

        seriesGroup.Controls.Add(_levelPreset);
        seriesGroup.Controls.Add(scopeLabel);
        seriesGroup.Controls.Add(_donorScope);
        seriesGroup.Controls.Add(modeLabel);
        seriesGroup.Controls.Add(_mode);
        seriesGroup.Controls.Add(iterLabel);
        seriesGroup.Controls.Add(_iterations);
        seriesGroup.Controls.Add(seedLabel);
        seriesGroup.Controls.Add(_seed);

        var settingsGroup = new GroupBox
        {
            Text = "Delta settings", Left = LeftCol, Top = 504, Width = LeftWidth, Height = 92
        };

        var sampleLabel = new Label { Text = "Sample size:", Left = 12, Top = 26, Width = 100 };
        _sampleSize = new NumericUpDown
        {
            Left = 118, Top = 22, Width = 80,
            Minimum = 500, Maximum = 20000, Increment = 250, Value = 2500
        };

        var featureLabel = new Label { Text = "MFW:", Left = 12, Top = 58, Width = 100 };
        _featureCount = new NumericUpDown
        {
            Left = 118, Top = 54, Width = 80, Minimum = 20, Maximum = 1000, Increment = 10, Value = 150
        };

        settingsGroup.Controls.Add(sampleLabel);
        settingsGroup.Controls.Add(_sampleSize);
        settingsGroup.Controls.Add(featureLabel);
        settingsGroup.Controls.Add(_featureCount);

        _runButton = new Button { Text = "Run series", Left = LeftCol, Top = 608, Width = 160, Height = 32 };
        _cancelButton = new Button { Text = "Stop", Left = LeftCol + 168, Top = 608, Width = 90, Height = 32, Enabled = false };

        // Four plays showed that three of them lose the same 0.020 of margin
        // and one does not. Four points cannot say what the normal range IS -
        // that needs the whole author, and running them one at a time is how a
        // question stops getting asked.
        _runAllButton = new Button
        {
            Text = "Run every work by this author...",
            Left = LeftCol, Top = 646, Width = 258, Height = 32
        };
        _runAllButton.Click += async (_, _) => await RunAllAsync();

        // Saving and loading sit next to each other because the pair is the
        // point: every run in this project so far existed only as a CSV
        // somebody remembered to export, and several conclusions in
        // docs/stylometry-notes.md rest on runs that are gone.
        _saveButton = new Button
        {
            Text = "Save experiment...", Left = LeftCol, Top = 684, Width = 126, Height = 30
        };
        _saveButton.Click += async (_, _) => await SaveAsync();

        _loadButton = new Button
        {
            Text = "Load...", Left = LeftCol + 132, Top = 684, Width = 126, Height = 30
        };
        _loadButton.Click += async (_, _) => await LoadAsync();

        _summary = new Label
        {
            Left = RightCol, Top = 10, Width = RightWidth, Height = 96,
            Text = "Pick a work and a donor, then run. The control runs alongside by default.",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _levels = new ListView
        {
            Left = RightCol, Top = 112, Width = RightWidth, Height = 608,
            View = View.Details, FullRowSelect = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _levels.Columns.Add("Work", 140);
        _levels.Columns.Add("Donor", 120);
        _levels.Columns.Add("Injected", 70, HorizontalAlignment.Right);
        _levels.Columns.Add("Mean margin", 90, HorizontalAlignment.Right);
        _levels.Columns.Add("SD", 70, HorizontalAlignment.Right);
        _levels.Columns.Add("% of baseline", 90, HorizontalAlignment.Right);

        // The percentage cannot separate a small real effect from noise. At 20%
        // Aeschylus, Alcestis read 98% of baseline and Rhesus 80% - but
        // Alcestis had moved a tenth of one standard deviation and Rhesus 2.3,
        // so only one of those is an effect. This column is what says which.
        _levels.Columns.Add("Shift (SD)", 75, HorizontalAlignment.Right);

        // The only column that compares across works. Percentage divides by the
        // baseline and SD divides by the noise, and on four real plays those
        // two rank the same experiment in exactly opposite orders. In Delta,
        // Rhesus, Hecuba and Helen all lost about 0.028 at 20% injection while
        // Heracleidae lost 0.011 - which is the finding, and neither of the
        // other columns shows it.
        _levels.Columns.Add("Drop (Δ)", 75, HorizontalAlignment.Right);
        _levels.Columns.Add("Recovered", 80, HorizontalAlignment.Right);
        _levels.Columns.Add("Nearest", 110);
        _levels.Columns.Add("Tokens", 70, HorizontalAlignment.Right);
        ReadingTheme.EnableThemedHeader(_levels);

        _status = new Label
        {
            Left = LeftCol, Top = 732, Width = 1360, Height = 36,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(targetGroup);
        Controls.Add(donorGroup);
        Controls.Add(seriesGroup);
        Controls.Add(settingsGroup);
        Controls.Add(_runButton);
        Controls.Add(_cancelButton);
        Controls.Add(_runAllButton);
        Controls.Add(_saveButton);
        Controls.Add(_loadButton);
        Controls.Add(_summary);
        Controls.Add(_levels);
        Controls.Add(_status);

        // The name follows the DATA, not the window: loading another author's
        // experiment into this form must not produce a file named after the
        // author the form happens to be open on.
        ResultExport.AttachTo(
            _levels,
            () => $"perturbation-{_exportAuthor}",
            ExportRows,
            () => _exportNotes);

        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();

        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private sealed class WorkChoice
    {
        public readonly WorkTokens Work;
        public WorkChoice(WorkTokens work) => Work = work;
        public override string ToString() => $"{Work.WorkTitle}  ({Work.Tokens.Count:N0} tokens)";
    }

    private async Task RunAsync()
    {
        if (_targetWork.SelectedItem is not WorkChoice choice) return;

        var donorAuthors = _donorAuthors.CheckedItems.Cast<string>().ToList();
        if (donorAuthors.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one donor author.",
                "No donor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var target = choice.Work;
        var levels = LevelPresets[_levelPreset.SelectedIndex].Levels;
        var mode = _mode.SelectedIndex == 0 ? InjectionMode.Replace : InjectionMode.Add;
        var scope = _donorScope.SelectedIndex == 1 ? DonorScope.SingleWork : DonorScope.WholeCorpus;
        var settings = new DeltaSettings((int)_featureCount.Value, (int)_sampleSize.Value);
        var seed = (int)_seed.Value;
        var iterations = (int)_iterations.Value;

        var crossDonors = _pool
            .Where(w => donorAuthors.Contains(w.AuthorName))
            .Select(w => w.WorkId)
            .ToList();

        // The control draws from the target's author EXCLUDING the target
        // itself - injecting a work into itself would measure nothing.
        var controlDonors = _pool
            .Where(w => w.AuthorName == _targetAuthor && w.WorkId != target.WorkId)
            .Select(w => w.WorkId)
            .ToList();

        _runButton.Enabled = false;
        _cancelButton.Enabled = true;
        _levels.Items.Clear();
        _cancellation = new CancellationTokenSource();

        // Both of these describe a cross-work sweep. A single series has no
        // cross-work reading and no detection power, and leaving the previous
        // sweep's figures in place would put them in this run's export header -
        // an exported table whose header describes different data is worse than
        // one with no header at all.
        _runNotes = string.Empty;
        _powerNotes = string.Empty;
        _exportNotes = Array.Empty<string>();

        try
        {
            var token = _cancellation.Token;

            // Progress<T> captures the synchronisation context it is
            // constructed on - the UI thread, here - and marshals every report
            // back to it. The series itself runs on a worker via Task.Run, and
            // a callback that assigned _status.Text directly from there threw
            // "Cross-thread operation not valid" on the first level it
            // reported. ParameterGridForm already did it this way; this form
            // did not, which is the whole of the bug.
            var progress = new Progress<string>(text => _status.Text = text);
            var report = (IProgress<string>)progress;

            var runs = new List<(string Donor, List<PerturbationLevel> Series)>();

            var crossLabel = string.Join(", ", donorAuthors);
            _status.Text = $"Contaminating {target.WorkTitle} with {crossLabel}...";
            await Task.Yield();

            runs.Add((crossLabel, await Task.Run(() => PerturbationRunner.RunSeries(
                _pool, target.WorkId, crossDonors, levels, mode, seed, iterations, settings,
                (i, n, level) => report.Report($"{crossLabel}: level {i + 1} of {n} ({level:P0})..."),
                token, scope), token)));

            if (_sameAuthorControl.Checked && controlDonors.Count == 0)
            {
                // The control needs other works by the same author, and the
                // pool may not have any - an author with one work, or a pool
                // filtered until only this work is left. Saying so beats
                // throwing "No donor material" from inside the engine, which is
                // what happened until the filter that caused it was removed.
                _status.Text = $"No control: the pool has no other {_targetAuthor} work to draw from.";
            }

            if (_sameAuthorControl.Checked && controlDonors.Count > 0)
            {
                _status.Text = $"Control: contaminating with {_targetAuthor}...";
                await Task.Yield();

                runs.Add(($"{_targetAuthor} (control)", await Task.Run(() => PerturbationRunner.RunSeries(
                    _pool, target.WorkId, controlDonors, levels, mode, seed, iterations, settings,
                    (i, n, level) => report.Report($"Control: level {i + 1} of {n} ({level:P0})..."),
                    token, scope), token)));
            }

            Show(target, runs, mode, seed, iterations);
            _exportAuthor = _targetAuthor;
            _exportNotes = CaptureNotes();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            _status.Text = "Series failed - see message.";
            MessageBox.Show(this, ex.Message, "Series failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _runButton.Enabled = true;
            _cancelButton.Enabled = false;
            _cancellation?.Dispose();
            _cancellation = null;
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
    /// The lines written above the exported table.
    ///
    /// A table of numbers without its seed and settings is not reproducible,
    /// which is the one property a perturbation experiment is supposed to
    /// have. These go into the file so the numbers cannot be separated from
    /// what produced them.
    /// </summary>
    /// <summary>
    /// Builds the header for whatever has just been produced. Called when a run
    /// finishes or an experiment is loaded - never at export time.
    /// </summary>
    private IReadOnlyList<string> CaptureNotes()
    {
        var notes = new List<string>
        {
            $"Classica Codex perturbation series - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Target author: {_targetAuthor}",
            $"Sample size: {_sampleSize.Value} tokens, {_featureCount.Value} most frequent words",
            $"Mode: {(_mode.SelectedIndex == 0 ? "Replace (length held)" : "Add (length grows)")}, " +
            $"seed {_seed.Value}, {_iterations.Value} iterations per level",
            $"Donor draw: {(_donorScope.SelectedIndex == 1 ? "one work per mixture" : "whole donor corpus")}",
            $"Pool: {string.Join(", ", _pool.Select(w => w.AuthorName).Distinct().OrderBy(a => a))} " +
            $"({_pool.Count} works)"
        };

        if (!string.IsNullOrEmpty(_runNotes)) notes.Add(_runNotes);
        if (!string.IsNullOrEmpty(_powerNotes)) notes.Add("Detection power - " + _powerNotes);

        notes.Add("Contamination is synthetic. This measures how much disturbance the method absorbs " +
                  "on texts of known authorship - it is not an estimate of how much of any real text " +
                  "somebody else wrote.");

        return notes;
    }

    /// <summary>
    /// Writes the current table to the library, with everything needed to
    /// rebuild it.
    ///
    /// The rows are stored at full precision from the underlying levels rather
    /// than from the ListView, for the same reason the export is: the table on
    /// screen is rounded for reading, and a stored run that carries only the
    /// rounded numbers cannot be re-analysed. The detection power was computed
    /// against the wrong scatter once, and the fix had to be checked against a
    /// stored run rather than a memory of one.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_rawLevels.Count == 0)
        {
            MessageBox.Show(this,
                _loadedRows.Count > 0
                    ? "This experiment was loaded from the library and is already saved. Run it again to " +
                      "store a new copy."
                    : "Run something first.",
                "Nothing to save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // TextPromptForm.Ask returns null when cancelled AND when left blank,
        // which suits every other caller and not this one: an unlabelled
        // experiment is a perfectly good experiment, and cancelling should not
        // save anything. So the dialog is shown directly to tell the two apart.
        using var prompt = new TextPromptForm(
            "Save experiment",
            "Label for this experiment (optional) - what you were testing, so the list " +
            "is readable in six months:",
            string.Empty);

        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        var label = prompt.Value;

        var parameters = new Dictionary<string, string>
        {
            ["mode"] = _mode.SelectedIndex == 0 ? "replace" : "add",
            ["donorScope"] = _donorScope.SelectedIndex == 1 ? "singleWork" : "wholeCorpus",
            ["levels"] = string.Join(",", LevelPresets[_levelPreset.SelectedIndex].Levels
                .Select(l => l.ToString(CultureInfo.InvariantCulture))),
            ["donors"] = string.Join(", ", _donorAuthors.CheckedItems.Cast<string>()),
            ["control"] = _sameAuthorControl.Checked ? "yes" : "no"
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
            null,
            string.Join(", ", _pool.Select(w => w.AuthorName).Distinct().OrderBy(a => a)),
            _pool.Select(w => w.WorkId).Distinct().OrderBy(x => x).ToList(),
            (int)_seed.Value,
            (int)_iterations.Value,
            (int)_sampleSize.Value,
            (int)_featureCount.Value,
            FoldAccents: true,
            StylometryRunRepository.CurrentAlgorithmVersion,
            parameters,
            metrics,
            string.IsNullOrWhiteSpace(label) ? null : label.Trim());

        try
        {
            var id = await _experiments.SaveAsync(definition, rows);
            _status.Text = $"Saved as experiment {id} - {rows.Count} rows, seed {_seed.Value}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Reloads a saved experiment into the table.
    ///
    /// Shows the stored rows rather than re-running: a sweep is thousands of
    /// mixtures and several minutes, and the stored numbers ARE the result. The
    /// settings are put back on the controls so that re-running it is one
    /// click, which is the other thing a saved experiment is for.
    /// </summary>
    private async Task LoadAsync()
    {
        var saved = await _experiments.GetAllAsync(ExperimentKinds.Perturbation);

        if (saved.Count == 0)
        {
            MessageBox.Show(this, "No saved perturbation experiments yet.", "Nothing to load",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new ExperimentPickerForm(saved);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected == null) return;

        var chosen = picker.Selected;
        var rows = await _experiments.GetRowsAsync(chosen.ExperimentId);
        var definition = await _experiments.GetDefinitionAsync(chosen.ExperimentId);

        _levels.Items.Clear();
        _rawLevels.Clear();
        _loadedRows.Clear();
        _loadedRows.AddRange(rows);

        foreach (var r in rows)
        {
            var item = new ListViewItem(new[]
            {
                r.WorkTitle, r.Donor, r.Level.ToString("P0"),
                StatFormat.Signed3(r.MeanMargin), r.StdDev.ToString("0.0000"),
                r.Level <= 0 ? "-" : (r.BaselineMargin == 0 ? "-" : (r.MeanMargin / r.BaselineMargin).ToString("P0")),

                // Recomputed rather than left blank. It is (mean - baseline) /
                // SD, and all three are stored, so there is no ambiguity for a
                // dash to protect against.
                r.Level <= 0 || r.StdDev < 1e-12
                    ? "-"
                    : StatFormat.Signed((r.MeanMargin - r.BaselineMargin) / r.StdDev),

                r.Level <= 0 ? "-" : StatFormat.Signed3(r.MeanMargin - r.BaselineMargin),
                $"{r.Recovered}/{r.Trials}",

                // NearestCount is 0 on rows written before v16, which did not
                // record it - shown as a bare name rather than as "(0/25)",
                // which would read as unanimous disagreement.
                r.NearestAuthor == null ? "-"
                    : r.NearestCount > 0 ? $"{r.NearestAuthor} ({r.NearestCount}/{r.Trials})"
                    : r.NearestAuthor,
                r.TokenCount.ToString("N0")
            });

            if (r.Donor.Contains("(control)")) item.ForeColor = ReadingTheme.MutedText;
            _levels.Items.Add(item);
        }

        // Settings back onto the controls, so re-running really is one click.
        // The four numeric ones are on the summary; the rest live in
        // Parameters, and leaving them alone would have meant a re-run silently
        // using whatever the form happened to be showing - a different donor or
        // a different level series against the same seed, which would look like
        // a reproduction and not be one.
        _seed.Value = Math.Clamp(chosen.Seed, (int)_seed.Minimum, (int)_seed.Maximum);
        _iterations.Value = Math.Clamp(chosen.Iterations, (int)_iterations.Minimum, (int)_iterations.Maximum);
        _sampleSize.Value = Math.Clamp(chosen.ChunkSize, (int)_sampleSize.Minimum, (int)_sampleSize.Maximum);
        _featureCount.Value = Math.Clamp(chosen.FeatureWordCount, (int)_featureCount.Minimum, (int)_featureCount.Maximum);

        if (definition != null)
        {
            if (definition.Parameters.TryGetValue("mode", out var mode))
                _mode.SelectedIndex = mode == "add" ? 1 : 0;

            if (definition.Parameters.TryGetValue("donorScope", out var scope))
                _donorScope.SelectedIndex = scope == "singleWork" ? 1 : 0;

            if (definition.Parameters.TryGetValue("control", out var control))
                _sameAuthorControl.Checked = control == "yes";

            if (definition.Parameters.TryGetValue("donors", out var donors))
            {
                var wanted = donors.Split(',').Select(d => d.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < _donorAuthors.Items.Count; i++)
                    _donorAuthors.SetItemChecked(i, wanted.Contains((string)_donorAuthors.Items[i]));
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
        if (titles.Count == 1)
        {
            for (var i = 0; i < _targetWork.Items.Count; i++)
            {
                if (_targetWork.Items[i] is WorkChoice c && c.Work.WorkTitle == titles[0])
                {
                    _targetWork.SelectedIndex = i;
                    break;
                }
            }
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
            $"Sample size: {chosen.ChunkSize} tokens, {chosen.FeatureWordCount} most frequent words",
            $"Seed {chosen.Seed}, {chosen.Iterations} iterations per level",
            $"Pool: {chosen.PoolSummary}" +
            (definition == null ? "" : $" ({definition.PoolWorkIds.Count} works)")
        };

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
                : $"{rows.Count} rows loaded. Pool was {definition.PoolWorkIds.Count} works, this form has " +
                  $"{currentPool.Count} - margins are not comparable across pools, so re-running will not " +
                  "reproduce these numbers.";
    }

    private async Task RunAllAsync()
    {
        var donorAuthors = _donorAuthors.CheckedItems.Cast<string>().ToList();
        if (donorAuthors.Count == 0)
        {
            MessageBox.Show(this, "Tick at least one donor author.",
                "No donor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var works = _pool
            .Where(w => w.AuthorName == _targetAuthor)
            .OrderBy(w => w.WorkTitle)
            .ToList();

        var levels = LevelPresets[_levelPreset.SelectedIndex].Levels;
        var mode = _mode.SelectedIndex == 0 ? InjectionMode.Replace : InjectionMode.Add;
        var scope = _donorScope.SelectedIndex == 1 ? DonorScope.SingleWork : DonorScope.WholeCorpus;
        var settings = new DeltaSettings((int)_featureCount.Value, (int)_sampleSize.Value);
        var seed = (int)_seed.Value;
        var iterations = (int)_iterations.Value;
        var runControl = _sameAuthorControl.Checked;

        var estimate = works.Count * levels.Length * iterations * (runControl ? 2 : 1);
        if (MessageBox.Show(this,
                $"Run {works.Count} works x {levels.Length} levels x {iterations} iterations" +
                (runControl ? " x 2 (with control)" : "") + $" = about {estimate:N0} mixtures." +
                Environment.NewLine + Environment.NewLine +
                "This takes a while. Stop is available throughout and keeps whatever has finished.",
                "Run every work", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        _runButton.Enabled = false;
        _runAllButton.Enabled = false;
        _cancelButton.Enabled = true;
        _levels.Items.Clear();
        _rawLevels.Clear();
        _loadedRows.Clear();
        _runNotes = string.Empty;
        _powerNotes = string.Empty;
        _exportNotes = Array.Empty<string>();
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
            var progress = new Progress<string>(text => _status.Text = text);
            var report = (IProgress<string>)progress;
            var crossLabel = string.Join(", ", donorAuthors);

            var crossDonors = _pool
                .Where(w => donorAuthors.Contains(w.AuthorName))
                .Select(w => w.WorkId)
                .ToList();

            for (var i = 0; i < works.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var work = works[i];
                _status.Text = $"Work {i + 1} of {works.Count}: {work.WorkTitle}...";
                await Task.Yield();

                var runs = new List<(string Donor, List<PerturbationLevel> Series)>();

                try
                {
                    runs.Add((crossLabel, await Task.Run(() => PerturbationRunner.RunSeries(
                        _pool, work.WorkId, crossDonors, levels, mode, seed, iterations, settings,
                        (l, n, level) => report.Report(
                            $"{work.WorkTitle} ({i + 1}/{works.Count}): level {l + 1} of {n} ({level:P0})..."),
                        token, scope), token)));

                    var controlDonors = _pool
                        .Where(w => w.AuthorName == _targetAuthor && w.WorkId != work.WorkId)
                        .Select(w => w.WorkId)
                        .ToList();

                    // Skipped rather than attempted when there is nothing to
                    // draw from: an author with a single work in the pool has
                    // no same-author control available, and that is a fact
                    // about the pool rather than an error.
                    if (runControl && controlDonors.Count > 0)
                    {
                        runs.Add(($"{_targetAuthor} (control)", await Task.Run(() => PerturbationRunner.RunSeries(
                            _pool, work.WorkId, controlDonors, levels, mode, seed, iterations, settings,
                            (l, n, level) => report.Report(
                                $"{work.WorkTitle} control ({i + 1}/{works.Count}): level {l + 1} of {n}..."),
                            token, scope), token)));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A work too short to sample at this size is skipped rather
                    // than aborting the other eighteen.
                    _levels.Items.Add(new ListViewItem(new[]
                    {
                        work.WorkTitle, "-", "-", "-", "-", "-", "-", "-", "-",
                        ex.Message.Length > 40 ? ex.Message[..40] + "..." : ex.Message, "-"
                    })
                    { ForeColor = ReadingTheme.MutedText });
                    continue;
                }

                Show(work, runs, mode, seed, iterations, append: true);

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
            _status.Text = $"Stopped after {finals.Count} works - the rows below are complete.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Run failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _runButton.Enabled = true;
            _runAllButton.Enabled = true;
            _cancelButton.Enabled = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }

        if (skipped.Count > 0)
        {
            _runNotes = $"Skipped {skipped.Count} work(s) shorter than one {_sampleSize.Value:N0}-token " +
                        $"sample: {string.Join(", ", skipped)}.";
        }

        if (finals.Count >= 3)
        {
            ShowSpread(finals, string.Join(", ", donorAuthors), levels[^1], byLevel, lengths);

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
        if (_levels.Items.Count > 0)
        {
            _exportAuthor = _targetAuthor;
            _exportNotes = CaptureNotes();
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
        string donor, double topLevel,
        Dictionary<double, List<double>>? byLevel = null,
        List<(double Length, double Margin)>? lengths = null)
    {
        var comparison = PerturbationRunner.CompareWorks(
            finals.Select(f => (f.Title, f.Baseline, f.Drop)).ToList());

        var drops = finals.Select(f => f.Drop).OrderBy(d => d).ToList();
        var median = drops[drops.Count / 2];
        var outliers = comparison.Outliers();

        _summary.Text =
            $"{finals.Count} works contaminated with {donor} at {topLevel:P0}, seed {_seed.Value}, " +
            $"{_iterations.Value} iterations." + Environment.NewLine +
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

        _status.Text = $"{_levels.Items.Count} rows. Contamination is synthetic - see the summary.";
    }

    private void Show(
        WorkTokens target,
        List<(string Donor, List<PerturbationLevel> Series)> runs,
        InjectionMode mode,
        int seed,
        int iterations,
        bool append = false)
    {
        if (!append)
        {
            _levels.Items.Clear();
            _rawLevels.Clear();
            _loadedRows.Clear();
            _exportNotes = Array.Empty<string>();
        }

        var workTitle = target.WorkTitle;

        foreach (var (donor, series) in runs)
        {
            foreach (var level in series.OrderBy(l => l.InjectionFraction))
            {
                var row = new ListViewItem(workTitle);
                row.SubItems.Add(donor);
                row.SubItems.Add(level.InjectionFraction.ToString("P0"));
                row.SubItems.Add(StatFormat.Signed3(level.MeanMargin));
                row.SubItems.Add(level.MarginStdDev.ToString("0.0000"));
                row.SubItems.Add(level.InjectionFraction <= 0 ? "-" : level.ProportionOfBaseline.ToString("P0"));
                row.SubItems.Add(level.InjectionFraction <= 0
                    ? "-"
                    : StatFormat.Signed(level.ShiftInStdDevs));
                row.SubItems.Add(level.InjectionFraction <= 0
                    ? "-"
                    : StatFormat.Signed3(level.AbsoluteShift));
                row.SubItems.Add($"{level.RecoveredCount}/{level.Trials.Count}");

                var nearest = level.NearestAuthorCounts
                    .OrderByDescending(kv => kv.Value)
                    .FirstOrDefault();
                row.SubItems.Add(nearest.Key == null
                    ? "-"
                    : $"{nearest.Key} ({nearest.Value}/{level.Trials.Count})");

                row.SubItems.Add(level.Trials.Count == 0 ? "-" : level.Trials[0].TokenCount.ToString("N0"));

                _rawLevels.Add((workTitle, donor, level));

                if (donor.Contains("(control)")) row.ForeColor = ReadingTheme.MutedText;
                else if (level.InjectionFraction > 0 && level.ShiftInStdDevs < -2)
                    row.ForeColor = ReadingTheme.IsDark ? Color.FromArgb(240, 140, 130) : Color.DarkRed;

                _levels.Items.Add(row);
            }
        }

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
            $"Reproduce: seed {seed}, {iterations} iterations, " +
            $"{(mode == InjectionMode.Replace ? "replace" : "add")}, " +
            $"{_sampleSize.Value:N0}-token samples, {_featureCount.Value} MFW.";

        _status.Text = $"{_levels.Items.Count} rows. Contamination is synthetic - see the summary before quoting a number.";
    }
}
