using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Styling;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// The reader: the library on the left, the original and its translation
/// side by side, and every tool a menu or a click away.
///
/// The Mac counterpart of MainForm, kept close to it in behaviour and split
/// the same way MainForm's own concerns split - this file is the window and
/// its chrome; MainWindow.Reader.cs is opening works and filling panes, the
/// history and the reading position; MainWindow.Menus.cs is the menu bar,
/// the context menus and the toolbar; MainWindow.Library.cs is choosing and
/// opening the library file itself.
/// </summary>
public partial class MainWindow : Window, IReaderHost
{
    /// <summary>The main window, for dialogs raised from code that has no window of its own.</summary>
    public static MainWindow? Current { get; private set; }

    private readonly ReaderPane _originalPane = new(PaneRole.Original);
    private readonly ReaderPane _translationPane = new(PaneRole.Translation);
    private readonly Grid _readerView;
    private readonly WelcomeView _welcome;
    private readonly NoLibraryView _noLibrary;

    public MainWindow()
    {
        Current = this;
        InitializeComponent();

        _readerView = BuildReaderView();
        _welcome = new WelcomeView(this);
        _noLibrary = new NoLibraryView(this);

        ContentHost.Children.Add(_readerView);
        ContentHost.Children.Add(_welcome);
        ContentHost.Children.Add(_noLibrary);

        if (Art.AppIcon() is { } icon) Icon = new WindowIcon(icon);

        ConfigureChrome();
        WireToolbar();
        WireSidebar();
        WireReader();
        TrackArcadePane();
        BuildMenus();
        ApplyAppearance(MacSettings.Current.Appearance);
        RestoreWindowPlacement();

        KeyDown += OnWindowKeyDown;
        Closing += (_, _) => SaveWindowPlacement();
        Opened += async (_, _) => await StartAsync();
    }

    /// <summary>
    /// Whether to draw the Mac's unified title bar - content under the
    /// traffic lights. On by default on macOS; CC_MAC_LOOK=1 turns it on
    /// elsewhere, which is how the headless tests photograph the Mac layout
    /// from a Linux machine.
    /// </summary>
    public static bool UnifiedTitleBar =>
        OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("CC_MAC_LOOK") == "1";

