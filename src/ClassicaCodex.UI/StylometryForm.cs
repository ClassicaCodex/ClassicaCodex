using System.Text.RegularExpressions;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

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
/// </summary>
public class StylometryForm : Form
{
    private class WorkItem
    {
        public int WorkId;
        public int EditionId;
        public string AuthorName = string.Empty;
        public string WorkTitle = string.Empty;
        public string Language = string.Empty;
        public override string ToString() => $"[{Language}] {AuthorName}, {WorkTitle}";
    }

    private readonly ListBox _workList;
    private readonly Button _analyzeButton;
    private readonly Button _saveRunButton;
    private readonly Button _batchButton;
    private readonly CheckBox _foldAccentsCheck;
    private readonly CheckBox _excludeNonCompositionsCheck;
    private readonly NumericUpDown _minTokensInput;
    private readonly NumericUpDown _chunkSizeInput;
    private readonly NumericUpDown _featureCountInput;
    private readonly Label _statusLabel;
    private readonly ListBox _resultsList;
    private readonly FingerprintCanvas _fingerprintCanvas;

    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

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

    private readonly StylometryRunRepository _runRepo = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, Task>? OnOpenWork { get; set; }

    public StylometryForm()
    {
        Text = "Stylometric Fingerprint (Burrows' Delta)";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 1150;
        Height = 830;
        StartPosition = FormStartPosition.CenterParent;

        // LAYOUT
        //
        // Two columns. Left is everything you set before a run; right is
        // everything a run produces. The settings were previously a single row
        // of controls strung along the bottom of the form, which gave no clue
        // that "Fold accents" and "Min tokens" answer different questions.
        //
        // They are grouped now because the distinction matters when reading a
        // result: one group changes how text is counted, the other changes
        // which texts are counted at all. A surprising Delta usually traces to
        // one or the other, and the grouping is the first place to look.

        const int LeftCol = 12;
        const int LeftWidth = 380;
        const int RightCol = 404;
        const int RightWidth = 730;

        var workLabel = new Label
        {
            Text = "Original-language works (pick one to analyze):",
            Left = LeftCol, Top = 10, Width = LeftWidth
        };

        _workList = new ListBox
        {
            Left = LeftCol,
            Top = 32,
            Width = LeftWidth,
            Height = 430,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        // --- Group 1: how text is counted ------------------------------------
        //
        // Both settings materially move the result, which is why they are
        // controls rather than constants: flipping one and re-running is the
        // difference between testing an assumption and making one.

        var textGroup = new GroupBox
        {
            Text = "How text is counted",
            Left = LeftCol,
            Top = 472,
            Width = LeftWidth,
            Height = 84,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        _foldAccentsCheck = new CheckBox
        {
            Text = "Fold accents (ἦ ἥ ᾗ -> η)",
            Left = 12, Top = 22, Width = 220,
            Checked = true
        };

        var featureCountLabel = new Label
        {
            Text = "Most frequent words:",
            Left = 12, Top = 52, Width = 130
        };

        _featureCountInput = new NumericUpDown
        {
            Left = 148, Top = 48, Width = 70,
            Minimum = 20, Maximum = 1000, Increment = 10, Value = 150
        };

        textGroup.Controls.Add(_foldAccentsCheck);
        textGroup.Controls.Add(featureCountLabel);
        textGroup.Controls.Add(_featureCountInput);

        // --- Group 2: which works are compared -------------------------------
        //
        // Both on by default. The corpus demonstrably contains indices,
        // commentaries and fragment collections, and every one of them was
        // contributing to the mean and standard deviation of every feature.

        var poolGroup = new GroupBox
        {
            Text = "Which works to compare against",
            Left = LeftCol,
            Top = 564,
            Width = LeftWidth,
            Height = 114,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        _excludeNonCompositionsCheck = new CheckBox
        {
            Text = "Skip fragment collections and indices",
            Left = 12, Top = 22, Width = 280,
            Checked = true
        };

        var minTokensLabel = new Label
        {
            Text = "Minimum length (tokens):",
            Left = 12, Top = 52, Width = 150
        };

        // 2,500 is the lower bound Eder found for stable attribution (Latin
        // prose; most corpora need closer to 5,000). Below it a work's Delta
        // says more about its length than its author - and it still shapes the
        // distribution every other work is measured against.
        _minTokensInput = new NumericUpDown
        {
            Left = 168, Top = 48, Width = 80,
            Minimum = 0, Maximum = 50000, Increment = 500, Value = 2500
        };

        var chunkLabel = new Label
        {
            Text = "Sample size (0 = whole works):",
            Left = 12, Top = 82, Width = 180
        };

        // Equalises the length of every comparison unit, which is the only way
        // to answer an authorship question on a corpus where depth to first
        // outsider tracks length. 0 disables it.
        //
        // 3,000 keeps all nineteen Euripides works in play (the shortest,
        // Cyclops, has 4,140 tokens) and sits above Eder's 2,500-token floor
        // for stable attribution. Raising it produces fewer, cleaner samples
        // and eventually starts dropping short works entirely.
        _chunkSizeInput = new NumericUpDown
        {
            Left = 198, Top = 78, Width = 80,
            Minimum = 0, Maximum = 20000, Increment = 500, Value = 3000
        };

        poolGroup.Controls.Add(chunkLabel);
        poolGroup.Controls.Add(_chunkSizeInput);
        poolGroup.Controls.Add(_excludeNonCompositionsCheck);
        poolGroup.Controls.Add(minTokensLabel);
        poolGroup.Controls.Add(_minTokensInput);

        // --- Action row -------------------------------------------------------
        //
        // Ordered by how often each is used, left to right.

        _analyzeButton = new Button
        {
            Text = "Analyze style",
            Left = LeftCol, Top = 686, Width = 110, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _analyzeButton.Click += async (_, _) => await RunAnalysisAsync();

        // Disabled until there is a result to save. Saving before running would
        // write an empty run, and an empty run in a reference distribution is
        // indistinguishable from a real one until someone works out why the
        // numbers look wrong.
        _saveRunButton = new Button
        {
            Text = "Save run",
            Left = LeftCol + 118, Top = 686, Width = 90, Height = 30,
            Enabled = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _saveRunButton.Click += async (_, _) => await SaveCurrentRunAsync();

        _batchButton = new Button
        {
            Text = "Run whole author...",
            Left = LeftCol + 216, Top = 686, Width = 140, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _batchButton.Click += async (_, _) => await RunBatchForAuthorAsync();

        _statusLabel = new Label
        {
            Text = "Pick a work, then Analyze style.",
            Left = LeftCol, Top = 722, Width = LeftWidth, Height = 46,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        // --- Right column: results -------------------------------------------

        var resultsLabel = new Label
        {
            Text = "Most stylistically similar works (lower Delta = closer match; double-click to open):",
            Left = RightCol, Top = 10, Width = RightWidth,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _resultsList = new ListBox
        {
            Left = RightCol, Top = 32, Width = RightWidth, Height = 300,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _resultsList.DoubleClick += async (_, _) => await OpenSelectedResultAsync();

        var fingerprintLabel = new Label
        {
            Text = "Word-frequency fingerprint (its most common words - mostly function words, that's expected):",
            Left = RightCol, Top = 344, Width = RightWidth,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _fingerprintCanvas = new FingerprintCanvas
        {
            Left = RightCol, Top = 366, Width = RightWidth, Height = 374,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(workLabel);
        Controls.Add(_workList);
        Controls.Add(textGroup);
        Controls.Add(poolGroup);
        Controls.Add(_analyzeButton);
        Controls.Add(_saveRunButton);
        Controls.Add(_batchButton);
        Controls.Add(_statusLabel);
        Controls.Add(resultsLabel);
        Controls.Add(_resultsList);
        Controls.Add(fingerprintLabel);
        Controls.Add(_fingerprintCanvas);

        Load += async (_, _) => await LoadWorksAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadWorksAsync()
    {
        _workList.Items.Clear();
        var editions = await _editionRepo.GetAllOriginalEditionsAsync();

        foreach (var e in editions.Where(e => !string.IsNullOrEmpty(e.Language)))
        {
            _workList.Items.Add(new WorkItem
            {
                WorkId = e.WorkId,
                EditionId = e.EditionId,
                AuthorName = e.AuthorName,
                WorkTitle = e.WorkTitle,
                Language = e.Language!
            });
        }

        if (_workList.Items.Count == 0)
        {
            _statusLabel.Text = "No original-language editions found - ingest some texts first.";
        }
    }

    /// <summary>
    /// Title fragments marking works that are not single compositions by a
    /// single author, and which therefore have no business in a pool whose
    /// z-scores define what "normal" looks like.
    ///
    /// Found by searching the ingested Greek corpus for editorial Latin and
    /// reading what came back. The hits were not tragedies carrying an
    /// apparatus - they were works whose entire content is scholarly:
    /// Aristonicus' notes on the Odyssey, Themistius' index verborum,
    /// the Fragmenta of Chrysippus and Democritus. Excerpts in Latin and
    /// German, "codd.", "coni.", editors cited throughout.
    ///
    /// The parser is not misreading these. They genuinely are commentary,
    /// indices and fragment collections. But a fragment collection is an
    /// anthology assembled by a modern editor out of quotations spanning
    /// centuries, and an index is a word list. Neither has an authorial
    /// style to measure, and both were sitting in every normalisation.
    ///
    /// Euripides' own Fragmenta is the clearest case: 17,375 tokens, the
    /// largest "work" in the Euripides set, and the reason Spearman's rho
    /// between length and depth reads 0.43 instead of 0.61.
    ///
    /// Matched case-insensitively on substrings of the title. Deliberately
    /// conservative - commentaries are hard to catch by title without also
    /// catching real works, so no attempt is made to match "In ... " or
    /// "Commentaria". The filter is a checkbox, and what it removed is
    /// reported, so an over-broad match is visible rather than silent.
    /// </summary>
    private static readonly string[] NonCompositionTitleMarkers =
    {
        "fragmenta", "fragment", "fragments",
        "index",          // Themistius, index verborum
        "scholia", "scholion",
        "testimonia",
        "excerpta",
        "lexicon",
        "anthology", "anthologia",
        "gnomologium",    // compiled sayings, not a composition
        "paraphrasis",    // Themistius' Aristotle paraphrases
        "commentar"       // Commentaria / Commentarii / Commentarius
    };

    // WHAT THIS FILTER DOES NOT CATCH.
    //
    // A title-based filter can only remove works whose titles announce what
    // they are. Two of the clearest offenders found in the corpus do not:
    //
    //   Aristonicus, "De signis Odysseae"   - ancient critical notes on Homer,
    //                                         entirely editorial in content
    //   Themistius, "Analyticorum Posteriorum Paraphrasis"
    //                                       - caught now, but only because
    //                                         "paraphrasis" was added above;
    //                                         its index_verborum sections are
    //                                         identifiable from the citation
    //                                         reference, not the title
    //
    // A content-based test would catch both: in a Greek work, the share of
    // nodes containing a run of three or more Latin letters is near zero for a
    // composition and high for a commentary. That is query 2 of
    // verify-apparatus-fix.sql, and running it per-analysis would be slow.
    //
    // Worth doing as an ingest-time classification - a Kind or IsCommentary
    // column set once - rather than a filter applied on every run. Until then,
    // this filter is partial and should be understood as such.

    private static bool IsNonComposition(WorkItem w) =>
        NonCompositionTitleMarkers.Any(m =>
            w.WorkTitle.Contains(m, StringComparison.OrdinalIgnoreCase));

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
    /// </summary>
    private (List<WorkItem> Pool, string Summary) BuildPool(WorkItem target, bool exemptTarget = true)
    {
        var all = _workList.Items.Cast<WorkItem>()
            .Where(w => w.Language == target.Language)
            .ToList();

        var pool = all;
        var notes = new List<string>();

        if (_excludeNonCompositionsCheck.Checked)
        {
            var before = pool.Count;
            pool = pool.Where(w => (exemptTarget && w.WorkId == target.WorkId) || !IsNonComposition(w)).ToList();
            if (before != pool.Count) notes.Add($"{before - pool.Count} fragment/index works");
        }

        var minTokens = (int)_minTokensInput.Value;
        if (minTokens > 0)
        {
            var before = pool.Count;
            pool = pool
                .Where(w => (exemptTarget && w.WorkId == target.WorkId) || GetTokenEstimate(w) >= minTokens)
                .ToList();
            if (before != pool.Count) notes.Add($"{before - pool.Count} works under {minTokens:N0} tokens");
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
    private int GetTokenEstimate(WorkItem w)
    {
        if (_tokenEstimateCache.TryGetValue(w.EditionId, out var cached)) return cached;

        var chars = _textNodeRepo.GetCharacterCountAsync(w.EditionId).GetAwaiter().GetResult();
        var estimate = chars / 6;
        _tokenEstimateCache[w.EditionId] = estimate;
        return estimate;
    }

    private readonly Dictionary<int, int> _tokenEstimateCache = new();

    private async Task RunAnalysisAsync()
    {
        if (_workList.SelectedItem is not WorkItem target)
        {
            MessageBox.Show(this, "Pick a work first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var (sameLanguage, poolSummary) = BuildPool(target);

        if (sameLanguage.Count < 4)
        {
            MessageBox.Show(this,
                $"Only {sameLanguage.Count} {target.Language} work(s) ingested - need at least a handful in the " +
                "same language to make the comparison meaningful.",
                "Not enough to compare", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _analyzeButton.Enabled = false;
        _statusLabel.Text = $"Analyzing - {poolSummary}. This reads full text for each, so it can take a bit.";
        _resultsList.Items.Clear();

        // Was Application.DoEvents() here, to get the status label painted
        // before the analysis starts. DoEvents pumps the whole message queue
        // mid-handler, which means a second Analyze click gets dispatched
        // while the first pass is still running - the button is disabled
        // above, but anything else on the form is still live. Task.Yield
        // gives the same repaint by returning to the message loop normally
        // and resuming here, without running queued input.
        await Task.Yield();

        try
        {
            // Read the settings HERE, on the UI thread. ComputeDelta runs inside
            // Task.Run, and touching a control from that thread would be a
            // cross-thread violation - so the values are captured first and
            // passed in rather than read from the controls inside the task.
            var foldAccents = _foldAccentsCheck.Checked;
            var featureCount = (int)_featureCountInput.Value;
            var chunkSize = (int)_chunkSizeInput.Value;

            // Fetch + tokenize is CPU/IO-bound - keep it off the UI thread.
            var (results, fingerprint, tokenCount, chunkNote) = await Task.Run(
                () => ComputeDelta(target, sameLanguage, foldAccents, featureCount, chunkSize));

            _currentResults = results;
            _currentFingerprint = fingerprint;
            _lastRunTarget = target;
            _lastRunSettings = new StylometrySettings(
                featureCount, foldAccents,
                StripElisionMarksAlways,
                StylometryRunRepository.CurrentAlgorithmVersion,
                chunkSize);
            _lastRunPoolSize = results.Count + 1;   // neighbours plus the target itself
            _lastRunTokenCount = tokenCount;
            _saveRunButton.Enabled = true;

            _resultsList.Items.Clear();
            foreach (var r in results.Take(20))
            {
                _resultsList.Items.Add($"Delta {r.Delta:F3} - {r.AuthorName}, {r.WorkTitle}");
            }

            _fingerprintCanvas.SetData(fingerprint);

            // results.Count, not sameLanguage.Count - 1: the latter counts
            // editions, so a pool holding three editions of Ajax reported three
            // more comparisons than were actually made.
            //
            // The settings are echoed back because a Delta figure is only
            // interpretable alongside the preprocessing that produced it, and
            // comparing runs from screenshots is otherwise guesswork.
            var accentNote = foldAccents ? "accents folded" : "accents kept";
            _statusLabel.Text =
                $"Compared {target.AuthorName}, {target.WorkTitle} against {results.Count} other {target.Language} works." +
                $"{Environment.NewLine}Settings: {featureCount} features, {accentNote}, elision marks stripped." +
                $"{Environment.NewLine}Pool: {poolSummary}." +
                $"{Environment.NewLine}Sampling: {chunkNote}.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Analysis failed - see message.";
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyzeButton.Enabled = true;
        }
    }

    /// <summary>
    /// Elision-mark stripping is not optional, unlike accent folding, so it is
    /// a constant rather than a control. It is recorded on saved runs anyway,
    /// because if it ever does become optional the runs saved before that point
    /// need to say which behaviour produced them.
    /// </summary>
    private const bool StripElisionMarksAlways = true;

    private static readonly Regex WordPattern = new(@"\p{L}+", RegexOptions.Compiled);

    // FeatureWordCount and UseAccentStripping used to be consts here. They are
    // now read from the controls on the form and passed into ComputeDelta,
    // because a const requires a rebuild to change - which in practice means
    // editing the value, re-running without rebuilding, and comparing a result
    // against itself while believing it is a second data point.
    //
    // Defaults live on the controls: 150 features, accents folded.
    //
    // Both settings materially move the result, so whichever produced a given
    // Delta figure is echoed into the status label with it.
    //
    // On the feature count: Burrows' original used 150; 100-1000 is the usual
    // range. Values near 60 are small enough that rank order among close
    // neighbours shifts on its own.
    //
    // On accent folding: with it on, ἦ / ἥ / ᾗ collapse to a single token,
    // which removes inconsistent accentuation across Perseus editions but
    // merges genuinely distinct function words. With it off, the distinctions
    // survive along with whatever inconsistency the editions carry. Neither is
    // obviously right - run both and compare rank order. Stable ordering means
    // the result is robust to the choice; a flip means it was orthography.

    /// <summary>
    /// Removes elision apostrophes so that δ' and δε count as the same token.
    ///
    /// This is a correctness fix, not a preference. Whether an elided form is
    /// written δ' or δε is a decision made by a nineteenth-century editor, not
    /// by the author, and Perseus is not consistent about it across ~2,000
    /// files. Left in place it becomes the single highest-weighted feature in a
    /// Greek run - i.e. the analysis measures which editor prepared the text.
    ///
    /// It has to happen here rather than being left to the tokenizer or to
    /// WordNormalizer, because both let it through:
    ///
    ///   - The regex above matches \p{L}+, and U+02BC MODIFIER LETTER
    ///     APOSTROPHE is Unicode category Lm, which is a Letter category. So
    ///     \p{L} captures it as part of the word.
    ///   - WordNormalizer.Normalize filters on char.IsLetter, which returns
    ///     true for Lm for the same reason. It strips combining marks and folds
    ///     final sigma, but the apostrophe survives.
    ///
    /// U+2019 (Pf) and U+0027 (Po) are punctuation and would be dropped by
    /// either filter - which is the actual problem, since it means the same
    /// word tokenizes differently depending on which codepoint an edition
    /// happens to use. All three are removed here so the treatment is uniform.
    ///
    /// Verify after running: δ' should no longer appear in the fingerprint
    /// chart. If it does, this is not being reached.
    ///
    /// WHAT THIS DOES NOT DO. Stripping the mark maps δ' to δ, not to δε. The
    /// elided and unelided forms of the same word therefore remain separate
    /// features. That is deliberate, and it is the smaller problem:
    ///
    ///   - The confound being fixed is CROSS-EDITION inconsistency. The same
    ///     elided form was previously counted as δ' in one edition and δ in
    ///     another purely on the codepoint chosen. Every edition now agrees.
    ///   - Merging δ into δε is a separate question requiring a restoration
    ///     table (δ'→δέ, τ'→τε, μ'→με, ἀλλ'→ἀλλά ...), some of which is
    ///     genuinely ambiguous.
    ///
    /// Whether to go further is arguable. Elision is metrically conditioned, so
    /// how often a poet elides is plausibly a real stylistic feature rather than
    /// noise - though it is also mediated by editorial convention. Leaving them
    /// separate keeps that signal; merging them would discard it. Not resolved
    /// here.
    /// </summary>
    private static string StripElisionMarks(string token) => token
        .Replace("\u02BC", string.Empty)   // MODIFIER LETTER APOSTROPHE (Lm - survives \p{L} and char.IsLetter)
        .Replace("\u2019", string.Empty)   // RIGHT SINGLE QUOTATION MARK (Pf)
        .Replace("\u1FBD", string.Empty)   // GREEK KORONIS (Sk)
        .Replace("'", string.Empty);       // ASCII APOSTROPHE (Po)

    private static string NormalizeToken(string raw, bool foldAccents)
    {
        var token = foldAccents ? WordNormalizer.Normalize(raw) : raw.ToLowerInvariant();
        return StripElisionMarks(token);
    }

    /// <summary>
    /// One comparison unit: either a whole work, or a fixed-size sample drawn
    /// from one.
    /// </summary>
    private sealed class Chunk
    {
        public int WorkId;
        public string AuthorName = string.Empty;
        public string WorkTitle = string.Empty;
        public int ChunkIndex;                       // 1-based; 0 when not chunking
        public int ChunkCount;                       // how many this work produced
        public Dictionary<string, int> Counts = new();
        public int TotalTokens;

        /// <summary>Label for the results list. Chunk numbers only appear when there is more than one.</summary>
        public string Label => ChunkCount > 1
            ? $"{AuthorName}, {WorkTitle} [{ChunkIndex}/{ChunkCount}]"
            : $"{AuthorName}, {WorkTitle}";
    }

    /// <summary>
    /// Splits a token list into equal-size samples.
    ///
    /// WHY SAMPLE RATHER THAN SLICE. Eder found randomly drawn bags of words
    /// outperform contiguous passages of the same size for attribution: a
    /// contiguous passage carries local subject matter - one episode, one
    /// speaker, one messenger speech - and that shows up in word frequencies as
    /// though it were style. Drawing across the whole work averages the topic
    /// out and leaves the habitual vocabulary, which is what Delta is for.
    ///
    /// The shuffle is seeded from the work id, so a given work always yields
    /// the same chunks. Reproducibility matters more here than fresh randomness:
    /// a run that cannot be repeated cannot be compared against.
    ///
    /// Remainder tokens are discarded. Keeping a short final chunk would put a
    /// noisier sample into the same pool as full ones and reintroduce exactly
    /// the length effect this is meant to remove. The cost is real - Rhesus at
    /// 5,431 tokens yields one 3,000-token chunk and 2,431 tokens go unused -
    /// and it is reported so the discard is visible rather than silent.
    /// </summary>
    private static List<List<string>> SplitIntoChunks(List<string> tokens, int chunkSize, int seed)
    {
        var chunkCount = tokens.Count / chunkSize;
        if (chunkCount < 1) return new List<List<string>>();

        var order = Enumerable.Range(0, tokens.Count).ToArray();
        var rng = new Random(seed);
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var result = new List<List<string>>(chunkCount);
        for (var c = 0; c < chunkCount; c++)
        {
            var bag = new List<string>(chunkSize);
            for (var k = c * chunkSize; k < (c + 1) * chunkSize; k++) bag.Add(tokens[order[k]]);
            result.Add(bag);
        }
        return result;
    }

    private List<string> TokenizeWork(WorkItem work, bool foldAccents)
    {
        var nodes = _textNodeRepo.GetByEditionAsync(work.EditionId).GetAwaiter().GetResult();
        var text = string.Join(' ', nodes.Select(n => n.Text));
        var tokens = new List<string>();

        foreach (Match m in WordPattern.Matches(text))
        {
            var w = NormalizeToken(m.Value, foldAccents);

            // Normalize drops everything that isn't a letter, so a token
            // consisting only of marks can come back empty. Counting those
            // would inflate the denominator with nothing.
            if (w.Length == 0) continue;
            tokens.Add(w);
        }

        return tokens;
    }

    /// <summary>
    /// Burrows' Delta over the pool, optionally at fixed sample size.
    ///
    /// WHY CHUNKING EXISTS. Without it, depth to first outsider tracks text
    /// length: across the Euripides corpus the four shortest works are the four
    /// shallowest, and Rhesus - second shortest - sits exactly where its length
    /// predicts. A shorter text gives noisier relative-frequency estimates,
    /// which inflates its Delta against everything and lets other authors rise
    /// earlier in its ranking. That is a property of the sample, not the author.
    ///
    /// With chunkSize > 0 every comparison unit holds the same number of tokens,
    /// so that particular explanation is removed rather than measured. If a work
    /// still ranks shallow at equal length, length is no longer available as the
    /// reason.
    ///
    /// WHY NO AGGREGATION BACK TO WORKS. Chunks stay chunks all the way through.
    /// Collapsing them into a per-work distance requires choosing between the
    /// mean of pairwise chunk distances - which rewards works with more chunks,
    /// i.e. longer works - and the minimum, which rewards them differently by
    /// giving them more chances at a close match. Both smuggle length back in.
    /// Leaving the unit as the chunk avoids the choice: every chunk is one
    /// observation of equal size, and a long work simply contributes more
    /// observations.
    ///
    /// The consequence for reading results: a work with three chunks appears
    /// three times, and the target work's own chunks are excluded so it cannot
    /// match itself.
    /// </summary>
    private (List<(int WorkId, string AuthorName, string WorkTitle, double Delta)> Results,
             List<(string Word, double Frequency)> Fingerprint,
             int TargetTokenCount,
             string ChunkNote)
        ComputeDelta(WorkItem target, List<WorkItem> pool, bool foldAccents, int featureWordCount, int chunkSize)
    {
        // 0. One entry per WORK, not per EDITION.
        //
        // GetAllOriginalEditionsAsync returns a row per edition, and a work can
        // carry several. Left unhandled, a work with three editions contributes
        // its values three times to every feature's mean and standard deviation.
        // The multi-edition works cluster in Aeschylus and Sophocles while most
        // Euripides plays carry one, so the normalisation ends up weighted
        // toward exactly the authors a Euripides comparison is measured against.
        var distinctPool = pool
            .GroupBy(w => w.WorkId)
            .Select(g => g.First())
            .ToList();

        // 1. Tokenize, then split into comparison units.
        var chunks = new List<Chunk>();
        var targetTotalTokens = 0;
        var droppedShort = new List<string>();
        var discardedTokens = 0;

        foreach (var work in distinctPool)
        {
            var tokens = TokenizeWork(work, foldAccents);
            if (work.WorkId == target.WorkId) targetTotalTokens = tokens.Count;

            if (chunkSize <= 0)
            {
                var counts = new Dictionary<string, int>();
                foreach (var t in tokens) counts[t] = counts.GetValueOrDefault(t) + 1;
                chunks.Add(new Chunk
                {
                    WorkId = work.WorkId,
                    AuthorName = work.AuthorName,
                    WorkTitle = work.WorkTitle,
                    ChunkIndex = 0,
                    ChunkCount = 1,
                    Counts = counts,
                    TotalTokens = Math.Max(tokens.Count, 1)
                });
                continue;
            }

            var bags = SplitIntoChunks(tokens, chunkSize, work.WorkId);

            if (bags.Count == 0)
            {
                // Shorter than one chunk. Dropped rather than padded or kept
                // whole - a short unit in a pool of full-size ones is the
                // problem chunking exists to solve.
                droppedShort.Add(work.WorkTitle);
                continue;
            }

            discardedTokens += tokens.Count - bags.Count * chunkSize;

            for (var i = 0; i < bags.Count; i++)
            {
                var counts = new Dictionary<string, int>();
                foreach (var t in bags[i]) counts[t] = counts.GetValueOrDefault(t) + 1;
                chunks.Add(new Chunk
                {
                    WorkId = work.WorkId,
                    AuthorName = work.AuthorName,
                    WorkTitle = work.WorkTitle,
                    ChunkIndex = i + 1,
                    ChunkCount = bags.Count,
                    Counts = counts,
                    TotalTokens = chunkSize
                });
            }
        }

        var targetChunks = chunks.Where(c => c.WorkId == target.WorkId).ToList();
        if (targetChunks.Count == 0)
        {
            throw new InvalidOperationException(
                $"{target.WorkTitle} has fewer than {chunkSize:N0} tokens, so it cannot be compared at " +
                "this sample size. Lower the sample size or turn chunking off.");
        }

        // 2. Feature set: the N most frequent words across all units.
        var aggregate = new Dictionary<string, int>();
        foreach (var c in chunks)
            foreach (var (word, count) in c.Counts)
                aggregate[word] = aggregate.GetValueOrDefault(word) + count;

        var featureWords = aggregate
            .OrderByDescending(kv => kv.Value)
            .Take(featureWordCount)
            .Select(kv => kv.Key)
            .ToList();

        // 3. Relative frequency per unit.
        var relFreq = new List<Dictionary<string, double>>(chunks.Count);
        foreach (var c in chunks)
            relFreq.Add(featureWords.ToDictionary(w => w, w => (double)c.Counts.GetValueOrDefault(w) / c.TotalTokens));

        // 4. Z-score each feature across the units.
        var zScores = Enumerable.Range(0, chunks.Count)
            .Select(_ => new Dictionary<string, double>())
            .ToList();

        foreach (var word in featureWords)
        {
            var values = relFreq.Select(r => r[word]).ToList();
            var mean = values.Average();
            var stdev = Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
            if (stdev < 1e-9) stdev = 1e-9; // a word every unit uses identically

            for (var i = 0; i < chunks.Count; i++)
                zScores[i][word] = (relFreq[i][word] - mean) / stdev;
        }

        // 5. Delta from the target's first unit to every unit of every OTHER work.
        //
        // Other chunks of the target's own work are excluded. They would
        // otherwise dominate the top of the ranking - two samples of one text
        // are about as close as two samples get - and would push the first
        // work by another author further down, inflating depth to first
        // outsider by however many chunks the target happens to have. Which is
        // the length effect, back again by another route.
        var targetIndex = chunks.IndexOf(targetChunks[0]);
        var targetZ = zScores[targetIndex];

        var results = new List<(int WorkId, string AuthorName, string WorkTitle, double Delta)>();
        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].WorkId == target.WorkId) continue;
            var otherZ = zScores[i];
            var delta = featureWords.Average(word => Math.Abs(targetZ[word] - otherZ[word]));
            results.Add((chunks[i].WorkId, chunks[i].AuthorName, chunks[i].Label, delta));
        }
        results = results.OrderBy(r => r.Delta).ToList();

        var fingerprint = relFreq[targetIndex]
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        var note = chunkSize <= 0
            ? "whole works (no sampling)"
            : $"{chunks.Count} samples of {chunkSize:N0} tokens from {chunks.Select(c => c.WorkId).Distinct().Count()} works" +
              (droppedShort.Count > 0 ? $"; {droppedShort.Count} works too short" : "") +
              (discardedTokens > 0 ? $"; {discardedTokens:N0} tokens unused" : "");

        // Token count is the work's full length, not the chunk's - it is what
        // the length-confound analysis needs, and at fixed chunk size the chunk
        // length is a constant and tells you nothing.
        return (results, fingerprint, targetTotalTokens, note);
    }

    private async Task SaveCurrentRunAsync()
    {
        if (_lastRunTarget == null || _lastRunSettings == null || _currentResults.Count == 0)
        {
            MessageBox.Show(this, "Run an analysis first.", "Nothing to save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            await _runRepo.SaveRunAsync(
                _lastRunTarget.WorkId,
                _lastRunTarget.EditionId,
                _lastRunTarget.AuthorName,
                _lastRunTarget.WorkTitle,
                _lastRunTarget.Language,
                _lastRunSettings,
                _lastRunPoolSize,
                _lastRunTokenCount,
                _currentResults,
                _currentFingerprint);

            _statusLabel.Text = $"Saved: {_lastRunTarget.AuthorName}, {_lastRunTarget.WorkTitle} " +
                                $"({_lastRunSettings.ProfileKey}).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
    /// reason anyone would notice.
    ///
    /// ComputeDelta re-tokenises the whole pool per target, so this is O(n) full
    /// passes for n targets. Scoped to one author that is fine (a dozen or so
    /// passes); pointed at a whole corpus it would not be. The pool tokenisation
    /// is the obvious thing to hoist if this ever needs to run corpus-wide.
    /// </summary>
    private async Task RunBatchForAuthorAsync()
    {
        if (_workList.SelectedItem is not WorkItem seed)
        {
            MessageBox.Show(this, "Pick any work by the author you want to batch.",
                "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Same filters as a single run. A reference distribution built from a
        // differently-composed pool than the runs it is compared against would
        // be quietly meaningless.
        var (pool, poolSummary) = BuildPool(seed, exemptTarget: false);

        var targets = pool
            .Where(w => w.AuthorName == seed.AuthorName)
            .GroupBy(w => w.WorkId)
            .Select(g => g.First())
            .ToList();

        if (targets.Count < 3)
        {
            MessageBox.Show(this,
                $"Only {targets.Count} work(s) by {seed.AuthorName} are ingested. A reference " +
                "distribution needs at least three to mean anything.",
                "Not enough works", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var foldAccents = _foldAccentsCheck.Checked;
        var featureCount = (int)_featureCountInput.Value;
        var chunkSize = (int)_chunkSizeInput.Value;
        var settings = new StylometrySettings(
            featureCount, foldAccents, StripElisionMarksAlways,
            StylometryRunRepository.CurrentAlgorithmVersion,
            chunkSize);

        var confirm = MessageBox.Show(this,
            $"Run and save all {targets.Count} works by {seed.AuthorName} at {settings.Describe()}?" +
            Environment.NewLine + $"Pool: {poolSummary}." +
            Environment.NewLine + Environment.NewLine +
            "Any existing saved runs for this language and settings profile will be replaced." +
            Environment.NewLine + Environment.NewLine +
            "This re-reads the corpus once per work, so expect it to take a while.",
            "Run batch", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

        if (confirm != DialogResult.OK) return;

        _analyzeButton.Enabled = false;
        _batchButton.Enabled = false;
        _saveRunButton.Enabled = false;

        try
        {
            await _runRepo.DeleteRunsForSettingsAsync(seed.Language, settings);

            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                _statusLabel.Text = $"Batch {i + 1} of {targets.Count}: {target.WorkTitle}...";
                await Task.Yield();

                var (results, fingerprint, tokenCount, _) = await Task.Run(
                    () => ComputeDelta(target, pool, foldAccents, featureCount, chunkSize));

                await _runRepo.SaveRunAsync(
                    target.WorkId, target.EditionId, target.AuthorName, target.WorkTitle,
                    target.Language, settings, results.Count + 1, tokenCount,
                    results, fingerprint);
            }

            _statusLabel.Text = $"Batch complete - {targets.Count} runs saved for {seed.AuthorName} " +
                                $"({settings.ProfileKey}). Open Compare Saved Runs to analyse.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Batch failed - see message.";
            MessageBox.Show(this, ex.Message, "Batch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyzeButton.Enabled = true;
            _batchButton.Enabled = true;
        }
    }

    private async Task OpenSelectedResultAsync()
    {
        var index = _resultsList.SelectedIndex;
        if (index < 0 || index >= _currentResults.Count || OnOpenWork == null) return;

        await OnOpenWork(_currentResults[index].WorkId);
        Close();
    }
}
