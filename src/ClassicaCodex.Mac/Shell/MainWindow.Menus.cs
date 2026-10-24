using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.About;
using ClassicaCodex.Mac.Features.Alphabet;
using ClassicaCodex.Mac.Features.Apparatus;
using ClassicaCodex.Mac.Features.Arcade;
using ClassicaCodex.Mac.Features.Bookmarks;
using ClassicaCodex.Mac.Features.Compare;
using ClassicaCodex.Mac.Features.Concordance;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Features.Export;
using ClassicaCodex.Mac.Features.Help;
using ClassicaCodex.Mac.Features.Instruments;
using ClassicaCodex.Mac.Features.MedievalHands;
using ClassicaCodex.Mac.Features.Morphology;
using ClassicaCodex.Mac.Features.MythNetwork;
using ClassicaCodex.Mac.Features.PlacesMap;
using ClassicaCodex.Mac.Features.Reactions;
using ClassicaCodex.Mac.Features.Research;
using ClassicaCodex.Mac.Features.Search;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Features.Stylometry;
using ClassicaCodex.Mac.Features.Tags;
using ClassicaCodex.Mac.Features.Timeline;
using ClassicaCodex.Mac.Features.Translation;
using ClassicaCodex.Mac.Features.Vocabulary;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Features.WorkInfo;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// The menu bar, the toolbar, the right-click menus and the one place every
/// tool is dispatched from.
///
/// The Windows app reaches its tools from eighteen toolbar buttons, the
/// library tree's menu and the reader's line menu. All three survive - the
/// line menu item for item, the tree menu item for item - and the menu bar
/// adds what a Mac user expects to find there: every command, with its key.
/// </summary>
public partial class MainWindow
{
    private NativeMenuItem? _backItem;
    private NativeMenuItem? _forwardItem;
    private NativeMenuItem? _linkItem;
    private NativeMenuItem? _marginItem;
    private NativeMenuItem? _sidebarItem;
    private readonly List<(NativeMenuItem Item, bool NeedsWork)> _workItems = new();

