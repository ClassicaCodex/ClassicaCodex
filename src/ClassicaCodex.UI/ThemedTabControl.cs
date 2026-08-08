namespace ClassicaCodex.UI;

/// <summary>
/// A TabControl that draws its own tab strip.
///
/// TabControl paints through the native common control, which takes no notice
/// of BackColor. In dark mode that left a light strip behind and beside the
/// tabs, above a dark page - the seam read as a rendering fault rather than a
/// design.
///
/// Handling the Paint event is not enough, and was tried first: Paint fires
/// BEFORE the native control paints its strip, so the themed fill was
/// immediately covered over. The strip has to be painted after base.WndProc
/// has finished with WM_PAINT, which needs a subclass rather than an event
/// handler - hence this type existing at all.
///
/// Only the strip is repainted. The pages beneath draw themselves and are left
/// alone.
/// </summary>
public class ThemedTabControl : TabControl
{
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;

    public ThemedTabControl()
    {
        // OwnerDrawFixed gets the tab buttons themselves through DrawItem;
        // this class handles only the strip they sit on.
        DrawMode = TabDrawMode.OwnerDrawFixed;
        DoubleBuffered = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            // Swallowed: the native erase paints the strip in the system
            // colour, and letting it through produces a visible flash of light
            // grey before the repaint below covers it.
            m.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref m);

        if (m.Msg != WM_PAINT) return;

        using var g = Graphics.FromHwnd(Handle);

        // The band above the pages. Anything below this belongs to the
        // selected page, which draws itself.
        var stripHeight = ItemSize.Height + 4;
        var strip = new Rectangle(0, 0, Width, stripHeight);

        using (var back = new SolidBrush(ReadingTheme.HeaderBackground))
        {
            g.FillRectangle(back, strip);
        }

        // Repaint the buttons, since the fill above has just covered whatever
        // DrawItem produced during the base paint.
        for (var i = 0; i < TabPages.Count; i++)
        {
            var bounds = GetTabRect(i);
            var selected = i == SelectedIndex;

            using (var back = new SolidBrush(selected ? ReadingTheme.Background : ReadingTheme.HeaderBackground))
            {
                g.FillRectangle(back, bounds);
            }

            using (var borderPen = new Pen(ReadingTheme.Border))
            {
                g.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }

            TextRenderer.DrawText(
                g,
                TabPages[i].Text,
                Font,
                bounds,
                selected ? ReadingTheme.Text : ReadingTheme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // A rule along the bottom of the strip, so the band and the page below
        // read as separate surfaces rather than one flat area.
        using var edgePen = new Pen(ReadingTheme.Border);
        g.DrawLine(edgePen, 0, strip.Bottom - 1, Width, strip.Bottom - 1);
    }
}
