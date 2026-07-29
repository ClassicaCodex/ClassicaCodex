using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Opened from a reader pane's right-click menu, next to (but separate
/// from) Find Echoes. That existing feature ranks passages by shared rare
/// words - fast, free, fully offline, and honest about a real limit: it
/// "can't spot an echo between a Greek original and an English translation
/// of something else, since those aren't the same words" (Help says so
/// plainly). This tool exists for exactly that gap - a shared image or idea
/// across languages, where the words themselves have nothing in common.
///
/// Deliberately scoped to comparing against one chosen work, not the whole
/// library - there's no way to usefully send an entire multi-hundred-author
/// corpus to an LLM in one request, and even if there were, the cost and the
/// hallucination surface would both scale with it. Pick one work; get a
/// real answer about that one relationship.
///
/// Gemini-only, on purpose - this is a newer, more speculative tool than
/// Translate, and a free feature shouldn't need anyone's credit card behind
/// it. Reuses the same Gemini key already configured for Translate; nothing
/// new to set up.
///
/// The one thing that matters most here: every citation the AI reports gets
/// checked against the comparison work's real, ingested TextNodes before
/// it's shown as a result. A citation that doesn't resolve to anything real
/// is dropped and counted, never displayed as if it were genuine - an LLM
/// asked to find subtle cross-language echoes is exactly the kind of task
/// where a wrong answer can sound just as confident as a right one.
/// </summary>
public class CrossLanguageEchoForm : Form
{
    // Comparison text capped well under either model's real context limit -
    // this is about keeping a single request's cost and latency
    // predictable, not working around an actual ceiling. Most single works
    // fit inside this easily; the rare ones that don't get an honest note
    // about exactly how far the search actually reached, not a silent cutoff.
    private const int MaxComparisonChars = 250_000;

    private readonly TextNode _sourceNode;
    private readonly string? _sourceLanguage;
    private readonly string _sourceAuthorName;
    private readonly string _sourceWorkTitle;
    private readonly int _sourceEditionId;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private List<(int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language)> _allEditions = new();

    private readonly TextBox _sourceBox;
    private readonly TextBox _workFilterBox;
    private readonly ListBox _workListBox;
    private readonly Button _findButton;
    private readonly Label _statusLabel;
    private readonly Label _explainerLabel;
    private readonly ListBox _resultsListBox;

    private List<(TextNode Node, EchoCandidate Candidate)> _verifiedResults = new();
    private int _comparisonWorkId;