    private void ConfigureChrome()
    {
        if (!UnifiedTitleBar) return;

        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = (double)Application.Current!.FindResource("Cc.TitleBarHeight")!;

        // The sidebar shows the desktop through it, as Finder's does. Where the
        // system cannot blur behind a window it falls back to the opaque
        // sidebar colour, so nothing reads as a hole.
        //
        // Asked for on a real Mac only: the headless renderer the tests use
        // composites a translucent window over white, which photographs the
        // dark sidebar grey and says nothing about how it looks on a Mac.
        if (OperatingSystem.IsMacOS())
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None };
            Background = Avalonia.Media.Brushes.Transparent;
            this.Bind(TransparencyBackgroundFallbackProperty, this.GetResourceObservable("Cc.Sidebar"));
        }
        else
        {
            SidebarHost.Bind(Border.BackgroundProperty, SidebarHost.GetResourceObservable("Cc.Sidebar"));
        }

        FallbackMenuBar.IsVisible = false;

        // The empty parts of the title-bar row drag the window, as a title bar
        // does. Marked on the background layers, not on the rows above them,
        // so the buttons in the toolbar stay buttons.
        WindowDecorationProperties.SetElementRole(SidebarDragArea, Avalonia.Input.WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(ToolbarDragArea, Avalonia.Input.WindowDecorationsElementRole.TitleBar);

        UpdateTrafficLightInset();
    }

    /// <summary>
    /// With the sidebar hidden, the toolbar reaches the window's left edge,
    /// where the traffic lights are; it steps aside for them.
    /// </summary>
    private void UpdateTrafficLightInset()
    {
        var inset = UnifiedTitleBar && !MacSettings.Current.SidebarVisible ? 78 : 10;
        ToolbarContent.Margin = new Thickness(inset, 0, 12, 0);
    }

    private void ToggleSidebar()
    {
        var settings = MacSettings.Current;
        settings.SidebarVisible = !settings.SidebarVisible;
        ApplySidebarVisibility();
    }

    private void ApplySidebarVisibility()
    {
        var settings = MacSettings.Current;
        SidebarHost.IsVisible = settings.SidebarVisible;
        SidebarSplitter.IsVisible = settings.SidebarVisible;
        Columns.ColumnDefinitions[0].Width = settings.SidebarVisible
            ? new GridLength(Math.Clamp(settings.SidebarWidth, 200, 480))
            : new GridLength(0);
        UpdateTrafficLightInset();
    }

    /// <summary>System, Light or Dark - applied to the whole application, remembered for next time.</summary>
    public void ApplyAppearance(AppearanceChoice choice)
    {
        MacSettings.Current.Appearance = choice;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = choice switch
            {
                AppearanceChoice.Light => ThemeVariant.Light,
                AppearanceChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        AppearanceIcon.Data = Glyphs.Get(choice switch
        {
            AppearanceChoice.Light => "Sun",
            AppearanceChoice.Dark => "Moon",
            _ => "Appearance"
        });
        ToolTip.SetTip(AppearanceButton, choice switch
        {
            AppearanceChoice.Light => "Appearance: Parchment (light)",
            AppearanceChoice.Dark => "Appearance: Lamplight (dark)",
            _ => "Appearance: follows macOS"
        });
    }

    private void RestoreWindowPlacement()
    {
        var settings = MacSettings.Current;
        if (settings.WindowWidth is > 400 and < 10000) Width = settings.WindowWidth.Value;
        if (settings.WindowHeight is > 300 and < 10000) Height = settings.WindowHeight.Value;
        if (settings.WindowX != null && settings.WindowY != null)
        {
            Position = new PixelPoint(settings.WindowX.Value, settings.WindowY.Value);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        ApplySidebarVisibility();
    }

    private void SaveWindowPlacement()
    {
        var settings = MacSettings.Current;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
        }

        if (settings.SidebarVisible) settings.SidebarWidth = Columns.ColumnDefinitions[0].ActualWidth;
        if (_readerView.ColumnDefinitions[0].ActualWidth > 0)
        {
            var total = _readerView.ColumnDefinitions[0].ActualWidth + _readerView.ColumnDefinitions[2].ActualWidth;
            if (total > 0) settings.OriginalShare = _readerView.ColumnDefinitions[0].ActualWidth / total;
        }

        settings.Save();
    }

    /// <summary>
    /// The two pages side by side with a splitter between them, inset from
    /// the window so their shadows have somewhere to fall.
    /// </summary>
    private Grid BuildReaderView()
    {
        var share = Math.Clamp(MacSettings.Current.OriginalShare, 0.2, 0.8);
        var grid = new Grid
        {
            Margin = new Thickness(18, 16, 18, 18),
            ColumnDefinitions = new ColumnDefinitions($"{share:0.###}*,14,{1 - share:0.###}*")
        };

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Avalonia.Media.Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(_translationPane, 2);

        grid.Children.Add(_originalPane);
        grid.Children.Add(splitter);
        grid.Children.Add(_translationPane);
        return grid;
    }

    /// <summary>Which of the three faces the content area shows: no library, the welcome page, or the reader.</summary>
    private void ShowContent(Control which)
    {
        _readerView.IsVisible = ReferenceEquals(which, _readerView);
        _welcome.IsVisible = ReferenceEquals(which, _welcome);
        _noLibrary.IsVisible = ReferenceEquals(which, _noLibrary);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleArcadeKeys(e))
        {
            e.Handled = true;
            return;
        }

        // On a Mac these come from the menu bar's key equivalents. Elsewhere
        // there may be no global menu to deliver them, so the window answers.
        if (OperatingSystem.IsMacOS()) return;

        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!command) return;

        switch (e.Key)
        {
            case Key.F: OpenTool("search"); e.Handled = true; break;
            case Key.L: Sidebar.FocusFilter(); e.Handled = true; break;
            case Key.OemOpenBrackets: _ = GoHistoryAsync(-1); e.Handled = true; break;
            case Key.OemCloseBrackets: _ = GoHistoryAsync(1); e.Handled = true; break;
        }
    }

    // IReaderHost
    Window IReaderHost.Window => this;

    public Work? OpenWork => _openWork;

    public Work? SelectedLibraryWork => Sidebar.SelectedWork;

    public string AuthorNameFor(Work work) => Sidebar.AuthorNameFor(work);

    /// <summary>Every work in the library, for the welcome page's "continue reading".</summary>
    public IEnumerable<Work> LibraryWorks => Sidebar.AllWorks;

    public int LibraryWorkCount => Sidebar.WorkCount;

    public int LibraryAuthorCount => Sidebar.AuthorCount;

    public Task ReloadLibraryAsync() => Sidebar.LoadAsync();

    public async Task ReloadOpenWorkAsync()
    {
        if (_openWork != null) await LoadEditionSelectorsAsync(_openWork.WorkId);
    }

    /// <summary>
    /// After the library has been opened: reads the tree, reopens the passage
    /// last read, and shows the welcome page if there is nothing to reopen.
    /// </summary>
    private async Task StartAsync()
    {
        if (!DbConnectionFactory.IsConfigured)
        {
            ShowContent(_noLibrary);
            return;
        }

        await Sidebar.LoadAsync();
        await _welcome.RefreshAsync();
        ShowContent(_welcome);
        await RestoreReadingPositionAsync();
    }
}
