using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

/// <summary>
/// Draws one horizontal bar per author along a real year axis (BCE/CE), so
/// you can see at a glance who was writing at the same time as whom. Authors
/// are stacked in a scrollable list sorted by start year; hover shows exact
/// dates, click raises AuthorClicked.
/// </summary>
public class TimelineCanvas : Panel
{
    public class TimelineEntry
    {
        public int AuthorId;
        public string Name = string.Empty;
        public int StartYear;
        public int EndYear;
    }

    private const int RowHeight = 26;
    private const int TopMargin = 40; // room for the year axis labels
    private const int LeftLabelWidth = 220;

    private List<TimelineEntry> _entries = new();
    private int _minYear;
    private int _maxYear;
    private TimelineEntry? _hovered;
    private readonly ToolTip _toolTip = new();

    public event Action<int>? AuthorClicked; // passes AuthorId

    public TimelineCanvas()
    {
        DoubleBuffered = true;
        BackColor = ReadingTheme.Surface;
        AutoScroll = false; // parent handles scrolling; this sizes itself to content
        MouseMove += TimelineCanvas_MouseMove;
        MouseClick += TimelineCanvas_MouseClick;
    }

    public void SetData(List<TimelineEntry> entries)
    {
        _entries = entries.OrderBy(e => e.StartYear).ToList();

        if (_entries.Count == 0)
        {
            _minYear = -800;
            _maxYear = 600;
        }
        else
        {
            _minYear = _entries.Min(e => e.StartYear) - 30;
            _maxYear = _entries.Max(e => e.EndYear) + 30;
        }

        Height = TopMargin + _entries.Count * RowHeight + 20;
        Invalidate();
    }

    private float YearToX(int year)
    {
        var usableWidth = Math.Max(Width - LeftLabelWidth - 20, 100);
        var fraction = (float)(year - _minYear) / Math.Max(_maxYear - _minYear, 1);
        return LeftLabelWidth + fraction * usableWidth;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (_entries.Count == 0)
        {
            using var emptyFont = new Font(Font, FontStyle.Italic);
            using var emptyBrush = new SolidBrush(ReadingTheme.MutedText);
            e.Graphics.DrawString("No dated authors matched yet.", emptyFont, emptyBrush, new PointF(16, 16));
            return;
        }

        DrawYearAxis(e.Graphics);

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var y = TopMargin + i * RowHeight;
            var isHovered = entry == _hovered;

            var x1 = YearToX(entry.StartYear);
            var x2 = YearToX(entry.EndYear);
            var barWidth = Math.Max(x2 - x1, 3);

            var barRect = new RectangleF(x1, y + 4, barWidth, RowHeight - 10);
            using var brush = new SolidBrush(isHovered ? Color.Gold : Color.CadetBlue);
            e.Graphics.FillRectangle(brush, barRect);
            using var pen = new Pen(Color.DimGray, isHovered ? 2 : 1);
            e.Graphics.DrawRectangle(pen, barRect.X, barRect.Y, barRect.Width, barRect.Height);

            var labelFont = isHovered ? new Font(Font, FontStyle.Bold) : Font;
            using var labelBrush = new SolidBrush(ReadingTheme.Text);
            e.Graphics.DrawString(entry.Name, labelFont, labelBrush, new PointF(4, y + 4));
        }
    }

    private void DrawYearAxis(Graphics g)
    {
        using var axisPen = new Pen(ReadingTheme.IsDark ? Color.FromArgb(70, 70, 78) : Color.LightGray);
        using var axisFont = new Font(Font, FontStyle.Regular);

        var step = ChooseAxisStep();
        var start = (_minYear / step) * step;

        for (var year = start; year <= _maxYear; year += step)
        {
            var x = YearToX(year);
            g.DrawLine(axisPen, x, TopMargin - 20, x, TopMargin + _entries.Count * RowHeight);
            var label = AuthorEraData.FormatYear(year);
            using var axisBrush = new SolidBrush(ReadingTheme.MutedText);
            g.DrawString(label, axisFont, axisBrush, x - 20, TopMargin - 36);
        }
    }

    private int ChooseAxisStep()
    {
        var span = _maxYear - _minYear;
        if (span > 2000) return 200;
        if (span > 800) return 100;
        if (span > 300) return 50;
        return 20;
    }

    private TimelineEntry? HitTest(Point p)
    {
        if (p.Y < TopMargin) return null;
        var row = (p.Y - TopMargin) / RowHeight;
        if (row < 0 || row >= _entries.Count) return null;

        var entry = _entries[row];
        var x1 = YearToX(entry.StartYear);
        var x2 = YearToX(entry.EndYear);
        return p.X >= x1 - 2 && p.X <= x2 + 2 ? entry : null;
    }

    private void TimelineCanvas_MouseMove(object? sender, MouseEventArgs e)
    {
        var hover = HitTest(e.Location);
        if (hover != _hovered)
        {
            _hovered = hover;
            Invalidate();

            if (hover != null)
            {
                _toolTip.SetToolTip(this,
                    $"{hover.Name}: {AuthorEraData.FormatYear(hover.StartYear)} - {AuthorEraData.FormatYear(hover.EndYear)}");
            }
        }
    }

    private void TimelineCanvas_MouseClick(object? sender, MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit != null) AuthorClicked?.Invoke(hit.AuthorId);
    }
}
