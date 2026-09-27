using System.Drawing.Drawing2D;

namespace ClassicaCodex.UI;

public enum LineImageZoom
{
    /// <summary>As tall as the panel, scrolling sideways. The default, and the one that reads.</summary>
    FitHeight,

    /// <summary>The whole line at once, however small that makes it - for seeing the shape of a line.</summary>
    FitWidth,

    Half,
    Actual,
    Double
}

/// <summary>
/// Shows one line of a manuscript.
///
/// <b>These are not pictures of pages.</b> A CATMuS image is a single line
/// cropped out of a page - about 4,772 by 170 pixels, a strip 28 times wider
/// than it is tall. Scaled to fit a 700-pixel panel it would be 25 pixels
/// high, which is a smudge; shown at its own size it is five screens wide. So
/// the default fits its height to the panel and scrolls sideways, which is
/// how a palaeographer reads a line anyway, and the other zooms are there for
/// when the question is a different one - Fit the width answers "how long is
/// this line", 200% answers "what exactly is that mark".
/// </summary>
public class LineImagePanel : Panel
{
    private Image? _image;
    private string? _message;
    private LineImageZoom _zoom = LineImageZoom.FitHeight;

    public LineImagePanel()
    {
        AutoScroll = true;
        DoubleBuffered = true;
    }

    public LineImageZoom Zoom
    {
        get => _zoom;
        set
        {
            if (_zoom == value) return;
            _zoom = value;
            Relayout();
        }
    }

    /// <summary>
    /// Shows an image, or a message in its place. Disposes whatever was shown
    /// before - one of these is a few hundred kilobytes of decoded bitmap and
    /// the reader moves through them a line at a time.
    /// </summary>
    public void Show(Image? image, string? message)
    {
        _image?.Dispose();
        _image = image;
        _message = message;
        AutoScrollPosition = new Point(0, 0);
        Relayout();
    }

    private void Relayout()
    {
        AutoScrollMinSize = _image == null ? Size.Empty : Scaled(_image);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // Both fitting modes depend on the panel's size, so a resize changes
        // the scrollable area as well as the painting.
        if (_zoom is LineImageZoom.FitHeight or LineImageZoom.FitWidth) Relayout();
        else Invalidate();
    }

    /// <summary>
    /// The size the image is drawn at.
    ///
    /// The viewport is measured with ClientSize less the scrollbars that are
    /// actually showing, so fitting the height does not produce an image one
    /// scrollbar too tall and then grow a second scrollbar to hold it.
    /// </summary>
    private Size Scaled(Image image)
    {
        var width = Math.Max(1, ClientSize.Width);
        var height = Math.Max(1, ClientSize.Height);

        var factor = _zoom switch
        {
            LineImageZoom.FitHeight => (double)height / image.Height,
            LineImageZoom.FitWidth => (double)width / image.Width,
            LineImageZoom.Half => 0.5,
            LineImageZoom.Actual => 1.0,
            _ => 2.0
        };

        return new Size(
            Math.Max(1, (int)Math.Round(image.Width * factor)),
            Math.Max(1, (int)Math.Round(image.Height * factor)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ReadingTheme.Surface);

        if (_image == null)
        {
            if (string.IsNullOrEmpty(_message)) return;

            // Wrapped inside the panel with a margin, so a two-line
            // explanation does not run off the right edge.
            var bounds = new Rectangle(12, 12, Math.Max(40, ClientSize.Width - 24), Math.Max(20, ClientSize.Height - 24));
            TextRenderer.DrawText(e.Graphics, _message, Font, bounds, ReadingTheme.MutedText,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }

        var size = Scaled(_image);

        // AutoScrollPosition reads back negative, which is exactly the offset
        // a scrolled drawing needs.
        var origin = AutoScrollPosition;

        // Centred when the image is smaller than the panel, so a short line at
        // 50% does not sit jammed against the top-left corner.
        var x = origin.X + Math.Max(0, (ClientSize.Width - size.Width) / 2);
        var y = origin.Y + Math.Max(0, (ClientSize.Height - size.Height) / 2);

        // Bicubic on the way down, nearest-neighbour on the way up: shrinking
        // a photograph wants smoothing, and magnifying one past its own
        // resolution to look at a mark wants the pixels the scribe's ink
        // actually landed on, not an interpolation of them.
        e.Graphics.InterpolationMode = size.Width < _image.Width
            ? InterpolationMode.HighQualityBicubic
            : InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        e.Graphics.DrawImage(_image, new Rectangle(x, y, size.Width, size.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