    private void WireToolbar()
    {
        SidebarToggle.Click += (_, _) => ToggleSidebar();
        BackButton.Click += async (_, _) => await GoHistoryAsync(-1);
        ForwardButton.Click += async (_, _) => await GoHistoryAsync(1);
        BookmarksButton.Click += (_, _) => OpenTool("bookmarks");
        TagsButton.Click += (_, _) => OpenTool("tags");
        LinkPanesButton.Click += (_, _) => SetPanesLinked(LinkPanesButton.IsChecked == true);
        SearchButton.Click += (_, _) => OpenTool("search");
        GalleryButton.Click += (_, _) => ShowGallery();

        FlyoutBase.SetAttachedFlyout(TextSizeButton, BuildTextSizeFlyout());
        TextSizeButton.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(TextSizeButton);

        var appearance = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var (label, choice) in new[]
                 {
                     ("Follow macOS", AppearanceChoice.System),
                     ("Parchment (light)", AppearanceChoice.Light),
                     ("Lamplight (dark)", AppearanceChoice.Dark)
                 })
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.Radio, GroupName = "appearance" };
            item.Click += (_, _) => ApplyAppearance(choice);
            appearance.Items.Add(item);
        }

        appearance.Opening += (_, _) =>
        {
            var index = (int)MacSettings.Current.Appearance;
            for (var i = 0; i < appearance.Items.Count; i++)
                ((MenuItem)appearance.Items[i]!).IsChecked = i == index;
        };
        AppearanceButton.Flyout = appearance;
    }

    private void WireSidebar()
    {
        Sidebar.WorkSelected += async work => await OnLibraryWorkSelectedAsync(work);
        Sidebar.ToolInvoked += tool => OpenTool(tool.Key);
        Sidebar.WorkMenu = BuildWorkMenu();
    }

    /// <summary>Opens a tool by its catalogue key: from the sidebar, the gallery, the menu bar or the toolbar.</summary>
    public void OpenTool(string key)
    {
        var work = _openWork ?? Sidebar.SelectedWork;

        switch (key)
        {
            case "search": SearchFeature.Open(this); break;
            case "concordance": ConcordanceFeature.Open(this); break;
            case "bookmarks": BookmarksFeature.Open(this); break;
            case "tags": TagsFeature.OpenBrowser(this); break;
            case "compare-translations": CompareFeature.CompareTranslations(this); break;
            case "collate": CompareFeature.Collate(this); break;
            case "starting-points": _ = TranslationFeature.StartingPointsAsync(this); break;
            case "morphology": MorphologyFeature.Open(this); break;
            case "myth-network": MythNetworkFeature.Open(this); break;
            case "places-map": PlacesMapFeature.Open(this); break;
            case "timeline": TimelineFeature.Open(this); break;
            case "stylometry": StylometryFeature.Open(this); break;
            case "stylometry-compare": StylometryFeature.OpenCompareRuns(this); break;
            case "medieval-hands": MedievalHandsFeature.Open(this); break;
            case "antikythera": InstrumentsFeature.OpenAntikythera(this); break;
            case "almagest": InstrumentsFeature.OpenAlmagest(this); break;
            case "harmonics": InstrumentsFeature.OpenHarmonics(this); break;
            case "setup": SetupFeature.Open(this); break;
            case "settings": SettingsFeature.Open(this); break;
            case "help": HelpFeature.Open(this); break;
            case "about": AboutFeature.Open(this); break;
            case "alphabet": AlphabetFeature.Open(this, _originalPane.Edition?.Language); break;
            case "arcade": ArcadeFeature.Open(this); break;
            case "gallery": ShowGallery(); break;

            case "vocabulary" when work != null: _ = VocabularyFeature.OpenAsync(this, work); break;
            case "workbench" when work != null: _ = TranslationFeature.OpenWorkbenchAsync(this, work); break;
            case "create-translation" when work != null: _ = TranslationFeature.CreateTranslationAsync(this, work); break;
            case "research" when work != null: ResearchFeature.OpenBench(this, work, Sidebar.AuthorNameFor(work)); break;
            case "reactions" when work != null:
                if (ReactionsFeature.HasDebate(Sidebar.AuthorNameFor(work), work))
                    ReactionsFeature.Open(this, work, Sidebar.AuthorNameFor(work));
                else
                    _ = Alert.Info(this, "Fictional Ancient Reactions",
                        $"There is no staged debate about {work.Title}. Thirteen debates ship, about twelve works: the Iliad, the Odyssey, the Clouds (twice), the Frogs, the Medea, Oedipus Tyrannus, Herodotus, Thucydides, the Republic, the Aeneid, the Metamorphoses and Cicero.");
                break;

            case "vocabulary" or "workbench" or "create-translation" or "research" or "reactions":
                _ = Alert.Info(this, ToolCatalog.Get(key).Title, "Open a work first - choose one in the library on the left.");
                break;
        }
    }

    /// <summary>Every tool as a tile, for the toolbar's gallery button.</summary>
    private void ShowGallery()
    {
        var window = new ToolGalleryWindow(this);
        window.Show(this);
    }

    /// <summary>The line menu, the Windows reader's item for item.</summary>
    private ContextMenu BuildPaneMenu(ReaderPane pane)
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, string glyph, Func<PassageContext, Task> action, string? gesture = null)
        {
            var item = new MenuItem { Header = header, Icon = Glyphs.Icon(glyph, 14) };
            if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
            item.Click += async (_, _) =>
            {
                if (await PassageContextAsync(pane) is { } passage) await action(passage);
            };
            return item;
        }

        menu.Items.Add(Item("Copy Passage", "Copy", async p => await ClipboardText.SetAsync(this, p.Node.Text)));
        menu.Items.Add(Item("Start Inquiry from This Passage…", "Inquiry", async p =>
        {
            if (await ResearchFeature.StartInquiryAsync(this, p)) pane.AddPassageMark(p.Node.CitationRef, PassageMarks.Inquiry);
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Tag This Line…", "Tags", async p =>
        {
            if (await TagsFeature.TagPassageAsync(this, p)) pane.AddPassageMark(p.Node.CitationRef, PassageMarks.Tag);
        }));
        menu.Items.Add(Item("Bookmark This Line…", "Bookmarks", async p =>
        {
            if (await BookmarksFeature.BookmarkPassageAsync(this, p)) pane.AddPassageMark(p.Node.CitationRef, PassageMarks.Bookmark);
        }));
        menu.Items.Add(Item("Find Echoes…", "Echo", p => { EchoFeature.FindEchoes(this, p); return Task.CompletedTask; }));
        menu.Items.Add(Item("Find Cross-Language Echo…", "CrossLanguage", p => EchoFeature.CrossLanguageAsync(this, p)));
        menu.Items.Add(Item("Reception History…", "Reception", p => { EchoFeature.ReceptionHistory(this, p); return Task.CompletedTask; }));

        var reactions = Item("Fictional Ancient Reactions…", "Reactions", _ =>
        {
            if (_openWork != null) ReactionsFeature.Open(this, _openWork, Sidebar.AuthorNameFor(_openWork));
            return Task.CompletedTask;
        });
        menu.Items.Add(reactions);

        menu.Items.Add(Item("Translate…", "Translate", p => TranslationFeature.TranslatePassageAsync(this, p)));
        menu.Items.Add(Item("Word Study…", "WordStudy", p => { WordStudyFeature.Open(this, p); return Task.CompletedTask; }));
        menu.Items.Add(Item("Editor's Notes…", "EditorNotes", p => { ApparatusFeature.Open(this, p); return Task.CompletedTask; }));
        menu.Items.Add(Item("Export…", "Export", p => ExportFeature.ExportPassageAsync(this, p)));

        var readAloud = Item("Read Aloud", "ReadAloud", p =>
        {
            SpeechService.Speak(p.Node.Text, p.Edition?.Language);
            return Task.CompletedTask;
        });
        var stopReading = new MenuItem { Header = "Stop Reading", Icon = Glyphs.Icon("Stop", 14) };
        stopReading.Click += (_, _) => SpeechService.Stop();
        menu.Items.Add(readAloud);
        menu.Items.Add(stopReading);

        menu.Items.Add(new Separator());

        var preface = new MenuItem { Header = "View Preface…", Icon = Glyphs.Icon("Preface", 14) };
        preface.Click += (_, _) =>
        {
            if (_prefaces.TryGetValue(pane, out var match) && match is { } found)
            {
                var descriptor = pane.Edition != null ? EditionLabels.Descriptor(pane.Edition) : "this edition";
                ApparatusFeature.ShowPreface(this, $"Preface — {descriptor}", found.Text);
            }
        };
        menu.Items.Add(preface);

        var show = new MenuItem { Header = "Show", Icon = Glyphs.Icon("Show", 14) };
        menu.Items.Add(show);

        var margin = new MenuItem { Header = "Citations in the Margin", ToggleType = MenuItemToggleType.CheckBox };
        margin.Click += (_, _) => SetCitationMargin(!CitationMarginSettings.Enabled);
        menu.Items.Add(margin);

        menu.Opening += (_, _) =>
        {
            reactions.IsVisible = OpenWorkHasDebate();
            preface.IsVisible = _prefaces.TryGetValue(pane, out var p) && p != null;
            margin.IsChecked = CitationMarginSettings.Enabled;
            readAloud.IsVisible = SpeechService.IsAvailable;
            stopReading.IsVisible = SpeechService.IsAvailable && SpeechService.IsSpeaking;
            BuildKindMenu(show, pane);
        };

        return menu;
    }

    private bool OpenWorkHasDebate() =>
        _openWork is { } work && ReactionsFeature.HasDebate(Sidebar.AuthorNameFor(work), work);

    private void SetCitationMargin(bool enabled)
    {
        CitationMarginSettings.Enabled = enabled;
        _originalPane.ShowCitationMargin = enabled;
        _translationPane.ShowCitationMargin = enabled;
        RefreshMenuState();
    }

    /// <summary>
    /// One checkable entry per kind of line this pane's edition contains, and
    /// only when it contains more than one - a menu of one permanently ticked
    /// box is noise.
    /// </summary>
    private void BuildKindMenu(MenuItem show, ReaderPane pane)
    {
        show.Items.Clear();
        var kinds = _paneKinds.TryGetValue(pane, out var known) ? known : new List<string>();
        show.IsVisible = kinds.Count > 1;
        if (kinds.Count <= 1) return;

        foreach (var kind in NodeKindVisibility.InMenuOrder(kinds))
        {
            var entry = new MenuItem
            {
                Header = NodeKindVisibility.Label(kind),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = NodeKindVisibility.IsVisible(kind)
            };

            var captured = kind;
            entry.Click += async (_, _) =>
            {
                NodeKindVisibility.SetVisible(captured, !NodeKindVisibility.IsVisible(captured));
                await RefreshReaderPanesAsync();
            };
            show.Items.Add(entry);
        }

        show.Items.Add(new Separator());
        var all = new MenuItem { Header = "Show Everything", IsEnabled = NodeKindVisibility.AnythingHidden() };
        all.Click += async (_, _) =>
        {
            NodeKindVisibility.ShowAll();
            await RefreshReaderPanesAsync();
        };
        show.Items.Add(all);
    }

    /// <summary>The library tree's menu for a work, the Windows one item for item.</summary>
    private ContextMenu BuildWorkMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, string glyph, Func<Work, Task> action)
        {
            var item = new MenuItem { Header = header, Icon = Glyphs.Icon(glyph, 14) };
            item.Click += async (_, _) =>
            {
                if (Sidebar.SelectedWork is { } work) await action(work);
            };
            return item;
        }

        menu.Items.Add(Item("View Details…", "About", w => { WorkInfoFeature.ShowDetails(this, w); return Task.CompletedTask; }));
        menu.Items.Add(Item("Translate This Myself…", "Workbench", w => TranslationFeature.OpenWorkbenchAsync(this, w)));
        menu.Items.Add(Item("Create Translation…", "Sparkle", w => TranslationFeature.CreateTranslationAsync(this, w)));
        menu.Items.Add(Item("Core Vocabulary…", "Vocabulary", w => VocabularyFeature.OpenAsync(this, w)));

        var reactions = Item("Fictional Ancient Reactions…", "Reactions", w =>
        {
            ReactionsFeature.Open(this, w, Sidebar.AuthorNameFor(w));
            return Task.CompletedTask;
        });
        menu.Items.Add(reactions);

        var attribution = Item("Attribution…", "Show", async w =>
        {
            if (!await WorkInfoFeature.EditAttributionAsync(this, w)) return;

            Sidebar.RefreshRows();
            if (_openWork?.WorkId == w.WorkId) await LoadEditionSelectorsAsync(w.WorkId);
        });
        menu.Items.Add(attribution);

        menu.Items.Add(Item("Research…", "Research", w =>
        {
            ResearchFeature.OpenBench(this, w, Sidebar.AuthorNameFor(w));
            return Task.CompletedTask;
        }));

        menu.Items.Add(new Separator());
        var favourite = Item("Add to Favourites", "Star", w => Sidebar.ToggleFavoriteAsync(w));
        menu.Items.Add(favourite);

        menu.Opening += (_, _) =>
        {
            if (Sidebar.SelectedWork is not { } work) return;

            favourite.Header = Sidebar.IsFavorite(work) ? "Remove from Favourites" : "Add to Favourites";
            reactions.IsVisible = ReactionsFeature.HasDebate(Sidebar.AuthorNameFor(work), work);
            attribution.Header = work.AttributionStatus switch
            {
                AttributionStatus.Disputed => "Attribution (disputed)…",
                AttributionStatus.Spurious => "Attribution (not by this author)…",
                _ => "Attribution…"
            };
        };

        return menu;
    }

    private Flyout BuildTextSizeFlyout()
    {
        var source = new Slider { Minimum = ReadingFontSettings.MinimumSize, Maximum = ReadingFontSettings.MaximumSize, Width = 200, Value = ReadingFontSettings.SourceSize };
        var translation = new Slider { Minimum = ReadingFontSettings.MinimumSize, Maximum = ReadingFontSettings.MaximumSize, Width = 200, Value = ReadingFontSettings.TranslationSize };
        var linked = new CheckBox { Content = "Keep the two the same size", IsChecked = ReadingFontSettings.Linked };
        var sourceLabel = new TextBlock { Classes = { "caption" } };
        var translationLabel = new TextBlock { Classes = { "caption" } };

        var updating = false;

        void Refresh()
        {
            updating = true;
            source.Value = ReadingFontSettings.SourceSize;
            translation.Value = ReadingFontSettings.TranslationSize;
            linked.IsChecked = ReadingFontSettings.Linked;
            sourceLabel.Text = $"Greek, Latin and other originals: {ReadingFontSettings.SourceSize:0.#} pt";
            translationLabel.Text = $"Translations: {ReadingFontSettings.TranslationSize:0.#} pt";
            updating = false;
        }

        source.ValueChanged += (_, e) => { if (!updating) ReadingFontSettings.SetSource((float)Math.Round(e.NewValue * 2) / 2); };
        translation.ValueChanged += (_, e) => { if (!updating) ReadingFontSettings.SetTranslation((float)Math.Round(e.NewValue * 2) / 2); };
        linked.IsCheckedChanged += (_, _) => { if (!updating) ReadingFontSettings.SetLinked(linked.IsChecked == true); };

        ReadingFontSettings.Changed += () =>
        {
            Refresh();
            _originalPane.ApplyFontSize();
            _translationPane.ApplyFontSize();
        };

        Refresh();

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(6), Width = 240 };
        panel.Children.Add(new TextBlock { Text = "TEXT SIZE", Classes = { "section" } });
        panel.Children.Add(sourceLabel);
        panel.Children.Add(source);
        panel.Children.Add(translationLabel);
        panel.Children.Add(translation);
        panel.Children.Add(linked);

        return new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
    }

    private void StepTextSize(float delta)
    {
        if (delta == 0)
        {
            ReadingFontSettings.SetSource(ReadingFontSettings.DefaultSize);
            if (!ReadingFontSettings.Linked) ReadingFontSettings.SetTranslation(ReadingFontSettings.DefaultSize);
            return;
        }

        ReadingFontSettings.SetSource(ReadingFontSettings.SourceSize + delta);
        if (!ReadingFontSettings.Linked) ReadingFontSettings.SetTranslation(ReadingFontSettings.TranslationSize + delta);
    }

    /// <summary>
    /// The menu bar. On a Mac this is the global menu at the top of the
    /// screen; elsewhere the window draws it as an ordinary menu bar.
    /// </summary>
    private void BuildMenus()
    {
        var bar = new NativeMenu();

        NativeMenuItem Command(string header, Action action, string? gesture = null)
        {
            var item = new NativeMenuItem(header);
            if (gesture != null) item.Gesture = KeyGesture.Parse(gesture);
            item.Click += (_, _) => action();
            return item;
        }

        NativeMenuItem Tool(string key, string? gesture = null)
        {
            var tool = ToolCatalog.Get(key);
            var item = Command(tool.Title + (key is "settings" or "about" or "help" ? string.Empty : "…"), () => OpenTool(key), gesture);
            if (tool.NeedsWork) _workItems.Add((item, true));
            return item;
        }

        NativeMenuItem Submenu(string header, params NativeMenuItemBase[] items)
        {
            var menu = new NativeMenu();
            foreach (var item in items) menu.Add(item);
            return new NativeMenuItem(header) { Menu = menu };
        }

        var cmd = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

        bar.Add(Submenu("File",
            Command("Open Library…", () => _ = OpenLibraryAsync(), $"{cmd}+O"),
            Command("New Library…", () => _ = CreateLibraryAsync(), $"{cmd}+Shift+N"),
            new NativeMenuItemSeparator(),
            Tool("setup"),
            new NativeMenuItemSeparator(),
            Command("Export Passage…", () => _ = ExportSelectedAsync(), $"{cmd}+E"),
            Command("Close Window", () => Close(), $"{cmd}+W")));

        bar.Add(Submenu("Edit",
            Command("Copy Passage", () => _ = CopySelectedAsync(), $"{cmd}+Shift+C"),
            Command("Filter Library", () => Sidebar.FocusFilter(), $"{cmd}+L"),
            new NativeMenuItemSeparator(),
            Tool("search", $"{cmd}+F")));

        _sidebarItem = Command("Hide Sidebar", ToggleSidebar, $"{cmd}+Alt+S");
        _linkItem = Command("Link the Two Pages", () => SetPanesLinked(!_panesLinked));
        _linkItem.ToggleType = MenuItemToggleType.CheckBox;
        _marginItem = Command("Citations in the Margin", () => SetCitationMargin(!CitationMarginSettings.Enabled));
        _marginItem.ToggleType = MenuItemToggleType.CheckBox;

        bar.Add(Submenu("View",
            _sidebarItem,
            Command("Show Library", () => { if (!MacSettings.Current.SidebarVisible) ToggleSidebar(); Sidebar.ShowMode("library"); }, $"{cmd}+1"),
            Command("Show Tools", () => { if (!MacSettings.Current.SidebarVisible) ToggleSidebar(); Sidebar.ShowMode("tools"); }, $"{cmd}+2"),
            Command("All Tools…", ShowGallery, $"{cmd}+Shift+T"),
            new NativeMenuItemSeparator(),
            _linkItem,
            _marginItem,
            new NativeMenuItemSeparator(),
            Command("Bigger Text", () => StepTextSize(1), $"{cmd}+OemPlus"),
            Command("Smaller Text", () => StepTextSize(-1), $"{cmd}+OemMinus"),
            Command("Default Text Size", () => StepTextSize(0), $"{cmd}+D0"),
            new NativeMenuItemSeparator(),
            Submenu("Appearance",
                Command("Follow macOS", () => ApplyAppearance(AppearanceChoice.System)),
                Command("Parchment (Light)", () => ApplyAppearance(AppearanceChoice.Light)),
                Command("Lamplight (Dark)", () => ApplyAppearance(AppearanceChoice.Dark)))));

        _backItem = Command("Back", () => _ = GoHistoryAsync(-1), $"{cmd}+OemOpenBrackets");
        _forwardItem = Command("Forward", () => _ = GoHistoryAsync(1), $"{cmd}+OemCloseBrackets");
        bar.Add(Submenu("Go",
            _backItem,
            _forwardItem,
            new NativeMenuItemSeparator(),
            Tool("bookmarks", $"{cmd}+Shift+B"),
            Tool("tags"),
            Tool("starting-points")));

        bar.Add(Submenu("Tools",
            Tool("concordance", $"{cmd}+Shift+K"),
            Tool("morphology"),
            Tool("vocabulary"),
            Tool("compare-translations"),
            Tool("collate"),
            new NativeMenuItemSeparator(),
            Tool("myth-network"),
            Tool("places-map"),
            Tool("timeline"),
            new NativeMenuItemSeparator(),
            Tool("stylometry"),
            Tool("stylometry-compare"),
            new NativeMenuItemSeparator(),
            Tool("workbench"),
            Tool("create-translation"),
            Command("AI Translation Settings…", () => OpenTool("settings")),
            new NativeMenuItemSeparator(),
            Tool("medieval-hands"),
            Command("Alphabet Reference…", () => OpenTool("alphabet"))));

        bar.Add(Submenu("Research",
            Tool("research"),
            Tool("reactions")));

        bar.Add(Submenu("Instruments",
            Tool("antikythera"),
            Tool("almagest"),
            Tool("harmonics")));

        bar.Add(Submenu("Help",
            Command("Classica Codex Help", () => OpenTool("help"), $"{cmd}+Shift+OemQuestion"),
            Tool("starting-points"),
            new NativeMenuItemSeparator(),
            Command("About Classica Codex", () => OpenTool("about"))));

        NativeMenu.SetMenu(this, bar);
        RefreshMenuState();
    }

    /// <summary>Keeps the menu bar's checkmarks and enabled states in step with the window.</summary>
    private void RefreshMenuState()
    {
        if (_backItem != null) _backItem.IsEnabled = BackButton.IsEnabled;
        if (_forwardItem != null) _forwardItem.IsEnabled = ForwardButton.IsEnabled;
        if (_linkItem != null) _linkItem.IsChecked = _panesLinked;
        if (_marginItem != null) _marginItem.IsChecked = CitationMarginSettings.Enabled;
        if (_sidebarItem != null) _sidebarItem.Header = MacSettings.Current.SidebarVisible ? "Hide Sidebar" : "Show Sidebar";
    }

    private ReaderPane? FocusedPane =>
        _translationPane.SelectedNode != null && _translationPane.List.IsKeyboardFocusWithin ? _translationPane
        : _originalPane.SelectedNode != null ? _originalPane
        : _translationPane.SelectedNode != null ? _translationPane
        : null;

    private async Task CopySelectedAsync()
    {
        if (FocusedPane is { } pane && pane.SelectedNode is { } node) await ClipboardText.SetAsync(this, node.Text);
    }

    private async Task ExportSelectedAsync()
    {
        var pane = FocusedPane ?? _originalPane;
        if (await PassageContextAsync(pane) is { } passage) await ExportFeature.ExportPassageAsync(this, passage);
    }
}
