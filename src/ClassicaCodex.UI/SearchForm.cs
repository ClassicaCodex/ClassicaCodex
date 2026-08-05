using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Search, given room to be a real tool rather than a text box wedged into
/// the toolbar.
///
/// The old search was one box and a strip of results along the bottom of the
/// reader, which meant the results competed with the text you were reading
/// for the same screen and there was nowhere to put anything else. Everything
/// here that isn't the box itself exists because there was no room for it
/// before: narrowing by language, corpus, author, era, or your own tags;
/// choosing how the words should match; and getting the whole result set out
/// through Export rather than one line at a time.
///
/// Non-modal on purpose. A search window you have to close before you can
/// look at what it found is a worse search window - this one stays open
/// beside the reader, and double-clicking a result moves the reader behind
/// it.
/// </summary>
public class SearchForm : Form
{
    private readonly TextBox _queryBox;
    private readonly Button _searchButton;
    private readonly ComboBox _matchModeBox;
    private readonly CheckBox _greekCheck;
    private readonly CheckBox _latinCheck;
    private readonly CheckBox _englishCheck;
    private readonly ComboBox _kindBox;
    private readonly ComboBox _authorBox;
    private readonly ComboBox _eraBox;
    private readonly ComboBox _tagBox;
    private readonly CheckBox _bookmarkedCheck;
    private readonly ListBox _resultsList;
    private readonly Label _statusLabel;
    private readonly Button _clearFiltersButton;
    private readonly ComboBox _recentBox;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly TagRepository _tagRepo = new();
    private readonly RecentSearchRepository _recentRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _results = new();
    private List<string> _highlightTerms = new();
    private List<Author> _authors = new();
    private List<RecentSearch> _recent = new();

    // Set while a recent search is being restored into the controls, so the
    // combo's own SelectedIndexChanged doesn't re-enter and run again.
    private bool _applyingRecent;
    private int _displayedCount;
    private bool _searching;

    /// <summary>
    /// How many result rows get painted. The list owner-draws every row to
    /// highlight the matched words, so this is a rendering budget rather
    /// than a limit on what was found - the status line reports the real
    /// total.
    /// </summary>
    private const int DisplayLimit = 500;

    /// <summary>Set by MainForm; moves the reader to a passage.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public SearchForm()
    {
        Text = "Search";
        AppIcons.ApplyWindowIcon(this, "Search");
        Width = 1100;
        Height = 720;
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;

        // --- query row -------------------------------------------------
        var queryLabel = new Label { Text = "Search for:", Left = 14, Top = 17, Width = 80 };
        _queryBox = new TextBox
        {
            Left = 96,
            Top = 14,
            Width = 520,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "A word or phrase - see Match for how it's compared"
        };
        _queryBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RunSearchAsync();
        };

