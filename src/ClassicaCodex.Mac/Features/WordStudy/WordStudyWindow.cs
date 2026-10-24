using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Meter;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using Occurrence = (int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone);

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// The classic word-study workflow: pick a word out of a line, find out
/// what dictionary headword it belongs to, see every inflected form of that
/// headword attested in the corpus, and jump to any occurrence.
///
/// This is the feature that only works once lemma data is loaded - without
/// it, every inflected form is an unrelated string and none of this is
/// possible.
///
/// The Mac counterpart of WordStudyForm, behaviour for behaviour: the same
/// lookups in the same order, the same messages, the hexameter scansion of a
/// Latin verse line, the scope that starts at the work the line came from.
/// What changes is the drawing - the occurrence highlighting the Windows list
/// paints by hand is inline text here - and the lookups run off the UI
/// thread, so the window answers while the corpus is searched.
/// </summary>
public sealed class WordStudyWindow : FeatureWindow
{
    /// <summary>How many occurrences the list draws. The status line gives the full count.</summary>
    private const int MaxListed = 500;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();

    /// <summary>
    /// The edition's language code ("grc", "lat", "eng"). Passed in rather
    /// than guessed because English and Latin share an alphabet - nothing
    /// about an English word's spelling distinguishes it from a Latin one, so
    /// only the edition knows.
    /// </summary>
    private readonly string? _language;

    /// <summary>
    /// Which works occurrences are drawn from. Starts as the work the line
    /// came from, because a common word across the whole corpus returns
    /// thousands of lines, stops at the result limit, and tells you only that
    /// the word is common. Empty means everything.
    /// </summary>
    private readonly HashSet<int> _scopeWorkIds = new();

    /// <summary>
    /// What the metre makes of the source line, or null where the question
    /// does not arise - see <see cref="ScanSourceLine"/>.
    /// </summary>
    private readonly Scansion? _scansion;

    private readonly FontFamily _readingFont;
    private readonly FontFamily _definitionFont;

    private readonly ListBox _wordList;
    private readonly ListBox _headwordList;
    private readonly TextBlock _headwordMessage;
    private readonly SelectableTextBlock _definition;
    private readonly ScrollViewer _definitionScroller;
    private readonly SelectableTextBlock _forms;
    private readonly ListBox _occurrenceList;
    private readonly TextBlock _occurrenceMessage;
    private readonly TextBlock _scopeLabel;
    private readonly TextBlock _statusLabel;
    private readonly Control _metreRow;
    private readonly TextBlock _metreBody;
    private readonly TextBlock _metreLegend;

    private List<Occurrence> _currentOccurrences = new();
    private List<HeadwordItem> _currentHeadwords = new();
    private bool _truncated;
    private string _definitionText = string.Empty;
    private string _statusText = string.Empty;
    private string _metreBodyText = string.Empty;
    private string _metreLegendText = string.Empty;

    /// <summary>
    /// Bumped by every lookup, so a slow answer to a word the reader has
    /// already moved on from is dropped rather than written over the answer
    /// to the word now selected. The Windows form ran its lookups on the UI
    /// thread, where they could not overtake one another; off it, they can.
    /// </summary>
    private int _generation;

    private bool _closed;
    private Task _pending = Task.CompletedTask;

