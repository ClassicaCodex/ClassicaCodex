using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Export;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// Finds every occurrence of a name across the corpus and tags them in one
/// go, instead of one line at a time. This is what makes the myth network
/// practical to build out - tag "Athena" once here and every occurrence in
/// the library becomes part of the graph.
///
/// Deliberately trust-the-preview rather than open-every-passage: a name
/// search on a proper noun has real failure modes (a name that is also an
/// ordinary word, translators rendering a god differently, an epithet the
/// search cannot catch at all), so the list is there to catch those by eye
/// before committing, with every match checked to start with and a
/// double-click to see one in full context.
///
/// The Windows form wanted to highlight the matched forms in each row and
/// could not - a CheckedListBox will not owner-draw - so it settled for
/// putting the match in view. Here the row does both.
/// </summary>
internal sealed class AutoTagWindow : FeatureWindow
{
    private readonly TextBox _nameBox;
    private readonly TextBox _categoryBox;
    private readonly TextBox _altSpellingsBox;
    private readonly Button _searchButton;
    private readonly ListBox _resultsList;
    private readonly TextBlock _resultsMessage;
    private readonly Button _tagAllButton;
    private readonly TextBlock _statusLabel;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly TagRepository _tagRepo = new();

    private List<Match> _currentResults = new();

    /// <summary>Raised after a successful tag-all, so the graph can refresh.</summary>
    public event Action? TagsChanged;

    public AutoTagWindow()
    {
        Title = "Auto-Tag";
        Width = 1100;
        Height = 720;
        MinWidth = 720;
        MinHeight = 460;
        PassageResults.AddListStyles(this);

        _nameBox = new TextBox { PlaceholderText = "Athena" };
        _categoryBox = new TextBox { Text = "god" };
        _altSpellingsBox = new TextBox { PlaceholderText = "Athene, Pallas, Minerva" };

        foreach (var box in new[] { _nameBox, _altSpellingsBox })
        {
            box.KeyDown += async (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                await RunSearchAsync();
            };
        }

        _searchButton = new Button { Content = TagBrowserWindow.Labelled("Search Corpus", "Search"), Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Bottom };
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,12,180,12,*,12,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto")
        };
        AddField(fields, 0, "Name (this becomes the tag):", _nameBox);
        AddField(fields, 2, "Category (optional):", _categoryBox);
        AddField(fields, 4, "Alternate spellings to also match (comma-separated - e.g. Athene, Pallas, Minerva):", _altSpellingsBox);
        Grid.SetColumn(_searchButton, 6);
        Grid.SetRow(_searchButton, 1);
        fields.Children.Add(_searchButton);

        var (resultsPage, resultsList, resultsMessage) = PassageResults.Page(item => item is Match m ? MatchRow(m) : new TextBlock());
        _resultsList = resultsList;
        _resultsMessage = resultsMessage;
        PassageResults.SetMessage(_resultsMessage, "Type a name and search the corpus.");

