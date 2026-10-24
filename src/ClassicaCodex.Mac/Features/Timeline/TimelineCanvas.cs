using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ClassicaCodex.Core;

namespace ClassicaCodex.Mac.Features.Timeline;

/// <summary>
/// Draws one horizontal bar per author along a real year axis (BCE/CE), so
/// you can see at a glance who was writing at the same time as whom. Authors
/// are stacked in a scrolling list sorted by start year; hover shows exact
/// dates, click raises AuthorClicked.
///
/// The control is as tall as its rows and sits in a ScrollViewer; the year
/// axis is drawn at the top of whatever part is in view, so the years stay
/// readable however far down the list the reader has scrolled.
/// </summary>
public sealed class TimelineCanvas : Control
{
    public sealed class TimelineEntry
    {
        public int AuthorId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int StartYear { get; init; }
        public int EndYear { get; init; }
    }

    private const double RowHeight = 28;
    private const double TopMargin = 40; // room for the year axis labels
    private const double LeftLabelWidth = 220;
    private const double RightPad = 24;
    private const double BarHeight = 12;

    private List<TimelineEntry> _entries = new();
    private int _minYear = -800;
    private int _maxYear = 600;
    private TimelineEntry? _hovered;
    private TimelineEntry? _selected;
    private double _headerOffset;
    private double _viewportHeight = double.NaN;

    private readonly Dictionary<(TimelineEntry, bool), FormattedText> _names = new();
    private readonly Dictionary<(string Text, string Ink, double Size, FontStyle Style, FontWeight Weight), FormattedText> _smallText = new();
    private readonly Dictionary<string, IBrush> _brushes = new(StringComparer.Ordinal);

    public TimelineCanvas()
    {
        ActualThemeVariantChanged += (_, _) =>
        {
            _names.Clear();
            _smallText.Clear();
            _brushes.Clear();
            InvalidateVisual();
        };
    }

    /// <summary>Raised with the author clicked.</summary>
    public event Action<TimelineEntry>? AuthorClicked;

    public IReadOnlyList<TimelineEntry> Entries => _entries;

    /// <summary>The author whose works are listed, drawn highlighted.</summary>
    public TimelineEntry? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Where the scroll viewer around this is looking - how far down, and how
    /// much it shows - so the year axis can stay at the top of the view and
    /// only the rows in it are drawn.
    /// </summary>
    public void SetViewport(double offset, double height)
    {
        if (Math.Abs(_headerOffset - offset) < 0.01 && Math.Abs(_viewportHeight - height) < 0.01) return;

        _headerOffset = offset;
        _viewportHeight = height;

        // At least as tall as the view, so the century bands run to the bottom
        // of it however few authors there are.
        MinHeight = height;
        InvalidateVisual();
    }

    public void SetData(List<TimelineEntry> entries)
    {
        _entries = entries.OrderBy(e => e.StartYear).ToList();
        _names.Clear();

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

        _hovered = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        return new Size(width, TopMargin + _entries.Count * RowHeight + 20);
    }

    private double YearToX(int year)
    {
        var usableWidth = Math.Max(Bounds.Width - LeftLabelWidth - RightPad, 100);
        var fraction = (double)(year - _minYear) / Math.Max(_maxYear - _minYear, 1);
        return LeftLabelWidth + fraction * usableWidth;
    }

    private int ChooseAxisStep()
    {
        var span = _maxYear - _minYear;
        if (span > 2000) return 200;
        if (span > 800) return 100;
        if (span > 300) return 50;
        return 20;
    }

