using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using ClassicaCodex.Core;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// The arena: a 480 by 300 pixel screen, drawn at its native size and then
/// enlarged with nearest-neighbour sampling to fill whatever room the window
/// gives it, so the game keeps its chunky pixels at any size - the Windows
/// canvas, drawing call for drawing call, on Avalonia's drawing context.
///
/// The frame is composed into an off-screen bitmap with aliased edges and
/// aliased text, which is what makes a line one pixel wide and a letter a
/// cluster of whole pixels; the enlargement then turns each of those into a
/// crisp square. Drawn straight onto the window at its own scale it would
/// come out smooth and the game would stop looking like itself.
///
/// The colours are the game's, fixed in both appearances (see BronzeSprites):
/// a night sky does not turn to parchment when the Mac goes light. The window
/// around it is what follows the appearance.
/// </summary>
internal sealed class BronzeArcadeCanvas : Control
{
    private const int FrameWidth = 480, FrameHeight = 300;
    private static readonly Rect Full = new(0, 0, FrameWidth, FrameHeight);

    private static readonly Color Gold = Color.FromRgb(255, 207, 113);
    private static readonly Color Cyan = Color.FromRgb(102, 240, 216);
    private static readonly Color Night = Color.FromRgb(9, 8, 20);

    private static readonly RenderOptions Pixelated = new()
    {
        EdgeMode = EdgeMode.Aliased,
        BitmapInterpolationMode = BitmapInterpolationMode.None
    };

    private static readonly TextOptions PixelText = new() { TextRenderingMode = TextRenderingMode.Alias };

    private RenderTargetBitmap? _frame;

    // The night sky, the temple, the floor grid and the CRT scanlines are the
    // same pixels in every frame of the game. Drawing them costs some three
    // hundred calls, so they are rendered once and copied thereafter.
    private RenderTargetBitmap? _backdrop, _scanlineOverlay;

    private readonly Typeface _typeface;
    private readonly Dictionary<Color, IBrush> _brushes = new();

    private BronzeArena? _arena;
    private bool _paused, _scanlines = true;
    private int _completedStories;
    private string _banner = "BRONZE & THUNDER", _subtitle = "THE LOST VERSES";

