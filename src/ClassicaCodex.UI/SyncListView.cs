using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// A read-only list of TextNodes that word-wraps each line to the control's
/// width instead of truncating it - which a plain ListView can't do (its
/// row height is fixed, not variable per item). Built on ListBox instead,
/// since ListBox natively supports per-item measurement via OwnerDrawVariable.
///
/// Items are <see cref="ReaderRow"/> objects: a whole passage where one fits,
/// and otherwise one of the pieces it was divided into. They used to be
/// TextNodes directly, on the assumption that one row meant one passage, and
/// that assumption could not survive the control's 255-pixel ceiling on row
/// height - about seven per cent of this corpus is taller than that, and used
/// to show only as much of itself as would fit.
///
/// Callers hand it passages through <see cref="SetPassagesAsync"/> and read
/// them back with <see cref="NodeAt"/>, which answers with the passage
/// whichever of its rows is asked about.
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

            // Not awaited, and nothing waits on it: this is a timer tick, and
            // the work it starts is a re-cut of every passage for the new
            // width, which belongs on a worker rather than in a Tick handler.
            _ = RelayoutAsync();
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

        // A font change moves where the cuts fall, not just how tall the rows
        // are - bigger text needs more rows for the same passage. Invalidating
        // the caches was enough while a row was a whole passage; now it would
        // leave the pane showing pieces cut for a font it is no longer using,
        // which is text in the wrong places rather than merely wrong heights.
        //
        // Through the same debounce as a resize, because the reading-size
        // dialog raises this on every click of its spinner.
        QueueRelayout();
    }

    /// <summary>
    /// Asks for the passages to be cut again shortly, collapsing a burst of
    /// changes - a drag, or a spinner being clicked - into one pass.
    /// </summary>
    private void QueueRelayout()
    {
        if (!IsHandleCreated) return;

        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
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
    /// The row at this index, or null where the row is one of the pane's
    /// placeholder messages rather than a passage.
    /// </summary>
    internal ReaderRow? RowAt(int index) =>
        index >= 0 && index < Items.Count ? Items[index] as ReaderRow : null;

    /// <summary>
    /// The passage at this index, whichever of its rows the index names.
    ///
    /// The one question nearly every caller actually has. A passage too tall
    /// for a single row is several rows, and a reader who clicks the third of
    /// them has selected the passage, not a third of one - so tagging,
    /// bookmarking, translating and the rest all resolve through here rather
    /// than casting the item.
    /// </summary>
    public TextNode? NodeAt(int index) => RowAt(index)?.Node;

    /// <summary>
    /// The first row of the passage the given index belongs to.
    ///
    /// What "go to this passage" means once a passage can occupy several rows:
    /// the top of it, not wherever in the middle the caller happened to land.
    /// </summary>
    public int FirstRowOfPassage(int index)
    {
        var row = RowAt(index);
        if (row == null) return index;

        return Math.Max(index - row.SegmentIndex, 0);
    }

    /// <summary>
    /// How many passages precede this row - the row's passage ordinal.
    ///
    /// This is what the two panes are kept in step by. Row number cannot do it
    /// once a passage can be several rows, because the two editions divide
    /// their text differently and split differently in consequence.
    /// </summary>
    public int PassageOrdinalAt(int index)
    {
        var ordinal = -1;

        for (var i = 0; i <= index && i < Items.Count; i++)
        {
            if (Items[i] is ReaderRow { IsFirst: true }) ordinal++;
        }

        return Math.Max(ordinal, 0);
    }

    /// <summary>The first row of the nth passage in this pane.</summary>
    public int RowOfPassageOrdinal(int ordinal)
    {
        var seen = -1;

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is not ReaderRow { IsFirst: true }) continue;
            if (++seen == ordinal) return i;
        }

        return Math.Max(Items.Count - 1, 0);
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
    ///
    /// Rows belonging to the same passage are stepped over rather than
    /// answered with. Without that, a passage divided over four rows compares
    /// itself with itself and prints its own reference four times down the
    /// margin, telling the reader it is four passages.
    /// </summary>
    private TextNode? PreviousLine(int index)
    {
        var self = NodeAt(index);

        for (var i = index - 1; i >= 0; i--)
        {
            if (Items[i] is not ReaderRow row) continue;
            if (self != null && ReferenceEquals(row.Node, self)) continue;
            if (string.Equals(row.Node.NodeKind, TextNodeKinds.Line, StringComparison.Ordinal)) return row.Node;
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
        if (UsableWidth == _lastMeasuredWidth) return;

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
        _ = RelayoutAsync();
    }

    /// <summary>
    /// Cuts the passages again for the width the pane now has, and puts the
    /// reader back where they were.
    ///
    /// Re-adding the existing rows is what this used to do, and it stopped
    /// being enough the moment a row could be part of a passage: where the cuts
    /// fall depends on the width, so a pane dragged narrower would show pieces
    /// that no longer fit, and one dragged wider would show short pieces with
    /// space beside them. Yesterday's pieces are not the right pieces.
    ///
    /// The reader's place is kept by passage and piece rather than by row
    /// number, for the same reason: the row count itself changes.
    /// </summary>
    private async Task RelayoutAsync()
    {
        if (_isRemeasuring) return;
        if (_passages.Count == 0) return;
        if (UsableWidth == _lastMeasuredWidth) return;

        _isRemeasuring = true;
        try
        {
            var anchor = CurrentAnchor();

            await SetPassagesAsync(_passages).ConfigureAwait(true);

            if (!IsDisposed) RestoreAnchor(anchor);
        }
        finally
        {
            _isRemeasuring = false;
        }

        if (!IsDisposed) Invalidate();
    }

    /// <summary>
    /// Where the reader is, in terms that survive the rows being cut
    /// differently: which passage is at the top of the pane, and which is
    /// selected.
    /// </summary>
    private (long TopNodeId, long SelectedNodeId) CurrentAnchor() =>
        (NodeAt(TopIndex)?.TextNodeId ?? -1, NodeAt(SelectedIndex)?.TextNodeId ?? -1);

    private void RestoreAnchor((long TopNodeId, long SelectedNodeId) anchor)
    {
        if (anchor.SelectedNodeId >= 0)
        {
            var row = RowOfNode(anchor.SelectedNodeId);
            if (row >= 0) SelectOnly(row);
        }

        if (anchor.TopNodeId >= 0)
        {
            var row = RowOfNode(anchor.TopNodeId);
            if (row >= 0) TopIndex = row;
        }
    }

    /// <summary>The first row showing this passage, or -1 if it is not here.</summary>
    public int RowOfNode(long textNodeId)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i] is ReaderRow { IsFirst: true } row && row.Node.TextNodeId == textNodeId) return i;
        }

        return -1;
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
    /// <see cref="SetPassagesAsync"/>, which cuts and measures a whole work
    /// there. It touches no control state and no cache, which is what makes
    /// that safe.
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
    /// <param name="isStillWanted">
    /// Asked once more just before the rows go on screen. Cutting a work takes
    /// seconds, and a reader can click a second work in that time; without
    /// this the slower of the two fills would win by finishing last, and the
    /// pane would settle on the work that was not asked for.
    /// </param>
    public async Task SetPassagesAsync(IReadOnlyList<TextNode> nodes, Func<bool>? isStillWanted = null)
    {
        _passages = nodes;

        if (IsDisposed) return;

        // Read on the UI thread, before anything is handed to a worker. Both
        // can change underneath the work - the reader can drag the splitter or
        // change the reading font while a work is opening - and rows cut
        // against the old layout would be wrong rather than merely late.
        var width = UsableWidth;
        var font = Font;
        var minGlyph = GetMinGlyphWidth();
        var maxGlyph = GetMaxGlyphWidth();

        List<ReaderRow> rows;
        Dictionary<string, int> heights;

        try
        {
            (rows, heights) = await Task.Run(
                () => BuildRows(nodes, width, font, minGlyph, maxGlyph)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // One row per passage, exactly as the reader behaved before it
            // could divide them, and measured inline by OnMeasureItem as it
            // always was. A passage too tall for a row then shows as much of
            // itself as fits, with the marker saying so.
            //
            // Swallowing is right here for the same reason it was when this
            // only warmed a cache: the fallback is complete and correct, just
            // slower and less generous, whereas letting the exception out
            // would take down the opening of the work itself.
            rows = nodes.Select(node => new ReaderRow(node, node.Text, 0, 1)).ToList();
            heights = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        if (IsDisposed) return;

        // If the layout moved while that ran, the rows describe a pane that no
        // longer exists. Filling anyway would show text cut for the wrong
        // width; the resize that moved it will queue its own re-split, so
        // dropping these is both safe and temporary.
        if (width != UsableWidth || !ReferenceEquals(font, Font)) return;
        if (isStillWanted != null && !isStillWanted()) return;

        var cache = GetHeightCacheForCurrentWidth(width);
        foreach (var pair in heights) cache[pair.Key] = pair.Value;

        Fill(rows);
        _lastMeasuredWidth = width;
    }

    /// <summary>
    /// Puts the rows on screen, keeping the pane from repainting until they are
    /// all in - the fill raises a measurement for every row as it goes.
    /// </summary>
    private void Fill(List<ReaderRow> rows)
    {
        BeginUpdate();
        try
        {
            Items.Clear();
            Items.AddRange(rows.Cast<object>().ToArray());
        }
        finally
        {
            EndUpdate();
        }
    }

    /// <summary>
    /// Cuts every passage into rows that fit, and measures each row once.
    ///
    /// Runs on a worker thread, so it touches no control state: everything it
    /// needs was read on the UI thread and passed in.
    ///
    /// The order of the work is the whole performance story. Asking GDI where
    /// to cut costs about a millisecond a question and a prose edition needs
    /// tens of thousands of them. So the cuts are guessed with
    /// <see cref="ReaderTextMetrics"/>, which is arithmetic and free, aiming
    /// deliberately short of the real ceiling; then each resulting row is
    /// measured once for real - the measurement its height needed in any case -
    /// and only a row that overshoots is cut again. That leaves roughly one
    /// measurement per row instead of twenty-five per passage.
    /// </summary>
    private static (List<ReaderRow> Rows, Dictionary<string, int> Heights) BuildRows(
        IReadOnlyList<TextNode> nodes, int width, Font font, int minGlyph, int maxGlyph)
    {
        var heights = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<ReaderRow>(nodes.Count);

        int MeasuredHeight(string text)
        {
            if (heights.TryGetValue(text, out var known)) return known;

            var height = MeasureUncappedHeight(text, width, font, minGlyph, maxGlyph);
            heights[text] = height;
            return height;
        }

        var characters = new HashSet<char>();
        foreach (var node in nodes)
        {
            foreach (var c in node.Text) characters.Add(c);
        }

        var metrics = ReaderTextMetrics.Build(font, characters, text =>
            TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width);

        // Short of the ceiling by a line, because the estimate is allowed to be
        // wrong by about that much and a row cut too long is a row that hides
        // text. A row cut too short only wastes a little space, and the
        // measurement below catches the rest either way.
        var target = Math.Max(ReaderRowHeight.Max - metrics.LineHeight, metrics.LineHeight);

        foreach (var node in nodes)
        {
            var text = node.Text;

            // Cannot wrap at all, so cannot need cutting - the same arithmetic
            // that lets OnMeasureItem answer without measuring.
            if (text.Length == 0 || (long)text.Length * maxGlyph <= width)
            {
                rows.Add(new ReaderRow(node, text, 0, 1));
                continue;
            }

            var segments = ReaderRowSplitter.Split(
                text,
                piece => metrics.EstimateHeight(piece, width),
                target,
                mayFitWhole: !ReaderRowHeight.CannotFit(text.Length, minGlyph, width, font.Height));

            // Confirm with real layout, and cut again anything the estimate let
            // through. The second cut uses real measurement, so it terminates
            // against the truth rather than against another guess.
            var confirmed = new List<string>(segments.Count);
            foreach (var segment in segments)
            {
                if (MeasuredHeight(segment) <= ReaderRowHeight.Max) { confirmed.Add(segment); continue; }

                confirmed.AddRange(ReaderRowSplitter.Split(
                    segment, MeasuredHeight, ReaderRowHeight.Max, mayFitWhole: false));
            }

            for (var i = 0; i < confirmed.Count; i++)
            {
                rows.Add(new ReaderRow(node, confirmed[i], i, confirmed.Count));
                MeasuredHeight(confirmed[i]);
            }
        }

        return (rows, heights);
    }

    /// <summary>
    /// The passages this pane is showing, kept so that a change of width or
    /// font can cut them again. The rows cannot be re-cut from themselves:
    /// where the cuts fall depends on the width, and yesterday's pieces are
    /// not the right pieces for a wider pane.
    /// </summary>
    private IReadOnlyList<TextNode> _passages = Array.Empty<TextNode>();

    /// <summary>
    /// The width text actually gets, once the citation margin and the padding
    /// either side are taken out. Named because measuring, cutting and drawing
    /// must all agree on it, and three copies of the arithmetic did not.
    /// </summary>
    private int UsableWidth => Math.Max(ClientSize.Width - 8 - GutterWidth, 50);

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
            var row = RowAt(e.Index);
            var athetized = row?.Node.IsAthetized == true;

            var foreColor = selected
                ? ReadingTheme.SelectionText
                : (athetized ? ReadingTheme.MutedText : ForeColor);

            var font = athetized ? GetAthetizedFont() : Font;

            var gutter = GutterWidth;
            var rect = new Rectangle(e.Bounds.X + 3 + gutter, e.Bounds.Y + 2,
                                     e.Bounds.Width - 6 - gutter, e.Bounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, text, font, rect, foreColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            // The marks go after the passage's last row, not into its text.
            // They are added and removed while the pane is on screen, and a
            // row's text was decided when the passage was divided - see
            // GetItemText for why that separation exists at all.
            if (row is { IsLast: true }) DrawPassageMarks(e, row, font, foreColor, gutter);

            // Only where the text genuinely cannot be divided - a single run
            // longer than a row, which GDI declines to break. Everything else
            // that used to be marked is now simply continued on the next row.
            if (IsTruncated(e.Index)) DrawTruncationMarker(e, selected);

            if (gutter > 0) DrawMargin(e, gutter, selected);
        }

        e.DrawFocusRectangle();
    }

    /// <summary>
    /// Draws the tag, bookmark and inquiry marks after the end of a passage.
    ///
    /// Drawn rather than appended to the text, which is what this used to do.
    /// A passage's rows are cut once, at the width the pane had; a mark made
    /// afterwards - and AddPassageMark exists precisely so that marking does
    /// not repopulate the pane - would never reach text frozen at that moment.
    /// Drawing it puts it back under the control of every repaint.
    ///
    /// Placed after the last line's text rather than at the row's right edge,
    /// so it reads as following the passage rather than as sitting in a column
    /// of its own.
    /// </summary>
    private void DrawPassageMarks(DrawItemEventArgs e, ReaderRow row, Font font, Color foreColor, int gutter)
    {
        var marks = MarksFor(row.Node);
        if (marks == default) return;

        var suffix = PassageMarkSymbols.Suffix(marks);
        if (suffix.Length == 0) return;

        var textWidth = Math.Max(e.Bounds.Width - 6 - gutter, 1);
        var lastLine = TextRenderer.MeasureText(row.Text, font, new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        var suffixWidth = TextRenderer.MeasureText(suffix, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;

        // Along the bottom line of the row, right-aligned within what is left.
        // Right-aligned rather than tucked against the text's own end, because
        // where a wrapped line ends is not something this can know without
        // laying the text out again for the sake of a two-character mark.
        var rect = new Rectangle(
            e.Bounds.X + 3 + gutter + Math.Max(textWidth - suffixWidth, 0),
            e.Bounds.Y + 2 + Math.Max(lastLine.Height - font.Height, 0),
            suffixWidth,
            font.Height);

        TextRenderer.DrawText(e.Graphics, suffix, font, rect, foreColor,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Right);
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
        if (RowAt(e.Index) is not { } row) return;

        // Only against the row a passage begins on. A passage divided over four
        // rows is one passage, and printing its reference beside each piece
        // would tell the reader it was four.
        if (!row.IsFirst) return;

        var node = row.Node;

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

    /// <summary>
    /// The string a row shows and is measured by.
    ///
    /// The marks are NOT part of it, and that is a change from when a row was
    /// a whole passage. They used to be appended here so they would be
    /// measured with the text; but a row's text is now decided when the
    /// passage is cut into rows, and marks are added while the pane is on
    /// screen - AddPassageMark deliberately redraws without repopulating. Text
    /// frozen at the moment of cutting cannot carry a mark added afterwards,
    /// so the mark is drawn after the last row of the passage instead.
    ///
    /// Neither is written into the node: node.Text is what gets copied,
    /// exported and searched, the same reasoning that keeps an athetized
    /// line's brackets out of its string.
    /// </summary>
    private string GetItemText(int index)
    {
        if (index < 0 || index >= Items.Count) return string.Empty;

        return Items[index] switch
        {
            ReaderRow row => row.Text,
            var other => other?.ToString() ?? string.Empty
        };
    }

    /// <summary>The marks this passage carries, if any.</summary>
    private PassageMarks MarksFor(TextNode node) =>
        _marks.TryGetValue(node.CitationRef, out var marks) ? marks : default;

    private void OnMouseMoveForTooltip(object? sender, MouseEventArgs e)
    {
        var index = IndexFromPoint(e.Location);
        if (index == _lastTooltipIndex) return;

        _lastTooltipIndex = index;

        if (RowAt(index) is { } row)
        {
            var node = row.Node;

            // The citation is the point of this tooltip; the notes after it are
            // there because each has a visible effect - italics, a continued
            // passage, an ellipsis - that shows something is different without
            // saying what.
            var citation = $"[{PassageCitation.Display(node.CitationRef, node.Milestone)}]";

            if (node.IsAthetized)
                citation += " - bracketed by the editor as probably not authentic";

            // Said on every row of a divided passage, because the reader can
            // hover any of them and the answer is the same: this is one
            // passage, shown over several rows because the list cannot make a
            // row tall enough for it.
            if (row.IsSplit)
                citation += $" - continued over {row.SegmentCount} rows ({row.SegmentIndex + 1} of {row.SegmentCount})";

            if (IsTruncated(index))
                citation += " - too long to show in full here; Copy to Clipboard takes all of it";

            _toolTip.SetToolTip(this, citation);
        }
        else
        {
            _toolTip.SetToolTip(this, string.Empty);
        }
    }

    /// <summary>
    /// Scrolls so the given row is visible - ListBox has no built-in
    /// EnsureVisible.
    ///
    /// How far down the pane a row is has to be worked out from the rows
    /// themselves. ItemHeight, which this used to divide by, is meaningless
    /// under OwnerDrawVariable: it returns the control's default backing value
    /// and not the height of anything on screen, so the old arithmetic thought
    /// far more rows were visible than were, and scrolled only when the target
    /// was a long way past the bottom. Divided passages made that worse, since
    /// every long passage became a row of the full 255 pixels.
    /// </summary>
    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        if (index < TopIndex) { TopIndex = index; return; }

        var available = ClientSize.Height;

        for (var i = TopIndex; i <= index && i < Items.Count; i++)
        {
            available -= RowHeight(i);
            if (available >= 0) continue;

            TopIndex = index;
            return;
        }
    }

    /// <summary>
    /// The height a row is actually drawn at, which is what it was measured to
    /// be, capped as the control caps it.
    /// </summary>
    private int RowHeight(int index)
    {
        var text = GetItemText(index);
        if (text.Length == 0) return Font.Height + 4;

        return ReaderRowHeight.Cap(UncappedHeightFor(text, UsableWidth));
    }

    /// <summary>Selects one item, clearing any other selection - SelectedItems.Clear() equivalent for a ListBox.</summary>
    public void SelectOnly(int index)
    {
        ClearSelected();
        if (index >= 0 && index < Items.Count) SetSelected(index, true);
    }
}