    public override void Render(DrawingContext context)
    {
        var page = Brush("Cc.Page");
        context.DrawRectangle(page, null, new Rect(Bounds.Size));

        if (_entries.Count == 0)
        {
            var empty = Small("No dated authors matched yet.", "Cc.InkMuted", 13, FontStyle.Italic);
            context.DrawText(empty, new Point(18, 18));
            return;
        }

        var bottom = Math.Max(TopMargin + _entries.Count * RowHeight, Bounds.Height);
        var step = ChooseAxisStep();
        var start = _minYear / step * step;

        // Every other interval shaded, so the eye can follow a century down a
        // long list without a ruler.
        var band = Brush("Cc.Hover");
        for (var year = start; year <= _maxYear; year += step)
        {
            if ((year / step) % 2 != 0) continue;
            var x1 = Math.Max(YearToX(year), LeftLabelWidth);
            var x2 = Math.Min(YearToX(year + step), Bounds.Width - RightPad);
            if (x2 > x1) context.DrawRectangle(band, null, new Rect(x1, TopMargin, x2 - x1, bottom - TopMargin));
        }

        var grid = new Pen(Brush("Cc.Rule"), 1);
        for (var year = start; year <= _maxYear; year += step)
        {
            var x = Math.Round(YearToX(year)) + 0.5;
            if (x < LeftLabelWidth - 0.5) continue;
            context.DrawLine(grid, new Point(x, TopMargin), new Point(x, bottom));
        }

        // Only the rows in view: a full library dates several hundred authors,
        // and every hover repaints.
        var viewHeight = double.IsNaN(_viewportHeight) ? Bounds.Height : _viewportHeight;
        var first = Math.Max(0, (int)Math.Floor((_headerOffset - TopMargin) / RowHeight) - 1);
        var last = Math.Min(_entries.Count - 1, (int)Math.Ceiling((_headerOffset + viewHeight - TopMargin) / RowHeight) + 1);

        var hoverRow = Brush("Cc.Hover");
        var selectedRow = Brush("Cc.Selection");
        var bar = Brush("Cc.Verdigris");
        var barHover = Brush("Cc.Accent");
        var barSelected = Brush("Cc.AccentStrong");

        for (var i = first; i <= last; i++)
        {
            var entry = _entries[i];
            var y = TopMargin + i * RowHeight;
            var isHovered = entry == _hovered;
            var isSelected = entry == _selected;

            if (isSelected) context.DrawRectangle(selectedRow, null, new Rect(4, y + 1, Bounds.Width - 8, RowHeight - 2), 5, 5);
            else if (isHovered) context.DrawRectangle(hoverRow, null, new Rect(4, y + 1, Bounds.Width - 8, RowHeight - 2), 5, 5);

            var x1 = YearToX(entry.StartYear);
            var x2 = YearToX(entry.EndYear);
            var barWidth = Math.Max(x2 - x1, 3);
            var barRect = new Rect(x1, y + (RowHeight - BarHeight) / 2, barWidth, BarHeight);
            context.DrawRectangle(isSelected ? barSelected : isHovered ? barHover : bar, null, barRect, BarHeight / 2, BarHeight / 2);

            var name = NameText(entry, isHovered || isSelected);
            context.DrawText(name, new Point(14, y + (RowHeight - name.Height) / 2));

            // The dates beside the bar, where they fit; before it where they don't.
            var dates = Small($"{AuthorEraData.FormatYear(entry.StartYear)} – {AuthorEraData.FormatYear(entry.EndYear)}", "Cc.InkFaint", 11, FontStyle.Normal);
            var datesX = barRect.Right + 8;
            if (datesX + dates.Width > Bounds.Width - 4) datesX = barRect.Left - 8 - dates.Width;
            if (datesX > LeftLabelWidth - 4)
                context.DrawText(dates, new Point(datesX, y + (RowHeight - dates.Height) / 2));
        }

        DrawYearAxis(context, step, start, page);
    }

