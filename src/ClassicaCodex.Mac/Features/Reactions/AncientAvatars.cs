using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.Mac.Features.Reactions;

/// <summary>
/// A small round portrait for each critic in Fictional Ancient Reactions,
/// drawn from the recipe in the pack - the Windows AncientAvatars, stroke for
/// stroke, on Avalonia's drawing context instead of GDI+.
///
/// <b>Drawn rather than shipped.</b> Thirty-odd PNGs would need an artist for
/// every new critic and would be painted for one backing scale and resampled
/// on every other. A recipe in the pack file needs neither: a critic carries a
/// trait string - "skin:tan;hair:grey;beard:full;hat:laurel;cloth:#7b5230;
/// accent:#3f2a16" - and this turns it into a face, as vectors, so it is as
/// sharp on a Retina display as the text beside it. Unknown traits fall back
/// rather than throw, because the packs are hand written and a typo in a
/// hairstyle should cost a hairstyle, not a window.
///
/// <b>These are cartoons and are meant to look like cartoons.</b> Nobody knows
/// what Plato looked like; the surviving portrait busts are Roman copies made
/// centuries after he died, and the faces of the invented critics never
/// existed at all. A friendly circular doodle is honest about that in a way
/// that a solemn marble bust would not be, and this feature's whole problem is
/// being clear about what is invented.
///
/// The colours here are the recipe's own - skin, hair, cloth - and so are
/// fixed data colours rather than the palette's: Pherekles' saffron robe is
/// saffron in either appearance.
/// </summary>
internal static class AncientAvatars
{
    /// <summary>
    /// Everything drawn below is positioned in a 100x100 box and scaled to the
    /// size asked for, so a portrait is the same portrait at 44 points and at
    /// 96.
    /// </summary>
    private const double Design = 100;