        _searchButton = new Button
        {
            Text = "Search",
            Left = 624,
            Top = 12,
            Width = 96,
            Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _searchButton.Click += async (_, _) => await RunSearchAsync();
        AppIcons.Apply(_searchButton, "Search", 16);

        _clearFiltersButton = new Button
        {
            Text = "Clear Filters",
            Left = 728,
            Top = 12,
            Width = 104,
            Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _clearFiltersButton.Click += (_, _) => ClearFilters();

        // --- recent searches -------------------------------------------
        var recentLabel = new Label { Text = "Recent:", Left = 14, Top = 52, Width = 54 };
        _recentBox = new ComboBox
        {
            Left = 68,
            Top = 48,
            Width = 560,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _recentBox.SelectedIndexChanged += async (_, _) => await ApplySelectedRecentAsync();

        // --- filter panel ----------------------------------------------
        var filterPanel = new GroupBox
        {
            Text = "Narrow the search",
            Left = 12,
            Top = 84,
            Width = 1060,
            Height = 104,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var matchLabel = new Label { Text = "Match:", Left = 12, Top = 26, Width = 50 };
        _matchModeBox = new ComboBox
        {
            Left = 64, Top = 22, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList
        };
        _matchModeBox.Items.AddRange(new object[]
        {
            "Anywhere in the line",
            "Whole words only",
            "All words, any order"
        });
        _matchModeBox.SelectedIndex = 0;

        var languageLabel = new Label { Text = "Language:", Left = 274, Top = 26, Width = 66 };
        _greekCheck = new CheckBox { Text = "Greek", Left = 342, Top = 24, Width = 62 };
        _latinCheck = new CheckBox { Text = "Latin", Left = 406, Top = 24, Width = 58 };
        _englishCheck = new CheckBox { Text = "English", Left = 464, Top = 24, Width = 70 };

        var kindLabel = new Label { Text = "Text:", Left = 552, Top = 26, Width = 40 };
        _kindBox = new ComboBox
        {
            Left = 592, Top = 22, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList
        };
        _kindBox.Items.AddRange(new object[]
        {
            "Originals and translations",
            "Originals only",
            "Translations only"
        });
        _kindBox.SelectedIndex = 0;

        _bookmarkedCheck = new CheckBox
        {
            Text = "Bookmarked passages only", Left = 800, Top = 24, Width = 200
        };

        var authorLabel = new Label { Text = "Author:", Left = 12, Top = 66, Width = 50 };
        _authorBox = new ComboBox
        {
            Left = 64, Top = 62, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList
        };

        var eraLabel = new Label { Text = "Era:", Left = 330, Top = 66, Width = 34 };
        _eraBox = new ComboBox
        {
            Left = 366, Top = 62, Width = 216, DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var era in SearchEras.All) _eraBox.Items.Add(era);
        _eraBox.SelectedIndex = 0;

        var tagLabel = new Label { Text = "Tagged:", Left = 598, Top = 66, Width = 54 };
        _tagBox = new ComboBox
        {
            Left = 652, Top = 62, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList
        };

        filterPanel.Controls.AddRange(new Control[]
        {
            matchLabel, _matchModeBox, languageLabel, _greekCheck, _latinCheck, _englishCheck,
            kindLabel, _kindBox, _bookmarkedCheck,
            authorLabel, _authorBox, eraLabel, _eraBox, tagLabel, _tagBox
        });

        // --- results ---------------------------------------------------
        _resultsList = new ListBox
        {
            Left = 12,
            Top = 196,
            Width = 1060,
            Height = 444,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true,
            DrawMode = DrawMode.OwnerDrawFixed
        };
        _resultsList.DrawItem += Results_DrawItem;
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedAsync();

        ListResultHelpers.AttachCitationTooltip(_resultsList,
            i => i < _displayedCount ? _results[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_resultsList,
            i => i < _displayedCount
                ? $"{_results[i].AuthorName}, {_results[i].WorkTitle} [{_results[i].CitationRef}]: {_results[i].Text}"
                : null);
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            DescribeSearch(),
            _results.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text)).ToList()), this);

        _statusLabel = new Label
        {
            Left = 14,
            Top = 650,
            Width = 1058,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray,
            Text = "Type a word and press Enter. Double-click a result to open it in the reader."
        };

        Controls.AddRange(new Control[]
        {
            queryLabel, _queryBox, _searchButton, _clearFiltersButton,
            recentLabel, _recentBox,
            filterPanel, _resultsList, _statusLabel
        });

        Load += async (_, _) =>
        {
            await LoadFilterOptionsAsync();
            await LoadRecentAsync();
        };
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    /// <summary>
    /// Author and tag lists come from the library itself rather than being
    /// typed, since both are closed sets and misremembering a spelling
    /// shouldn't silently return nothing.
    /// </summary>
    private async Task LoadFilterOptionsAsync()
    {
        try
        {
            _authors = await _authorRepo.GetAllAsync();
            _authorBox.Items.Add("(any author)");
            foreach (var author in _authors) _authorBox.Items.Add(author.Name);
            _authorBox.SelectedIndex = 0;

            var tags = await _tagRepo.GetAllTagsAsync();
            _tagBox.Items.Add("(any)");
            foreach (var tag in tags) _tagBox.Items.Add(tag.Name);
            _tagBox.SelectedIndex = 0;

            if (tags.Count == 0)
            {
                _tagBox.Enabled = false;
                var tip = new ToolTip();
                ReadingTheme.ApplyToToolTip(tip);
                tip.SetToolTip(_tagBox, "You haven't tagged anything yet.");
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't load the filter lists: {ex.Message}";
        }
    }

    private async Task LoadRecentAsync()
    {
        try
        {
            _recent = await _recentRepo.GetAllAsync();

            _applyingRecent = true;
            _recentBox.Items.Clear();
            _recentBox.Items.Add(_recent.Count == 0 ? "(no recent searches yet)" : "(pick a recent search)");
            foreach (var recent in _recent) _recentBox.Items.Add(recent.Name);
            _recentBox.SelectedIndex = 0;
            _applyingRecent = false;
        }
        catch (Exception ex)
        {
            _applyingRecent = false;
            _statusLabel.Text = $"Couldn't load recent searches: {ex.Message}";
        }
    }

    /// <summary>
    /// Restores a recent search into the controls and runs it.
    ///
    /// Runs it rather than only loading it: someone reaching into this list
    /// wants the results back, and if they want to adjust something the
    /// filters are all sitting there afterwards anyway.
    ///
    /// Author and era are matched by name and label rather than restored
    /// from an id, so an entry recorded against an older library still
    /// selects the right thing - and quietly selects nothing, rather than
    /// the wrong thing, when that author is no longer loaded.
    /// </summary>
    private async Task ApplySelectedRecentAsync()
    {
        if (_applyingRecent) return;
        if (_recentBox.SelectedIndex <= 0 || _recentBox.SelectedIndex - 1 >= _recent.Count) return;

        var recent = _recent[_recentBox.SelectedIndex - 1];

        _applyingRecent = true;
        try
        {
            _queryBox.Text = recent.Query;

            _matchModeBox.SelectedIndex = recent.MatchMode switch
            {
                nameof(SearchMatchMode.WholeWord) => 1,
                nameof(SearchMatchMode.AllWords) => 2,
                _ => 0
            };

            var languages = recent.Languages.Split(',', StringSplitOptions.RemoveEmptyEntries);
            _greekCheck.Checked = languages.Contains("grc");
            _latinCheck.Checked = languages.Contains("lat");
            _englishCheck.Checked = languages.Contains("eng");

            _kindBox.SelectedIndex = recent.OriginalsOnly switch { true => 1, false => 2, null => 0 };
            _bookmarkedCheck.Checked = recent.BookmarkedOnly;

            SelectByText(_authorBox, recent.AuthorName);
            SelectByText(_tagBox, recent.TagName);
            SelectByText(_eraBox, recent.EraLabel);
        }
        finally
        {
            _applyingRecent = false;
        }

        await RunSearchAsync();
    }

    /// <summary>
    /// Selects an entry by its text, falling back to the first item - which
    /// is always the "(any)" option - when it isn't there. An entry naming
    /// an author whose corpus isn't currently loaded must not silently land
    /// on whichever author happens to sit at that position now.
    /// </summary>
    private static void SelectByText(ComboBox combo, string? text)
    {
        if (combo.Items.Count == 0) return;

        if (!string.IsNullOrEmpty(text))
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i]?.ToString() == text)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        combo.SelectedIndex = 0;
    }

