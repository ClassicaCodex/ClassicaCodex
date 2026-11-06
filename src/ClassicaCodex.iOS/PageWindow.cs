using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace ClassicaCodex.iOS;

/// <summary>Avalonia's own closing arguments cannot be constructed outside it.</summary>
public sealed class PageClosingEventArgs : CancelEventArgs
{
    public WindowCloseReason CloseReason => WindowCloseReason.WindowClosing;
}

/// <summary>Stands in for the desktop lifetime in shared code. Never implemented, so never matched.</summary>
public interface INoDesktopLifetime
{
    PageWindow? MainWindow { get; set; }
    ShutdownMode ShutdownMode { get; set; }
    IReadOnlyList<PageWindow> Windows { get; }
}

/// <summary>The little of Screens the shared code asks of a window.</summary>
public sealed class PageScreens
{
    public Screen? Primary => PageWindow.Root.Screens?.Primary;

    public Screen? ScreenFromWindow(PageWindow window) => Primary;

    public Screen? ScreenFromVisual(Visual visual) => Primary;
}

/// <summary>
/// What a Window becomes on a device that has none. It offers the members the
/// Mac front end uses, and <see cref="PageHost"/> shows it: a tool as a
/// full-screen page, a dialog as a card over the page behind it.
///
/// Sizes are kept but not obeyed. A tool asking for 980 by 680 would be a
/// letterbox on an iPad, so only a dialog pays any attention to them, and then
/// only up to the size of the screen. The chrome settings are accepted and
/// ignored, because there is no window for them to apply to.
/// </summary>
public class PageWindow : ContentControl
{
    /// <summary>Exists so the shared main window's chrome code compiles. Nothing draws it.</summary>
    public static readonly StyledProperty<IBrush?> TransparencyBackgroundFallbackProperty =
        AvaloniaProperty.Register<PageWindow, IBrush?>(nameof(TransparencyBackgroundFallback));

    /// <summary>A property rather than a plain field because App gives it to every window with a style.</summary>
    public static readonly StyledProperty<WindowIcon?> IconProperty =
        AvaloniaProperty.Register<PageWindow, WindowIcon?>(nameof(Icon));

    private TaskCompletionSource<object?>? _dialogResult;
    private bool _opened;

    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler<PageClosingEventArgs>? Closing;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;

    /// <summary>The real top level: the one view iOS gave the app.</summary>
    internal static Avalonia.Controls.TopLevel Root =>
        Avalonia.Controls.TopLevel.GetTopLevel(PageHost.Current)
        ?? throw new InvalidOperationException("The page host is not on screen yet.");

    /// <summary>Theme lookup goes by exact type, and the tools derive from this.</summary>
    protected override Type StyleKeyOverride => typeof(ContentControl);

    public string? Title { get; set; }
    public PageWindow? Owner { get; private set; }
    public bool IsActive { get; internal set; }
    public bool IsDialog => _dialogResult != null;

    public new double Width { get; set; } = double.NaN;
    public new double Height { get; set; } = double.NaN;
    public new double MinWidth { get; set; }
    public new double MinHeight { get; set; }

    public WindowIcon? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? TransparencyBackgroundFallback
    {
        get => GetValue(TransparencyBackgroundFallbackProperty);
        set => SetValue(TransparencyBackgroundFallbackProperty, value);
    }

    public WindowStartupLocation WindowStartupLocation { get; set; }
    public WindowState WindowState { get; set; } = WindowState.Normal;
    public SizeToContent SizeToContent { get; set; }
    public WindowDecorations WindowDecorations { get; set; }
    public PixelPoint Position { get; set; }
    public bool CanResize { get; set; } = true;
    public bool CanMinimize { get; set; } = true;
    public bool CanMaximize { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = true;
    public bool Topmost { get; set; }
    public bool ExtendClientAreaToDecorationsHint { get; set; }
    public double ExtendClientAreaTitleBarHeightHint { get; set; }
    public IReadOnlyList<WindowTransparencyLevel> TransparencyLevelHint { get; set; } = Array.Empty<WindowTransparencyLevel>();

    public IReadOnlyList<PageWindow> OwnedWindows => PageHost.Current.PagesOwnedBy(this);
    public PageScreens Screens { get; } = new();
    public double RenderScaling => Root.RenderScaling;
    public IStorageProvider StorageProvider => Root.StorageProvider;
    public IClipboard? Clipboard => Root.Clipboard;
    public IFocusManager? FocusManager => Root.FocusManager;

    /// <summary>The page a control is on, or the page on top for one in a popup.</summary>
    public static PageWindow? GetTopLevel(Visual? visual)
    {
        for (var current = visual; current != null; current = current.GetVisualParent())
        {
            if (current is PageWindow page) return page;
        }

        return PageHost.Current.Top;
    }

    public void RequestAnimationFrame(Action<TimeSpan> action) => Root.RequestAnimationFrame(action);

    public void Show() => Show(null);

    public void Show(PageWindow? owner)
    {
        Owner = owner;
        PageHost.Current.Present(this);
    }

    public Task ShowDialog(PageWindow owner) => ShowDialog<object?>(owner);

    public async Task<TResult> ShowDialog<TResult>(PageWindow owner)
    {
        Owner = owner;
        _dialogResult = new TaskCompletionSource<object?>();
        PageHost.Current.Present(this);
        var result = await _dialogResult.Task;
        return result is TResult typed ? typed : default!;
    }

    public void Close() => Close(null);

    public void Close(object? dialogResult)
    {
        var closing = new PageClosingEventArgs();
        OnClosing(closing);
        if (closing.Cancel) return;

        PageHost.Current.Dismiss(this);
        Closed?.Invoke(this, EventArgs.Empty);
        _dialogResult?.TrySetResult(dialogResult);
    }

    protected virtual void OnClosing(PageClosingEventArgs e) => Closing?.Invoke(this, e);

    public void Activate() => PageHost.Current.BringToFront(this);

    public void Hide() => Close();

    internal void RaiseOpened()
    {
        if (_opened) return;
        _opened = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseActivated()
    {
        IsActive = true;
        Activated?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseDeactivated()
    {
        IsActive = false;
        Deactivated?.Invoke(this, EventArgs.Empty);
    }
}
