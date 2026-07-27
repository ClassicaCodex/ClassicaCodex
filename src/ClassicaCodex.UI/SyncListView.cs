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

        _lastMeasuredWidth = ClientSize.Width;
        _isRemeasuring = true;
        try
        {
            var items = new object[Items.Count];
            Items.CopyTo(items, 0);

            BeginUpdate();
            Items.Clear();
            Items.AddRange(items);
            EndUpdate();
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
        var size = TextRenderer.MeasureText(text, Font, new Size(width, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        e.ItemHeight = Math.Max(size.Height + 6, Font.Height + 6);
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
            var foreColor = selected ? ReadingTheme.SelectionText : ForeColor;
            var rect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, text, Font, rect, foreColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        e.DrawFocusRectangle();
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
            _toolTip.SetToolTip(this, $"[{node.CitationRef}]");
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
