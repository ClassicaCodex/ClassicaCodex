using System.Globalization;
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
    private GeminiEchoResult? _lastAiResult;
    private DateTime? _lastAiGeneratedUtc;
    private string? _lastTruncatedAtRef;

    // Captured alongside the work id when a comparison target is chosen -
    // the results themselves are bare TextNodes, which carry no attribution,
    // and an exported passage with no author or work on it is unusable.
    private string _comparisonAuthorName = string.Empty;
    private string _comparisonWorkTitle = string.Empty;

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

        // The rationale is the whole point of this view - it's why a
        // candidate is being suggested at all, and unlike the passage text
        // it exists nowhere else in the library. Exporting the passages
        // without it would throw away the only part that isn't already
        // reachable from the reader.
        ListResultHelpers.AttachExportMenu(_resultsListBox, () => (
            $"Cross-language echoes of [{_sourceNode.CitationRef}]",
            _verifiedResults.Select(r => new ExportPassage(
                _comparisonWorkId,
                r.Node.TextNodeId,
                _comparisonAuthorName,
                _comparisonWorkTitle,
                r.Node.CitationRef,
                r.Node.Text,
                $"[{r.Candidate.Confidence}] {r.Candidate.Rationale}")).ToList()),
            this,
            "why each was suggested");

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

        var saveButton = new Button
        {
            Text = "Save investigation…", Left = 12, Top = 622, Width = 150, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        saveButton.Click += async (_, _) => await SaveInvestigationAsync();

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
        Controls.Add(saveButton);
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

        // The other side of the same argument that filters the comparison
        // corpus: if the line this was opened on is not the author's own
        // words, there is nothing to look for an echo of. Someone
        // right-clicking a speaker name would otherwise spend an API call
        // asking which passages of Aeschylus resemble "Ber.".
        _findButton.Enabled = (hasSelection && SourceIsReadingText) || !hasKey;
        _findButton.Text = !hasKey ? "Configure Gemini Key..."
            : !SourceIsReadingText
                ? $"{NodeKindVisibility.Label(_sourceNode.NodeKind)} can't be echoed - pick a line of text"
            : hasSelection ? "Find Echoes with Gemini (free)"
            : "Pick a work above first";
        AppIcons.Apply(_findButton, "Translate", 16);
    }

    /// <summary>
    /// Whether the line this was opened on is the author's own words rather
    /// than a speech attribution, stage direction or heading. Blank counts as
    /// reading text, so an edition ingested before node kinds existed behaves
    /// as it always did.
    /// </summary>
    private bool SourceIsReadingText =>
        string.IsNullOrWhiteSpace(_sourceNode.NodeKind)
        || string.Equals(_sourceNode.NodeKind, TextNodeKinds.Line, StringComparison.OrdinalIgnoreCase);

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
        _lastAiResult = null;
        _lastAiGeneratedUtc = null;
        _comparisonWorkId = selected.Edition.WorkId;
        _comparisonAuthorName = selected.Edition.AuthorName;
        _comparisonWorkTitle = selected.Edition.WorkTitle;

        try
        {
            // Reading lines only, for both the prompt and the verification
            // that follows it.
            //
            // An echo is a resemblance between things an author wrote. "Ber."
            // and "Enter the Ghost" are not candidates, and offering them
            // invites a match on the stage business two plays happen to share
            // rather than on their language.
            //
            // They also cost room. The prompt stops at MaxComparisonChars and
            // reports where it stopped, so anything included is something
            // else excluded: in Hamlet the non-line nodes are 11.2% of the
            // tagged text, in Gorgias 10.2%, and the play is already close
            // enough to the ceiling that a tenth of it matters.
            var comparisonNodes = await _textNodeRepo.GetByEditionAsync(
                selected.Edition.EditionId, readingLinesOnly: true);

            var (taggedText, truncatedAtRef) = BuildTaggedComparisonText(comparisonNodes);
            _lastTruncatedAtRef = truncatedAtRef;

            _statusLabel.Text = "Asking Gemini...";
            var apiKey = TranslationSettings.GeminiApiKey!;
            _lastAiResult = await GeminiTranslationService.FindEchoesWithProvenanceAsync(
                _sourceNode.Text, _sourceLanguage, _sourceAuthorName, _sourceWorkTitle, _sourceNode.CitationRef,
                selected.Edition.AuthorName, selected.Edition.WorkTitle, selected.Edition.Language,
                taggedText, apiKey);
            _lastAiGeneratedUtc = DateTime.UtcNow;

            VerifyAndDisplayResults(_lastAiResult.Candidates.ToList(), comparisonNodes, truncatedAtRef);
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

    private async Task SaveInvestigationAsync()
    {
        if (_verifiedResults.Count == 0 || _lastAiResult == null)
        {
            MessageBox.Show(this, "Run a search with verified candidates before saving it.");
            return;
        }
        var source = await _textNodeRepo.GetPassageResearchIdentityAsync(_sourceNode.TextNodeId);
        if (source == null) { MessageBox.Show(this, "The source passage is no longer present in the local corpus."); return; }
        var scope = $"{_comparisonAuthorName}, {_comparisonWorkTitle}" +
                    (_lastTruncatedAtRef == null ? " (complete ingested edition)" : $" (through {_lastTruncatedAtRef})");
        var request = new EchoCaptureRequest(
            ResearchEchoMethod.AiCrossLanguage, source,
            $"Cross-language echoes: {source.WorkTitle} {source.CitationRef} → {_comparisonWorkTitle}",
            scope,
            "Gemini thematic/imagistic comparison. Returned citations were resolved exactly against local reading-text nodes; unsupported words in rationales remain flagged for human checking.",
            _lastAiResult.Model, _lastAiResult.PromptProvenance, _lastAiGeneratedUtc,
            _verifiedResults.Select(r => new EchoCaptureCandidate(
                _comparisonWorkId, r.Node.TextNodeId, _comparisonAuthorName, _comparisonWorkTitle,
                r.Node.CitationRef, r.Node.Text, null, r.Candidate.Confidence, r.Candidate.Rationale)).ToList());
        using var form = new ResearchEchoCaptureForm(request);
        if (form.ShowDialog(this) == DialogResult.OK)
            MessageBox.Show(this, "The verified candidates and Gemini provenance are saved for review in Research Bench → Project → Echo investigations.");
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
    /// <summary>
    /// Latin letters for the Greek ones, close enough to recognise a word by.
    /// Not a reading transliteration - no breathings, no vowel-length marks,
    /// no iota subscript - because the only question here is whether a word
    /// the rationale wrote in Latin letters is the same word as one in the
    /// line it cites.
    /// </summary>
    private static readonly Dictionary<char, string> GreekToLatin = new()
    {
        ['α'] = "a", ['β'] = "b", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e",
        ['ζ'] = "z", ['η'] = "e", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k",
        ['λ'] = "l", ['μ'] = "m", ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o",
        ['π'] = "p", ['ρ'] = "r", ['σ'] = "s", ['ς'] = "s", ['τ'] = "t",
        ['υ'] = "u", ['φ'] = "ph", ['χ'] = "ch", ['ψ'] = "ps", ['ω'] = "o"
    };

    /// <summary>
    /// Marks that only a transliterated Greek word is likely to carry. A
    /// quoted Latin-script word without one of these is taken for the
    /// rationale's own English - a gloss like 'remember' or a source-language
    /// dictionary form like 'muna' - and left alone, because neither is a
    /// claim about the Greek line and flagging them is just noise.
    /// </summary>
    private static readonly string[] TransliterationMarks = { "th", "ph", "ch", "ps", "rh" };
    private static readonly string[] TransliterationEndings = { "os", "on", "ai", "ei", "oi", "eus", "ato" };

    private static string Transliterate(string normalized)
    {
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized) sb.Append(GreekToLatin.TryGetValue(c, out var s) ? s : c.ToString());
        return sb.ToString();
    }

    private static IEnumerable<string> Forms(string word)
    {
        var normalized = WordNormalizer.Normalize(word);
        if (normalized.Length == 0) yield break;

        yield return normalized;

        var transliterated = Transliterate(normalized);
        if (transliterated != normalized) yield return transliterated;
    }

    private static HashSet<string> FormsIn(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        foreach (var form in Forms(word))
            set.Add(form);

        return set;
    }

    /// <summary>
    /// Whether a word is close enough to one in the passage to count as the
    /// same word. Three shared opening letters and near-equal length, which
    /// covers a rationale citing a dictionary form where the line has an
    /// inflected one - 'noos' against νόον - without stretching to unrelated
    /// words.
    /// </summary>
    private static bool NearAny(string word, HashSet<string> forms)
    {
        if (word.Length < 4) return true;

        foreach (var form in forms)
        {
            if (form.Length < 4) continue;
            if (Math.Abs(form.Length - word.Length) > 1) continue;
            if (string.CompareOrdinal(form, 0, word, 0, 3) == 0) return true;
        }

        return false;
    }

    /// <summary>
    /// The spans a rationale puts in quotation marks, which is where it puts
    /// its evidence.
    ///
    /// An apostrophe between two letters is a possessive or a contraction -
    /// "the Odyssey's theme", "Telemachus's father" - and opening a quoted
    /// span on one swallows the surrounding English prose, which then fails
    /// every check and flags everything. So a quote only opens before a
    /// letter and only closes after one.
    /// </summary>
    private static List<string> QuotedSpans(string rationale)
    {
        const string quotes = "'\u2018\u2019\u201C\u201D\"";

        var spans = new List<string>();
        var current = new System.Text.StringBuilder();
        var inside = false;

        for (var i = 0; i < rationale.Length; i++)
        {
            var c = rationale[i];
            var previous = i > 0 ? rationale[i - 1] : ' ';
            var next = i + 1 < rationale.Length ? rationale[i + 1] : ' ';

            if (quotes.Contains(c))
            {
                if (char.IsLetter(previous) && char.IsLetter(next))
                {
                    if (inside) current.Append(c);
                    continue;
                }

                if (inside && char.IsLetter(previous))
                {
                    spans.Add(current.ToString());
                    current.Clear();
                    inside = false;
                }
                else if (!inside && char.IsLetter(next))
                {
                    inside = true;
                }

                continue;
            }

            if (inside) current.Append(c);
        }

        if (current.Length > 0) spans.Add(current.ToString());

        return spans;
    }

    private static string LettersOnly(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Whether the words a rationale offers as evidence are in the line it
    /// cites.
    ///
    /// The citation check above confirms a reference resolves to a real
    /// passage. It does not confirm the reason given for it. Gemini reasons
    /// about a work as a whole and then attaches that reasoning to one line,
    /// and the two come apart: asked for echoes of Vǫluspá's "þau er fremst um
    /// man", it cited Odyssey 1.3 and justified it with μνήσατο and ἔννεπε,
    /// which are at 1.29 and 1.1. The reference was real, the words were real,
    /// and neither was in the line the two were joined at.
    ///
    /// Two passes, because the evidence arrives in two alphabets. Any word in
    /// the comparison work's own script is checked wherever it appears.
    /// Latin-script words are checked only inside quotation marks and only
    /// when they look transliterated, since a rationale writing 'munesthai' is
    /// making the same claim as one writing μνήσασθαι and the earlier check
    /// saw straight through it.
    ///
    /// The source passage counts as support too. A rationale properly quotes
    /// the passage it was asked about, and those words are not meant to be in
    /// the Greek.
    ///
    /// Flagged, never dropped. A rationale may legitimately reach for the
    /// surrounding context - an invocation two lines up genuinely bears on the
    /// line beneath it - so this marks evidence a reader should check rather
    /// than deciding it is wrong.
    /// </summary>
    private static List<string> UnsupportedQuotedWords(string rationale, string lineText, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(rationale)) return new List<string>();

        var lineForms = FormsIn(lineText);
        var sourceForms = FormsIn(sourceText);

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        bool Supported(string word) =>
            Forms(word).Any(f => lineForms.Contains(f) || sourceForms.Contains(f)
                                 || NearAny(f, lineForms) || NearAny(f, sourceForms));

        foreach (var span in QuotedSpans(rationale))
        foreach (var raw in span.Replace('/', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = LettersOnly(raw);
            if (word.Length < 4) continue;

            var lower = word.ToLowerInvariant();
            if (word.All(char.IsAscii)
                && !TransliterationMarks.Any(m => lower.Contains(m, StringComparison.Ordinal))
                && !TransliterationEndings.Any(e => lower.EndsWith(e, StringComparison.Ordinal)))
                continue;

            if (Supported(word)) continue;
            if (seen.Add(word)) missing.Add(word);
        }

        foreach (var raw in rationale.Replace('/', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = LettersOnly(raw);
            if (word.Length < 3 || word.All(char.IsAscii)) continue;
            if (Supported(word)) continue;
            if (seen.Add(word)) missing.Add(word);
        }

        return missing;
    }

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
            // The corpus is sent as "[ref] text" and the prompt asks for the reference
            // "exactly as tagged above", so a model that complies returns "[1.1]" while
            // this index is keyed on "1.1". Matching verbatim made a correct response
            // read as entirely unresolved, and the better the model followed the
            // instruction the worse the result looked.
            var citation = candidate.CitationRef.Trim().TrimStart('[').TrimEnd(']');
            if (byRef.TryGetValue(citation, out var node))
            {
                _verifiedResults.Add((node, candidate));
            }
            else
            {
                unresolvedCount++;
            }
        }

        var unsupportedCount = 0;
        foreach (var (node, candidate) in _verifiedResults)
        {
            var preview = node.Text.Length > 70 ? node.Text[..70] + "..." : node.Text;

            var missing = UnsupportedQuotedWords(candidate.Rationale, node.Text, _sourceNode.Text);
            var flag = "";
            if (missing.Count > 0)
            {
                unsupportedCount++;
                flag = $"  (!) {string.Join(", ", missing.Take(3))} " +
                       (missing.Count == 1 ? "is" : "are") + " not in this line";
            }

            _resultsListBox.Items.Add(
                $"[{candidate.Confidence}] {node.CitationRef}: {preview}  \u2014  {candidate.Rationale}{flag}");
        }

        var statusParts = new List<string>();
        statusParts.Add(_verifiedResults.Count == 0
            ? "No verified echoes found."
            : $"{_verifiedResults.Count} verified candidate(s).");

        if (unsupportedCount > 0)
        {
            statusParts.Add(
                $"{unsupportedCount} marked (!) - the rationale quotes a word that isn't in the line it " +
                "cites. Often the reasoning belongs to a nearby line; worth reading before trusting it.");
        }

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