    /// <summary>Set by MainForm, same pattern every other results form here already uses.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public CrossLanguageEchoForm(
        TextNode sourceNode, string? sourceLanguage, string sourceAuthorName, string sourceWorkTitle,
        int sourceEditionId)
    {
        _sourceNode = sourceNode;
        _sourceLanguage = sourceLanguage;
        _sourceAuthorName = sourceAuthorName;
        _sourceWorkTitle = sourceWorkTitle;
        _sourceEditionId = sourceEditionId;

        Text = "Find Cross-Language Echo";
        AppIcons.ApplyWindowIcon(this, "SimilarWorks");
        Width = 820;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 520);

        var sourceLabel = new Label
        {
            Left = 12,
            Top = 10,
            Width = 780,
            Text = $"Looking for echoes of: {sourceAuthorName}, {sourceWorkTitle} [{sourceNode.CitationRef}]"
        };
        _sourceBox = new TextBox
        {
            Left = 12,
            Top = 32,
            Width = 780,
            Height = 50,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = sourceNode.Text
        };

        var pickLabel = new Label
        {
            Left = 12,
            Top = 92,
            Width = 780,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Text = "Compare against (type to filter - any author, any language):"
        };

        // Filter.png (a funnel glyph) already exists in the icon set,
        // separate from Search.png (a magnifying glass) - this is a filter
        // field, not a search box, so it gets the icon actually built for
        // that meaning rather than the nearest-available one.
        var workFilterIcon = new PictureBox
        {
            Left = 12,
            Top = 115,
            Width = 16,
            Height = 16,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = AppIcons.Get("Filter", 16)
        };
        _workFilterBox = new TextBox
        {
            Left = 34,
            Top = 112,
            Width = 758,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _workFilterBox.TextChanged += (_, _) => RefreshWorkList();

        _workListBox = new ListBox
        {
            Left = 12,
            Top = 138,
            Width = 780,
            Height = 130,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            IntegralHeight = false
        };
        _workListBox.SelectedIndexChanged += (_, _) => RefreshFindButtonState();

        _findButton = new Button { Left = 12, Top = 276, Width = 260, Height = 28 };
        _findButton.Click += async (_, _) => await OnFindButtonClickAsync();

        _statusLabel = new Label
        {
            Left = 282,
            Top = 281,
            Width = 510,
            ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _explainerLabel = new Label
        {
            Left = 12,
            Top = 312,
            Width = 780,
            Height = 34,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray,
            Text = "Ranked by the AI's own confidence, not proof of borrowing - candidates worth a human " +
                   "look, the same standard Find Echoes holds itself to. Double-click a result to open it."
        };

        _resultsListBox = new ListBox
        {
            Left = 12,
            Top = 350,
            Width = 780,
            Height = 260,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        _resultsListBox.DoubleClick += async (_, _) => await OnResultActivatedAsync();

        var closeButton = new Button
        {
            Text = "Close",
            Left = 716,
            Top = 622,
            Width = 76,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        CancelButton = closeButton;

        Controls.Add(sourceLabel);
        Controls.Add(_sourceBox);
        Controls.Add(pickLabel);
        Controls.Add(workFilterIcon);
        Controls.Add(_workFilterBox);
        Controls.Add(_workListBox);
        Controls.Add(_findButton);
        Controls.Add(_statusLabel);
        Controls.Add(_explainerLabel);
        Controls.Add(_resultsListBox);
        Controls.Add(closeButton);

        RefreshFindButtonState();
        Load += async (_, _) => await LoadWorkListAsync();
        ReadingTheme.AttachTo(this);
    }

    private async Task LoadWorkListAsync()
    {
        _allEditions = await _editionRepo.GetAllOriginalEditionsAsync();
        RefreshWorkList();
    }

    private void RefreshWorkList()
    {
        var filter = _workFilterBox.Text.Trim();

        _workListBox.Items.Clear();
        foreach (var edition in _allEditions)
        {
            // The source work's own original edition is excluded from the
            // picker - "does this echo itself" isn't the question this tool
            // answers, and it would only ever trivially match.
            if (edition.EditionId == _sourceEditionId) continue;

            var label = $"{edition.AuthorName} \u2014 {edition.WorkTitle} ({edition.Language?.ToUpperInvariant() ?? "?"})";
            if (filter.Length == 0 || label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _workListBox.Items.Add(new WorkOption(edition, label));
            }
        }

        RefreshFindButtonState();
    }

    private void RefreshFindButtonState()
    {
        var hasSelection = _workListBox.SelectedItem is WorkOption;
        var hasKey = !string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey);

        _findButton.Enabled = hasSelection || !hasKey;
        _findButton.Text = !hasKey ? "Configure Gemini Key..."
            : hasSelection ? "Find Echoes with Gemini (free)"
            : "Pick a work above first";
        AppIcons.Apply(_findButton, "Translate", 16);
    }

    private async Task OnFindButtonClickAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settingsForm = new TranslateApiSettingsForm();
            settingsForm.ShowDialog(this);
            RefreshFindButtonState();
            return;
        }

        if (_workListBox.SelectedItem is not WorkOption selected) return;

        if (TranslationSettings.AlwaysConfirmBeforeSending)
        {
            var confirmed = MessageBox.Show(this,
                $"This will send the selected passage, and the text of {selected.Edition.AuthorName}'s " +
                $"{selected.Edition.WorkTitle}, to Gemini's API over the internet to look for echoes. This " +
                "is the same kind of network use Translate already discloses.\n\n" +
                "Continue? (You can turn this confirmation off in AI Translation Settings.)",
                "Send to Gemini's API?",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

            if (!confirmed) return;
        }

        _findButton.Enabled = false;
        _statusLabel.ForeColor = Color.DimGray;
        _statusLabel.Text = "Reading the comparison work...";
        _resultsListBox.Items.Clear();
        _verifiedResults = new List<(TextNode, EchoCandidate)>();
        _comparisonWorkId = selected.Edition.WorkId;

        try
        {
            var comparisonNodes = await _textNodeRepo.GetByEditionAsync(selected.Edition.EditionId);

            var (taggedText, truncatedAtRef) = BuildTaggedComparisonText(comparisonNodes);

            _statusLabel.Text = "Asking Gemini...";
            var apiKey = TranslationSettings.GeminiApiKey!;
            var candidates = await GeminiTranslationService.FindEchoesAsync(
                _sourceNode.Text, _sourceLanguage, _sourceAuthorName, _sourceWorkTitle, _sourceNode.CitationRef,
                selected.Edition.AuthorName, selected.Edition.WorkTitle, selected.Edition.Language,
                taggedText, apiKey);

            VerifyAndDisplayResults(candidates, comparisonNodes, truncatedAtRef);
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = $"Couldn't finish: {ex.Message}";
        }
        finally
        {
            RefreshFindButtonState();
        }
    }

    /// <summary>
    /// Builds the "[ref] text" block the prompt asks the model to cite back
    /// against, stopping at MaxComparisonChars rather than silently
    /// including only part of the work with no sign anything was left out.
    /// </summary>
    private static (string TaggedText, string? TruncatedAtRef) BuildTaggedComparisonText(List<TextNode> nodes)
    {
        var builder = new System.Text.StringBuilder();
        string? lastIncludedRef = null;

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text)) continue;

            var line = $"[{node.CitationRef}] {node.Text}\n";
            if (builder.Length + line.Length > MaxComparisonChars)
            {
                return (builder.ToString(), lastIncludedRef);
            }

            builder.Append(line);
            lastIncludedRef = node.CitationRef;
        }

        return (builder.ToString(), null);
    }

    /// <summary>
    /// The core honesty check: every citation the AI returned gets looked up
    /// against the comparison work's actual TextNodes (exact match on the
    /// ref it was explicitly given, not a fuzzy cross-edition match - this
    /// is all within the one work it was just shown). Anything that doesn't
    /// resolve is dropped and counted, never displayed as a result.
    /// </summary>
    private void VerifyAndDisplayResults(
        List<EchoCandidate> candidates, List<TextNode> comparisonNodes, string? truncatedAtRef)
    {
        var byRef = new Dictionary<string, TextNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in comparisonNodes)
        {
            byRef.TryAdd(node.CitationRef, node);
        }

        var unresolvedCount = 0;
        foreach (var candidate in candidates)
        {
            if (byRef.TryGetValue(candidate.CitationRef, out var node))
            {
                _verifiedResults.Add((node, candidate));
            }
            else
            {
                unresolvedCount++;
            }
        }

        foreach (var (node, candidate) in _verifiedResults)
        {
            var preview = node.Text.Length > 70 ? node.Text[..70] + "..." : node.Text;
            _resultsListBox.Items.Add(
                $"[{candidate.Confidence}] {node.CitationRef}: {preview}  \u2014  {candidate.Rationale}");
        }

        var statusParts = new List<string>();
        statusParts.Add(_verifiedResults.Count == 0
            ? "No verified echoes found."
            : $"{_verifiedResults.Count} verified candidate(s).");

        if (unresolvedCount > 0)
        {
            statusParts.Add(
                $"{unresolvedCount} citation(s) Gemini mentioned didn't match anything in this work and " +
                "aren't shown.");
        }

        if (truncatedAtRef != null)
        {
            statusParts.Add($"Only compared through [{truncatedAtRef}] - the work is longer than this tool sends in one request.");
        }

        _statusLabel.ForeColor = Color.DimGray;
        _statusLabel.Text = string.Join(" ", statusParts);
    }

    private async Task OnResultActivatedAsync()
    {
        var index = _resultsListBox.SelectedIndex;
        if (index < 0 || index >= _verifiedResults.Count || OnNavigate == null) return;

        var (node, _) = _verifiedResults[index];
        await OnNavigate(_comparisonWorkId, node.TextNodeId);
    }

    private class WorkOption
    {
        public (int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language) Edition { get; }
        private readonly string _label;

        public WorkOption((int, int, string, string, string?) edition, string label)
        {
            Edition = edition;
            _label = label;
        }

        public override string ToString() => _label;
    }
}
