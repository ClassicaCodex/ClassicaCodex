using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Platform;
using MorphologyHit = (int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string MatchedForm, string Headword, string Tag, string? Milestone);
using ClassicaCodex.Mac.Features.Export;

namespace ClassicaCodex.Mac.Features.Morphology;

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
/// vocabularies. The window says so directly rather than silently returning
/// nothing for Latin.
///
/// The Mac counterpart of MorphologyForm. The one thing added is that the
/// matched form is set off in each passage, which is how the Windows comment
/// on the row format says a morphology search is read.
/// </summary>
public sealed class MorphologyWindow : FeatureWindow
{
    /// <summary>
    /// How many passages one search will show. Passed to the query rather than
    /// left to its default so that the status line, which tells the reader
    /// whether they are looking at all the matches or the first of them, is
    /// reading the same number the query capped at.
    /// </summary>
    private const int MaxSearchResults = 2000;

    private readonly LemmaRepository _lemmaRepo = new();

    private readonly ComboBox _languageCombo;
    private readonly List<(string Label, int Position, ComboBox Combo)> _categoryCombos = new();
    private readonly Button _searchButton;
    private readonly TextBlock _patternLabel;
    private readonly TextBlock _statusLabel;
    private readonly TextBlock _scopeLabel;
    private readonly TextBlock _countLabel;
    private readonly ListBox _resultsList;
    private readonly TextBlock _resultsMessage;
    private readonly FontFamily _readingFont = ToolPane.Font("Cc.SourceFont");

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

    private List<MorphologyHit> _currentResults = new();
    private bool _available;
    private bool _searching;
    private bool _closed;
    private Task _pending = Task.CompletedTask;

    public MorphologyWindow()
    {
        Title = "Morphology - find passages by grammatical form";
        Width = 1180;
        Height = 760;
        MinWidth = 860;
        MinHeight = 560;

        var heading = new TextBlock { Text = "Find passages by grammatical form", Classes = { "h2" } };
        var intro = new TextBlock
        {
            Text = "Pick any combination of grammatical features and find every passage using a form that matches. " +
                   "Leave a box on \"(any)\" to ignore that category.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 14)
        };

        _languageCombo = new ComboBox { ItemsSource = new[] { "Greek", "Latin" }, SelectedIndex = 0, MinWidth = 140 };
        _languageCombo.SelectionChanged += (_, _) => _pending = RefreshAvailabilityAsync();

        var categories = new WrapPanel { Orientation = Orientation.Horizontal };
        categories.Children.Add(Labelled("Language", _languageCombo));

        // The category boxes are built from MorphologyDecoder's own tables
        // rather than a second hardcoded list here, so a category can never be
        // searchable but undisplayable (or the reverse).
        foreach (var (label, position, options) in MorphologyDecoder.SearchableCategories)
        {
            var items = new List<MorphOption> { new(null, "(any)") };
            items.AddRange(options.Select(o => new MorphOption(o.Key, o.Value)));

            var combo = new ComboBox { ItemsSource = items, SelectedIndex = 0, MinWidth = 118, MaxDropDownHeight = 420 };
            combo.SelectionChanged += (_, _) => UpdatePatternLabel();

            categories.Children.Add(Labelled(label, combo));
            _categoryCombos.Add((label, position, combo));
        }

        _searchButton = ToolPane.GlyphButton("Search", "Search");
        _searchButton.Classes.Add("accent");
        _searchButton.IsDefault = true;
        _searchButton.IsEnabled = false;
        _searchButton.Click += (_, _) => _pending = RunSearchAsync();

        var clearButton = new Button { Content = "Clear" };
        clearButton.Click += (_, _) =>
        {
            foreach (var (_, _, combo) in _categoryCombos) combo.SelectedIndex = 0;
            UpdatePatternLabel();
        };

        var scopeButton = ToolPane.GlyphButton("Choose Texts…", "Filter");
        scopeButton.Click += async (_, _) => await ChooseScopeAsync();

        _scopeLabel = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

        _patternLabel = new TextBlock { Classes = { "mono", "faint" }, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(_patternLabel, "The tag patterns searched for: ? is any value. The corpus writes the same analysis in a nine- and a ten-character layout, so both are matched.");

        var actions = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _searchButton, clearButton, scopeButton, _scopeLabel } };
        DockPanel.SetDock(left, Dock.Left);
        actions.Children.Add(left);
        _patternLabel.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Children.Add(_patternLabel);

