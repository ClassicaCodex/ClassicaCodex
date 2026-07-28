using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class MainForm : Form
{
    private readonly TreeView _libraryTree;
    private readonly Button _treeToggleButton;
    private bool _libraryTreeCollapsed;

    /// <summary>
    /// Set only while OpenWorkAsync assigns the tree selection itself, so
    /// the AfterSelect handler skips the load that OpenWorkAsync is about
    /// to do explicitly. Without it, opening a work from any other form
    /// loads and re-measures the whole text twice.
    /// </summary>
    private bool _suppressTreeSelectionLoad;
    private readonly SyncListView _originalPane;
    private readonly SyncListView _translationPane;
    private readonly ComboBox _originalEditionCombo;
    private readonly ComboBox _translationEditionCombo;
    private readonly Button _themeButton;
    private readonly Button _helpButton;
    private readonly TextBox _searchBox;
    private readonly Button _searchButton;
    private readonly ListBox _searchResults;
    private readonly Button _resultsToggleButton;
    private bool _searchResultsCollapsed;

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly TagRepository _tagRepo = new();
    private readonly BookmarkRepository _bookmarkRepo = new();

    private bool _syncingScroll;
    private List<string> _highlightTerms = new();
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _currentSearchResults = new();

    // ContextMenuStrip isn't a child Control, so ReadingTheme.Apply's tree
    // walk never reaches it - these are tracked here so ApplyTheme can
    // re-theme them explicitly, the same way it re-themes everything else
    // on a toggle. One per reader pane (original, translation).
    private readonly List<ContextMenuStrip> _lineContextMenus = new();

    // These menu items get their icon once, at construction - which happens
    // before ReadingTheme.Load() ever runs (that's in Shown, this is in the
    // constructor). So on a saved dark-mode preference, they'd otherwise be
    // stuck with the icon fetched under the assumed default (light) mode
    // forever, since nothing else ever revisits them. Tracked here so
    // ApplyTheme can re-fetch each one against whatever theme is actually
    // current, the same as it does for the menu's own colors above.
    private readonly List<(ToolStripItem Item, string IconName)> _lineContextMenuIcons = new();

    public MainForm()
    {
        Text = "Classica Codex";
        Width = 1840;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        var tagsButton = new Button { Text = "Tags...", Left = 532, Top = 10, Width = 80, Height = 30 };
        tagsButton.Click += (_, _) =>
        {
            using var tagBrowser = new TagBrowserForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            tagBrowser.ShowDialog(this);
        };

        _searchBox = new TextBox { Left = 10, Top = 14, Width = 300, PlaceholderText = "Search (matches word forms, e.g. \"run\" finds \"running\")" };
        _searchButton = new Button { Text = "Search", Left = 316, Top = 12, Width = 90 };
        _searchButton.Click += async (_, _) => await RunSearchAsync();

        var bookmarksButton = new Button { Text = "Bookmarks...", Left = 416, Top = 10, Width = 110, Height = 30 };
        bookmarksButton.Click += (_, _) =>
        {
            using var bookmarksForm = new BookmarksForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            bookmarksForm.ShowDialog(this);
        };

        var mythNetworkButton = new Button { Text = "Myth Network...", Left = 622, Top = 10, Width = 140, Height = 30 };
        mythNetworkButton.Click += (_, _) =>
        {
            using var networkForm = new MythNetworkForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            networkForm.ShowDialog(this);
        };

        var timelineButton = new Button { Text = "Timeline...", Left = 768, Top = 10, Width = 100, Height = 30 };
        timelineButton.Click += (_, _) =>
        {
            using var timelineForm = new TimelineForm
            {
                OnOpenWork = OpenWorkAsync
            };
            timelineForm.ShowDialog(this);
        };

        var stylometryButton = new Button { Text = "Stylometry...", Left = 874, Top = 10, Width = 120, Height = 30 };
        stylometryButton.Click += (_, _) =>
        {
            using var stylometryForm = new StylometryForm
            {
                OnOpenWork = OpenWorkAsync
            };
            stylometryForm.ShowDialog(this);
        };

        var concordanceButton = new Button { Text = "Concordance...", Left = 1000, Top = 10, Width = 130, Height = 30 };
        concordanceButton.Click += (_, _) =>
        {
            using var concordanceForm = new ConcordanceForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            concordanceForm.ShowDialog(this);
        };

        var compareTranslationsButton = new Button { Text = "Compare Translations...", Left = 1136, Top = 10, Width = 180, Height = 30 };
        compareTranslationsButton.Click += (_, _) =>
        {
            using var compareTranslationsForm = new CompareTranslationsForm();
            compareTranslationsForm.ShowDialog(this);
        };

        var placesMapButton = new Button { Text = "Places Map...", Left = 1322, Top = 10, Width = 130, Height = 30 };
        placesMapButton.Click += (_, _) =>
        {
            using var placesMapForm = new PlacesMapForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            placesMapForm.ShowDialog(this);
        };

        var morphologyButton = new Button { Text = "Morphology...", Left = 1458, Top = 10, Width = 130, Height = 30 };
        morphologyButton.Click += (_, _) =>
        {
            using var morphologyForm = new MorphologyForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            morphologyForm.ShowDialog(this);
        };

        // A small strip above the tree rather than a separate row in the
        // already-crowded top toolbar - clicking it hides the tree entirely,
        // widening the reader area to reclaim that space (handled in
        // RelayoutReaderArea below), for when someone just wants to read
        // without the sidebar taking up room.
        _treeToggleButton = new Button
        {
            Left = 10,
            Top = 50,
            Width = 300,
            Height = 24,
            Text = "\u25C0 Collapse Library"
        };
        AppIcons.Apply(_treeToggleButton, "Collapse", 14);

        _libraryTree = new TreeView
        {
            Left = 10,
            Top = 78,
            Width = 300,
            Height = 662,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        _libraryTree.AfterSelect += async (_, e) => await LibraryTree_AfterSelectAsync(e);

        var splitContainer = new SplitContainer
        {
            Left = 320,
            Top = 78,
            Width = 1360,
            Height = 472,
            Orientation = Orientation.Vertical
        };

        _originalPane = CreateReaderList(new Font("Palatino Linotype", 11F));
        _translationPane = CreateReaderList(new Font("Georgia", 11F));

        _originalPane.TopItemChanged += (_, _) => SyncScroll(_originalPane, _translationPane);
        _translationPane.TopItemChanged += (_, _) => SyncScroll(_translationPane, _originalPane);
        _originalPane.MouseClick += (_, e) => SyncSelectionFromClick(_originalPane, _translationPane, e);
        _translationPane.MouseClick += (_, e) => SyncSelectionFromClick(_translationPane, _originalPane, e);

        // A work can have more than one edition of the same kind - most
        // often several different translators for the same original text.
        // These let you switch between them; PopulateEditionCombo below
        // defaults each to its first option, and picking a different one
        // just repopulates that one pane.
        _originalEditionCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        _translationEditionCombo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        _originalEditionCombo.SelectedIndexChanged += async (_, _) => await OnOriginalEditionChangedAsync();
        _translationEditionCombo.SelectedIndexChanged += async (_, _) => await OnTranslationEditionChangedAsync();

        splitContainer.Panel1.Controls.Add(_originalPane);
        splitContainer.Panel1.Controls.Add(_originalEditionCombo);
        splitContainer.Panel2.Controls.Add(_translationPane);
        splitContainer.Panel2.Controls.Add(_translationEditionCombo);

        // A Button rather than a Label - it's the collapse/expand toggle for
        // the results list below it, not just a caption, so it should look
        // and feel clickable the same way the theme toggle already does.
        _resultsToggleButton = new Button
        {
            Left = 320,
            Top = 560,
            Width = 220,
            Height = 24,
            Text = "\u25BC Search results:"
        };
        AppIcons.Apply(_resultsToggleButton, "Collapse", 14);
        _searchResults = new ListBox
        {
            Left = 320,
            Top = 585,
            Width = 1360,
            Height = 155,
            DrawMode = DrawMode.OwnerDrawFixed,
            HorizontalScrollbar = true
        };
        _searchResults.DrawItem += SearchResults_DrawItem;
        _searchResults.DoubleClick += async (_, _) => await JumpToSelectedSearchResultAsync();
        ListResultHelpers.AttachCitationTooltip(_searchResults,
            i => i < _currentSearchResults.Count ? _currentSearchResults[i].CitationRef : null);

        // Unlike every dialog above, MainForm stays open across a theme
        // toggle - so this menu (and its icon) are tracked the same way the
        // reader panes' menus already are, for ApplyTheme to refresh live.
        var searchResultsMenu = ListResultHelpers.AttachCopyToClipboardMenu(_searchResults,
            i => i < _currentSearchResults.Count
                ? $"{_currentSearchResults[i].AuthorName}, {_currentSearchResults[i].WorkTitle} [{_currentSearchResults[i].CitationRef}]: {_currentSearchResults[i].Text}"
                : null);
        _lineContextMenus.Add(searchResultsMenu);
        _lineContextMenuIcons.Add((searchResultsMenu.Items[0], "CopyToClipboard"));

        Controls.Add(tagsButton);
        Controls.Add(_searchBox);
        Controls.Add(_searchButton);
        Controls.Add(bookmarksButton);
        Controls.Add(mythNetworkButton);
        Controls.Add(timelineButton);
        Controls.Add(stylometryButton);
        var aboutButton = new Button { Top = 10, Width = 36, Height = 30 };
        aboutButton.Click += (_, _) =>
        {
            using var aboutForm = new AboutForm();
            aboutForm.ShowDialog(this);
        };

        var setupWizardButton = new Button { Top = 10, Width = 36, Height = 30 };
        setupWizardButton.Click += (_, _) =>
        {
            using var choiceForm = new SetupModeChoiceForm();
            var choice = choiceForm.ShowDialog(this);

            if (choice == DialogResult.Yes)
            {
                using var guidedSetupForm = new GuidedSetupForm();
                guidedSetupForm.CorpusChanged += () => _ = LoadLibraryTreeAsync();
                guidedSetupForm.ShowDialog(this);
                _ = LoadLibraryTreeAsync();
            }
            else if (choice == DialogResult.No)
            {
                using var setupWizardForm = new SetupWizardForm();
                setupWizardForm.CorpusChanged += () => _ = LoadLibraryTreeAsync();
                setupWizardForm.ShowDialog(this);
                _ = LoadLibraryTreeAsync();
            }
            // Cancel: closed the picker without choosing - nothing to do.
        };

        _themeButton = new Button { Top = 10, Width = 36, Height = 30 };
        _themeButton.Click += (_, _) =>
        {
            ReadingTheme.Toggle();
            ApplyTheme();
        };

        _helpButton = new Button { Top = 10, Width = 36, Height = 30 };
        _helpButton.Click += (_, _) =>
        {
            using var helpForm = new HelpForm();
            helpForm.ShowDialog(this);
        };

        // These two have to be wired up here, not back where the buttons
        // themselves are created - both call RelayoutReaderArea, a local
        // function that closes over splitContainer, aboutButton, and
        // setupWizardButton. A local function can be called before its own
        // textual declaration, but the compiler still needs every variable
        // it captures to be definitely assigned at the point a delegate
        // referencing it is *created* - and back where these buttons are
        // built, none of those three existed yet, which is exactly the
        // CS0165 "unassigned local variable" this position avoids.
        _treeToggleButton.Click += (_, _) =>
        {
            _libraryTreeCollapsed = !_libraryTreeCollapsed;
            _libraryTree.Visible = !_libraryTreeCollapsed;
            _treeToggleButton.Text = _libraryTreeCollapsed ? "\u25B6" : "\u25C0 Collapse Library";
            AppIcons.Apply(_treeToggleButton, _libraryTreeCollapsed ? "Expand" : "Collapse", 14);
            RelayoutReaderArea();
        };
        _resultsToggleButton.Click += (_, _) =>
        {
            _searchResultsCollapsed = !_searchResultsCollapsed;
            _resultsToggleButton.Text = _searchResultsCollapsed ? "\u25B6" : "\u25BC Search results:";
            AppIcons.Apply(_resultsToggleButton, _searchResultsCollapsed ? "Expand" : "Collapse", 14);
            RelayoutReaderArea();
        };

        Controls.Add(concordanceButton);
        Controls.Add(compareTranslationsButton);
        Controls.Add(placesMapButton);
        Controls.Add(morphologyButton);
        Controls.Add(aboutButton);
        Controls.Add(setupWizardButton);
        Controls.Add(_themeButton);
        Controls.Add(_helpButton);

        // Icons are optional - AppIcons leaves a button alone when its file
        // isn't present, so the toolbar still works text-only.
        AppIcons.Apply(tagsButton, "AutoTag", 16);
        AppIcons.Apply(_searchButton, "Search", 16);
        AppIcons.Apply(bookmarksButton, "Bookmarks", 16);
        AppIcons.Apply(mythNetworkButton, "MythNetwork", 16);
        AppIcons.Apply(timelineButton, "Timeline", 16);
        AppIcons.Apply(stylometryButton, "Stylometry", 16);
        AppIcons.Apply(concordanceButton, "Concordance", 16);
        AppIcons.Apply(compareTranslationsButton, "CompareTexts", 16);
        AppIcons.Apply(placesMapButton, "PlaceMap", 16);
        AppIcons.Apply(morphologyButton, "WordStudy", 16);
        AppIcons.Apply(setupWizardButton, "Settings", 16);
        AppIcons.Apply(aboutButton, "About", 16);
        AppIcons.Apply(_helpButton, "Help", 16);
        Controls.Add(_libraryTree);
        Controls.Add(_treeToggleButton);
        Controls.Add(splitContainer);
        Controls.Add(_resultsToggleButton);
        Controls.Add(_searchResults);

        Load += async (_, _) => await LoadLibraryTreeAsync();

        // Deliberately NOT using Anchor for these three - Anchor bakes in a
        // distance captured at the moment a control is added to Controls,
        // and re-applies that on every resize regardless of what's set
        // afterward. That fought with a one-time post-Shown fix here: the
        // fix looked right until the window was actually resized, at which
        // point WinForms' anchor engine reasserted the original (wrong)
        // distances. Recomputing explicitly on every Resize - not just once
        // - is what actually keeps this correct continuously.
        void RelayoutReaderArea()
        {
            const int margin = 20;
            const int searchResultsHeight = 155;
            const int labelHeight = 24;
            const int gap = 6;
            const int collapsedToggleWidth = 36;

            // Same reasoning applies to the top-right buttons - pinned here
            // rather than via Anchor, for the identical reason. Left to
            // right: Setup Wizard, theme toggle, Help, About - built from
            // the right edge inward, so About anchors the chain and each
            // one before it is positioned off the one already placed.
            aboutButton.Left = Math.Max(ClientSize.Width - aboutButton.Width - margin, 0);
            _helpButton.Left = Math.Max(aboutButton.Left - _helpButton.Width - 8, 0);
            _themeButton.Left = Math.Max(_helpButton.Left - _themeButton.Width - 8, 0);
            setupWizardButton.Left = Math.Max(_themeButton.Left - setupWizardButton.Width - 8, 0);

            // Both toggle strips shrink to just their arrow once collapsed -
            // there's nothing left underneath either of them to line up
            // with, so the full descriptive label would only be clutter.
            _treeToggleButton.Width = _libraryTreeCollapsed ? collapsedToggleWidth : 300;
            _resultsToggleButton.Width = _searchResultsCollapsed ? collapsedToggleWidth : 220;

            // Reader area starts right after the tree - or right at the
            // window's own left margin if the tree is collapsed, reclaiming
            // its width for reading room.
            var readerAreaLeft = _libraryTreeCollapsed ? 10 : 320;
            splitContainer.Left = readerAreaLeft;
            _resultsToggleButton.Left = readerAreaLeft;
            _searchResults.Left = readerAreaLeft;

            _searchResults.Visible = !_searchResultsCollapsed;
            var searchResultsTop = ClientSize.Height - margin - (_searchResultsCollapsed ? 0 : searchResultsHeight);
            _searchResults.Top = searchResultsTop;
            _searchResults.Height = searchResultsHeight;
            _searchResults.Width = Math.Max(ClientSize.Width - _searchResults.Left - margin, 200);

            // The toggle button itself always stays visible, pinned near
            // the bottom whether the list under it is showing or not.
            _resultsToggleButton.Top = _searchResultsCollapsed
                ? ClientSize.Height - margin - labelHeight
                : searchResultsTop - labelHeight - gap;

            splitContainer.Width = Math.Max(ClientSize.Width - splitContainer.Left - margin, 400);

            // Always the toggle button's own row, in both states - not just
            // when expanded. Using ClientSize.Height directly in the
            // collapsed case (as this once did) let the reader pane grow
            // all the way to the bottom margin, overlapping the toggle
            // button's row and covering it entirely - which is exactly why
            // it "disappeared and wouldn't come back."
            var splitBottom = _resultsToggleButton.Top - gap;
            splitContainer.Height = Math.Max(splitBottom - splitContainer.Top, 100);
        }

        Resize += (_, _) => RelayoutReaderArea();
        Shown += (_, _) =>
        {
            RelayoutReaderArea();
            ReadingTheme.Load();
            ApplyTheme();
        };
    }

    /// <summary>
    /// Re-applies the current theme across this window and refreshes the
    /// toggle's label. Called at startup and on every toggle.
    /// </summary>
    private void ApplyTheme()
    {
        ReadingTheme.Apply(this);

        // Not part of the control tree Apply just walked (see the field
        // comment), so themed explicitly here alongside everything else.
        foreach (var menu in _lineContextMenus) ReadingTheme.ApplyToContextMenu(menu);

        // Same reason: an icon set once at construction time, before this
        // ran for the first time, would otherwise never revisit whichever
        // theme was actually current. Each uses whichever of its two icons
        // matches its current collapsed/expanded state, not just one fixed
        // name, since - unlike the six above - these two can change icons
        // for a reason other than the theme.
        foreach (var (item, iconName) in _lineContextMenuIcons) item.Image = AppIcons.Get(iconName, 16);
        AppIcons.Apply(_treeToggleButton, _libraryTreeCollapsed ? "Expand" : "Collapse", 14);
        AppIcons.Apply(_resultsToggleButton, _searchResultsCollapsed ? "Expand" : "Collapse", 14);

        // Icon shows what clicking will switch *to*, not the current state -
        // no text label needed, the sun/moon glyph already says it plainly.
        AppIcons.Apply(_themeButton, ReadingTheme.IsDark ? "LightMode" : "DarkMode", 16);

        Invalidate(true);
    }

    private async Task LoadLibraryTreeAsync()
    {
        _libraryTree.Nodes.Clear();

        List<Author> authors;
        Dictionary<int, List<Work>> worksByAuthor;
        try
        {
            authors = await _authorRepo.GetAllAsync();

            // One query for every work in the library, rather than one per
            // author inside the loop below - with a full corpus that was
            // hundreds of round trips before the tree could render at all.
            worksByAuthor = await _workRepo.GetAllGroupedByAuthorAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't load library: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Without this, every Nodes.Add triggers the tree to re-measure and
        // repaint - thousands of times over a full corpus. The reader pane
        // already does the same thing for the same reason.
        _libraryTree.BeginUpdate();
        try
        {
            foreach (var author in authors)
            {
                var authorNode = new TreeNode(author.Name) { Tag = author };

                if (worksByAuthor.TryGetValue(author.AuthorId, out var works))
                {
                    foreach (var work in works)
                    {
                        authorNode.Nodes.Add(new TreeNode(work.Title) { Tag = work });
                    }
                }

                _libraryTree.Nodes.Add(authorNode);
            }
        }
        finally
        {
            _libraryTree.EndUpdate();
        }
    }

    private SyncListView CreateReaderList(Font font)
    {
        var list = new SyncListView
        {
            Dock = DockStyle.Fill,
            Font = font
        };

        var menu = new ContextMenuStrip();
        var tagItem = menu.Items.Add("Tag this line...");
        tagItem.Image = AppIcons.Get("AutoTag", 16);
        tagItem.Click += async (_, _) => await TagSelectedLineAsync(list);
        var bookmarkItem = menu.Items.Add("Bookmark this line...");
        bookmarkItem.Image = AppIcons.Get("Bookmarks", 16);
        bookmarkItem.Click += async (_, _) => await BookmarkSelectedLineAsync(list);
        var echoItem = menu.Items.Add("Find Echoes...");
        // No dedicated "echo" glyph in the sheet - SimilarWorks is the
        // closest fit for "passages that echo this one elsewhere".
        echoItem.Image = AppIcons.Get("SimilarWorks", 16);
        echoItem.Click += (_, _) => FindEchoesForSelectedLine(list);
        var receptionItem = menu.Items.Add("Reception History...");
        receptionItem.Image = AppIcons.Get("ReceptionTracker", 16);
        receptionItem.Click += (_, _) => ShowReceptionHistoryForSelectedLine(list);
        var wordStudyItem = menu.Items.Add("Word Study...");
        wordStudyItem.Image = AppIcons.Get("WordStudy", 16);
        wordStudyItem.Click += (_, _) => ShowWordStudyForSelectedLine(list);
        var exportItem = menu.Items.Add("Export...");
        exportItem.Image = AppIcons.Get("Export", 16);
        exportItem.Click += async (_, _) => await ExportSelectedLineAsync(list, font.Name);
        list.ContextMenuStrip = menu;
        _lineContextMenus.Add(menu);
        _lineContextMenuIcons.Add((tagItem, "AutoTag"));
        _lineContextMenuIcons.Add((bookmarkItem, "Bookmarks"));
        _lineContextMenuIcons.Add((echoItem, "SimilarWorks"));
        _lineContextMenuIcons.Add((receptionItem, "ReceptionTracker"));
        _lineContextMenuIcons.Add((wordStudyItem, "WordStudy"));
        _lineContextMenuIcons.Add((exportItem, "Export"));

        list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hitIndex = list.IndexFromPoint(e.Location);
            if (hitIndex >= 0) list.SelectOnly(hitIndex);
        };

        return list;
    }

    /// <summary>
    /// Mirrors scroll position between the two panes by line index. Works
    /// well for verse texts where a translation keeps the same line count as
    /// the original; for prose works where line counts diverge it'll drift,
    /// but that's an inherent limit of index-based sync, not a bug to chase.
    /// </summary>
    private void SyncScroll(SyncListView source, SyncListView target)
    {
        if (_syncingScroll) return;
        if (target.Items.Count == 0) return;

        _syncingScroll = true;
        try
        {
            var index = source.TopIndex;
            if (index >= 0 && index < target.Items.Count)
            {
                target.TopIndex = index;
            }
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>
    /// Mirrors which line is highlighted between the two panes, but only in
    /// response to an actual mouse click - not SelectedIndexChanged, which
    /// also fires for programmatic selection (jump-to-passage, search jump,
    /// tag jump, citation jump). Those all pick the correct line by matching
    /// TextNodeId directly; hooking this to SelectedIndexChanged as well
    /// caused it to fire right after and immediately overwrite that correct
    /// selection with a wrong index-based guess in the other pane. A plain
    /// click only ever needs the index-based mirror, so it's kept separate.
    /// </summary>
    private void SyncSelectionFromClick(SyncListView source, SyncListView target, MouseEventArgs e)
    {
        var index = source.IndexFromPoint(e.Location);
        if (index < 0 || index >= target.Items.Count) return;

        target.SelectOnly(index);
        target.EnsureVisible(index);
    }

    private async Task TagSelectedLineAsync(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var prompt = new TagPromptForm(node.Text);
        if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.TagName)) return;

        var tagId = await _tagRepo.GetOrCreateAsync(prompt.TagName, prompt.Category);
        await _tagRepo.TagTextNodeAsync(node.TextNodeId, tagId);

        MessageBox.Show(this, $"Tagged [{node.CitationRef}] with \"{prompt.TagName}\".", "Tagged",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task BookmarkSelectedLineAsync(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var prompt = new BookmarkPromptForm(node.Text);
        if (prompt.ShowDialog(this) != DialogResult.OK) return;

        await _bookmarkRepo.AddAsync(node.TextNodeId, prompt.Note);

        MessageBox.Show(this, $"Bookmarked [{node.CitationRef}].", "Bookmarked",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void FindEchoesForSelectedLine(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var echoForm = new EchoResultsForm(node)
        {
            OnNavigate = NavigateToPassageAsync
        };
        echoForm.ShowDialog(this);
    }

    private void ShowReceptionHistoryForSelectedLine(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var receptionForm = new ReceptionTrackerForm(node)
        {
            OnNavigate = NavigateToPassageAsync
        };
        receptionForm.ShowDialog(this);
    }

    private void ShowWordStudyForSelectedLine(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var wordStudyForm = new WordStudyForm(node)
        {
            OnNavigate = NavigateToPassageAsync
        };
        wordStudyForm.ShowDialog(this);
    }

    private async Task ExportSelectedLineAsync(SyncListView list, string fontName)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var editionId = GetSelectedEditionId(list);
        if (editionId == null)
        {
            MessageBox.Show(this, "Couldn't determine which edition this line belongs to.", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // The other pane's edition, if one is loaded - this is what makes a
        // bilingual export possible. Null when the work has no counterpart
        // ingested (original-only or translation-only), in which case the
        // dialog just disables that option.
        var counterpartPane = list == _originalPane ? _translationPane : _originalPane;
        var counterpartEditionId = GetSelectedEditionId(counterpartPane);
        var counterpartIsOriginal = counterpartPane == _originalPane;

        var sourceInfo = await _textNodeRepo.GetTextNodeSourceInfoAsync(node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        using var exportForm = new PassageExportForm(
            node, editionId.Value, authorName, workTitle, fontName,
            counterpartEditionId, counterpartIsOriginal, _originalPane.Font.Name);
        exportForm.ShowDialog(this);
    }

    /// <summary>Which edition is currently loaded in a given pane, based on that pane's combo selection.</summary>
    private int? GetSelectedEditionId(SyncListView pane)
    {
        var combo = pane == _originalPane ? _originalEditionCombo
            : pane == _translationPane ? _translationEditionCombo
            : null;

        return (combo?.SelectedItem as EditionOption)?.Edition.EditionId;
    }

    private async Task LibraryTree_AfterSelectAsync(TreeViewEventArgs e)
    {
        // Checked before anything else, and before any await, so that
        // OpenWorkAsync's programmatic selection can reliably suppress the
        // duplicate load it would otherwise cause. See the note there.
        if (_suppressTreeSelectionLoad) return;

        if (e.Node?.Tag is not Work work) return;
        await LoadEditionSelectorsAsync(work.WorkId);
    }

    /// <summary>
    /// A single dropdown entry - just enough to display something readable
    /// (translator name if there is one, otherwise a fallback derived from
    /// the URN) while keeping the real Edition on hand for populating the pane.
    /// </summary>
    private class EditionOption
    {
        public required Edition Edition { get; init; }
        public required string Label { get; init; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// The edition-specific part of a dropdown label - what distinguishes
    /// one edition of a work from another.
    /// </summary>
    private static string GetEditionDescriptor(Edition edition)
    {
        if (edition.Kind == EditionKind.Translation)
        {
            if (!string.IsNullOrWhiteSpace(edition.Translator)) return $"trans. {edition.Translator}";

            var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return string.IsNullOrEmpty(suffix) ? "Translation" : $"Translation ({suffix})";
        }

        return edition.Language?.ToUpperInvariant() switch
        {
            "GRC" => "Greek (original)",
            "LAT" => "Latin (original)",
            not null => $"{edition.Language} (original)",
            null => "Original"
        };
    }

    /// <summary>
    /// An author's dates as a single span - "384-322 BCE", "23-79 CE", or
    /// "106 BCE-43 CE" when the life crosses the era boundary. Drawn from
    /// the same curated table the timeline uses, so it only covers authors
    /// listed there; anyone else simply gets no dates rather than a guess.
    /// </summary>
    private static string? FormatAuthorEra(string authorName)
    {
        var era = AuthorEraData.Lookup(authorName);
        if (era == null) return null;

        var (start, end) = era.Value;

        // Only print the era marker once when both dates share one, which
        // reads far better than "384 BCE-322 BCE".
        if (start < 0 && end < 0) return $"{-start}-{-end} BCE";
        if (start >= 0 && end >= 0) return $"{start}-{end} CE";

        return $"{AuthorEraData.FormatYear(start)}-{AuthorEraData.FormatYear(end)}";
    }

    private static string BuildEditionLabel(Edition edition, string? authorName, string? workTitle)
    {
        var descriptor = GetEditionDescriptor(edition);

        var context = new List<string>();
        if (!string.IsNullOrWhiteSpace(authorName)) context.Add(authorName);
        if (!string.IsNullOrWhiteSpace(workTitle)) context.Add(workTitle);

        if (context.Count == 0) return descriptor;

        var prefix = string.Join(", ", context);

        if (!string.IsNullOrWhiteSpace(authorName))
        {
            var era = FormatAuthorEra(authorName);
            if (era != null) prefix += $" ({era})";
        }

        return $"{prefix} \u2014 {descriptor}";
    }

    /// <summary>
    /// Fills a combo with every edition of one kind for a work and selects
    /// the first. Always explicitly sets SelectedIndex (rather than relying
    /// on default ComboBox behavior, which doesn't auto-select anything) so
    /// SelectedIndexChanged reliably fires and the corresponding pane
    /// actually gets populated - the same code path handles both "a work
    /// was just opened" and "the user picked a different edition".
    ///
    /// Sorted by the edition descriptor rather than the whole label, since
    /// the author/work prefix is identical across every entry here and would
    /// contribute nothing to the ordering.
    /// </summary>
    private static void PopulateEditionCombo(
        ComboBox combo, List<Edition> editions, string? authorName, string? workTitle)
    {
        combo.Items.Clear();
        foreach (var edition in editions.OrderBy(GetEditionDescriptor, StringComparer.OrdinalIgnoreCase))
        {
            combo.Items.Add(new EditionOption
            {
                Edition = edition,
                Label = BuildEditionLabel(edition, authorName, workTitle)
            });
        }

        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private async Task OnOriginalEditionChangedAsync()
    {
        var selected = _originalEditionCombo.SelectedItem as EditionOption;
        await PopulateReaderAsync(_originalPane, selected?.Edition, "(no original-language edition ingested)");
    }

    private async Task OnTranslationEditionChangedAsync()
    {
        var selected = _translationEditionCombo.SelectedItem as EditionOption;
        await PopulateReaderAsync(_translationPane, selected?.Edition, "(no translation ingested)");
    }

    private async Task PopulateReaderAsync(SyncListView pane, Edition? edition, string emptyMessage)
    {
        pane.BeginUpdate();
        try
        {
            pane.Items.Clear();

            if (edition == null)
            {
                pane.Items.Add(emptyMessage);
                return;
            }

            var nodes = await _textNodeRepo.GetByEditionAsync(edition.EditionId);

            // An edition row exists but produced no lines - the file was
            // catalogued during ingest but its text didn't parse into
            // anything. Saying so beats a blank pane that looks like a
            // display bug rather than an ingestion one.
            if (nodes.Count == 0)
            {
                pane.Items.Add("(this edition was catalogued but contains no text - its source file may have failed to parse during ingest)");
                return;
            }

            // One bulk insert rather than a call per line. The node itself
            // is the item (not wrapped in a Tag property), so right-click
            // "Tag this line" and similar features read it straight back
            // out of pane.Items.
            pane.Items.AddRange(nodes.Cast<object>().ToArray());
        }
        finally
        {
            pane.EndUpdate();
        }
    }

    /// <summary>
    /// Called from the tag browser when you double-click a tagged passage:
    /// selects the right work in the tree, loads both reader panes, and
    /// highlights + scrolls to the exact line that was tagged - in whichever
    /// pane (original or translation) actually contains it.
    ///
    /// With more than one translation possible per work, the line being
    /// jumped to might belong to an edition that isn't the default (first)
    /// one a fresh work-open selects. Rather than silently fail to find it,
    /// this checks which edition the target line actually lives in and
    /// switches that combo to match before selecting it.
    /// </summary>
    private async Task NavigateToPassageAsync(int workId, long textNodeId)
    {
        var opened = await OpenWorkAsync(workId);
        if (!opened) return;

        if (!SelectItemByTextNodeId(_originalPane, textNodeId) &&
            !SelectItemByTextNodeId(_translationPane, textNodeId))
        {
            var editionId = await _textNodeRepo.GetEditionIdAsync(textNodeId);
            if (editionId != null)
            {
                if (TrySelectEditionInCombo(_originalEditionCombo, editionId.Value))
                {
                    await OnOriginalEditionChangedAsync();
                }
                else if (TrySelectEditionInCombo(_translationEditionCombo, editionId.Value))
                {
                    await OnTranslationEditionChangedAsync();
                }
            }

            SelectItemByTextNodeId(_originalPane, textNodeId);
            SelectItemByTextNodeId(_translationPane, textNodeId);
        }
    }

    /// <summary>Selects the combo entry for a given EditionId, if present. Doesn't repopulate the pane itself.</summary>
    private static bool TrySelectEditionInCombo(ComboBox combo, int editionId)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is EditionOption option && option.Edition.EditionId == editionId)
            {
                if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Selects a work in the tree and loads both reader panes for it,
    /// without targeting any specific line. Shared by the passage/tag/search
    /// navigation (which then highlights a specific line on top of this) and
    /// the timeline (which just wants the work open to start reading).
    /// </summary>
    private async Task<bool> OpenWorkAsync(int workId)
    {
        var workNode = FindWorkNode(workId);
        if (workNode == null) return false;

        // Already the selected work - its panes are populated and current,
        // so there's nothing to reload. Jumping between passages of the
        // work you're already reading (a search result, an echo, a tagged
        // line) is then instant rather than a full reload of the whole text.
        if (ReferenceEquals(_libraryTree.SelectedNode, workNode)) return true;

        // Assigning SelectedNode raises AfterSelect, whose handler loads the
        // work too - so without suppressing it here, every open from another
        // form loaded and re-measured the entire text twice, and the two
        // loads could interleave while both populated the same panes.
        //
        // The flag is checked as the first statement of that handler, before
        // any await, so it is read synchronously during this assignment and
        // is reliably still set. The explicit awaited call below is what
        // actually loads, which keeps this method's completion meaning "the
        // panes are populated" - callers depend on that to select a line
        // immediately afterward.
        _suppressTreeSelectionLoad = true;
        try
        {
            _libraryTree.SelectedNode = workNode;
        }
        finally
        {
            _suppressTreeSelectionLoad = false;
        }

        await LoadEditionSelectorsAsync(workId);
        return true;
    }

    /// <summary>
    /// Loads every edition of a work into the two combos and, via their
    /// SelectedIndexChanged handlers, populates both panes with whichever
    /// edition ends up selected in each (always the first of its kind, for
    /// a freshly opened work).
    /// </summary>
    private async Task LoadEditionSelectorsAsync(int workId)
    {
        var editions = await _editionRepo.GetByWorkAsync(workId);
        var originals = editions.Where(ed => ed.Kind == EditionKind.Original).ToList();
        var translations = editions.Where(ed => ed.Kind == EditionKind.Translation).ToList();

        // The library tree already holds both names - the work on its node,
        // the author on that node's parent - so no extra query is needed.
        var workNode = FindWorkNode(workId);
        var workTitle = workNode?.Text;
        var authorName = workNode?.Parent?.Text;

        PopulateEditionCombo(_originalEditionCombo, originals, authorName, workTitle);
        PopulateEditionCombo(_translationEditionCombo, translations, authorName, workTitle);

        // PopulateEditionCombo only triggers a pane load when it actually
        // has something to select - handle the "this work has none of this
        // kind" case explicitly so the pane still shows the right empty message.
        if (_originalEditionCombo.Items.Count == 0)
            await PopulateReaderAsync(_originalPane, null, "(no original-language edition ingested)");
        if (_translationEditionCombo.Items.Count == 0)
            await PopulateReaderAsync(_translationPane, null, "(no translation ingested)");
    }

    private TreeNode? FindWorkNode(int workId)
    {
        foreach (TreeNode authorNode in _libraryTree.Nodes)
        {
            foreach (TreeNode workNode in authorNode.Nodes)
            {
                if (workNode.Tag is Work w && w.WorkId == workId) return workNode;
            }
        }
        return null;
    }

    private static bool SelectItemByTextNodeId(SyncListView pane, long textNodeId)
    {
        for (var i = 0; i < pane.Items.Count; i++)
        {
            if (pane.Items[i] is not TextNode node || node.TextNodeId != textNodeId) continue;

            pane.SelectOnly(i);
            pane.EnsureVisible(i);
            return true;
        }
        return false;
    }

    private async Task RunSearchAsync()
    {
        var query = _searchBox.Text.Trim();
        if (query.Length == 0) return;

        _highlightTerms = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 0)
            .ToList();

        _searchResults.Items.Clear();
        _currentSearchResults = await _textNodeRepo.SearchAsync(query);

        // Same reasoning as the library tree: each Add otherwise repaints
        // the list, and this runs on every single search.
        _searchResults.BeginUpdate();
        try
        {
            foreach (var r in _currentSearchResults.Take(500))
            {
                _searchResults.Items.Add($"{r.AuthorName}, {r.WorkTitle}: {r.Text}");
            }

            if (_currentSearchResults.Count == 0)
            {
                _searchResults.Items.Add("No matches.");
            }
        }
        finally
        {
            _searchResults.EndUpdate();
        }
    }

    /// <summary>
    /// Double-clicking a search result jumps to it in full context - same
    /// navigation the tag browser uses - rather than isolating just that one
    /// line, so you land on the passage with everything around it and the
    /// matching row highlighted in place.
    /// </summary>
    private async Task JumpToSelectedSearchResultAsync()
    {
        var index = _searchResults.SelectedIndex;
        if (index < 0 || index >= _currentSearchResults.Count) return;

        var result = _currentSearchResults[index];
        await NavigateToPassageAsync(result.WorkId, result.TextNodeId);
    }

    /// <summary>
    /// Paints each result line manually so the words you searched for can be
    /// highlighted inline. Since search does word-stem matching, this
    /// highlights by substring rather than exact form - "run" highlights
    /// inside "running" too since one contains the other, which covers most
    /// common inflections; irregular forms (e.g. "ran") won't highlight even
    /// though they matched, since they don't share the typed substring.
    /// </summary>
    private void SearchResults_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        // Explicit fill rather than e.DrawBackground(), which paints the
        // system selection color and so ignores the app's own theme.
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var backBrush = new SolidBrush(selected ? ReadingTheme.SelectionBackground : ReadingTheme.Surface))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        var text = _searchResults.Items[e.Index]?.ToString() ?? string.Empty;
        var font = _searchResults.Font;
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
                // Khaki reads well behind dark text but turns to mud in dark
                // mode, where the surrounding text is near-white - a deep
                // amber keeps the "highlighted" signal without fighting it.
                var highlightColor = ReadingTheme.IsDark
                    ? Color.FromArgb(120, 92, 20)
                    : Color.Khaki;
                using var highlightBrush = new SolidBrush(highlightColor);
                e.Graphics.FillRectangle(highlightBrush, rect);
            }

            TextRenderer.DrawText(e.Graphics, part, font, rect, foreColor, TextFormatFlags.NoPadding);
            x += size.Width;
        }

        var spans = FindHighlightSpans(text, _highlightTerms);
        int pos = 0;
        foreach (var (start, length) in spans)
        {
            if (start < pos) continue; // overlapping match, already covered
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
