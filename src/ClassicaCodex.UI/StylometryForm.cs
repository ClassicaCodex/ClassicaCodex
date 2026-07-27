using System.Text.RegularExpressions;
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
    private readonly Label _statusLabel;
    private readonly ListBox _resultsList;
    private readonly FingerprintCanvas _fingerprintCanvas;

    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<(int WorkId, string AuthorName, string WorkTitle, double Delta)> _currentResults = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, Task>? OnOpenWork { get; set; }

    public StylometryForm()
    {
        Text = "Stylometric Fingerprint (Burrows' Delta)";
        AppIcons.ApplyWindowIcon(this, "Stylometry");
        Width = 1150;
        Height = 760;
        StartPosition = FormStartPosition.CenterParent;

        var workLabel = new Label { Text = "Original-language works (pick one to analyze):", Left = 12, Top = 10, Width = 400 };
        _workList = new ListBox
        {
            Left = 12,
            Top = 32,
            Width = 380,
            Height = 560,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        _analyzeButton = new Button { Text = "Analyze Style", Left = 12, Top = 602, Width = 160, Height = 34, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _analyzeButton.Click += async (_, _) => await RunAnalysisAsync();

        _statusLabel = new Label { Text = "Pick a work and click Analyze Style.", Left = 12, Top = 646, Width = 380, Height = 60, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

        var resultsLabel = new Label
        {
            Text = "Most stylistically similar works (lower Delta = closer match; double-click to open):",
            Left = 404,
            Top = 10,
            Width = 730,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _resultsList = new ListBox
        {
            Left = 404,
            Top = 32,
            Width = 730,
            Height = 300,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _resultsList.DoubleClick += async (_, _) => await OpenSelectedResultAsync();

        var fingerprintLabel = new Label
        {
            Text = "Word-frequency fingerprint (its most common words - mostly function words, that's expected):",
            Left = 404,
            Top = 344,
            Width = 730,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _fingerprintCanvas = new FingerprintCanvas
        {
            Left = 404,
            Top = 366,
            Width = 730,
            Height = 330,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(workLabel);
        Controls.Add(_workList);
        Controls.Add(_analyzeButton);
        Controls.Add(_statusLabel);
        Controls.Add(resultsLabel);
        Controls.Add(_resultsList);
        Controls.Add(fingerprintLabel);
        Controls.Add(_fingerprintCanvas);

        Load += async (_, _) => await LoadWorksAsync();
        ReadingTheme.AttachTo(this);
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

    private async Task RunAnalysisAsync()
    {
        if (_workList.SelectedItem is not WorkItem target)
        {
            MessageBox.Show(this, "Pick a work first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sameLanguage = _workList.Items.Cast<WorkItem>()
            .Where(w => w.Language == target.Language)
            .ToList();

        if (sameLanguage.Count < 4)
        {
            MessageBox.Show(this,
                $"Only {sameLanguage.Count} {target.Language} work(s) ingested - need at least a handful in the " +
                "same language to make the comparison meaningful.",
                "Not enough to compare", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _analyzeButton.Enabled = false;
        _statusLabel.Text = $"Analyzing {sameLanguage.Count} works in {target.Language}... this reads full text for each, so it can take a bit.";
        _resultsList.Items.Clear();
        Application.DoEvents();

        try
        {
            // Fetch + tokenize is CPU/IO-bound - keep it off the UI thread.
            var (results, fingerprint) = await Task.Run(() => ComputeDelta(target, sameLanguage));

            _currentResults = results;
            _resultsList.Items.Clear();
            foreach (var r in results.Take(20))
            {
                _resultsList.Items.Add($"Delta {r.Delta:F3} - {r.AuthorName}, {r.WorkTitle}");
            }

            _fingerprintCanvas.SetData(fingerprint);
            _statusLabel.Text = $"Compared {target.AuthorName}, {target.WorkTitle} against {sameLanguage.Count - 1} other {target.Language} works.";
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

    private static readonly Regex WordPattern = new(@"\p{L}+", RegexOptions.Compiled);
    private const int FeatureWordCount = 60;

    private (List<(int WorkId, string AuthorName, string WorkTitle, double Delta)> Results, List<(string Word, double Frequency)> Fingerprint)
        ComputeDelta(WorkItem target, List<WorkItem> pool)
    {
        // 1. Tokenize every work in the pool into word-count dictionaries.
        var wordCounts = new Dictionary<int, Dictionary<string, int>>();
        var totalWords = new Dictionary<int, int>();

        foreach (var work in pool)
        {
            var nodes = _textNodeRepo.GetByEditionAsync(work.EditionId).GetAwaiter().GetResult();
            var text = string.Join(' ', nodes.Select(n => n.Text));
            var counts = new Dictionary<string, int>();
            var total = 0;

            foreach (Match m in WordPattern.Matches(text))
            {
                var w = m.Value.ToLowerInvariant();
                counts[w] = counts.GetValueOrDefault(w) + 1;
                total++;
            }

            wordCounts[work.WorkId] = counts;
            totalWords[work.WorkId] = Math.Max(total, 1);
        }

        // 2. The feature set is the N most frequent words across the whole
        // pool - in practice almost entirely function words (particles,
        // articles, common pronouns/conjunctions), which is exactly what
        // Burrows' Delta is meant to run on.
        var aggregate = new Dictionary<string, int>();
        foreach (var counts in wordCounts.Values)
        {
            foreach (var (word, count) in counts)
            {
                aggregate[word] = aggregate.GetValueOrDefault(word) + count;
            }
        }
        var featureWords = aggregate.OrderByDescending(kv => kv.Value).Take(FeatureWordCount).Select(kv => kv.Key).ToList();

        // 3. Relative frequency of each feature word, per work.
        var relFreq = new Dictionary<int, Dictionary<string, double>>();
        foreach (var work in pool)
        {
            var counts = wordCounts[work.WorkId];
            var total = totalWords[work.WorkId];
            relFreq[work.WorkId] = featureWords.ToDictionary(w => w, w => (double)counts.GetValueOrDefault(w) / total);
        }

        // 4. Z-score each feature across the pool (Burrows' Delta setup).
        var zScores = new Dictionary<int, Dictionary<string, double>>();
        foreach (var word in featureWords)
        {
            var values = pool.Select(w => relFreq[w.WorkId][word]).ToList();
            var mean = values.Average();
            var stdev = Math.Sqrt(values.Select(v => (v - mean) * (v - mean)).Average());
            if (stdev < 1e-9) stdev = 1e-9; // avoid divide-by-zero for a word every work uses identically

            foreach (var work in pool)
            {
                zScores.TryAdd(work.WorkId, new Dictionary<string, double>());
                zScores[work.WorkId][word] = (relFreq[work.WorkId][word] - mean) / stdev;
            }
        }

        // 5. Delta(target, other) = mean absolute z-score difference across all features.
        var targetZ = zScores[target.WorkId];
        var results = pool
            .Where(w => w.WorkId != target.WorkId)
            .Select(w =>
            {
                var otherZ = zScores[w.WorkId];
                var delta = featureWords.Average(word => Math.Abs(targetZ[word] - otherZ[word]));
                return (w.WorkId, w.AuthorName, w.WorkTitle, Delta: delta);
            })
            .OrderBy(r => r.Delta)
            .ToList();

        var fingerprint = relFreq[target.WorkId]
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

        return (results, fingerprint);
    }

    private async Task OpenSelectedResultAsync()
    {
        var index = _resultsList.SelectedIndex;
        if (index < 0 || index >= _currentResults.Count || OnOpenWork == null) return;

        await OnOpenWork(_currentResults[index].WorkId);
        Close();
    }
}