        _resultsList.DoubleTapped += async (_, args) =>
        {
            // A double-click on the check box is two clicks on the check box.
            if (args.Source is Visual v && v.FindAncestorOfType<CheckBox>(includeSelf: true) != null) return;
            await JumpToSelectedAsync();
        };
        _resultsList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Space && _resultsList.SelectedItem is Match m)
            {
                m.IsChecked = !m.IsChecked;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedAsync();
            }
        };

        PassageResults.Menu(_resultsList,
            PassageResults.CopyItem(_resultsList, item => (item as Match)?.Passage.CopyText),
            PassageResults.ExportAllItem(this, () => $"Auto-Tag matches for {(_nameBox.Text ?? string.Empty).Trim()}", () => _currentResults
                .Select(m => new ExportPassage(m.Passage.WorkId, m.Passage.TextNodeId, m.Passage.AuthorName, m.Passage.WorkTitle,
                    m.Passage.CitationRef, m.Passage.Text, Milestone: m.Passage.Milestone))
                .ToList()));

        var checkAll = new Button { Content = "Check All" };
        checkAll.Click += (_, _) => SetAllChecked(true);
        var uncheckAll = new Button { Content = "Uncheck All" };
        uncheckAll.Click += (_, _) => SetAllChecked(false);

        _tagAllButton = new Button { Content = TagBrowserWindow.Labelled("Tag All Checked", "Tags"), Classes = { "accent" }, MinWidth = 180, HorizontalContentAlignment = HorizontalAlignment.Center };
        _tagAllButton.Click += async (_, _) => await TagCheckedAsync();

        _statusLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 2, 0) };

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_tagAllButton, Dock.Right);
        buttons.Children.Add(_tagAllButton);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { checkAll, uncheckAll } });

        var resultsLabel = new TextBlock
        {
            Text = "Matches (checked ones get tagged - double-click to jump and verify):",
            Classes = { "muted" },
            Margin = new Thickness(2, 16, 0, 8)
        };

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(fields, Dock.Top);
        DockPanel.SetDock(resultsLabel, Dock.Top);
        DockPanel.SetDock(_statusLabel, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(fields);
        root.Children.Add(resultsLabel);
        root.Children.Add(_statusLabel);
        root.Children.Add(buttons);
        root.Children.Add(resultsPage);
        Content = root;

        Opened += (_, _) => _nameBox.Focus();
    }

    private static void AddField(Grid grid, int column, string label, TextBox box)
    {
        var caption = new TextBlock { Text = label, Classes = { "caption" }, Margin = new Thickness(1, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(caption, column);
        Grid.SetColumn(box, column);
        Grid.SetRow(box, 1);
        grid.Children.Add(caption);
        grid.Children.Add(box);
    }

    /// <summary>The matches listed; for tests.</summary>
    internal IReadOnlyList<Match> Matches => _currentResults;

    internal string Status => _statusLabel.Text ?? string.Empty;

    internal void SetQuery(string name, string? category, string alternates)
    {
        _nameBox.Text = name;
        _categoryBox.Text = category;
        _altSpellingsBox.Text = alternates;
    }

    /// <summary>
    /// Where each searched form appears in a line, as (start, length) pairs
    /// in reading order. Longer forms are listed first by the caller, so a
    /// longer form wins over a shorter one it contains.
    /// </summary>
    private static List<(int Start, int Length)> FindHighlightSpans(string text, List<string> forms)
    {
        var spans = new List<(int Start, int Length)>();
        if (forms.Count == 0) return spans;

        foreach (var form in forms)
        {
            if (form.Length == 0) continue;

            var index = 0;
            while ((index = text.IndexOf(form, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                spans.Add((index, form.Length));
                index += form.Length;
            }
        }

        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        return spans;
    }

    internal async Task RunSearchAsync()
    {
        var name = (_nameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            await Alert.Info(this, "Nothing to search", "Type a name first.");
            return;
        }

        var terms = new List<string> { name };
        terms.AddRange((_altSpellingsBox.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        _searchButton.IsEnabled = false;
        _statusLabel.Classes.Remove("warning");
        _statusLabel.Text = "Searching…";
        _currentResults = new List<Match>();
        _resultsList.ItemsSource = null;
        PassageResults.SetMessage(_resultsMessage, null);

        try
        {
            // Every typed term goes through the lemma data (inflected Greek
            // and Latin forms, where lemma coverage exists) and the forms are
            // merged into one list; a plain English name with no lemma match
            // just falls back to the term itself.
            //
            // The expansion and the search go to the thread pool together:
            // Microsoft.Data.Sqlite's async methods run synchronously, so
            // awaiting them here would hold the window for the whole round -
            // a lemma query per term, then a corpus-wide search for every
            // form they expanded to.
            var allForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hits = await Task.Run(async () =>
            {
                foreach (var term in terms)
                {
                    foreach (var form in await _lemmaRepo.ExpandFormAsync(term))
                        allForms.Add(form);
                }

                return await _textNodeRepo.SearchByFormsAsync(allForms.ToList());
            });

            // Kept so each row can show exactly what matched - including the
            // inflected forms the lemma expansion pulled in, not just the
            // words that were typed.
            var highlightForms = allForms
                .Where(f => f.Length > 1)
                .OrderByDescending(f => f.Length)
                .ToList();

            _currentResults = hits.Rows.Select(r =>
            {
                var passage = PassageItem.From(r);

                // The row's window sits on the first match rather than the
                // opening of the passage. Some of this corpus is ingested in
                // whole sections, so the matched form is often thousands of
                // characters in - past the cut, leaving a row that shows no
                // reason for being in the list at all.
                var spans = FindHighlightSpans(passage.Text, highlightForms);
                var first = spans.Count > 0 ? spans[0] : (Start: -1, Length: 0);
                var shown = PassageResults.RowTextAround(passage.Text, first.Start, first.Length);

                return new Match(passage, shown, FindHighlightSpans(shown, highlightForms));
            }).ToList();

            _resultsList.ItemsSource = _currentResults;

            // Truncation matters more here than in a read-only view: this
            // window writes tags, so a capped result set means a tagging pass
            // that looks complete and isn't. Say so before anything is saved.
            if (_currentResults.Count == 0)
            {
                _statusLabel.Text = "No matches. Try alternate spellings, or check whether lemma data is loaded for this language.";
                PassageResults.SetMessage(_resultsMessage, "No matches.");
            }
            else if (hits.Truncated)
            {
                _statusLabel.Classes.Add("warning");
                _statusLabel.Text = $"{hits.DisplayCount} match(es) found - stopped at the result limit, so tagging these will NOT tag every occurrence. Narrow the search and repeat to cover the rest.";
            }
            else
            {
                _statusLabel.Text = $"{_currentResults.Count} match(es) found, all checked by default. Uncheck anything that's wrong before tagging.";
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "The search failed - see message.";
            await ShowErrorAsync("Search failed", ex);
        }
        finally
        {
            _searchButton.IsEnabled = true;
        }
    }

    private void SetAllChecked(bool value)
    {
        foreach (var match in _currentResults) match.IsChecked = value;
    }

    /// <summary>
    /// Puts the reader on the passage, and deliberately stays open:
    /// auto-tagging is a review, and closing on every verification jump would
    /// break the flow of checking a few passages before committing.
    /// </summary>
    private async Task JumpToSelectedAsync()
    {
        if (_resultsList.SelectedItem is not Match match) return;
        await NavigateAsync(match.Passage.WorkId, match.Passage.TextNodeId);
    }

    private async Task TagCheckedAsync()
    {
        var name = (_nameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            await Alert.Info(this, "Nothing to tag", "Type a name first.");
            return;
        }

        var checkedIds = _currentResults.Where(m => m.IsChecked).Select(m => m.Passage.TextNodeId).ToList();
        if (checkedIds.Count == 0)
        {
            await Alert.Info(this, "Nothing to tag", "Nothing is checked.");
            return;
        }

        if (!await Alert.Confirm(this, "Confirm", $"Tag {checkedIds.Count} line(s) with “{name}”?", "Tag Them"))
            return;

        await TagCoreAsync(name, checkedIds, announce: true);
    }

    /// <summary>The tagging itself, after the confirmation; the tests call it directly.</summary>
    internal async Task<int> TagCoreAsync(string name, IReadOnlyList<long> checkedIds, bool announce)
    {
        var category = string.IsNullOrWhiteSpace(_categoryBox.Text) ? null : _categoryBox.Text.Trim();

        _tagAllButton.IsEnabled = false;
        _statusLabel.Classes.Remove("warning");
        _statusLabel.Text = "Tagging…";

        try
        {
            var (tagId, storedCategory) = await _tagRepo.GetOrCreateAsync(name, category);
            var tagged = await _tagRepo.BulkTagTextNodesAsync(tagId, checkedIds);

            // A category typed here is not applied to a tag that already has
            // one - see TagRepository.GetOrCreateAsync for why keeping the old
            // one is the safer of the two wrong-looking answers. Saying so is
            // the part that was missing: the box was filled in, the tag kept
            // its old category, and nothing on screen mentioned it.
            var categoryNote = CategoryNote(name, category, storedCategory);

            _statusLabel.Text = $"Tagged {tagged} line(s) with “{name}”."
                                + (categoryNote.Length > 0 ? $" Kept its existing category “{storedCategory}”." : string.Empty);
            TagsChanged?.Invoke();

            if (announce)
            {
                await Alert.Info(this, "Done",
                    $"Tagged {tagged} line(s) with “{name}” ({checkedIds.Count - tagged} were already tagged)." + categoryNote);
            }

            return tagged;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Tagging failed - see message.";
            await Alert.Error(this, "Error", ex.Message);
            return 0;
        }
        finally
        {
            _tagAllButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The sentence that explains a category typed and not taken, or nothing
    /// when the tag took what was typed (or nothing was typed).
    /// </summary>
    internal static string CategoryNote(string name, string? typed, string? stored) =>
        !string.IsNullOrWhiteSpace(typed)
        && !string.IsNullOrWhiteSpace(stored)
        && !string.Equals(typed, stored, StringComparison.OrdinalIgnoreCase)
            ? $"\n\n“{name}” already existed under the category “{stored}”, so it kept that "
              + $"rather than taking “{typed}”. Rename or re-categorise it from the Tag Browser."
            : string.Empty;

    private static Control MatchRow(Match match)
    {
        var box = new CheckBox { IsChecked = match.IsChecked, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 6, 0), MinWidth = 0, Padding = new Thickness(0) };
        box.IsCheckedChanged += (_, _) => match.IsChecked = box.IsChecked == true;

        // Subscribed only while the row is on screen, so Check All reaches the
        // rows already drawn and the matches do not keep dead rows alive.
        PropertyChangedEventHandler onChanged = (_, _) => box.IsChecked = match.IsChecked;
        box.AttachedToVisualTree += (_, _) =>
        {
            match.PropertyChanged -= onChanged;
            match.PropertyChanged += onChanged;
            box.IsChecked = match.IsChecked;
        };
        box.DetachedFromVisualTree += (_, _) => match.PropertyChanged -= onChanged;

        var row = PassageResults.Row(match.Passage.Source, match.Shown, match.Passage.CitationRef, match.Passage.Milestone, highlight: match.Highlight);

        var dock = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        dock.Children.Add(box);
        dock.Children.Add(row);
        return dock;
    }

    /// <summary>One match: the passage, the stretch of it the row shows, and whether it will be tagged.</summary>
    internal sealed class Match : INotifyPropertyChanged
    {
        private bool _isChecked = true;

        public Match(PassageItem passage, string shown, IReadOnlyList<(int Start, int Length)> highlight)
        {
            Passage = passage;
            Shown = shown;
            Highlight = highlight;
        }

        public PassageItem Passage { get; }
        public string Shown { get; }
        public IReadOnlyList<(int Start, int Length)> Highlight { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
