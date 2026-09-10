namespace ClassicaCodex.UI;

/// <summary>
/// How tall a row of the reader may be, and how to tell without measuring
/// that a passage cannot possibly fit in one.
///
/// Separated from <see cref="SyncListView"/> because the interesting part is
/// arithmetic rather than drawing, and arithmetic can be tested. Measuring
/// text needs a font, a device context and a window; deciding what to do with
/// a measurement needs none of those.
/// </summary>
internal static class ReaderRowHeight
{
    /// <summary>
    /// The tallest row a Win32 owner-draw-variable list box can hold.
    ///
    /// The control keeps each item's height in a single byte, so a height
    /// above 255 is stored as height % 256. Measured against a bare ListBox:
    /// asked 255 it stored 255, asked 256 it stored 0, asked 257 it stored 1,
    /// asked 300 it stored 44, asked 400 it stored 144, asked 511 it stored
    /// 255, asked 21,861 it stored 101, asked 75,636 it stored 116.
    ///
    /// That is why the symptom read as arbitrary rather than as a ceiling: a
    /// paragraph wanting 300px showed two lines of itself while the longer one
    /// beneath it, wanting 511px, showed all fifteen.
    /// </summary>
    internal const int Max = 255;

    /// <summary>
    /// Stored in place of a real height for a row already known not to fit,
    /// where the exact figure cannot change what the control does with it and
    /// so is not worth a text layout.
    /// </summary>
    internal const int AtLeastTheMax = Max + 1;

    /// <summary>What the control will actually be given for a row this tall.</summary>
    internal static int Cap(int uncappedHeight) => uncappedHeight < Max ? uncappedHeight : Max;

    /// <summary>
    /// Whether a row this tall holds more than it can show - the question the
    /// reader needs answered, since the answer is not visible in the row.
    /// </summary>
    internal static bool ExceedsMax(int uncappedHeight) => uncappedHeight > Max;

    /// <summary>
    /// The fewest lines the text could occupy, given the narrowest glyph in
    /// the font.
    ///
    /// A lower bound, not an estimate: the text takes at least
    /// length * narrowest pixels in total, so it needs at least that many
    /// pixels' worth of lines. Real text uses wider glyphs and so takes more
    /// lines, never fewer - which is the direction that makes this safe to
    /// act on.
    /// </summary>
    internal static long MinimumLines(int textLength, int narrowestGlyphWidth, int width)
    {
        if (textLength <= 0) return 0;

        var usableWidth = width > 0 ? width : 1;
        var glyph = narrowestGlyphWidth > 0 ? narrowestGlyphWidth : 1;
        var totalPixels = (long)textLength * glyph;

        return (totalPixels + usableWidth - 1) / usableWidth;
    }

    /// <summary>
    /// Whether the text cannot fit a row however it wraps, decided by
    /// arithmetic alone.
    ///
    /// Worth having because the passages this answers for are exactly the
    /// expensive ones: a word-wrap layout of a long paragraph costs
    /// milliseconds, and for these the result would be discarded by
    /// <see cref="Cap"/> anyway. The bound is a floor
    /// (<see cref="MinimumLines"/>), so a true answer here is always true;
    /// a false answer only means "measure it and see".
    ///
    /// Verified against the real corpus rather than argued: over 44,152
    /// passages sampled at five pane widths in both reader fonts, every
    /// passage this returned true for did in fact need more than
    /// <see cref="Max"/> pixels. The single passage where the bound ran ahead
    /// of the measured height was Optatianus Porfyrius's grid poem, whose
    /// longest unbroken run is 334 characters - a layout 2,997px wide in a
    /// 380px pane, which GDI declines to break and draws clipped. It holds
    /// more text than it shows either way.
    /// </summary>
    internal static bool CannotFit(int textLength, int narrowestGlyphWidth, int width, int fontHeight)
    {
        var lineHeight = fontHeight > 0 ? fontHeight : 1;
        return MinimumLines(textLength, narrowestGlyphWidth, width) * lineHeight + 6 > Max;
    }
}
