using ClassicaCodex.Core;
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
    private readonly Button _gettingStartedButton;
    private readonly Button _backButton;
    private readonly Button _forwardButton;

    /// <summary>
    /// Where the reader has been, in order, so Back returns there.
    ///
    /// Ten features in this application end in "jump to it" - search
    /// results, concordance, echoes, the places map, the myth network - and
    /// every one of them throws away where you were. Following a reference
    /// out of a passage you were reading meant finding your way back to it
    /// by hand.
    ///
    /// Session-only and keyed on ids rather than CTS URNs, which is the
    /// opposite of what tags, bookmarks and reading position do. Those have
    /// to survive a re-ingest that renumbers every id; this list does not
    /// outlive the window it belongs to, so the durable key would buy
    /// nothing and cost a lookup per entry.
    /// </summary>
    private readonly List<(int WorkId, long? TextNodeId)> _history = new();

    private int _historyIndex = -1;

    /// <summary>
    /// Set while Back or Forward is doing the navigating, so the jump they
    /// perform isn't recorded as a new destination - which would make Back
    /// append to the list it is walking and leave Forward permanently
    /// unreachable.
    /// </summary>
    private bool _navigatingHistory;

    /// <summary>
    /// Long enough that Back is always there when wanted, short enough that
    /// it stays a list rather than a session log.
    /// </summary>
    private const int MaxHistoryEntries = 100;
    private readonly Button _fontSizeButton;

    /// <summary>
    /// Whether the panes scroll and select together.
    ///
    /// An IconButton holding a plain bool, not a CheckBox. A checkbox drawn
    /// in button appearance seemed right while there was no artwork - it
    /// draws itself pressed when checked, which was free state - but it
    /// paints the system button face to do it, which is a white slab in dark
    /// mode and a highlighted box in light, either way a rectangle of chrome
    /// among six flat icons. Now that there are two pieces of artwork the
    /// icon is the state, and the chrome has nothing left to say.
    /// </summary>
    private readonly IconButton _syncPanesButton;

    private bool _panesLinked;
    private readonly Button _searchButton;

    // Non-modal and reused: reopening Search shouldn't lose the filters you
    // just set, and a search window you have to close to read what it found
    // would be a worse one than the inline strip it replaced.
    private SearchForm? _searchForm;

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    /// <summary>
    /// A preface/front-matter block held back from the reader pane, per
    /// pane - some translations carry a translator's preface as its own
    /// citation ("...perseus-eng4.preface"), which doesn't correspond to
    /// anything in the original and used to just sit at the top of the
    /// translation list, out of sync with the original pane and easy to
    /// mistake for the first line of the actual text. Held here instead,
    /// null when the current edition has none, and surfaced only through
    /// "View Preface..." on that pane's right-click menu.
    /// </summary>
    private (string CitationRef, string Text)? _originalPrefaceMatch;
    private (string CitationRef, string Text)? _translationPrefaceMatch;
    private readonly TagRepository _tagRepo = new();
    private readonly BookmarkRepository _bookmarkRepo = new();

    private bool _syncingScroll;

    // ContextMenuStrip isn't a child Control, so ReadingTheme.Apply's tree
    // walk never reaches it - these are tracked here so ApplyTheme can
    // re-theme them explicitly, the same way it re-themes everything else
    // on a toggle. The two reader panes' line menus, the search results
    // menu, and the library tree's own menu.
    //
    // Adding a menu here rather than calling ApplyToContextMenu once at
    // construction is what makes it follow a later theme toggle: the
    // library tree's menu was themed nowhere at all and so stayed light
    // against a dark window, and theming it once in the constructor would
    // only have moved the problem to the first time someone switched.
    private readonly List<ContextMenuStrip> _themedContextMenus = new();

    // One ToolTip for the whole toolbar. A ToolTip is a component rather
    // than a control, so the theme's tree walk never reaches it - it gets
    // re-themed explicitly alongside the context menus.
    private readonly ToolTip _toolbarTips = new();

    // The work currently in the reader, kept so a reading position can name
    // it by CTS URN rather than by an id that means nothing outside this
    // database file.
    private Work? _openWork;

    // What the tree was last built from. Filtering rebuilds the nodes from
    // these rather than going back to the database on every keystroke - a
    // full corpus is a couple of thousand authors and one query for all
    // their works, which is not something to repeat per character typed.
    private readonly TextBox _treeFilterBox;
    private readonly PictureBox _treeFilterIcon;
    private readonly CheckBox _favoritesOnlyCheck;

    private readonly FavoriteWorkRepository _favoriteRepo = new();

    /// <summary>
    /// CTS URNs of the favourited works, read once per tree load. The tree
    /// draws a few thousand nodes and each needs to know whether it carries a
    /// star while it is being built, so this is held rather than queried.
    /// </summary>
    private HashSet<string> _favoriteUrns = new(StringComparer.Ordinal);

    private List<Author> _allAuthors = new();
    private Dictionary<int, List<Work>> _worksByAuthor = new();

    // These menu items get their icon once, at construction - which happens
    // before ReadingTheme.Load() ever runs (that's in Shown, this is in the
    // constructor). So on a saved dark-mode preference, they'd otherwise be
    // stuck with the icon fetched under the assumed default (light) mode
    // forever, since nothing else ever revisits them. Tracked here so
    // ApplyTheme can re-fetch each one against whatever theme is actually
    // current, the same as it does for the menu's own colors above.
    private readonly List<(ToolStripItem Item, string IconName)> _themedMenuItemIcons = new();

    /// <summary>
    /// The open work's attribution, read when it opens and refreshed when the
    /// reader changes it.
    /// </summary>
    private (AttributionStatus Status, string? Note, bool SetByUser) _currentWorkAttribution
        = (AttributionStatus.Accepted, null, false);

    // Toolbar icons need the same treatment as the menu icons above, and for
    // a sharper reason now that each has a separate light and dark file: the
    // constructor runs before ReadingTheme.Load(), so every icon is first
    // fetched as though the theme were light. Without re-fetching on the
    // toggle, a dark-mode session shows the light-mode artwork for its whole
    // life - pale parchment tiles on a dark toolbar.
    private readonly List<(Button Button, string IconName)> _themedButtonIcons = new();

    public MainForm()
    {
        DpiScaling.UseDesignFontScaling(this);
        Text = "Classica Codex";
        AppIcons.ApplyWindowIcon(this, "AppIcon");
        Width = 1840;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        var tagsButton = new IconButton { Text = "Tags...", Left = 532, Top = 10, Width = 80, Height = 30 };
        tagsButton.Click += (_, _) =>
        {
            using var tagBrowser = new TagBrowserForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            tagBrowser.ShowDialog(this);
        };

        _searchButton = new IconButton { Text = "Search...", Left = 10, Top = 12, Width = 110 };
        _searchButton.Click += (_, _) => OpenSearchWindow();

        var bookmarksButton = new IconButton { Text = "Bookmarks...", Left = 416, Top = 10, Width = 110, Height = 30 };
        bookmarksButton.Click += (_, _) =>
        {
            using var bookmarksForm = new BookmarksForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            bookmarksForm.ShowDialog(this);
        };

        var mythNetworkButton = new IconButton { Text = "Myth Network...", Left = 622, Top = 10, Width = 140, Height = 30 };
        mythNetworkButton.Click += (_, _) =>
        {
            using var networkForm = new MythNetworkForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            networkForm.ShowDialog(this);
        };

        var timelineButton = new IconButton { Text = "Timeline...", Left = 768, Top = 10, Width = 100, Height = 30 };
        timelineButton.Click += (_, _) =>
        {
            using var timelineForm = new TimelineForm
            {
                OnOpenWork = OpenWorkAsync
            };
            timelineForm.ShowDialog(this);
        };

        var stylometryButton = new IconButton { Text = "Stylometry...", Left = 874, Top = 10, Width = 120, Height = 30 };
        stylometryButton.Click += (_, _) =>
        {
            using var stylometryForm = new StylometryForm
            {
                OnOpenWork = OpenWorkAsync
            };
            stylometryForm.ShowDialog(this);
        };

        // Compare Saved Runs. A separate window rather than a tab on
        // StylometryForm: that form produces one run, this one compares many,
        // and they're used at different moments - the compare view would sit
        // idle behind a tab during the batch that fills it.
        //
        // No Left/Top/Width/Height here on purpose. The toolbar loop further
        // down assigns all four from toolbarLeft, so anything set at
        // construction is overwritten. Passing them anyway is misleading: it
        // reads as though the position matters when it doesn't.
        var stylometryCompareButton = new IconButton();
        stylometryCompareButton.Click += (_, _) =>
        {
            using var analysisForm = new StylometryAnalysisForm();
            analysisForm.ShowDialog(this);
        };

        var concordanceButton = new IconButton { Text = "Concordance...", Left = 1000, Top = 10, Width = 130, Height = 30 };
        concordanceButton.Click += (_, _) =>
        {
            using var concordanceForm = new ConcordanceForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            concordanceForm.ShowDialog(this);
        };

        var compareTranslationsButton = new IconButton { Text = "Compare Translations...", Left = 1136, Top = 10, Width = 180, Height = 30 };
        compareTranslationsButton.Click += (_, _) =>
        {
            using var compareTranslationsForm = new CompareTranslationsForm();
            compareTranslationsForm.ShowDialog(this);
        };

        var placesMapButton = new IconButton { Text = "Places Map...", Left = 1322, Top = 10, Width = 130, Height = 30 };
        placesMapButton.Click += (_, _) =>
        {
            using var placesMapForm = new PlacesMapForm
            {
                OnNavigate = NavigateToPassageAsync
            };
            placesMapForm.ShowDialog(this);
        };

        var morphologyButton = new IconButton { Text = "Morphology...", Left = 1458, Top = 10, Width = 130, Height = 30 };
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
        // Icon only. The word "Library" was the widest thing on this row and
        // the row now has to carry a favourites filter as well; the icon
        // already says collapse, and the tooltip says the rest.
        _treeToggleButton = new Button
        {
            Left = 10,
            Top = 54,
            Width = 36,
            Height = 24,
            Text = string.Empty
        };
        _toolbarTips.SetToolTip(_treeToggleButton, "Show or hide the library");
        _treeToggleButton.AccessibleName = "Show or hide the library";
        AppIcons.Apply(_treeToggleButton, "Collapse", 14);

        // Sits on the toggle's own row rather than taking a row of its own,
        // which is why that button lost its wordier label - the tree is
        // long enough that jumping to an author by name beats scrolling to
        // them, and the header area was already the tallest thing above the
        // reader.
        _treeFilterIcon = new PictureBox
        {
            Left = 52,
            Top = 57,
            Width = 16,
            Height = 16,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = AppIcons.Get("Filter", 16)
        };

        _treeFilterBox = new TextBox
        {
            Left = 72,
            Top = 54,
            Width = 190,
            PlaceholderText = "Filter authors"
        };

        // The star alone, with the word in a tooltip. It is the same glyph
        // that marks a favourited work in the tree, so the row does not need
        // to spell out what it filters - and the space it gives back goes to
        // the author filter, which is the control on this row that benefits
        // from being wider.
        _favoritesOnlyCheck = new CheckBox
        {
            Left = 268,
            Top = 54,
            Width = 42,
            Height = 24,
            Text = "\u2605"
        };
        _favoritesOnlyCheck.CheckedChanged += (_, _) => PopulateLibraryTree();
        _toolbarTips.SetToolTip(_favoritesOnlyCheck, "Show favourites only");
        _favoritesOnlyCheck.AccessibleName = "Show favourites only";

        // Rebuilt straight from the cached lists on each keystroke rather
        // than debounced - there is no query behind it, so the work is a
        // string comparison per author and a tree rebuild, which a full
        // corpus absorbs without a pause.
        _treeFilterBox.TextChanged += (_, _) => PopulateLibraryTree();

        _libraryTree = new TreeView
        {
            Left = 10,
            Top = 82,
            Width = 300,
            Height = 658,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        _libraryTree.AfterSelect += async (_, e) => await LibraryTree_AfterSelectAsync(e);

        // Right-click doesn't select a TreeNode the way left-click does, so
        // the node under the cursor is selected explicitly first - otherwise
        // this would act on whatever was last left-clicked, not on what the
        // person actually right-clicked. Only Work-level nodes carry a Work
        // in their Tag (author nodes don't), so the item is simply hidden
        // rather than shown-but-disabled for anything else.
        var libraryTreeMenu = new ContextMenuStrip();

        var viewDetailsItem = libraryTreeMenu.Items.Add("View Details...");
        viewDetailsItem.Image = AppIcons.Get("Help", 16);
        viewDetailsItem.Click += (_, _) => ShowDetailsForSelectedWork();
        _themedMenuItemIcons.Add((viewDetailsItem, "Help"));

        var translateMyselfItem = libraryTreeMenu.Items.Add("Translate This Myself...");
        translateMyselfItem.Image = AppIcons.Get("WordStudy", 16);
        translateMyselfItem.Click += async (_, _) => await OpenTranslationWorkbenchAsync();
        _themedMenuItemIcons.Add((translateMyselfItem, "WordStudy"));

        var createTranslationItem = libraryTreeMenu.Items.Add("Create Translation...");
        createTranslationItem.Image = AppIcons.Get("Translate", 16);
        createTranslationItem.Click += async (_, _) => await CreateTranslationForSelectedWorkAsync();
        _themedMenuItemIcons.Add((createTranslationItem, "Translate"));

        var vocabularyItem = libraryTreeMenu.Items.Add("Core Vocabulary...");
        vocabularyItem.Image = AppIcons.Get("CoreVocabulary", 16);
        vocabularyItem.Click += async (_, _) => await ShowVocabularyForSelectedWorkAsync();
        _themedMenuItemIcons.Add((vocabularyItem, "CoreVocabulary"));

        var attributionItem = libraryTreeMenu.Items.Add("Attribution...");
        attributionItem.Image = AppIcons.Get("Show", 16);
        attributionItem.Click += async (_, _) => await EditAttributionForSelectedWorkAsync();
        _themedMenuItemIcons.Add((attributionItem, "Show"));

        var researchItem = libraryTreeMenu.Items.Add("Research...");
        researchItem.Image = AppIcons.Get("WordStudy", 16);
        researchItem.Click += async (_, _) => await OpenResearchBenchForSelectedWorkAsync();
        _themedMenuItemIcons.Add((researchItem, "WordStudy"));

        var favoriteItem = libraryTreeMenu.Items.Add("Add to Favourites");
        favoriteItem.Image = AppIcons.Get("Bookmarks", 16);
        favoriteItem.Click += async (_, _) => await ToggleFavoriteForSelectedWorkAsync();
        _themedMenuItemIcons.Add((favoriteItem, "Bookmarks"));

        // Both items act on a work, and only work nodes carry one in their
        // Tag. Cancelling outright rather than hiding both: with every item
        // invisible the menu still opens, as an empty grey sliver next to
        // the cursor, which reads as a glitch rather than as "nothing to do
        // here".
        libraryTreeMenu.Opening += (_, e) =>
        {
            if (_libraryTree.SelectedNode?.Tag is not Work work)
            {
                e.Cancel = true;
                return;
            }

            // One item that reads as what it will do, rather than two with
            // one of them permanently greyed out.
            favoriteItem.Text = _favoriteUrns.Contains(work.CtsUrn)
                ? "Remove from Favourites"
                : "Add to Favourites";

            // Says the current answer, so the common case - checking what the
            // library thinks - does not need the dialog opened at all.
            attributionItem.Text = work.AttributionStatus switch
            {
                AttributionStatus.Disputed => "Attribution (disputed)...",
                AttributionStatus.Spurious => "Attribution (not by this author)...",
                _ => "Attribution..."
            };
        };
        _libraryTree.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var node = _libraryTree.GetNodeAt(e.Location);
            if (node != null) _libraryTree.SelectedNode = node;
        };
        _libraryTree.ContextMenuStrip = libraryTreeMenu;
        _themedContextMenus.Add(libraryTreeMenu);

        var splitContainer = new SplitContainer
        {
            Left = 320,

            // Starts level with the library's toggle row rather than below
            // it. That row belongs to the tree - a Collapse button and an
            // author filter - and the reader side has nothing to put there,
            // so matching the tree's top left a band of empty window across
            // the whole reader for no reason.
            Top = 54,
            Width = 1360,
            Height = 496,
            Orientation = Orientation.Vertical
        };

        // Each pane takes its own size. They are linked by default and so
        // will usually be equal, but the two panes sit side by side and text
        // at two different sizes in adjacent panes has to be something the
        // reader asked for rather than something the app did.
        _originalPane = CreateReaderList(new Font("Palatino Linotype", ReadingFontSettings.SourceSize));
        _translationPane = CreateReaderList(new Font("Georgia", ReadingFontSettings.TranslationSize));

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

        Controls.Add(tagsButton);
        Controls.Add(_searchButton);
        Controls.Add(bookmarksButton);
        Controls.Add(mythNetworkButton);
        Controls.Add(timelineButton);
        Controls.Add(stylometryButton);
        Controls.Add(stylometryCompareButton);
        var aboutButton = new IconButton { Top = 10, Width = 36, Height = 30 };
        aboutButton.Click += (_, _) =>
        {
            using var aboutForm = new AboutForm();
            aboutForm.ShowDialog(this);
        };

        var setupWizardButton = new IconButton { Top = 10, Width = 36, Height = 30 };
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

        _themeButton = new IconButton { Top = 10, Width = 36, Height = 30 };
        _themeButton.Click += (_, _) =>
        {
            ReadingTheme.Toggle();
            ApplyTheme();
        };

        // Arrow glyphs as text, not icons. AppIcons leaves a button alone
        // when the file is missing, so dropping Back.png and Forward.png in
        // later will light them up without touching this - and until then
        // the arrows read perfectly well, which beats two blank squares.
        _backButton = new IconButton { Text = "\u25C0", Enabled = false };
        _backButton.Click += async (_, _) => await GoHistoryAsync(-1);

        _forwardButton = new IconButton { Text = "\u25B6", Enabled = false };
        _forwardButton.Click += async (_, _) => await GoHistoryAsync(1);

        _gettingStartedButton = new IconButton { Top = 10, Width = 36, Height = 30 };
        _gettingStartedButton.Click += async (_, _) => await ShowStartingPointsAsync();

        _fontSizeButton = new IconButton { Top = 10, Width = 36, Height = 30 };
        _fontSizeButton.Click += (_, _) =>
        {
            using var sizeForm = new ReadingFontSizeForm();
            sizeForm.ShowDialog(this);
        };

        // Subscribed rather than applied at the point of change, because the
        // workbench listens to the same event - the size has to reach a
        // window this one did not open.
        ReadingFontSettings.Changed += ApplyReadingFontSize;
        FormClosed += (_, _) => ReadingFontSettings.Changed -= ApplyReadingFontSize;

        _panesLinked = PaneSyncSettings.Enabled;

        _syncPanesButton = new IconButton { Top = 10, Width = 36, Height = 30 };
        _syncPanesButton.Click += (_, _) =>
        {
            _panesLinked = !_panesLinked;
            PaneSyncSettings.Enabled = _panesLinked;
            RefreshSyncPanesIcon();
            RefreshSyncPanesTooltip();
        };

        _helpButton = new IconButton { Top = 10, Width = 36, Height = 30 };
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

            // Filtering a hidden tree isn't a thing, and leaving the box
            // sitting there over the reader would look like part of it.
            _treeFilterIcon.Visible = !_libraryTreeCollapsed;
            _treeFilterBox.Visible = !_libraryTreeCollapsed;
            _favoritesOnlyCheck.Visible = !_libraryTreeCollapsed;

            _favoritesOnlyCheck.Visible = !_libraryTreeCollapsed;
            AppIcons.Apply(_treeToggleButton, _libraryTreeCollapsed ? "Expand" : "Collapse", 14);
            RefreshSyncPanesIcon();
            RelayoutReaderArea();
        };
        Controls.Add(concordanceButton);
        Controls.Add(compareTranslationsButton);
        Controls.Add(placesMapButton);
        Controls.Add(morphologyButton);
        Controls.Add(aboutButton);
        Controls.Add(setupWizardButton);
        Controls.Add(_themeButton);
        Controls.Add(_backButton);
        Controls.Add(_forwardButton);
        Controls.Add(_gettingStartedButton);
        Controls.Add(_fontSizeButton);
        Controls.Add(_syncPanesButton);
        Controls.Add(_helpButton);

        // Icons are optional - AppIcons leaves a button alone when its file
        // isn't present, so the toolbar still works text-only.
        //
        // Icon-only, with the label moved to a tooltip. The icons are drawn
        // as detailed tiles rather than flat glyphs, and at the 16px a
        // labelled button leaves room for they turn to mush - dropping the
        // text buys enough width to show them at 24px, where the artwork is
        // actually readable. The name isn't lost, just moved: it's one hover
        // away, and it's on AccessibleName for anything reading the UI aloud.
        //
        // Positions are computed here rather than written per button. The
        // previous hand-tuned Left values had to be re-tuned by hand every
        // time a button's width changed, which is exactly the kind of thing
        // that drifts a pixel at a time until the row looks crooked.
        _toolbarTips.InitialDelay = 400;
        _toolbarTips.ReshowDelay = 200;
        ReadingTheme.ApplyToToolTip(_toolbarTips);

        var toolbarButtons = new (Button Button, string Label, string Icon)[]
        {
            (_backButton, "Back  (Alt+Left)", "Back"),
            (_forwardButton, "Forward  (Alt+Right)", "Forward"),
            (_searchButton, "Search  (Ctrl+F)", "Search"),
            (bookmarksButton, "Bookmarks", "Bookmarks"),
            (tagsButton, "Tags", "AutoTag"),
            (mythNetworkButton, "Myth Network", "MythNetwork"),
            (timelineButton, "Timeline", "Timeline"),
            (stylometryButton, "Stylometry", "Stylometry"),
            (stylometryCompareButton, "Compare Saved Runs", "StylometryCompare"),
            (concordanceButton, "Concordance", "Concordance"),
            (compareTranslationsButton, "Compare Translations", "CompareTexts"),
            (placesMapButton, "Places Map", "PlaceMap"),
            (morphologyButton, "Morphology", "Morphology")
        };

        // The icon nearly fills the button - these are illustrations, and
        // they earn their space. The few pixels left over are what the hover
        // tint shows through, which is the only chrome an IconButton has.
        const int toolbarSize = 46;
        const int toolbarIcon = 40;
        const int toolbarGap = 4;
        var toolbarLeft = 10;

        foreach (var (button, label, icon) in toolbarButtons)
        {
            var isHistoryButton = ReferenceEquals(button, _backButton) || ReferenceEquals(button, _forwardButton);
            if (!isHistoryButton) button.Text = string.Empty;
            button.Width = toolbarSize;
            button.Height = toolbarSize;
            button.Top = 4;
            button.Left = toolbarLeft;
            button.AccessibleName = label;
            _toolbarTips.SetToolTip(button, label);
            AppIcons.Apply(button, icon, toolbarIcon);
            _themedButtonIcons.Add((button, icon));

            // Once artwork exists the arrow would sit on top of it.
            if (isHistoryButton && button.Image != null) button.Text = string.Empty;

            toolbarLeft += toolbarSize + toolbarGap;

            // Back and Forward are a pair and belong to the reader, not to
            // the analysis tools - the gap says so.
            if (ReferenceEquals(button, _forwardButton)) toolbarLeft += 14;

            // A wider gap after Search: it opens the window someone reaches
            // for most, and grouping the analysis tools apart from it keeps
            // a row of ten identical squares from reading as one undivided
            // block.
            if (ReferenceEquals(button, _searchButton)) toolbarLeft += 14;
        }

        // The four on the right were already icon-only, but had no labels at
        // all - not even a tooltip - so a new icon there was a guess.
        _toolbarTips.SetToolTip(setupWizardButton, "Setup");
        _toolbarTips.SetToolTip(_themeButton, "Light / dark mode");
        _toolbarTips.SetToolTip(_gettingStartedButton, "Getting started");
        _toolbarTips.SetToolTip(_fontSizeButton, "Text size");
        RefreshSyncPanesTooltip();
        _toolbarTips.SetToolTip(_helpButton, "Help  (F1)");
        _toolbarTips.SetToolTip(aboutButton, "About");
        setupWizardButton.AccessibleName = "Setup";
        _themeButton.AccessibleName = "Light / dark mode";
        _gettingStartedButton.AccessibleName = "Getting started";
        _fontSizeButton.AccessibleName = "Text size";
        _syncPanesButton.AccessibleName = "Link the two panes";
        _helpButton.AccessibleName = "Help";
        aboutButton.AccessibleName = "About";

        foreach (var button in new[]
                 {
                     setupWizardButton, _themeButton, _gettingStartedButton,
                     _fontSizeButton, _syncPanesButton, _helpButton, aboutButton
                 })
        {
            button.Width = toolbarSize;
            button.Height = toolbarSize;
            button.Top = 4;
        }

        AppIcons.Apply(_gettingStartedButton, "GettingStarted", toolbarIcon);
        AppIcons.Apply(_fontSizeButton, "FontSize", toolbarIcon);
        _themedButtonIcons.Add((_gettingStartedButton, "GettingStarted"));
        _themedButtonIcons.Add((_fontSizeButton, "FontSize"));

        AppIcons.Apply(setupWizardButton, "Settings", toolbarIcon);
        AppIcons.Apply(aboutButton, "About", toolbarIcon);
        AppIcons.Apply(_helpButton, "Help", toolbarIcon);
        _themedButtonIcons.Add((setupWizardButton, "Settings"));
        _themedButtonIcons.Add((aboutButton, "About"));
        _themedButtonIcons.Add((_helpButton, "Help"));
        Controls.Add(_libraryTree);
        Controls.Add(_treeToggleButton);
        Controls.Add(_treeFilterIcon);
        Controls.Add(_treeFilterBox);
        Controls.Add(_favoritesOnlyCheck);
        Controls.Add(splitContainer);

        Load += async (_, _) =>
        {
            await LoadLibraryTreeAsync();
            await RestoreReadingPositionAsync();
        };

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
            const int labelHeight = 24;
            const int gap = 6;
            const int collapsedToggleWidth = 36;

            // Same reasoning applies to the top-right buttons - pinned here
            // rather than via Anchor, for the identical reason. Left to
            // right: Setup Wizard, theme toggle, Getting Started, text size,
            // link panes, Help, About - built from the right edge inward, so About
            // anchors the chain and each one before it is positioned off the
            // one already placed.
            aboutButton.Left = Math.Max(ClientSize.Width - aboutButton.Width - margin, 0);
            _helpButton.Left = Math.Max(aboutButton.Left - _helpButton.Width - 8, 0);
            _syncPanesButton.Left = Math.Max(_helpButton.Left - _syncPanesButton.Width - 8, 0);
            _fontSizeButton.Left = Math.Max(_syncPanesButton.Left - _fontSizeButton.Width - 8, 0);
            _gettingStartedButton.Left = Math.Max(_fontSizeButton.Left - _gettingStartedButton.Width - 8, 0);
            _themeButton.Left = Math.Max(_gettingStartedButton.Left - _themeButton.Width - 8, 0);
            setupWizardButton.Left = Math.Max(_themeButton.Left - setupWizardButton.Width - 8, 0);

            // Shrinks to just its arrow once collapsed - there's nothing
            // left underneath to line up with, so the full descriptive
            // label would only be clutter.
            _treeToggleButton.Width = collapsedToggleWidth;

            // Reader area starts right after the tree - or right at the
            // window's own left margin if the tree is collapsed, reclaiming
            // its width for reading room.
            var readerAreaLeft = _libraryTreeCollapsed ? 10 : 320;
            splitContainer.Left = readerAreaLeft;

            splitContainer.Width = Math.Max(ClientSize.Width - splitContainer.Left - margin, 400);

            // The reader now runs to the bottom margin: the search results
            // strip that used to sit under it has become its own window, so
            // there's nothing left down there to leave room for.
            splitContainer.Height = Math.Max(ClientSize.Height - margin - splitContainer.Top, 100);
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
        foreach (var menu in _themedContextMenus) ReadingTheme.ApplyToContextMenu(menu);
        ReadingTheme.ApplyToToolTip(_toolbarTips);

        // Same reason: an icon set once at construction time, before this
        // ran for the first time, would otherwise never revisit whichever
        // theme was actually current. Each uses whichever of its two icons
        // matches its current collapsed/expanded state, not just one fixed
        // name, since - unlike the six above - these two can change icons
        // for a reason other than the theme.
        foreach (var (item, iconName) in _themedMenuItemIcons) item.Image = AppIcons.Get(iconName, 16);
        foreach (var (button, iconName) in _themedButtonIcons) AppIcons.Apply(button, iconName, 40);
        AppIcons.Apply(_treeToggleButton, _libraryTreeCollapsed ? "Expand" : "Collapse", 14);
        RefreshSyncPanesIcon();

        // Icon shows what clicking will switch *to*, not the current state -
        // no text label needed, the sun/moon glyph already says it plainly.
        AppIcons.Apply(_themeButton, ReadingTheme.IsDark ? "LightMode" : "DarkMode", 40);

        Invalidate(true);
    }

    /// <summary>
    /// Reopens whatever was last being read.
    ///
    /// Everything here is best-effort by design. The remembered work may
    /// have been removed, the corpus re-ingested with different citation
    /// references, or the position saved against a different database
    /// entirely - all of which simply mean the app opens the way it did
    /// before any of this existed, which is a perfectly good outcome and not
    /// worth a message about.
    /// </summary>
    private async Task RestoreReadingPositionAsync()
    {
        if (!ReadingPosition.ReopenOnLaunch) return;

        var saved = ReadingPosition.Load();
        if (saved == null) return;

        try
        {
            var target = await _textNodeRepo.FindByWorkUrnAndCitationAsync(
                saved.Value.WorkCtsUrn, saved.Value.CitationRef);

            if (target == null) return;

            await NavigateToPassageAsync(target.Value.WorkId, target.Value.TextNodeId);
        }
        catch (Exception)
        {
            // See above - opening at nothing is the fallback, not an error.
        }
    }

    private async Task LoadLibraryTreeAsync()
    {
        List<Author> authors;
        Dictionary<int, List<Work>> worksByAuthor;
        try
        {
            authors = await _authorRepo.GetAllAsync();

            // One query for every work in the library, rather than one per
            // author inside the loop below - with a full corpus that was
            // hundreds of round trips before the tree could render at all.
            worksByAuthor = await _workRepo.GetAllGroupedByAuthorAsync();
            _favoriteUrns = await _favoriteRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't load library: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _allAuthors = authors;
        _worksByAuthor = worksByAuthor;
        PopulateLibraryTree();
    }

    /// <summary>
    /// Builds the tree from what was last loaded, keeping only authors whose
    /// name matches the filter box.
    ///
    /// Matched on the author's own name rather than the displayed label, so
    /// typing "eng" finds an author called English and not every Renaissance
    /// author whose row happens to be tagged "(English)".
    /// </summary>
    private void PopulateLibraryTree()
    {
        var filter = _treeFilterBox.Text.Trim();
        var favoritesOnly = _favoritesOnlyCheck.Checked;

        var authors = filter.Length == 0
            ? _allAuthors
            : _allAuthors
                .Where(a => a.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        _libraryTree.Nodes.Clear();

        // Without this, every Nodes.Add triggers the tree to re-measure and
        // repaint - thousands of times over a full corpus. The reader pane
        // already does the same thing for the same reason.
        _libraryTree.BeginUpdate();
        try
        {
            foreach (var author in authors)
            {
                // The tree is one flat alphabetical list of every author in
                // the library, so a Renaissance name lands between two
                // classical ones with nothing to distinguish it - King
                // James I sits quietly between Isocrates and Jerome. A
                // label on the non-classical corpora makes them findable
                // without reorganizing the tree, which would cost an extra
                // expand on every Greek or Latin lookup to help with a
                // collection that's a fraction of the size.
                var label = author.Namespace switch
                {
                    "engLit" => $"{author.Name}  (English)",
                    "greekLit" or "latinLit" => author.Name,
                    null or "" => author.Name,
                    _ => $"{author.Name}  ({author.Namespace})"
                };

                var authorNode = new TreeNode(label) { Tag = author };

                if (_worksByAuthor.TryGetValue(author.AuthorId, out var works))
                {
                    foreach (var work in works)
                    {
                        var isFavorite = _favoriteUrns.Contains(work.CtsUrn);
                        if (favoritesOnly && !isFavorite) continue;

                        // A star in the label rather than a node image. The
                        // tree has no ImageList and giving it one would put
                        // an icon slot on every author row too, indenting
                        // the whole library to mark a few dozen works.
                        authorNode.Nodes.Add(
                            new TreeNode(isFavorite ? $"\u2605 {work.Title}" : work.Title) { Tag = work });
                    }
                }

                // Under the favourites filter an author with nothing
                // favourited is not an author with an empty expander - they
                // are simply not part of the answer.
                if (favoritesOnly && authorNode.Nodes.Count == 0) continue;

                _libraryTree.Nodes.Add(authorNode);
            }
        }
        finally
        {
            _libraryTree.EndUpdate();
        }

        // Says so rather than showing an empty panel, which reads as a
        // library that failed to load rather than a filter that matched
        // nothing.
        if (_libraryTree.Nodes.Count == 0)
        {
            // Distinguishes an empty result from a library that failed to
            // load. The favourites case gets its own wording because "no
            // author matching" would be wrong when the filter box is empty.
            if (favoritesOnly)
            {
                _libraryTree.Nodes.Add(new TreeNode(filter.Length > 0
                    ? $"No favourites matching \u201c{filter}\u201d"
                    : "No favourites yet - right-click a work to add one"));
            }
            else if (filter.Length > 0)
            {
                _libraryTree.Nodes.Add(new TreeNode($"No author matching \u201c{filter}\u201d"));
            }
        }
    }

    /// <summary>
    /// Opens the vocabulary profile for the selected work.
    ///
    /// Counted from an original-language edition, never a translation: the
    /// point is which Greek or Latin words you need, and the English one
    /// would produce a perfectly accurate frequency list for the wrong
    /// language. Where a work has several originals the first is taken -
    /// they are the same text in different editions, and the vocabulary of
    /// one is the vocabulary of the others bar textual variants.
    /// </summary>
    private async Task ShowVocabularyForSelectedWorkAsync()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;

        try
        {
            var editions = await _editionRepo.GetByWorkAsync(work.WorkId);
            var original = editions.FirstOrDefault(e => e.Kind == EditionKind.Original);

            if (original == null)
            {
                MessageBox.Show(this,
                    "This work has no original-language text loaded, so there is no Greek or Latin "
                    + "vocabulary to count.",
                    "Core Vocabulary", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var authorName = FindWorkNode(work.WorkId)?.Parent?.Text ?? string.Empty;

            using var form = new VocabularyForm(work, authorName, original.EditionId, original.Language);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open the vocabulary list: {ex.Message}",
                "Core Vocabulary", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Adds or removes the selected work from favourites, then rebuilds the
    /// tree so the star appears or disappears.
    ///
    /// Rebuilt from the cached lists rather than reloaded from the database -
    /// only the favourites set changed, and a full reload of a corpus-sized
    /// library to add one star would be felt.
    /// </summary>
    /// <summary>
    /// Opens the attribution editor for the selected work and applies whatever
    /// the reader decided.
    ///
    /// Reloads the work afterwards rather than patching the tree node in place:
    /// the header, the node's own Tag and the reader panes all carry the value,
    /// and three copies updated by hand is three chances to leave one stale.
    /// </summary>
    private async Task EditAttributionForSelectedWorkAsync()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;

        var authorName = _libraryTree.SelectedNode.Parent?.Text ?? string.Empty;
        var current = await _workRepo.GetAttributionAsync(work.WorkId);

        using var form = new AttributionForm(
            authorName, work.Title, current.Status, current.Note, current.SetByUser);

        if (form.ShowDialog(this) != DialogResult.OK) return;

        if (form.ClearOverride)
        {
            await _workRepo.ClearAttributionOverrideAsync(work.WorkId);
        }
        else if (form.Chosen is { } chosen)
        {
            await _workRepo.SetAttributionAsync(work.WorkId, chosen.Status, chosen.Note);
        }
        else
        {
            return;
        }

        var updated = await _workRepo.GetAttributionAsync(work.WorkId);
        work.AttributionStatus = updated.Status;
        work.AttributionNote = updated.Note;
        work.AttributionSetByUser = updated.SetByUser;

        // Only reload the reader when this is the work it is showing.
        if (_openWork?.WorkId == work.WorkId) await LoadEditionSelectorsAsync(work.WorkId);
    }

    /// <summary>Opens persistent research projects for the selected work.</summary>
    private async Task OpenResearchBenchForSelectedWorkAsync()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;
        var author = await _authorRepo.GetByIdAsync(work.AuthorId);
        using var bench = new ResearchBenchForm(work, author?.Name ?? "Unknown author");
        bench.ShowDialog(this);
        if (bench.NavigationTarget is { } target)
            await NavigateToPassageAsync(target.WorkId, target.TextNodeId);
    }

    private async Task ToggleFavoriteForSelectedWorkAsync()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;

        try
        {
            if (_favoriteUrns.Contains(work.CtsUrn))
            {
                await _favoriteRepo.RemoveAsync(work.CtsUrn);
                _favoriteUrns.Remove(work.CtsUrn);
            }
            else
            {
                await _favoriteRepo.AddAsync(work.CtsUrn);
                _favoriteUrns.Add(work.CtsUrn);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't update favourites: {ex.Message}", "Favourites",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var workId = work.WorkId;
        PopulateLibraryTree();

        // The tree was rebuilt, so the old node object is gone - reselect by
        // id rather than holding on to a stale reference.
        var node = FindWorkNode(workId);
        if (node != null)
        {
            _libraryTree.SelectedNode = node;
            node.EnsureVisible();
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
        var copyItem = menu.Items.Add("Copy to Clipboard");
        copyItem.Image = AppIcons.Get("CopyToClipboard", 16);
        copyItem.Click += (_, _) => CopySelectedLineToClipboard(list);
        var inquiryItem = menu.Items.Add("Start inquiry from this passage...");
        inquiryItem.Image = AppIcons.Get("WordStudy", 16);
        inquiryItem.Click += async (_, _) => await StartInquiryForSelectedLineAsync(list);
        menu.Items.Add(new ToolStripSeparator());
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
        var crossLanguageEchoItem = menu.Items.Add("Find Cross-Language Echo...");
        crossLanguageEchoItem.Image = AppIcons.Get("SimilarWorks", 16);
        crossLanguageEchoItem.Click += async (_, _) => await ShowCrossLanguageEchoForSelectedLineAsync(list);
        var receptionItem = menu.Items.Add("Reception History...");
        receptionItem.Image = AppIcons.Get("ReceptionTracker", 16);
        receptionItem.Click += (_, _) => ShowReceptionHistoryForSelectedLine(list);
        var translateItem = menu.Items.Add("Translate...");
        translateItem.Image = AppIcons.Get("Translate", 16);
        translateItem.Click += async (_, _) => await ShowTranslateForSelectedLineAsync(list);
        var wordStudyItem = menu.Items.Add("Word Study...");
        wordStudyItem.Image = AppIcons.Get("WordStudy", 16);
        wordStudyItem.Click += (_, _) => ShowWordStudyForSelectedLine(list);
        var apparatusItem = menu.Items.Add("Editor's Notes...");
        apparatusItem.Image = AppIcons.Get("Concordance", 16);
        apparatusItem.Click += (_, _) => ShowApparatusForSelectedLine(list);

        var exportItem = menu.Items.Add("Export...");
        exportItem.Image = AppIcons.Get("Export", 16);
        exportItem.Click += async (_, _) => await ExportSelectedLineAsync(list, font.Name);

        // Not a per-line action like everything above - a preface belongs to
        // the edition, not the clicked line - so it's appended after a
        // separator and only made visible when one actually exists for
        // whatever's currently loaded in this pane, checked fresh each time
        // the menu opens rather than baked in once at construction.
        menu.Items.Add(new ToolStripSeparator());
        var prefaceItem = menu.Items.Add("View Preface...");
        prefaceItem.Image = AppIcons.Get("Preface", 16);
        prefaceItem.Click += (_, _) => ShowPrefaceForPane(list);
        menu.Opening += (_, _) => prefaceItem.Visible = GetPrefaceMatch(list) != null;

        // What to show beside the text. Built fresh each time the menu opens
        // and only from the kinds this edition actually contains, so a prose
        // history offers nothing about speakers and a play does - the menu
        // describes the document in front of the reader rather than everything
        // the parser can produce.
        var showItem = new ToolStripMenuItem("Show");
        showItem.Image = AppIcons.Get("Show", 16);
        menu.Items.Add(showItem);
        menu.Opening += (_, _) => BuildKindMenu(showItem, list);

        // Registered so the icon is re-resolved when the theme changes: it
        // ships both a dark and a light variant, and the cached image is per
        // theme.
        _themedMenuItemIcons.Add((showItem, "Show"));

        list.ContextMenuStrip = menu;
        _themedContextMenus.Add(menu);
        _themedMenuItemIcons.Add((copyItem, "CopyToClipboard"));
        _themedMenuItemIcons.Add((inquiryItem, "WordStudy"));
        _themedMenuItemIcons.Add((tagItem, "AutoTag"));
        _themedMenuItemIcons.Add((bookmarkItem, "Bookmarks"));
        _themedMenuItemIcons.Add((echoItem, "SimilarWorks"));
        _themedMenuItemIcons.Add((crossLanguageEchoItem, "SimilarWorks"));
        _themedMenuItemIcons.Add((receptionItem, "ReceptionTracker"));
        _themedMenuItemIcons.Add((translateItem, "Translate"));
        _themedMenuItemIcons.Add((wordStudyItem, "WordStudy"));
        _themedMenuItemIcons.Add((apparatusItem, "Concordance"));
        _themedMenuItemIcons.Add((exportItem, "Export"));
        _themedMenuItemIcons.Add((prefaceItem, "Preface"));

        list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var hitIndex = list.IndexFromPoint(e.Location);
            if (hitIndex >= 0) list.SelectOnly(hitIndex);
        };

        return list;
    }

    /// <summary>
    /// Shows whichever of the two icons matches the current state - joined
    /// panes with an arrow between them, or a broken chain.
    ///
    /// Re-run on every theme pass as well as on every toggle, because this
    /// button can change icon for either reason - which is why it is not in
    /// the themed-icon list, the same exception the tree collapse button
    /// makes.
    /// </summary>
    private void RefreshSyncPanesIcon() =>
        AppIcons.Apply(_syncPanesButton, _panesLinked ? "PanesLinked" : "PanesUnlinked", 40);

    /// <summary>
    /// The tooltip names the state and what clicking will do, since the icon
    /// alone asks the reader to notice a broken chain.
    /// </summary>
    private void RefreshSyncPanesTooltip()
    {
        var text = _panesLinked
            ? "Panes are linked - they scroll and select together. Click to unlink."
            : "Panes are independent. Click to link them again.";

        _toolbarTips.SetToolTip(_syncPanesButton, text);
    }

    /// <summary>
    /// Opens the editor's notes for the selected line.
    ///
    /// The edition comes from whichever pane was clicked rather than from the
    /// original pane always: a translation carries its own notes, and Smyth's on
    /// Agamemnon are not Dindorf's.
    /// </summary>
    private void ShowApparatusForSelectedLine(ListBox list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "No line selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var editionLabel = ReferenceEquals(list, _originalPane)
            ? _originalEditionCombo.Text
            : _translationEditionCombo.Text;

        using var form = new ApparatusForm(
            node.EditionId,
            node.CitationRef,
            node.Text,
            string.IsNullOrWhiteSpace(editionLabel) ? "this edition" : editionLabel);

        form.ShowDialog(this);
    }

    /// <summary>
    /// Mirrors scroll position between the two panes by line index. Works
    /// well for verse texts where a translation keeps the same line count as
    /// the original; for prose works where line counts diverge it'll drift,
    /// but that's an inherent limit of index-based sync, not a bug to chase -
    /// which is why the link can be switched off from the toolbar.
    /// </summary>
    /// <summary>
    /// Fills the "Show" submenu with one checkable entry per kind of node this
    /// pane's edition contains.
    ///
    /// Read from the items already loaded rather than from the database. The
    /// pane holds the nodes it rendered, but those are the visible ones - a
    /// kind that has been switched off would disappear from the menu that
    /// switches it back on, which is a trap - so the kinds come from the
    /// edition's full set, refreshed when the pane is.
    ///
    /// Only offered when the edition has more than one kind in it. A work with
    /// nothing but text has nothing to choose between, and a menu of one
    /// permanently-ticked box is noise.
    /// </summary>
    private void BuildKindMenu(ToolStripMenuItem showItem, SyncListView pane)
    {
        showItem.DropDownItems.Clear();

        var kinds = _paneKinds.TryGetValue(pane, out var known)
            ? known
            : new List<string>();

        // Available, not Visible, and the decision held in a local rather than
        // read back off the item.
        //
        // ToolStripItem.Visible's getter is not a mirror of its setter: it
        // returns Available && Parent != null && Parent.Visible, and during
        // Opening the menu has not been shown yet, so the parent is not
        // visible and the getter answers false whatever was just assigned.
        // Reading it back therefore returned early every time and the submenu
        // was never filled - while the setter had correctly marked the item
        // available, so "Show" appeared in the menu and did nothing.
        var hasChoices = kinds.Count > 1;
        showItem.Available = hasChoices;
        if (!hasChoices) return;

        foreach (var kind in NodeKindVisibility.InMenuOrder(kinds))
        {
            var entry = new ToolStripMenuItem(NodeKindVisibility.Label(kind))
            {
                CheckOnClick = true,
                Checked = NodeKindVisibility.IsVisible(kind)
            };

            var captured = kind;
            entry.Click += async (_, _) =>
            {
                NodeKindVisibility.SetVisible(captured, entry.Checked);
                await RefreshReaderPanesAsync();
            };

            showItem.DropDownItems.Add(entry);
        }

        // The way back from a pane hidden to nothing, and from a set of
        // toggles the reader has lost track of.
        showItem.DropDownItems.Add(new ToolStripSeparator());
        var showAll = new ToolStripMenuItem("Show everything")
        {
            Enabled = NodeKindVisibility.AnythingHidden()
        };
        showAll.Click += async (_, _) =>
        {
            NodeKindVisibility.ShowAll();
            await RefreshReaderPanesAsync();
        };
        showItem.DropDownItems.Add(showAll);
    }

    /// <summary>
    /// Re-renders both panes from what they last loaded.
    ///
    /// Both, not just the one clicked: the panes scroll and select together,
    /// and a Greek play showing its speakers opposite an English one that is
    /// not would put the two sides permanently out of step.
    /// </summary>
    private async Task RefreshReaderPanesAsync()
    {
        foreach (var (pane, source) in _paneSource.ToList())
        {
            await PopulateReaderAsync(pane, source.Edition, source.EmptyMessage);
        }
    }

    private void SyncScroll(SyncListView source, SyncListView target)
    {
        if (!_panesLinked) return;
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
        if (index < 0) return;

        // Where you are is recorded whether or not the panes are linked -
        // unlinking them is a statement about the other pane, not about
        // giving up your place in this one. Recorded before the bounds check
        // below, too: a line past the end of the other pane is still a line
        // you clicked.
        RememberReadingPosition(source, index);

        if (!_panesLinked) return;
        if (index >= target.Items.Count) return;

        target.SelectOnly(index);
        target.EnsureVisible(index);
    }

    /// <summary>
    /// Notes where the reader is, for the next launch.
    ///
    /// Hooked to a click rather than to selection changing, for the same
    /// reason the mirroring above is: programmatic selection fires
    /// constantly during jumps and would overwrite a real position with
    /// whatever a search or tag browser happened to land on.
    /// </summary>
    private void RememberReadingPosition(SyncListView pane, int index)
    {
        if (_openWork == null) return;
        if (index < 0 || index >= pane.Items.Count) return;
        if (pane.Items[index] is not TextNode node) return;

        ReadingPosition.Save(_openWork.CtsUrn, node.CitationRef);
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

    private async Task StartInquiryForSelectedLineAsync(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a passage first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var passage = await _textNodeRepo.GetPassageResearchIdentityAsync(node.TextNodeId);
        if (passage == null)
        {
            MessageBox.Show(this, "That passage is no longer present in the local corpus.", "Inquiry",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var inquiry = new PassageInquiryForm(passage);
        if (inquiry.ShowDialog(this) != DialogResult.OK || inquiry.OpenProjectId is not { } projectId)
            return;

        var work = _openWork?.WorkId == passage.WorkId
            ? _openWork
            : _worksByAuthor.Values.SelectMany(works => works)
                .FirstOrDefault(candidate => candidate.WorkId == passage.WorkId);
        if (work == null)
        {
            // The project was created; only the library entry for its work is missing
            // from the loaded tree. Returning silently makes a promotion that succeeded
            // look like a button that did nothing.
            MessageBox.Show(this,
                "The research project was created, but this work is not in the library list at the " +
                "moment, so the Research Bench cannot be opened from here. Open the work from the " +
                "library and choose Research… to find it.",
                "Research project created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var bench = new ResearchBenchForm(work, passage.AuthorName, projectId);
        bench.ShowDialog(this);
        if (bench.NavigationTarget is { } target)
            await NavigateToPassageAsync(target.WorkId, target.TextNodeId);
    }

    /// <summary>
    /// Opens the search window, or brings the existing one forward.
    ///
    /// Non-modal and kept alive between openings: the filters someone has
    /// set are worth more than the memory a closed form would save, and a
    /// modal search would make "look at this result in the reader" require
    /// closing the thing that found it.
    /// </summary>
    private void OpenSearchWindow()
    {
        if (_searchForm is { IsDisposed: false })
        {
            if (_searchForm.WindowState == FormWindowState.Minimized)
            {
                _searchForm.WindowState = FormWindowState.Normal;
            }

            _searchForm.BringToFront();
            _searchForm.Activate();
            return;
        }

        _searchForm = new SearchForm { OnNavigate = NavigateToPassageAsync };
        _searchForm.FormClosed += (_, _) => _searchForm = null;
        _searchForm.Show(this);
    }

    /// <summary>
    /// Opens the translation workbench for the selected work, creating or
    /// resuming a hand-written translation edition for it.
    ///
    /// A hand-written translation gets a name, unlike the AI ones - those
    /// are the same process run twice and a timestamp tells them apart
    /// perfectly well, whereas "my literal draft" is a thing someone chose
    /// to make and should be able to call something.
    /// </summary>
    private Edition? ChooseEdition(
        List<Edition> editions, IReadOnlyDictionary<int, int> lineCounts, string prompt)
    {
        using var chooser = new EditionChoiceForm("Choose an Edition", prompt, editions, lineCounts);
        return chooser.ShowDialog(this) == DialogResult.OK ? chooser.Chosen : null;
    }

    /// <summary>
    /// How a candidate comparison translation is labelled in the workbench's
    /// picker - translator, length, and whether its citation references
    /// actually line up with the text being translated.
    /// </summary>
    private static string DescribeComparison(
        Edition edition, IReadOnlyDictionary<int, int> lineCounts, bool isClosest)
    {
        var who = string.IsNullOrWhiteSpace(edition.Translator)
            ? edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "translation"
            : edition.Translator;

        var lines = lineCounts.TryGetValue(edition.EditionId, out var n) ? $"{n:N0} lines" : "unknown length";

        return isClosest ? $"{who} - {lines} - lines up with this text" : $"{who} - {lines}";
    }

    /// <summary>
    /// Redraws both reader panes at the current configured sizes.
    ///
    /// Replaces the Font rather than mutating it - a Font is immutable, and
    /// SyncListView recomputes its row heights in OnFontChanged, which only
    /// fires on assignment.
    /// </summary>
    private void ApplyReadingFontSize()
    {
        _originalPane.Font = new Font(_originalPane.Font.FontFamily, ReadingFontSettings.SourceSize);
        _translationPane.Font = new Font(_translationPane.Font.FontFamily, ReadingFontSettings.TranslationSize);
        _originalPane.Invalidate();
        _translationPane.Invalidate();
    }

    /// <summary>
    /// Offers a short list of works worth translating first, and opens the
    /// workbench on whichever the reader picks.
    ///
    /// Selects the work in the tree and then defers to
    /// OpenTranslationWorkbenchAsync rather than launching the workbench
    /// directly. That method reads the tree selection and carries a good deal
    /// besides - resuming an existing hand-written edition, ranking the
    /// comparison translations, the no-original guard - and a second launch
    /// path would have to keep pace with all of it.
    /// </summary>
    private async Task ShowStartingPointsAsync()
    {
        using var form = new StartingPointsForm();

        if (form.ShowDialog(this) != DialogResult.OK || form.ChosenWork == null) return;

        var node = FindWorkNodeRevealingIfFiltered(form.ChosenWork.WorkId);
        if (node == null)
        {
            MessageBox.Show(this, "That work is no longer in the library.",
                "Where to start", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _libraryTree.SelectedNode = node;
        node.EnsureVisible();

        await OpenTranslationWorkbenchAsync();
    }

    private async Task OpenTranslationWorkbenchAsync()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;

        try
        {
            var editions = await _editionRepo.GetByWorkAsync(work.WorkId);
            var originals = editions.Where(e => e.Kind == EditionKind.Original).ToList();

            if (originals.Count == 0)
            {
                MessageBox.Show(this, "This work has no original-language text to translate from.",
                    "Translate", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Resume an existing hand-written edition rather than starting a
            // second one - the workbench is for working through a text over
            // time, and silently beginning again would be the opposite.
            var mine = editions.FirstOrDefault(e => e.CtsUrn.Contains(".mine-", StringComparison.Ordinal));

            var lineCounts = new Dictionary<int, int>();
            foreach (var edition in editions)
            {
                lineCounts[edition.EditionId] = await _textNodeRepo.CountByEditionAsync(edition.EditionId);
            }

            Edition source;

            if (mine != null)
            {
                // Which original this translation was built against isn't
                // recorded anywhere, so it's inferred from shared citation
                // references. Getting this wrong on resume would be worse
                // than asking - the passages already written would stop
                // lining up - so a failed inference falls back to asking.
                var inferred = await _textNodeRepo.FindClosestEditionAsync(
                    mine.EditionId, work.WorkId, EditionKind.Original);

                source = originals.FirstOrDefault(e => e.EditionId == inferred)
                         ?? (originals.Count == 1
                             ? originals[0]
                             : ChooseEdition(originals, lineCounts,
                                 "Which text is this translation being made from?"))!;

                if (source == null) return;
            }
            else if (originals.Count == 1)
            {
                source = originals[0];
            }
            else
            {
                var chosen = ChooseEdition(originals, lineCounts,
                    "This work has more than one original-language edition. Which one are you " +
                    "translating from? Every passage you write is filed under that edition's " +
                    "citation references, so this can't be changed later.");

                if (chosen == null) return;
                source = chosen;
            }

            if (mine == null)
            {
                var name = TextPromptForm.Ask(this, "Translate This Work",
                    "What should this translation be called?", "My translation");
                if (string.IsNullOrWhiteSpace(name)) return;

                var editionId = await _editionRepo.UpsertAsync(new Edition
                {
                    WorkId = work.WorkId,
                    CtsUrn = $"{work.CtsUrn}.mine-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Kind = EditionKind.Translation,
                    Language = "eng",
                    Translator = name.Trim(),
                    SourcePath = null
                });

                mine = (await _editionRepo.GetByWorkAsync(work.WorkId)).First(e => e.EditionId == editionId);
            }

            var sourcePassages = await _textNodeRepo.GetByEditionAsync(source.EditionId);
            var existing = (await _textNodeRepo.GetByEditionAsync(mine.EditionId))
                .GroupBy(n => n.CitationRef)
                .ToDictionary(g => g.Key, g => g.First().Text);

            // Anything else that translates this work, yours excluded -
            // checking your work against itself would tell you nothing.
            var others = editions
                .Where(e => e.Kind == EditionKind.Translation && e.EditionId != mine.EditionId)
                .ToList();

            // The one whose citation references overlap the chosen source
            // most is the likeliest match, so it leads the list.
            var closest = await _textNodeRepo.FindClosestEditionAsync(
                source.EditionId, work.WorkId, EditionKind.Translation);

            others = others
                .OrderBy(e => e.EditionId == closest ? 0 : 1)
                .ThenBy(e => e.Translator, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var comparisons = others
                .Select(e => (Edition: e, Label: DescribeComparison(e, lineCounts, e.EditionId == closest)))
                .ToList();

            var authorName = FindWorkNode(work.WorkId)?.Parent?.Text ?? string.Empty;

            using var workbench = new TranslationWorkbenchForm(
                work, authorName, mine.EditionId, source.Language,
                sourcePassages, existing, comparisons,
                async editionId => new PassageAligner(await _textNodeRepo.GetByEditionAsync(editionId)));

            workbench.ShowDialog(this);

            await LoadEditionSelectorsAsync(work.WorkId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open the translation workbench: {ex.Message}",
                "Translate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Opens the details view for whichever work is selected in the library
    /// tree. Guarded even though the menu only offers it on a work node -
    /// the selection can change between the menu opening and the item being
    /// clicked.
    /// </summary>
    private void ShowDetailsForSelectedWork()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;

        using var detailsForm = new WorkDetailsForm(work);
        detailsForm.ShowDialog(this);
    }

    /// <summary>
    /// Same node/pane-language lookup Translate and Word Study already use.
    /// The edition currently loaded in whichever pane was clicked is what
    /// gets excluded from the comparison-work picker inside the form itself
    /// (comparing a work against its own text isn't the question this tool
    /// answers).
    /// </summary>
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

    /// <summary>
    /// Needs the work's Original-kind edition specifically - there has to
    /// be something to translate *from*. A work with only translations
    /// already ingested (rare, but possible for a stray edition-only entry)
    /// gets a clear message instead of a form with nothing to show.
    /// </summary>
    private async Task CreateTranslationForSelectedWorkAsync()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return;

        var editions = await _editionRepo.GetByWorkAsync(work.WorkId);
        var originalEdition = editions.FirstOrDefault(e => e.Kind == EditionKind.Original);
        if (originalEdition == null)
        {
            MessageBox.Show(this,
                $"\"{work.Title}\" has no original-language edition ingested, so there's nothing to " +
                "translate from.",
                "Nothing to translate", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var author = await _authorRepo.GetByIdAsync(work.AuthorId);
        var authorName = author?.Name ?? "Unknown Author";

        using var createTranslationForm = new CreateTranslationForm(
            work, authorName, originalEdition.EditionId, originalEdition.Language, targetLanguage: "eng");
        createTranslationForm.ShowDialog(this);
    }

    private async Task ShowCrossLanguageEchoForSelectedLineAsync(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sourceEdition = ReferenceEquals(list, _originalPane)
            ? (_originalEditionCombo.SelectedItem as EditionOption)?.Edition
            : (_translationEditionCombo.SelectedItem as EditionOption)?.Edition;

        var sourceInfo = await _textNodeRepo.GetTextNodeSourceInfoAsync(node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        using var crossEchoForm = new CrossLanguageEchoForm(
            node, sourceEdition?.Language, authorName, workTitle, sourceEdition?.EditionId ?? -1)
        {
            OnNavigate = NavigateToPassageAsync
        };
        crossEchoForm.ShowDialog(this);
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

    /// <summary>
    /// Same node/pane-language lookup as Word Study below, but now
    /// bidirectional: the counterpart is whichever pane *wasn't* clicked, so
    /// a click in the original pane looks toward the translation (as
    /// before), while a click in the translation pane now looks toward the
    /// original instead of uselessly matching against its own edition. The
    /// counterpart's language also becomes the AI translation target, so
    /// clicking an English line asks for a rendering into the work's
    /// original language rather than English-to-English.
    /// </summary>
    private async Task ShowTranslateForSelectedLineAsync(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var clickedOriginal = ReferenceEquals(list, _originalPane);

        var sourceEdition = clickedOriginal
            ? (_originalEditionCombo.SelectedItem as EditionOption)?.Edition
            : (_translationEditionCombo.SelectedItem as EditionOption)?.Edition;

        var counterpartEdition = clickedOriginal
            ? (_translationEditionCombo.SelectedItem as EditionOption)?.Edition
            : (_originalEditionCombo.SelectedItem as EditionOption)?.Edition;

        var sourceInfo = await _textNodeRepo.GetTextNodeSourceInfoAsync(node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        using var translateForm = new TranslateForm(
            node, sourceEdition?.Language, counterpartEdition?.Language, authorName, workTitle,
            counterpartEdition?.EditionId, counterpartIsTranslation: clickedOriginal);
        translateForm.ShowDialog(this);
    }

    /// <summary>
    /// Opens whatever preface is currently held for this pane - the menu
    /// item that triggers this is only visible when GetPrefaceMatch(list)
    /// is non-null in the first place, so the null case here is just a
    /// safety net, not an expected path.
    /// </summary>
    private void ShowPrefaceForPane(SyncListView list)
    {
        var match = GetPrefaceMatch(list);
        if (match == null) return;

        var edition = ReferenceEquals(list, _originalPane)
            ? (_originalEditionCombo.SelectedItem as EditionOption)?.Edition
            : (_translationEditionCombo.SelectedItem as EditionOption)?.Edition;
        var descriptor = edition != null ? GetEditionDescriptor(edition) : "this edition";

        using var prefaceForm = new PrefaceForm($"Preface \u2014 {descriptor}", match.Value.Text);
        prefaceForm.ShowDialog(this);
    }

    /// <summary>
    /// Plain text only, no citation prefix - Export already exists for the
    /// richer, structured case (citation, edition, bilingual layout). This
    /// is meant to be the fast, no-dialog version for quickly pasting a
    /// line somewhere else.
    /// </summary>
    private void CopySelectedLineToClipboard(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Clipboard.SetText(node.Text);
        }
        catch (Exception ex)
        {
            // The Windows clipboard is a shared, single-owner resource that
            // another process can be holding for a moment (a known,
            // occasional WinForms quirk, not something this app is doing
            // wrong) - worth a clear message instead of an unhandled crash
            // over something this minor.
            MessageBox.Show(this, $"Couldn't copy to the clipboard: {ex.Message}", "Copy failed",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowWordStudyForSelectedLine(SyncListView list)
    {
        if (list.SelectedIndex < 0 || list.Items[list.SelectedIndex] is not TextNode node)
        {
            MessageBox.Show(this, "Select a line first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Which pane the line came from determines its language, and that
        // can't be inferred from the word itself - an English translation
        // and a Latin original are both written in the Latin alphabet.
        var edition = ReferenceEquals(list, _originalPane)
            ? (_originalEditionCombo.SelectedItem as EditionOption)?.Edition
            : (_translationEditionCombo.SelectedItem as EditionOption)?.Edition;

        // Scoped to the work being read, same as from the translation
        // workbench - the corpus-wide count for a common word stops at the
        // result limit and says only that the word is common. Choose Texts
        // widens it.
        using var wordStudyForm = new WordStudyForm(node, edition?.Language, _openWork?.WorkId)
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
        var counterpartEdition = GetSelectedEdition(counterpartPane);
        var counterpartEditionId = counterpartEdition?.EditionId;
        var counterpartIsOriginal = counterpartPane == _originalPane;

        var sourceInfo = await _textNodeRepo.GetTextNodeSourceInfoAsync(node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        using var exportForm = new PassageExportForm(
            node, editionId.Value, authorName, workTitle, fontName,
            counterpartEditionId, counterpartIsOriginal, _originalPane.Font.Name,
            counterpartEdition == null ? null : EditionLabels.Descriptor(counterpartEdition));
        exportForm.ShowDialog(this);
    }

    /// <summary>Which edition is currently loaded in a given pane, based on that pane's combo selection.</summary>
    private Edition? GetSelectedEdition(SyncListView pane)
    {
        var combo = pane == _originalPane ? _originalEditionCombo
            : pane == _translationPane ? _translationEditionCombo
            : null;

        return (combo?.SelectedItem as EditionOption)?.Edition;
    }

    /// <summary>Which edition is currently loaded in a given pane, based on that pane's combo selection.</summary>
    private int? GetSelectedEditionId(SyncListView pane) => GetSelectedEdition(pane)?.EditionId;

    private async Task LibraryTree_AfterSelectAsync(TreeViewEventArgs e)
    {
        // Checked before anything else, and before any await, so that
        // OpenWorkAsync's programmatic selection can reliably suppress the
        // duplicate load it would otherwise cause. See the note there.
        if (_suppressTreeSelectionLoad) return;

        if (e.Node?.Tag is not Work work) return;

        _openWork = work;
        await LoadEditionSelectorsAsync(work.WorkId);

        RecordHistory(work.WorkId, null);
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
    /// one edition of a work from another. Delegates to EditionLabels so the
    /// dropdown and Export cannot drift apart; see the remarks there for why
    /// that mattered.
    /// </summary>
    private static string GetEditionDescriptor(Edition edition) => EditionLabels.Descriptor(edition);

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

    private static string BuildEditionLabel(
        Edition edition, string? authorName, string? workTitle, bool disambiguate = false,
        string? coverageNote = null, AttributionStatus attribution = AttributionStatus.Accepted)
    {
        var descriptor = GetEditionDescriptor(edition);
        if (coverageNote != null) descriptor += $" \u2014 {coverageNote}";

        // Only reached when two or more editions of this work produced the
        // identical descriptor above - which never used to happen (each work
        // had at most one Original edition) until a second data source could
        // add alternate editions of an already-covered work (First1KGreek
        // adding old 19th/20th-century editions alongside canonical-greekLit's
        // own, e.g. Sophocles' Ajax). The CTS URN suffix is the one piece of
        // per-edition identity every edition already carries - not as
        // readable as an editor's name would be, but truthful and guaranteed
        // unique, and needs no new data.
        if (disambiguate)
        {
            var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(suffix)) descriptor += $" ({suffix})";
        }

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

        // Doubted attribution goes next to the author's name, where the claim
        // it qualifies is. Perseus and First1KGreek file the spuria under the
        // author without comment - correctly, since their job is to transmit
        // what the manuscripts say - so without this the reader is told
        // Definitiones is Plato and given no hint that nobody thinks so.
        var doubt = attribution switch
        {
            AttributionStatus.Disputed => " [attribution disputed]",
            AttributionStatus.Spurious => " [not by this author]",
            _ => string.Empty
        };

        return $"{prefix}{doubt} \u2014 {descriptor}";
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
        ComboBox combo, List<Edition> editions, string? authorName, string? workTitle,
        IReadOnlyDictionary<int, string>? coverage = null,
        AttributionStatus attribution = AttributionStatus.Accepted)
    {
        var coverageNotes = coverage ?? new Dictionary<int, string>();

        combo.Items.Clear();

        // Editions of one work that reduce to the same descriptor (two
        // Original-Greek editions of the same play is the case that surfaced
        // this - see BuildEditionLabel) would otherwise show as
        // indistinguishable duplicate rows. Flag just those for the suffix;
        // everyone else's label is untouched, so the ordinary one-edition
        // case still reads exactly as clean as before.
        var collisions = editions
            .GroupBy(GetEditionDescriptor, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToHashSet();

        foreach (var edition in editions
                     .OrderBy(GetEditionDescriptor, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.CtsUrn, StringComparer.OrdinalIgnoreCase))
        {
            coverageNotes.TryGetValue(edition.EditionId, out var coverageNote);

            combo.Items.Add(new EditionOption
            {
                Edition = edition,
                Label = BuildEditionLabel(
                    edition, authorName, workTitle, collisions.Contains(edition), coverageNote,
                    attribution)
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

    /// <summary>
    /// What each pane last loaded, so the kind toggles can re-render it
    /// without knowing which combo it came from or going back to the database.
    /// </summary>
    private readonly Dictionary<SyncListView, (Edition? Edition, string EmptyMessage)> _paneSource = new();

    /// <summary>
    /// Every kind of node each pane's edition contains, hidden ones included.
    ///
    /// Taken from the full set before filtering. Reading it back off the
    /// rendered items would only ever show what is already visible, so a kind
    /// switched off would vanish from the menu that switches it on.
    /// </summary>
    private readonly Dictionary<SyncListView, List<string>> _paneKinds = new();

    private async Task PopulateReaderAsync(SyncListView pane, Edition? edition, string emptyMessage)
    {
        _paneSource[pane] = (edition, emptyMessage);
        _paneKinds[pane] = new List<string>();

        pane.BeginUpdate();
        try
        {
            pane.Items.Clear();

            // Reset up front so switching to an edition with none (or
            // clearing the pane entirely) doesn't leave a stale preface
            // pointing at the previous work.
            SetPrefaceMatch(pane, null);

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

            // Some translations carry a translator's preface as its own
            // citation (Perseus's convention is a trailing ".preface"
            // segment, e.g. "...perseus-eng4.preface") - front matter with
            // nothing on the original side to sync against, so it just sat
            // at the top of the list looking like the play's first line.
            // Held back here and offered through the right-click menu
            // instead, on whichever pane it belongs to.
            var prefaceNodes = nodes.Where(IsPrefaceNode).ToList();
            var bodyNodes = prefaceNodes.Count == 0 ? nodes : nodes.Except(prefaceNodes).ToList();

            _paneKinds[pane] = bodyNodes
                .Select(n => string.IsNullOrWhiteSpace(n.NodeKind) ? TextNodeKinds.Line : n.NodeKind)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Speakers, stage directions, headings and the rest are shown or
            // withheld per the reader's own choice - see NodeKindVisibility.
            // Applied after the preface split so hiding a kind cannot take the
            // preface with it.
            var visibleNodes = NodeKindVisibility.Filter(bodyNodes);

            // Everything the edition has was switched off. Saying so, with the
            // way back, beats a blank pane that reads as a bug - and this is
            // reachable, because the text itself can be hidden.
            if (visibleNodes.Count == 0)
            {
                pane.Items.Add("(everything in this edition is hidden - right-click and use Show to bring it back)");
                return;
            }

            bodyNodes = visibleNodes;

            if (prefaceNodes.Count > 0)
            {
                var combinedText = string.Join(
                    Environment.NewLine + Environment.NewLine, prefaceNodes.Select(n => n.Text));
                SetPrefaceMatch(pane, (prefaceNodes[0].CitationRef, combinedText));
            }

            // One bulk insert rather than a call per line. The node itself
            // is the item (not wrapped in a Tag property), so right-click
            // "Tag this line" and similar features read it straight back
            // out of pane.Items.
            pane.Items.AddRange(bodyNodes.Cast<object>().ToArray());
        }
        finally
        {
            pane.EndUpdate();
        }
    }

    /// <summary>
    /// True for a translator's preface or similar front matter - Perseus's
    /// convention is a citation ending in a "preface" segment (bare
    /// "...perseus-eng4.preface", or "...preface.1", "...preface.2" if the
    /// preface itself was split into several paragraphs), distinct from any
    /// numbered passage. Uses PassageAligner's own URN-stripping so this
    /// stays consistent with how citation refs are read everywhere else,
    /// rather than a second, separately-maintained way of parsing them.
    /// </summary>
    private static bool IsPrefaceNode(TextNode node)
    {
        var passage = PassageAligner.ExtractPassageRef(node.CitationRef);
        return passage.StartsWith("preface", StringComparison.OrdinalIgnoreCase);
    }

    private void SetPrefaceMatch(SyncListView pane, (string CitationRef, string Text)? match)
    {
        if (ReferenceEquals(pane, _originalPane)) _originalPrefaceMatch = match;
        else _translationPrefaceMatch = match;
    }

    private (string CitationRef, string Text)? GetPrefaceMatch(SyncListView pane) =>
        ReferenceEquals(pane, _originalPane) ? _originalPrefaceMatch : _translationPrefaceMatch;

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
        if (await NavigateToPassageAsyncCore(workId, textNodeId)) RecordHistory(workId, textNodeId);
    }

    /// <summary>
    /// The jump itself, without recording it. Split out so Back and Forward
    /// can reuse the whole edition-resolving path without their own
    /// navigation being appended to the list they are walking.
    /// </summary>
    private async Task<bool> NavigateToPassageAsyncCore(int workId, long textNodeId)
    {
        var opened = await OpenWorkAsync(workId);
        if (!opened) return false;

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

        return true;
    }

    /// <summary>
    /// Alt+Left and Alt+Right, wherever focus happens to be.
    ///
    /// ProcessCmdKey rather than a KeyDown handler with KeyPreview: the
    /// reader spends its time inside list controls, and a key handler on the
    /// form would only see these once the list had declined them. The same
    /// pair of keys every browser uses for the same action.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.Left:
                _ = GoHistoryAsync(-1);
                return true;

            case Keys.Alt | Keys.Right:
                _ = GoHistoryAsync(1);
                return true;

            case Keys.Control | Keys.F:
                OpenSearchWindow();
                return true;

            // Ctrl+L for the author filter, the same key every browser uses
            // to jump to the thing you type into to get somewhere.
            case Keys.Control | Keys.L:
                if (_libraryTreeCollapsed) _treeToggleButton.PerformClick();
                _treeFilterBox.Focus();
                _treeFilterBox.SelectAll();
                return true;

            case Keys.F1:
                using (var helpForm = new HelpForm())
                {
                    helpForm.ShowDialog(this);
                }
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Where the reader is now - the selected work, and the selected line
    /// within it if there is one.
    ///
    /// Reads the original pane first and falls back to the translation,
    /// matching how the panes are used: a line is normally selected in one
    /// or the other, not both.
    /// </summary>
    private (int WorkId, long? TextNodeId)? CurrentPosition()
    {
        if (_libraryTree.SelectedNode?.Tag is not Work work) return null;

        foreach (var pane in new[] { _originalPane, _translationPane })
        {
            if (pane.SelectedIndex >= 0
                && pane.SelectedIndex < pane.Items.Count
                && pane.Items[pane.SelectedIndex] is TextNode node)
            {
                return (work.WorkId, node.TextNodeId);
            }
        }

        return (work.WorkId, null);
    }

    /// <summary>
    /// Records a destination, discarding any forward entries - the same
    /// thing a browser does when you follow a link after going back.
    ///
    /// The first record also captures where the reader already was, so that
    /// Back after a single jump returns to the passage being read rather
    /// than finding an empty list.
    /// </summary>
    private void RecordHistory(int workId, long? textNodeId)
    {
        if (_navigatingHistory) return;

        if (_history.Count == 0)
        {
            var start = CurrentPosition();
            if (start != null && start.Value.WorkId != workId)
            {
                _history.Add(start.Value);
                _historyIndex = 0;
            }
        }

        var entry = (workId, textNodeId);

        if (_historyIndex >= 0)
        {
            var current = _history[_historyIndex];
            if (current.Equals(entry)) return;

            // Opening the work already open is not a destination. Without
            // this, jumping twice to the same passage recorded the work a
            // second time - the refine below only fires when the current
            // entry has no line yet - and Back then took two presses to
            // leave a place you had only arrived at once.
            if (textNodeId == null && current.WorkId == workId) return;

            // NavigateToPassageAsync opens the work before selecting the
            // line, so a single jump arrives here twice - once with no line
            // and again with one. The second refines the first rather than
            // being a place of its own, or every jump would cost two presses
            // of Back to undo.
            if (current.WorkId == workId && current.TextNodeId == null && textNodeId != null)
            {
                _history[_historyIndex] = entry;
                RefreshHistoryButtons();
                return;
            }
        }

        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }

        _history.Add(entry);
        _historyIndex = _history.Count - 1;

        if (_history.Count > MaxHistoryEntries)
        {
            _history.RemoveAt(0);
            _historyIndex--;
        }

        RefreshHistoryButtons();
    }

    /// <summary>
    /// Steps back or forward through the history. Delta is -1 or 1.
    /// </summary>
    private async Task GoHistoryAsync(int delta)
    {
        var target = _historyIndex + delta;
        if (target < 0 || target >= _history.Count) return;

        var (workId, textNodeId) = _history[target];

        _navigatingHistory = true;
        try
        {
            var reached = textNodeId != null
                ? await NavigateToPassageAsyncCore(workId, textNodeId.Value)
                : await OpenWorkAsync(workId);

            // A work that has since been removed - by a re-ingest, say -
            // leaves the index where it was rather than moving to a place
            // that no longer exists.
            if (!reached) return;
        }
        finally
        {
            _navigatingHistory = false;
        }

        _historyIndex = target;
        RefreshHistoryButtons();
    }

    private void RefreshHistoryButtons()
    {
        _backButton.Enabled = _historyIndex > 0;
        _forwardButton.Enabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    }

    /// <summary>
    /// Finds a work's node, clearing whichever tree filters are hiding it.
    ///
    /// The tree is a filtered view, but every jump in the application - a
    /// search result, an echo, Back, the starting-points picker - looks the
    /// work up in it. With the favourites filter on, jumping to a work that
    /// isn't favourited found nothing and the jump silently did nothing at
    /// all. The author filter box did the same for anyone outside the letters
    /// typed in it.
    ///
    /// A filter is a statement about what you want to look through, not about
    /// where you are willing to go. So the filter gives way, rather than the
    /// destination.
    ///
    /// Filters are dropped one at a time, favourites first, so as little of
    /// what was set up is discarded as possible - and none of it is touched
    /// when the work is genuinely absent, where clearing them would throw
    /// away the filter and still not arrive anywhere.
    /// </summary>
    private TreeNode? FindWorkNodeRevealingIfFiltered(int workId)
    {
        var node = FindWorkNode(workId);
        if (node != null) return node;

        var known = _worksByAuthor.Values.Any(works => works.Any(w => w.WorkId == workId));
        if (!known) return null;

        if (_favoritesOnlyCheck.Checked)
        {
            _favoritesOnlyCheck.Checked = false;   // rebuilds the tree
            node = FindWorkNode(workId);
            if (node != null) return node;
        }

        if (_treeFilterBox.Text.Length > 0)
        {
            _treeFilterBox.Text = string.Empty;    // rebuilds the tree
            node = FindWorkNode(workId);
        }

        return node;
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
        var workNode = FindWorkNodeRevealingIfFiltered(workId);
        if (workNode == null) return false;

        // Also set here, not only in the tree's own handler: every open that
        // arrives from another window - a search result, an echo, a tagged
        // line - deliberately suppresses that handler, and without this the
        // reader would hold a work that reading-position tracking couldn't
        // name.
        if (workNode.Tag is Work work) _openWork = work;

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

        RecordHistory(workId, null);
        return true;
    }

    /// <summary>
    /// A short "incomplete" note for any AI-generated translation that
    /// doesn't yet cover its whole source, keyed by edition id.
    ///
    /// Restricted to AI translations on purpose. They're the only editions
    /// where a missing line means unfinished work rather than a different
    /// editorial choice - see GetTranslationCoverageAsync - and they're also
    /// the only ones a reader can do something about, by reopening Create
    /// Translation and letting it continue.
    ///
    /// Nothing is added for a complete one: a note on every edition would be
    /// noise, and the absence of a warning is the ordinary case.
    /// </summary>
    private async Task<Dictionary<int, string>> BuildCoverageNotesAsync(
        List<Edition> translations, int workId)
    {
        var notes = new Dictionary<int, string>();

        foreach (var edition in translations.Where(IsAiGenerated))
        {
            try
            {
                var coverage = await _textNodeRepo.GetTranslationCoverageAsync(edition.EditionId, workId);
                if (coverage == null) continue;

                var (translated, sourceTotal) = coverage.Value;
                if (translated >= sourceTotal) continue;

                notes[edition.EditionId] =
                    $"INCOMPLETE: {translated:N0} of {sourceTotal:N0} lines translated";
            }
            catch (Exception)
            {
                // A label decoration is never worth failing the load of a
                // work over - without the note the edition still opens and
                // reads exactly as before.
            }
        }

        return notes;
    }

    /// <summary>
    /// Whether an edition came from this app's own AI translation rather
    /// than from an ingested corpus. Matched on the translator label
    /// CreateTranslationForm writes, with the CTS URN marker it also mints
    /// as a fallback.
    /// </summary>
    private static bool IsAiGenerated(Edition edition) =>
        (edition.Translator?.Contains("AI-generated", StringComparison.OrdinalIgnoreCase) ?? false)
        || edition.CtsUrn.Contains(".ai-", StringComparison.OrdinalIgnoreCase);

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

        // Read fresh rather than cached on the node: the reader can change it
        // from the work's own context menu, and a header still claiming a work
        // is genuine after somebody has just marked it otherwise would be worse
        // than not showing it at all.
        _currentWorkAttribution = await _workRepo.GetAttributionAsync(workId);

        PopulateEditionCombo(_originalEditionCombo, originals, authorName, workTitle,
            attribution: _currentWorkAttribution.Status);
        PopulateEditionCombo(_translationEditionCombo, translations, authorName, workTitle,
            await BuildCoverageNotesAsync(translations, workId),
            _currentWorkAttribution.Status);

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

}
