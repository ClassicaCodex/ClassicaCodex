using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassicaCodex.iOS;

/// <summary>
/// The app's one view, and the stack of pages in it. The main window is the
/// bottom page. Anything the shared code calls Show on goes on top with a Close
/// bar, because Escape and ⌘W are not there to rely on, and anything it calls
/// ShowDialog on goes on top as a card.
/// </summary>
public sealed class PageHost : Panel
{
    private static PageHost? _current;

    private readonly List<(PageWindow Page, Control Frame)> _stack = new();
    private Thickness _safeArea;

    public PageHost()
    {
        _current = this;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Cc.Toolbar"));
    }

    /// <summary>
    /// Keeps every page clear of the status bar and the home indicator. Set on
    /// the pages rather than on this panel, so the bars still show the page colour.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Avalonia.Controls.TopLevel.GetTopLevel(this)?.InsetsManager is not { } insets) return;

        ApplySafeArea(insets.SafeAreaPadding);
        insets.SafeAreaChanged += (_, args) => ApplySafeArea(args.SafeAreaPadding);
    }

    private void ApplySafeArea(Thickness padding)
    {
        _safeArea = padding;
        foreach (var child in Children) child.Margin = padding;
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
            : page.IsDialog ? DialogFrame(page) : PageFrame(page);

        if (_stack.Count > 0) _stack[^1].Page.RaiseDeactivated();
        _stack.Add((page, frame));
        frame.Margin = _safeArea;
        Children.Add(frame);

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
    }

    /// <summary>A tool: the whole screen, under a bar with its title and a way back.</summary>
    private static Control PageFrame(PageWindow page)
    {
        var close = new Button
        {
            Content = "‹ Back",
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

        var dock = new DockPanel { Children = { barBorder, page } };
        dock.Bind(BackgroundProperty, dock.GetResourceObservable("Cc.Window"));
        return dock;
    }

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
        return dim;
    }
}
