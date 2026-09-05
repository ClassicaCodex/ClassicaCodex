using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

using ClassicaCodex.Core;

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
public class SearchForm : ScaledForm
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
    private readonly Button _collectionsButton;
    private readonly ContextMenuStrip _collectionsMenu = new();
    private readonly ComboBox _viewBox;
    private readonly ListBox _resultsList;
    private readonly Label _statusLabel;
    private readonly Button _clearFiltersButton;
    private readonly ComboBox _recentBox;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly TagRepository _tagRepo = new();
    private readonly RecentSearchRepository _recentRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _results = new();

    /// <summary>
    /// The passages actually on screen - every result, or one document's worth
    /// when a document row has been opened. Everything that indexes the list by
    /// row reads this rather than <see cref="_results"/>.
    /// </summary>
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _visible = new();

    /// <summary>
    /// One row per work, with how many matches fell in it - counted across
    /// the whole library rather than across the rows that came back.
    ///
    /// It used to be built by grouping the returned rows, which was wrong in
    /// a way nothing on screen could show. The search stops at its limit
    /// having ordered by author name, so what it stops with is the front of
    /// the alphabet rather than a sample: searching for "vel", 90% of the
    /// works containing it showed nothing, and Augustine - who has more of
    /// them than anyone, 1,056 - was credited with 184, because the cap
    /// landed in the middle of him. See CountMatchesByWorkAsync.
    /// </summary>
    private SearchDistribution _distribution = SearchDistribution.Empty;

    /// <summary>
    /// The rows the document view is actually showing - the distribution
    /// where it can be trusted, and the returned rows grouped where it
    /// cannot. Everything that indexes the list by row reads this, so the
    /// copy menu and the double-click cannot disagree with what was drawn.
    /// </summary>
    private List<(int WorkId, string AuthorName, string WorkTitle, long Matches)> _documents = new();

    /// <summary>Set while the passage list is scoped to one document.</summary>
    private int? _openDocumentWorkId;

    /// <summary>
    /// That document's own matches, fetched with the work pinned so the
    /// global result cap does not apply to it. Held apart from
    /// <see cref="_results"/> so that opening a document cannot change what
    /// the search itself found, and therefore cannot change what an export
    /// of the whole search would contain.
    /// </summary>
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _openDocumentRows = new();

    private bool _openDocumentTruncated;

    // Carried from the last search so the view can be re-rendered without
    // re-running it - switching between passages and documents is a change of
    // presentation, and should not cost another query.
    private bool _truncated;
    private string _displayCount = "0";
    private bool _narrowed;

    /// <summary>
    /// The filters the rows on screen came from, so that opening one document
    /// out of a truncated search can go back and ask for that document's
    /// matches rather than showing the handful the cap left behind.
    /// </summary>
    private SearchFilters? _lastFilters;

    /// <summary>The collections present in the library, so the menu offers only real ones.</summary>
    private List<(string Title, string Key)> _collections = new();

    private bool DocumentView => _viewBox.SelectedIndex == 1 && _openDocumentWorkId == null;

    private List<string> _highlightTerms = new();

    /// <summary>
    /// The normalized spellings the word index was actually asked for, so the
    /// highlighter can mark the word in a line the literal query does not
    /// appear in - see FindHighlightSpans.
    /// </summary>
    private HashSet<string> _highlightWords = new(StringComparer.Ordinal);

    /// <summary>Whether the search that produced these rows matched whole words.</summary>
    private bool _highlightWholeWordsOnly;
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

        // Whole words, not "anywhere in the line", and this is a correctness
        // choice rather than a speed one.
        //
        // "Anywhere" compares a LIKE pattern against the raw text. Greek is
        // written with diacritics, editions disagree about which, and nobody
        // types them into a search box - so the pattern contains characters the
        // text does not have and the search quietly misses most of what it was
        // asked for. Searching this library for a bare "μηνιν" returns 8 lines
        // in that mode and 316 in this one. With the accents typed correctly it
        // is still 303 against 316, because whole-word goes through an index
        // that normalises across editions and a LIKE pattern cannot.
        //
        // Latin has the same problem for a different reason. u/v and i/j were
        // one letter each, editors disagree about which glyph to print, and
        // only the index folds them - so "anywhere" finds "iustitia" 1,425
        // times where whole-word finds it 4,196, and finds "adiuvare" 58 times
        // against 293. That falls the wrong way round: the spellings it is
        // worst at are the ones a reader was taught.
        //
        // Nothing on screen says any of that. Someone reads "8 results" as a
        // fact about Homer rather than about the match mode, which is the worst
        // shape a wrong answer can take.
        //
        // It is also about 250x faster - 10ms against 2.5s, since it seeks the
        // word index instead of reading 594MB of text - but that is the smaller
        // half. "Anywhere" stays one item up the list for anyone hunting a stem
        // or a fragment, which is the thing it is genuinely good for.
        //
        // Safe when the index has not been built: the repository checks and
        // falls back to a LIKE prefilter with a normalised confirmation, which
        // still rejects substrings correctly, just without the accent folding.
        _matchModeBox.SelectedIndex = 1;

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

        // A menu rather than a row of checkboxes: the number of collections grows
        // every time a corpus is added, and a filter panel that has to grow with
        // it would push the results further down the window each time.
        _collectionsButton = new Button
        {
            Text = "Collections: all", Left = 870, Top = 61, Width = 178, Height = 26
        };
        _collectionsButton.Click += (_, _) =>
        {
            // Re-themed at the moment of showing. A context menu is not in the
            // control tree a theme toggle walks, so one themed when it was
            // filled would still be painted for whichever mode was current
            // then - and this menu is filled once, when the window opens.
            ReadingTheme.ApplyToContextMenu(_collectionsMenu);
            _collectionsMenu.Show(_collectionsButton, new Point(0, _collectionsButton.Height));
        };

        filterPanel.Controls.AddRange(new Control[]
        {
            matchLabel, _matchModeBox, languageLabel, _greekCheck, _latinCheck, _englishCheck,
            kindLabel, _kindBox, _bookmarkedCheck,
            authorLabel, _authorBox, eraLabel, _eraBox, tagLabel, _tagBox, _collectionsButton
        });

        // A view control, not a filter - it changes how the same matches are
        // presented, so it sits with the results rather than inside "Narrow the
        // search".
        var viewLabel = new Label { Text = "Show:", Left = 14, Top = 198, Width = 42 };
        _viewBox = new ComboBox
        {
            Left = 58, Top = 194, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList
        };
        _viewBox.Items.AddRange(new object[] { "Every matching passage", "One row per document" });
        _viewBox.SelectedIndex = 0;
        _viewBox.SelectedIndexChanged += (_, _) =>
        {
            // Switching the view always leaves a single document, so the results
            // never disagree with the control describing them.
            _openDocumentWorkId = null;
            _openDocumentRows = new();
            RenderResults();
        };

        // --- results ---------------------------------------------------
        _resultsList = new ListBox
        {
            Left = 12,
            Top = 226,
            Width = 1060,
            Height = 414,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,

            // This used to carry a note saying it had no horizontal scrollbar because
            // measuring items for one threw on the private-use codepoints in the Menota
            // transcriptions, and that owner-drawing "may well" skip that measurement -
            // an inference the note itself said nobody had tested.
            //
            // Tested now. Owner-drawing does skip it: WinForms will not measure rows it
            // does not draw, which is why this list needs RefreshHorizontalExtent after
            // every repopulate to get a scrollbar at all. And the measurement does not
            // throw - see the corpus-wide re-test recorded in ReadingTheme. Both halves
            // of the note were wrong in the same direction, and the screen used most was
            // the one paying for it: a long passage was cut off at the right edge with
            // nothing to say the line continued.
            DrawMode = DrawMode.OwnerDrawFixed
        };
        _resultsList.DrawItem += Results_DrawItem;
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedAsync();

        // All three index the list by row, so all three have to know that a row
        // means a document rather than a passage in the grouped view - a document
        // row has no single citation to show and no line to copy.
        ListResultHelpers.AttachCitationTooltip(_resultsList,
            i => !DocumentView && i < _displayedCount ? _visible[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_resultsList,
            i => i >= _displayedCount ? null
                : DocumentView
                    ? $"{_documents[i].AuthorName}, {_documents[i].WorkTitle} — {_documents[i].Matches} match(es)"
                    : $"{_visible[i].AuthorName}, {_visible[i].WorkTitle} [{PassageCitation.Display(_visible[i].CitationRef)}]: {_visible[i].Text}");

        // Export stays passage-level in both views: a list of documents and their
        // counts is not something anyone wants to paste into a notebook, and the
        // export is scoped to what is on screen, so opening a document exports
        // that document.
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            DescribeSearch(),
            _visible.Select(r => new ExportPassage(
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
            filterPanel, viewLabel, _viewBox, _resultsList, _statusLabel
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

            await LoadCollectionsAsync();

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
            SelectCollections(recent.Collections);
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
    /// Ticks the collections a recorded search was narrowed to, and unticks the
    /// rest - restoring a filter has to be able to clear one as well as set one.
    ///
    /// A key naming a collection no longer installed has no menu item and so is
    /// simply dropped, on the same principle as the author box above: the entry
    /// finds less than it once did rather than something different.
    /// </summary>
    private void SelectCollections(string keys)
    {
        var wanted = keys.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _collectionsMenu.Items.OfType<ToolStripMenuItem>())
            if (item.CheckOnClick && item.Tag is string key)
                item.Checked = wanted.Contains(key);
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
            Collections = string.Join(",", CheckedCollections()),
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

        // Back to the same default the form opens on - whole words - not to the
        // first item in the list. See the note where that default is set.
        _matchModeBox.SelectedIndex = 1;
        _greekCheck.Checked = false;
        _latinCheck.Checked = false;
        _englishCheck.Checked = false;
        _kindBox.SelectedIndex = 0;
        _bookmarkedCheck.Checked = false;
        if (_authorBox.Items.Count > 0) _authorBox.SelectedIndex = 0;
        if (_tagBox.Items.Count > 0) _tagBox.SelectedIndex = 0;
        _eraBox.SelectedIndex = 0;

        foreach (var item in _collectionsMenu.Items.OfType<ToolStripMenuItem>())
            if (item.CheckOnClick) item.Checked = false;
        UpdateCollectionsButton();
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

        foreach (var folder in CheckedCollections()) filters.Collections.Add(folder);

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

            _highlightWords = WordOccurrences.TargetsFor(filters.Query);
            _highlightWholeWordsOnly = filters.MatchMode == SearchMatchMode.WholeWord;

            // Task.Run, because the await above it is not one. SQLite has no
            // asynchronous I/O and Microsoft.Data.Sqlite says so: its Async
            // methods run to completion synchronously and hand back a task
            // that is already finished. Awaiting one on the UI thread yields
            // nothing and returns to nobody - so "Searching..." never painted,
            // the wait cursor never appeared, and the window sat frozen for
            // however long the query took, which in this form's default mode
            // - "Anywhere in the line", a substring match across 594MB of
            // text - is about two and a half seconds. Long enough for Windows
            // to offer to close it for you.
            //
            // Measured rather than assumed: the call spent 2,477ms of 2,477ms
            // inside itself and came back with IsCompleted already true.
            //
            // The same convention the ingest, word index, and stylometry
            // screens already use for their long work. Everything after the
            // await is back on the UI thread, as before.
            // Both on the one background hop. The distribution is what makes
            // the per-document view and the match total true rather than true
            // of whatever the cap left behind, and running it here means no
            // later interaction has to wonder whether it has been computed.
            // It reads the index instead of the text, so it costs a fraction
            // of the search it accompanies - measured on a full library, 26ms
            // beside 88ms for "virtus" and 116ms beside 245ms for "vel".
            var (hits, distribution) = await Task.Run(async () => (
                await _textNodeRepo.SearchFilteredAsync(filters),
                await _textNodeRepo.CountMatchesByWorkAsync(filters)));

            _results = hits.Rows;
            _distribution = distribution;
            _lastFilters = filters;
            _openDocumentWorkId = null;
            _openDocumentRows = new();
            _openDocumentTruncated = false;
            _truncated = hits.Truncated;
            _displayCount = hits.DisplayCount;
            _narrowed = filters.HasAnyNarrowing;
            RenderResults();

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

        // Names the match mode only when it is not the default one, the same
        // way the kind filter below names only its non-default choices. Whole
        // words is the default now, so "anywhere" is the one worth recording -
        // and it has to be recorded, because this description is also the
        // recent list's identity and the two modes can return very different
        // result sets for the same word.
        if (_matchModeBox.SelectedIndex == 0) parts.Add("anywhere in the line");
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

        // The description is also the recent list's identity, so the collections
        // have to appear in it. Without them "wrath" across the whole library and
        // "wrath" in CSEL are one name, and the second run overwrites the first.
        var chosen = CheckedCollections();
        if (chosen.Count > 0)
            parts.Add("in " + string.Join("/", chosen.Select(SetupDataSourceCatalog.DescribeCollection)));

        var query = _queryBox.Text.Trim();
        return parts.Count == 0
            ? $"Search: {query}"
            : $"Search: {query} ({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Offers only the collections that are actually installed, asked of the
    /// library rather than of the filesystem - a folder that was downloaded and
    /// never ingested has nothing in it to search, and a checkbox for it would
    /// silently return nothing.
    ///
    /// Unchecked throughout means all of them, which is both the default and
    /// what someone with a single collection should never have to think about.
    /// </summary>
    private async Task LoadCollectionsAsync()
    {
        _collections = (await _editionRepo.GetCollectionsAsync())
            .Select(key => (Title: SetupDataSourceCatalog.DescribeCollection(key), Key: key))
            .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _collectionsMenu.Items.Clear();

        // With one collection there is nothing to choose between, and a menu
        // offering the only option is noise.
        _collectionsButton.Visible = _collections.Count > 1;
        if (!_collectionsButton.Visible) return;

        foreach (var (title, key) in _collections)
        {
            var item = new ToolStripMenuItem(title) { CheckOnClick = true, Tag = key };
            item.CheckedChanged += (_, _) => UpdateCollectionsButton();
            _collectionsMenu.Items.Add(item);
        }

        _collectionsMenu.Items.Add(new ToolStripSeparator());
        var all = new ToolStripMenuItem("Search all collections");
        all.Click += (_, _) =>
        {
            foreach (var item in _collectionsMenu.Items.OfType<ToolStripMenuItem>())
                if (item.CheckOnClick) item.Checked = false;
        };
        _collectionsMenu.Items.Add(all);

        ReadingTheme.ApplyToContextMenu(_collectionsMenu);
        UpdateCollectionsButton();
    }

    private void UpdateCollectionsButton()
    {
        var chosen = CheckedCollections().Count;
        _collectionsButton.Text = chosen == 0
            ? "Collections: all"
            : $"Collections: {chosen} of {_collections.Count}";
    }

    private List<string> CheckedCollections() =>
        _collectionsMenu.Items.OfType<ToolStripMenuItem>()
            .Where(i => i.CheckOnClick && i.Checked && i.Tag is string)
            .Select(i => (string)i.Tag!)
            .ToList();

    /// <summary>
    /// Fills the list from the last search's results, in whichever view is
    /// selected. Never queries: the same matches are being shown a different way.
    /// </summary>
    private void RenderResults()
    {
        // A document opened out of a truncated search was re-fetched in full;
        // one opened out of a complete search is already here.
        _visible = _openDocumentWorkId is { } workId
            ? (_openDocumentRows.Count > 0
                ? _openDocumentRows
                : _results.Where(r => r.WorkId == workId).ToList())
            : _results;

        // Already ordered by match count, from the database, over every
        // matching line rather than over the ones that fitted.
        //
        // Except where the aggregate cannot mean what the rows mean. Without
        // a word index, whole-word search is a LIKE prefilter plus a per-row
        // confirmation, and a confirmation cannot run inside a COUNT - so the
        // distribution would be of the prefilter, listing works whose only
        // "match" is the query sitting inside a longer word. Grouping the
        // confirmed rows is the smaller, true answer there, and it is what
        // this screen did before the aggregate existed.
        _documents = !DocumentView
            ? new()
            : _distribution.ExactlyMatchesTheSearch
                ? _distribution.Works
                : _results
                    .GroupBy(r => r.WorkId)
                    .Select(g => (WorkId: g.Key, g.First().AuthorName, g.First().WorkTitle, Matches: (long)g.Count()))
                    .OrderByDescending(d => d.Matches).ThenBy(d => d.AuthorName).ThenBy(d => d.WorkTitle)
                    .ToList();

        var documents = _documents;

        _displayedCount = DocumentView
            ? Math.Min(documents.Count, DisplayLimit)
            : Math.Min(_visible.Count, DisplayLimit);

        _resultsList.BeginUpdate();
        try
        {
            _resultsList.Items.Clear();

            if (DocumentView)
            {
                foreach (var d in documents.Take(DisplayLimit))
                    _resultsList.Items.Add($"{d.AuthorName}, {d.WorkTitle} — {d.Matches:N0} match(es)");
            }
            else
            {
                foreach (var r in _visible.Take(DisplayLimit))
                    _resultsList.Items.Add($"{r.AuthorName}, {r.WorkTitle}: {r.Text}");
            }

            if (_results.Count == 0)
            {
                _resultsList.Items.Add(_narrowed
                    ? "No matches. Try Clear Filters, or a different match mode."
                    : "No matches.");
            }
            else if (DocumentView)
            {
                // The document list is complete however the passage list ended,
                // so its own notice is only ever about the rows on screen.
                if (documents.Count > _displayedCount)
                {
                    _resultsList.Items.Add(
                        $"--- showing {_displayedCount:N0} of {documents.Count:N0} documents. ---");
                }
            }
            else if (_openDocumentWorkId != null)
            {
                // Inside one document the only cap that applies is that
                // document's own - the global one was lifted when it was
                // re-fetched with the work pinned. A work with more matches
                // than the limit can still hit it, and saying so is the point.
                if (_openDocumentTruncated)
                {
                    _resultsList.Items.Add(
                        $"--- showing {_displayedCount:N0} of this document's matches; " +
                        "it has more than one search can return. ---");
                }
                else if (_visible.Count > _displayedCount)
                {
                    _resultsList.Items.Add(
                        $"--- showing {_displayedCount:N0} of {_visible.Count:N0} matches in this document. ---");
                }
            }
            else if (_truncated || _visible.Count > _displayedCount)
            {
                _resultsList.Items.Add(_truncated
                    ? $"--- showing {_displayedCount:N0} passages of {TotalMatchText()}; " +
                      "switch Show to “One row per document” to see where all of them are, " +
                      "or narrow the search to read the rest. ---"
                    : $"--- showing {_displayedCount:N0} of {_visible.Count:N0} matches. ---");
            }
        }
        finally
        {
            _resultsList.EndUpdate();
        }

        // Every row here is the string that was added, which is also what
        // Results_DrawItem draws, so the list can be measured from itself.
        ListResultHelpers.RefreshHorizontalExtent(
            _resultsList, i => _resultsList.Items[i]?.ToString());

        if (_results.Count == 0)
        {
            _statusLabel.Text = "No matches.";
            return;
        }

        if (_openDocumentWorkId != null)
        {
            // Empty is reachable: a document can be listed by a count that
            // does not agree line-for-line with the row query - the LIKE
            // prefilter's does not - and then opening it produces nothing to
            // name. Reading _visible[0] there threw.
            _statusLabel.Text = _visible.Count == 0
                ? "No passages in that document matched after all. " +
                  "Switch Show back to see every document again."
                : $"{_visible.Count:N0} match(es) in {_visible[0].AuthorName}, {_visible[0].WorkTitle}. " +
                  "Switch Show back to see every document again.";
            return;
        }

        // "Counted across the whole library" is only said where it is true.
        var counted = _distribution.ExactlyMatchesTheSearch
            ? ", counted across the whole library"
            : " among the matches that came back - build the word index (Setup Wizard) to count the whole library";

        _statusLabel.Text = DocumentView
            ? $"{TotalMatchText()} across {_documents.Count:N0} document(s)" +
              $"{counted}. Double-click a document to list its matches."
            : $"{TotalMatchText()}. Double-click to open in the reader; " +
              "right-click to copy or export.";
    }

    /// <summary>
    /// The number of matching lines, as a fact about the library rather than
    /// about the result cap.
    ///
    /// The status line used to read "5000+" for anything common, which is all
    /// a capped row query can honestly say. It is also useless to anyone
    /// asking how often a word occurs, and that is most of why someone opens
    /// this window: 6 of 14 ordinary research words - virtus, libertas,
    /// imperium, λόγος, ψυχή, πόλις - answered "5000+".
    ///
    /// Falls back to the old form where the count cannot be trusted to mean
    /// the same thing as the rows, which is the no-word-index whole-word
    /// path, where the confirmation runs per row and an aggregate would be
    /// counting the prefilter.
    /// </summary>
    private string TotalMatchText() =>
        _distribution.ExactlyMatchesTheSearch
            ? $"{_distribution.TotalMatches:N0} match(es)"
            : $"{_displayCount} match(es)";

    private async Task JumpToSelectedAsync()
    {
        var index = _resultsList.SelectedIndex;

        // Bounded by the displayed count, not the result count - the last
        // row can be a notice rather than a result.
        if (index < 0 || index >= _displayedCount) return;

        // In the grouped view a row is a document, and a document has no single
        // passage to open. Double-clicking it asks to see its matches instead.
        if (DocumentView)
        {
            await OpenDocumentAsync(_documents[index].WorkId);
            return;
        }

        if (OnNavigate == null) return;
        var result = _visible[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
    }

    /// <summary>
    /// Scopes the passage list to one work.
    ///
    /// Re-queries when the search was truncated, rather than filtering the
    /// rows already in hand. Those rows are the front of the alphabet, so a
    /// work past the cap has none of them and a work the cap landed inside
    /// has some of them - and "some of them" is the case worth worrying
    /// about, because it looks exactly like all of them. Asking again with
    /// the work pinned lifts the global cap off this one document.
    /// </summary>
    private async Task OpenDocumentAsync(int workId)
    {
        _openDocumentWorkId = workId;

        if (!_truncated || _lastFilters == null)
        {
            RenderResults();
            return;
        }

        _statusLabel.Text = "Fetching this document's matches...";

        // The filters the rows on screen came from, not whatever the boxes say
        // now - a dropdown changed after the search but before the double-click
        // would otherwise fetch one work under different terms from the rest.
        // Pinned and put back rather than copied, since SearchFilters has no
        // clone and this is the only place that needs one.
        var filters = _lastFilters;
        try
        {
            filters.WorkId = workId;
            var hits = await Task.Run(() => _textNodeRepo.SearchFilteredAsync(filters));

            // Kept beside the search's own rows rather than merged into them.
            //
            // Merging changed what the search had found. Export writes out
            // whatever is on screen, so having opened a document and gone
            // back left the same search exporting a different set from the
            // one it would have exported a moment earlier - a result that
            // depended on where the reader had clicked, which is the one
            // thing an exported dataset must not do. _results stays the
            // answer to the query; this is one document, fetched in full.
            _openDocumentRows = hits.Rows;
            _openDocumentTruncated = hits.Truncated;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't fetch that document's matches: {ex.Message}";
            return;
        }
        finally
        {
            filters.WorkId = null;
        }

        RenderResults();
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
            ? FindHighlightSpans(text, _highlightTerms, _highlightWords, _highlightWholeWordsOnly)
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

    /// <summary>
    /// Both ways of finding the query in the line, because both are right for
    /// a different mode.
    ///
    /// The literal pass is what "anywhere in the line" means, and is the only
    /// one that can mark a stem inside a longer word - typing "virt" and
    /// seeing it lit inside "virtutem".
    ///
    /// The word pass is what the whole-word default actually matched on. That
    /// search goes through the index, which folds accents, both sigmas, and
    /// u/v and i/j - so it returns the line accented however its edition
    /// accents it, and the edition printing "justitia" for a query of
    /// "iustitia". A literal IndexOf finds none of those, so the row came
    /// back with nothing marked in it, which reads as the application having
    /// returned a line that does not contain the word.
    ///
    /// Overlaps between the two are harmless: the draw loop walks the spans
    /// in order and skips any that starts inside the one before it.
    /// </summary>
    private static List<(int Start, int Length)> FindHighlightSpans(
        string text, List<string> terms, IReadOnlyCollection<string> wordTargets, bool wholeWordsOnly)
    {
        var spans = new List<(int Start, int Length)>();

        // Whole-word mode gets the word pass alone. Running both marked the
        // letters of the query inside longer words - searching "arm" and
        // seeing "harm" lit up in a line that matched on a real "arm"
        // elsewhere - which contradicts the mode the reader chose and makes
        // the highlighting look like the search had done something it had
        // not. The substring modes get the literal pass, which is the only
        // one that can mark a stem inside a word, which is their whole point.
        if (!wholeWordsOnly)
        {
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
        }

        spans.AddRange(WordOccurrences.Find(text, wordTargets));

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }
}
