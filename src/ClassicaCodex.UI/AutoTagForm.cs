using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Finds every occurrence of a name across the corpus and lets you bulk-tag
/// them, instead of tagging one line at a time. This is what makes the myth
/// network practical to build out - tag "Athena" once here and every
/// occurrence in the library becomes part of the graph, rather than needing
/// to be found and tagged by hand one passage at a time.
///
/// Deliberately trust-the-preview-text rather than require opening each
/// passage: a name search on a proper noun has real failure modes (a name
/// that's also an ordinary word, different translators rendering a god
/// differently, an epithet-only reference the search can't catch at all),
/// so the preview list is there to catch those by eye before committing -
/// double-click still jumps to a passage in full context if something looks
/// worth checking closer.
/// </summary>
public class AutoTagForm : ScaledForm
{
    private readonly TextBox _nameBox;
    private readonly TextBox _categoryBox;
    private readonly TextBox _altSpellingsBox;
    private readonly Button _searchButton;
    private readonly CheckedListBox _resultsList;
    private readonly Button _checkAllButton;
    private readonly Button _uncheckAllButton;
    private readonly Button _tagAllButton;
    private readonly Label _statusLabel;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly TagRepository _tagRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone)> _currentResults = new();

    /// <summary>The forms actually searched for, used to find the match in each row.</summary>
    private List<string> _highlightForms = new();

    /// <summary>
    /// Where each searched form appears in a line, as (start, length) pairs
    /// in reading order. Longer forms are matched first so a longer form
    /// wins over a shorter one it contains.
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

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    /// <summary>Set by MainForm before showing this dialog - lets a double-click jump to a passage.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    /// <summary>Raised after a successful tag-all, so the graph can refresh.</summary>
    public event Action? TagsChanged;

    public AutoTagForm()
    {
        Text = "Auto-Tag";
        AppIcons.ApplyWindowIcon(this, "AutoTag");
        Width = 1100;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;

        var nameLabel = new Label { Text = "Name (this becomes the tag):", Left = 12, Top = 12, Width = 220 };
        _nameBox = new TextBox { Left = 12, Top = 34, Width = 220 };

        var categoryLabel = new Label { Text = "Category (optional):", Left = 244, Top = 12, Width = 180 };
        _categoryBox = new TextBox { Left = 244, Top = 34, Width = 180, Text = "god" };

        var altLabel = new Label
        {
            Text = "Alternate spellings to also match (comma-separated - e.g. Athene, Pallas, Minerva):",
            Left = 436,
            Top = 12,
            Width = 636,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _altSpellingsBox = new TextBox
        {
            Left = 436,
            Top = 34,
            Width = 636,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _searchButton = new Button { Text = "Search Corpus", Left = 12, Top = 66, Width = 150, Height = 30 };
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        var resultsLabel = new Label
        {
            Text = "Matches (checked ones get tagged - double-click to jump and verify):",
            Left = 12,
            Top = 108,
            Width = 700,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _resultsList = new CheckedListBox
        {
            Left = 12,
            Top = 130,
            Width = 1060,
            Height = 480,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            CheckOnClick = true

            // No DrawMode here, and no DrawItem handler: CheckedListBox
            // overrides DrawMode with a setter that discards the value and a
            // getter that always answers Normal, so owner-draw cannot be
            // turned on and DrawItem never fires. This form carried both for
            // some time, to highlight the matched forms in each row; measured
            // against a plain ListBox in the same window, the ListBox's
            // DrawItem fired for every item and this one's fired for none.
            // The rows put the match in view instead - see RunSearchAsync.
        };
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        ListResultHelpers.AttachCitationTooltip(_resultsList,
            i => i < _currentResults.Count ? _currentResults[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_resultsList,
            i => i < _currentResults.Count
                ? $"{_currentResults[i].AuthorName}, {_currentResults[i].WorkTitle} [{PassageCitation.Display(_currentResults[i].CitationRef, _currentResults[i].Milestone)}]: {_currentResults[i].Text}"
                : null);
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            $"Auto-Tag matches for {_nameBox.Text.Trim()}",
            _currentResults.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text,
                Milestone: r.Milestone)).ToList()), this);

        _checkAllButton = new Button { Text = "Check All", Left = 12, Top = 618, Width = 100, Height = 28, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _checkAllButton.Click += (_, _) => SetAllChecked(true);

        _uncheckAllButton = new Button { Text = "Uncheck All", Left = 120, Top = 618, Width = 100, Height = 28, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _uncheckAllButton.Click += (_, _) => SetAllChecked(false);

        _tagAllButton = new Button { Text = "Tag All Checked", Left = 872, Top = 616, Width = 200, Height = 32, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        _tagAllButton.Click += async (_, _) => await TagCheckedAsync();

        _statusLabel = new Label { Left = 12, Top = 656, Width = 1060, Height = 24, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

        Controls.Add(nameLabel);
        Controls.Add(_nameBox);
        Controls.Add(categoryLabel);
        Controls.Add(_categoryBox);
        Controls.Add(altLabel);
        Controls.Add(_altSpellingsBox);
        Controls.Add(_searchButton);
        Controls.Add(resultsLabel);
        Controls.Add(_resultsList);
        Controls.Add(_checkAllButton);
        Controls.Add(_uncheckAllButton);
        Controls.Add(_tagAllButton);
        Controls.Add(_statusLabel);

        ReadingTheme.AttachTo(this);

        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task RunSearchAsync()
    {
        var name = _nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Type a name first.", "Nothing to search",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var terms = new List<string> { name };
        terms.AddRange(_altSpellingsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        _searchButton.Enabled = false;
        _statusLabel.Text = "Searching...";
        _resultsList.Items.Clear();

        try
        {
            // Expand every typed term through the lemma data (covers
            // inflected Greek/Latin forms where lemma coverage exists) and
            // merge into one combined form list. For a plain English name
            // with no lemma match, this just falls back to the term itself.
            //
            // The expansion and the search go to the thread pool together -
            // see the note in SearchForm. Microsoft.Data.Sqlite's async
            // methods run synchronously, so awaiting these on the UI thread
            // held the window for the whole round: one lemma query per typed
            // term, then a corpus-wide search for every form they expanded to.
            var allForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hits = await Task.Run(async () =>
            {
                foreach (var term in terms)
                {
                    foreach (var form in await _lemmaRepo.ExpandFormAsync(term))
                    {
                        allForms.Add(form);
                    }
                }

                return await _textNodeRepo.SearchByFormsAsync(allForms.ToList());
            });

            _currentResults = hits.Rows;

            // Kept so the results list can highlight exactly what matched -
            // including the inflected forms the lemma expansion pulled in,
            // not just the words that were typed.
            _highlightForms = allForms
                .Where(f => f.Length > 1)
                .OrderByDescending(f => f.Length)
                .ToList();

            foreach (var r in _currentResults)
            {
                // Position the row's window on the first match rather than on
                // the opening of the passage. Some of this corpus is ingested
                // in whole sections, so the form a search matched is often
                // thousands of characters in - past the cut, leaving a row
                // that shows no reason for being in the list at all.
                var spans = FindHighlightSpans(r.Text, _highlightForms);
                var first = spans.Count > 0 ? spans[0] : (Start: -1, Length: 0);

                var index = _resultsList.Items.Add(
                    $"{r.AuthorName}, {r.WorkTitle}: " +
                    ListResultHelpers.RowTextAround(r.Text, first.Start, first.Length));
                _resultsList.SetItemChecked(index, true);
            }

            ListResultHelpers.RefreshHorizontalExtent(
                _resultsList, i => _resultsList.Items[i]?.ToString());

            // Truncation matters more here than in a read-only view: this
            // form writes tags, so a capped result set means a tagging pass
            // that looks complete and isn't. Say so before anything is saved.
            _statusLabel.Text = _currentResults.Count == 0
                ? "No matches. Try alternate spellings, or check whether lemma data is loaded for this language."
                : hits.Truncated
                    ? $"{hits.DisplayCount} match(es) found - stopped at the result limit, so tagging these will NOT tag every occurrence. Narrow the search and repeat to cover the rest."
                    : $"{_currentResults.Count} match(es) found, all checked by default. Uncheck anything that's wrong before tagging.";
        }
        finally
        {
            _searchButton.Enabled = true;
        }
    }

    private void SetAllChecked(bool value)
    {
        for (var i = 0; i < _resultsList.Items.Count; i++)
        {
            _resultsList.SetItemChecked(i, value);
        }
    }

    private async Task JumpToSelectedAsync()
    {
        var index = _resultsList.SelectedIndex;
        if (index < 0 || index >= _currentResults.Count || OnNavigate == null) return;

        var result = _currentResults[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
        // Deliberately doesn't close - auto-tagging is a review workflow,
        // and closing on every verification jump would break the flow of
        // checking a few passages before committing.
    }

    private async Task TagCheckedAsync()
    {
        var name = _nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Type a name first.", "Nothing to tag",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var checkedIds = new List<long>();
        for (var i = 0; i < _resultsList.Items.Count; i++)
        {
            if (_resultsList.GetItemChecked(i) && i < _currentResults.Count)
            {
                checkedIds.Add(_currentResults[i].TextNodeId);
            }
        }

        if (checkedIds.Count == 0)
        {
            MessageBox.Show(this, "Nothing is checked.", "Nothing to tag",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var category = string.IsNullOrWhiteSpace(_categoryBox.Text) ? null : _categoryBox.Text.Trim();

        var confirm = MessageBox.Show(this,
            $"Tag {checkedIds.Count} line(s) with \"{name}\"?",
            "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _tagAllButton.Enabled = false;
        _statusLabel.Text = "Tagging...";

        try
        {
            var (tagId, storedCategory) = await _tagRepo.GetOrCreateAsync(name, category);
            var tagged = await _tagRepo.BulkTagTextNodesAsync(tagId, checkedIds);

            // A category typed here is not applied to a tag that already has
            // one - see TagRepository.GetOrCreateAsync for why keeping the old
            // one is the safer of the two wrong-looking answers. Saying so is
            // the part that was missing: the box was filled in, the tag kept
            // its old category, and nothing on screen mentioned it.
            var categoryNote =
                !string.IsNullOrWhiteSpace(category)
                && !string.IsNullOrWhiteSpace(storedCategory)
                && !string.Equals(category, storedCategory, StringComparison.OrdinalIgnoreCase)
                    ? $"\r\n\r\n\"{name}\" already existed under the category \"{storedCategory}\", so it kept that "
                      + $"rather than taking \"{category}\". Rename or re-categorise it from the Tag Browser."
                    : string.Empty;

            _statusLabel.Text = $"Tagged {tagged} line(s) with \"{name}\"."
                                + (categoryNote.Length > 0 ? $" Kept its existing category \"{storedCategory}\"." : string.Empty);
            TagsChanged?.Invoke();

            MessageBox.Show(this,
                $"Tagged {tagged} line(s) with \"{name}\" ({checkedIds.Count - tagged} were already tagged)."
                + categoryNote,
                "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Tagging failed - see message.";
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _tagAllButton.Enabled = true;
        }
    }
}
