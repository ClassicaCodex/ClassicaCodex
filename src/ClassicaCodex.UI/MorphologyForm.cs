using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Searches the corpus by grammatical form rather than by word - "every
/// aorist optative", "every genitive absolute", "every superlative
/// adjective". This is the thing lemma data makes possible that a plain
/// text search never can, and it's the natural companion to Word Study:
/// that answers "what is this word doing?", this answers "where else does
/// the language do this?".
///
/// Only Greek carries the positional tags this searches on - see the note
/// in MorphologyDecoder about the two corpora using different tag
/// vocabularies. The form says so directly rather than silently returning
/// nothing for Latin.
/// </summary>
public class MorphologyForm : Form
{
    private readonly ComboBox _languageComboBox;
    private readonly List<(string Label, int Position, ComboBox Combo)> _categoryCombos = new();
    private readonly Button _searchButton;
    private readonly Button _clearButton;
    private readonly Label _patternLabel;
    private readonly Label _statusLabel;
    private readonly ListBox _resultsList;
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();

    private readonly Button _scopeButton;
    private readonly Label _scopeLabel;

    /// <summary>
    /// Which works the search covers. Empty means every text, which is what
    /// the picker returns for "everything" and what the repository takes as
    /// no filter at all.
    ///
    /// This matters more here than in Word Study. A morphology pattern like
    /// "every aorist optative" matches tens of thousands of lines, the query
    /// stops at its result limit in author order, and what comes back is
    /// therefore the start of the alphabet rather than a sample of the
    /// corpus. Narrowing the scope is the only way to ask the question about
    /// a text you actually care about.
    /// </summary>
    private readonly HashSet<int> _scopeWorkIds = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string MatchedForm, string Headword, string Tag)> _currentResults = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public MorphologyForm()
    {
        Text = "Morphology - find passages by grammatical form";
        AppIcons.ApplyWindowIcon(this, "WordStudy");
        ClientSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterParent;

        var intro = new Label
        {
            Text = "Pick any combination of grammatical features and find every passage using a form that matches. " +
                   "Leave a box on \"(any)\" to ignore that category.",
            Left = 12,
            Top = 10,
            Width = 1140,
            Height = 32
        };

        var languageLabel = new Label { Text = "Language:", Left = 12, Top = 52, Width = 70 };
        _languageComboBox = new ComboBox
        {
            Left = 86,
            Top = 48,
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _languageComboBox.Items.AddRange(new object[] { "Greek", "Latin" });
        _languageComboBox.SelectedIndex = 0;
        _languageComboBox.SelectedIndexChanged += async (_, _) => await RefreshAvailabilityAsync();

        // The category dropdowns are built from MorphologyDecoder's own
        // tables rather than a second hardcoded list here, so a category can
        // never be searchable but undisplayable (or the reverse).
        var left = 12;
        var top = 84;
        foreach (var (label, position, options) in MorphologyDecoder.SearchableCategories)
        {
            var categoryLabel = new Label { Text = label, Left = left, Top = top, Width = 110 };
            var combo = new ComboBox
            {
                Left = left,
                Top = top + 20,
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            combo.Items.Add(new MorphOption(null, "(any)"));
            foreach (var (code, meaning) in options)
            {
                combo.Items.Add(new MorphOption(code, meaning));
            }
            combo.SelectedIndex = 0;
            combo.SelectedIndexChanged += (_, _) => UpdatePatternLabel();

            Controls.Add(categoryLabel);
            Controls.Add(combo);
            _categoryCombos.Add((label, position, combo));

            left += 120;
        }

        _searchButton = new Button { Text = "Search", Left = 12, Top = 140, Width = 110, Height = 28 };
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        _scopeButton = new Button
        {
            Text = "Choose Texts...", Left = 232, Top = 140, Width = 140, Height = 28
        };
        _scopeButton.Click += async (_, _) => await ChooseScopeAsync();
        AppIcons.Apply(_scopeButton, "Filter", 16);

        _scopeLabel = new Label
        {
            Left = 382, Top = 146, Width = 220, Height = 20,
            ForeColor = Color.DimGray
        };

        _clearButton = new Button { Text = "Clear", Left = 132, Top = 140, Width = 90, Height = 28 };
        _clearButton.Click += (_, _) =>
        {
            foreach (var (_, _, combo) in _categoryCombos) combo.SelectedIndex = 0;
            UpdatePatternLabel();
        };

        _patternLabel = new Label
        {
            Left = 616,
            Top = 146,
            Width = 400,
            ForeColor = Color.DimGray
        };

        _statusLabel = new Label
        {
            Left = 12,
            Top = 176,
            Width = 1140,
            Height = 32
        };

        _resultsList = new ListBox
        {
            Left = 12,
            Top = 212,
            Width = 1156,
            Height = 496,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedResultAsync();
        ListResultHelpers.AttachCitationTooltip(_resultsList,
            i => i < _currentResults.Count ? _currentResults[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_resultsList,
            i => i < _currentResults.Count
                ? $"{_currentResults[i].AuthorName}, {_currentResults[i].WorkTitle} [{_currentResults[i].CitationRef}]: {_currentResults[i].Text}"
                : null);
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            "Morphology search results",
            _currentResults.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text)).ToList()), this);

        Controls.Add(intro);
        Controls.Add(languageLabel);
        Controls.Add(_languageComboBox);
        Controls.Add(_searchButton);
        Controls.Add(_clearButton);
        Controls.Add(_scopeButton);
        Controls.Add(_scopeLabel);
        Controls.Add(_patternLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_resultsList);

        UpdatePatternLabel();
        RefreshScopeLabel();

        Load += async (_, _) => await RefreshAvailabilityAsync();
        ReadingTheme.AttachTo(this);
    }

    /// <summary>One entry in a category dropdown - null Code means "(any)", i.e. don't constrain this position.</summary>
    private class MorphOption
    {
        public char? Code { get; }
        private string Meaning { get; }

        public MorphOption(char? code, string meaning)
        {
            Code = code;
            Meaning = meaning;
        }

        public override string ToString() => Meaning;
    }

    private string SelectedLanguageCode => _languageComboBox.SelectedIndex == 1 ? "lat" : "grc";

    private Dictionary<int, char> CurrentSelections()
    {
        var selections = new Dictionary<int, char>();
        foreach (var (_, position, combo) in _categoryCombos)
        {
            if (combo.SelectedItem is MorphOption { Code: { } code })
            {
                selections[position] = code;
            }
        }
        return selections;
    }

    private void UpdatePatternLabel()
    {
        var (pattern9, pattern10) = MorphologyDecoder.BuildGlobPatterns(CurrentSelections());
        _patternLabel.Text = $"Tag patterns: {pattern9}  /  {pattern10}";
    }

    /// <summary>
    /// Says up front whether the selected language actually has tagged data
    /// loaded. Without this, searching Latin would just return nothing and
    /// look like a broken feature rather than a missing-data situation -
    /// and the two need very different responses from the person.
    /// </summary>
    private async Task RefreshAvailabilityAsync()
    {
        try
        {
            var count = await _lemmaRepo.CountTaggedFormsAsync(SelectedLanguageCode);

            if (count == 0)
            {
                _statusLabel.Text =
                    $"No morphologically tagged forms loaded for {_languageComboBox.Text}. " +
                    "Run the matching Lemma Data step in Setup, then try again.";
                _searchButton.Enabled = false;
                return;
            }

            _searchButton.Enabled = true;
            _statusLabel.Text = $"{count:N0} tagged forms available for {_languageComboBox.Text}.";

            // Latin's tags come from a different corpus using a coarser,
            // non-positional vocabulary, so the positional search below
            // mostly won't match them. Better to say that plainly than to
            // let it look like the search is simply broken.
            if (SelectedLanguageCode == "lat")
            {
                _statusLabel.Text +=
                    "  Note: Latin tags use a different, coarser format than the positional Greek tags " +
                    "this search is built around, so most combinations will find nothing.";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't check lemma data: {ex.Message}";
            _searchButton.Enabled = false;
        }
    }

    /// <summary>
    /// Chooses which texts the search covers, then reruns it if a search has
    /// already been made - changing the scope with stale results on screen
    /// would leave the label and the list disagreeing.
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

            if (_currentResults.Count > 0) await RunSearchAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't load the text list: {ex.Message}";
        }
    }

    private void RefreshScopeLabel() =>
        _scopeLabel.Text = _scopeWorkIds.Count switch
        {
            0 => "Searching every text",
            1 => "Searching 1 text",
            _ => $"Searching {_scopeWorkIds.Count:N0} texts"
        };

    private async Task RunSearchAsync()
    {
        var selections = CurrentSelections();
        if (selections.Count == 0)
        {
            MessageBox.Show(this,
                "Pick at least one grammatical feature - searching with everything on \"(any)\" would " +
                "match every tagged word in the corpus.",
                "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _resultsList.Items.Clear();
        _currentResults = new List<(int, long, string, string, string, string, string, string, string)>();
        _statusLabel.Text = "Searching...";
        _searchButton.Enabled = false;

        try
        {
            var (pattern9, pattern10) = MorphologyDecoder.BuildGlobPatterns(selections);
            _currentResults = await _lemmaRepo.SearchByMorphologyAsync(
                pattern9, pattern10, SelectedLanguageCode, workIds: _scopeWorkIds.ToList());

            foreach (var r in _currentResults)
            {
                // No citation reference on the line. It is the least useful
                // thing here and the widest - a morphology search is read by
                // scanning the matched form and the text either side of it,
                // and "[12.4.1]" between the title and the form breaks that
                // scan on every row. Still on the hover tooltip, in the
                // right-click copy, and in the export, which is where it is
                // wanted: at the point of citing one, not while reading two
                // thousand.
                _resultsList.Items.Add($"{r.AuthorName}, {r.WorkTitle}  ({r.MatchedForm} < {r.Headword}): {r.Text}");
            }

            if (_currentResults.Count == 0)
            {
                _resultsList.Items.Add("(nothing matched this combination)");
                _statusLabel.Text = "No matches - try loosening a category back to \"(any)\".";
            }
            else
            {
                _statusLabel.Text = $"{_currentResults.Count:N0} passage(s). Double-click one to jump to it.";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            _searchButton.Enabled = true;
        }
    }

    private async Task JumpToSelectedResultAsync()
    {
        var index = _resultsList.SelectedIndex;
        if (index < 0 || index >= _currentResults.Count || OnNavigate == null) return;

        var result = _currentResults[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
        Close();
    }
}