    /// <summary>
    /// Files the search that just ran into the recent list.
    ///
    /// Its description is its identity - DescribeSearch already renders the
    /// query and every active filter, and the table's unique constraint on
    /// that string is what makes re-running a search move it up the list
    /// instead of adding a duplicate.
    ///
    /// Failing to record is not worth telling anyone about: the search
    /// itself succeeded, and the results are on screen.
    /// </summary>
    private async Task RecordCurrentAsync()
    {
        var languages = new List<string>();
        if (_greekCheck.Checked) languages.Add("grc");
        if (_latinCheck.Checked) languages.Add("lat");
        if (_englishCheck.Checked) languages.Add("eng");

        var entry = new RecentSearch
        {
            Name = DescribeSearch(),
            Query = _queryBox.Text.Trim(),
            MatchMode = _matchModeBox.SelectedIndex switch
            {
                1 => nameof(SearchMatchMode.WholeWord),
                2 => nameof(SearchMatchMode.AllWords),
                _ => nameof(SearchMatchMode.Contains)
            },
            Languages = string.Join(",", languages),
            Corpora = string.Empty,
            OriginalsOnly = _kindBox.SelectedIndex switch { 1 => true, 2 => false, _ => null },
            AuthorName = _authorBox.SelectedIndex > 0 ? _authorBox.SelectedItem?.ToString() : null,
            TagName = _tagBox.SelectedIndex > 0 ? _tagBox.SelectedItem?.ToString() : null,
            BookmarkedOnly = _bookmarkedCheck.Checked,
            EraLabel = _eraBox.SelectedIndex > 0 ? _eraBox.SelectedItem?.ToString() : null
        };

        try
        {
            await _recentRepo.RecordAsync(entry);
            await LoadRecentAsync();
        }
        catch (Exception ex)
        {
            // Says so rather than failing silently. Swallowing this hid two
            // separate bugs during development - a malformed statement, then
            // a schema that only half-migrated - and in both cases the
            // symptom was an empty list with nothing anywhere to explain it.
            // The search itself did succeed, so this is appended to the
            // result count rather than replacing it.
            _statusLabel.Text += $"  (couldn't add to Recent: {ex.Message})";
        }
    }


