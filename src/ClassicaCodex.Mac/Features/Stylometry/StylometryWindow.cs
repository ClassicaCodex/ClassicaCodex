using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Core.Stylometry;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Runs Burrows' Delta - the standard statistical method real classical
/// philologists use for disputed-authorship questions (the Homeric Question,
/// spurious Platonic dialogues, pseudo-Aristotelian works) - against the
/// ingested original-language corpus. It measures HOW a text is written
/// (frequency distribution of its most common words - largely function
/// words: particles, conjunctions, pronouns) rather than WHAT it says, which
/// is what makes it useful for authorship questions instead of just topic
/// similarity.
///
/// Deliberately scoped to original-language text only (never translations):
/// running this on English translations would mostly measure which 19th/20th
/// century translator worked on which text, not the ancient author's own
/// style - a real methodological trap, so it's avoided here rather than
/// producing a plausible-looking but meaningless result.
///
/// The Mac counterpart of StylometryForm, setting for setting. What changes:
/// the reading and counting run off the UI thread with a progress bar and a
/// Stop button, the list of works can be filtered, and a batch reads the pool
/// once rather than once per work. docs/stylometry-notes.md says what the
/// numbers can and cannot tell you.
/// </summary>
public sealed class StylometryWindow : FeatureWindow
{
    internal sealed class WorkItem
    {
        public int WorkId;
        public int EditionId;
        public string AuthorName = string.Empty;
        public string WorkTitle = string.Empty;
        public string Language = string.Empty;
        public override string ToString() => $"[{Language}] {AuthorName}, {WorkTitle}";
    }

    /// <summary>
    /// How many neighbours the table shows, as on Windows. The export and the
    /// saved run carry the whole list - the reference-distribution analysis
    /// needs to find the first outsider wherever it falls.
    /// </summary>
    private const int ShownNeighbours = 20;

    /// <summary>
    /// Elision-mark stripping is not optional, unlike accent folding, so it is
    /// a constant rather than a control. It is recorded on saved runs anyway,
    /// because if it ever does become optional the runs saved before that point
    /// need to say which behaviour produced them.
    /// </summary>
    private const bool StripElisionMarksAlways = true;

    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly StylometryRunRepository _runRepo = new();

    private readonly TextBox _filterBox;
    private readonly ListBox _workList;
    private readonly CheckBox _foldAccentsCheck;
    private readonly CheckBox _excludeNonCompositionsCheck;
    private readonly NumericUpDown _featureCountInput;
    private readonly NumericUpDown _minTokensInput;
    private readonly NumericUpDown _chunkSizeInput;
    private readonly Button _analyzeButton;
    private readonly Button _stopButton;
    private readonly Button _saveRunButton;
    private readonly Button _batchButton;
    private readonly Button _validateButton;
    private readonly SelectableTextBlock _statusLabel;
    private readonly ProgressBar _progress;
    private readonly TextBlock _progressLabel;
    private readonly DataGrid _resultsGrid;
    private readonly TextBlock _resultsCount;
    private readonly FingerprintCanvas _fingerprintCanvas;

    private readonly List<WorkItem> _allWorks = new();
    private readonly int? _preselectWorkId;

    private List<(int WorkId, string AuthorName, string WorkTitle, double Delta)> _currentResults = new();
    private List<(string Word, double Frequency)> _currentFingerprint = new();

    // The target and pool size behind _currentResults. Held so Save records
    // what actually produced the numbers on screen rather than whatever the
    // controls happen to say at save time - the user can change the settings
    // after running, and a run saved with the wrong settings is worse than no
    // run at all.
    private WorkItem? _lastRunTarget;
    private StylometrySettings? _lastRunSettings;
    private int _lastRunPoolSize;
    private int _lastRunTokenCount;
    private string _lastRunPoolSummary = string.Empty;
    private string _lastRunChunkNote = string.Empty;
    private long? _lastSavedRunId;

    private readonly ConcurrentDictionary<int, int> _tokenEstimateCache = new();

    private CancellationTokenSource? _cancellation;
    private bool _running;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    public StylometryWindow(int? preselectWorkId = null)
    {
        _preselectWorkId = preselectWorkId;
        Confirm = (title, message) => Alert.Confirm(this, title, message, "Run Batch");

        Title = "Stylometric Fingerprint (Burrows' Delta)";
        Width = 1200;
        Height = 840;
        MinWidth = 980;
        MinHeight = 680;

        // LAYOUT
        //
        // Two columns. Left is everything you set before a run; right is
        // everything a run produces. The settings are grouped because the
        // distinction matters when reading a result: one group changes how
        // text is counted, the other changes which texts are counted at all.
        // A surprising Delta usually traces to one or the other, and the
        // grouping is the first place to look.

        _filterBox = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Filter by author or title",
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Search"), Width = 13, Height = 13, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6 },
            Margin = new Thickness(0, 6, 0, 8)
        };
        _filterBox.TextChanged += (_, _) => RefreshWorkList();

