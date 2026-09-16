using System.Drawing.Drawing2D;
using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.UI;

/// <summary>
/// A small round portrait for each critic in Fictional Ancient Reactions.
///
/// <b>Drawn rather than shipped.</b> Thirty-odd PNGs would have been the
/// obvious answer and would have been wrong three times over: they would live
/// in the Icons folder, which is the one part of this download a person can
/// lose by extracting the ZIP badly (see Program's startup check); they would
/// be drawn for one display scaling and resampled at every other, which is
/// exactly the fuzz the rest of this application goes to some trouble to
/// avoid; and a new critic would need an artist. A recipe in the pack file
/// needs neither.
///
/// So a critic carries a trait string - "skin:tan;hair:grey;beard:full;
/// hat:laurel;cloth:#7b5230;accent:#3f2a16" - and this turns it into a face.
/// Unknown traits fall back rather than throw, because the packs are hand
/// written and a typo in a hairstyle should cost a hairstyle, not a window.
///
/// <b>These are cartoons and are meant to look like cartoons.</b> Nobody knows
/// what Plato looked like; the surviving portrait busts are Roman copies made
/// centuries after he died, and the faces of the invented critics never
/// existed at all. A friendly circular doodle is honest about that in a way
/// that a solemn marble bust would not be, and this feature's whole problem is
/// being clear about what is invented.
/// </summary>
internal static class AncientAvatars
{
    private static readonly Dictionary<(string Traits, int Size), Image> Cache = new();

    /// <summary>
    /// Everything drawn below is positioned in a 100x100 box and scaled to the
    /// size asked for, so a portrait is the same portrait at 40 pixels and at
    /// 96 - which is what a 200% display asks for.
    /// </summary>
    private const float Design = 100f;

