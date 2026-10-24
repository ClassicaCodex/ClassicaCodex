using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClassicaCodex.Mac.Features.MedievalHands;

internal enum ManuscriptImageZoom
{
    /// <summary>
    /// As tall as the panel, scrolling sideways. The default for a line, and
    /// the one that reads.
    /// </summary>
    FitHeight,

    /// <summary>The whole width at once, however small that makes it - for seeing the shape of a line.</summary>
    FitWidth,

    /// <summary>
    /// The whole image inside the panel, both ways. Right for a leaf, which
    /// is a page-shaped thing that wants to be seen entire; wrong for a line,
    /// where it is the same as fitting the width.
    /// </summary>
    FitAll,

    Half,
    Actual,
    Double
}

/// <summary>
/// Shows one image from a manuscript: a line, or a whole leaf - the Mac
/// counterpart of the Windows ManuscriptImagePanel.
///
/// <b>The two are shaped nothing alike, which is why the zoom matters.</b> A
/// CATMuS image is a single line cropped out of a page - about 4,772 by 170
/// pixels, a strip 28 times wider than it is tall. Scaled to fit a 700-point
/// panel it would be 25 points high, which is a smudge; shown at its own size
/// it is five screens wide. So a line defaults to fitting its height and
/// scrolling sideways, which is how a palaeographer reads one anyway.
///
/// A leaf from the holding library's IIIF service is the opposite - 2,857 by
/// 4,096, taller than it is wide - and wants fitting entire, which is what
/// FitAll is for. The remaining zooms answer different questions again: Fit
/// the width says how long a line is, and 200% says what exactly that mark is.
///
/// On a Mac the presets are where a zoom starts rather than the only places
/// it can be. Two fingers pan, as they do in every Mac document; a pinch, or
/// ⌘ with the scroll wheel, zooms about the point under the pointer; a drag
/// pans with the mouse; a double-click goes to 200% at the point clicked and
/// back. Any of those leaves the presets and is reported as a custom zoom.
/// The scroll bars are the system's overlay ones, which take no room - so the
/// Windows panel's care to fit the height to the client area less a visible
/// scroll bar has nothing left to correct here.
/// </summary>
internal sealed class ManuscriptImageView : UserControl
{
    private const double MinScale = 0.02;
    private const double MaxScale = 12;

    private readonly ScrollViewer _scroller;
    private readonly Image _image;
    private readonly StackPanel _messagePanel;
    private readonly ContentControl _art;
    private readonly TextBlock _message;

    private Bitmap? _bitmap;
    private ManuscriptImageZoom _zoom = ManuscriptImageZoom.FitHeight;

    /// <summary>Set when a pinch, ⌘-scroll, ⌘+ or a double-click has moved the zoom off its preset.</summary>
    private double? _customScale;

    /// <summary>The scale the image is laid out at now, in points per image pixel.</summary>
    private double _shownScale = 1;

    private Point? _dragStart;
    private Vector _dragOffset;

    public ManuscriptImageView()
    {
        _image = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };

        _scroller = new ScrollViewer
        {
            Content = _image,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        _art = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };
        _message = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 440
        };

        // Wrapped inside the panel with a margin, so a two-line explanation
        // does not run off the edge.
        _messagePanel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            IsVisible = false,
            Children = { _art, _message }
        };

        var layers = new Grid();
        layers.Children.Add(_scroller);
        layers.Children.Add(_messagePanel);
        Content = layers;

        this.Bind(BackgroundProperty, this.GetResourceObservable("Cc.Page"));

        // Both fitting modes depend on the panel's size, so a resize changes
        // the scrollable area as well as the drawing.
        SizeChanged += (_, _) =>
        {
            if (_customScale == null) Relayout();
        };

        // On the way down, so ⌘ and the wheel are a zoom before the scroll
        // viewer has taken them for a scroll.
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnify);

        _image.PointerPressed += OnImagePressed;
        _image.PointerMoved += OnImageMoved;
        _image.PointerReleased += (_, e) => EndDrag(e.Pointer);
        _image.PointerCaptureLost += (_, _) => _dragStart = null;
        _image.DoubleTapped += OnImageDoubleTapped;
    }

    /// <summary>Raised whenever the scale on screen changes - a preset, a pinch, or a resize while fitting.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>The preset the reader chose. Setting it leaves any custom zoom.</summary>
    public ManuscriptImageZoom Zoom
    {
        get => _zoom;
        set
        {
            if (_zoom == value && _customScale == null) return;
            _zoom = value;
            _customScale = null;
            Relayout();
        }
    }

    /// <summary>The fitting preset a double-click and ⌘9 come back to: Fit the height for a line, Fit the page for a leaf.</summary>
    public ManuscriptImageZoom FitPreset { get; set; } = ManuscriptImageZoom.FitHeight;

    public bool IsCustomZoom => _customScale != null;

    /// <summary>Points per image pixel, as laid out now.</summary>
    public double Scale => _shownScale;

    public Bitmap? Bitmap => _bitmap;

    public string? Message => _messagePanel.IsVisible ? _message.Text : null;

    /// <summary>The image as laid out, in points - what the reader is looking at.</summary>
    public Size DisplayedSize => _bitmap == null ? default : Scaled(_shownScale);

    /// <summary>How far the image is scrolled, in points.</summary>
    internal Vector Offset => _scroller.Offset;

    /// <summary>
    /// Shows an image, or a message in its place, with optional artwork above
    /// the message. Disposes whatever was shown before - one of these is a few
    /// hundred kilobytes of decoded bitmap, a leaf several megabytes, and the
    /// reader moves through them one at a time.
    /// </summary>
    public void Show(Bitmap? image, string? message, Control? art = null)
    {
        var previous = _bitmap;
        _bitmap = image;

        _image.Source = image;
        _image.IsVisible = image != null;

        // The scroller goes with the image, scroll bars and all: a message
        // has nothing to scroll.
        _scroller.IsVisible = image != null;
        _message.Text = message;
        _message.IsVisible = !string.IsNullOrEmpty(message);
        _art.Content = art;
        _messagePanel.IsVisible = image == null && (art != null || !string.IsNullOrEmpty(message));

        // A new image starts at its top-left corner: a line is read from its
        // beginning, a leaf from its head.
        _scroller.Offset = default;
        Relayout();

        if (!ReferenceEquals(previous, image)) previous?.Dispose();
    }

    /// <summary>
    /// Swaps in a sharper rendition of the image already shown, keeping the
    /// size and the place the reader is looking at - for a leaf fetched again
    /// at a higher resolution once a zoom has gone past what the first fetch
    /// can show.
    /// </summary>
    public void ReplaceKeepingView(Bitmap sharper)
    {
        if (_bitmap == null)
        {
            Show(sharper, null);
            return;
        }

        var shown = Scaled(_shownScale);
        var offset = _scroller.Offset;
        var previous = _bitmap;

        _bitmap = sharper;
        _image.Source = sharper;

        // The same size on screen means a smaller scale for a bigger bitmap.
        // A preset keeps meaning what it says; only a custom zoom is a size.
        if (_customScale != null) _customScale = shown.Width / Math.Max(1, sharper.PixelSize.Width);

        Relayout();
        _scroller.UpdateLayout();
        _scroller.Offset = offset;

        previous.Dispose();
    }

    public void ZoomIn() => ZoomBy(1.25, null);

    public void ZoomOut() => ZoomBy(0.8, null);

    /// <summary>
    /// The Mac's own zoom keys, as Preview has them: ⌘+ and ⌘- step, ⌘0 is
    /// the actual size and ⌘9 fits. True if the key was one of them.
    /// </summary>
    public bool TryHandleZoomKey(KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return false;

        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                ZoomIn();
                return true;
            case Key.OemMinus or Key.Subtract:
                ZoomOut();
                return true;
            case Key.D0 or Key.NumPad0:
                Zoom = ManuscriptImageZoom.Actual;
                return true;
            case Key.D9 or Key.NumPad9:
                Zoom = FitPreset;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Multiplies the scale, keeping the image point under <paramref name="anchor"/> (the middle if null) where it is.</summary>
    public void ZoomBy(double factor, Point? anchor)
    {
        if (_bitmap == null) return;
        SetScale(Math.Clamp(_shownScale * factor, MinScale, MaxScale), anchor, custom: true);
    }

    private void SetScale(double scale, Point? anchor, bool custom, ManuscriptImageZoom? preset = null)
    {
        if (_bitmap == null) return;

        var viewport = Bounds.Size;
        var at = anchor ?? new Point(viewport.Width / 2, viewport.Height / 2);

        // The image pixel under the anchor, before.
        var before = Origin(Scaled(_shownScale), viewport);
        var pixelX = (_scroller.Offset.X + at.X - before.X) / _shownScale;
        var pixelY = (_scroller.Offset.Y + at.Y - before.Y) / _shownScale;

        if (preset != null) _zoom = preset.Value;
        _customScale = custom ? scale : null;
        Relayout();

        // Laid out now rather than at the next pass, so the scroll viewer
        // knows the new extent before it is asked to scroll into it - it
        // clamps an offset to the extent it last measured.
        _scroller.UpdateLayout();

        var size = Scaled(_shownScale);
        var after = Origin(size, viewport);
        var x = after.X + pixelX * _shownScale - at.X;
        var y = after.Y + pixelY * _shownScale - at.Y;

        _scroller.Offset = new Vector(
            Math.Clamp(x, 0, Math.Max(0, size.Width - viewport.Width)),
            Math.Clamp(y, 0, Math.Max(0, size.Height - viewport.Height)));
    }

    /// <summary>Where an image of this size sits in the viewport: centred when it is smaller, so a short line at 50% does not sit jammed against the corner.</summary>
    private static Point Origin(Size size, Size viewport) =>
        new(Math.Max(0, (viewport.Width - size.Width) / 2), Math.Max(0, (viewport.Height - size.Height) / 2));

    private void Relayout()
    {
        if (_bitmap == null)
        {
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _shownScale = _customScale ?? PresetScale();
        var size = Scaled(_shownScale);
        _image.Width = size.Width;
        _image.Height = size.Height;

        // Smoothed on the way down, nearest-neighbour on the way up:
        // shrinking a photograph wants smoothing, and magnifying one past its
        // own resolution to look at a mark wants the pixels the scribe's ink
        // actually landed on, not an interpolation of them. Judged in device
        // pixels, so 100% on a Retina display - two pixels a side for each of
        // the photograph's - counts as magnifying.
        var device = _shownScale * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        RenderOptions.SetBitmapInterpolationMode(_image,
            device < 1 ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None);

        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private double PresetScale()
    {
        if (_bitmap == null) return 1;

        var width = Math.Max(1, Bounds.Width);
        var height = Math.Max(1, Bounds.Height);
        var pixels = _bitmap.PixelSize;

        return _zoom switch
        {
            ManuscriptImageZoom.FitHeight => height / pixels.Height,
            ManuscriptImageZoom.FitWidth => width / pixels.Width,
            ManuscriptImageZoom.FitAll => Math.Min(width / pixels.Width, height / pixels.Height),
            ManuscriptImageZoom.Half => 0.5,
            ManuscriptImageZoom.Actual => 1.0,
            _ => 2.0
        };
    }

    /// <summary>
    /// The image's size at a scale. In pixels, not the bitmap's own Size,
    /// which divides by whatever resolution the PNG declares - and a line
    /// photograph that says it is 300 dpi is still 4,772 pixels of ink.
    /// </summary>
    private Size Scaled(double scale)
    {
        if (_bitmap == null) return default;
        var pixels = _bitmap.PixelSize;
        return new Size(Math.Max(1, Math.Round(pixels.Width * scale)), Math.Max(1, Math.Round(pixels.Height * scale)));
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_bitmap == null) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // A notch of a mouse wheel is a delta of one; a trackpad sends
            // many small ones, which compound to the same thing.
            ZoomBy(Math.Pow(1.2, e.Delta.Y), e.GetPosition(_scroller));
            e.Handled = true;
            return;
        }

        // A line fitted to its height has nowhere to go but sideways, and a
        // mouse wheel only turns one way - so on a strip with no height to
        // scroll, the wheel scrolls along it, as it would in a timeline. A
        // trackpad's sideways swipe is left to the scroll viewer.
        var size = Scaled(_shownScale);
        var vertical = Math.Abs(e.Delta.Y) > Math.Abs(e.Delta.X);
        if (vertical && size.Height <= Bounds.Height + 0.5 && size.Width > Bounds.Width)
        {
            var x = Math.Clamp(_scroller.Offset.X - e.Delta.Y * 50, 0, size.Width - Bounds.Width);
            _scroller.Offset = new Vector(x, _scroller.Offset.Y);
            e.Handled = true;
        }
    }

    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_bitmap == null) return;

        // The trackpad reports the change in magnification since the last
        // event: 0.03 is three per cent larger.
        ZoomBy(Math.Max(0.2, 1 + e.Delta.X), e.GetPosition(_scroller));
        e.Handled = true;
    }

    private void OnImagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_image).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;

        var size = Scaled(_shownScale);
        if (size.Width <= Bounds.Width && size.Height <= Bounds.Height) return;

        _dragStart = e.GetPosition(_scroller);
        _dragOffset = _scroller.Offset;
        e.Pointer.Capture(_image);
        _image.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void OnImageMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start) return;

        var now = e.GetPosition(_scroller);
        _scroller.Offset = _dragOffset - (now - start);
    }

    private void EndDrag(IPointer pointer)
    {
        if (_dragStart == null) return;
        _dragStart = null;
        _image.Cursor = null;
        pointer.Capture(null);
    }

    /// <summary>
    /// 200% at the point clicked, for the question "what exactly is that
    /// mark", and back to the fit for the line or the page as a whole.
    /// </summary>
    private void OnImageDoubleTapped(object? sender, TappedEventArgs e)
    {
        var at = e.GetPosition(_scroller);
        if (_customScale == null && _zoom == FitPreset && _shownScale < 2)
        {
            SetScale(2.0, at, custom: false, ManuscriptImageZoom.Double);
        }
        else
        {
            SetScale(_shownScale, at, custom: false, FitPreset);
        }

        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Kept only while it is on screen.
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