        var criteria = new StackPanel { Children = { categories, actions } };
        var criteriaCard = new Border { Classes = { "card" }, Padding = new Thickness(16, 12, 16, 14), Child = criteria };

        _statusLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 12, 2, 10) };

        _resultsList = new ListBox
        {
            Padding = new Thickness(4, 6),
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<ResultRow?>((row, _) => row == null ? null : BuildResultRow(row), supportsRecycling: false)
        };
        _resultsList.DoubleTapped += async (_, _) => await JumpToSelectedResultAsync();
        _resultsList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedResultAsync();
            }
            else if (e.Key == Key.C && e.KeyModifiers.HasFlag(WordStudyWindow.CommandKey))
            {
                e.Handled = true;
                await CopySelectedAsync();
            }
        };
        AttachResultsMenu();
        _resultsMessage = ToolPane.Message();

        _countLabel = new TextBlock { Classes = { "caption" }, Margin = new Thickness(0, 0, 6, 0) };
        var resultsPane = ToolPane.Build(
            "PASSAGES · DOUBLE-CLICK TO JUMP",
            ToolPane.Overlay(_resultsList, _resultsMessage),
            _countLabel);

        var top = new StackPanel { Children = { heading, intro, criteriaCard, _statusLabel } };

        var layout = new DockPanel { Margin = new Thickness(20, 18, 20, 20) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(resultsPane);
        Content = layout;

        UpdatePatternLabel();
        RefreshScopeLabel();

        Closed += (_, _) => _closed = true;
        Opened += (_, _) => _pending = RefreshAvailabilityAsync();
    }

    // For the tests, which drive the window as a reader would.
    internal IReadOnlyList<(string Label, int Position, ComboBox Combo)> CategoryCombos => _categoryCombos;
    internal ComboBox LanguageCombo => _languageCombo;
    internal IReadOnlyList<MorphologyHit> Results => _currentResults;
    internal string StatusText => _statusLabel.Text ?? string.Empty;
    internal string PatternText => _patternLabel.Text ?? string.Empty;
    internal bool CanSearch => _searchButton.IsEnabled;

    internal Task SearchAsync()
    {
        _pending = RunSearchAsync();
        return _pending;
    }

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

    /// <summary>Selects the option with this code in the named category - "Mood", 'o'.</summary>
    internal void Choose(string category, char code)
    {
        var combo = _categoryCombos.First(c => c.Label == category).Combo;
        combo.SelectedItem = ((IEnumerable<MorphOption>)combo.ItemsSource!).First(o => o.Code == code);
    }

    private static Control Labelled(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 8) };
        panel.Children.Add(new TextBlock { Text = label, Classes = { "caption" } });
        panel.Children.Add(control);
        return panel;
    }

    /// <summary>One entry in a category box - a null Code means "(any)", i.e. don't constrain this position.</summary>
    internal sealed record MorphOption(char? Code, string Meaning)
    {
        public override string ToString() => Meaning;
    }

    private string SelectedLanguageCode => _languageCombo.SelectedIndex == 1 ? "lat" : "grc";

    private string SelectedLanguageName => _languageCombo.SelectedIndex == 1 ? "Latin" : "Greek";

    private Dictionary<int, char> CurrentSelections()
    {
        var selections = new Dictionary<int, char>();
        foreach (var (_, position, combo) in _categoryCombos)
        {
            if (combo.SelectedItem is MorphOption { Code: { } code }) selections[position] = code;
        }

        return selections;
    }

    private void UpdatePatternLabel()
    {
        var (pattern9, pattern10) = MorphologyDecoder.BuildGlobPatterns(CurrentSelections());
        _patternLabel.Text = $"Tag patterns: {pattern9}  /  {pattern10}";
    }

    private void SetSearchEnabled() => _searchButton.IsEnabled = _available && !_searching;

    /// <summary>
    /// Says up front whether the selected language actually has tagged data
    /// loaded. Without this, searching Latin would just return nothing and
    /// look like a broken feature rather than a missing-data situation -
    /// and the two need very different responses from the person.
    /// </summary>
    private async Task RefreshAvailabilityAsync()
    {
        var language = SelectedLanguageCode;
        var languageName = SelectedLanguageName;

        try
        {
            // Off the UI thread. Counting the distinct tagged forms means a
            // pass over every lemma in the language, 585,225 of them for
            // Latin, and it runs on opening the window and again on every
            // language change.
            var count = await Task.Run(() => _lemmaRepo.CountTaggedFormsAsync(language));
            if (_closed || language != SelectedLanguageCode) return;

            if (count == 0)
            {
                _available = false;
                SetSearchEnabled();
                SetStatus(
                    $"No morphologically tagged forms loaded for {languageName}. " +
                    "Run the matching Lemma Data step in Setup, then try again.", warning: true);
                return;
            }

            _available = true;
            SetSearchEnabled();

            var status = $"{count:N0} tagged forms available for {languageName}.";

            // Latin's tags come from a different corpus using a coarser,
            // non-positional vocabulary, so the positional search below
            // mostly won't match them. Better to say that plainly than to
            // let it look like the search is simply broken.
            if (language == "lat")
            {
                status += "  Note: Latin tags use a different, coarser format than the positional Greek tags " +
                          "this search is built around, so most combinations will find nothing.";
            }

            SetStatus(status);
        }
        catch (Exception ex)
        {
            if (_closed) return;
            _available = false;
            SetSearchEnabled();
            SetStatus($"Couldn't check lemma data: {ex.Message}", warning: true);
        }
    }

    private void SetStatus(string text, bool warning = false)
    {
        _statusLabel.Text = text;
        _statusLabel.Classes.Set("warning", warning);
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
            var chosen = await WorkPickerWindow.ChooseAsync(this, _scopeWorkIds.ToList());
            if (chosen == null) return;

            await ApplyScopeAsync(chosen);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't load the text list: {ex.Message}", warning: true);
        }
    }

    internal async Task ApplyScopeAsync(IReadOnlyCollection<int> workIds)
    {
        _scopeWorkIds.Clear();
        foreach (var id in workIds) _scopeWorkIds.Add(id);

        RefreshScopeLabel();

        if (_currentResults.Count > 0) await SearchAsync();
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
            await Alert.Info(this, "Nothing selected",
                "Pick at least one grammatical feature - searching with everything on \"(any)\" would " +
                "match every tagged word in the corpus.");
            return;
        }

        _currentResults = new List<MorphologyHit>();
        _resultsList.ItemsSource = null;
        _countLabel.Text = string.Empty;
        ShowMessage(null);
        SetStatus("Searching...");
        _searching = true;
        SetSearchEnabled();

        try
        {
            var (pattern9, pattern10) = MorphologyDecoder.BuildGlobPatterns(selections);
            var language = SelectedLanguageCode;
            var scope = _scopeWorkIds.ToList();

            // Off the UI thread, and more so than the availability count.
            // Microsoft.Data.Sqlite's async methods run synchronously, so
            // awaiting this on the UI thread would block for the query's whole
            // duration: "Searching..." would never paint, and a broad
            // selection would leave the window spinning.
            var results = await Task.Run(() => _lemmaRepo.SearchByMorphologyAsync(
                pattern9, pattern10, language, MaxSearchResults, workIds: scope));

            // A reader who gives up and closes the window mid-search should not
            // have this continuation write into a window that is gone.
            if (_closed) return;

            _currentResults = results;
            _resultsList.ItemsSource = results.Select(r => new ResultRow(r)).ToList();
            _countLabel.Text = results.Count == 0 ? string.Empty : $"{results.Count:N0}";

            if (results.Count == 0)
            {
                ShowMessage("(nothing matched this combination)");
                SetStatus("No matches - try loosening a category back to \"(any)\".");
            }
            else if (results.Count >= MaxSearchResults)
            {
                // Said plainly because it is not the whole answer. A broad
                // selection matches more passages than any list should hold -
                // "every Greek verb" matches over a hundred thousand distinct
                // forms - and reporting the cap as though it were the count
                // would invite reading conclusions off a slice.
                SetStatus(
                    $"First {results.Count:N0} passages found - there are more. " +
                    "Narrow a category, or set a text scope, to see all of them. Double-click one to jump to it.");
            }
            else
            {
                SetStatus($"{results.Count:N0} passage(s). Double-click one to jump to it.");
            }
        }
        catch (Exception ex)
        {
            if (!_closed) SetStatus($"Search failed: {ex.Message}", warning: true);
        }
        finally
        {
            _searching = false;
            if (!_closed) SetSearchEnabled();
        }
    }

    private void ShowMessage(string? message)
    {
        _resultsMessage.Text = message;
        _resultsMessage.IsVisible = message != null;
    }

    /// <summary>
    /// One passage: where it is from, the form and its headword, then the
    /// passage with that form set off.
    ///
    /// No citation reference on the row. It is the least useful thing here
    /// and the widest - a morphology search is read by scanning the matched
    /// form and the text either side of it, and "[12.4.1]" between the title
    /// and the form breaks that scan on every row. It is on the tooltip, in
    /// the copy and in the export, which is where it is wanted: at the point
    /// of citing one, not while reading two thousand.
    /// </summary>
    private Control BuildResultRow(ResultRow row)
    {
        var r = row.Hit;
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 3), LineHeight = 20 };
        text.Inlines!.Add(PassageText.Ink(text, $"{r.AuthorName}, {r.WorkTitle}  ", "Cc.InkMuted", FontFamily.Default, 12));
        text.Inlines.Add(PassageText.Ink(text, $"({r.MatchedForm} < {r.Headword}):  ", "Cc.Accent", _readingFont, 13.5));

        var matched = WordNormalizer.Normalize(r.MatchedForm);
        var font = ScriptFonts.Compose(_readingFont, ScriptFonts.FamilyFor(r.Text));
        PassageText.AddHighlighted(text, PassageText.RowText(r.Text), w => w == matched, font, 14.5);

        ToolTip.SetTip(text, $"{PassageCitation.Bracketed(r.CitationRef, r.Milestone)}   {r.Tag}");
        ToolTip.SetShowDelay(text, 700);
        return text;
    }

    private void AttachResultsMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copy Passage", Icon = Glyphs.Icon("Copy", 14), InputGesture = new KeyGesture(Key.C, WordStudyWindow.CommandKey) };
        copy.Click += async (_, _) => await CopySelectedAsync();
        menu.Items.Add(copy);

        var exportAll = new MenuItem { Header = "Export All Passages…", Icon = Glyphs.Icon("Export", 14) };
        exportAll.Click += (_, _) => ExportFeature.ExportPassagesFrom(this, "Morphology search results",
            _currentResults.Select(r => new ExportPassage(r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text, Milestone: r.Milestone)).ToList());
        menu.Items.Add(exportAll);

        menu.Opening += (_, _) =>
        {
            copy.IsEnabled = _resultsList.SelectedItem is ResultRow;
            exportAll.IsEnabled = _currentResults.Count > 0;
        };
        _resultsList.ContextMenu = menu;

        TableExport.Attach(
            _resultsList,
            () => "Morphology search results",
            () => ExportRows(_currentResults),
            () => ExportRows(_resultsList.SelectedItem is ResultRow row ? new[] { row.Hit } : Array.Empty<MorphologyHit>()),
            ExportNotes);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ExportRows(IEnumerable<MorphologyHit> hits)
    {
        var table = new List<IReadOnlyList<string>> { new[] { "Author", "Work", "Citation", "Form", "Headword", "Tag", "Passage" } };
        table.AddRange(hits.Select(r => (IReadOnlyList<string>)new[]
        {
            r.AuthorName, r.WorkTitle, PassageCitation.Display(r.CitationRef, r.Milestone), r.MatchedForm, r.Headword, r.Tag, r.Text
        }));
        return table;
    }

    private IReadOnlyList<string> ExportNotes()
    {
        var notes = new List<string>
        {
            $"Classica Codex morphology search - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"{SelectedLanguageName}: {string.Join(", ", _categoryCombos.Where(c => c.Combo.SelectedItem is MorphOption { Code: not null }).Select(c => $"{c.Label.ToLowerInvariant()} {c.Combo.SelectedItem}"))}",
            $"{PatternText} - {(_scopeLabel.Text ?? string.Empty).ToLowerInvariant()}."
        };

        if (_currentResults.Count >= MaxSearchResults)
            notes.Add($"The search stopped at its first {MaxSearchResults:N0} passages; there are more.");

        return notes;
    }

    private static string FullLine(MorphologyHit r) =>
        $"{r.AuthorName}, {r.WorkTitle} [{PassageCitation.Display(r.CitationRef, r.Milestone)}]: {r.Text}";

    private async Task CopySelectedAsync()
    {
        if (_resultsList.SelectedItem is ResultRow row) await ClipboardText.SetAsync(this, FullLine(row.Hit));
    }

    internal async Task JumpToResultAsync(int index)
    {
        _resultsList.SelectedIndex = index;
        await JumpToSelectedResultAsync();
    }

    private async Task JumpToSelectedResultAsync()
    {
        if (_resultsList.SelectedItem is not ResultRow row || OnNavigate == null) return;

        await NavigateAsync(row.Hit.WorkId, row.Hit.TextNodeId);
        Close();
    }

    private sealed record ResultRow(MorphologyHit Hit);
}