    private static readonly Dictionary<string, Color> Skins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["light"] = Color.FromArgb(240, 206, 178),
        ["olive"] = Color.FromArgb(214, 176, 138),
        ["tan"] = Color.FromArgb(196, 150, 110),
        ["brown"] = Color.FromArgb(158, 113, 78),
        ["dark"] = Color.FromArgb(118, 82, 56)
    };

    private static readonly Dictionary<string, Color> Hairs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dark"] = Color.FromArgb(58, 44, 38),
        ["brown"] = Color.FromArgb(98, 68, 44),
        ["red"] = Color.FromArgb(150, 78, 42),
        ["grey"] = Color.FromArgb(132, 128, 122),
        ["white"] = Color.FromArgb(226, 222, 214)
    };

    public static Image Portrait(AncientCritic critic, int size) =>
        Portrait(critic.Avatar, size);

    public static Image Portrait(string? traits, int size)
    {
        size = Math.Max(16, size);
        var key = (traits ?? string.Empty, size);

        if (Cache.TryGetValue(key, out var cached)) return cached;

        var portrait = Draw(Parse(traits), size);
        Cache[key] = portrait;
        return portrait;
    }

    private sealed class Traits
    {
        public Color Skin = Skins["olive"];
        public Color Hair = Hairs["dark"];
        public bool Bald;
        public string Beard = "none";
        public string Hat = "none";
        public Color Cloth = Color.FromArgb(110, 120, 140);
        public Color Accent = Color.FromArgb(214, 190, 140);
    }

    private static Traits Parse(string? traits)
    {
        var result = new Traits();
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
    /// ColorTranslator.FromHtml throws on malformed input, and these strings
    /// are typed by hand into a JSON file.
    /// </summary>
    private static Color Hex(string value, Color fallback)
    {
        try
        {
            return value.StartsWith('#') ? ColorTranslator.FromHtml(value) : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>
    /// The order here is the whole trick, and getting it wrong is what the
    /// first version did.
    ///
    /// Hair is a slightly larger, slightly higher ellipse drawn BEHIND the
    /// face, so what shows is the band around the crown and temples. Built the
    /// other way round - arcs and beziers traced round the hairline, drawn on
    /// top - it produced a row of bald men wearing ear-muffs, and no beards at
    /// all, because the paths closed into shapes that enclosed almost nothing.
    /// A backdrop cannot fail that way: there is no outline to get wrong.
    ///
    /// The veil is a backdrop for the same reason. Drawn over the face and
    /// then cut back out, it removed the features along with itself and left
    /// two critics with blank ovals for heads.
    /// </summary>
    private static Image Draw(Traits traits, int size)
    {
        var bitmap = new Bitmap(size, size);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.ScaleTransform(size / Design, size / Design);

            // Everything is clipped to the circle, so the shoulders can simply
            // be drawn oversized and run off the bottom edge the way a
            // cropped photograph does.
            using var disc = new GraphicsPath();
            disc.AddEllipse(0, 0, Design, Design);
            g.SetClip(disc);

            DrawBackground(g, traits);
            DrawShoulders(g, traits);

            var veiled = string.Equals(traits.Hat, "veil", StringComparison.OrdinalIgnoreCase);

            if (veiled) DrawVeil(g, traits);
            else if (!traits.Bald) DrawHair(g, traits);

            DrawHeadAndFace(g, traits);
            DrawBeard(g, traits);
            DrawMouth(g, traits);

            if (!veiled) DrawHeadwear(g, traits);

            g.ResetClip();

            // A ring, so a pale portrait still has an edge against a pale
            // bubble and a dark one against a dark window.
            using var ring = new Pen(Blend(traits.Cloth, Color.Black, 0.25f), 3f);
            g.DrawEllipse(ring, 1.5f, 1.5f, Design - 3f, Design - 3f);
        }

        return bitmap;
    }

    private static void DrawBackground(Graphics g, Traits traits)
    {
        // A wash of the garment colour, well lightened. Tying it to the
        // garment gives every critic a recognisable colour at a glance, which
        // is what a chat window needs when six people are talking.
        using var brush = new SolidBrush(Blend(traits.Cloth, Color.White, 0.72f));
        g.FillEllipse(brush, 0, 0, Design, Design);
    }

    private static void DrawShoulders(Graphics g, Traits traits)
    {
        using var neck = new SolidBrush(Blend(traits.Skin, Color.Black, 0.12f));
        g.FillRectangle(neck, 42f, 62f, 16f, 20f);

        using var cloth = new SolidBrush(traits.Cloth);
        g.FillEllipse(cloth, 8f, 78f, 84f, 56f);

        // The fold over one shoulder that says "draped" rather than "shirt" -
        // a himation on a Greek, a toga on a Roman, and close enough to both
        // at forty pixels.
        using var fold = new SolidBrush(Blend(traits.Cloth, Color.Black, 0.18f));
        using var path = new GraphicsPath();
        path.AddPolygon(new[]
        {
            new PointF(34f, 82f), new PointF(50f, 100f), new PointF(66f, 82f),
            new PointF(58f, 80f), new PointF(50f, 90f), new PointF(42f, 80f)
        });
        g.FillPath(fold, path);

        using var trim = new Pen(traits.Accent, 2.6f);
        g.DrawArc(trim, 8f, 78f, 84f, 56f, 200f, 140f);
    }

    /// <summary>The hair, behind the head. See <see cref="Draw"/> for why.</summary>
    private static void DrawHair(Graphics g, Traits traits)
    {
        using var brush = new SolidBrush(traits.Hair);
        g.FillEllipse(brush, 21f, 7f, 58f, 56f);
    }

    /// <summary>
    /// A himation drawn up over the head, which is how a respectable Greek
    /// woman appears in public and how most surviving images of one look.
    /// Behind the face, so it frames rather than covers.
    /// </summary>
    private static void DrawVeil(Graphics g, Traits traits)
    {
        using var cloth = new SolidBrush(Blend(traits.Cloth, Color.White, 0.3f));
        using var shadow = new SolidBrush(Blend(traits.Cloth, Color.Black, 0.1f));

        using var drape = new GraphicsPath();
        drape.AddPolygon(new[]
        {
            new PointF(17f, 48f), new PointF(83f, 48f),
            new PointF(95f, 104f), new PointF(5f, 104f)
        });
        g.FillPath(shadow, drape);

        g.FillEllipse(cloth, 16f, 4f, 68f, 66f);
    }

    private static void DrawHeadAndFace(Graphics g, Traits traits)
    {
        using var skin = new SolidBrush(traits.Skin);
        using var shade = new SolidBrush(Blend(traits.Skin, Color.Black, 0.14f));

        g.FillEllipse(shade, 24f, 40f, 8f, 13f);   // ears
        g.FillEllipse(shade, 68f, 40f, 8f, 13f);
        g.FillEllipse(skin, 26f, 16f, 48f, 54f);   // head

        // Eyes large and set low, which is most of what makes a drawn face
        // read as friendly rather than as a portrait bust.
        using var white = new SolidBrush(Color.FromArgb(252, 250, 246));
        using var pupil = new SolidBrush(Color.FromArgb(46, 38, 34));

        g.FillEllipse(white, 36f, 38f, 10f, 8f);
        g.FillEllipse(white, 54f, 38f, 10f, 8f);
        g.FillEllipse(pupil, 39f, 39.5f, 5f, 5f);
        g.FillEllipse(pupil, 57f, 39.5f, 5f, 5f);

        using var brow = new Pen(Blend(traits.Hair, Color.Black, 0.15f), 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawArc(brow, 35f, 31f, 12f, 9f, 195f, 150f);
        g.DrawArc(brow, 53f, 31f, 12f, 9f, 195f, 150f);
    }

    /// <summary>
    /// A mass of hair clipped to the head rather than a traced outline, for
    /// the reason given on <see cref="Draw"/>. A full beard starts under the
    /// cheekbones and takes the mouth with it; a short one is a chin strap
    /// below it.
    /// </summary>
    private static void DrawBeard(Graphics g, Traits traits)
    {
        if (string.Equals(traits.Beard, "none", StringComparison.OrdinalIgnoreCase)) return;

        var full = string.Equals(traits.Beard, "full", StringComparison.OrdinalIgnoreCase);

        var state = g.Save();

        using (var head = new GraphicsPath())
        {
            head.AddEllipse(26f, 16f, 48f, 54f);
            g.SetClip(head, CombineMode.Intersect);
        }

        using (var brush = new SolidBrush(traits.Hair))
        {
            if (full) g.FillEllipse(brush, 19f, 44f, 62f, 52f);
            else g.FillEllipse(brush, 28f, 57f, 44f, 32f);
        }

        g.Restore(state);

        // The moustache sits outside the head clip on purpose: it is the one
        // part of a beard that reads at forty pixels.
        if (full)
        {
            using var brush = new SolidBrush(Blend(traits.Hair, Color.Black, 0.1f));
            g.FillEllipse(brush, 38f, 47f, 24f, 8f);
        }
    }

    /// <summary>
    /// Drawn after the beard, so a bearded critic has lips rather than a hole
    /// in the hair - and an unbearded one gets an ordinary line.
    /// </summary>
    private static void DrawMouth(Graphics g, Traits traits)
    {
        if (string.Equals(traits.Beard, "full", StringComparison.OrdinalIgnoreCase))
        {
            using var lips = new SolidBrush(Blend(traits.Skin, Color.FromArgb(150, 72, 66), 0.4f));
            g.FillEllipse(lips, 42f, 55f, 16f, 6f);
            return;
        }

        using var mouth = new Pen(Blend(traits.Skin, Color.Black, 0.45f), 2f) { EndCap = LineCap.Round };
        g.DrawArc(mouth, 42f, 50f, 16f, 10f, 20f, 140f);
    }

    private static void DrawHeadwear(Graphics g, Traits traits)
    {
        switch (traits.Hat.ToLowerInvariant())
        {
            case "laurel":
                DrawLaurel(g, traits.Accent);
                break;

            case "fillet":
                using (var band = new Pen(traits.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(band, 25f, 15f, 50f, 40f, 190f, 160f);
                break;

            case "cap":
                using (var cap = new SolidBrush(Blend(traits.Accent, Color.Black, 0.2f)))
                using (var path = new GraphicsPath())
                {
                    path.AddArc(24f, 10f, 52f, 42f, 180f, 180f);
                    path.CloseFigure();
                    g.FillPath(cap, path);
                }
                break;
        }
    }

    /// <summary>
    /// Eight small leaves up each side of the crown. Not botany - it has to
    /// read as a wreath at forty pixels and as something deliberate at
    /// ninety-six.
    /// </summary>
    private static void DrawLaurel(Graphics g, Color accent)
    {
        using var leaf = new SolidBrush(Blend(accent, Color.FromArgb(70, 110, 70), 0.45f));
        using var stem = new Pen(Blend(accent, Color.Black, 0.35f), 2f);

        g.DrawArc(stem, 24f, 13f, 52f, 44f, 190f, 160f);

        for (var i = 0; i < 8; i++)
        {
            var angle = (float)(Math.PI * (1.06 + i * 0.12));
            var cx = 50f + 26f * (float)Math.Cos(angle);
            var cy = 35f + 22f * (float)Math.Sin(angle);

            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-70f + i * 18f);
            g.FillEllipse(leaf, -6f, -2.6f, 12f, 5.2f);
            g.Restore(state);
        }
    }

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

    internal static Color Mix(Color from, Color to, float amount) => Blend(from, to, amount);

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            from.A,
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }
}
