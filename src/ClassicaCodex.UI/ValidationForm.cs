using ClassicaCodex.Core;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Asks whether the method recovers texts whose authorship is not in question,
/// before it is used on one that is.
///
/// This is the first bench in what is meant to become a validation and
/// experiments environment - parameter grids and controlled perturbation are
/// the obvious next two - so the shape here is deliberate. All the arithmetic
/// is in ClassicaCodex.Core.Stylometry.LeaveOneOutValidator; this form picks a
/// pool, calls it, and lays the answer out. Nothing is computed in this file.
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
/// That last paragraph is the reason this form exists. It is easier to build a
/// tool that produces encouraging numbers than one that catches itself.
/// </summary>
public class ValidationForm : Form
{
    private class WorkItem
    {
        public int WorkId;
        public int EditionId;
        public string AuthorName = string.Empty;
        public string WorkTitle = string.Empty;
        public string Language = string.Empty;
    }

    private readonly ComboBox _targetAuthor;
    private readonly CheckedListBox _poolAuthors;
    private readonly ComboBox _poolPreset;
    private readonly CheckBox _foldAccents;
    private readonly CheckBox _excludeHeldOut;
    private readonly CheckBox _excludeNonCompositions;
    private readonly NumericUpDown _sampleSize;
    private readonly NumericUpDown _featureCount;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private readonly Button _gridButton;
    private readonly Button _perturbButton;
    private readonly Label _status;
    private readonly Label _summary;
    private readonly ListView _results;

    /// <summary>
    /// Titles that mark a volume as a compilation rather than a composition.
    ///
    /// Kept in step with StylometryForm's list of the same name, and here for
    /// a sharper reason than there. In a single run a fragment collection
    /// merely distorts the variance; in a validation sweep it becomes a WORK
    /// BEING VALIDATED - the harness holds out Euripides' Fragmenta, asks
    /// whether the method recovers it as Euripidean, and counts the answer
    /// towards a recovery rate.
    ///
    /// That question has no correct answer. Fragmenta is a modern editor's
    /// gathering of lines quoted by Athenaeus, Stobaeus and the scholiasts,
    /// each excerpted for being quotable. Whatever the method says about it is
    /// a fact about anthologising practice, and a recovery rate that includes
    /// it is measuring something other than what it claims.
    /// </summary>
    private static readonly string[] NonCompositionTitleMarkers =
    {
        "fragmenta", "fragment", "fragments",
        "index", "scholia", "scholion", "testimonia", "excerpta",
        "lexicon", "anthology", "anthologia", "gnomologium",
        "paraphrasis", "commentar"
    };

    private static bool IsNonComposition(WorkItem w) =>
        NonCompositionTitleMarkers.Any(m =>
            w.WorkTitle.Contains(m, StringComparison.OrdinalIgnoreCase));

    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<WorkItem> _allWorks = new();

    private const int CustomPresetIndex = 2;

    /// <summary>Set while a preset is setting the ticks, so it does not undo itself.</summary>
    private bool _suppressPresetReset;

    private List<string> _filteredOut = new();
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Tokens by (workId, foldAccents), held for the lifetime of the form.
    ///
    /// Tokenising is the expensive half - it reads every reading line of every
    /// work in the pool out of SQLite - and a validation sweep runs the engine
    /// once per sample per work, which at nineteen plays is around fifty runs
    /// over the same text. Without this the form would re-read the corpus fifty
    /// times to answer one question, and the parameter grid that comes next
    /// would multiply that by the size of the grid.
    ///
    /// Keyed on accent folding as well as work id because folding happens
    /// during tokenisation, so the two settings produce genuinely different
    /// token streams from the same rows.
    /// </summary>
    private readonly Dictionary<(int WorkId, bool Fold), IReadOnlyList<string>> _tokenCache = new();

    /// <summary>Set by the caller so a result can be opened as text.</summary>
    public Func<int, Task>? OnOpenWork { get; set; }