    public BronzeArcadeCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        var family = Application.Current?.FindResource("Cc.MonoFont") as FontFamily ?? new FontFamily("Menlo, monospace");
        _typeface = new Typeface(family, FontStyle.Normal, FontWeight.Bold);
        Avalonia.Automation.AutomationProperties.SetName(this, "Bronze and Thunder arcade arena");
        PointerPressed += (_, _) => Focus();
    }

    public BronzeArena? Arena { get => _arena; set { _arena = value; InvalidateVisual(); } }
    public bool Paused { get => _paused; set { _paused = value; InvalidateVisual(); } }
    public bool Scanlines { get => _scanlines; set { _scanlines = value; InvalidateVisual(); } }
    public int CompletedStories { get => _completedStories; set { _completedStories = value; InvalidateVisual(); } }
    public string Banner { get => _banner; set { _banner = value; InvalidateVisual(); } }
    public string Subtitle { get => _subtitle; set { _subtitle = value; InvalidateVisual(); } }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush(Night), bounds);

        var fit = Math.Min(bounds.Width / FrameWidth, bounds.Height / FrameHeight);
        if (fit <= 0) return;

        ComposeFrame();

        var width = FrameWidth * fit;
        var height = FrameHeight * fit;
        var target = new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            context.DrawImage(_frame!, Full, target);
    }

    private void ComposeFrame()
    {
        _frame ??= new RenderTargetBitmap(new PixelSize(FrameWidth, FrameHeight), new Vector(96, 96));

        using var g = _frame.CreateDrawingContext();
        using var pixels = g.PushRenderOptions(Pixelated);
        using var text = g.PushTextOptions(PixelText);

        g.DrawImage(_backdrop ??= Build(DrawWorld), Full, Full);
        if (Arena == null) DrawIdleScene(g);
        if (Arena is { } arena) DrawArena(g, arena);

        if (Arena == null || Arena.State != BronzeBattleState.Fighting || Paused)
        {
            g.FillRectangle(Brush(Color.FromArgb(160, 9, 8, 20)), new Rect(30, 78, 420, 138));
            Outline(g, Gold, 30, 78, 420, 138);
            var title = Paused ? "PAUSED" : Arena?.State == BronzeBattleState.Lost ? "RISE AGAIN" :
                Arena?.State == BronzeBattleState.Won ? "VERSE UNLOCKED" : Banner;
            Center(g, title, title.Length > 19 ? 13 : 28, Gold, 100);
            Center(g, Paused ? "ESC TO RESUME" : Subtitle, 13, Cyan, 149);
            Center(g, "A CLASSICACODEX ARCADE SECRET", 10, Gold, 188);
        }

        if (Scanlines) g.DrawImage(_scanlineOverlay ??= Build(DrawScanlines), Full, Full);
    }

    private static RenderTargetBitmap Build(Action<DrawingContext> draw)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(FrameWidth, FrameHeight), new Vector(96, 96));
        using var g = bitmap.CreateDrawingContext();
        using var pixels = g.PushRenderOptions(Pixelated);
        draw(g);
        return bitmap;
    }

    private void DrawScanlines(DrawingContext g)
    {
        var pen = new Pen(Brush(Color.FromArgb(25, 0, 0, 0)));
        for (var y = 0; y < FrameHeight; y += 2) Line(g, pen, 0, y, FrameWidth, y);
    }

    /// <summary>The scenery, which never changes. Rendered once into <see cref="_backdrop"/>.</summary>
    private void DrawWorld(DrawingContext g)
    {
        var sky = Color.FromRgb(15, 12, 34);
        g.FillRectangle(Brush(sky), Full);

        var stars = Brush(Color.FromRgb(130, 106, 161));
        for (var i = 0; i < 48; i++) g.FillRectangle(stars, new Rect((i * 113 + 29) % 480, (i * 37 + 7) % 72, 1, 1));

        var moon = Brush(Gold);
        g.DrawEllipse(moon, null, new Rect(392, 32, 22, 22));
        g.DrawEllipse(Brush(sky), null, new Rect(385, 28, 22, 22));

        var mountains = Brush(Color.FromRgb(43, 28, 60));
        for (var i = 0; i < 8; i++)
            Polygon(g, mountains, null, new Point(i * 70 - 35, 80), new Point(i * 70, 42 + i % 3 * 8), new Point(i * 70 + 65, 80));

        g.FillRectangle(Brush(Color.FromRgb(32, 24, 49)), new Rect(12, 70, 456, 203));
        var grid = new Pen(Brush(Color.FromRgb(56, 39, 68)));
        for (var y = 75; y < 273; y += 24)
        {
            Line(g, grid, 12, y, 468, y);
            for (var x = 12 + (y % 48 == 3 ? 0 : 20); x < 468; x += 40) Line(g, grid, x, y, x, y + 24);
        }

        Outline(g, Color.FromRgb(171, 88, 101), 12, 53, 456, 220);

        var pillar = Brush(Color.FromRgb(102, 72, 98));
        for (var i = 0; i < 6; i++)
        {
            var x = 191 + i * 17;
            g.FillRectangle(pillar, new Rect(x, 39, 7, 28));
            g.FillRectangle(pillar, new Rect(x - 2, 38, 11, 3));
        }

        Polygon(g, Brush(Color.FromRgb(163, 109, 117)), null, new Point(181, 38), new Point(239, 20), new Point(298, 38));

        var stone = Brush(Color.FromRgb(104, 72, 90));
        var fire = Brush(Color.FromRgb(255, 135, 65));
        foreach (var x in new[] { 7, 466 })
            foreach (var y in new[] { 85, 159, 236 })
            {
                g.FillRectangle(stone, new Rect(x, y, 7, 18));
                g.FillRectangle(fire, new Rect(x, y - 4, 7, 4));
                g.FillRectangle(moon, new Rect(x + 2, y - 8, 3, 6));
            }
    }

    /// <summary>The attract screen between battles, where the star count changes.</summary>
    private void DrawIdleScene(DrawingContext g)
    {
        BronzeSprites.Draw(g, "hero", 125, 253, false, true, 0, false);
        BronzeSprites.Draw(g, "Hydra", 360, 253, true, true, 0, false);

        for (var i = 0; i < 6; i++)
        {
            var x = 180 + i * 24;
            var y = 241 - (i % 2) * 8;
            var star = new Pen(Brush(i < CompletedStories ? Gold : Color.FromRgb(58, 42, 70)));
            Line(g, star, x - 3, y, x + 3, y);
            Line(g, star, x, y - 3, x, y + 3);
            if (i > 0) Line(g, star, x - 21, 241 - ((i - 1) % 2) * 8, x - 3, y);
        }

        if (CompletedStories > 0)
            Center(g, $"{CompletedStories} {(CompletedStories == 1 ? "STORY" : "STORIES")} WRITTEN IN THE STARS", 10, Gold, 266);
    }

    private void DrawArena(DrawingContext g, BronzeArena a)
    {
        var shake = a.Shake > 0
            ? Matrix.CreateTranslation((int)(Math.Sin(a.Time * 83) * a.Shake), (int)(Math.Cos(a.Time * 61) * a.Shake))
            : Matrix.Identity;

        using (g.PushTransform(shake))
        {
            foreach (var pickup in a.Pickups)
            {
                var b = Brush(pickup.Healing ? Color.FromRgb(255, 103, 130) : Cyan);
                var x = (int)pickup.Position.X;
                var y = (int)pickup.Position.Y;
                g.FillRectangle(b, new Rect(x - 4, y - 2, 8, 4));
                g.FillRectangle(b, new Rect(x - 2, y - 4, 4, 8));
            }

            var shadow = Brush(Color.FromArgb(110, 0, 0, 0));
            foreach (var enemy in a.Enemies.OrderBy(e => e.Position.Y))
            {
                var x = enemy.Position.X;
                var y = enemy.Position.Y;
                g.DrawEllipse(shadow, null, new Rect(x - enemy.Radius, y - 3, enemy.Radius * 2, 7));

                if (enemy.Telegraph > 0)
                {
                    var warning = new Pen(Brush(Color.FromRgb(255, 77, 111)));
                    var r = enemy.Kind == BronzeEnemyKind.Cyclops ? 44 : 18;
                    g.DrawEllipse(null, warning, new Rect(x - r + .5, y - r + .5, r * 2, r * 2));
                    Line(g, warning, x, y, x + enemy.Aim.X * 45, y + enemy.Aim.Y * 45);
                }

                BronzeSprites.Draw(g, enemy.Kind.ToString(), x, y, a.Player.X < x, enemy.Boss, a.Time, enemy.Flash > 0);
                var health = Brush(enemy.Boss ? Gold : Color.FromRgb(210, 70, 112));
                g.FillRectangle(health, new Rect(x - 10, y + 8, 20 * Math.Max(0, enemy.Health) / enemy.MaxHealth, 2));
            }

            var px = a.Player.X;
            var py = a.Player.Y;
            if (a.Invulnerable == 0 || (int)(a.Time * 18) % 2 == 0)
                BronzeSprites.Draw(g, "hero", px, py, a.Facing.X < 0, false, a.Time, false);

            if (a.Shielding)
                g.DrawEllipse(null, new Pen(Brush(Cyan), 2), new Rect(px + a.Facing.X * 9 - 5, py + a.Facing.Y * 9 - 8, 10, 12));

            if (a.ConcealTime > 0) Write(g, "UNSEEN", px - 18, py - 25, Cyan);

            if (a.StrikeTime > 0)
            {
                var weapon = new Pen(Brush(Gold), 2);
                g.DrawLine(weapon, new Point(px, py - 4), new Point(px + a.Facing.X * 38, py - 4 + a.Facing.Y * 38));
                if (a.Level >= 3) Arc(g, weapon, px, py, 30, MathF.Atan2(a.Facing.Y, a.Facing.X) * 180 / MathF.PI - 70, 140);
            }

            if (a.MagicTime > 0 && a.Level >= 4)
            {
                var r = (1 - a.MagicTime / .3f) * 100;
                g.DrawEllipse(null, new Pen(Brush(Cyan), 2), new Point(px, py), r, r);
            }
            else if (a.MagicTime > 0)
            {
                var r = 3 + a.MagicTime * 20;
                g.DrawEllipse(Brush(Cyan), null, new Point(px + a.Facing.X * 12, py + a.Facing.Y * 12), r, r);
                g.FillRectangle(Brush(Color.FromRgb(230, 255, 241)), new Rect(px + a.Facing.X * 12 - 2, py + a.Facing.Y * 12 - 2, 4, 4));
            }

            foreach (var shot in a.Shots)
            {
                var color = shot.Hostile ? Color.FromRgb(244, 68, 120) : shot.Magic || shot.SeaBlessed ? Cyan : Gold;
                var pen = new Pen(Brush(color), shot.Magic ? 5 : 2);
                g.DrawLine(pen, new Point(shot.Position.X, shot.Position.Y),
                    new Point(shot.Position.X - shot.Velocity.X * .055f, shot.Position.Y - shot.Velocity.Y * .055f));
                var core = Brush(shot.Magic || shot.Reflected ? Color.FromRgb(240, 255, 238) : Gold);
                if (!shot.Hostile) g.FillRectangle(core, new Rect(shot.Position.X - 2, shot.Position.Y - 2, 4, 4));
            }

            foreach (var spark in a.Sparks)
            {
                var brush = Brush(spark.Color == 0 ? Gold : spark.Color == 1 ? Cyan : Color.FromRgb(241, 77, 119));
                g.FillRectangle(brush, new Rect((int)spark.Position.X, (int)spark.Position.Y, 2, 2));
            }
        }

        var hud = Brush(Night);
        g.FillRectangle(hud, new Rect(0, 0, 480, 20));
        g.FillRectangle(hud, new Rect(0, 275, 480, 25));
        Write(g, $"CH {a.Level:00}   SCORE {a.Score:000000}", 8, 4, Gold);
        Write(g, $"FOES {Math.Max(0, a.Remaining):00}", 218, 4, Gold);
        Bar(g, "HP", 281, a.Health / a.MaxHealth, Color.FromRgb(255, 104, 139));
        Bar(g, "MP", 347, a.Mana / 100, Cyan);
        Bar(g, "GD", 413, a.Guard / 100, Gold);
        Write(g, $"J STRIKE  K {a.RangedName}  L {a.MagicName} ({a.MagicCost})", 8, 279, Gold);
        Write(g, a.MagicReadiness, 396, 279, Cyan);
        Write(g, "WASD MOVE  SHIFT SHIELD  SPACE DODGE  ESC PAUSE  F6 LIBRARY", 8, 289, Cyan);

        if (a.MagicFeedbackTime > 0)
        {
            g.FillRectangle(Brush(Color.FromArgb(225, 9, 8, 20)), new Rect(120, 257, 240, 14));
            Center(g, a.MagicFeedback, 10, Cyan, 258);
        }

        var boss = a.Enemies.FirstOrDefault(e => e.Boss);
        if (boss != null)
        {
            Outline(g, Gold, 160, 24, 160, 5);
            g.FillRectangle(Brush(Color.FromRgb(205, 66, 113)), new Rect(161, 25, 158 * Math.Max(0, boss.Health) / boss.MaxHealth, 3));
            Center(g, boss.Kind.ToString().ToUpperInvariant(), 10, Gold, 31);
        }
    }

    private void Bar(DrawingContext g, string label, int x, float value, Color color)
    {
        Write(g, label, x, 4, color);
        g.FillRectangle(Brush(Color.FromRgb(54, 40, 63)), new Rect(x + 14, 6, 42, 5));
        g.FillRectangle(Brush(color), new Rect(x + 14, 6, Math.Clamp(value, 0, 1) * 42, 5));
    }

    private void Write(DrawingContext g, string text, double x, double y, Color color) =>
        g.DrawText(Text(text, 10, color), new Point(x, y));

    private void Center(DrawingContext g, string text, double size, Color color, double y)
    {
        var formatted = Text(text, size, color);
        g.DrawText(formatted, new Point(Math.Round((FrameWidth - formatted.Width) / 2), y));
    }

    private FormattedText Text(string text, double size, Color color) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, size, Brush(color));

    /// <summary>
    /// A one-pixel line through the centres of the pixels it names, which is
    /// where GDI+ puts it on the Windows canvas; at whole-number coordinates
    /// it would straddle two rows and the aliasing would pick either.
    /// </summary>
    private static void Line(DrawingContext g, IPen pen, double x1, double y1, double x2, double y2) =>
        g.DrawLine(pen, new Point(x1 + .5, y1 + .5), new Point(x2 + .5, y2 + .5));

    /// <summary>A one-pixel rectangle outline covering columns x..x+w and rows y..y+h, as DrawRectangle does in GDI+.</summary>
    private void Outline(DrawingContext g, Color color, double x, double y, double width, double height) =>
        g.DrawRectangle(null, new Pen(Brush(color)), new Rect(x + .5, y + .5, width, height));

    private static void Polygon(DrawingContext g, IBrush? fill, IPen? pen, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], fill != null);
            for (var i = 1; i < points.Length; i++) context.LineTo(points[i]);
            context.EndFigure(true);
        }

        g.DrawGeometry(fill, pen, geometry);
    }

    /// <summary>An arc of a circle, angles in degrees clockwise from the x axis - GDI+'s DrawArc on a square.</summary>
    private static void Arc(DrawingContext g, IPen pen, double cx, double cy, double radius, double startDegrees, double sweepDegrees)
    {
        var start = startDegrees * Math.PI / 180;
        var end = (startDegrees + sweepDegrees) * Math.PI / 180;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(cx + radius * Math.Cos(start), cy + radius * Math.Sin(start)), false);
            context.ArcTo(new Point(cx + radius * Math.Cos(end), cy + radius * Math.Sin(end)), new Size(radius, radius), 0,
                sweepDegrees > 180, SweepDirection.Clockwise);
            context.EndFigure(false);
        }

        g.DrawGeometry(null, pen, geometry);
    }

    private IBrush Brush(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
            _brushes[color] = brush = new ImmutableSolidColorBrush(color);
        return brush;
    }
}
