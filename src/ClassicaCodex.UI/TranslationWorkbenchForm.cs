using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Translating a work yourself, one passage at a time, with the lexicon and
/// the grammar at hand.
///
/// This exists partly because it sidesteps a problem rather than solving it.
/// Importing somebody else's finished translation means aligning a file of
/// paragraphs onto the original's citation references, and getting that
/// slightly wrong misaligns everything downstream in a way that is very hard
/// to notice. Translating in place has no alignment step at all: the passage
/// on screen has a citation reference, and whatever gets typed belongs to it
/// by construction.
///
/// Everything in the side panel is looked up rather than generated - the
/// headword from the lemma data, the parse from its morphology tag, the
/// entry from LSJ or Lewis and Short. That matters for trusting it: this is
/// the same information a printed commentary would give, not a guess about
/// what the passage means.
/// </summary>
public class TranslationWorkbenchForm : Form
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly int _translationEditionId;
    private readonly string? _sourceLanguage;

    private readonly List<TextNode> _sourcePassages;
    private readonly Dictionary<string, string> _myTranslations;

    private int _index;
    private bool _revealed;

    private readonly Label _headerLabel;
    private readonly Label _progressLabel;
    private readonly ListBox _sourceWords;
    private readonly RichTextBox _sourceBox;
    private readonly ComboBox _gotoBox;

    // Set while the passage picker is being driven by code, so its own
    // SelectedIndexChanged doesn't treat that as a request to navigate.
    private bool _navigating;

    // What every citation reference in this work starts with. Some corpora
    // store a full CTS URN per line, so 45 identical characters precede the
    // part that differs and every entry in the picker reads the same.
    private readonly string _citationPrefix;
    private readonly TextBox _myTranslationBox;
    private readonly TextBox _wordPanel;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _glossWordButton;
    private readonly Button _suggestButton;
    private readonly Button _revealButton;
    private readonly Label _statusLabel;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    private PassageAligner? _publishedAligner;

    // Every other translation of this work, so the one to check against can
    // be changed without leaving. Unlike the source edition this is safe to
    // switch at any time - it is only ever read.
    private readonly List<(Edition Edition, string Label)> _comparisons;
    private readonly ComboBox _comparisonBox;
    private readonly Func<int, Task<PassageAligner>> _loadAligner;

    public TranslationWorkbenchForm(
        Work work, string authorName, int translationEditionId, string? sourceLanguage,
        List<TextNode> sourcePassages, Dictionary<string, string> existingTranslations,
        List<(Edition Edition, string Label)> comparisons,
        Func<int, Task<PassageAligner>> loadAligner)
    {
        _comparisons = comparisons;
        _loadAligner = loadAligner;
        _citationPrefix = CommonCitationPrefix(sourcePassages);
        _work = work;
        _authorName = authorName;
        _translationEditionId = translationEditionId;
        _sourceLanguage = sourceLanguage;
        _sourcePassages = sourcePassages;
        _myTranslations = existingTranslations;


        Text = $"Translate - {authorName}, {work.Title}";
        AppIcons.ApplyWindowIcon(this, "Translate");
        ClientSize = new Size(1164, 740);
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterParent;

        _headerLabel = new Label
        {
            Left = 14, Top = 12, Width = 556, Height = 22,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font, FontStyle.Bold)
        };

        // Previous and Next alone means passage 40 of 1,646 is forty clicks
        // away, which is no way to pick at a work over months. Doubles as
        // the progress view: every passage is listed with a tick if it has
        // been done.
        var gotoLabel = new Label
        {
            Text = "Go to:", Left = 580, Top = 14, Width = 46, Height = 22,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _gotoBox = new ComboBox
        {
            Left = 628, Top = 10, Width = 260, Height = 24,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var passage in sourcePassages) _gotoBox.Items.Add(DescribePassage(passage, existingTranslations));

        // The list is far wider than the box it drops from, which is what
        // makes the preview text worth having at all.
        _gotoBox.DropDownWidth = 620;
        _gotoBox.SelectedIndexChanged += async (_, _) => await JumpToSelectedAsync();

        _progressLabel = new Label
        {
            Left = 898, Top = 12, Width = 252, Height = 22,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = Color.DimGray
        };

        // The passage twice over: as running text to read, and as a list of
        // its own words to click. A single rich control could do both, but
        // word hit-testing inside wrapped text is fiddly and this keeps the
        // passage readable as a passage.
        var sourceLabel = new Label { Text = "Passage:", Left = 14, Top = 40, Width = 200 };
        // A RichTextBox rather than a TextBox so the passage either side can
        // be shown dimmed around the current one. Verse citations cut across
        // sentences constantly - a relative pronoun at the end of one
        // passage resolves in the next - and translating a clause with both
        // halves off screen is how you produce something confidently wrong.
        _sourceBox = new RichTextBox
        {
            Left = 14, Top = 62, Width = 700, Height = 184,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true, BorderStyle = BorderStyle.Fixed3D,
            Font = new Font("Palatino Linotype", 13F)
        };

        var wordsLabel = new Label
        {
            Text = "Click a word:", Left = 14, Top = 254, Width = 200
        };
        _sourceWords = new ListBox
        {
            Left = 14, Top = 276, Width = 200, Height = 366,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font("Palatino Linotype", 12F)
        };
        _sourceWords.SelectedIndexChanged += async (_, _) => await ShowWordAsync();

        var panelLabel = new Label
        {
            Text = "Headword, grammar, and dictionary:", Left = 226, Top = 254, Width = 320
        };

        // Sits by the word list rather than with the AI buttons because it
        // belongs to reading the passage, not to translating it - and it is
        // the one thing here that is useful before you can do either.
        var alphabetButton = new Button
        {
            Text = "Alphabet", Left = 588, Top = 248, Width = 126, Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        alphabetButton.Click += (_, _) =>
        {
            using var form = new AlphabetForm(_sourceLanguage);
            form.ShowDialog(this);
        };
        AppIcons.Apply(alphabetButton, "WordStudy", 16);
        _wordPanel = new TextBox
        {
            Left = 226, Top = 276, Width = 488, Height = 366,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = "Click a word on the left."
        };

        var myLabel = new Label
        {
            Text = "Your translation of this passage:", Left = 730, Top = 40, Width = 400,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        // Which translation to check against, when the work has more than
        // one. Pre-selected by citation-ref overlap with the source edition,
        // which is inference rather than recorded fact - hence a picker
        // rather than a silent choice.
        var comparisonLabel = new Label
        {
            Text = "Check against:", Left = 730, Top = 656, Width = 90,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _comparisonBox = new ComboBox
        {
            Left = 822, Top = 652, Width = 328, Height = 24,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _comparisonBox.Items.Add(comparisons.Count == 0 ? "(no other translation loaded)" : "(none)");
        foreach (var (_, label) in comparisons) _comparisonBox.Items.Add(label);
        _comparisonBox.SelectedIndex = comparisons.Count > 0 ? 1 : 0;
        _comparisonBox.SelectedIndexChanged += async (_, _) => await ChangeComparisonAsync();
        _myTranslationBox = new TextBox
        {
            Left = 730, Top = 62, Width = 420, Height = 580,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Georgia", 11F)
        };
        _myTranslationBox.TextChanged += (_, _) => RefreshRevealButton();

        // Named for what they do rather than for being helpful. Everywhere
        // else in the app an AI request says so before it is made, and these
        // two send text over the internet like any other.
        _glossWordButton = new Button
        {
            Text = "AI Translate Word", Left = 624, Top = 688, Width = 155, Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false
        };
        _glossWordButton.Click += async (_, _) => await GlossWordAsync();
        AppIcons.Apply(_glossWordButton, "Translate", 22);

        _suggestButton = new Button
        {
            Text = "AI Translate Passage", Left = 787, Top = 688, Width = 175, Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _suggestButton.Click += async (_, _) => await SuggestAsync();
        AppIcons.Apply(_suggestButton, "Translate", 22);

        _revealButton = new Button
        {
            Text = "Compare published", Left = 970, Top = 688, Width = 180, Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false
        };
        _revealButton.Click += (_, _) => RevealPublished();

        // The same icon Compare Translations uses in the main toolbar - it
        // is the same act, against one passage instead of a whole work.
        AppIcons.Apply(_revealButton, "CompareTexts", 22);

        _previousButton = new Button
        {
            Text = "Previous", Left = 14, Top = 688, Width = 110, Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _previousButton.Click += async (_, _) => await MoveAsync(-1);
        AppIcons.Apply(_previousButton, "Back", 16);

        _nextButton = new Button
        {
            Text = "Save and Next \u25B6", Left = 132, Top = 688, Width = 150, Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _nextButton.Click += async (_, _) => await MoveAsync(1);
        AppIcons.Apply(_nextButton, "Save", 16);

        var closeButton = new Button
        {
            Text = "Save and Close", Left = 290, Top = 688, Width = 150, Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        closeButton.Click += async (_, _) =>
        {
            await SaveCurrentAsync();
            Close();
        };

        AppIcons.Apply(closeButton, "Save", 16);

        _statusLabel = new Label
        {
            Left = 14, Top = 656, Width = 700, Height = 22,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };

        Controls.AddRange(new Control[]
        {
            _headerLabel, gotoLabel, _gotoBox, _progressLabel, sourceLabel, _sourceBox, wordsLabel, _sourceWords,
            panelLabel, alphabetButton, _wordPanel, myLabel, _myTranslationBox,
            comparisonLabel, _comparisonBox, _glossWordButton, _suggestButton, _revealButton,
            _previousButton, _nextButton, closeButton, _statusLabel
        });

        // Opens at the first passage with nothing written yet, which for
        // anyone returning to a long work is where they stopped rather than
        // where they started.
        _index = FirstUntranslatedIndex();
        ShowPassage();

        Load += async (_, _) => await ChangeComparisonAsync();

        ReadingTheme.AttachTo(this);
    }

    private async Task ChangeComparisonAsync()
    {
        var choice = _comparisonBox.SelectedIndex - 1;

        _publishedAligner = choice >= 0 && choice < _comparisons.Count
            ? await _loadAligner(_comparisons[choice].Edition.EditionId)
            : null;

        _revealed = false;
        RefreshRevealButton();
    }

    private int FirstUntranslatedIndex()
    {
        for (var i = 0; i < _sourcePassages.Count; i++)
        {
            if (!_myTranslations.ContainsKey(_sourcePassages[i].CitationRef)) return i;
        }

        return 0;
    }

    private void ShowPassage()
    {
        if (_sourcePassages.Count == 0)
        {
            _headerLabel.Text = "This work has no original-language text loaded.";
            return;
        }

        _index = Math.Clamp(_index, 0, _sourcePassages.Count - 1);
        var passage = _sourcePassages[_index];

        _headerLabel.Text = $"{_authorName}, {_work.Title}  [{passage.CitationRef}]";
        RenderPassageWithContext();

        _sourceWords.BeginUpdate();
        try
        {
            _sourceWords.Items.Clear();
            foreach (var word in SplitWords(passage.Text)) _sourceWords.Items.Add(word);
        }
        finally
        {
            _sourceWords.EndUpdate();
        }

        _myTranslationBox.Text = _myTranslations.TryGetValue(passage.CitationRef, out var mine) ? mine : string.Empty;
        _wordPanel.Text = "Click a word on the left.";
        _glossWordButton.Enabled = false;

        _revealed = false;
        RefreshRevealButton();

        var done = _myTranslations.Count;
        _progressLabel.Text =
            $"Passage {_index + 1:N0} of {_sourcePassages.Count:N0}   |   {done:N0} translated";

        _navigating = true;
        try
        {
            if (_index < _gotoBox.Items.Count) _gotoBox.SelectedIndex = _index;
        }
        finally
        {
            _navigating = false;
        }

        _previousButton.Enabled = _index > 0;
        _nextButton.Text = _index < _sourcePassages.Count - 1 ? "Save and Next \u25B6" : "Save";
    }

    /// <summary>
    /// Draws the passage with the one before and after it dimmed around it.
    ///
    /// The neighbours are shown but not clickable and not translated - they
    /// are there to finish a sentence that the citation scheme cut in half,
    /// which is the ordinary case in verse rather than a rare one.
    /// </summary>
    private void RenderPassageWithContext()
    {
        _sourceBox.Clear();

        var dim = ReadingTheme.IsDark ? Color.FromArgb(120, 120, 128) : Color.FromArgb(150, 145, 135);
        var normal = ReadingTheme.Text;

        if (_index > 0) AppendPassage(_sourcePassages[_index - 1], dim, 11F);

        AppendPassage(_sourcePassages[_index], normal, 13F);

        if (_index < _sourcePassages.Count - 1) AppendPassage(_sourcePassages[_index + 1], dim, 11F);

        // Scrolled so the passage being worked on is what you see, not
        // whatever preceded it.
        var offset = _index > 0 ? _sourcePassages[_index - 1].Text.Length + 2 : 0;
        _sourceBox.SelectionStart = offset;
        _sourceBox.ScrollToCaret();
        _sourceBox.SelectionLength = 0;
    }

    private void AppendPassage(TextNode passage, Color colour, float size)
    {
        _sourceBox.SelectionStart = _sourceBox.TextLength;
        _sourceBox.SelectionLength = 0;
        _sourceBox.SelectionColor = colour;
        _sourceBox.SelectionFont = new Font("Palatino Linotype", size);
        _sourceBox.AppendText(passage.Text + "\r\n\r\n");
    }

    /// <summary>
    /// The part every citation reference in this work shares, so it can be
    /// left out of the picker.
    ///
    /// Backed off to the last separator rather than used raw: the longest
    /// common prefix of "1.10" and "1.11" is "1.1", and trimming that would
    /// leave "0" and "1". Cutting at the separator leaves "10" and "11".
    ///
    /// Returns empty when trimming would leave any entry with nothing at
    /// all, which is the case for a work whose references are already short.
    /// </summary>
    private static string CommonCitationPrefix(List<TextNode> passages)
    {
        if (passages.Count < 2) return string.Empty;

        var prefix = passages[0].CitationRef;

        foreach (var passage in passages)
        {
            var i = 0;
            while (i < prefix.Length && i < passage.CitationRef.Length
                   && prefix[i] == passage.CitationRef[i]) i++;

            prefix = prefix[..i];
            if (prefix.Length == 0) return string.Empty;
        }

        var cut = Math.Max(prefix.LastIndexOf('.'), prefix.LastIndexOf(':'));
        if (cut < 0) return string.Empty;

        prefix = prefix[..(cut + 1)];

        return passages.Any(p => p.CitationRef.Length <= prefix.Length) ? string.Empty : prefix;
    }

    private string ShortCitation(string citationRef) =>
        _citationPrefix.Length > 0 && citationRef.StartsWith(_citationPrefix, StringComparison.Ordinal)
            ? citationRef[_citationPrefix.Length..]
            : citationRef;

    /// <summary>
    /// How a passage reads in the Go to list - its citation reference with
    /// the shared prefix removed, a tick once it has been translated, and
    /// enough of the text to recognise it by.
    /// </summary>
    private string DescribePassage(TextNode passage, IReadOnlyDictionary<string, string> done)
    {
        var mark = done.ContainsKey(passage.CitationRef) ? "\u2713" : "\u00b7";
        var preview = passage.Text.Length > 60 ? passage.Text[..60] + "\u2026" : passage.Text;

        return $"{mark}  {ShortCitation(passage.CitationRef),-12}  {preview}";
    }

    private async Task JumpToSelectedAsync()
    {
        if (_navigating) return;

        var target = _gotoBox.SelectedIndex;
        if (target < 0 || target >= _sourcePassages.Count || target == _index) return;

        await SaveCurrentAsync();
        _index = target;
        _statusLabel.Text = string.Empty;
        ShowPassage();
    }

    /// <summary>
    /// Every word of the passage, in the order it appears, with punctuation
    /// stripped but the original accents kept - accents are how a form is
    /// recognised, and stripping them here would show a word nobody wrote.
    ///
    /// Deliberately not deduplicated, and deliberately not filtered by
    /// length. This list is how someone walks a sentence, so it has to
    /// correspond to the sentence: collapsing a repeated word breaks the
    /// correspondence, and the one-letter words it used to drop are among
    /// the most grammatically loaded things in Greek - the article and the
    /// relative pronoun are single letters, and in a line like "to de peri
    /// HO dia ton phusikon" the whole clause turns on the one that was being
    /// discarded.
    ///
    /// The cost is a long list for a long passage, which is honest: the
    /// passage really does have that many words in it.
    /// </summary>
    private static IEnumerable<string> SplitWords(string text)
    {
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Elision leaves an apostrophe that isn't part of the word -
            // stripping non-letters turns "d'" into "d", which is the form
            // the lemma data actually holds.
            var word = new string(raw.Where(char.IsLetter).ToArray());
            if (word.Length > 0) yield return word;
        }
    }

    private async Task ShowWordAsync()
    {
        if (_sourceWords.SelectedItem is not string word)
        {
            _glossWordButton.Enabled = false;
            return;
        }

        _glossWordButton.Enabled = true;
        _wordPanel.Text = "Looking up...";

        try
        {
            var headwords = await _lemmaRepo.GetHeadwordsForFormAsync(word, _sourceLanguage);

            if (headwords.Count == 0)
            {
                _wordPanel.Text =
                    $"{word}\r\n\r\nNo headword found for this form. That usually means the lemma " +
                    "data for this language isn't loaded (Setup Wizard), or the form is one the " +
                    "lemma data doesn't cover - proper names especially.";
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine(word);
            report.AppendLine();

            foreach (var (headword, tag) in headwords)
            {
                report.AppendLine($"  {headword}");

                var parse = MorphologyDecoder.Decode(tag);
                if (parse.IsDecoded && parse.Description.Length > 0)
                {
                    report.AppendLine($"     {parse.Description}");
                }

                if (!string.IsNullOrWhiteSpace(_sourceLanguage))
                {
                    var entries = await _definitionRepo.GetByHeadwordAsync(headword, _sourceLanguage);
                    foreach (var (_, entry, source) in entries.Take(2))
                    {
                        report.AppendLine();
                        report.AppendLine($"     {entry}");
                        if (!string.IsNullOrWhiteSpace(source)) report.AppendLine($"     - {source}");
                    }
                }

                report.AppendLine();
            }

            _wordPanel.Text = report.ToString();
            _wordPanel.SelectionStart = 0;
            _wordPanel.SelectionLength = 0;
        }
        catch (Exception ex)
        {
            _wordPanel.Text = $"Couldn't look that up: {ex.Message}";
        }
    }

    /// <summary>
    /// The published translation stays out of reach until something has been
    /// written.
    ///
    /// Not paternalism for its own sake - reading someone else's rendering
    /// first is the one action that quietly removes the point of doing this,
    /// and the gate costs nothing to anyone using this purely to author,
    /// since they have typed by the time they would want to compare.
    /// </summary>
    private void RefreshRevealButton()
    {
        var hasAttempt = _myTranslationBox.Text.Trim().Length > 0;

        _revealButton.Enabled = _publishedAligner != null && hasAttempt && !_revealed;

        // The disabled state has two quite different causes and it is worth
        // saying which: nothing to compare against, versus deliberately
        // withheld until something has been attempted.
        _revealButton.Text = _publishedAligner == null
            ? "Nothing to compare"
            : _revealed ? "Comparing"
            : hasAttempt ? "Compare published"
            : "Compare (write first)";
    }

    private void RevealPublished()
    {
        if (_publishedAligner == null || _sourcePassages.Count == 0) return;

        var published = _publishedAligner.ResolveText(_sourcePassages[_index].CitationRef);

        if (string.IsNullOrWhiteSpace(published))
        {
            _statusLabel.Text = "The published translation has nothing at this citation.";
            return;
        }

        _revealed = true;
        RefreshRevealButton();

        _wordPanel.Text = $"Published translation of [{_sourcePassages[_index].CitationRef}]\r\n\r\n{published}";
    }

    /// <summary>
    /// Asks what the selected word means in this line.
    ///
    /// Deliberately narrower than the dictionary panel beside it rather than
    /// a replacement for it: the lexicon entry is sourced and complete,
    /// while this answers the one thing it can't - which sense is in play
    /// here, and what the word is doing in this sentence.
    /// </summary>
    private async Task GlossWordAsync()
    {
        if (_sourceWords.SelectedItem is not string word || _sourcePassages.Count == 0) return;

        var geminiKey = TranslationSettings.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            MessageBox.Show(this,
                "No Gemini key is configured. Setup Wizard, then AI Translation.",
                "AI Translate Word", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var passage = _sourcePassages[_index];
        _glossWordButton.Enabled = false;
        _statusLabel.Text = $"Asking about {word}...";

        try
        {
            var gloss = await GeminiTranslationService.GlossWordAsync(
                word, passage.Text, _sourceLanguage, "eng", _authorName, _work.Title,
                passage.CitationRef, geminiKey);

            _wordPanel.Text = $"{word} - as used here\r\n\r\n{gloss}\r\n\r\n" +
                              "(AI-generated, unlike the dictionary entry - worth checking against it.)";
            _statusLabel.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't ask about that word: {ex.Message}";
        }
        finally
        {
            _glossWordButton.Enabled = _sourceWords.SelectedItem is string;
        }
    }

    private async Task SuggestAsync()
    {
        if (_sourcePassages.Count == 0) return;

        var geminiKey = TranslationSettings.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            MessageBox.Show(this,
                "No Gemini key is configured. Setup Wizard, then AI Translation.",
                "AI Translate Passage", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var passage = _sourcePassages[_index];
        _suggestButton.Enabled = false;
        _statusLabel.Text = "AI translating this passage...";

        try
        {
            var suggestion = await GeminiTranslationService.TranslateAsync(
                passage.Text, _sourceLanguage, "eng", _authorName, _work.Title,
                passage.CitationRef, geminiKey);

            // Into the reference panel, never into the box. A suggestion
            // that arrives already typed stops being something to weigh
            // against and becomes the answer.
            _wordPanel.Text =
                $"AI translation of [{passage.CitationRef}]\r\n\r\n{suggestion}\r\n\r\n" +
                "(AI-generated - a rendering to weigh against yours, not a correct answer.)";
            _statusLabel.Text = "AI translation shown on the left - yours stays as you wrote it.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't get an AI translation: {ex.Message}";
        }
        finally
        {
            _suggestButton.Enabled = true;
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (_sourcePassages.Count == 0) return;

        var passage = _sourcePassages[_index];
        var text = _myTranslationBox.Text.Trim();

        try
        {
            await _textNodeRepo.SaveTranslatedLineAsync(
                _translationEditionId, passage.CitationRef, _index, text);

            if (text.Length == 0) _myTranslations.Remove(passage.CitationRef);
            else _myTranslations[passage.CitationRef] = text;

            // Only this passage's entry is rewritten - rebuilding the whole
            // list on every save would mean a thousand-odd strings each time
            // Next is pressed.
            if (_index < _gotoBox.Items.Count)
            {
                _navigating = true;
                try
                {
                    _gotoBox.Items[_index] = DescribePassage(passage, _myTranslations);
                    _gotoBox.SelectedIndex = _index;
                }
                finally
                {
                    _navigating = false;
                }
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't save this passage: {ex.Message}";
        }
    }

    private async Task MoveAsync(int delta)
    {
        await SaveCurrentAsync();

        var next = _index + delta;
        if (next < 0 || next >= _sourcePassages.Count)
        {
            _statusLabel.Text = delta > 0 ? "That was the last passage." : "Already at the first passage.";
            ShowPassage();
            return;
        }

        _index = next;
        _statusLabel.Text = string.Empty;
        ShowPassage();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        await SaveCurrentAsync();
        base.OnFormClosing(e);
    }
}