    /// <summary>The years, along the top of whatever is in view.</summary>
    private void DrawYearAxis(DrawingContext context, int step, int start, IBrush page)
    {
        var top = _headerOffset;
        context.DrawRectangle(page, null, new Rect(0, top, Bounds.Width, TopMargin));
        context.DrawLine(new Pen(Brush("Cc.Rule"), 1), new Point(0, top + TopMargin - 0.5), new Point(Bounds.Width, top + TopMargin - 0.5));

        var tick = new Pen(Brush("Cc.RuleStrong"), 1);

        for (var year = start; year <= _maxYear; year += step)
        {
            var x = Math.Round(YearToX(year)) + 0.5;
            if (x < LeftLabelWidth - 0.5) continue;

            context.DrawLine(tick, new Point(x, top + TopMargin - 7), new Point(x, top + TopMargin - 1));

            var label = Small(AuthorEraData.FormatYear(year), "Cc.InkMuted", 11, FontStyle.Normal);
            var labelX = Math.Clamp(x - label.Width / 2, LeftLabelWidth - 10, Bounds.Width - label.Width - 4);
            context.DrawText(label, new Point(labelX, top + TopMargin - 10 - label.Height));
        }

        var heading = Small("AUTHOR", "Cc.InkFaint", 10.5, FontStyle.Normal, FontWeight.SemiBold);
        context.DrawText(heading, new Point(14, top + TopMargin - 10 - heading.Height));
    }

    private TimelineEntry? HitTest(Point p)
    {
        // The axis covers whatever row is scrolled beneath it.
        if (p.Y < _headerOffset + TopMargin) return null;

        var row = (int)Math.Floor((p.Y - TopMargin) / RowHeight);
        if (row < 0 || row >= _entries.Count) return null;

        // The whole row, not just the bar.
        //
        // The bar is only as wide as the author's dates, and the name is drawn
        // at the left edge of the row - so for anyone who did not happen to live
        // at the very start of the timeline, the name sat hundreds of points
        // away from the only thing that answered a click. Clicking an author's
        // name, which is the obvious thing to click, did nothing at all.
        // Short-lived authors made it worse: their bar is a few points wide and
        // has to be hit exactly. A row belongs to one author for its whole
        // width, so there is nothing else here for a click to have meant.
        return _entries[row];
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var hover = HitTest(e.GetPosition(this));
        Cursor = hover != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        if (hover == _hovered) return;

        _hovered = hover;
        ToolTip.SetTip(this, hover == null
            ? null
            : $"{hover.Name}: {AuthorEraData.FormatYear(hover.StartYear)} - {AuthorEraData.FormatYear(hover.EndYear)}");
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hovered == null) return;

        _hovered = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        var hit = HitTest(e.GetPosition(this));
        if (hit == null) return;

        Selected = hit;
        AuthorClicked?.Invoke(hit);
    }

    /// <summary>The middle of an author's row, in this control's coordinates - for tests that click it.</summary>
    internal Point RowCentreOf(TimelineEntry entry)
    {
        var index = _entries.IndexOf(entry);
        return new Point(LeftLabelWidth / 2, TopMargin + index * RowHeight + RowHeight / 2);
    }

    private FormattedText NameText(TimelineEntry entry, bool emphasised)
    {
        if (_names.TryGetValue((entry, emphasised), out var cached)) return cached;

        var text = new FormattedText(entry.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, emphasised ? FontWeight.SemiBold : FontWeight.Normal),
            12.5, Brush("Cc.Ink"))
        {
            MaxTextWidth = LeftLabelWidth - 24,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        _names[(entry, emphasised)] = text;
        return text;
    }

    private FormattedText Small(string text, string ink, double size, FontStyle style, FontWeight weight = FontWeight.Normal)
    {
        var key = (text, ink, size, style, weight);
        if (_smallText.TryGetValue(key, out var cached)) return cached;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, style, weight), size, Brush(ink));
        _smallText[key] = formatted;
        return formatted;
    }

    /// <summary>A palette brush for the appearance in force, kept until the appearance changes.</summary>
    private IBrush Brush(string key)
    {
        if (_brushes.TryGetValue(key, out var cached)) return cached;

        IBrush brush = this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush solid
            ? new ImmutableSolidColorBrush(solid.Color, solid.Opacity)
            : Brushes.Gray;

        _brushes[key] = brush;
        return brush;
    }
}