    public WordStudyWindow(
        TextNode sourceNode, string? language = null, int? workId = null, string? selectedWord = null,
        FontFamily? readingFont = null)
    {
        _language = language;
        if (workId != null) _scopeWorkIds.Add(workId.Value);

        _readingFont = readingFont ?? ToolPane.Font("Cc.SourceFont");
        _definitionFont = new FontFamily($"{ToolPane.Font("Cc.TranslationFont")}, {ToolPane.Font("Cc.SourceFont")}");

        Title = "Word Study";
        Width = 1240;
        Height = 780;
        MinWidth = 900;
        MinHeight = 560;

        // Scanned once, here, rather than per word selection - the search runs
        // over every reading of the spelling against thirty-two shapes, and
        // the line does not change while the window is open.
        _scansion = ScanSourceLine(sourceNode);

        // The line itself, as the reader saw it.
        var citation = PassageCitation.Display(sourceNode.CitationRef, sourceNode.Milestone);
        var sourceLine = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (citation.Length > 0)
        {
            sourceLine.Inlines!.Add(PassageText.Ink(sourceLine, citation + "   ", "Cc.InkFaint", FontFamily.Default, 13));
        }
        sourceLine.Inlines!.Add(new Run(sourceNode.Text) { FontFamily = _readingFont, FontSize = 19 });
        ToolTip.SetTip(sourceLine, $"{PassageCitation.Bracketed(sourceNode.CitationRef, sourceNode.Milestone)} {PassageText.RowText(sourceNode.Text, 1200)}".Trim());

        // The metre row is drawn only when there is a metre to report, and
        // the layout closes up when there is not - which is most of the
        // library.
        var metreLabel = new TextBlock { Text = "METRE", Classes = { "section" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _metreBody = new TextBlock { FontFamily = _readingFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _metreLegend = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        var metreDock = new DockPanel();
        DockPanel.SetDock(metreLabel, Dock.Left);
        DockPanel.SetDock(_metreLegend, Dock.Right);
        metreDock.Children.Add(metreLabel);
        metreDock.Children.Add(_metreLegend);
        metreDock.Children.Add(_metreBody);
        _metreRow = new Border { Classes = { "notice" }, Child = metreDock, Margin = new Thickness(0, 10, 0, 0), IsVisible = _scansion != null };
        ShowLineMetre();

        var header = new StackPanel { Margin = new Thickness(2, 0, 2, 14) };
        header.Children.Add(sourceLine);
        header.Children.Add(_metreRow);

        // Words in this line.
        _wordList = new ListBox
        {
            FontFamily = _readingFont,
            FontSize = 15,
            Padding = new Thickness(4, 6),
            SelectionMode = SelectionMode.Single
        };
        _wordList.SelectionChanged += (_, _) =>
        {
            var word = _wordList.SelectedItem as string;
            ShowWordMetre(word);
            _pending = LoadHeadwordsAsync(word);
        };

        // Dictionary headwords.
        _headwordList = new ListBox
        {
            Padding = new Thickness(4, 4),
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<HeadwordItem?>((item, _) => item == null ? null : BuildHeadwordRow(item), supportsRecycling: false)
        };
        _headwordList.SelectionChanged += (_, _) =>
        {
            if (_headwordList.SelectedIndex >= 0) _pending = LoadDefinitionAndOccurrencesAsync();
        };
        _headwordMessage = ToolPane.Message();

        // The definition.
        _definition = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = _definitionFont,
            FontSize = 13.5,
            LineHeight = 20,
            Margin = new Thickness(14, 10, 14, 14)
        };
        _definitionScroller = new ScrollViewer { Content = _definition };

        // Every attested form.
        _forms = new SelectableTextBlock
        {
            Text = "Attested forms will appear here.",
            TextWrapping = TextWrapping.Wrap,
            FontFamily = _readingFont,
            FontSize = 13.5,
            LineHeight = 20,
            Margin = new Thickness(14, 10, 14, 14)
        };
        _forms.Bind(TextBlock.ForegroundProperty, _forms.GetResourceObservable("Cc.InkMuted"));

        // Occurrences.
        _occurrenceList = new ListBox
        {
            Padding = new Thickness(4, 6),
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<OccurrenceRow?>((row, _) => row == null ? null : BuildOccurrenceRow(row), supportsRecycling: false)
        };
        _occurrenceList.DoubleTapped += async (_, _) => await JumpToSelectedAsync();
        _occurrenceList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedAsync();
            }
            else if (e.Key == Key.C && e.KeyModifiers.HasFlag(CommandKey))
            {
                e.Handled = true;
                await CopySelectedAsync();
            }
        };
        AttachOccurrenceMenu();
        _occurrenceMessage = ToolPane.Message();

        // "This text" and "everything" are the ends of a range, and the
        // useful answers are often between them - the plays of one trilogy,
        // or everything by one author. A checkbox could only offer the ends.
        _scopeLabel = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var scopeButton = ToolPane.GlyphButton("Choose Texts…", "Filter");
        scopeButton.Padding = new Thickness(10, 3);
        scopeButton.Click += async (_, _) => await ChooseScopeAsync();
        var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _scopeLabel, scopeButton } };

        _statusLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        var wordsPane = ToolPane.Build("WORDS IN THIS LINE", _wordList);
        var headwordsPane = ToolPane.Build("DICTIONARY HEADWORDS", ToolPane.Overlay(_headwordList, _headwordMessage));
        var definitionPane = ToolPane.Build("DEFINITION", _definitionScroller);
        var formsPane = ToolPane.Build("ATTESTED FORMS", new ScrollViewer { Content = _forms });
        var occurrencesPane = ToolPane.Build(
            "OCCURRENCES · DOUBLE-CLICK TO JUMP",
            ToolPane.Overlay(_occurrenceList, _occurrenceMessage),
            scopeRow,
            _statusLabel);

        var middle = new Grid { RowDefinitions = new RowDefinitions("150,12,*,12,150") };
        Grid.SetRow(definitionPane, 2);
        Grid.SetRow(formsPane, 4);
        middle.Children.Add(headwordsPane);
        middle.Children.Add(definitionPane);
        middle.Children.Add(formsPane);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("200,12,390,12,*") };
        var leftSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        var rightSplitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(leftSplitter, 1);
        Grid.SetColumn(middle, 2);
        Grid.SetColumn(rightSplitter, 3);
        Grid.SetColumn(occurrencesPane, 4);
        columns.Children.Add(wordsPane);
        columns.Children.Add(leftSplitter);
        columns.Children.Add(middle);
        columns.Children.Add(rightSplitter);
        columns.Children.Add(occurrencesPane);

        var layout = new DockPanel { Margin = new Thickness(18, 16, 18, 18) };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(columns);
        Content = layout;

        PopulateWords(sourceNode.Text);
        RefreshScopeLabel();

        Closed += (_, _) => _closed = true;

        // The lemma count first, then the word the caller had in mind, so the
        // status line ends on the answer about that word rather than on the
        // count it was opened with.
        Opened += (_, _) => _pending = OpenOnAsync(selectedWord);
    }

    // For the tests, which read what the reader would see.
    internal IReadOnlyList<string> Words => _wordList.ItemsSource as IReadOnlyList<string> ?? Array.Empty<string>();
    internal ListBox WordList => _wordList;
    internal ListBox HeadwordList => _headwordList;
    internal IReadOnlyList<HeadwordItem> Headwords => _currentHeadwords;
    internal string DefinitionText => _definitionText;
    internal string FormsText => _forms.Text ?? string.Empty;
    internal IReadOnlyList<Occurrence> Occurrences => _currentOccurrences;
    internal string StatusText => _statusText;
    internal string ScopeText => _scopeLabel.Text ?? string.Empty;
    internal bool HasMetreRow => _metreRow.IsVisible;
    internal string MetreText => _metreRow.IsVisible
        ? "Metre:  " + _metreBodyText + (_metreLegendText.Length == 0 ? string.Empty : "      (" + _metreLegendText + ")")
        : string.Empty;

    /// <summary>Waits until every lookup started so far has answered.</summary>
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

        // A line that does not scan is still worth a row: the reader can see
        // that the question was asked and answered, rather than wondering
        // whether the feature is broken or the text is unusual. What is not
        // worth a row is a line that was never a candidate, which is what the
        // three tests above have already removed.
        return HexameterScanner.Scan(sourceNode.Text);
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
    private void ShowLineMetre()
    {
        if (_scansion == null) return;

        if (!_scansion.Scans)
        {
            SetMetre(_scansion.Failure switch
            {
                ScansionFailure.TooShort =>
                    "too short for a hexameter - often a half-line, printed as the poet left it.",
                ScansionFailure.TooLong =>
                    "too long for a hexameter.",
                ScansionFailure.Inconsistent =>
                    "the right length for a hexameter, but no arrangement of feet fits the spelling. "
                    + "Greek proper names are the usual reason - they keep Greek quantities.",
                _ => "nothing to scan on this line."
            }, string.Empty);
            return;
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

        SetMetre($"hexameter  {_scansion.Pattern}  ·  {readings}{elision}", "D dactyl, S spondee, ? the readings disagree");
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
            ShowLineMetre();
            return;
        }

        var matches = ScannedWords.Matching(_scansion, word);
        if (matches.Count == 0)
        {
            ShowLineMetre();
            return;
        }

        SetMetre(string.Join("   ·   ", matches.Select(Render)), "¯ long syllable, ˘ short, × the metre does not say");

        static string Render(ScannedWord w)
        {
            if (w.Syllables.All(s => s.Elided)) return $"{w.Text}  elided";

            var marks = string.Join("  ", w.Syllables.Select(s => s.Elided ? "(elided)" : s.Mark));
            return $"{w.Text}   {marks}";
        }
    }

    /// <summary>
    /// Writes the metre row. The quantity marks are drawn larger and in the
    /// accent's ink: a macron and a breve are spacing accents, made to sit
    /// over a letter, and at the size of the words beside them they are a
    /// couple of pixels high - too small to tell apart, on the one row whose
    /// whole point is telling them apart.
    /// </summary>
    private void SetMetre(string body, string legend)
    {
        _metreBodyText = body;
        _metreLegendText = legend;
        WriteWithMarks(_metreBody, body, 22);
        WriteWithMarks(_metreLegend, legend, 17);
    }

    private static void WriteWithMarks(TextBlock block, string text, double markSize)
    {
        var inlines = new InlineCollection();
        var plain = new System.Text.StringBuilder();

        void Flush()
        {
            if (plain.Length == 0) return;
            inlines.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        foreach (var c in text)
        {
            if (c is '¯' or '˘' or '×')
            {
                Flush();
                var mark = PassageText.Ink(block, c.ToString(), "Cc.Accent", size: markSize);
                mark.FontWeight = FontWeight.SemiBold;
                inlines.Add(mark);
            }
            else
            {
                plain.Append(c);
            }
        }

        Flush();
        block.Text = null;
        block.Inlines = inlines;
    }

    private void PopulateWords(string text)
    {
        // Rejoined first. A word the printed page broke across two lines is
        // "gra<SHY> tiam" in the text, and without this the list offered two
        // entries, "gra" and "tiam", neither of which is a word and neither of
        // which has a dictionary entry or a lemma to find.
        _wordList.ItemsSource = WordNormalizer.JoinSoftHyphenBreaks(text)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetter).ToArray()))
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task OpenOnAsync(string? selectedWord)
    {
        await CheckLemmaDataAsync();

        // Opened on a particular word rather than at the top of the line,
        // when the caller had one in mind.
        if (string.IsNullOrWhiteSpace(selectedWord)) return;

        var target = WordNormalizer.Normalize(selectedWord);
        var match = Words.FirstOrDefault(w => WordNormalizer.Normalize(w) == target);
        if (match != null)
        {
            _wordList.SelectedItem = match;
            _wordList.ScrollIntoView(match);
        }
    }

    private async Task CheckLemmaDataAsync()
    {
        try
        {
            var count = await Task.Run(() => _lemmaRepo.CountAsync());
            if (_closed) return;

            if (count == 0)
            {
                // Setup's Lemma Data steps are the Mac's way in; the Windows
                // wording names a Load Lemmas button that only Windows has.
                SetStatus("No lemma data loaded - install the Lemma Data for this language in Setup first.", warning: true);
            }
            else
            {
                SetStatus($"{count:N0} lemma mappings available.");
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, warning: true);
        }
    }

    private void SetStatus(string text, bool warning = false)
    {
        _statusText = text;
        _statusLabel.Text = text;
        _statusLabel.Classes.Set("warning", warning);
    }

    private void ShowOccurrenceMessage(string? message)
    {
        _occurrenceMessage.Text = message;
        _occurrenceMessage.IsVisible = message != null;
    }

    private void ClearOccurrences()
    {
        _currentOccurrences = new List<Occurrence>();
        _truncated = false;
        _occurrenceList.ItemsSource = null;
        ShowOccurrenceMessage(null);
    }

    private async Task LoadHeadwordsAsync(string? word)
    {
        var generation = ++_generation;

        _currentHeadwords = new List<HeadwordItem>();
        _headwordList.ItemsSource = null;
        _headwordMessage.IsVisible = false;
        ClearOccurrences();
        _forms.Text = string.Empty;

        // Cleared here as well. The definition panel is only written when a
        // headword gets selected, so on a word with no headwords at all
        // that step never runs and the previous word's definition just sits
        // there - looking like it belongs to the word now selected.
        SetDefinition(string.Empty);

        if (word == null) return;

        List<(string Headword, string? PartOfSpeech)> found;
        try
        {
            found = await Task.Run(() => _lemmaRepo.GetHeadwordsForFormAsync(word, _language));
        }
        catch (Exception ex)
        {
            if (generation == _generation && !_closed) SetStatus($"Couldn't look the word up: {ex.Message}", warning: true);
            return;
        }

        if (generation != _generation || _closed) return;

        if (found.Count == 0)
        {
            _headwordMessage.Text = "(no dictionary entry)";
            _headwordMessage.IsVisible = true;

            // English needs its own explanation. WordNet is a lexicon of
            // content words - nouns, verbs, adjectives, adverbs - and
            // deliberately excludes the function words that hold sentences
            // together. So "from", "of", "the" and "my" genuinely have no
            // entry, and saying "not found in lemma data" for those reads
            // as a loading failure when nothing is wrong at all.
            _forms.Text = _language == "eng"
                ? "WordNet covers nouns, verbs, adjectives and adverbs. Function words - prepositions, " +
                  "articles, pronouns, conjunctions - aren't included in it, so common words like " +
                  "\"from\", \"the\" and \"my\" have no entry by design."
                : "No headword on record for this form. Either the lemma data doesn't " +
                  "cover it, or no lemma data is loaded for this language.";
            return;
        }

        // Rows the corpus could not analyse are dropped, as long as something
        // analysed survives.
        //
        // The Greek data marks an unanalysed accent variant with the tag
        // u--------, and in the AGDT tagset u is punctuation - correctly, for
        // that tagset, and uselessly here, because these rows are words. So
        // the commonest words in the language opened a panel that repeated
        // the word back with the label "punctuation", over and over: ten of
        // ἦν's seventeen rows, thirteen of οὐ's twenty-seven, eight of μή's
        // ten. A reader clicking ἦν to find out what it is was told, nine
        // times, that it is punctuation.
        //
        // None of them carry information a word lookup can use. They are held
        // back only when every row is one, since an empty list would say less
        // than a wrong label.
        var analysed = found
            .Where(h => MorphologyDecoder.Decode(h.PartOfSpeech).PartOfSpeech != "punctuation")
            .ToList();

        if (analysed.Count > 0) found = analysed;

        // Deduplicate on what the reader actually sees, not on the raw tag.
        // The corpus stores the same analysis in two tag layouts (a nine- and
        // a ten-character form), so a word can have two rows that are
        // genuinely distinct in the database but decode to exactly the same
        // parse - which showed up as the identical line listed twice.
        var seenDisplayText = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<HeadwordItem>();

        foreach (var (headword, pos) in found)
        {
            // The stored tag is positional and unreadable on its own
            // ("v-sppemn-"), so show the decoded parse where the format is
            // recognized. An unrecognized tag still gets shown raw rather
            // than hidden - it's real information from the corpus, just not
            // in a vocabulary this can safely interpret.
            var parse = MorphologyDecoder.Decode(pos);
            var item = new HeadwordItem(
                headword,
                pos,
                parse.IsDecoded ? parse.Description : null,
                parse.IsDecoded ? null : pos);

            if (!seenDisplayText.Add(item.DisplayText)) continue;
            items.Add(item);
        }

        // Replaces the list the selection indexes into, so the two stay
        // aligned - the definition lookup relies on it.
        _currentHeadwords = items;
        _headwordList.ItemsSource = items;
        if (items.Count > 0) _headwordList.SelectedIndex = 0;
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
    /// </summary>
    private async Task ChooseScopeAsync()
    {
        try
        {
            var chosen = await WorkPickerWindow.ChooseAsync(this, _scopeWorkIds.ToList());
            if (chosen == null) return;

            await ApplyScopeAsync(chosen);
        }
        catch (Exception ex)
        {
            _scopeLabel.Text = $"Couldn't load the text list: {ex.Message}";
        }
    }

    /// <summary>Narrows or widens the occurrences to these works - empty for every text - and looks again.</summary>
    internal async Task ApplyScopeAsync(IReadOnlyCollection<int> workIds)
    {
        _scopeWorkIds.Clear();
        foreach (var id in workIds) _scopeWorkIds.Add(id);

        RefreshScopeLabel();
        _pending = LoadDefinitionAndOccurrencesAsync();
        await _pending;
    }

    private void RefreshScopeLabel() =>
        _scopeLabel.Text = _scopeWorkIds.Count switch
        {
            0 => "Searching every text",
            1 => "Searching 1 text",
            _ => $"Searching {_scopeWorkIds.Count:N0} texts"
        };

    /// <summary>
    /// Where the count was taken, for the status line. The Windows line says
    /// "across the corpus" whatever the scope; with the scope starting at one
    /// text, that described a search that had not been made.
    /// </summary>
    private string ScopePhrase(int scopeCount) => scopeCount switch
    {
        0 => "across the corpus",
        1 => "in the text searched",
        _ => $"in the {scopeCount:N0} texts searched"
    };

    private async Task LoadDefinitionAndOccurrencesAsync()
    {
        var index = _headwordList.SelectedIndex;
        if (index < 0 || index >= _currentHeadwords.Count) return;

        var generation = ++_generation;
        var headword = _currentHeadwords[index].Headword;
        var scope = _scopeWorkIds.ToList();

        SetDefinition("Looking up...");
        ClearOccurrences();
        ShowOccurrenceMessage("Searching...");

        await LoadDefinitionAsync(headword, generation);
        if (generation != _generation || _closed) return;

        try
        {
            var forms = await Task.Run(() => _lemmaRepo.GetFormsForHeadwordAsync(headword, _language));
            if (generation != _generation || _closed) return;

            _forms.Text = forms.Count == 0
                ? "(no attested forms on record)"
                : $"Attested forms of {headword} ({forms.Count}):\n\n" + string.Join(", ", forms);

            // Normalized so the highlighter can match regardless of accentuation
            // or precomposed-vs-combining Unicode, same as the search itself.
            var highlight = forms
                .Select(WordNormalizer.Normalize)
                .Where(f => f.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            // Off the UI thread. Microsoft.Data.Sqlite's async methods run
            // synchronously, so awaiting this directly would hold the window
            // while it gathered every occurrence of the word in the corpus.
            var hits = await Task.Run(() => _textNodeRepo.SearchByFormsAsync(forms, workIds: scope));
            if (generation != _generation || _closed) return;

            _currentOccurrences = hits.Rows;
            _truncated = hits.Truncated;

            _occurrenceList.ItemsSource = _currentOccurrences
                .Take(MaxListed)
                .Select(o => new OccurrenceRow(o, highlight))
                .ToList();

            ShowOccurrenceMessage(_currentOccurrences.Count == 0 ? "(no occurrences found)" : null);

            var where = ScopePhrase(scope.Count);
            var listed = _currentOccurrences.Count > MaxListed ? $" The first {MaxListed:N0} are listed." : string.Empty;
            SetStatus(hits.Truncated
                ? $"{hits.DisplayCount} occurrence(s) of {headword} {where} - stopped at the result limit, so this is a sample rather than the full count.{listed}"
                : $"{_currentOccurrences.Count:N0} occurrence(s) of {headword} {where}.{listed}");
        }
        catch (Exception ex)
        {
            if (generation != _generation || _closed) return;
            ShowOccurrenceMessage(null);
            SetStatus($"Couldn't search for occurrences: {ex.Message}", warning: true);
        }
    }

    private async Task LoadDefinitionAsync(string headword, int generation)
    {
        var language = DetectLanguage(headword);

        try
        {
            var entries = await Task.Run(() => _definitionRepo.GetByHeadwordAsync(headword, language));
            if (generation != _generation || _closed) return;

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
                var counts = await Task.Run(() => _definitionRepo.CountByLanguageAsync());
                if (generation != _generation || _closed) return;

                var loaded = counts.Any(l =>
                    string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase) && l.Count > 0);

                SetDefinition(loaded
                    ? "(no dictionary entry for this headword)\n\n" +
                      "The dictionary is loaded, but has nothing under this spelling. Lemma data " +
                      "and lexicons number and capitalise headwords differently, so where a form " +
                      "has more than one candidate the others are worth trying - the list above " +
                      "puts the ones the dictionary can answer for first."
                    : "(no dictionary loaded for this language)\n\n" +
                      "Install \"Dictionaries (LSJ + Lewis & Short)\" in Setup to load one - LSJ " +
                      "for Greek, Lewis & Short for Latin.");
                return;
            }

            ShowEntries(entries, language);
        }
        catch (Exception ex)
        {
            if (generation == _generation && !_closed) SetDefinition($"Couldn't load definition: {ex.Message}");
        }
    }

    private void SetDefinition(string text)
    {
        _definitionText = text;
        _definition.Inlines = null;
        _definition.Text = text;
        _definitionScroller.Offset = default;
    }

    /// <summary>
    /// The dictionary's entries, each headed by its headword and lexicon, the
    /// way a printed lexicon sets them.
    /// </summary>
    private void ShowEntries(IReadOnlyList<(string Headword, string Entry, string? Source)> entries, string language)
    {
        var inlines = new InlineCollection();
        var plain = new System.Text.StringBuilder();

        // More than one entry means the homograph numbering in the lemma
        // data and the lexicon don't line up - worth saying so rather
        // than letting it look like duplication.
        if (entries.Count > 1)
        {
            var note = $"{entries.Count} entries share this spelling - the lexicon numbers homographs " +
                       "differently from the lemma data, so all candidates are shown.";
            var noteRun = PassageText.Ink(_definition, note, "Cc.InkMuted", FontFamily.Default, 12);
            noteRun.FontStyle = FontStyle.Italic;
            inlines.Add(noteRun);
            inlines.Add(new LineBreak());
            inlines.Add(new LineBreak());
            plain.Append(note).Append("\n\n");
        }

        var first = true;
        foreach (var (entryHeadword, entry, source) in entries)
        {
            if (!first)
            {
                inlines.Add(new LineBreak());
                inlines.Add(PassageText.Ink(_definition, new string('—', 12), "Cc.Rule"));
                inlines.Add(new LineBreak());
                inlines.Add(new LineBreak());
                plain.Append("\n").Append(new string('-', 40)).Append("\n\n");
            }

            first = false;

            // LSJ stores its entry bodies in Beta Code, same as its
            // headwords - the Greek has to be decoded to Unicode or the
            // definition reads as "sunair-esiw/ths , ou , o(". Only the
            // Greek is touched; the English glosses interleaved with it
            // are left alone. Latin (Lewis & Short) bodies are already
            // Latin script and pass through untouched.
            var body = Reflow(language == "grc" ? BetaCodeConverter.ConvertMixed(entry) : entry);

            inlines.Add(new Run(entryHeadword) { FontWeight = FontWeight.Bold, FontSize = 16, FontFamily = _readingFont });
            plain.Append(entryHeadword);
            if (!string.IsNullOrEmpty(source))
            {
                inlines.Add(PassageText.Ink(_definition, $"   {source}", "Cc.InkFaint", FontFamily.Default, 11.5));
                plain.Append("   [").Append(source).Append(']');
            }

            inlines.Add(new LineBreak());
            inlines.Add(new LineBreak());
            inlines.Add(new Run(body));
            inlines.Add(new LineBreak());
            plain.Append("\n\n").Append(body).Append('\n');
        }

        _definitionText = plain.ToString();
        _definition.Text = null;
        _definition.Inlines = inlines;
        _definitionScroller.Offset = default;
    }

    /// <summary>
    /// A lexicon entry as prose. The ingested bodies carry a line break at
    /// every element boundary of the TEI they came from - around each cited
    /// Greek phrase, each abbreviation, each sense label - so set as written
    /// "μῆνις , Dor. and Aeol." is four lines. The breaks are joined back
    /// into running text, and kept only where a sense begins ("A.", "II.",
    /// "2."), which is where a printed lexicon starts a new paragraph too.
    /// Nothing but whitespace changes. A lone lower-case letter is not taken
    /// for a sense: LSJ abbreviates the headword to its initial ("m. χθονίων"
    /// under μῆνις), and those are mid-sentence.
    /// </summary>
    internal static string Reflow(string body)
    {
        var paragraphs = new List<List<string>> { new() };

        foreach (var raw in body.Split('\n'))
        {
            var fragment = raw.Trim();
            if (fragment.Length == 0) continue;

            if (SenseLabel.IsMatch(fragment) && paragraphs[^1].Count > 0) paragraphs.Add(new List<string>());
            paragraphs[^1].Add(fragment);
        }

        return string.Join("\n\n", paragraphs.Where(p => p.Count > 0).Select(p => string.Join(" ", p)));
    }

    private static readonly System.Text.RegularExpressions.Regex SenseLabel =
        new(@"^(?:[IVX]+|[A-Z]|\d+)\.(?:\s|$)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Which dictionary to look a headword up in: the language of the edition
    /// it came from, falling back to the script only when the edition does not
    /// say.
    ///
    /// The fallback is what this method used to be, in full - Greek letters
    /// meant Greek and everything else meant Latin, on the reasoning that
    /// script is an unambiguous signal "for these two languages". It was, for
    /// two.
    /// </summary>
    private string DetectLanguage(string headword)
    {
        // The edition's own language, whenever there is one. Guessing from the
        // script is only ever a fallback, and it had to stop being the first
        // answer the moment a third language written in Latin letters arrived:
        // every Middle High German headword would have been looked up in the
        // Latin dictionary, and "an", "in", "a" and "her" are all real Lewis
        // and Short headwords - so the pane would not have come back empty, it
        // would have come back confidently wrong, with a Latin entry under a
        // German word.
        if (!string.IsNullOrWhiteSpace(_language)) return _language;

        foreach (var c in headword)
        {
            if ((c >= 'Ͱ' && c <= 'Ͽ') || (c >= 'ἀ' && c <= '῿')) return "grc";
        }

        return "lat";
    }

    private Control BuildHeadwordRow(HeadwordItem item)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 1) };
        text.Inlines!.Add(new Run(item.Headword) { FontFamily = _readingFont, FontSize = 15, FontWeight = FontWeight.SemiBold });

        if (item.Parse != null)
            text.Inlines.Add(PassageText.Ink(text, "  —  " + item.Parse, "Cc.InkMuted", FontFamily.Default, 12.5));
        else if (item.RawTag != null)
            text.Inlines.Add(PassageText.Ink(text, $"  [{item.RawTag}]", "Cc.InkFaint", ToolPane.Font("Cc.MonoFont"), 12));

        return text;
    }

    /// <summary>
    /// One occurrence: where it is from, quietly, then the passage with the
    /// headword's forms set off. The citation is on the tooltip, the copy and
    /// the export rather than the row, as in every Windows result list - it
    /// is the widest thing on a row and the least read while scanning.
    /// </summary>
    private Control BuildOccurrenceRow(OccurrenceRow row)
    {
        var o = row.Hit;
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 3), LineHeight = 20 };
        text.Inlines!.Add(PassageText.Ink(text, $"{o.AuthorName}, {o.WorkTitle}:  ", "Cc.InkMuted", FontFamily.Default, 12));

        var font = ScriptFonts.Compose(_readingFont, ScriptFonts.FamilyFor(o.Text));
        PassageText.AddHighlighted(text, PassageText.RowText(o.Text), row.Forms.Contains, font, 14.5);

        ToolTip.SetTip(text, PassageCitation.Bracketed(o.CitationRef, o.Milestone));
        ToolTip.SetShowDelay(text, 700);
        return text;
    }

    /// <summary>⌘ on a Mac, Control elsewhere - what the menu bar's own key equivalents use.</summary>
    internal static KeyModifiers CommandKey => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private static string FullLine(Occurrence o) =>
        $"{o.AuthorName}, {o.WorkTitle} [{PassageCitation.Display(o.CitationRef, o.Milestone)}]: {o.Text}";

    /// <summary>
    /// The right-click menu: the whole line of the passage under the pointer,
    /// then the copy and export every Mac result table has. The export reads
    /// the gathered set rather than the visible rows, so it carries every
    /// occurrence found and not only the first five hundred listed.
    /// </summary>
    private void AttachOccurrenceMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copy Passage", Icon = Glyphs.Icon("Copy", 14), InputGesture = new KeyGesture(Key.C, CommandKey) };
        copy.Click += async (_, _) => await CopySelectedAsync();
        menu.Items.Add(copy);
        menu.Opening += (_, _) => copy.IsEnabled = _occurrenceList.SelectedItem is OccurrenceRow;
        _occurrenceList.ContextMenu = menu;

        TableExport.Attach(
            _occurrenceList,
            () => $"Occurrences of {SelectedHeadwordOrDefault()}",
            () => ExportRows(_currentOccurrences),
            () => ExportRows(_occurrenceList.SelectedItem is OccurrenceRow row ? new[] { row.Hit } : Array.Empty<Occurrence>()),
            ExportNotes);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExportRows(IEnumerable<Occurrence> occurrences)
    {
        var table = new List<IReadOnlyList<string>> { new[] { "Author", "Work", "Citation", "Passage" } };
        table.AddRange(occurrences.Select(o => (IReadOnlyList<string>)new[]
        {
            o.AuthorName, o.WorkTitle, PassageCitation.Display(o.CitationRef, o.Milestone), o.Text
        }));
        return table;
    }

    private IReadOnlyList<string> ExportNotes()
    {
        var notes = new List<string>
        {
            $"Classica Codex word study - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Occurrences of {SelectedHeadwordOrDefault()}, every attested form - {ScopeText.ToLowerInvariant()}."
        };

        if (_truncated)
            notes.Add("The search stopped at its result limit, so this is a sample rather than the full count.");

        return notes;
    }

    private async Task CopySelectedAsync()
    {
        if (_occurrenceList.SelectedItem is OccurrenceRow row) await ClipboardText.SetAsync(this, FullLine(row.Hit));
    }

    internal async Task JumpToOccurrenceAsync(int index)
    {
        _occurrenceList.SelectedIndex = index;
        await JumpToSelectedAsync();
    }

    private async Task JumpToSelectedAsync()
    {
        if (_occurrenceList.SelectedItem is not OccurrenceRow row || OnNavigate == null) return;

        await NavigateAsync(row.Hit.WorkId, row.Hit.TextNodeId);
        Close();
    }

    /// <summary>
    /// A candidate headword for the selected word, with its parse decoded
    /// where the tag's format is known and the raw tag kept where it is not.
    /// </summary>
    internal sealed record HeadwordItem(string Headword, string? PartOfSpeech, string? Parse, string? RawTag)
    {
        /// <summary>What the reader sees, which is what duplicates are judged on.</summary>
        public string DisplayText => Parse != null ? $"{Headword}  -  {Parse}"
            : RawTag != null ? $"{Headword}  [{RawTag}]"
            : Headword;

        public override string ToString() => DisplayText;
    }

    /// <summary>An occurrence as the list draws it, with the forms to set off in it.</summary>
    private sealed record OccurrenceRow(Occurrence Hit, HashSet<string> Forms);
}
