using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.iOS;

/// <summary>
/// The app's one view, and the stack of pages in it. The main window is the
/// bottom page. Anything the shared code calls Show on goes on top with a Close
/// bar, because Escape and ⌘W are not there to rely on, and anything it calls
/// ShowDialog on goes on top as a card, unless the dialog has no button of its
/// own to close it, when it gets the page and the bar.
///
/// A jump to a passage brings the main window forward over the tool it came
/// from, which stays as it was underneath, so a search is not lost by looking
/// at one of its results. The way back floats over the reader while one waits.
/// </summary>
public sealed class PageHost : Panel
{
    private static PageHost? _current;

    /// <summary>Room kept around a dialog, so it reads as a card on any size of screen.</summary>
    private const double CardMargin = 24;

    private readonly List<(PageWindow Page, Control Frame)> _stack = new();
    private readonly Button _wayBack;
    private readonly StackPanel _wayBackBar;
    private PageWindow? _home;
    private Thickness _safeArea;
    private double _keyboard;

    public PageHost()
    {
        _current = this;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Cc.Toolbar"));

        _wayBack = new Button { Classes = { "accent" }, MaxWidth = 420, Padding = new Thickness(18, 0) };
        _wayBack.Click += (_, _) =>
        {
            if (PageBehindReader is { } page) BringToFront(page);
        };

        var dismiss = new Button { Content = "✕", MinWidth = 44 };
        dismiss.Click += (_, _) => PageBehindReader?.Close();

        _wayBackBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            ZIndex = 1,
            IsVisible = false,
            Children = { _wayBack, dismiss }
        };
        Children.Add(_wayBackBar);
    }

    /// <summary>The tool a jump left behind, when the reader is what is showing.</summary>
    private PageWindow? PageBehindReader =>
        _stack.Count > 1 && ReferenceEquals(_stack[^1].Page, _home) ? _stack[^2].Page : null;

    private void ShowWayBack()
    {
        var page = PageBehindReader;
        _wayBackBar.IsVisible = page != null;
        if (page != null) _wayBack.Content = new TextBlock { Text = "‹ Back to " + page.Title, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    /// <summary>
    /// Keeps every page clear of the status bar and the home indicator. Set on
    /// the pages rather than on this panel, so the bars still show the page colour.
    /// The on-screen keyboard counts too: a page ends where it begins, or the
    /// field being typed in can be underneath it.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var top = Avalonia.Controls.TopLevel.GetTopLevel(this);

        if (top?.InputPane is { } keyboard)
        {
            keyboard.StateChanged += (_, args) =>
            {
                _keyboard = args.NewState == InputPaneState.Open ? Math.Max(0, Bounds.Height - args.EndRect.Top) : 0;
                ApplySafeArea(_safeArea);
            };
        }

        if (top?.InsetsManager is not { } insets) return;

        ApplySafeArea(insets.SafeAreaPadding);
        insets.SafeAreaChanged += (_, args) => ApplySafeArea(args.SafeAreaPadding);
    }

    private Thickness FrameMargin =>
        new(_safeArea.Left, _safeArea.Top, _safeArea.Right, Math.Max(_safeArea.Bottom, _keyboard));

    private void ApplySafeArea(Thickness padding)
    {
        _safeArea = padding;
        foreach (var entry in _stack) entry.Frame.Margin = FrameMargin;
        _wayBackBar.Margin = new Thickness(0, 0, 0, padding.Bottom + 16);
    }

    public static PageHost Current =>
        _current ?? throw new InvalidOperationException("The page host has not been created yet.");

    public PageWindow? Top => _stack.Count == 0 ? null : _stack[^1].Page;

    public IReadOnlyList<PageWindow> PagesOwnedBy(PageWindow owner) =>
        _stack.Where(entry => ReferenceEquals(entry.Page.Owner, owner)).Select(entry => entry.Page).ToList();

    public void Present(PageWindow page)
    {
        if (_stack.Any(entry => ReferenceEquals(entry.Page, page)))
        {
            BringToFront(page);
            return;
        }

        var frame = _stack.Count == 0
            ? page
            : IsCard(page) ? DialogFrame(page) : PageFrame(page);

        if (_stack.Count > 0) _stack[^1].Page.RaiseDeactivated();
        _home ??= page;
        _stack.Add((page, frame));
        frame.Margin = FrameMargin;
        Children.Add(frame);
        ShowWayBack();

        // Opened means laid out and showing, and the shared code relies on
        // that: the main window's handler starts reading the library.
        Dispatcher.UIThread.Post(() =>
        {
            page.RaiseOpened();
            page.RaiseActivated();
        }, DispatcherPriority.Loaded);
    }

    public void Dismiss(PageWindow page)
    {
        var index = _stack.FindIndex(entry => ReferenceEquals(entry.Page, page));
        if (index < 0) return;

        var wasTop = index == _stack.Count - 1;
        Children.Remove(_stack[index].Frame);
        _stack.RemoveAt(index);
        if (wasTop && _stack.Count > 0) _stack[^1].Page.RaiseActivated();
        ShowWayBack();
    }

    public void BringToFront(PageWindow page)
    {
        var index = _stack.FindIndex(entry => ReferenceEquals(entry.Page, page));
        if (index < 0 || index == _stack.Count - 1) return;

        var entry = _stack[index];
        _stack[^1].Page.RaiseDeactivated();
        _stack.RemoveAt(index);
        _stack.Add(entry);
        Children.Remove(entry.Frame);
        Children.Add(entry.Frame);
        page.RaiseActivated();
        ShowWayBack();
    }

    private static bool IsCard(PageWindow page) =>
        page.IsDialog && page is not FeatureWindow { ClosesFromTitleBar: true };

    /// <summary>
    /// A tool: the whole screen, under a bar with its title and a way back. A
    /// dialog on a page says Close, because setup has a Back of its own that
    /// goes to the step before.
    /// </summary>
    private static Control PageFrame(PageWindow page)
    {
        var close = new Button
        {
            Content = page.IsDialog ? "Close" : "‹ Back",
            MinHeight = 44,
            MinWidth = 80,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent
        };
        close.Click += (_, _) => page.Close();

        var title = new TextBlock
        {
            Text = page.Title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 17,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 52 };
        bar.Children.Add(close);
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var barBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        barBorder.Bind(Border.BackgroundProperty, barBorder.GetResourceObservable("Cc.Toolbar"));
        barBorder.Bind(Border.BorderBrushProperty, barBorder.GetResourceObservable("Cc.Rule"));
        DockPanel.SetDock(barBorder, Dock.Top);

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = page
        };
        scroller.SizeChanged += (_, _) => FitWidth(page, scroller);

        // A list showing its selected row is not a reason to move the page.
        page.AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        var dock = new DockPanel { Children = { barBorder, scroller } };
        dock.Bind(BackgroundProperty, dock.GetResourceObservable("Cc.Window"));
        return dock;
    }

    /// <summary>
    /// Lets a tool built for a wide window scroll sideways when it does not fit,
    /// which a few of the research tools do not in portrait. Decided by looking,
    /// because many tools ask for a wider window than they need and fold up
    /// neatly in a narrower one, and those are better left alone.
    /// </summary>
    private static void FitWidth(PageWindow page, ScrollViewer scroller)
    {
        Layoutable laidOut = page;
        laidOut.Width = double.NaN;
        scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        Dispatcher.UIThread.Post(() =>
        {
            if (page.MinWidth <= scroller.Bounds.Width || !RunsOffRight(page)) return;

            laidOut.Width = page.MinWidth;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        }, DispatcherPriority.Background);
    }

    private static bool RunsOffRight(PageWindow page) =>
        page.GetVisualDescendants().OfType<Control>().Any(control =>
            control is TextBlock or Button or ComboBox
            && control.IsEffectivelyVisible
            && control.Bounds.Width > 0
            && control.TranslatePoint(new Point(control.Bounds.Width, 0), page) is { } corner
            && corner.X > page.Bounds.Width + 2
            && !control.GetVisualAncestors().Any(ScrollsItsOwnContent));

    private static bool ScrollsItsOwnContent(Visual visual) =>
        visual is ListBox or TextBox or TreeView or ComboBox or DataGrid
        || visual is ScrollViewer { HorizontalScrollBarVisibility: not ScrollBarVisibility.Disabled };

    /// <summary>A dialog: a card over a dimmed page, as big as it asks for and no bigger than the screen.</summary>
    private static Control DialogFrame(PageWindow page)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Child = page,
            BoxShadow = BoxShadows.Parse("0 12 40 0 #66000000")
        };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Cc.Window"));

        if (!double.IsNaN(page.Width) && page.SizeToContent is not (SizeToContent.Width or SizeToContent.WidthAndHeight))
        {
            card.Width = page.Width;
        }

        if (!double.IsNaN(page.Height) && page.SizeToContent is not (SizeToContent.Height or SizeToContent.WidthAndHeight))
        {
            card.Height = page.Height;
        }

        var dim = new Panel { Background = new SolidColorBrush(Colors.Black, 0.4) };
        dim.Children.Add(card);

        // A size a dialog asks for is honoured only as far as the screen goes.
        dim.SizeChanged += (_, e) =>
        {
            card.MaxWidth = Math.Max(0, e.NewSize.Width - CardMargin);
            card.MaxHeight = Math.Max(0, e.NewSize.Height - CardMargin);
        };

        return dim;
    }
}
