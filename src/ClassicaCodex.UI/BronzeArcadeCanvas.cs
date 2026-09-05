using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

internal sealed class BronzeArcadeCanvas : Control
{
    private readonly Bitmap _frame = new(480, 300);
    // The night sky, the temple, the floor grid and the CRT scanlines are the
    // same pixels in every frame of the game. Drawing them costs some three
    // hundred GDI+ calls, so they are rendered once and blitted thereafter.
    private Bitmap? _backdrop, _scanlineOverlay;
    private readonly BronzeSprites _sprites = new();
    private readonly Font _small = new(FontFamily.GenericMonospace, 10, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _large = new(FontFamily.GenericMonospace, 28, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _medium = new(FontFamily.GenericMonospace, 13, FontStyle.Bold, GraphicsUnit.Pixel);
    public BronzeArena? Arena { get; set; }
    public bool Paused { get; set; }
    public bool Scanlines { get; set; } = true;
    public int CompletedStories { get; set; }
    public string Banner { get; set; } = "BRONZE & THUNDER";
    public string Subtitle { get; set; } = "THE LOST VERSES";
    private static readonly Color Gold = Color.FromArgb(255, 207, 113);
    private static readonly Color Cyan = Color.FromArgb(102, 240, 216);

    public BronzeArcadeCanvas()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(9, 8, 20); TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        AccessibleName = "Bronze and Thunder arcade arena";
        MouseDown += (_, _) => Focus();
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var g = Graphics.FromImage(_frame))
        {
            g.SmoothingMode = SmoothingMode.None; g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half; g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            var previous = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_backdrop ??= BuildBackdrop(), 0, 0);
            g.CompositingMode = previous;
            if (Arena == null) DrawIdleScene(g);
            if (Arena is { } arena) DrawArena(g, arena);
            if (Arena == null || Arena.State != BronzeBattleState.Fighting || Paused)
            {
                using var dim = new SolidBrush(Color.FromArgb(160, 9, 8, 20));
                g.FillRectangle(dim, 30, 78, 420, 138);
                using var border = new Pen(Gold);
                g.DrawRectangle(border, 30, 78, 420, 138);
                var title = Paused ? "PAUSED" : Arena?.State == BronzeBattleState.Lost ? "RISE AGAIN" :
                    Arena?.State == BronzeBattleState.Won ? "VERSE UNLOCKED" : Banner;
                Center(g, title, title.Length > 19 ? _medium : _large, Gold, 100);
                Center(g, Paused ? "ESC TO RESUME" : Subtitle, _medium, Cyan, 149);
                Center(g, "A CLASSICACODEX ARCADE SECRET", _small, Gold, 188);
            }
            if (Scanlines) g.DrawImageUnscaled(_scanlineOverlay ??= BuildScanlines(), 0, 0);
        }
        var fit = Math.Min(ClientSize.Width / 480f, ClientSize.Height / 300f);
        if (fit <= 0) return;
        var scale = fit;
        var width = (int)(480 * scale); var height = (int)(300 * scale);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(_frame, new Rectangle((Width - width) / 2, (Height - height) / 2, width, height),
            0, 0, 480, 300, GraphicsUnit.Pixel);
    }

    private static Bitmap BuildScanlines()
    {
        var overlay = new Bitmap(480, 300);
        using var g = Graphics.FromImage(overlay);
        using var pen = new Pen(Color.FromArgb(25, 0, 0, 0));
        for (var y = 0; y < 300; y += 2) g.DrawLine(pen, 0, y, 480, y);
        return overlay;
    }

    private Bitmap BuildBackdrop()
    {
        var backdrop = new Bitmap(480, 300, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(backdrop);
        g.SmoothingMode = SmoothingMode.None; g.PixelOffsetMode = PixelOffsetMode.Half;
        DrawWorld(g);
        return backdrop;
    }

    /// <summary>The scenery, which never changes. Rendered once into <see cref="_backdrop"/>.</summary>
    private void DrawWorld(Graphics g)
    {
        g.Clear(Color.FromArgb(15, 12, 34));
        using var stars = new SolidBrush(Color.FromArgb(130, 106, 161));
        for (var i = 0; i < 48; i++) g.FillRectangle(stars, (i * 113 + 29) % 480, (i * 37 + 7) % 72, 1, 1);
        using var moon = new SolidBrush(Gold);
        g.FillEllipse(moon, 392, 32, 22, 22);
        using var dark = new SolidBrush(Color.FromArgb(15, 12, 34)); g.FillEllipse(dark, 385, 28, 22, 22);
        using var mountains = new SolidBrush(Color.FromArgb(43, 28, 60));
        for (var i = 0; i < 8; i++)
            g.FillPolygon(mountains, new[] { new Point(i * 70 - 35, 80), new Point(i * 70, 42 + i % 3 * 8), new Point(i * 70 + 65, 80) });
        using var floor = new SolidBrush(Color.FromArgb(32, 24, 49)); g.FillRectangle(floor, 12, 70, 456, 203);
        using var grid = new Pen(Color.FromArgb(56, 39, 68));
        for (var y = 75; y < 273; y += 24)
        {
            g.DrawLine(grid, 12, y, 468, y);
            for (var x = 12 + (y % 48 == 3 ? 0 : 20); x < 468; x += 40) g.DrawLine(grid, x, y, x, y + 24);
        }
        using var edge = new Pen(Color.FromArgb(171, 88, 101));
        g.DrawRectangle(edge, 12, 53, 456, 220);
        for (var i = 0; i < 6; i++)
        {
            var x = 191 + i * 17;
            using var pillar = new SolidBrush(Color.FromArgb(102, 72, 98));
            g.FillRectangle(pillar, x, 39, 7, 28); g.FillRectangle(pillar, x - 2, 38, 11, 3);
        }
        using var pediment = new SolidBrush(Color.FromArgb(163, 109, 117));
        g.FillPolygon(pediment, new[] { new Point(181, 38), new Point(239, 20), new Point(298, 38) });
        using var stone = new SolidBrush(Color.FromArgb(104, 72, 90));
        using var fire = new SolidBrush(Color.FromArgb(255, 135, 65));
        foreach (var x in new[] { 7, 466 }) foreach (var y in new[] { 85, 159, 236 })
        {
            g.FillRectangle(stone, x, y, 7, 18); g.FillRectangle(fire, x, y - 4, 7, 4);
            g.FillRectangle(moon, x + 2, y - 8, 3, 6);
        }
    }

    /// <summary>The attract screen between battles, where the star count changes.</summary>
    private void DrawIdleScene(Graphics g)
    {
        _sprites.Draw(g, "hero", 125, 253, false, true, 0, false);
        _sprites.Draw(g, "Hydra", 360, 253, true, true, 0, false);
        for (var i = 0; i < 6; i++)
        {
            var x = 180 + i * 24; var y = 241 - (i % 2) * 8;
            using var star = new Pen(i < CompletedStories ? Gold : Color.FromArgb(58, 42, 70));
            g.DrawLine(star, x - 3, y, x + 3, y); g.DrawLine(star, x, y - 3, x, y + 3);
            if (i > 0) g.DrawLine(star, x - 21, 241 - ((i - 1) % 2) * 8, x - 3, y);
        }
        if (CompletedStories > 0) Center(g, $"{CompletedStories} {(CompletedStories == 1 ? "STORY" : "STORIES")} WRITTEN IN THE STARS", _small, Gold, 266);
    }

    private void DrawArena(Graphics g, BronzeArena a)
    {
        var state = g.Save();
        if (a.Shake > 0) g.TranslateTransform((int)(Math.Sin(a.Time * 83) * a.Shake), (int)(Math.Cos(a.Time * 61) * a.Shake));
        foreach (var pickup in a.Pickups)
        {
            using var b = new SolidBrush(pickup.Healing ? Color.FromArgb(255, 103, 130) : Cyan);
            var x = (int)pickup.Position.X; var y = (int)pickup.Position.Y;
            g.FillRectangle(b, x - 4, y - 2, 8, 4); g.FillRectangle(b, x - 2, y - 4, 4, 8);
        }
        foreach (var enemy in a.Enemies.OrderBy(e => e.Position.Y))
        {
            var x = enemy.Position.X; var y = enemy.Position.Y;
            using var shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
            g.FillEllipse(shadow, x - enemy.Radius, y - 3, enemy.Radius * 2, 7);
            if (enemy.Telegraph > 0)
            {
                using var warning = new Pen(Color.FromArgb(255, 77, 111));
                var r = enemy.Kind == BronzeEnemyKind.Cyclops ? 44 : 18;
                g.DrawEllipse(warning, x - r, y - r, r * 2, r * 2);
                g.DrawLine(warning, x, y, x + enemy.Aim.X * 45, y + enemy.Aim.Y * 45);
            }
            _sprites.Draw(g, enemy.Kind.ToString(), x, y, a.Player.X < x, enemy.Boss, a.Time, enemy.Flash > 0);
            using var health = new SolidBrush(enemy.Boss ? Gold : Color.FromArgb(210, 70, 112));
            g.FillRectangle(health, x - 10, y + 8, 20 * Math.Max(0, enemy.Health) / enemy.MaxHealth, 2);
        }
        var px = a.Player.X; var py = a.Player.Y;
        if (a.Invulnerable == 0 || (int)(a.Time * 18) % 2 == 0)
            _sprites.Draw(g, "hero", px, py, a.Facing.X < 0, false, a.Time, false);
        if (a.Shielding)
        {
            using var shield = new Pen(Cyan, 2);
            g.DrawEllipse(shield, px + a.Facing.X * 9 - 5, py + a.Facing.Y * 9 - 8, 10, 12);
        }
        if (a.ConcealTime > 0) Write(g, "UNSEEN", px - 18, py - 25, Cyan);
        if (a.StrikeTime > 0)
        {
            using var weapon = new Pen(Gold, 2);
            g.DrawLine(weapon, px, py - 4, px + a.Facing.X * 38, py - 4 + a.Facing.Y * 38);
            if (a.Level >= 3) g.DrawArc(weapon, px - 30, py - 30, 60, 60, MathF.Atan2(a.Facing.Y, a.Facing.X) * 180 / MathF.PI - 70, 140);
        }
        if (a.MagicTime > 0 && a.Level >= 4)
        {
            using var ring = new Pen(Cyan, 2); var r = (1 - a.MagicTime / .3f) * 100;
            g.DrawEllipse(ring, px - r, py - r, r * 2, r * 2);
        }
        else if (a.MagicTime > 0)
        {
            var r = 3 + a.MagicTime * 20;
            using var flare = new SolidBrush(Cyan);
            g.FillEllipse(flare, px + a.Facing.X * 12 - r, py + a.Facing.Y * 12 - r, r * 2, r * 2);
            using var core = new SolidBrush(Color.FromArgb(230, 255, 241));
            g.FillRectangle(core, px + a.Facing.X * 12 - 2, py + a.Facing.Y * 12 - 2, 4, 4);
        }
        foreach (var shot in a.Shots)
        {
            var color = shot.Hostile ? Color.FromArgb(244, 68, 120) : shot.Magic || shot.SeaBlessed ? Cyan : Gold;
            using var pen = new Pen(color, shot.Magic ? 5 : 2);
            g.DrawLine(pen, shot.Position.X, shot.Position.Y, shot.Position.X - shot.Velocity.X * .055f, shot.Position.Y - shot.Velocity.Y * .055f);
            using var core = new SolidBrush(shot.Magic || shot.Reflected ? Color.FromArgb(240, 255, 238) : Gold);
            if (!shot.Hostile) g.FillRectangle(core, shot.Position.X - 2, shot.Position.Y - 2, 4, 4);
        }
        foreach (var spark in a.Sparks)
        {
            using var brush = new SolidBrush(spark.Color == 0 ? Gold : spark.Color == 1 ? Cyan : Color.FromArgb(241, 77, 119));
            g.FillRectangle(brush, (int)spark.Position.X, (int)spark.Position.Y, 2, 2);
        }
        g.Restore(state);
        using var hud = new SolidBrush(Color.FromArgb(9, 8, 20)); g.FillRectangle(hud, 0, 0, 480, 20); g.FillRectangle(hud, 0, 275, 480, 25);
        Write(g, $"CH {a.Level:00}   SCORE {a.Score:000000}", 8, 4, Gold);
        Write(g, $"FOES {Math.Max(0, a.Remaining):00}", 218, 4, Gold);
        Bar(g, "HP", 281, a.Health / a.MaxHealth, Color.FromArgb(255, 104, 139));
        Bar(g, "MP", 347, a.Mana / 100, Cyan); Bar(g, "GD", 413, a.Guard / 100, Gold);
        Write(g, $"J STRIKE  K {a.RangedName}  L {a.MagicName} ({a.MagicCost})", 8, 279, Gold);
        Write(g, a.MagicReadiness, 396, 279, Cyan);
        Write(g, "WASD MOVE  SHIFT SHIELD  SPACE DODGE  ESC PAUSE  F6 LIBRARY", 8, 289, Cyan);
        if (a.MagicFeedbackTime > 0)
        {
            using var backing = new SolidBrush(Color.FromArgb(225, 9, 8, 20));
            g.FillRectangle(backing, 120, 257, 240, 14);
            Center(g, a.MagicFeedback, _small, Cyan, 258);
        }
        var boss = a.Enemies.FirstOrDefault(e => e.Boss);
        if (boss != null)
        {
            using var pen = new Pen(Gold); g.DrawRectangle(pen, 160, 24, 160, 5);
            using var brush = new SolidBrush(Color.FromArgb(205, 66, 113));
            g.FillRectangle(brush, 161, 25, 158 * Math.Max(0, boss.Health) / boss.MaxHealth, 3);
            Center(g, boss.Kind.ToString().ToUpperInvariant(), _small, Gold, 31);
        }
    }

    private void Bar(Graphics g, string label, int x, float value, Color color)
    {
        Write(g, label, x, 4, color);
        using var back = new SolidBrush(Color.FromArgb(54, 40, 63));
        using var fill = new SolidBrush(color);
        g.FillRectangle(back, x + 14, 6, 42, 5); g.FillRectangle(fill, x + 14, 6, Math.Clamp(value, 0, 1) * 42, 5);
    }
    private void Write(Graphics g, string text, float x, float y, Color color)
    { using var brush = new SolidBrush(color); g.DrawString(text, _small, brush, x, y); }
    private static void Center(Graphics g, string text, Font font, Color color, float y)
    { using var brush = new SolidBrush(color); g.DrawString(text, font, brush, (480 - g.MeasureString(text, font).Width) / 2, y); }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frame.Dispose(); _backdrop?.Dispose(); _scanlineOverlay?.Dispose();
            _sprites.Dispose(); _small.Dispose(); _medium.Dispose(); _large.Dispose();
        }
        base.Dispose(disposing);
    }
}