    private static readonly Dictionary<string, Color> Skins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["light"] = Color.FromRgb(240, 206, 178),
        ["olive"] = Color.FromRgb(214, 176, 138),
        ["tan"] = Color.FromRgb(196, 150, 110),
        ["brown"] = Color.FromRgb(158, 113, 78),
        ["dark"] = Color.FromRgb(118, 82, 56)
    };

    private static readonly Dictionary<string, Color> Hairs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dark"] = Color.FromRgb(58, 44, 38),
        ["brown"] = Color.FromRgb(98, 68, 44),
        ["red"] = Color.FromRgb(150, 78, 42),
        ["grey"] = Color.FromRgb(132, 128, 122),
        ["white"] = Color.FromRgb(226, 222, 214)
    };

    private static readonly Color Black = Colors.Black;
    private static readonly Color White = Colors.White;

    /// <summary>A recipe, parsed.</summary>
    internal sealed class Recipe
    {
        public Color Skin = Skins["olive"];
        public Color Hair = Hairs["dark"];
        public bool Bald;
        public string Beard = "none";
        public string Hat = "none";
        public Color Cloth = Color.FromRgb(110, 120, 140);
        public Color Accent = Color.FromRgb(214, 190, 140);
    }

    internal static Recipe Parse(string? traits)
    {
        var result = new Recipe();
        if (string.IsNullOrWhiteSpace(traits)) return result;

        foreach (var pair in traits.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = pair.IndexOf(':');
            if (colon <= 0) continue;

            var name = pair[..colon].Trim();
            var value = pair[(colon + 1)..].Trim();

            switch (name.ToLowerInvariant())
            {
                case "skin" when Skins.TryGetValue(value, out var skin): result.Skin = skin; break;
                case "hair" when Hairs.TryGetValue(value, out var hair): result.Hair = hair; break;
                case "hair" when string.Equals(value, "bald", StringComparison.OrdinalIgnoreCase): result.Bald = true; break;
                case "beard": result.Beard = value; break;
                case "hat": result.Hat = value; break;
                case "cloth": result.Cloth = Hex(value, result.Cloth); break;
                case "accent": result.Accent = Hex(value, result.Accent); break;
            }
        }

        return result;
    }

    /// <summary>
    /// "#7b5230" to a colour, and anything else to what was already there.
    /// These strings are typed by hand into a JSON file.
    /// </summary>
    private static Color Hex(string value, Color fallback) =>
        value.StartsWith('#') && Color.TryParse(value, out var colour) ? colour : fallback;

    /// <summary>
    /// The one colour that stands for a critic - their garment.
    ///
    /// Used for the tint of their speech bubble, so that following who is
    /// talking in a six-way conversation does not depend on reading the name
    /// each time. It comes from the same trait string as the portrait, so the
    /// bubble and the face always agree without anything having to keep them
    /// in step.
    /// </summary>
    public static Color Signature(string? traits) => Parse(traits).Cloth;

    /// <summary>Moves <paramref name="from"/> toward <paramref name="to"/> by <paramref name="amount"/>, keeping its alpha.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            from.A,
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }

    /// <summary>
    /// Draws the portrait into a square of <paramref name="size"/> at the origin.
    ///
    /// The order here is the whole trick, and getting it wrong is what the
    /// first Windows version did. Hair is a slightly larger, slightly higher
    /// ellipse drawn BEHIND the face, so what shows is the band around the
    /// crown and temples. Built the other way round - arcs traced round the
    /// hairline, drawn on top - it produced a row of bald men wearing
    /// ear-muffs, and no beards at all, because the paths closed into shapes
    /// that enclosed almost nothing. A backdrop cannot fail that way: there is
    /// no outline to get wrong.
    ///
    /// The veil is a backdrop for the same reason. Drawn over the face and
    /// then cut back out, it removed the features along with itself and left
    /// two critics with blank ovals for heads.
    /// </summary>
    public static void Draw(DrawingContext context, Recipe recipe, double size)
    {
        using var scale = context.PushTransform(Matrix.CreateScale(size / Design, size / Design));

        // Everything is clipped to the circle, so the shoulders can simply be
        // drawn oversized and run off the bottom edge the way a cropped
        // photograph does.
        using (context.PushGeometryClip(new EllipseGeometry(new Rect(0, 0, Design, Design))))
        {
            // A wash of the garment colour, well lightened. Tying it to the
            // garment gives every critic a recognisable colour at a glance,
            // which is what a chat window needs when six people are talking.
            Ellipse(context, Fill(Mix(recipe.Cloth, White, 0.72)), 0, 0, Design, Design);

            DrawShoulders(context, recipe);

            var veiled = string.Equals(recipe.Hat, "veil", StringComparison.OrdinalIgnoreCase);

            if (veiled) DrawVeil(context, recipe);
            else if (!recipe.Bald) Ellipse(context, Fill(recipe.Hair), 21, 7, 58, 56);

            DrawHeadAndFace(context, recipe);
            DrawBeard(context, recipe);
            DrawMouth(context, recipe);

            if (!veiled) DrawHeadwear(context, recipe);
        }

        // A ring, so a pale portrait still has an edge against a pale bubble
        // and a dark one against a dark window.
        context.DrawEllipse(null, new Pen(Fill(Mix(recipe.Cloth, Black, 0.25)), 3), new Point(50, 50), 48.5, 48.5);
    }

    private static void DrawShoulders(DrawingContext context, Recipe recipe)
    {
        context.FillRectangle(Fill(Mix(recipe.Skin, Black, 0.12)), new Rect(42, 62, 16, 20));
        Ellipse(context, Fill(recipe.Cloth), 8, 78, 84, 56);

        // The fold over one shoulder that says "draped" rather than "shirt" -
        // a himation on a Greek, a toga on a Roman, and close enough to both
        // at forty points.
        context.DrawGeometry(Fill(Mix(recipe.Cloth, Black, 0.18)), null, Polygon(
            new Point(34, 82), new Point(50, 100), new Point(66, 82),
            new Point(58, 80), new Point(50, 90), new Point(42, 80)));

        context.DrawGeometry(null, new Pen(Fill(recipe.Accent), 2.6), Arc(8, 78, 84, 56, 200, 140));
    }

    /// <summary>
    /// A himation drawn up over the head, which is how a respectable Greek
    /// woman appears in public and how most surviving images of one look.
    /// Behind the face, so it frames rather than covers.
    /// </summary>
    private static void DrawVeil(DrawingContext context, Recipe recipe)
    {
        context.DrawGeometry(Fill(Mix(recipe.Cloth, Black, 0.1)), null, Polygon(
            new Point(17, 48), new Point(83, 48), new Point(95, 104), new Point(5, 104)));

        Ellipse(context, Fill(Mix(recipe.Cloth, White, 0.3)), 16, 4, 68, 66);
    }

    private static void DrawHeadAndFace(DrawingContext context, Recipe recipe)
    {
        var skin = Fill(recipe.Skin);
        var shade = Fill(Mix(recipe.Skin, Black, 0.14));

        Ellipse(context, shade, 24, 40, 8, 13);   // ears
        Ellipse(context, shade, 68, 40, 8, 13);
        Ellipse(context, skin, 26, 16, 48, 54);   // head

        // Eyes large and set low, which is most of what makes a drawn face
        // read as friendly rather than as a portrait bust.
        var white = Fill(Color.FromRgb(252, 250, 246));
        var pupil = Fill(Color.FromRgb(46, 38, 34));

        Ellipse(context, white, 36, 38, 10, 8);
        Ellipse(context, white, 54, 38, 10, 8);
        Ellipse(context, pupil, 39, 39.5, 5, 5);
        Ellipse(context, pupil, 57, 39.5, 5, 5);

        var brow = new Pen(Fill(Mix(recipe.Hair, Black, 0.15)), 2.2) { LineCap = PenLineCap.Round };
        context.DrawGeometry(null, brow, Arc(35, 31, 12, 9, 195, 150));
        context.DrawGeometry(null, brow, Arc(53, 31, 12, 9, 195, 150));
    }

    /// <summary>
    /// A mass of hair clipped to the head rather than a traced outline, for
    /// the reason given on <see cref="Draw"/>. A full beard starts under the
    /// cheekbones and takes the mouth with it; a short one is a chin strap
    /// below it.
    /// </summary>
    private static void DrawBeard(DrawingContext context, Recipe recipe)
    {
        if (string.Equals(recipe.Beard, "none", StringComparison.OrdinalIgnoreCase)) return;

        var full = string.Equals(recipe.Beard, "full", StringComparison.OrdinalIgnoreCase);

        using (context.PushGeometryClip(new EllipseGeometry(new Rect(26, 16, 48, 54))))
        {
            if (full) Ellipse(context, Fill(recipe.Hair), 19, 44, 62, 52);
            else Ellipse(context, Fill(recipe.Hair), 28, 57, 44, 32);
        }

        // The moustache sits outside the head clip on purpose: it is the one
        // part of a beard that reads at forty points.
        if (full) Ellipse(context, Fill(Mix(recipe.Hair, Black, 0.1)), 38, 47, 24, 8);
    }

    /// <summary>
    /// Drawn after the beard, so a bearded critic has lips rather than a hole
    /// in the hair - and an unbearded one gets an ordinary line.
    /// </summary>
    private static void DrawMouth(DrawingContext context, Recipe recipe)
    {
        if (string.Equals(recipe.Beard, "full", StringComparison.OrdinalIgnoreCase))
        {
            Ellipse(context, Fill(Mix(recipe.Skin, Color.FromRgb(150, 72, 66), 0.4)), 42, 55, 16, 6);
            return;
        }

        var mouth = new Pen(Fill(Mix(recipe.Skin, Black, 0.45)), 2) { LineCap = PenLineCap.Round };
        context.DrawGeometry(null, mouth, Arc(42, 50, 16, 10, 20, 140));
    }

    private static void DrawHeadwear(DrawingContext context, Recipe recipe)
    {
        switch (recipe.Hat.ToLowerInvariant())
        {
            case "laurel":
                DrawLaurel(context, recipe.Accent);
                break;

            case "fillet":
                context.DrawGeometry(null, new Pen(Fill(recipe.Accent), 4) { LineCap = PenLineCap.Round },
                    Arc(25, 15, 50, 40, 190, 160));
                break;

            case "cap":
                context.DrawGeometry(Fill(Mix(recipe.Accent, Black, 0.2)), null, Arc(24, 10, 52, 42, 180, 180, closed: true));
                break;
        }
    }

    /// <summary>
    /// Eight small leaves up each side of the crown. Not botany - it has to
    /// read as a wreath at forty points and as something deliberate at
    /// ninety-six.
    /// </summary>
    private static void DrawLaurel(DrawingContext context, Color accent)
    {
        var leaf = Fill(Mix(accent, Color.FromRgb(70, 110, 70), 0.45));
        context.DrawGeometry(null, new Pen(Fill(Mix(accent, Black, 0.35)), 2), Arc(24, 13, 52, 44, 190, 160));

        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * (1.06 + i * 0.12);
            var cx = 50 + 26 * Math.Cos(angle);
            var cy = 35 + 22 * Math.Sin(angle);

            // Rotate the leaf about its own centre, then move it into place.
            var placement = Matrix.CreateRotation((-70 + i * 18) * Math.PI / 180) * Matrix.CreateTranslation(cx, cy);
            using (context.PushTransform(placement)) Ellipse(context, leaf, -6, -2.6, 12, 5.2);
        }
    }

    private static IBrush Fill(Color colour) => new SolidColorBrush(colour);

    /// <summary>An ellipse given as its bounding box, the way the Windows drawing code gives them.</summary>
    private static void Ellipse(DrawingContext context, IBrush brush, double x, double y, double width, double height) =>
        context.DrawEllipse(brush, null, new Point(x + width / 2, y + height / 2), width / 2, height / 2);

    private static Geometry Polygon(params Point[] points)
    {
        var geometry = new StreamGeometry();
        using var figure = geometry.Open();
        figure.BeginFigure(points[0], true);
        foreach (var point in points.Skip(1)) figure.LineTo(point);
        figure.EndFigure(true);
        return geometry;
    }

    /// <summary>
    /// An arc of the ellipse in a bounding box, with GDI+'s meaning for the
    /// angles - degrees clockwise from three o'clock, measured to the point on
    /// the ellipse rather than as the parametric angle - so the recipes the
    /// Windows portraits were tuned with land in the same places here.
    /// </summary>
    private static Geometry Arc(double x, double y, double width, double height, double start, double sweep, bool closed = false)
    {
        var a = width / 2;
        var b = height / 2;
        var centre = new Point(x + a, y + b);

        Point At(double degrees)
        {
            var theta = degrees * Math.PI / 180;
            var t = Math.Atan2(a * Math.Sin(theta), b * Math.Cos(theta));
            return new Point(centre.X + a * Math.Cos(t), centre.Y + b * Math.Sin(t));
        }

        var geometry = new StreamGeometry();
        using var figure = geometry.Open();
        figure.BeginFigure(At(start), closed);
        figure.ArcTo(At(start + sweep), new Size(a, b), 0, Math.Abs(sweep) > 180,
            sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
        figure.EndFigure(closed);
        return geometry;
    }
}

/// <summary>A critic's face, drawn at whatever size the control is given.</summary>
internal sealed class CriticPortrait : Control
{
    private readonly AncientAvatars.Recipe _recipe;

    public CriticPortrait(AncientCritic critic, double size)
    {
        _recipe = AncientAvatars.Parse(critic.Avatar);
        Width = size;
        Height = size;
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size > 0) AncientAvatars.Draw(context, _recipe, size);
    }
}
