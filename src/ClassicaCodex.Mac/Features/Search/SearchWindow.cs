using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;
using ClassicaCodex.Mac.Features.Export;

namespace ClassicaCodex.Mac.Features.Search;

/// <summary>What a row of the results list is.</summary>
internal enum ResultRowKind { Passage, Document, Notice }

/// <summary>
/// One row of the results list: a passage, a document in the grouped view,
/// or a notice about the rows above it. <see cref="Index"/> points into
/// whichever list the row was drawn from, and is -1 for a notice, which is
/// not a result and must not be opened, copied or highlighted as one.
/// </summary>
internal sealed record ResultRow(ResultRowKind Kind, int Index, string Lead, string Text, string? Citation = null);

/// <summary>
/// Search, given room to be a real tool rather than a text box wedged into
/// the toolbar - the Windows SearchForm, filter for filter.
///
/// Everything here that isn't the box itself exists because there was no
/// room for it in the old inline search: narrowing by language, corpus,
/// author, era, or your own tags; choosing how the words should match; and
/// getting the whole result set out through Export rather than one line at
/// a time.
///
/// Modeless on purpose. A search window you have to close before you can
/// look at what it found is a worse search window - this one stays open
/// beside the reader, and double-clicking a result moves the reader behind
/// it.
/// </summary>
public sealed class SearchWindow : FeatureWindow
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
    private readonly List<(string Key, string Title, CheckBox Box)> _collectionBoxes = new();
    private IDisposable? _collectionsAccent;
    private readonly ToggleButton _passagesTab;
    private readonly ToggleButton _documentsTab;
    private readonly Button _backToDocuments;
    private readonly ListBox _resultsList;
    private readonly StackPanel _emptyState;
    private readonly TextBlock _emptyMessage;
    private readonly TextBlock _statusLabel;
    private readonly ComboBox _recentBox;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly TagRepository _tagRepo = new();
    private readonly RecentSearchRepository _recentRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone)> _results = new();

    /// <summary>
    /// The passages actually on screen - every result, or one document's worth
    /// when a document row has been opened. Everything that indexes the list by
    /// row reads this rather than <see cref="_results"/>.
    /// </summary>
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone)> _visible = new();

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
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone)> _openDocumentRows = new();

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

    /// <summary>0 for every matching passage, 1 for one row per document.</summary>
    private int _viewIndex;

    private bool DocumentView => _viewIndex == 1 && _openDocumentWorkId == null;

    private List<string> _highlightTerms = new();

    /// <summary>
    /// The normalized spellings the word index was actually asked for, so the
    /// highlighter can mark the word in a line the literal query does not
    /// appear in - see ResultText.HighlightSpans.
    /// </summary>
    private HashSet<string> _highlightWords = new(StringComparer.Ordinal);

    /// <summary>Whether the search that produced these rows matched whole words.</summary>
    private bool _highlightWholeWordsOnly;

    /// <summary>The words the last search was looking for, to place each row's window on its match.</summary>
    private List<string> _displayTerms = new();

    private List<Author> _authors = new();
    private List<RecentSearch> _recent = new();
    private List<ResultRow> _rows = new();

    // Set while a recent search is being restored into the controls, so the
    // picker's own selection change doesn't re-enter and run again.
    private bool _applyingRecent;
    private int _displayedCount;
    private bool _searching;

    /// <summary>
    /// How many result rows are put in the list.
    ///
    /// Windows paints 500, because its list owner-draws every row to highlight
    /// the matched words and that is a rendering budget. Avalonia's list
    /// realises only the rows on screen, so here the budget is the search's
    /// own cap and every row the search returned can be scrolled to. The
    /// notices below still say so whenever it bites; the status line reports
    /// the real total either way.
    /// </summary>
    private const int DisplayLimit = TextNodeRepository.DefaultMaxResults;

    private const string DefaultStatus = "Type a word and press Return. Double-click a result to open it in the reader.";

    private const string EmptyIntro = "Every passage, in every edition.";

    public SearchWindow()
    {
        Title = "Search";
        Width = 1140;
        Height = 740;
        MinWidth = 820;
        MinHeight = 520;

        // --- query row -------------------------------------------------
        _queryBox = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "A word or phrase - see Match for how it's compared",
            FontSize = 14,
            MinHeight = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Search"), Width = 14, Height = 14, Margin = new Thickness(10, 0, 2, 0), Opacity = 0.6 }
        };
        _queryBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await RunSearchAsync();
        };

        _searchButton = new Button { Content = "Search", Classes = { "accent" }, MinWidth = 92, Height = 32, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        var clearFiltersButton = new Button { Content = "Clear Filters", Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        clearFiltersButton.Click += (_, _) => ClearFilters();

        var queryRow = new DockPanel();
        var queryButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0) };
        queryButtons.Children.Add(_searchButton);
        queryButtons.Children.Add(clearFiltersButton);
        DockPanel.SetDock(queryButtons, Dock.Right);
        queryRow.Children.Add(queryButtons);
        queryRow.Children.Add(_queryBox);

        // --- recent searches -------------------------------------------
        _recentBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 360 };
        _recentBox.SelectionChanged += async (_, _) => await ApplySelectedRecentAsync();

        var recentRow = new DockPanel();
        var recentLabel = FieldLabel("Recent");
        recentLabel.Width = 52;
        DockPanel.SetDock(recentLabel, Dock.Left);
        recentRow.Children.Add(recentLabel);
        recentRow.Children.Add(_recentBox);

        // --- filter panel ----------------------------------------------
        _matchModeBox = Combo(175, "Anywhere in the line", "Whole words only", "All words, any order");

        // Whole words, not "anywhere in the line", and this is a correctness
        // choice rather than a speed one.
        //
        // "Anywhere" compares a LIKE pattern against the raw text. Greek is
        // written with diacritics, editions disagree about which, and nobody
        // types them into a search box - so the pattern contains characters the
        // text does not have and the search quietly misses most of what it was
        // asked for. Searching the full library for a bare "μηνιν" returns 8
        // lines in that mode and 316 in this one. With the accents typed
        // correctly it is still 303 against 316, because whole-word goes
        // through an index that normalises across editions and a LIKE pattern
        // cannot.
        //
        // Latin has the same problem for a different reason. u/v and i/j were
        // one letter each, editors disagree about which glyph to print, and
        // only the index folds them - so "anywhere" finds "iustitia" 1,425
        // times where whole-word finds it 4,196. That falls the wrong way
        // round: the spellings it is worst at are the ones a reader was taught.
        //
        // Nothing on screen says any of that. Someone reads "8 results" as a
        // fact about Homer rather than about the match mode, which is the worst
        // shape a wrong answer can take. It is also about 250x faster, since it
        // seeks the word index instead of reading the text, but that is the
        // smaller half. "Anywhere" stays one item up the list for anyone
        // hunting a stem or a fragment, which is what it is genuinely good for.
        //
        // Safe when the index has not been built: the repository checks and
        // falls back to a LIKE prefilter with a normalised confirmation, which
        // still rejects substrings correctly, just without the accent folding.
        _matchModeBox.SelectedIndex = 1;

        _greekCheck = new CheckBox { Content = "Greek" };
        _latinCheck = new CheckBox { Content = "Latin" };
        _englishCheck = new CheckBox { Content = "English" };
        var languages = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        languages.Children.Add(_greekCheck);
        languages.Children.Add(_latinCheck);
        languages.Children.Add(_englishCheck);

        _kindBox = Combo(200, "Originals and translations", "Originals only", "Translations only");
        _kindBox.SelectedIndex = 0;

        _bookmarkedCheck = new CheckBox { Content = "Bookmarked passages only" };

        _authorBox = Combo(230);
        _eraBox = new ComboBox { Width = 240, ItemsSource = SearchEras.All.ToList(), SelectedIndex = 0, MaxDropDownHeight = 420 };
        _tagBox = Combo(170);

        // A menu rather than a row of checkboxes: the number of collections grows
        // every time a corpus is added, and a filter panel that has to grow with
        // it would push the results further down the window each time.
        _collectionsButton = new Button { Content = CollectionsButtonContent("Collections: all"), IsVisible = false };
        _collectionsButton.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(_collectionsButton);

        var firstFilters = new WrapPanel { Orientation = Orientation.Horizontal };
        firstFilters.Children.Add(Labeled("Match", _matchModeBox));
        firstFilters.Children.Add(Labeled("Language", languages));
        firstFilters.Children.Add(Labeled("Text", _kindBox));
        firstFilters.Children.Add(Spaced(_bookmarkedCheck));

        var secondFilters = new WrapPanel { Orientation = Orientation.Horizontal };
        secondFilters.Children.Add(Labeled("Author", _authorBox));
        secondFilters.Children.Add(Labeled("Era", _eraBox));
        secondFilters.Children.Add(Labeled("Tagged", _tagBox));
        secondFilters.Children.Add(Spaced(_collectionsButton));

        var filterStack = new StackPanel { Spacing = 2 };
        filterStack.Children.Add(new TextBlock { Text = "NARROW THE SEARCH", Classes = { "section" }, Margin = new Thickness(0, 0, 0, 8) });
        filterStack.Children.Add(firstFilters);
        filterStack.Children.Add(secondFilters);

        var filterPanel = new Border { Classes = { "panel" }, Padding = new Thickness(14, 10, 14, 4), Child = filterStack };

        // A view control, not a filter - it changes how the same matches are
        // presented, so it sits with the results rather than inside "Narrow
        // the search".
        _passagesTab = SegmentButton("Every matching passage", "Rows");
        _documentsTab = SegmentButton("One row per document", "ListBullets");
        _passagesTab.IsChecked = true;
        _passagesTab.Click += (_, _) => SetView(0);
        _documentsTab.Click += (_, _) => SetView(1);

        var segment = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Child = new UniformGrid { Columns = 2, Children = { _passagesTab, _documentsTab } }
        };
        segment.Bind(Border.BackgroundProperty, segment.GetResourceObservable("Cc.Hover"));

        _backToDocuments = new Button { Classes = { "link" }, IsVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        _backToDocuments.Content = IconText("Back", "All documents", 12);
        _backToDocuments.Click += (_, _) => SetView(1);

        var exportButton = new Button { Classes = { "toolbar" }, Content = IconText("Export", "Export…", 14) };
        ToolTip.SetTip(exportButton, "Export every passage on screen to a document");
        exportButton.Click += async (_, _) => await ExportAsync();

        var viewRow = new DockPanel();
        var showLabel = FieldLabel("Show");
        DockPanel.SetDock(showLabel, Dock.Left);
        DockPanel.SetDock(segment, Dock.Left);
        DockPanel.SetDock(_backToDocuments, Dock.Left);
        DockPanel.SetDock(exportButton, Dock.Right);
        viewRow.Children.Add(showLabel);
        viewRow.Children.Add(segment);
        viewRow.Children.Add(_backToDocuments);
        viewRow.Children.Add(exportButton);
        viewRow.Children.Add(new Panel());

        // --- results ---------------------------------------------------
        _resultsList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4, 6),
            ItemTemplate = new FuncDataTemplate<ResultRow>((row, _) => BuildRow(row), supportsRecycling: false)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_resultsList, ScrollBarVisibility.Disabled);

        // Fluent pads a list item for touch; a result list reads better set
        // close, like the reader's own lines, with the row supplying its inset.
        _resultsList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.MinHeightProperty, 0d),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0, 1))
            }
        });
        _resultsList.DoubleTapped += async (_, _) => await JumpToSelectedAsync();

        // On the way down rather than the way up: the list's own keyboard
        // handling would otherwise take Return before it reached this.
        _resultsList.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedAsync();
            }
            else if (e.Key == Key.C && Command(e))
            {
                e.Handled = true;
                await CopySelectedAsync();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _resultsList.ContextRequested += OnResultsContextRequested;
        _resultsList.ContextMenu = BuildResultsMenu();

        _emptyMessage = new TextBlock
        {
            Text = EmptyIntro,
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var emptyTile = Art.Tile("search", "Search", 56);
        emptyTile.HorizontalAlignment = HorizontalAlignment.Center;
        emptyTile.Opacity = 0.85;
        _emptyState = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Children = { emptyTile, _emptyMessage }
        };

        var resultsBody = new Grid();
        resultsBody.Children.Add(_resultsList);
        resultsBody.Children.Add(_emptyState);
        var resultsPage = new Border { Classes = { "page" }, Child = resultsBody };

        _statusLabel = new TextBlock
        {
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 10, 4, 0),
            Text = DefaultStatus
        };

        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(queryRow);
        header.Children.Add(recentRow);
        header.Children.Add(filterPanel);
        header.Children.Add(viewRow);

        var layout = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_statusLabel, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(_statusLabel);
        layout.Children.Add(resultsPage);
        Content = layout;

        KeyDown += (_, e) =>
        {
            // ⌘F in a window that is already the search window puts the caret
            // in its box, as every Mac app with a search field does.
            if (e.Key == Key.F && Command(e))
            {
                FocusQuery();
                e.Handled = true;
            }
        };

        Opened += async (_, _) =>
        {
            FocusQuery();
            await LoadFilterOptionsAsync();
            await LoadRecentAsync();
        };
    }

    /// <summary>Puts the caret in the search box with what it holds selected, ready to be typed over.</summary>
    public void FocusQuery()
    {
        _queryBox.Focus();
        _queryBox.SelectAll();
    }

    // For the tests, which drive the window as a reader would.
    internal TextBox QueryBox => _queryBox;
    internal ComboBox MatchModeBox => _matchModeBox;
    internal ComboBox RecentBox => _recentBox;
    internal CheckBox LatinCheck => _latinCheck;
    internal CheckBox GreekCheck => _greekCheck;
    internal ComboBox AuthorBox => _authorBox;
    internal Button CollectionsButton => _collectionsButton;
    internal IReadOnlyList<ResultRow> Rows => _rows;
    internal ListBox ResultsList => _resultsList;
    internal string EmptyMessage => _emptyState.IsVisible ? _emptyMessage.Text ?? string.Empty : string.Empty;
    internal string StatusText => _statusLabel.Text ?? string.Empty;
    internal string Description => DescribeSearch();
    internal IReadOnlyList<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone)> VisiblePassages => _visible;
    internal IReadOnlyList<(int WorkId, string AuthorName, string WorkTitle, long Matches)> Documents => _documents;
    internal SearchDistribution Distribution => _distribution;
    internal bool Truncated => _truncated;

    internal void SelectRow(int index) => _resultsList.SelectedIndex = index;

    internal Task OpenSelectedAsync() => JumpToSelectedAsync();

    internal string? CopyTextAt(int index) => FullTextAt(index);

    private static bool Command(KeyEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

    private static TextBlock FieldLabel(string text) =>
        new() { Text = text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

    private static Control Labeled(string label, Control control)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 8) };
        panel.Children.Add(FieldLabel(label));
        control.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(control);
        return panel;
    }

    private static Control Spaced(Control control)
    {
        control.Margin = new Thickness(0, 0, 20, 8);
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static ComboBox Combo(double width, params string[] items) =>
        new() { Width = width, ItemsSource = items.ToList(), MaxDropDownHeight = 420 };

    private static StackPanel IconText(string glyph, string text, double size)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Glyphs.Icon(glyph, size));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private static StackPanel CollectionsButtonContent(string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Glyphs.Icon("Filter", 13));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(Glyphs.Icon("ChevronDown", 11));
        return panel;
    }

    private static ToggleButton SegmentButton(string label, string glyph)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new PathIcon { Data = Glyphs.Get(glyph), Width = 13, Height = 13 });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

        return new ToggleButton
        {
            Content = content,
            Classes = { "segment" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 4),
            CornerRadius = new CornerRadius(6)
        };
    }

    /// <summary>
    /// Changes how the same matches are shown. Choosing either view always
    /// leaves a single opened document - including choosing the view already
    /// showing - so the results never disagree with the control describing
    /// them.
    /// </summary>
    internal void SetView(int index)
    {
        _viewIndex = index;
        _passagesTab.IsChecked = index == 0;
        _documentsTab.IsChecked = index == 1;
        _openDocumentWorkId = null;
        _openDocumentRows = new();
        RenderResults();
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
            _authorBox.ItemsSource = new[] { "(any author)" }.Concat(_authors.Select(a => a.Name)).ToList();
            _authorBox.SelectedIndex = 0;

            await LoadCollectionsAsync();

            var tags = await _tagRepo.GetAllTagsAsync();
            _tagBox.ItemsSource = new[] { "(any)" }.Concat(tags.Select(t => t.Name)).ToList();
            _tagBox.SelectedIndex = 0;

            if (tags.Count == 0)
            {
                _tagBox.IsEnabled = false;
                ToolTip.SetTip(_tagBox, "You haven't tagged anything yet.");
                ToolTip.SetShowOnDisabled(_tagBox, true);
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
            _recentBox.ItemsSource = new[] { _recent.Count == 0 ? "(no recent searches yet)" : "(pick a recent search)" }
                .Concat(_recent.Select(r => r.Name))
                .ToList();
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
            _greekCheck.IsChecked = languages.Contains("grc");
            _latinCheck.IsChecked = languages.Contains("lat");
            _englishCheck.IsChecked = languages.Contains("eng");

            _kindBox.SelectedIndex = recent.OriginalsOnly switch { true => 1, false => 2, null => 0 };
            _bookmarkedCheck.IsChecked = recent.BookmarkedOnly;

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
        if (combo.ItemCount == 0) return;

        if (!string.IsNullOrEmpty(text))
        {
            for (var i = 0; i < combo.ItemCount; i++)
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
    /// A key naming a collection no longer installed has no box and so is
    /// simply dropped, on the same principle as the author picker: the entry
    /// finds less than it once did rather than something different.
    /// </summary>
    private void SelectCollections(string keys)
    {
        var wanted = keys.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, _, box) in _collectionBoxes) box.IsChecked = wanted.Contains(key);
        UpdateCollectionsButton();
    }

    /// <summary>
    /// Files the search that just ran into the recent list.
    ///
    /// Its description is its identity - DescribeSearch already renders the
    /// query and every active filter, and the table's unique constraint on
    /// that string is what makes re-running a search move it up the list
    /// instead of adding a duplicate.
    ///
    /// Failing to record is not worth an alert: the search itself succeeded,
    /// and the results are on screen.
    /// </summary>
    private async Task RecordCurrentAsync()
    {
        var languages = new List<string>();
        if (_greekCheck.IsChecked == true) languages.Add("grc");
        if (_latinCheck.IsChecked == true) languages.Add("lat");
        if (_englishCheck.IsChecked == true) languages.Add("eng");

        var entry = new RecentSearch
        {
            Name = DescribeSearch(),
            Query = (_queryBox.Text ?? string.Empty).Trim(),
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
            BookmarkedOnly = _bookmarkedCheck.IsChecked == true,
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
        if (_recentBox.ItemCount > 0) _recentBox.SelectedIndex = 0;
        _applyingRecent = false;

        // Back to the same default the window opens on - whole words - not to
        // the first item in the list. See the note where that default is set.
        _matchModeBox.SelectedIndex = 1;
        _greekCheck.IsChecked = false;
        _latinCheck.IsChecked = false;
        _englishCheck.IsChecked = false;
        _kindBox.SelectedIndex = 0;
        _bookmarkedCheck.IsChecked = false;
        if (_authorBox.ItemCount > 0) _authorBox.SelectedIndex = 0;
        if (_tagBox.ItemCount > 0) _tagBox.SelectedIndex = 0;
        _eraBox.SelectedIndex = 0;

        foreach (var (_, _, box) in _collectionBoxes) box.IsChecked = false;
        UpdateCollectionsButton();
    }

    private SearchFilters BuildFilters()
    {
        var filters = new SearchFilters
        {
            Query = (_queryBox.Text ?? string.Empty).Trim(),
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
            BookmarkedOnly = _bookmarkedCheck.IsChecked == true
        };

        if (_greekCheck.IsChecked == true) filters.Languages.Add("grc");
        if (_latinCheck.IsChecked == true) filters.Languages.Add("lat");
        if (_englishCheck.IsChecked == true) filters.Languages.Add("eng");

        foreach (var key in CheckedCollections()) filters.Collections.Add(key);

        if (_authorBox.SelectedIndex > 0 && _authorBox.SelectedIndex - 1 < _authors.Count)
        {
            filters.AuthorId = _authors[_authorBox.SelectedIndex - 1].AuthorId;
        }

        if (_tagBox.SelectedIndex > 0)
        {
            filters.TagName = _tagBox.SelectedItem?.ToString();
        }

        // Author dates live in a curated table in Core, not in the database,
        // so the era becomes a list of author ids here rather than a clause
        // the repository could write for itself.
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

    internal async Task RunSearchAsync()
    {
        if (_searching) return;

        var filters = BuildFilters();
        if (filters.Query.Length == 0)
        {
            _statusLabel.Text = "Type something to search for.";
            return;
        }

        _searching = true;
        _searchButton.IsEnabled = false;
        _statusLabel.Text = "Searching...";
        Cursor = new Cursor(StandardCursorType.Wait);

        try
        {
            _highlightTerms = filters.Query
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 0)
                .ToList();

            _highlightWords = WordOccurrences.TargetsFor(filters.Query);
            _highlightWholeWordsOnly = filters.MatchMode == SearchMatchMode.WholeWord;

            // Task.Run, because the awaits inside it are not real ones. SQLite
            // has no asynchronous I/O, and Microsoft.Data.Sqlite's Async
            // methods run to completion synchronously and hand back a task
            // already finished - so awaited on the UI thread, "Searching..."
            // never painted and the window sat frozen for however long the
            // query took, which for a substring match across the whole text is
            // seconds. Everything after the await is back on the UI thread.
            //
            // Both queries on the one background hop. The distribution is what
            // makes the per-document view and the match total true rather than
            // true of whatever the cap left behind, and running it here means
            // no later interaction has to wonder whether it has been computed.
            // It reads the index instead of the text, so it costs a fraction of
            // the search it accompanies.
            var (hits, distribution) = await Task.Run(async () => (
                await _textNodeRepo.SearchFilteredAsync(filters),
                await _textNodeRepo.CountMatchesByWorkAsync(filters)));

            _results = hits.Rows;
            _distribution = distribution;
            _lastFilters = filters;
            _displayTerms = SearchTermsForDisplay();
            _openDocumentWorkId = null;
            _openDocumentRows = new();
            _openDocumentTruncated = false;
            _truncated = hits.Truncated;
            _displayCount = hits.DisplayCount;
            _narrowed = filters.HasAnyNarrowing;
            RenderResults();

            // Recorded at the end rather than the start: a search that threw
            // isn't one worth offering back. Replaying an entry from the list
            // records it too, which is what moves it to the top - a recent
            // list should reflect what was actually run most recently,
            // however it was launched.
            await RecordCurrentAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            _searching = false;
            _searchButton.IsEnabled = true;
            Cursor = null;
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
        // words is the default, so "anywhere" is the one worth recording -
        // and it has to be recorded, because this description is also the
        // recent list's identity and the two modes can return very different
        // result sets for the same word.
        if (_matchModeBox.SelectedIndex == 0) parts.Add("anywhere in the line");
        if (_matchModeBox.SelectedIndex == 2) parts.Add("all words");

        var languages = new List<string>();
        if (_greekCheck.IsChecked == true) languages.Add("Greek");
        if (_latinCheck.IsChecked == true) languages.Add("Latin");
        if (_englishCheck.IsChecked == true) languages.Add("English");
        if (languages.Count > 0) parts.Add(string.Join("/", languages));

        if (_kindBox.SelectedIndex == 1) parts.Add("originals only");
        if (_kindBox.SelectedIndex == 2) parts.Add("translations only");
        if (_authorBox.SelectedIndex > 0) parts.Add(_authorBox.SelectedItem?.ToString() ?? string.Empty);
        if (_eraBox.SelectedIndex > 0) parts.Add(_eraBox.SelectedItem?.ToString() ?? string.Empty);
        if (_tagBox.SelectedIndex > 0) parts.Add($"tagged “{_tagBox.SelectedItem}”");
        if (_bookmarkedCheck.IsChecked == true) parts.Add("bookmarked");

        // The description is also the recent list's identity, so the collections
        // have to appear in it. Without them "wrath" across the whole library and
        // "wrath" in CSEL are one name, and the second run overwrites the first.
        var chosen = CheckedCollections();
        if (chosen.Count > 0)
            parts.Add("in " + string.Join("/", chosen.Select(SetupDataSourceCatalog.DescribeCollection)));

        var query = (_queryBox.Text ?? string.Empty).Trim();
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
        var collections = (await _editionRepo.GetCollectionsAsync())
            .Select(key => (Title: SetupDataSourceCatalog.DescribeCollection(key), Key: key))
            .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _collectionBoxes.Clear();

        // With one collection there is nothing to choose between, and a menu
        // offering the only option is noise.
        _collectionsButton.IsVisible = collections.Count > 1;
        if (!_collectionsButton.IsVisible) return;

        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "SEARCH IN", Classes = { "section" }, Margin = new Thickness(4, 2, 4, 6) });

        foreach (var (title, key) in collections)
        {
            var box = new CheckBox { Content = title };
            box.IsCheckedChanged += (_, _) => UpdateCollectionsButton();
            _collectionBoxes.Add((key, title, box));
            panel.Children.Add(box);
        }

        var all = new Button { Content = "Search all collections", Classes = { "link" }, Margin = new Thickness(4, 8, 4, 2) };
        all.Click += (_, _) =>
        {
            foreach (var (_, _, box) in _collectionBoxes) box.IsChecked = false;
        };
        panel.Children.Add(all);

        FlyoutBase.SetAttachedFlyout(_collectionsButton, new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft });
        UpdateCollectionsButton();
    }

    private void UpdateCollectionsButton()
    {
        var chosen = CheckedCollections().Count;
        _collectionsButton.Content = CollectionsButtonContent(chosen == 0
            ? "Collections: all"
            : $"Collections: {chosen} of {_collectionBoxes.Count}");

        // A filter you cannot see is one you forget you set. Bound, not looked
        // up: a lookup naming no appearance never reaches the palette's light
        // and dark dictionaries.
        _collectionsAccent?.Dispose();
        _collectionsAccent = chosen == 0
            ? null
            : _collectionsButton.Bind(Button.ForegroundProperty, _collectionsButton.GetResourceObservable("Cc.Accent"));
    }

    private List<string> CheckedCollections() =>
        _collectionBoxes.Where(c => c.Box.IsChecked == true).Select(c => c.Key).ToList();

    /// <summary>
    /// The words the last search was actually looking for, used only to decide
    /// which part of a long passage to show. Whole-word and all-words searches
    /// match on individual words, so the query is split; a contains search is
    /// looking for the string as typed.
    /// </summary>
    private List<string> SearchTermsForDisplay()
    {
        var query = _lastFilters?.Query?.Trim();
        if (string.IsNullOrEmpty(query)) return new List<string>();

        if (_lastFilters!.MatchMode == SearchMatchMode.Contains)
            return new List<string> { query };

        return query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1)
            .OrderByDescending(w => w.Length)
            .ToList();
    }

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

        _displayedCount = DocumentView
            ? Math.Min(_documents.Count, DisplayLimit)
            : Math.Min(_visible.Count, DisplayLimit);

        var rows = new List<ResultRow>(_displayedCount + 1);

        if (DocumentView)
        {
            for (var i = 0; i < _displayedCount; i++)
            {
                var d = _documents[i];
                rows.Add(new ResultRow(ResultRowKind.Document, i, $"{d.AuthorName}, {d.WorkTitle}", $"{d.Matches:N0} match(es)"));
            }
        }
        else
        {
            for (var i = 0; i < _displayedCount; i++)
            {
                var r = _visible[i];

                // Position the row's window on the match rather than on the
                // start of the passage. Much of this corpus is ingested in
                // whole sections, so a hit thousands of characters in used to
                // show the opening of the section and no sign of what matched -
                // a result the reader has to open the passage to make sense of.
                var (start, _) = ResultText.FirstMatch(r.Text, _displayTerms, _highlightWords);
                rows.Add(new ResultRow(ResultRowKind.Passage, i, $"{r.AuthorName}, {r.WorkTitle}", ResultText.RowAround(r.Text, start),
                    PassageCitation.Bracketed(r.CitationRef, r.Milestone)));
            }
        }

        string? notice = null;
        if (_results.Count == 0)
        {
            // Nothing to list; the empty state says so instead.
        }
        else if (DocumentView)
        {
            // The document list is complete however the passage list ended,
            // so its own notice is only ever about the rows on screen.
            if (_documents.Count > _displayedCount)
                notice = $"Showing {_displayedCount:N0} of {_documents.Count:N0} documents.";
        }
        else if (_openDocumentWorkId != null)
        {
            // Inside one document the only cap that applies is that
            // document's own - the global one was lifted when it was
            // re-fetched with the work pinned. A work with more matches
            // than the limit can still hit it, and saying so is the point.
            if (_openDocumentTruncated)
                notice = $"Showing {_displayedCount:N0} of this document's matches; it has more than one search can return.";
            else if (_visible.Count > _displayedCount)
                notice = $"Showing {_displayedCount:N0} of {_visible.Count:N0} matches in this document.";
        }
        else if (_truncated || _visible.Count > _displayedCount)
        {
            notice = _truncated
                ? $"Showing {_displayedCount:N0} passages of {TotalMatchText()}; " +
                  "switch Show to “One row per document” to see where all of them are, " +
                  "or narrow the search to read the rest."
                : $"Showing {_displayedCount:N0} of {_visible.Count:N0} matches.";
        }

        if (notice != null) rows.Add(new ResultRow(ResultRowKind.Notice, -1, string.Empty, notice));

        _rows = rows;
        _resultsList.ItemsSource = rows;
        _backToDocuments.IsVisible = _openDocumentWorkId != null;

        if (_lastFilters == null)
        {
            ShowEmpty(EmptyIntro);
            return;
        }

        if (_results.Count == 0)
        {
            ShowEmpty(_narrowed
                ? "No matches. Try Clear Filters, or a different match mode."
                : "No matches.");
            _statusLabel.Text = "No matches.";
            return;
        }

        _emptyState.IsVisible = false;

        if (_openDocumentWorkId != null)
        {
            // Empty is reachable: a document can be listed by a count that
            // does not agree line-for-line with the row query - the LIKE
            // prefilter's does not - and then opening it produces nothing to
            // name. Reading _visible[0] there threw on Windows.
            _statusLabel.Text = _visible.Count == 0
                ? "No passages in that document matched after all. " +
                  "Switch Show back to see every document again."
                : $"{_visible.Count:N0} match(es) in {_visible[0].AuthorName}, {_visible[0].WorkTitle}. " +
                  "Switch Show back to see every document again.";
            if (_visible.Count == 0) ShowEmpty("No passages in that document matched after all.");
            return;
        }

        // "Counted across the whole library" is only said where it is true.
        var counted = _distribution.ExactlyMatchesTheSearch
            ? ", counted across the whole library"
            : " among the matches that came back - build the word index (Setup, in the File menu) to count the whole library";

        _statusLabel.Text = DocumentView
            ? $"{TotalMatchText()} across {_documents.Count:N0} document(s)" +
              $"{counted}. Double-click a document to list its matches."
            : $"{TotalMatchText()}. Double-click to open in the reader; " +
              "right-click to copy or export.";
    }

    private void ShowEmpty(string message)
    {
        _emptyMessage.Text = message;
        _emptyState.IsVisible = true;
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

        // Bounded by the displayed count, not the row count - the last row
        // can be a notice rather than a result.
        if (index < 0 || index >= _displayedCount) return;

        // In the grouped view a row is a document, and a document has no single
        // passage to open. Double-clicking it asks to see its matches instead.
        if (DocumentView)
        {
            await OpenDocumentAsync(_documents[index].WorkId);
            return;
        }

        // While one document's matches are being fetched the list still holds
        // the document rows, so a row number need not be a passage number yet.
        if (index >= _visible.Count) return;

        var result = _visible[index];
        await NavigateAsync(result.WorkId, result.TextNodeId);
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
    internal async Task OpenDocumentAsync(int workId)
    {
        _openDocumentWorkId = workId;

        if (!_truncated || _lastFilters == null)
        {
            RenderResults();
            return;
        }

        _statusLabel.Text = "Fetching this document's matches...";

        // The filters the rows on screen came from, not whatever the boxes say
        // now - a picker changed after the search but before the double-click
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
    /// The full line for a row - author, work, citation and the whole passage,
    /// not the window the row shows - or a document's name and count. Null
    /// for a notice, which is not a result.
    /// </summary>
    private string? FullTextAt(int index)
    {
        if (index < 0 || index >= _displayedCount) return null;

        if (DocumentView)
        {
            var d = _documents[index];
            return $"{d.AuthorName}, {d.WorkTitle} — {d.Matches} match(es)";
        }

        if (index >= _visible.Count) return null;

        var r = _visible[index];
        return $"{r.AuthorName}, {r.WorkTitle} [{PassageCitation.Display(r.CitationRef, r.Milestone)}]: {r.Text}";
    }

    private async Task CopySelectedAsync()
    {
        if (FullTextAt(_resultsList.SelectedIndex) is { Length: > 0 } text) await ClipboardText.SetAsync(this, text);
    }

    /// <summary>
    /// Every passage on screen, as a document. Passage-level in both views: a
    /// list of documents and their counts is not something anyone wants to
    /// paste into a notebook, and the export is scoped to what is on screen,
    /// so opening a document exports that document.
    ///
    /// Exporting the backing list rather than the rows means the notice row
    /// is structurally incapable of ending up in the output.
    /// </summary>
    private async Task ExportAsync()
    {
        var passages = _visible
            .Select(r => new ExportPassage(r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text, Milestone: r.Milestone))
            .ToList();

        ExportFeature.ExportPassagesFrom(this, DescribeSearch(), passages);
        await Task.CompletedTask;
    }

    private ContextMenu BuildResultsMenu()
    {
        var menu = new ContextMenu();

        var copy = new MenuItem { Header = "Copy", Icon = Glyphs.Icon("Copy", 14), InputGesture = new KeyGesture(Key.C, KeyModifiers.Meta) };
        copy.Click += async (_, _) => await CopySelectedAsync();

        var open = new MenuItem { Header = "Open in Reader", Icon = Glyphs.Icon("OpenExternal", 14) };
        open.Click += async (_, _) => await JumpToSelectedAsync();

        var export = new MenuItem { Header = "Export All Passages…", Icon = Glyphs.Icon("Export", 14) };
        export.Click += async (_, _) => await ExportAsync();

        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(export);

        menu.Opening += (_, _) =>
        {
            var index = _resultsList.SelectedIndex;
            var isResult = index >= 0 && index < _displayedCount;
            copy.IsEnabled = isResult;
            open.IsEnabled = isResult;
            open.Header = DocumentView ? "List This Document's Matches" : "Open in Reader";
            export.IsEnabled = _visible.Count > 0;
        };

        return menu;
    }

    /// <summary>
    /// Selects the row under the pointer before the menu opens, as a
    /// right-click on a Mac list does - otherwise the menu would act on
    /// whatever was last clicked rather than on what was right-clicked.
    /// </summary>
    private void OnResultsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (item != null && _resultsList.IndexFromContainer(item) is var index and >= 0)
            _resultsList.SelectedIndex = index;
    }

    /// <summary>
    /// The visual for one row. A passage is its source, muted, then the window
    /// onto its text in the reading face - with the script's own font put in
    /// front where the text needs one - and the matched words marked, the
    /// treatment the old inline search results had, which is the part of it
    /// worth keeping. The citation is on hover rather than in the row, the
    /// way the reader keeps it.
    /// </summary>
    private Control BuildRow(ResultRow? row)
    {
        // The list hands its template a null while it clears a container
        // for reuse; there is nothing to draw for it.
        if (row == null) return new Panel();

        switch (row.Kind)
        {
            case ResultRowKind.Document:
            {
                var title = new TextBlock { Text = row.Lead, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                var count = new TextBlock { Text = row.Text, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
                var chevron = new PathIcon { Data = Glyphs.Get("ChevronRight"), Width = 11, Height = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };

                var dock = new DockPanel { Margin = new Thickness(10, 8) };
                DockPanel.SetDock(chevron, Dock.Right);
                DockPanel.SetDock(count, Dock.Right);
                dock.Children.Add(chevron);
                dock.Children.Add(count);
                dock.Children.Add(title);
                return dock;
            }

            case ResultRowKind.Notice:
                return new TextBlock
                {
                    Text = row.Text,
                    Classes = { "caption" },
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 10)
                };
        }

        var reading = (FontFamily)this.FindResource("Cc.SourceFont")!;
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            FontSize = 14,
            LineHeight = 20,
            FontFamily = ScriptFonts.Compose(reading, ScriptFonts.FamilyFor(row.Text))
        };

        var lead = new Run(row.Lead + ":  ") { FontFamily = FontFamily.Default, FontSize = 12.5, FontWeight = FontWeight.SemiBold };
        lead.Bind(TextElement.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
        text.Inlines!.Add(lead);

        var highlight = text.GetResourceObservable("Cc.Gold", HighlightBrush);
        var pos = 0;
        foreach (var (start, length) in ResultText.HighlightSpans(row.Text, _highlightTerms, _highlightWords, _highlightWholeWordsOnly))
        {
            if (start > pos) text.Inlines.Add(new Run(row.Text[pos..start]));

            var match = new Run(row.Text.Substring(start, length));
            match.Bind(TextElement.BackgroundProperty, highlight);
            text.Inlines.Add(match);
            pos = start + length;
        }

        if (pos < row.Text.Length) text.Inlines.Add(new Run(row.Text[pos..]));

        if (!string.IsNullOrEmpty(row.Citation))
        {
            ToolTip.SetTip(text, row.Citation);
            ToolTip.SetShowDelay(text, 700);
        }

        return new Border { Padding = new Thickness(10, 6), Child = text };
    }

    /// <summary>
    /// The palette's gold, thinned to a wash, so a marked word reads like a
    /// highlighter stroke in either appearance and stays distinct from the
    /// selection colour of the row it is in.
    /// </summary>
    private static object? HighlightBrush(object? gold) =>
        gold is ISolidColorBrush brush ? new SolidColorBrush(brush.Color, 0.38) : gold;
}