    public ValidationForm()
    {
        Text = "Validation - can these settings recover known texts?";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 1180;
        Height = 820;
        StartPosition = FormStartPosition.CenterParent;

        const int LeftCol = 12;
        const int LeftWidth = 330;
        const int RightCol = 354;
        const int RightWidth = 800;

        // --- what is being validated -----------------------------------------

        var targetGroup = new GroupBox
        {
            Text = "Author to validate",
            Left = LeftCol, Top = 10, Width = LeftWidth, Height = 62
        };

        _targetAuthor = new ComboBox
        {
            Left = 12, Top = 24, Width = LeftWidth - 30,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        targetGroup.Controls.Add(_targetAuthor);

        // --- the pool ---------------------------------------------------------
        //
        // Chosen rather than fixed, because which authors are in the pool is
        // not a detail - it moved the margin by more than twelvefold in
        // testing. A preset sets the ticks; the ticks are what actually runs,
        // so a preset can be adjusted rather than being a mode to fight.

        var poolGroup = new GroupBox
        {
            Text = "Compare against",
            Left = LeftCol, Top = 80, Width = LeftWidth, Height = 330
        };

        _poolPreset = new ComboBox
        {
            Left = 12, Top = 22, Width = LeftWidth - 30,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _poolPreset.Items.AddRange(new object[]
        {
            "Same author only (no margin - dispersion instead)",
            "Everyone in this language",
            "Custom - tick below"
        });
        _poolPreset.SelectedIndex = 2;

        _poolAuthors = new CheckedListBox
        {
            Left = 12, Top = 52, Width = LeftWidth - 30, Height = 262,
            CheckOnClick = true,
            IntegralHeight = false
        };

        poolGroup.Controls.Add(_poolPreset);
        poolGroup.Controls.Add(_poolAuthors);

        // --- settings ---------------------------------------------------------

        var settingsGroup = new GroupBox
        {
            Text = "Settings",
            Left = LeftCol, Top = 418, Width = LeftWidth, Height = 180
        };

        _foldAccents = new CheckBox
        {
            Text = "Fold accents (ἦ ἥ ᾗ -> η)",
            Left = 12, Top = 22, Width = 240, Checked = true
        };

        var sampleLabel = new Label { Text = "Sample size (tokens):", Left = 12, Top = 52, Width = 140 };
        _sampleSize = new NumericUpDown
        {
            Left = 158, Top = 48, Width = 80,
            Minimum = 500, Maximum = 20000, Increment = 250, Value = 2500
        };

        var featureLabel = new Label { Text = "Most frequent words:", Left = 12, Top = 82, Width = 140 };
        _featureCount = new NumericUpDown
        {
            Left = 158, Top = 78, Width = 80,
            Minimum = 20, Maximum = 1000, Increment = 10, Value = 150
        };

        // Off by default because every saved run in the database was produced
        // with the held-out work contributing to the normalisation, and a
        // default that silently disagreed with the archive would make old and
        // new results quietly incomparable. Measured at under 1% on a
        // nineteen-work Euripides pool - worth having, not worth assuming.
        _excludeHeldOut = new CheckBox
        {
            Text = "Held-out work excluded from normalisation",
            Left = 12, Top = 110, Width = 290
        };

        _excludeNonCompositions = new CheckBox
        {
            Text = "Skip fragment collections and indices",
            Left = 12, Top = 136, Width = 290, Checked = true
        };

        settingsGroup.Controls.Add(_excludeNonCompositions);
        settingsGroup.Controls.Add(_foldAccents);
        settingsGroup.Controls.Add(sampleLabel);
        settingsGroup.Controls.Add(_sampleSize);
        settingsGroup.Controls.Add(featureLabel);
        settingsGroup.Controls.Add(_featureCount);
        settingsGroup.Controls.Add(_excludeHeldOut);

        _runButton = new Button
        {
            Text = "Run validation",
            Left = LeftCol, Top = 608, Width = 200, Height = 32
        };

        _cancelButton = new Button
        {
            Text = "Stop",
            Left = LeftCol + 208, Top = 608, Width = 100, Height = 32,
            Enabled = false
        };

        // The grid inherits the author and pool chosen here rather than asking
        // again. Two windows offering the same pool picker would be two ways to
        // build a pool, and a stability sweep run against a different pool from
        // the validation it is meant to be testing would be quietly worthless.
        _gridButton = new Button
        {
            Text = "Test parameter stability...",
            Left = LeftCol, Top = 648, Width = 320, Height = 32
        };
        _gridButton.Click += (_, _) => OpenGrid();

        _perturbButton = new Button
        {
            Text = "Perturbation series...",
            Left = LeftCol, Top = 686, Width = 320, Height = 32
        };
        _perturbButton.Click += async (_, _) => await OpenPerturbationAsync();

        // --- results ----------------------------------------------------------

        _summary = new Label
        {
            Left = RightCol, Top = 10, Width = RightWidth, Height = 112,
            Text = "Pick an author and a comparison pool, then run.",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _results = new ListView
        {
            Left = RightCol, Top = 128, Width = RightWidth, Height = 580,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _results.Columns.Add("Work", 230);
        _results.Columns.Add("Margin", 80, HorizontalAlignment.Right);
        _results.Columns.Add("Own author", 80, HorizontalAlignment.Right);
        _results.Columns.Add("Others", 80, HorizontalAlignment.Right);
        _results.Columns.Add("Floor", 70, HorizontalAlignment.Right);
        _results.Columns.Add("Nearest", 120);
        _results.Columns.Add("Rank", 50, HorizontalAlignment.Right);
        _results.Columns.Add("Samples", 60, HorizontalAlignment.Right);
        _results.Columns.Add("Tokens", 70, HorizontalAlignment.Right);
        ReadingTheme.EnableThemedHeader(_results);

        _status = new Label
        {
            Left = LeftCol, Top = 726, Width = 1120, Height = 36,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(targetGroup);
        Controls.Add(poolGroup);
        Controls.Add(settingsGroup);
        Controls.Add(_runButton);
        Controls.Add(_cancelButton);
        Controls.Add(_gridButton);
        Controls.Add(_perturbButton);
        Controls.Add(_summary);
        Controls.Add(_results);
        Controls.Add(_status);

        _poolPreset.SelectedIndexChanged += (_, _) => ApplyPreset();

        // Ticking anything by hand makes the run custom, whatever the dropdown
        // last said. Without this the header reads "Same author only" over a
        // pool holding three authors, which is worse than no label at all -
        // the pool is the single biggest lever on the margin, and a screenshot
        // of a result should say what it was measured against.
        _poolAuthors.ItemCheck += (_, _) =>
        {
            if (_suppressPresetReset) return;
            BeginInvoke(() => _poolPreset.SelectedIndex = CustomPresetIndex);
        };
        _targetAuthor.SelectedIndexChanged += (_, _) => ApplyPreset();
        ResultExport.AttachTo(_results, "validation", notes: () => new[]
        {
            $"Classica Codex leave-one-out validation - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Author: {_targetAuthor.SelectedItem}",
            $"Sample size: {_sampleSize.Value} tokens, {_featureCount.Value} most frequent words, " +
            $"accents {(_foldAccents.Checked ? "folded" : "unfolded")}",
            _excludeHeldOut.Checked
                ? "Held-out work excluded from normalisation."
                : "Held-out work contributed to normalisation, as saved runs do."
        });

        _runButton.Click += async (_, _) => await RunAsync();
        _cancelButton.Click += (_, _) => _cancellation?.Cancel();
        _results.DoubleClick += async (_, _) => await OpenSelectedAsync();

        Load += async (_, _) => await LoadWorksAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadWorksAsync()
    {
        var editions = await _editionRepo.GetAllOriginalEditionsAsync();

        _allWorks = editions
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

        // Only authors with enough works to hold one out and still have
        // company. Two works means every run compares a work against one other,
        // which is a number rather than a distribution.
        var eligible = _allWorks
            .GroupBy(w => w.AuthorName)
            .Where(g => g.Count() >= 3)
            .Select(g => g.Key)
            .OrderBy(a => a)
            .ToList();

        _targetAuthor.Items.AddRange(eligible.Cast<object>().ToArray());
        if (_targetAuthor.Items.Count > 0) _targetAuthor.SelectedIndex = 0;

        _status.Text = $"{_allWorks.Count} original-language works, {eligible.Count} authors with three or more.";
    }

    /// <summary>
    /// Fills the tick list for the chosen target, and applies whatever the
    /// preset says. The list is always the authors sharing the target's
    /// language - comparing Greek against Latin measures the alphabet.
    /// </summary>
    private void ApplyPreset()
    {
        if (_targetAuthor.SelectedItem is not string target) return;

        var language = _allWorks.FirstOrDefault(w => w.AuthorName == target)?.Language;
        if (language == null) return;

        var authors = _allWorks
            .Where(w => w.Language == language && w.AuthorName != target)
            .GroupBy(w => w.AuthorName)
            .Where(g => g.Count() >= 1)
            .Select(g => (Author: g.Key, Works: g.Count()))
            .OrderBy(a => a.Author)
            .ToList();

        _poolAuthors.Items.Clear();
        foreach (var (author, works) in authors)
            _poolAuthors.Items.Add($"{author}  ({works})");

        _suppressPresetReset = true;
        try
        {
            switch (_poolPreset.SelectedIndex)
            {
                case 0: // same author only
                    for (var i = 0; i < _poolAuthors.Items.Count; i++) _poolAuthors.SetItemChecked(i, false);
                    break;
                case 1: // everyone
                    for (var i = 0; i < _poolAuthors.Items.Count; i++) _poolAuthors.SetItemChecked(i, true);
                    break;
            }
        }
        finally
        {
            _suppressPresetReset = false;
        }
    }

    private List<WorkItem> BuildPool(string targetAuthor)
    {
        var language = _allWorks.First(w => w.AuthorName == targetAuthor).Language;

        var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var index in _poolAuthors.CheckedIndices.Cast<int>())
        {
            var label = (string)_poolAuthors.Items[index];
            chosen.Add(label[..label.LastIndexOf("  (", StringComparison.Ordinal)]);
        }

        var pool = _allWorks
            .Where(w => w.Language == language)
            .Where(w => w.AuthorName == targetAuthor || chosen.Contains(w.AuthorName))
            .ToList();

        // No exemption for the target author, which is the one place this
        // differs from StylometryForm. There, analysing Fragmenta deliberately
        // is a reasonable thing to ask for, so the filter spares the work the
        // user selected. Here nobody selects a work - the sweep holds out every
        // work the author has - so an exemption would only mean silently
        // validating against a compilation.
        if (_excludeNonCompositions.Checked)
            pool = pool.Where(w => !IsNonComposition(w)).ToList();

        return pool;
    }

    private async Task<IReadOnlyList<string>> TokensForAsync(WorkItem work, bool fold)
    {
        if (_tokenCache.TryGetValue((work.WorkId, fold), out var cached)) return cached;

        var nodes = await _textNodeRepo.GetByEditionAsync(work.EditionId, readingLinesOnly: true);
        var text = string.Join(' ', nodes.Select(n => n.Text));
        var tokens = StylometryTokenizer.Tokenize(text, fold);

        _tokenCache[(work.WorkId, fold)] = tokens;
        return tokens;
    }

    private async Task RunAsync()
    {
        if (_targetAuthor.SelectedItem is not string target) return;

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

        _filteredOut = _excludeNonCompositions.Checked
            ? _allWorks
                .Where(w => w.Language == language && IsNonComposition(w))
                .Where(w => w.AuthorName == target || pool.Any(p => p.AuthorName == w.AuthorName))
                .Select(w => w.WorkTitle)
                .OrderBy(t => t)
                .ToList()
            : new List<string>();

        if (!pool.Any(w => w.AuthorName == target))
        {
            MessageBox.Show(this,
                $"Every work by {target} is filtered out as a compilation, so there is nothing to " +
                "hold out." + Environment.NewLine + Environment.NewLine +
                "Untick \"Skip fragment collections and indices\" to validate against them anyway - " +
                "but a recovery rate over fragment collections measures anthologising practice " +
                "rather than authorship.",
                "Nothing to validate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (otherAuthors < 1)
        {
            MessageBox.Show(this,
                "A margin compares a work's own author against other authors, so at least one other " +
                "author has to be ticked." + Environment.NewLine + Environment.NewLine +
                "A single-author pool is a legitimate question - how far apart an author's own works " +
                "sit - but it is dispersion rather than recovery, and it is not what this bench " +
                "measures.",
                "Pool needs a second author", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var fold = _foldAccents.Checked;
        var settings = new DeltaSettings((int)_featureCount.Value, (int)_sampleSize.Value);

        _runButton.Enabled = false;
        _cancelButton.Enabled = true;
        _results.Items.Clear();
        _cancellation = new CancellationTokenSource();

        try
        {
            var tokenized = new List<WorkTokens>(pool.Count);
            for (var i = 0; i < pool.Count; i++)
            {
                _cancellation.Token.ThrowIfCancellationRequested();
                _status.Text = $"Reading {i + 1} of {pool.Count}: {pool[i].WorkTitle}...";
                await Task.Yield();

                var tokens = await TokensForAsync(pool[i], fold);
                tokenized.Add(new WorkTokens(pool[i].WorkId, pool[i].AuthorName, pool[i].WorkTitle, tokens));
            }

            _status.Text = "Validating...";

            var token = _cancellation.Token;
            var result = await Task.Run(() => LeaveOneOutValidator.Validate(
                tokenized, target, settings,
                _excludeHeldOut.Checked,
                progress: null,
                cancellation: token), token);

            Show(result);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            _status.Text = "Validation failed - see message.";
            MessageBox.Show(this, ex.Message, "Validation failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _runButton.Enabled = true;
            _cancelButton.Enabled = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Show(ValidationResult result)
    {
        // Worst margin first. The works that nearly failed are the ones worth
        // reading, and a list sorted best-first buries them under eighteen
        // rows of agreement.
        foreach (var w in result.Works.OrderBy(w => w.Margin))
        {
            var row = new ListViewItem(w.WorkTitle);
            row.SubItems.Add(StatFormat.Signed3(w.Margin));
            row.SubItems.Add(w.MeanDeltaSameAuthor.ToString("0.000"));
            row.SubItems.Add(w.MeanDeltaOtherAuthor.ToString("0.000"));
            row.SubItems.Add(w.DeltaFloor.ToString("0.000"));
            row.SubItems.Add(w.NearestAuthor);
            row.SubItems.Add(w.CorrectAuthorRank?.ToString() ?? "-");
            row.SubItems.Add(w.SamplesMeasured.ToString());
            row.SubItems.Add(w.TokenCount.ToString("N0"));
            row.Tag = w.WorkId;

            if (!w.Recovered)
                row.ForeColor = ReadingTheme.IsDark ? Color.FromArgb(240, 140, 130) : Color.DarkRed;
            else if (!string.Equals(w.NearestAuthor, result.TargetAuthor, StringComparison.OrdinalIgnoreCase))
                row.ForeColor = ReadingTheme.MutedText;

            _results.Items.Add(row);
        }

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
            Environment.NewLine +
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

    /// <summary>
    /// Hands the grid the same author, the same pool and the same token cache.
    ///
    /// The delegate is what matters: the grid tokenises once per accent-folding
    /// setting rather than once per cell, and because it goes through this
    /// form's cache, a grid run straight after a validation at the same folding
    /// reads nothing from the database at all.
    /// </summary>
    private void OpenGrid()
    {
        if (_targetAuthor.SelectedItem is not string target) return;

        var pool = BuildPool(target);
        if (pool.Select(w => w.AuthorName).Distinct().Count() < 2)
        {
            MessageBox.Show(this,
                "Tick at least one other author first - the grid runs the same validation at " +
                "different settings, and a margin needs a second author.",
                "Pool needs a second author", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var summary =
            $"{target} against {string.Join(", ", pool.Select(w => w.AuthorName).Distinct().Where(a => a != target))} " +
            $"({pool.Count} works)";

        using var grid = new ParameterGridForm(target, summary, async (fold, cancellation) =>
        {
            var tokenized = new List<WorkTokens>(pool.Count);
            foreach (var w in pool)
            {
                cancellation.ThrowIfCancellationRequested();
                tokenized.Add(new WorkTokens(w.WorkId, w.AuthorName, w.WorkTitle,
                    await TokensForAsync(w, fold)));
            }
            return tokenized;
        });

        grid.ShowDialog(this);
    }

    /// <summary>
    /// Opens the perturbation bench on the same author and pool.
    ///
    /// Tokenises here rather than passing a delegate, because unlike the grid
    /// the perturbation series never varies accent folding - it holds every
    /// preprocessing choice fixed and moves only the contamination, which is
    /// the entire reason it can be read when a comparison of works cannot.
    /// </summary>
    private async Task OpenPerturbationAsync()
    {
        if (_targetAuthor.SelectedItem is not string target) return;

        var pool = BuildPool(target);
        if (pool.Select(w => w.AuthorName).Distinct().Count() < 2)
        {
            MessageBox.Show(this,
                "Tick at least one other author first - a perturbation series needs donor material " +
                "and something to measure the contaminated work against.",
                "Pool needs a second author", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _perturbButton.Enabled = false;
        try
        {
            var fold = _foldAccents.Checked;
            var tokenized = new List<WorkTokens>(pool.Count);

            for (var i = 0; i < pool.Count; i++)
            {
                _status.Text = $"Reading {i + 1} of {pool.Count}: {pool[i].WorkTitle}...";
                await Task.Yield();
                tokenized.Add(new WorkTokens(pool[i].WorkId, pool[i].AuthorName, pool[i].WorkTitle,
                    await TokensForAsync(pool[i], fold)));
            }

            _status.Text = "Ready.";

            using var bench = new PerturbationForm(target, tokenized);
            bench.ShowDialog(this);
        }
        finally
        {
            _perturbButton.Enabled = true;
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (_results.SelectedItems.Count == 0 || OnOpenWork == null) return;
        if (_results.SelectedItems[0].Tag is not int workId) return;

        await OnOpenWork(workId);
        Close();
    }
}