    private void ClearFilters()
    {
        // The picker would otherwise still name a search these controls no
        // longer match.
        _applyingRecent = true;
        if (_recentBox.Items.Count > 0) _recentBox.SelectedIndex = 0;
        _applyingRecent = false;

        _matchModeBox.SelectedIndex = 0;
        _greekCheck.Checked = false;
        _latinCheck.Checked = false;
        _englishCheck.Checked = false;
        _kindBox.SelectedIndex = 0;
        _bookmarkedCheck.Checked = false;
        if (_authorBox.Items.Count > 0) _authorBox.SelectedIndex = 0;
        if (_tagBox.Items.Count > 0) _tagBox.SelectedIndex = 0;
        _eraBox.SelectedIndex = 0;
    }

    private SearchFilters BuildFilters()
    {
        var filters = new SearchFilters
        {
            Query = _queryBox.Text.Trim(),
            MatchMode = _matchModeBox.SelectedIndex switch
            {
                1 => SearchMatchMode.WholeWord,
                2 => SearchMatchMode.AllWords,
                _ => SearchMatchMode.Contains
            },
            OriginalsOnly = _kindBox.SelectedIndex switch
            {
                1 => true,
                2 => false,
                _ => null
            },
            BookmarkedOnly = _bookmarkedCheck.Checked
        };

        if (_greekCheck.Checked) filters.Languages.Add("grc");
        if (_latinCheck.Checked) filters.Languages.Add("lat");
        if (_englishCheck.Checked) filters.Languages.Add("eng");

        if (_authorBox.SelectedIndex > 0 && _authorBox.SelectedIndex - 1 < _authors.Count)
        {
            filters.AuthorId = _authors[_authorBox.SelectedIndex - 1].AuthorId;
        }

        if (_tagBox.SelectedIndex > 0)
        {
            filters.TagName = _tagBox.SelectedItem?.ToString();
        }

        // Author dates live in a curated table in this layer, not in the
        // database, so the era becomes a list of author ids here rather than
        // a clause the repository could write for itself.
        if (_eraBox.SelectedItem is SearchEra era && era.StartYear != null)
        {
            filters.EraAuthorIds = _authors
                .Where(a =>
                {
                    var dates = AuthorEraData.Lookup(a.Name);
                    return dates != null
                        && dates.Value.EndYear >= era.StartYear
                        && dates.Value.StartYear <= era.EndYear;
                })
                .Select(a => a.AuthorId)
                .ToList();
        }

        return filters;
    }