        _workList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4),
            ItemTemplate = new FuncDataTemplate<WorkItem?>((w, _) => w == null ? null : BuildWorkRow(w), supportsRecycling: false)
        };
        _workList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 4)),
                new Setter(ListBoxItem.MinHeightProperty, 0d)
            }
        });
        _workList.DoubleTapped += (_, _) => _pending = RunAnalysisAsync();

        var worksPane = ToolPane.Build("ORIGINAL-LANGUAGE WORKS · PICK ONE TO ANALYZE", _workList);

        // --- How text is counted ---------------------------------------------
        //
        // Both settings materially move the result, which is why they are
        // controls rather than constants: flipping one and re-running is the
        // difference between testing an assumption and making one.
        //
        // On the feature count: Burrows' original used 150; 100-1000 is the
        // usual range. Values near 60 are small enough that rank order among
        // close neighbours shifts on its own.
        //
        // On accent folding: with it on, ἦ / ἥ / ᾗ collapse to a single token,
        // which removes inconsistent accentuation across Perseus editions but
        // merges genuinely distinct function words. With it off, the
        // distinctions survive along with whatever inconsistency the editions
        // carry. Neither is obviously right - run both and compare rank order.
        // Stable ordering means the result is robust to the choice; a flip
        // means it was orthography.
        _foldAccentsCheck = new CheckBox { Content = "Fold accents (ἦ ἥ ᾗ -> η)", IsChecked = true };
        _featureCountInput = Bench.Number(20, 1000, 10, 150);

        var textGroup = Bench.Group("How text is counted", new StackPanel
        {
            Spacing = 6,
            Children = { _foldAccentsCheck, Bench.Field("Most frequent words:", _featureCountInput, 214) }
        });

        // --- Which works are compared ----------------------------------------
        //
        // Both filters on by default. The corpus demonstrably contains indices,
        // commentaries and fragment collections, and every one of them was
        // contributing to the mean and standard deviation of every feature.
        _excludeNonCompositionsCheck = new CheckBox { Content = "Skip fragment collections and indices", IsChecked = true };

        // 2,500 is the lower bound Eder found for stable attribution (Latin
        // prose; most corpora need closer to 5,000). Below it a work's Delta
        // says more about its length than its author - and it still shapes the
        // distribution every other work is measured against.
        _minTokensInput = Bench.Number(0, 50000, 500, 2500);

        // Equalises the length of every comparison unit, which is the only way
        // to answer an authorship question on a corpus where depth to first
        // outsider tracks length. 0 disables it.
        //
        // 3,000 keeps all nineteen Euripides works in play (the shortest,
        // Cyclops, has 4,140 tokens) and sits above Eder's 2,500-token floor
        // for stable attribution. Raising it produces fewer, cleaner samples
        // and eventually starts dropping short works entirely.
        _chunkSizeInput = Bench.Number(0, 20000, 500, 3000);

        var poolGroup = Bench.Group("Which works to compare against", new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _excludeNonCompositionsCheck,
                Bench.Field("Minimum length (tokens):", _minTokensInput, 214),
                Bench.Field("Sample size (0 = whole works):", _chunkSizeInput, 214)
            }
        });

        // --- Actions -----------------------------------------------------------
        //
        // Ordered by how often each is used.

        _analyzeButton = Bench.Button("Analyze Style", "Stylometry", Bench.WithChord("Analyze the selected work", "R"));
        _analyzeButton.Classes.Add("accent");
        _analyzeButton.IsDefault = true;
        _analyzeButton.Click += (_, _) => _pending = RunAnalysisAsync();

        _stopButton = Bench.Button("Stop", "Stop", Bench.WithChord("Stop the run", "."));
        _stopButton.IsEnabled = false;
        _stopButton.Click += (_, _) => _cancellation?.Cancel();

        // Disabled until there is a result to save. Saving before running would
        // write an empty run, and an empty run in a reference distribution is
        // indistinguishable from a real one until someone works out why the
        // numbers look wrong.
        _saveRunButton = Bench.Button("Save Run", "Download", Bench.WithChord("Save this run for Compare Saved Runs", "S"));
        _saveRunButton.IsEnabled = false;
        _saveRunButton.Click += (_, _) => _pending = SaveCurrentRunAsync();

        _batchButton = Bench.Button("Run Whole Author…", "Author", "Run and save every work by the selected work's author, at these settings");
        _batchButton.Click += (_, _) => _pending = RunBatchForAuthorAsync();

        // The validation bench. A separate window rather than a tab here, for
        // the same reason Compare Saved Runs is: this window answers "what is
        // the Delta for this work", the bench answers "should I believe a
        // Delta at these settings at all". They are asked at different
        // moments, and the second one should be asked first.
        _validateButton = Bench.Button("Validate Settings…", "Validation", "Can these settings recover texts whose authorship is not in question?");
        _validateButton.Click += (_, _) => new ValidationWindow { OnOpenWork = OnOpenWork, OnNavigate = OnNavigate }.Show(this);

        var compareButton = Bench.Button("Compare Saved Runs…", "StylometryCompare", "Many saved runs side by side");
        compareButton.Click += (_, _) => new StylometryAnalysisWindow().Show(this);

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto,8,Auto")
        };
        Bench.Place(actions, _analyzeButton, 0, 0, span: 3);
        Bench.Place(actions, _stopButton, 2, 0);
        Bench.Place(actions, _saveRunButton, 2, 2);
        Bench.Place(actions, _batchButton, 4, 0);
        Bench.Place(actions, _validateButton, 4, 2);
        Bench.Place(actions, compareButton, 6, 0, span: 3);

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), Width = 390 };
        Grid.SetRow(worksPane, 1);
        textGroup.Margin = new Thickness(0, 12, 0, 0);
        poolGroup.Margin = new Thickness(0, 10, 0, 0);
        actions.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(textGroup, 2);
        Grid.SetRow(poolGroup, 3);
        Grid.SetRow(actions, 4);
        left.Children.Add(_filterBox);
        left.Children.Add(worksPane);
        left.Children.Add(textGroup);
        left.Children.Add(poolGroup);
        left.Children.Add(actions);

        // --- Right column: results ---------------------------------------------

        _statusLabel = Bench.Report("Pick a work, then Analyze Style.");
        _progress = Bench.Progress();
        _progressLabel = Bench.Status();
        _progressLabel.IsVisible = false;
        var notice = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(14, 10),
            Child = new StackPanel { Children = { _statusLabel, _progress, _progressLabel } }
        };

        _resultsGrid = BenchTable.Create();
        _resultsGrid.Columns.Add(BenchTable.Column<NeighbourRow>("#", r => r.Rank.ToString(CultureInfo.CurrentCulture), r => r.Rank, 52, numeric: true));
        _resultsGrid.Columns.Add(BenchTable.Column<NeighbourRow>("Delta", r => r.Delta.ToString("F3", CultureInfo.CurrentCulture), r => r.Delta, 84, numeric: true));
        _resultsGrid.Columns.Add(BenchTable.Column<NeighbourRow>("Author", r => r.AuthorName, width: 190));
        _resultsGrid.Columns.Add(BenchTable.Column<NeighbourRow>("Work", r => Bench.WithoutAuthor(r.AuthorName, r.WorkTitle), star: true));
        _resultsGrid.DoubleTapped += async (_, _) => await OpenSelectedResultAsync();
        _resultsGrid.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSelectedResultAsync();
            }
        };
        AttachResultsExport();

        _resultsCount = Bench.Status();
        var resultsPane = ToolPane.Build(
            "MOST STYLISTICALLY SIMILAR · LOWER DELTA = CLOSER MATCH · DOUBLE-CLICK TO OPEN",
            _resultsGrid,
            footer: _resultsCount);

        _fingerprintCanvas = new FingerprintCanvas { Margin = new Thickness(6, 6, 6, 6) };
        var fingerprintCaption = new TextBlock
        {
            Text = "Its most common words - mostly function words, that's expected.",
            Classes = { "caption" },
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var fingerprintPane = ToolPane.Build(
            "WORD-FREQUENCY FINGERPRINT",
            new ScrollViewer { Content = _fingerprintCanvas },
            fingerprintCaption);

        // The neighbours and the fingerprint share the column, split where a
        // reader can move it: fifteen rows of bars at a comfortable height by
        // default, and the table takes the rest.
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*,12,332") };
        right.RowDefinitions[1].MinHeight = 160;
        right.RowDefinitions[3].MinHeight = 140;
        notice.Margin = new Thickness(0, 0, 0, 12);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(resultsPane, 1);
        Grid.SetRow(splitter, 2);
        Grid.SetRow(fingerprintPane, 3);
        right.Children.Add(notice);
        right.Children.Add(resultsPane);
        right.Children.Add(splitter);
        right.Children.Add(fingerprintPane);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,20,*") };
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var heading = Bench.Heading(
            "Stylometric fingerprint",
            "Burrows' Delta over the original-language texts: how a work is written, measured by its commonest " +
            "words, rather than what it says. Translations are left out - on them this would measure the translator. " +
            "Delta measures similarity of word-frequency profile; it does not measure authorship.");

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 20) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        layout.Children.Add(columns);
        Content = layout;

        Bench.Chord(this, Key.R, _analyzeButton, () => _pending = RunAnalysisAsync());
        Bench.Chord(this, Key.OemPeriod, _stopButton, () => _cancellation?.Cancel());
        Bench.Chord(this, Key.S, _saveRunButton, () => _pending = SaveCurrentRunAsync());

        Opened += (_, _) => _pending = LoadWorksAsync();
        Closed += (_, _) =>
        {
            // A run nobody can see any more is stopped rather than left to
            // write into a closed window - or, for a batch, to go on replacing
            // saved runs with nobody watching.
            _closed = true;
            _cancellation?.Cancel();
        };
    }

    // For the tests, which drive the window as a reader would.
    internal IReadOnlyList<WorkItem> Works => _allWorks;
    internal ListBox WorkList => _workList;
    internal IReadOnlyList<(int WorkId, string AuthorName, string WorkTitle, double Delta)> Results => _currentResults;
    internal FingerprintCanvas Fingerprint => _fingerprintCanvas;
    internal DataGrid ResultsGrid => _resultsGrid;
    internal string StatusText => _statusLabel.Text ?? string.Empty;
    internal long? LastSavedRunId => _lastSavedRunId;
    internal NumericUpDown ChunkSizeInput => _chunkSizeInput;
    internal NumericUpDown MinTokensInput => _minTokensInput;
    internal NumericUpDown FeatureCountInput => _featureCountInput;
    internal TextBox FilterBox => _filterBox;
    internal bool CanSave => _saveRunButton.IsEnabled;

    /// <summary>Asks before a batch replaces saved runs. The tests answer it themselves.</summary>
    internal Func<string, string, Task<bool>> Confirm { get; set; }

    internal Task AnalyzeAsync() => _pending = RunAnalysisAsync();

    internal Task SaveRunAsync() => _pending = SaveCurrentRunAsync();

    internal Task BatchAsync() => _pending = RunBatchForAuthorAsync();

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

    internal void Select(WorkItem work)
    {
        _filterBox.Text = string.Empty;
        RefreshWorkList();
        _workList.SelectedItem = work;
    }

    private Control BuildWorkRow(WorkItem w)
    {
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        text.Inlines!.Add(PassageText.Ink(text, $"{w.Language}  ", "Cc.InkFaint", FontFamily.Default, 11));
        text.Inlines.Add(new Avalonia.Controls.Documents.Run($"{w.AuthorName}, {w.WorkTitle}"));
        ToolTip.SetTip(text, w.ToString());
        ToolTip.SetShowDelay(text, 900);
        return text;
    }

    private async Task LoadWorksAsync()
    {
        try
        {
            // Off the UI thread: Microsoft.Data.Sqlite's async calls run
            // synchronously, and this one reads every original edition.
            var editions = await Task.Run(() => _editionRepo.GetAllOriginalEditionsAsync());
            if (_closed) return;

            _allWorks.Clear();
            foreach (var e in editions.Where(e => !string.IsNullOrEmpty(e.Language)))
            {
                _allWorks.Add(new WorkItem
                {
                    WorkId = e.WorkId,
                    EditionId = e.EditionId,
                    AuthorName = e.AuthorName,
                    WorkTitle = e.WorkTitle,
                    Language = e.Language!
                });
            }

            RefreshWorkList();

            if (_allWorks.Count == 0)
            {
                SetStatus("No original-language editions found - ingest some texts first.");
                return;
            }

            // The work open in the reader, if it has an original: what someone
            // opening this from a text they are reading most likely means.
            if (_preselectWorkId is { } id && _allWorks.FirstOrDefault(w => w.WorkId == id) is { } open)
            {
                _workList.SelectedItem = open;
                _workList.ScrollIntoView(open);
            }
        }
        catch (Exception ex)
        {
            if (!_closed) SetStatus($"Couldn't load the works: {ex.Message}");
        }
    }

    /// <summary>
    /// Narrows what the list shows. Only what it shows: the pool is always
    /// built from every work loaded, so a filter typed to find one play
    /// cannot quietly become the comparison set.
    /// </summary>
    private void RefreshWorkList()
    {
        var selected = _workList.SelectedItem;
        var filter = _filterBox.Text?.Trim() ?? string.Empty;

        _workList.ItemsSource = filter.Length == 0
            ? _allWorks.ToList()
            : _allWorks.Where(w => w.ToString().Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

        if (selected is WorkItem w && ((IEnumerable<WorkItem>)_workList.ItemsSource).Contains(w))
            _workList.SelectedItem = w;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>The settings, read on the UI thread before a run - never from the controls inside one.</summary>
    private sealed record RunOptions(bool FoldAccents, int FeatureCount, int ChunkSize, bool ExcludeNonCompositions, int MinTokens);

    private RunOptions ReadOptions() => new(
        _foldAccentsCheck.IsChecked == true,
        Bench.Read(_featureCountInput),
        Bench.Read(_chunkSizeInput),
        _excludeNonCompositionsCheck.IsChecked == true,
        Bench.Read(_minTokensInput));

    /// <summary>
    /// Applies the pool filters and reports what each removed.
    ///
    /// exemptTarget exists because a single run and a batch want opposite
    /// behaviour here.
    ///
    /// In a single run the target is what the user asked about. Analysing
    /// Euripides' Fragmenta deliberately is reasonable, so the filters must not
    /// remove it - only stop it defining the variance for everything else.
    ///
    /// In a batch the target is just the seed used to pick an author, and the
    /// pool doubles as the list of works to run. Exempting it there means the
    /// seed re-admits itself: seeding a batch on Fragmenta produced nineteen
    /// works plus Fragmenta, so the size of the reference distribution depended
    /// on which work happened to be selected when the button was pressed. A
    /// distribution that changes shape based on an incidental UI selection is
    /// not reproducible, and the difference is invisible in the saved results.
    ///
    /// Runs off the UI thread: the length filter asks the library for every
    /// candidate's character count.
    /// </summary>
    private (List<WorkItem> Pool, string Summary) BuildPool(WorkItem target, RunOptions options, bool exemptTarget = true)
    {
        var all = _allWorks.Where(w => w.Language == target.Language).ToList();

        var pool = all;
        var notes = new List<string>();

        if (options.ExcludeNonCompositions)
        {
            var before = pool.Count;
            pool = pool.Where(w => (exemptTarget && w.WorkId == target.WorkId) || !Compilations.IsNonComposition(w.WorkTitle)).ToList();
            if (before != pool.Count) notes.Add($"{before - pool.Count} fragment/index works");
        }

        if (options.MinTokens > 0)
        {
            var before = pool.Count;
            pool = pool
                .Where(w => (exemptTarget && w.WorkId == target.WorkId) || GetTokenEstimate(w) >= options.MinTokens)
                .ToList();
            if (before != pool.Count) notes.Add($"{before - pool.Count} works under {options.MinTokens:N0} tokens");
        }

        var summary = notes.Count == 0
            ? $"{pool.Count} works in pool"
            : $"{pool.Count} works in pool (excluded {string.Join(", ", notes)})";

        return (pool, summary);
    }

    /// <summary>
    /// Cheap token estimate from stored character length, used only to apply
    /// the minimum-length filter before the expensive tokenisation pass.
    ///
    /// Greek averages roughly 6 characters per token including the separator.
    /// This is an approximation and is used only for a threshold - the exact
    /// counts recorded on saved runs come from the real tokeniser.
    /// </summary>
    private int GetTokenEstimate(WorkItem w) =>
        _tokenEstimateCache.GetOrAdd(w.EditionId, id => _textNodeRepo.GetCharacterCountAsync(id).GetAwaiter().GetResult() / 6);

    private List<string> TokenizeWork(WorkItem work, bool foldAccents)
    {
        // Reading lines only. Delta is a measure over relative word
        // frequencies, so anything in the text that the author did not write
        // competes for the same frequency table - and speaker tags are
        // exactly the kind of very high frequency, very short token the
        // measure weights most heavily. Gorgias is 4.1% speaker
        // abbreviations by word count.
        var nodes = _textNodeRepo
            .GetByEditionAsync(work.EditionId, readingLinesOnly: true)
            .GetAwaiter().GetResult();
        var text = string.Join(' ', nodes.Select(n => n.Text));

        return StylometryTokenizer.Tokenize(text, foldAccents);
    }

    /// <summary>
    /// Reads and tokenises every work in the pool, once per work rather than
    /// once per edition. Called on a worker thread.
    ///
    /// Tokenisation is the expensive half - it reads every node of every work
    /// in the pool from SQLite. It is done once per run and handed to the
    /// engine as data, which is also the seam a batch uses: tokenise the pool
    /// once, then run the engine over it for every target.
    /// </summary>
    private List<WorkTokens> TokenizePool(
        IReadOnlyList<WorkItem> pool, bool foldAccents, IProgress<(int Done, int Total, string Title)> progress,
        CancellationToken cancellation)
    {
        var distinct = pool.GroupBy(w => w.WorkId).Select(g => g.First()).ToList();
        var tokenized = new List<WorkTokens>(distinct.Count);

        for (var i = 0; i < distinct.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            progress.Report((i, distinct.Count, distinct[i].WorkTitle));

            var w = distinct[i];
            tokenized.Add(new WorkTokens(w.WorkId, w.AuthorName, w.WorkTitle, TokenizeWork(w, foldAccents)));
        }

        return tokenized;
    }

    /// <summary>
    /// Runs Burrows' Delta for one target against a tokenised pool.
    ///
    /// The calculation itself lives in DeltaEngine, in Core, because
    /// leave-one-out validation, parameter grids and perturbation experiments
    /// all need to run it thousands of times with no window open. All the
    /// reasoning about sampling - why it draws bags rather than passages, why
    /// chunks are never aggregated back to works, why the target's own chunks
    /// are excluded - is there.
    ///
    /// What stays here is what is specific to this window: turning the result
    /// into the sentence under the chart.
    /// </summary>
    private static (List<(int WorkId, string AuthorName, string WorkTitle, double Delta)> Results,
                    List<(string Word, double Frequency)> Fingerprint,
                    int TargetTokenCount,
                    string ChunkNote)
        ComputeDelta(WorkItem target, IReadOnlyList<WorkTokens> tokenized, int featureWordCount, int chunkSize)
    {
        var result = DeltaEngine.Compute(tokenized, target.WorkId, new DeltaSettings(featureWordCount, chunkSize));

        var results = result.Neighbors
            .Select(n => (n.WorkId, n.AuthorName, WorkTitle: n.Label, n.Delta))
            .ToList();

        var note = chunkSize <= 0
            ? "whole works (no sampling)"
            : $"{result.SampleCount} samples of {chunkSize:N0} tokens from {result.WorkCount} works" +
              (result.WorksTooShort.Count > 0 ? $"; {result.WorksTooShort.Count} works too short" : "") +
              (result.DiscardedTokens > 0 ? $"; {result.DiscardedTokens:N0} tokens unused" : "");

        return (results, result.Fingerprint.ToList(), result.TargetTokenCount, note);
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _analyzeButton.IsEnabled = !running;
        _batchButton.IsEnabled = !running;
        _stopButton.IsEnabled = running;
        if (running) _saveRunButton.IsEnabled = false;

        _progress.IsVisible = running;
        _progressLabel.IsVisible = running;
        if (running)
        {
            _progress.IsIndeterminate = true;
            _progressLabel.Text = string.Empty;
        }
    }

    private IProgress<(int Done, int Total, string Title)> ReadingProgress(string prefix = "") =>
        new Progress<(int Done, int Total, string Title)>(p =>
        {
            if (_closed) return;
            _progress.IsIndeterminate = false;
            _progress.Maximum = p.Total;
            _progress.Value = p.Done;
            _progressLabel.Text = $"{prefix}Reading {p.Done + 1} of {p.Total}: {p.Title}…";
        });

    private async Task RunAnalysisAsync()
    {
        if (_running) return;

        if (_workList.SelectedItem is not WorkItem target)
        {
            await Alert.Info(this, "Nothing selected", "Pick a work first.");
            return;
        }

        // Read the settings HERE, on the UI thread. The work runs on a worker,
        // and the values are captured first and passed in rather than read
        // from the controls inside it.
        var options = ReadOptions();

        SetRunning(true);
        SetStatus("Building the pool…");
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        try
        {
            var (sameLanguage, poolSummary) = await Task.Run(() => BuildPool(target, options), token);
            if (_closed) return;

            if (sameLanguage.Count < 4)
            {
                SetStatus("Pick a work, then Analyze Style.");
                SetRunning(false);
                await Alert.Info(this, "Not enough to compare",
                    $"Only {sameLanguage.Count} {target.Language} work(s) ingested - need at least a handful in the " +
                    "same language to make the comparison meaningful.");
                return;
            }

            SetStatus($"Analyzing - {poolSummary}. This reads full text for each, so it can take a bit.");
            _resultsGrid.ItemsSource = null;
            _resultsCount.Text = string.Empty;

            var progress = ReadingProgress();
            var (results, fingerprint, tokenCount, chunkNote) = await Task.Run(() =>
            {
                var tokenized = TokenizePool(sameLanguage, options.FoldAccents, progress, token);
                return ComputeDelta(target, tokenized, options.FeatureCount, options.ChunkSize);
            }, token);

            if (_closed) return;

            _currentResults = results;
            _currentFingerprint = fingerprint;
            _lastRunTarget = target;
            _lastRunSettings = new StylometrySettings(
                options.FeatureCount, options.FoldAccents,
                StripElisionMarksAlways,
                StylometryRunRepository.CurrentAlgorithmVersion,
                options.ChunkSize);
            _lastRunPoolSize = results.Count + 1;   // neighbours plus the target itself
            _lastRunTokenCount = tokenCount;
            _lastRunPoolSummary = poolSummary;
            _lastRunChunkNote = chunkNote;
            _lastSavedRunId = null;

            ShowResults();
            _fingerprintCanvas.SetData(fingerprint);

            // results.Count, not the pool's size less one: that counts
            // editions, so a pool holding three editions of Ajax reported three
            // more comparisons than were actually made.
            //
            // The settings are echoed back because a Delta figure is only
            // interpretable alongside the preprocessing that produced it, and
            // comparing runs from screenshots is otherwise guesswork.
            var accentNote = options.FoldAccents ? "accents folded" : "accents kept";
            SetStatus(
                $"Compared {target.AuthorName}, {target.WorkTitle} against {results.Count} other {target.Language} works." +
                $"{Environment.NewLine}Settings: {options.FeatureCount} features, {accentNote}, elision marks stripped." +
                $"{Environment.NewLine}Pool: {poolSummary}." +
                $"{Environment.NewLine}Sampling: {chunkNote}.");

            SetRunning(false);
            _saveRunButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SetStatus("Stopped.");
        }
        catch (Exception ex)
        {
            if (_closed) return;
            SetStatus("Analysis failed - see message.");
            SetRunning(false);
            await Alert.Error(this, "Error", ex.Message);
        }
        finally
        {
            if (!_closed && _running) SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void ShowResults()
    {
        _resultsGrid.ItemsSource = _currentResults
            .Take(ShownNeighbours)
            .Select((r, i) => new NeighbourRow(i + 1, r.WorkId, r.AuthorName, r.WorkTitle, r.Delta))
            .ToList();

        _resultsCount.Text = _currentResults.Count > ShownNeighbours
            ? $"Nearest {ShownNeighbours} of {_currentResults.Count:N0} - the export and a saved run carry them all."
            : $"{_currentResults.Count:N0} compared.";
    }

    private async Task SaveCurrentRunAsync()
    {
        if (_lastRunTarget == null || _lastRunSettings == null || _currentResults.Count == 0)
        {
            await Alert.Info(this, "Nothing to save", "Run an analysis first.");
            return;
        }

        var target = _lastRunTarget;
        var settings = _lastRunSettings;
        var poolSize = _lastRunPoolSize;
        var tokenCount = _lastRunTokenCount;
        var results = _currentResults;
        var fingerprint = _currentFingerprint;

        _saveRunButton.IsEnabled = false;
        try
        {
            var runId = await Task.Run(() => _runRepo.SaveRunAsync(
                target.WorkId,
                target.EditionId,
                target.AuthorName,
                target.WorkTitle,
                target.Language,
                settings,
                poolSize,
                tokenCount,
                results,
                fingerprint));

            if (_closed) return;
            _lastSavedRunId = runId;

            // Saved once. The button stays off until the next analysis,
            // because the same run saved twice sits in its reference
            // distribution twice and halves the apparent variance for no
            // reason anyone would notice - the trap the batch clears old runs
            // to avoid.
            SetStatus($"Saved: {target.AuthorName}, {target.WorkTitle} ({settings.ProfileKey}).");
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _saveRunButton.IsEnabled = true;
            await Alert.Error(this, "Save failed", ex.Message);
        }
    }

    /// <summary>
    /// Runs and saves every work by the selected work's author, at the current
    /// settings.
    ///
    /// This exists because a reference distribution needs a dozen-plus runs at
    /// identical settings, and producing those by hand means clicking Analyze,
    /// screenshotting, and transcribing - which is slow, and worse, is the kind
    /// of slow that tempts people to skip the check entirely.
    ///
    /// Existing runs for the same author and settings are cleared first.
    /// Without that, running a batch twice would put each work into its own
    /// reference distribution twice, halving the apparent variance for no
    /// reason anyone would notice. They are cleared after the pool has been
    /// read rather than before, so a batch stopped while reading changes
    /// nothing.
    ///
    /// The pool is tokenised once for the whole batch. Windows re-reads it for
    /// every target - its own comment names hoisting the tokenisation as the
    /// thing to do if the batch ever had to scale - and every target sees the
    /// same pool, so the tokens are the same either way and the runs saved are
    /// identical.
    /// </summary>
    private async Task RunBatchForAuthorAsync()
    {
        if (_running) return;

        if (_workList.SelectedItem is not WorkItem seed)
        {
            await Alert.Info(this, "Nothing selected", "Pick any work by the author you want to batch.");
            return;
        }

        var options = ReadOptions();
        var settings = new StylometrySettings(
            options.FeatureCount, options.FoldAccents, StripElisionMarksAlways,
            StylometryRunRepository.CurrentAlgorithmVersion,
            options.ChunkSize);

        // Same filters as a single run. A reference distribution built from a
        // differently-composed pool than the runs it is compared against would
        // be quietly meaningless.
        SetRunning(true);
        SetStatus("Building the pool…");
        List<WorkItem> pool;
        string poolSummary;
        try
        {
            (pool, poolSummary) = await Task.Run(() => BuildPool(seed, options, exemptTarget: false));
        }
        catch (Exception ex)
        {
            if (_closed) return;
            SetRunning(false);
            SetStatus("Batch failed - see message.");
            await Alert.Error(this, "Batch failed", ex.Message);
            return;
        }

        if (_closed) return;
        SetRunning(false);
        SetStatus("Pick a work, then Analyze Style.");

        var targets = pool
            .Where(w => w.AuthorName == seed.AuthorName)
            .GroupBy(w => w.WorkId)
            .Select(g => g.First())
            .ToList();

        if (targets.Count < 3)
        {
            await Alert.Info(this, "Not enough works",
                $"Only {targets.Count} work(s) by {seed.AuthorName} are ingested. A reference " +
                "distribution needs at least three to mean anything.");
            return;
        }

        var confirmed = await Confirm(
            "Run batch",
            $"Run and save all {targets.Count} works by {seed.AuthorName} at {settings.Describe()}?" +
            Environment.NewLine + $"Pool: {poolSummary}." +
            Environment.NewLine + Environment.NewLine +
            $"Any saved runs for {seed.AuthorName} at this profile will be replaced. Runs for " +
            "other authors are left alone." +
            Environment.NewLine + Environment.NewLine +
            "This reads the whole pool, so expect it to take a while.");

        if (!confirmed || _closed) return;

        SetRunning(true);
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var saved = 0;
        var cleared = false;

        try
        {
            SetStatus($"Batch: reading the pool - {poolSummary}.");

            // Made here, on the UI thread: a Progress reports back to the
            // context it was created on, and one created on the worker would
            // write to the window from there.
            var progress = ReadingProgress();
            var tokenized = await Task.Run(() => TokenizePool(pool, options.FoldAccents, progress, token), token);

            await Task.Run(() => _runRepo.DeleteRunsForAuthorAndSettingsAsync(seed.Language, seed.AuthorName, settings));
            cleared = true;

            for (var i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var target = targets[i];
                SetStatus($"Batch {i + 1} of {targets.Count}: {target.WorkTitle}…");
                _progress.IsIndeterminate = false;
                _progress.Maximum = targets.Count;
                _progress.Value = i;
                _progressLabel.Text = $"{saved} of {targets.Count} saved.";

                await Task.Run(async () =>
                {
                    var (results, fingerprint, tokenCount, _) = ComputeDelta(target, tokenized, options.FeatureCount, options.ChunkSize);

                    await _runRepo.SaveRunAsync(
                        target.WorkId, target.EditionId, target.AuthorName, target.WorkTitle,
                        target.Language, settings, results.Count + 1, tokenCount,
                        results, fingerprint);
                });

                saved++;
            }

            if (_closed) return;
            SetStatus($"Batch complete - {targets.Count} runs saved for {seed.AuthorName} " +
                      $"({settings.ProfileKey}). Open Compare Saved Runs to analyse.");
        }
        catch (OperationCanceledException)
        {
            if (_closed) return;

            // Said in full, because a stopped batch is not the same as one
            // that never ran: the old runs are gone and only some new ones
            // replaced them.
            SetStatus(cleared
                ? $"Batch stopped after {saved} of {targets.Count} works - {saved} run(s) saved for {seed.AuthorName} " +
                  $"({settings.ProfileKey}). The runs it replaced are gone, so that reference distribution is " +
                  "incomplete until the batch is run again."
                : "Batch stopped while reading the pool. Nothing was saved and no saved run was touched.");
        }
        catch (Exception ex)
        {
            if (_closed) return;
            SetStatus("Batch failed - see message.");
            SetRunning(false);
            await Alert.Error(this, "Batch failed", ex.Message);
        }
        finally
        {
            if (!_closed && _running) SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async Task OpenSelectedResultAsync()
    {
        if (_resultsGrid.SelectedItem is not NeighbourRow row || OnOpenWork == null) return;

        await OnOpenWork(row.WorkId);
        Close();
    }

    /// <summary>
    /// The whole neighbour list, not the twenty on screen, at full precision -
    /// with the run's settings, pool and sampling in the notes, because a
    /// Delta figure means nothing apart from the preprocessing that produced
    /// it.
    /// </summary>
    private void AttachResultsExport()
    {
        TableExport.Attach(
            _resultsGrid,
            () => _lastRunTarget == null ? "stylometry" : $"stylometry-{_lastRunTarget.AuthorName}-{_lastRunTarget.WorkTitle}",
            () => NeighbourTable(_currentResults.Select((r, i) => (Rank: i + 1, r.WorkId, r.AuthorName, r.WorkTitle, r.Delta))),
            () => NeighbourTable(_resultsGrid.SelectedItems.OfType<NeighbourRow>()
                .OrderBy(r => r.Rank)
                .Select(r => (r.Rank, r.WorkId, r.AuthorName, r.WorkTitle, r.Delta))),
            ExportNotes);
    }

    private static IReadOnlyList<IReadOnlyList<string>> NeighbourTable(
        IEnumerable<(int Rank, int WorkId, string AuthorName, string WorkTitle, double Delta)> rows)
    {
        var table = new List<IReadOnlyList<string>> { new[] { "Rank", "Author", "Work", "Delta" } };
        table.AddRange(rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Rank.ToString(CultureInfo.InvariantCulture),
            r.AuthorName,
            r.WorkTitle,
            r.Delta.ToString("R", CultureInfo.InvariantCulture)
        }));
        return table;
    }

    private IReadOnlyList<string> ExportNotes()
    {
        var notes = new List<string> { $"Classica Codex stylometry - {DateTime.Now:yyyy-MM-dd HH:mm}" };

        if (_lastRunTarget != null && _lastRunSettings != null)
        {
            notes.Add($"Target: {_lastRunTarget.AuthorName}, {_lastRunTarget.WorkTitle} ({_lastRunTarget.Language}), " +
                      $"{_lastRunTokenCount:N0} tokens");
            notes.Add($"Settings: {_lastRunSettings.Describe()} [{_lastRunSettings.ProfileKey}]");
            notes.Add($"Pool: {_lastRunPoolSummary}.");
            notes.Add($"Sampling: {_lastRunChunkNote}.");
        }

        notes.Add("Lower Delta is a closer word-frequency profile. Delta measures similarity of style, not " +
                  "authorship - see docs/stylometry-notes.md.");
        return notes;
    }

    internal sealed record NeighbourRow(int Rank, int WorkId, string AuthorName, string WorkTitle, double Delta);
}
