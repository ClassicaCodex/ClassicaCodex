using System.Globalization;
using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// The classic word-study workflow: pick a word out of a line, find out
/// what dictionary headword it belongs to, see every inflected form of that
/// headword attested in the corpus, and jump to any occurrence.
///
/// This is the feature that only works once lemma data is loaded - without
/// it, every inflected form is an unrelated string and none of this is
/// possible.
/// </summary>
public class WordStudyForm : Form
{
    private readonly ListBox _wordList;
    private readonly ListBox _headwordList;
    private readonly TextBox _definitionBox;
    private readonly TextBox _formsBox;
    private readonly ListBox _occurrenceList;
    private readonly Label _statusLabel;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _currentOccurrences = new();
    private List<(string Headword, string? PartOfSpeech)> _currentHeadwords = new();
    private HashSet<string> _highlightForms = new(StringComparer.Ordinal);

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    /// <summary>
    /// language is the edition's language code ("grc", "lat", "eng"). It's
    /// passed in rather than guessed because English and Latin share an
    /// alphabet - nothing about an English word's spelling distinguishes it
    /// from a Latin one, so only the edition knows.
    /// </summary>
    private readonly string? _language;

    public WordStudyForm(TextNode sourceNode, string? language = null)
    {
        _language = language;

        Text = "Word Study";
        AppIcons.ApplyWindowIcon(this, "WordStudy");
        Width = 1400;
        Height = 790;
        StartPosition = FormStartPosition.CenterParent;

        var sourceLabel = new Label
        {
            Text = $"[{sourceNode.CitationRef}] {sourceNode.Text}",
            Left = 12,
            Top = 10,
            Width = 1360,
            Height = 36,
            ForeColor = Color.DimGray
        };

        var wordLabel = new Label { Text = "Words in this line:", Left = 12, Top = 52, Width = 200 };
        _wordList = new ListBox
        {
            Left = 12,
            Top = 74,
            Width = 200,
            Height = 620,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        _wordList.SelectedIndexChanged += async (_, _) => await LoadHeadwordsAsync();

        var headwordLabel = new Label { Text = "Dictionary headword(s):", Left = 224, Top = 52, Width = 380 };
        _headwordList = new ListBox
        {
            Left = 224,
            Top = 74,
            Width = 380,
            Height = 120,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _headwordList.SelectedIndexChanged += async (_, _) => await LoadDefinitionAndOccurrencesAsync();

        var definitionLabel = new Label
        {
            Text = "Definition:",
            Left = 224,
            Top = 202,
            Width = 380,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _definitionBox = new TextBox
        {
            Left = 224,
            Top = 224,
            Width = 380,
            Height = 320,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Georgia", 9.5F)
        };

        _formsBox = new TextBox
        {
            Text = "Attested forms will appear here.",
            Left = 224,
            Top = 556,
            Width = 380,
            Height = 138,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Georgia", 9.5F)
        };

        var occurrenceLabel = new Label
        {
            Text = "Occurrences across the corpus (double-click to jump):",
            Left = 616,
            Top = 52,
            Width = 756,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _occurrenceList = new ListBox
        {
            Left = 616,
            Top = 74,
            Width = 756,
            Height = 592,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true,
            DrawMode = DrawMode.OwnerDrawFixed
        };
        _occurrenceList.DrawItem += OccurrenceList_DrawItem;
        _occurrenceList.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        ListResultHelpers.AttachCitationTooltip(_occurrenceList,
            i => i < _currentOccurrences.Count ? _currentOccurrences[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_occurrenceList,
            i => i < _currentOccurrences.Count
                ? $"{_currentOccurrences[i].AuthorName}, {_currentOccurrences[i].WorkTitle} [{_currentOccurrences[i].CitationRef}]: {_currentOccurrences[i].Text}"
                : null);

        _statusLabel = new Label
        {
            Left = 616,
            Top = 672,
            Width = 756,
            Height = 24,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(sourceLabel);
        Controls.Add(wordLabel);
        Controls.Add(_wordList);
        Controls.Add(headwordLabel);
        Controls.Add(_headwordList);
        Controls.Add(definitionLabel);
        Controls.Add(_definitionBox);
        Controls.Add(_formsBox);
        Controls.Add(occurrenceLabel);
        Controls.Add(_occurrenceList);
        Controls.Add(_statusLabel);

        PopulateWords(sourceNode.Text);
        Load += async (_, _) => await CheckLemmaDataAsync();
        ReadingTheme.AttachTo(this);
    }

    private void PopulateWords(string text)
    {
        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetter).ToArray()))
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var word in words) _wordList.Items.Add(word);
    }

    private async Task CheckLemmaDataAsync()
    {
        try
        {
            var count = await _lemmaRepo.CountAsync();
            if (count == 0)
            {
                _statusLabel.Text = "No lemma data loaded - use \"Load Lemmas...\" first.";
                _statusLabel.ForeColor = Color.DarkRed;
            }
            else
            {
                _statusLabel.Text = $"{count:N0} lemma mappings available.";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
        }
    }

    private async Task LoadHeadwordsAsync()
    {
        _headwordList.Items.Clear();
        _occurrenceList.Items.Clear();
        _formsBox.Text = "";

        // Cleared here as well. The definition panel is only written when a
        // headword gets selected, so on a word with no headwords at all
        // that step never runs and the previous word's definition just sits
        // there - looking like it belongs to the word now selected.
        _definitionBox.Text = "";

        if (_wordList.SelectedItem is not string word) return;

        _currentHeadwords = await _lemmaRepo.GetHeadwordsForFormAsync(word, _language);

        if (_currentHeadwords.Count == 0)
        {
            _headwordList.Items.Add("(no dictionary entry)");

            // English needs its own explanation. WordNet is a lexicon of
            // content words - nouns, verbs, adjectives, adverbs - and
            // deliberately excludes the function words that hold sentences
            // together. So "from", "of", "the" and "my" genuinely have no
            // entry, and saying "not found in lemma data" for those reads
            // as a loading failure when nothing is wrong at all.
            _formsBox.Text = _language == "eng"
                ? "WordNet covers nouns, verbs, adjectives and adverbs. Function words - prepositions, " +
                  "articles, pronouns, conjunctions - aren't included in it, so common words like " +
                  "\"from\", \"the\" and \"my\" have no entry by design."
                : "No headword on record for this form. Either the lemma data doesn't " +
                  "cover it, or no lemma data is loaded for this language.";
            return;
        }

        // Deduplicate on what the reader actually sees, not on the raw tag.
        // The corpus stores the same analysis in two tag layouts (a nine- and
        // a ten-character form), so a word can have two rows that are
        // genuinely distinct in the database but decode to exactly the same
        // parse - which showed up as the identical line listed twice. The
        // filtered list replaces _currentHeadwords so its indices stay
        // aligned with the list box, which the definition lookup relies on.
        var deduplicated = new List<(string Headword, string? PartOfSpeech)>();
        var seenDisplayText = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (headword, pos) in _currentHeadwords)
        {
            // The stored tag is positional and unreadable on its own
            // ("v-sppemn-"), so show the decoded parse where the format is
            // recognized. An unrecognized tag still gets shown raw rather
            // than hidden - it's real information from the corpus, just not
            // in a vocabulary this can safely interpret.
            var parse = MorphologyDecoder.Decode(pos);
            var displayText = parse.IsDecoded
                ? $"{headword}  -  {parse.Description}"
                : pos != null ? $"{headword}  [{pos}]" : headword;

            if (!seenDisplayText.Add(displayText)) continue;

            deduplicated.Add((headword, pos));
            _headwordList.Items.Add(displayText);
        }

        _currentHeadwords = deduplicated;

        if (_headwordList.Items.Count > 0) _headwordList.SelectedIndex = 0;
    }

    private async Task LoadDefinitionAndOccurrencesAsync()
    {
        var index = _headwordList.SelectedIndex;
        if (index < 0 || index >= _currentHeadwords.Count) return;

        var headword = _currentHeadwords[index].Headword;

        _definitionBox.Text = "Looking up...";
        _occurrenceList.Items.Clear();
        _occurrenceList.Items.Add("Searching...");

        await LoadDefinitionAsync(headword);

        var forms = await _lemmaRepo.GetFormsForHeadwordAsync(headword, _language);
        _formsBox.Text = forms.Count == 0
            ? "(no attested forms on record)"
            : $"Attested forms of {headword} ({forms.Count}):\r\n\r\n" + string.Join(", ", forms);

        // Normalized so the highlighter can match regardless of accentuation
        // or precomposed-vs-combining Unicode, same as the search itself.
        _highlightForms = forms
            .Select(WordNormalizer.Normalize)
            .Where(f => f.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        _currentOccurrences = await _textNodeRepo.SearchByFormsAsync(forms);

        _occurrenceList.Items.Clear();
        foreach (var o in _currentOccurrences.Take(500))
        {
            _occurrenceList.Items.Add($"{o.AuthorName}, {o.WorkTitle}: {o.Text}");
        }

        if (_currentOccurrences.Count == 0)
        {
            _occurrenceList.Items.Add("(no occurrences found)");
        }

        _statusLabel.Text = $"{_currentOccurrences.Count} occurrence(s) of {headword} across the corpus.";
    }

    private async Task LoadDefinitionAsync(string headword)
    {
        var language = DetectLanguage(headword);

        try
        {
            var entries = await _definitionRepo.GetByHeadwordAsync(headword, language);

            if (entries.Count == 0)
            {
                _definitionBox.Text =
                    "(no dictionary entry found)\r\n\r\n" +
                    "Either no dictionary is loaded for this language, or this headword isn't in it. " +
                    "Use \"Load Lemmas...\" and switch Data type to \"Dictionary (lexicon)\" to load one.";
                return;
            }

            var sb = new StringBuilder();
            foreach (var (entryHeadword, entry, source) in entries)
            {
                if (sb.Length > 0) sb.AppendLine().AppendLine(new string('-', 40)).AppendLine();

                // LSJ stores its entry bodies in Beta Code, same as its
                // headwords - the Greek has to be decoded to Unicode or the
                // definition reads as "sunair-esiw/ths , ou , o(". Only the
                // Greek is touched; the English glosses interleaved with it
                // are left alone. Latin (Lewis & Short) bodies are already
                // Latin script and pass through untouched.
                var body = language == "grc" ? BetaCodeConverter.ConvertMixed(entry) : entry;

                sb.Append(entryHeadword);
                if (!string.IsNullOrEmpty(source)) sb.Append("   [").Append(source).Append(']');
                sb.AppendLine().AppendLine();
                sb.AppendLine(body);
            }

            // More than one entry means the homograph numbering in the lemma
            // data and the lexicon don't line up - worth saying so rather
            // than letting it look like duplication.
            if (entries.Count > 1)
            {
                sb.Insert(0, $"{entries.Count} entries share this spelling - the lexicon numbers homographs " +
                             "differently from the lemma data, so all candidates are shown.\r\n\r\n");
            }

            _definitionBox.Text = sb.ToString();
            _definitionBox.SelectionStart = 0;
            _definitionBox.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _definitionBox.Text = $"Couldn't load definition: {ex.Message}";
        }
    }

    /// <summary>
    /// Guesses the language from the script - a headword containing Greek
    /// letters is Greek, anything else is treated as Latin. The lemma tables
    /// do record a language, but a headword arrives here without it, and
    /// script is an unambiguous signal for these two languages.
    /// </summary>
    private static string DetectLanguage(string headword)
    {
        foreach (var c in headword)
        {
            if ((c >= '\u0370' && c <= '\u03FF') || (c >= '\u1F00' && c <= '\u1FFF')) return "grc";
        }
        return "lat";
    }

    /// <summary>
    /// Paints each occurrence line manually so the matched forms can be
    /// highlighted in place. Matching is done per word against the
    /// normalized form set rather than by substring: Greek accents and
    /// combining-vs-precomposed Unicode make raw substring comparison
    /// unreliable, and a substring match would also light up unrelated words
    /// that merely contain the form's letters.
    /// </summary>
    private void OccurrenceList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _occurrenceList.Items.Count) return;
        e.DrawBackground();

        var text = _occurrenceList.Items[e.Index]?.ToString() ?? string.Empty;
        var font = _occurrenceList.Font;
        var x = e.Bounds.Left;
        var foreColor = e.ForeColor;

        void DrawPart(string part, bool highlighted)
        {
            if (part.Length == 0) return;

            var size = TextRenderer.MeasureText(e.Graphics, part, font,
                new Size(int.MaxValue, e.Bounds.Height), TextFormatFlags.NoPadding);
            var rect = new Rectangle(x, e.Bounds.Top, size.Width, e.Bounds.Height);

            if (highlighted)
            {
                // Plain Khaki reads fine on the light surface, but once this
                // form follows dark mode the theme's off-white text would
                // sit on a bright yellow rectangle - low contrast, the
                // opposite of what a highlight is for. AutoTagForm hits the
                // same owner-draw situation and darkens the same way.
                using var highlightBrush = new SolidBrush(
                    ReadingTheme.IsDark ? Color.FromArgb(120, 92, 20) : Color.Khaki);
                e.Graphics.FillRectangle(highlightBrush, rect);
            }

            TextRenderer.DrawText(e.Graphics, part, font, rect, foreColor, TextFormatFlags.NoPadding);
            x += size.Width;
        }

        // Walk the line in alternating word / non-word runs so spacing and
        // punctuation are preserved exactly as drawn.
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            var inWord = IsWordChar(text[i]);
            while (i < text.Length && IsWordChar(text[i]) == inWord) i++;

            var chunk = text[start..i];
            var highlight = inWord && _highlightForms.Contains(WordNormalizer.Normalize(chunk));
            DrawPart(chunk, highlight);
        }

        e.DrawFocusRectangle();
    }

    /// <summary>
    /// Combining marks count as part of a word - otherwise a decomposed
    /// Greek word would split apart mid-token and never match.
    /// </summary>
    private static bool IsWordChar(char c)
    {
        return char.IsLetter(c) ||
               CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;
    }

    private async Task JumpToSelectedAsync()
    {
        var index = _occurrenceList.SelectedIndex;
        if (index < 0 || index >= _currentOccurrences.Count || OnNavigate == null) return;

        var occurrence = _currentOccurrences[index];
        await OnNavigate(occurrence.WorkId, occurrence.TextNodeId);
        Close();
    }
}
