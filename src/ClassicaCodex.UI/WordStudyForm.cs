using ClassicaCodex.Core.Meter;
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
public class WordStudyForm : ScaledForm
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

    // Which works occurrences are drawn from. Starts as the work the line
    // came from, because a common word across the whole corpus returns
    // thousands of lines, stops at the result limit, and tells you only that
    // the word is common. Empty means everything.
    private readonly HashSet<int> _scopeWorkIds = new();

    private readonly Label _scopeLabel;
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();

    /// <summary>
    /// How much room the metre row takes, and how far everything under it
    /// moves when there is one.
    /// </summary>
    private const int MetreRowHeight = 22;

    /// <summary>
    /// What the metre makes of the source line, or null where the question
    /// does not arise - see <see cref="ScanSourceLine"/>.
    /// </summary>
    private readonly Scansion? _scansion;

    private readonly Label _metreLabel;

    /// <summary>
    /// The scansion of the line the reader came from, where there is one to
    /// have.
    ///
    /// Three conditions, and each rules out a large part of the library
    /// rather than an edge case. The scanner reads Latin, so Greek and the
    /// translations are out. It reads dactylic hexameter, so the lyric metres
    /// are out - Horace's Odes scan at 2%, correctly, and a row saying so on
    /// every line of them would be noise. And it needs verse: IsVerse comes
    /// from the markup rather than from guessing at the text, so prose is
    /// excluded by the edition rather than by this.
    ///
    /// Null means the row is not drawn at all and the layout closes up, which
    /// is the right answer for most of what this window opens on.
    /// </summary>
    private Scansion? ScanSourceLine(TextNode sourceNode)
    {
        if (!string.Equals(_language, "lat", StringComparison.OrdinalIgnoreCase)) return null;
        if (!sourceNode.IsVerse) return null;
        if (string.IsNullOrWhiteSpace(sourceNode.Text)) return null;

        var scansion = HexameterScanner.Scan(sourceNode.Text);

        // A line that does not scan is still worth a row: the reader can see
        // that the question was asked and answered, rather than wondering
        // whether the feature is broken or the text is unusual. What is not
        // worth a row is a line that was never a candidate, which is what the
        // three tests above have already removed.
        return scansion;
    }

    public WordStudyForm(
        TextNode sourceNode, string? language = null, int? workId = null, string? selectedWord = null)
    {
        _language = language;
        if (workId != null) _scopeWorkIds.Add(workId.Value);

        Text = "Word Study";
        AppIcons.ApplyWindowIcon(this, "WordStudy");
        Width = 1400;
        Height = 790;
        StartPosition = FormStartPosition.CenterParent;

        var sourceLabel = new Label
        {
            Text = $"[{PassageCitation.Display(sourceNode.CitationRef)}] {sourceNode.Text}",
            Left = 12,
            Top = 10,
            Width = 1360,
            Height = 32,
            ForeColor = Color.DimGray
        };

        // Scanned once, here, rather than per word selection - the search runs
        // over every reading of the spelling against thirty-two shapes, and
        // the line does not change while the window is open.
        _scansion = ScanSourceLine(sourceNode);

        // Everything below the source line moves down by one row when there is
        // a metre to report, and not at all when there is not - which is most
        // of the library. Applied as one offset rather than two sets of
        // coordinates, so the two layouts cannot drift apart.
        var drop = _scansion == null ? 0 : MetreRowHeight;

        _metreLabel = new Label
        {
            Text = DescribeLineMetre(),
            Left = 12,
            Top = 46,
            Width = 1360,
            Height = 20,
            ForeColor = Color.DimGray,
            Visible = _scansion != null
        };

        var wordLabel = new Label { Text = "Words in this line:", Left = 12, Top = 52 + drop, Width = 200 };
        _wordList = new ListBox
        {
            Left = 12,
            Top = 74 + drop,
            Width = 200,
            Height = 620 - drop,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        _wordList.SelectedIndexChanged += async (_, _) =>
        {
            ShowWordMetre(_wordList.SelectedItem as string);
            await LoadHeadwordsAsync();
        };

        var headwordLabel = new Label { Text = "Dictionary headword(s):", Left = 224, Top = 52 + drop, Width = 380 };
        _headwordList = new ListBox
        {
            Left = 224,
            Top = 74 + drop,
            Width = 380,
            Height = 120,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _headwordList.SelectedIndexChanged += async (_, _) => await LoadDefinitionAndOccurrencesAsync();

        var definitionLabel = new Label
        {
            Text = "Definition:",
            Left = 224,
            Top = 202 + drop,
            Width = 380,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _definitionBox = new TextBox
        {
            Left = 224,
            Top = 224 + drop,
            Width = 380,
            Height = 320 - drop,
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
            Text = "Occurrences (double-click to jump):",
            Left = 616,
            Top = 52 + drop,
            Width = 300,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        // "This text" and "everything" are the ends of a range, and the
        // useful answers are often between them - the plays of one trilogy,
        // or everything by one author. A checkbox could only offer the ends.
        _scopeLabel = new Label
        {
            Left = 920,
            Top = 54 + drop,
            Width = 320,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.DimGray
        };

        var scopeButton = new Button
        {
            Text = "Choose Texts...", Left = 1248, Top = 48 + drop, Width = 124, Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        scopeButton.Click += async (_, _) => await ChooseScopeAsync();
        AppIcons.Apply(scopeButton, "Filter", 16);
        _occurrenceList = new ListBox
        {
            Left = 616,
            Top = 74 + drop,
            Width = 756,
            Height = 592 - drop,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            DrawMode = DrawMode.OwnerDrawFixed
        };
        _occurrenceList.DrawItem += OccurrenceList_DrawItem;
        _occurrenceList.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        ListResultHelpers.AttachCitationTooltip(_occurrenceList,
            i => i < _currentOccurrences.Count ? _currentOccurrences[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_occurrenceList,
            i => i < _currentOccurrences.Count
                ? $"{_currentOccurrences[i].AuthorName}, {_currentOccurrences[i].WorkTitle} [{PassageCitation.Display(_currentOccurrences[i].CitationRef)}]: {_currentOccurrences[i].Text}"
                : null);
        ListResultHelpers.AttachExportMenu(_occurrenceList, () => (
            $"Occurrences of {SelectedHeadwordOrDefault()}",
            _currentOccurrences.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text)).ToList()), this);

        _statusLabel = new Label
        {
            Left = 616,
            Top = 672,
            Width = 756,
            Height = 24,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(sourceLabel);
        Controls.Add(_metreLabel);
        Controls.Add(wordLabel);
        Controls.Add(_wordList);
        Controls.Add(headwordLabel);
        Controls.Add(_headwordList);
        Controls.Add(definitionLabel);
        Controls.Add(_definitionBox);
        Controls.Add(_formsBox);
        Controls.Add(occurrenceLabel);
        Controls.Add(_scopeLabel);
        Controls.Add(scopeButton);
        Controls.Add(_occurrenceList);

        // Opened on a particular word rather than at the top of the line,
        // when the caller had one in mind.
        if (!string.IsNullOrWhiteSpace(selectedWord))
        {
            var target = WordNormalizer.Normalize(selectedWord);
            for (var i = 0; i < _wordList.Items.Count; i++)
            {
                if (WordNormalizer.Normalize(_wordList.Items[i]?.ToString() ?? string.Empty) != target) continue;

                _wordList.SelectedIndex = i;
                break;
            }
        }
        Controls.Add(_statusLabel);

        PopulateWords(sourceNode.Text);
        Load += async (_, _) => await CheckLemmaDataAsync();
        RefreshScopeLabel();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    /// <summary>
    /// The metre row as it reads before a word is chosen: the line's feet, and
    /// how far the reading is settled.
    ///
    /// ReadingCount is reported rather than hidden. One reading is a solved
    /// line and is the common case; more than one means the letters
    /// underdetermine the shape, and a reader looking at a quantity below
    /// should know whether it is a measurement or one of several possibilities
    /// that happen to agree here.
    /// </summary>
    private string DescribeLineMetre()
    {
        if (_scansion == null) return string.Empty;

        if (!_scansion.Scans)
        {
            return _scansion.Failure switch
            {
                ScansionFailure.TooShort =>
                    "Metre:  too short for a hexameter - often a half-line, printed as the poet left it.",
                ScansionFailure.TooLong =>
                    "Metre:  too long for a hexameter.",
                ScansionFailure.Inconsistent =>
                    "Metre:  the right length for a hexameter, but no arrangement of feet fits the spelling. "
                    + "Greek proper names are the usual reason - they keep Greek quantities.",
                _ => "Metre:  nothing to scan on this line."
            };
        }

        var readings = _scansion.ReadingCount == 1
            ? "one reading"
            : $"{_scansion.ReadingCount} readings, so the marks below show only what they agree on";

        var elision = _scansion.Elisions switch
        {
            0 => string.Empty,
            1 => "  ·  1 elision",
            _ => $"  ·  {_scansion.Elisions} elisions"
        };

        return $"Metre:  hexameter  {_scansion.Pattern}  ·  {readings}{elision}"
             + "      (D dactyl, S spondee, ? the readings disagree)";
    }

    /// <summary>
    /// What the metre makes of one word - the row that is the actual point of
    /// this, because it says something no dictionary and no spelling can.
    ///
    /// Latin editions print no macrons, so the letters of "cano" are equally
    /// the first person of cano and something else entirely; the metre makes
    /// its final o long and settles it. Across Virgil, Ovid, Lucretius and
    /// Juvenal the metre settles three quarters of the syllables the spelling
    /// leaves open.
    ///
    /// The marks are on SYLLABLES, not vowels, and the row says so. A long
    /// syllable is long by nature or by position, and "arma" opens with a long
    /// syllable containing a short a - printing it as a long vowel would be
    /// teaching the reader something false about the word.
    /// </summary>
    private void ShowWordMetre(string? word)
    {
        if (_scansion == null) return;

        if (string.IsNullOrWhiteSpace(word) || !_scansion.Scans)
        {
            _metreLabel.Text = DescribeLineMetre();
            return;
        }

        var matches = ScannedWords.Matching(_scansion, word);
        if (matches.Count == 0)
        {
            _metreLabel.Text = DescribeLineMetre();
            return;
        }

        var rendered = matches.Select(Render);
        _metreLabel.Text = $"Metre:  {string.Join("   ·   ", rendered)}"
                         + "      (¯ long syllable, ˘ short, × the metre does not say)";

        static string Render(ScannedWord w)
        {
            if (w.Syllables.All(s => s.Elided)) return $"{w.Text}  elided";

            var marks = string.Join(" ", w.Syllables.Select(s => s.Elided ? "(elided)" : s.Mark));
            return $"{w.Text}   {marks}";
        }
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

    /// <summary>
    /// The headword whose occurrences are currently listed, for labelling an
    /// export. Falls back to the searched word when no headword row is
    /// selected - an export should still be named something meaningful.
    /// </summary>
    private string SelectedHeadwordOrDefault()
    {
        var index = _headwordList.SelectedIndex;
        return index >= 0 && index < _currentHeadwords.Count
            ? _currentHeadwords[index].Headword
            : "this word";
    }

    /// <summary>
    /// Opens the work picker and re-runs the lookup against whatever comes
    /// back.
    ///
    /// The full author and work lists are fetched here rather than held from
    /// construction, so opening Word Study on a word costs nothing extra
    /// unless the scope is actually changed.
    /// </summary>
    private async Task ChooseScopeAsync()
    {
        try
        {
            var authors = await _authorRepo.GetAllAsync();
            var worksByAuthor = await _workRepo.GetAllGroupedByAuthorAsync();

            using var picker = new WorkPickerForm(authors, worksByAuthor, _scopeWorkIds.ToList());
            if (picker.ShowDialog(this) != DialogResult.OK) return;

            _scopeWorkIds.Clear();
            foreach (var id in picker.SelectedWorkIds) _scopeWorkIds.Add(id);

            RefreshScopeLabel();
            await LoadDefinitionAndOccurrencesAsync();
        }
        catch (Exception ex)
        {
            _scopeLabel.Text = $"Couldn't load the text list: {ex.Message}";
        }
    }

    private void RefreshScopeLabel() =>
        _scopeLabel.Text = _scopeWorkIds.Count switch
        {
            0 => "Searching every text",
            1 => "Searching 1 text",
            _ => $"Searching {_scopeWorkIds.Count:N0} texts"
        };

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

        // Off the UI thread - see the note in SearchForm. Microsoft.Data.Sqlite's
        // async methods run synchronously, so awaiting this directly held the
        // window while it gathered every occurrence of the word in the corpus,
        // about a fifth of a second for a common one.
        var hits = await Task.Run(() => _textNodeRepo.SearchByFormsAsync(forms, workIds: _scopeWorkIds));
        _currentOccurrences = hits.Rows;

        _occurrenceList.Items.Clear();
        foreach (var o in _currentOccurrences.Take(500))
        {
            _occurrenceList.Items.Add($"{o.AuthorName}, {o.WorkTitle}: {o.Text}");
        }

        if (_currentOccurrences.Count == 0)
        {
            _occurrenceList.Items.Add("(no occurrences found)");
        }

        ListResultHelpers.RefreshHorizontalExtent(
            _occurrenceList, i => _occurrenceList.Items[i]?.ToString());

        _statusLabel.Text = hits.Truncated
            ? $"{hits.DisplayCount} occurrence(s) of {headword} across the corpus - stopped at the result limit, so this is a sample rather than the full count."
            : $"{_currentOccurrences.Count} occurrence(s) of {headword} across the corpus.";
    }

    private async Task LoadDefinitionAsync(string headword)
    {
        var language = DetectLanguage(headword);

        try
        {
            var entries = await _definitionRepo.GetByHeadwordAsync(headword, language);

            if (entries.Count == 0)
            {
                // Which of the two it is, rather than both at once. The old
                // wording offered "no dictionary is loaded, or this headword
                // isn't in it" and then told the reader to go and load one -
                // advice that is simply wrong when the dictionary is already
                // there, and sends someone off to fix a problem they do not
                // have. Only 43,507 of the Latin lemma data's 139,190 headwords
                // have anything in Lewis and Short behind them, so the second
                // case is much the commoner one by far, and it was the one
                // getting the first case's instructions.
                var loaded = (await _definitionRepo.CountByLanguageAsync())
                    .Any(l => string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase)
                              && l.Count > 0);

                _definitionBox.Text = loaded
                    ? "(no dictionary entry for this headword)\r\n\r\n" +
                      "The dictionary is loaded, but has nothing under this spelling. Lemma data " +
                      "and lexicons number and capitalise headwords differently, so where a form " +
                      "has more than one candidate the others are worth trying - the list above " +
                      "puts the ones the dictionary can answer for first."
                    : "(no dictionary loaded for this language)\r\n\r\n" +
                      "Use \"Load Lemmas...\" and switch Data type to \"Dictionary (lexicon)\" to " +
                      "load one - LSJ for Greek, Lewis & Short for Latin.";
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