    private async Task RunSearchAsync()
    {
        if (_searching) return;

        var filters = BuildFilters();
        if (filters.Query.Length == 0)
        {
            _statusLabel.Text = "Type something to search for.";
            return;
        }

        _searching = true;
        _searchButton.Enabled = false;
        _statusLabel.Text = "Searching...";
        UseWaitCursor = true;

        try
        {
            _highlightTerms = filters.Query
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 0)
                .ToList();

            var hits = await _textNodeRepo.SearchFilteredAsync(filters);
            _results = hits.Rows;
            _displayedCount = Math.Min(_results.Count, DisplayLimit);

            _resultsList.BeginUpdate();
            try
            {
                _resultsList.Items.Clear();

                foreach (var r in _results.Take(DisplayLimit))
                {
                    _resultsList.Items.Add($"{r.AuthorName}, {r.WorkTitle}: {r.Text}");
                }

                if (_results.Count == 0)
                {
                    _resultsList.Items.Add(filters.HasAnyNarrowing
                        ? "No matches. Try Clear Filters, or a different match mode."
                        : "No matches.");
                }
                else if (hits.Truncated || _results.Count > _displayedCount)
                {
                    _resultsList.Items.Add(hits.Truncated
                        ? $"--- showing {_displayedCount:N0} of {hits.DisplayCount} matches; the search stopped at its limit. Narrow it to see the rest. ---"
                        : $"--- showing {_displayedCount:N0} of {_results.Count:N0} matches. ---");
                }
            }
            finally
            {
                _resultsList.EndUpdate();
            }

            _statusLabel.Text = _results.Count == 0
                ? "No matches."
                : $"{hits.DisplayCount} match(es). Double-click to open in the reader; " +
                  "right-click to copy or export.";

            // Recorded at the end rather than the start: a search that threw
            // isn't one worth offering back. Replaying an entry from the
            // list records it too, which is what moves it to the top - a
            // recent list should reflect what was actually run most
            // recently, however it was launched.
            await RecordCurrentAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            _searching = false;
            _searchButton.Enabled = true;
            UseWaitCursor = false;
        }
    }

    /// <summary>
    /// A title for an exported result set that says what was actually asked,
    /// filters included - "wrath" and "wrath (Greek, originals only)" produce
    /// very different documents and shouldn't be named the same thing.
    /// </summary>
    private string DescribeSearch()
    {
        var parts = new List<string>();

        if (_matchModeBox.SelectedIndex == 1) parts.Add("whole words");
        if (_matchModeBox.SelectedIndex == 2) parts.Add("all words");

        var languages = new List<string>();
        if (_greekCheck.Checked) languages.Add("Greek");
        if (_latinCheck.Checked) languages.Add("Latin");
        if (_englishCheck.Checked) languages.Add("English");
        if (languages.Count > 0) parts.Add(string.Join("/", languages));

        if (_kindBox.SelectedIndex == 1) parts.Add("originals only");
        if (_kindBox.SelectedIndex == 2) parts.Add("translations only");
        if (_authorBox.SelectedIndex > 0) parts.Add(_authorBox.SelectedItem?.ToString() ?? string.Empty);
        if (_eraBox.SelectedIndex > 0) parts.Add(_eraBox.SelectedItem?.ToString() ?? string.Empty);
        if (_tagBox.SelectedIndex > 0) parts.Add($"tagged \u201c{_tagBox.SelectedItem}\u201d");
        if (_bookmarkedCheck.Checked) parts.Add("bookmarked");

        var query = _queryBox.Text.Trim();
        return parts.Count == 0
            ? $"Search: {query}"
            : $"Search: {query} ({string.Join(", ", parts)})";
    }

    private async Task JumpToSelectedAsync()
    {
        var index = _resultsList.SelectedIndex;

        // Bounded by the displayed count, not the result count - the last
        // row can be a notice rather than a result.
        if (index < 0 || index >= _displayedCount || OnNavigate == null) return;

        var result = _results[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
    }

    /// <summary>
    /// Owner-drawn so the matched words can be highlighted inside the row -
    /// the same treatment the old inline search results had, which is the
    /// part of it worth keeping.
    /// </summary>
    private void Results_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var backBrush = new SolidBrush(selected ? ReadingTheme.SelectionBackground : ReadingTheme.Surface))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        var text = _resultsList.Items[e.Index]?.ToString() ?? string.Empty;
        var font = _resultsList.Font;
        var bounds = e.Bounds;
        var x = bounds.Left;
        var foreColor = selected ? ReadingTheme.SelectionText : ReadingTheme.Text;

        void DrawPart(string part, bool highlighted)
        {
            if (part.Length == 0) return;

            var size = TextRenderer.MeasureText(e.Graphics, part, font,
                new Size(int.MaxValue, bounds.Height), TextFormatFlags.NoPadding);
            var rect = new Rectangle(x, bounds.Top, size.Width, bounds.Height);

            if (highlighted)
            {
                var highlightColor = ReadingTheme.IsDark
                    ? Color.FromArgb(120, 92, 20)
                    : Color.Khaki;
                using var highlightBrush = new SolidBrush(highlightColor);
                e.Graphics.FillRectangle(highlightBrush, rect);
            }

            TextRenderer.DrawText(e.Graphics, part, font, rect, foreColor, TextFormatFlags.NoPadding);
            x += size.Width;
        }

        // Notice rows carry no match and shouldn't be highlighted as if they
        // did - they aren't results.
        var spans = e.Index < _displayedCount
            ? FindHighlightSpans(text, _highlightTerms)
            : new List<(int Start, int Length)>();

        var pos = 0;
        foreach (var (start, length) in spans)
        {
            if (start < pos) continue;
            DrawPart(text[pos..start], highlighted: false);
            DrawPart(text.Substring(start, length), highlighted: true);
            pos = start + length;
        }
        DrawPart(text[pos..], highlighted: false);

        e.DrawFocusRectangle();
    }

    private static List<(int Start, int Length)> FindHighlightSpans(string text, List<string> terms)
    {
        var spans = new List<(int Start, int Length)>();

        foreach (var term in terms)
        {
            if (term.Length == 0) continue;

            var idx = 0;
            while ((idx = text.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                spans.Add((idx, term.Length));
                idx += term.Length;
            }
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }
}
