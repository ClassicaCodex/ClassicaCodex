using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Features.About;

/// <summary>
/// The stand-in for about-art.png until the painting arrives: bronze gears
/// on dark oak, lit from the upper left, with the right third left dark and
/// plain because the name and version are set there - the same composition
/// the art brief asks the painting for, so the window does not change shape
/// when the real one lands.
///
/// Fixed colours on purpose. The painting is the same picture in both
/// appearances, and so is this: oak and bronze are not palette colours, and
/// the light text over them is legible because the ground is always dark.
/// </summary>
internal sealed class AboutArt : Control
{
    private static readonly Color OakLight = Color.FromRgb(74, 50, 32);
    private static readonly Color OakDark = Color.FromRgb(22, 15, 10);
    private static readonly Color BronzeLight = Color.FromRgb(214, 160, 96);
    private static readonly Color BronzeDark = Color.FromRgb(110, 66, 30);
    private static readonly Color Verdigris = Color.FromRgb(86, 140, 122);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(OakLight, 0), new GradientStop(OakDark, 0.75) }
        }, bounds);

        // The grain: long, faint, slightly wavering strokes along the board.
        var grain = new Pen(new SolidColorBrush(Color.FromArgb(22, 0, 0, 0)), 1.2);
        for (var y = 6.0; y < bounds.Height; y += 9)
        {
            var wave = Math.Sin(y * 0.37) * 3;
            context.DrawLine(grain, new Point(0, y + wave), new Point(bounds.Width, y - wave));
        }

        var unit = bounds.Height;
        using (context.PushOpacity(0.92))
        {
            Gear(context, new Point(unit * 0.62, unit * 0.58), unit * 0.40, 40);
            Gear(context, new Point(unit * 1.28, unit * 0.30), unit * 0.24, 24);
            Gear(context, new Point(unit * 1.22, unit * 0.98), unit * 0.20, 20);
            Gear(context, new Point(unit * 0.08, unit * 0.06), unit * 0.17, 16);
        }

        // Raking light from the upper left, warm, falling off before the
        // right third so the type there sits on shadow.
        context.FillRectangle(new RadialGradientBrush
        {
            Center = new RelativePoint(0.18, 0.1, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.18, 0.1, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(70, 255, 214, 150), 0),
                new GradientStop(Color.FromArgb(0, 255, 214, 150), 1)
            }
        }, bounds);

        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 12, 8, 5), 0),
                new GradientStop(Color.FromArgb(225, 12, 8, 5), 0.55)
            }
        }, bounds);
    }

    /// <summary>
    /// A wheel with triangular teeth, as the Antikythera wheels were cut, a
    /// hub and four spokes, with a little verdigris in the gaps.
    /// </summary>
    private static void Gear(DrawingContext context, Point centre, double radius, int teeth)
    {
        var outline = new StreamGeometry();
        using (var figure = outline.Open())
        {
            figure.SetFillRule(FillRule.EvenOdd);

            var root = radius * 0.9;
            for (var i = 0; i < teeth; i++)
            {
                var a0 = i * 2 * Math.PI / teeth;
                var a1 = a0 + Math.PI / teeth;
                var low = Polar(centre, root, a0);
                var tip = Polar(centre, radius, a0 + Math.PI / teeth * 0.5);
                if (i == 0) figure.BeginFigure(low, true);
                else figure.LineTo(low);
                figure.LineTo(tip);
                figure.LineTo(Polar(centre, root, a1));
            }

            figure.EndFigure(true);

            // The rim's inner edge and the four windows between the spokes,
            // cut out by the even-odd rule.
            var inner = radius * 0.72;
            var hub = radius * 0.2;
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var start = quarter * Math.PI / 2 + 0.16;
                var end = (quarter + 1) * Math.PI / 2 - 0.16;
                figure.BeginFigure(Polar(centre, hub + radius * 0.06, start + 0.25), true);
                figure.LineTo(Polar(centre, inner, start));
                figure.ArcTo(Polar(centre, inner, end), new Size(inner, inner), 0, false, SweepDirection.Clockwise);
                figure.LineTo(Polar(centre, hub + radius * 0.06, end - 0.25));
                figure.EndFigure(true);
            }
        }

        var bronze = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(BronzeLight, 0), new GradientStop(BronzeDark, 1) }
        };

        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, Verdigris.R, Verdigris.G, Verdigris.B)), null, centre, radius * 0.95, radius * 0.95);
        context.DrawGeometry(bronze, new Pen(new SolidColorBrush(Color.FromArgb(150, 40, 24, 10)), 1), outline);
        context.DrawEllipse(new SolidColorBrush(BronzeDark), new Pen(new SolidColorBrush(BronzeLight), 1), centre, radius * 0.08, radius * 0.08);
    }

    private static Point Polar(Point centre, double r, double angle) =>
        new(centre.X + r * Math.Cos(angle), centre.Y + r * Math.Sin(angle));
}
