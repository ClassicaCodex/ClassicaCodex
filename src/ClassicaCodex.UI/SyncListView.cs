using ClassicaCodex.Core;
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
        // Owner-drawn is what makes the wrapping possible, and it is also what
        // keeps the reader out of the app-wide horizontal scrollbar. The theme
        // sets HorizontalScrollbar on every ListBox, but WinForms sizes the
        // scroll extent from rows it measures itself and it cannot measure
        // rows a DrawItem handler paints - so the extent stays at zero here and
        // no bar appears. Which is right: these panes wrap, and reading text
        // that slid sideways would be the wrong control entirely.
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
            _marginFont?.Dispose();
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

        _gutterWidth = -1;
        _marginFont?.Dispose();
        _marginFont = null;
    }

    private int _gutterWidth = -1;
    private Font? _marginFont;

    /// <summary>
    /// Whether this pane prints references in its margin. Read once per pane
    /// rather than per row: the setting cannot change while a row is being
    /// drawn, and touching the file system from a paint handler would be a
    /// disk read per line per repaint.
    /// </summary>
    public bool ShowCitationMargin
    {
        get => _showCitationMargin;
        set
        {
            if (_showCitationMargin == value) return;
            _showCitationMargin = value;

            // Every row's height was measured against the other layout, and
            // the resize path skips a remeasure when the control's width has
            // not changed - which it has not. Forgetting this leaves a pane
            // whose margin appeared but whose rows are still sized for text
            // that had the full width.
            _lastMeasuredWidth = -1;
        }
    }

    private bool _showCitationMargin = CitationMarginSettings.Enabled;

    /// <summary>
    /// Smaller than the text, as marginal numerals are set in print - and it
    /// is the difference that makes them read as apparatus rather than as
    /// something the author wrote.
    /// </summary>
    private Font MarginFont => _marginFont ??= new Font(Font.FontFamily,
        Math.Max(6f, Font.Size * 0.82f), FontStyle.Regular, Font.Unit);

    /// <summary>
    /// How much width the margin takes, measured from the font so it follows
    /// the reading size and the system text size alike.
    ///
    /// Sized from the font rather than from the marks actually present, which
    /// would be the tighter fit and the wrong trade: measurement runs while
    /// the pane is still filling, before the marks are all known, and a gutter
    /// that changed width afterwards would leave every row sized for a layout
    /// it no longer has. A fixed width is one both passes can agree on.
    /// </summary>
    private int GutterWidth
    {
        get
        {
            if (!ShowCitationMargin) return 0;
            if (_gutterWidth >= 0) return _gutterWidth;

            var sample = new string('8', CitationMargin.MaxLength);
            _gutterWidth = TextRenderer.MeasureText(sample, MarginFont,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + 10;
            return _gutterWidth;
        }
    }

    /// <summary>
    /// The nearest line above <paramref name="index"/>, which is what decides
    /// whether this one is marked.
    ///
    /// Walks back rather than taking the item directly above because a
    /// dialogue puts a speaker between every pair of lines - see
    /// <see cref="CitationMargin.MarkFor"/>, whose whole restraint depends on
    /// being handed a line. The walk is short in practice: one step in verse,
    /// two in a dialogue.
    /// </summary>
    private TextNode? PreviousLine(int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (Items[i] is not TextNode node) continue;
            if (string.Equals(node.NodeKind, TextNodeKinds.Line, StringComparison.Ordinal)) return node;
        }

        return null;
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

    /// <summary>
    /// Re-measures every row against the current layout, keeping the reader's
    /// place in the work.
    ///
    /// For a change that alters how much width the text has rather than what
    /// the text is - turning the margin on or off. Repopulating the pane would
    /// do it too, and would send the reader back to the top of the Iliad for
    /// having flipped a display switch.
    /// </summary>
    public void Relayout()
    {
        RemeasureAllItems();
        Invalidate();
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

        var width = Math.Max(ClientSize.Width - 8 - GutterWidth, 50);

        e.ItemHeight = ReaderRowHeight.Cap(UncappedHeightFor(text, width));
    }

    /// <summary>
    /// How tall the row would be if the control could hold it, which is not
    /// the same question as how tall the row will be - see
    /// <see cref="ReaderRowHeight.Max"/>. A result above the cap may be the
    /// <see cref="ReaderRowHeight.AtLeastTheMax"/> sentinel rather than a real
    /// measurement.
    ///
    /// OwnerDrawVariable means this runs once for EVERY item as the list is
    /// populated - the control needs the total height before it can size its
    /// scrollbar, so there is no virtualization to fall back on. A
    /// full-corpus work is tens of thousands of lines, and word-wrap layout
    /// is by far the expensive part, so each tier below exists to answer
    /// "how tall?" without paying for it.
    /// </summary>
    private int UncappedHeightFor(string text, int width)
    {
        var maxGlyph = GetMaxGlyphWidth();

        // Tier 1 answered before the cache is even consulted: a dictionary
        // lookup on a long string costs more than the multiply that settles it.
        if ((long)text.Length * maxGlyph <= width) return Font.Height + 6;

        var cache = GetHeightCacheForCurrentWidth(width);
        if (cache.TryGetValue(text, out var cachedHeight)) return cachedHeight;

        var height = MeasureUncappedHeight(text, width, Font, GetMinGlyphWidth(), maxGlyph);
        cache[text] = height;
        return height;
    }

    /// <summary>
    /// The measurement itself, with everything it depends on passed in.
    ///
    /// Takes the font and the glyph bounds as arguments rather than reading
    /// them off the control so that it can run on a worker thread - see
    /// <see cref="PrewarmHeightsAsync"/>. It touches no control state and no
    /// cache, which is what makes that safe.
    /// </summary>
    private static int MeasureUncappedHeight(string text, int width, Font font, int minGlyph, int maxGlyph)
    {
        // Tier 1: if even the widest glyph in the font repeated for the whole
        // string would still fit, the line cannot wrap and its height is
        // exactly one line. An integer multiply instead of a GDI call. The
        // bound is deliberately pessimistic, so a pass is always correct; only
        // lines that might genuinely wrap fall through. Most lines of verse
        // stop here.
        if ((long)text.Length * maxGlyph <= width) return font.Height + 6;

        var minPossibleWidth = (long)text.Length * minGlyph;
        if (minPossibleWidth <= width)
        {
            // Tier 2: genuinely ambiguous - somewhere between "all narrow
            // glyphs" and "all wide glyphs". Measure as a single line, which
            // is markedly cheaper than word-wrap layout because it never has
            // to search for break opportunities. Most verse lines land here,
            // being too long for tier 1 but nowhere near wrapping.
            var singleLine = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);

            if (singleLine.Width <= width) return Math.Max(singleLine.Height + 6, font.Height + 6);
        }
        else if (ReaderRowHeight.CannotFit(text.Length, minGlyph, width, font.Height))
        {
            // Tier 3: it must wrap, and the same narrowest-glyph bound says how
            // little it can wrap to. When even that floor overflows the row,
            // the exact height cannot change what the control stores and laying
            // the text out would be work done to be thrown away. This is the
            // tier that pays for itself on prose: the longest passages, the
            // ones costing milliseconds each, never reach a measurement at all.
            return ReaderRowHeight.AtLeastTheMax;
        }

        var size = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        return Math.Max(size.Height + 6, font.Height + 6);
    }

    /// <summary>
    /// Measures these lines on a worker thread and seeds the height cache with
    /// the answers, so that filling the pane afterwards costs dictionary
    /// lookups instead of text layout.
    ///
    /// Why this exists: DrawMode.OwnerDrawVariable means the control asks for
    /// every item's height the moment the item is added - it needs the total
    /// before it can size a scrollbar, so there is no virtualization to fall
    /// back on. Adding a prose work therefore ran a word-wrap layout per
    /// paragraph, synchronously, and the window was dead for all of it:
    /// measured at this reader's own settings, 2.6 s for Herodotus, 8.3 s for
    /// Livy, 9.3 s for Pliny, and both panes are filled per work, so those are
    /// halves. Opening a book is the commonest thing anyone does here.
    ///
    /// Measuring text off the UI thread is not something WinForms documents as
    /// supported, so it was verified rather than assumed: over 6,233 passages
    /// including the two hundred longest in the library, at two pane widths in
    /// both reader fonts, every height computed on a worker thread was
    /// identical to the one computed on the UI thread - and four threads
    /// sharing a single Font concurrently produced 0 mismatches in 24,132
    /// comparisons.
    ///
    /// It is also written so that failing is harmless. Nothing here is
    /// required to be complete or even to run: a text the cache does not have
    /// is measured inline exactly as before, so a prewarm that is skipped,
    /// abandoned or discarded costs time and changes nothing else.
    /// </summary>
    public async Task PrewarmHeightsAsync(IReadOnlyList<TextNode> nodes)
    {
        if (nodes.Count == 0 || IsDisposed) return;

        // Read on the UI thread, before anything is handed to a worker. Both
        // can change underneath a long measurement - the reader can drag the
        // splitter or change the reading font size while a work is opening -
        // and heights measured against the old layout would be wrong rather
        // than merely late.
        var width = Math.Max(ClientSize.Width - 8 - GutterWidth, 50);
        var font = Font;
        var minGlyph = GetMinGlyphWidth();
        var maxGlyph = GetMaxGlyphWidth();

        var texts = new List<string>(nodes.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var text = DisplayTextFor(node);

            // Neither is worth a worker's time: an empty row has a fixed
            // height, and a line short enough that it cannot wrap is settled
            // by a multiply wherever it is asked.
            if (text.Length == 0) continue;
            if ((long)text.Length * maxGlyph <= width) continue;

            if (seen.Add(text)) texts.Add(text);
        }

        if (texts.Count == 0) return;

        Dictionary<string, int> measured;
        try
        {
            measured = await Task.Run(() =>
            {
                var heights = new Dictionary<string, int>(texts.Count, StringComparer.Ordinal);
                foreach (var text in texts)
                {
                    heights[text] = MeasureUncappedHeight(text, width, font, minGlyph, maxGlyph);
                }

                return heights;
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Swallowed on purpose, and it is the one place in this file where
            // that is the right thing to do. Every height this would have
            // supplied is computed inline by OnMeasureItem when the row is
            // added, so abandoning the whole prewarm costs the reader the
            // seconds it was meant to save and nothing else - whereas letting
            // it escape would take down the opening of the work itself, which
            // is the thing that used to succeed.
            return;
        }

        if (IsDisposed) return;

        // The layout these were measured against has to still be the layout
        // the pane has. If the reader resized the window or changed the
        // reading font while this ran, the heights describe a pane that no
        // longer exists - so they are dropped, and the fill measures inline as
        // it always did.
        if (width != Math.Max(ClientSize.Width - 8 - GutterWidth, 50)) return;
        if (!ReferenceEquals(font, Font)) return;

        var cache = GetHeightCacheForCurrentWidth(width);
        foreach (var pair in measured) cache[pair.Key] = pair.Value;
    }

    /// <summary>
    /// Whether this row holds more text than the control can show, which the
    /// reader is entitled to know - see <see cref="ReaderRowHeight.Max"/>.
    ///
    /// Cheap enough for a paint: the row was measured when the pane filled,
    /// so this is a dictionary hit for every row a repaint touches, and the
    /// arithmetic tiers answer without measuring at all for the rest.
    /// </summary>
    private bool IsTruncated(int index)
    {
        var text = GetItemText(index);
        if (text.Length == 0) return false;

        var width = Math.Max(ClientSize.Width - 8 - GutterWidth, 50);
        return ReaderRowHeight.ExceedsMax(UncappedHeightFor(text, width));
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

            var gutter = GutterWidth;
            var rect = new Rectangle(e.Bounds.X + 3 + gutter, e.Bounds.Y + 2,
                                     e.Bounds.Width - 6 - gutter, e.Bounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, text, font, rect, foreColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            if (IsTruncated(e.Index)) DrawTruncationMarker(e, selected);

            if (gutter > 0) DrawMargin(e, gutter, selected);
        }

        e.DrawFocusRectangle();
    }

    /// <summary>
    /// Marks a row holding more text than the control can show.
    ///
    /// Drawn over a patch of the row's own background rather than straight
    /// onto the text, because the last visible line of a wrapped paragraph
    /// usually runs the full width and an ellipsis laid on top of it would
    /// read as part of a word. Right-aligned on that last line, which is
    /// where a reader already looks to see whether something continues.
    ///
    /// The alternative to marking it is what was there before: a passage
    /// that stops mid-sentence with nothing at all to say that it has.
    /// </summary>
    private void DrawTruncationMarker(DrawItemEventArgs e, bool selected)
    {
        const string Marker = "…";

        var size = TextRenderer.MeasureText(Marker, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding);
        var patch = new Rectangle(e.Bounds.Right - size.Width - 10, e.Bounds.Bottom - Font.Height - 2,
            size.Width + 7, Font.Height + 1);

        using (var background = new SolidBrush(selected ? ReadingTheme.SelectionBackground : BackColor))
        {
            e.Graphics.FillRectangle(background, patch);
        }

        TextRenderer.DrawText(e.Graphics, Marker, Font, patch,
            selected ? ReadingTheme.SelectionText : ReadingTheme.MutedText,
            TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    /// <summary>
    /// The reference beside the line, where an editor would have printed one -
    /// see <see cref="CitationMargin"/> for when that is.
    ///
    /// Aligned right against the text so the marks form a column the eye can
    /// run down, and drawn at the row's first line rather than centred in it,
    /// because a wrapped passage should be marked where it starts.
    /// </summary>
    private void DrawMargin(DrawItemEventArgs e, int gutter, bool selected)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not TextNode node) return;

        // Checked before the walk below, not just inside MarkFor. A speech
        // attribution is never marked, and a pane showing only attributions -
        // which the Show menu permits - would otherwise search the whole work
        // for a line that is not there, once per row, on every repaint.
        if (!string.Equals(node.NodeKind, TextNodeKinds.Line, StringComparison.Ordinal)) return;

        var mark = CitationMargin.MarkFor(node, PreviousLine(e.Index));
        if (mark == null) return;

        var rect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 3, gutter - 8, MarginFont.Height + 2);
        TextRenderer.DrawText(e.Graphics, mark, MarginFont, rect,
            selected ? ReadingTheme.SelectionText : ReadingTheme.MutedText,
            TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
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

    /// <summary>
    /// Which passages of this pane's edition carry an inquiry, a tag or a
    /// bookmark, keyed on citation reference. Set when the pane is filled;
    /// empty means nothing is marked.
    /// </summary>
    private Dictionary<string, PassageMarks> _marks = new(StringComparer.Ordinal);

    /// <summary>
    /// Replaces the marks shown at the end of each line. Call before filling
    /// the pane: the marks are part of the text that gets measured, so setting
    /// them afterwards would leave every row sized for a line it no longer
    /// draws.
    /// </summary>
    public void SetPassageMarks(Dictionary<string, PassageMarks> marks) => _marks = marks;

    /// <summary>
    /// Records a mark just added to a passage, so it appears without reloading
    /// the whole pane - which on a long work would cost a visible pause and
    /// throw away the reader's place in it.
    ///
    /// Redraws rather than remeasures. Row height is fixed once measured, so a
    /// line already close to wrapping can have its new mark clipped until the
    /// work is reopened. That is the cheap failure of the two: the alternative
    /// is rebuilding every row's height to show one character.
    /// </summary>
    public void AddPassageMark(string citationRef, PassageMarks mark)
    {
        _marks[citationRef] = _marks.TryGetValue(citationRef, out var existing) ? existing | mark : mark;
        Invalidate();
    }

    private string GetItemText(int index)
    {
        if (index < 0 || index >= Items.Count) return string.Empty;
        if (Items[index] is not TextNode node) return Items[index]?.ToString() ?? string.Empty;

        return DisplayTextFor(node);
    }

    /// <summary>
    /// The string a row actually shows, which is the one that has to be
    /// measured.
    ///
    /// The marks are appended here rather than written into the node, because
    /// node.Text is what gets copied, exported and searched - the same
    /// reasoning that keeps an athetized line's brackets out of its string.
    /// This is the display path and nothing else reads it.
    /// </summary>
    private string DisplayTextFor(TextNode node) =>
        _marks.TryGetValue(node.CitationRef, out var marks)
            ? node.Text + PassageMarkSymbols.Suffix(marks)
            : node.Text;

    private void OnMouseMoveForTooltip(object? sender, MouseEventArgs e)
    {
        var index = IndexFromPoint(e.Location);
        if (index == _lastTooltipIndex) return;

        _lastTooltipIndex = index;

        if (index >= 0 && index < Items.Count && Items[index] is TextNode node)
        {
            // The citation is the point of this tooltip; the other two notes
            // are appended because each has a visible effect - italics, an
            // ellipsis - that shows something is different without saying
            // what. The truncation note names Copy to Clipboard because that
            // is where the whole passage can actually be had.
            var citation = $"[{PassageCitation.Display(node.CitationRef, node.Milestone)}]";

            if (node.IsAthetized)
                citation += " - bracketed by the editor as probably not authentic";

            if (IsTruncated(index))
                citation += " - too long to show in full here; Copy to Clipboard takes all of it";

            _toolTip.SetToolTip(this, citation);
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
