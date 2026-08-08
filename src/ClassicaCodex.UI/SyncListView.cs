using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// A read-only list of TextNodes that word-wraps each line to the control's
/// width instead of truncating it - which a plain ListView can't do (its
/// row height is fixed, not variable per item). Built on ListBox instead,
/// since ListBox natively supports per-item measurement via OwnerDrawVariable.
///
/// Items are TextNode objects directly, not wrapper objects - callers add
/// nodes straight into .Items and read them back the same way, rather than
/// going through a Tag property on a wrapper as ListViewItem required.
///
/// Also exposes TopItemChanged (for scroll sync) and a citation-ref tooltip
/// on hover, replacing what ListView gave for free.
/// </summary>
public class SyncListView : ListBox
{
    private readonly ToolTip _toolTip = new();
    private int _lastTooltipIndex = -2;

    private const int WM_VSCROLL = 0x115;
    private const int WM_MOUSEWHEEL = 0x20A;

    /// <summary>Raised when the scroll position changes - mouse wheel or scrollbar drag.</summary>
    public event EventHandler? TopItemChanged;

    public SyncListView()
    {
        DrawMode = DrawMode.OwnerDrawVariable;
        IntegralHeight = false;
        DrawItem += OnDrawItem;
        MeasureItem += OnMeasureItem;
        MouseMove += OnMouseMoveForTooltip;

        // Long enough that a drag's intermediate widths are all skipped,
        // short enough that letting go feels like it snaps straight to the
        // right layout rather than lagging behind.
        _resizeDebounceTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _resizeDebounceTimer.Tick += (_, _) =>
        {
            _resizeDebounceTimer.Stop();
            RemeasureAllItems();
        };
    }

    private readonly System.Windows.Forms.Timer _resizeDebounceTimer;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Dispose();
            _toolTip.Dispose();
            _athetizedFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
        {
            TopItemChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isRemeasuring;
    private int _lastMeasuredWidth = -1;

    /// <summary>
    /// Width of the widest glyph likely to appear in this font, cached so
    /// OnMeasureItem can cheaply rule out wrapping - see the note there for
    /// why that matters. Recomputed whenever the font changes; -1 means
    /// "not yet computed".
    /// </summary>
    private int _maxGlyphWidth = -1;
    private int _minGlyphWidth = -1;

    /// <summary>
    /// Measured heights, keyed by the width they were measured at and then
    /// by the line's text. Word-wrap measurement is the expensive part of
    /// showing a prose translation, and the widths this control gets asked
    /// about repeat constantly in normal use - collapsing the library tree
    /// and expanding it again returns to exactly the width measured a
    /// moment ago, as does maximizing and restoring the window. Caching
    /// makes the return trip free instead of a full remeasure.
    ///
    /// Only a few widths are kept: this exists to make repeated widths
    /// cheap, not to remember every width a drag passed through, and an
    /// unbounded cache on a full-corpus work would hold a lot of entries
    /// for no benefit. Text strings are the same references already held in
    /// Items, so the entries cost a dictionary slot and an int, not a copy.
    /// </summary>
    private readonly Dictionary<int, Dictionary<string, int>> _heightCacheByWidth = new();
    private readonly Queue<int> _cachedWidthOrder = new();
    private const int MaxCachedWidths = 4;

    private Dictionary<string, int> GetHeightCacheForCurrentWidth(int width)
    {
        if (_heightCacheByWidth.TryGetValue(width, out var cache)) return cache;

        cache = new Dictionary<string, int>(StringComparer.Ordinal);
        _heightCacheByWidth[width] = cache;
        _cachedWidthOrder.Enqueue(width);

        while (_cachedWidthOrder.Count > MaxCachedWidths)
        {
            _heightCacheByWidth.Remove(_cachedWidthOrder.Dequeue());
        }

        return cache;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        // Both caches are font-specific: a theme or reading-font change
        // invalidates every measured height and the glyph-width bound alike.
        _maxGlyphWidth = -1;
        _minGlyphWidth = -1;
        _heightCacheByWidth.Clear();
        _cachedWidthOrder.Clear();
        _lastMeasuredWidth = -1;
    }

    /// <summary>
    /// Measures a handful of deliberately wide glyphs and keeps the largest.
    /// Sampling rather than scanning the whole font because this only needs
    /// to be an upper bound, not an exact maximum - it's used to prove a
    /// line CAN'T wrap, so overestimating is safe and underestimating is
    /// what would cause trouble.
    /// </summary>
    private int GetMaxGlyphWidth()
    {
        if (_maxGlyphWidth > 0) return _maxGlyphWidth;
        MeasureGlyphBounds();
        return _maxGlyphWidth;
    }

    private int GetMinGlyphWidth()
    {
        if (_minGlyphWidth > 0) return _minGlyphWidth;
        MeasureGlyphBounds();
        return _minGlyphWidth;
    }

    /// <summary>
    /// Measures a few deliberately wide and deliberately narrow glyphs and
    /// keeps the extremes. These are bounds, not exact figures - one proves
    /// a line can't wrap, the other proves it must - so overestimating the
    /// max and underestimating the min are both safe directions.
    /// </summary>
    private void MeasureGlyphBounds()
    {
        var widest = 0;
        // Latin caps, Greek caps, and an em dash - the widest things this
        // corpus realistically contains.
        foreach (var sample in new[] { "W", "M", "Ω", "Δ", "—" })
        {
            var width = TextRenderer.MeasureText(sample, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width;
            if (width > widest) widest = width;
        }

        var narrowest = int.MaxValue;
        foreach (var sample in new[] { "i", "l", ".", "ι" })
        {
            var width = TextRenderer.MeasureText(sample, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width;
            if (width < narrowest) narrowest = width;
        }

        _maxGlyphWidth = Math.Max(widest, 1);
        _minGlyphWidth = Math.Max(narrowest == int.MaxValue ? 1 : narrowest, 1);
    }

    /// <summary>
    /// ListBox doesn't re-measure existing items just because the control
    /// was resized - word-wrap width depends on that width, so force a
    /// remeasure by re-adding everything whenever it changes (e.g. dragging
    /// the split container divider, or resizing the window).
    ///
    /// Guarded two ways, both load-bearing: forcing a remeasure can itself
    /// toggle the scrollbar and trigger another Resize, so without the
    /// reentrancy flag this could recurse and lock up the UI thread on a
    /// long work - which is exactly what was happening before this guard
    /// existed. The width-unchanged check skips the (expensive, full
    /// Items.Clear()+AddRange) remeasure entirely for any Resize that isn't
    /// an actual width change.
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_isRemeasuring) return;
        if (Items.Count == 0) return;
        if (ClientSize.Width == _lastMeasuredWidth) return;

        // Deliberately NOT remeasuring inline. Dragging a window edge or a
        // splitter fires Resize continuously - dozens of times a second -
        // and a remeasure is a full Clear()+AddRange() that re-measures
        // every item. On a prose translation, where essentially every item
        // wraps and so needs real word-wrap layout, doing that per drag
        // frame makes the window feel like it can barely be moved.
        //
        // Restarting the timer on each Resize instead means the intermediate
        // widths are skipped entirely and exactly one remeasure runs, once
        // the drag settles. Item heights are briefly stale during the drag -
        // text still wraps correctly since OnDrawItem re-wraps at the
        // current width, but row heights lag until the timer fires - which
        // is a far better trade than an unusable resize.
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    private void RemeasureAllItems()
    {
        if (_isRemeasuring) return;
        if (Items.Count == 0) return;
        if (ClientSize.Width == _lastMeasuredWidth) return;

        _lastMeasuredWidth = ClientSize.Width;
        _isRemeasuring = true;
        try
        {
            var items = new object[Items.Count];
            Items.CopyTo(items, 0);

            // Preserved across the rebuild - Clear() drops both, and losing
            // your place in the text every time the window is resized would
            // be its own bug.
            var selectedIndex = SelectedIndex;
            var topIndex = TopIndex;

            BeginUpdate();
            Items.Clear();
            Items.AddRange(items);
            EndUpdate();

            if (selectedIndex >= 0 && selectedIndex < Items.Count) SelectedIndex = selectedIndex;
            if (topIndex >= 0 && topIndex < Items.Count) TopIndex = topIndex;
        }
        finally
        {
            _isRemeasuring = false;
        }
    }

    private void OnMeasureItem(object? sender, MeasureItemEventArgs e)
    {
        var text = GetItemText(e.Index);
        if (text.Length == 0)
        {
            e.ItemHeight = Font.Height + 4;
            return;
        }

        var width = Math.Max(ClientSize.Width - 8, 50);

        // OwnerDrawVariable means this runs once for EVERY item as the list
        // is populated - the control needs the total height before it can
        // size its scrollbar, so there's no virtualization to fall back on.
        // A full-corpus work is tens of thousands of lines, and a word-wrap
        // measurement each is what makes opening one slow.
        //
        // Most lines of verse are far too short to wrap at any sane reader
        // width, so rule that out arithmetically first: if even the widest
        // glyph in the font repeated for the whole string would still fit,
        // the line cannot wrap, and its height is exactly one line. That's
        // an integer multiply instead of a GDI text-layout call. The bound
        // is deliberately pessimistic, so a pass is always correct; only
        // lines that might genuinely wrap fall through to real measurement.
        // OwnerDrawVariable means this runs once for EVERY item as the list
        // is populated - the control needs the total height before it can
        // size its scrollbar, so there's no virtualization to fall back on.
        // Word-wrap layout is by far the expensive part, so the goal here is
        // to answer "does this wrap?" without paying for it wherever
        // possible.
        //
        // Tier 1: if even the widest glyph repeated for the whole string
        // would fit, it cannot wrap. Pure arithmetic, no measurement.
        var maxPossibleWidth = text.Length * GetMaxGlyphWidth();
        if (maxPossibleWidth <= width)
        {
            e.ItemHeight = Font.Height + 6;
            return;
        }

        var cache = GetHeightCacheForCurrentWidth(width);
        if (cache.TryGetValue(text, out var cachedHeight))
        {
            e.ItemHeight = cachedHeight;
            return;
        }

        // Tier 2: if even the narrowest glyph repeated for the whole string
        // would overflow, it must wrap - so skip straight to real layout
        // rather than paying for a single-line measurement that can only
        // confirm what's already known. This is what keeps long prose from
        // being measured twice.
        var minPossibleWidth = text.Length * GetMinGlyphWidth();
        if (minPossibleWidth <= width)
        {
            // Tier 3: genuinely ambiguous - somewhere between "all narrow
            // glyphs" and "all wide glyphs". Measure as a single line, which
            // is markedly cheaper than word-wrap layout because it never has
            // to search for break opportunities. Most verse lines land here,
            // being too long for tier 1 but nowhere near wrapping.
            var singleLine = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);

            if (singleLine.Width <= width)
            {
                var singleLineHeight = Math.Max(singleLine.Height + 6, Font.Height + 6);
                cache[text] = singleLineHeight;
                e.ItemHeight = singleLineHeight;
                return;
            }
        }

        var size = TextRenderer.MeasureText(text, Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        var height = Math.Max(size.Height + 6, Font.Height + 6);
        cache[text] = height;
        e.ItemHeight = height;
    }

    private void OnDrawItem(object? sender, DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0;

        // Theme colors rather than SystemColors.Highlight - the system
        // selection colors don't change with the app's own light/dark mode,
        // so a dark pane would keep drawing pale-blue-on-black selections.
        using (var backgroundBrush = new SolidBrush(selected ? ReadingTheme.SelectionBackground : BackColor))
        {
            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        }

        var text = GetItemText(e.Index);
        if (text.Length > 0)
        {
            // An athetized line is one the editor bracketed as suspected
            // interpolation: transmitted by the manuscripts, printed, but
            // doubted. Every printed edition marks the doubt somehow, usually
            // with square brackets. Rendering it identically to an accepted
            // line silently presents a contested line as settled.
            //
            // Shown by style rather than by inserting brackets into the string,
            // because the string is what gets copied, exported, searched and
            // tokenised - brackets added for display would travel into all of
            // those. Italic carries it where colour cannot: the muted colour
            // alone would be invisible against a selection highlight, and
            // unusable for anyone who cannot distinguish it.
            var athetized = e.Index >= 0 && e.Index < Items.Count
                            && Items[e.Index] is TextNode { IsAthetized: true };

            var foreColor = selected
                ? ReadingTheme.SelectionText
                : (athetized ? ReadingTheme.MutedText : ForeColor);

            var font = athetized ? GetAthetizedFont() : Font;

            var rect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, text, font, rect, foreColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        e.DrawFocusRectangle();
    }

    private Font? _athetizedFont;

    /// <summary>
    /// Italic variant of the current font, created once and rebuilt when the
    /// font changes. DrawItem runs for every visible row on every repaint, and
    /// a font allocated there would be thousands of objects a second during a
    /// scroll.
    /// </summary>
    private Font GetAthetizedFont()
    {
        if (_athetizedFont == null || _athetizedFont.FontFamily != Font.FontFamily
            || Math.Abs(_athetizedFont.Size - Font.Size) > 0.01f)
        {
            _athetizedFont?.Dispose();
            _athetizedFont = new Font(Font, Font.Style | FontStyle.Italic);
        }

        return _athetizedFont;
    }

    private string GetItemText(int index)
    {
        if (index < 0 || index >= Items.Count) return string.Empty;
        return Items[index] is TextNode node ? node.Text : Items[index]?.ToString() ?? string.Empty;
    }

    private void OnMouseMoveForTooltip(object? sender, MouseEventArgs e)
    {
        var index = IndexFromPoint(e.Location);
        if (index == _lastTooltipIndex) return;

        _lastTooltipIndex = index;

        if (index >= 0 && index < Items.Count && Items[index] is TextNode node)
        {
            // The citation is the point of this tooltip; the athetesis note is
            // appended because the italic styling shows that something is
            // different without saying what.
            _toolTip.SetToolTip(this, node.IsAthetized
                ? $"[{node.CitationRef}] - bracketed by the editor as probably not authentic"
                : $"[{node.CitationRef}]");
        }
        else
        {
            _toolTip.SetToolTip(this, string.Empty);
        }
    }

    /// <summary>Scrolls so the given index is visible - ListBox has no built-in EnsureVisible.</summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= Items.Count) return;

        if (index < TopIndex || index > TopIndex + (ClientSize.Height / Math.Max(ItemHeight, 1)))
        {
            TopIndex = index;
        }
    }

    /// <summary>Selects one item, clearing any other selection - SelectedItems.Clear() equivalent for a ListBox.</summary>
    public void SelectOnly(int index)
    {
        ClearSelected();
        if (index >= 0 && index < Items.Count) SetSelected(index, true);
    }
}
