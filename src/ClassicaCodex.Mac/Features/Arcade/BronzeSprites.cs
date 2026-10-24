using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// Original hand-authored pixel silhouettes, drawn on the arcade's native
/// pixel grid - the Windows sprites, digit for digit.
///
/// Each digit is an index into the palette below, so a sprite is readable
/// and editable as text. The colours are the game's own, not the
/// application's: the arena is a little night-time screen that looks the
/// same in Parchment and in Lamplight, and its gold and cyan are part of the
/// art the way a painting's colours are.
///
/// Built once and shared, being the same pixels for every window and every
/// frame. Drawn with nearest-neighbour sampling, so a boss at double size is
/// chunky rather than blurred.
/// </summary>
internal static class BronzeSprites
{
    private static readonly Color[] Palette =
    {
        Colors.Transparent, Color.FromRgb(27, 17, 44),
        Color.FromRgb(255, 207, 113), Color.FromRgb(197, 110, 49), Color.FromRgb(246, 151, 116),
        Color.FromRgb(217, 50, 103), Color.FromRgb(102, 240, 216), Color.FromRgb(99, 129, 68),
        Color.FromRgb(173, 112, 197), Color.FromRgb(255, 244, 208)
    };

    private static readonly IPen FlashPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(255, 244, 208)), 1);

    private static readonly Dictionary<string, string[]> Rows = new(StringComparer.Ordinal)
    {
        ["hero"] = new[] {
            "000055550000", "000555555000", "000222220000", "000233324000",
            "000234994000", "000034440000", "002222330000", "022323344300",
            "223323344440", "233322330000", "023333330000", "000555550000",
            "000555550000", "000440440000", "000330330000", "003330333000" },
        ["Serpent"] = new[] {
            "0000000777700000", "0000007272770000", "0000007997770000", "0000007777700000",
            "0000000075500000", "0000777070000000", "0077227770000000", "0777777777770000",
            "7722777772277000", "7777000077777000", "0777777777770770", "0007777777700077" },
        ["Harpy"] = new[] {
            "8000000440000008", "8800002442000088", "8880004994000888", "8888004444008888",
            "0888805555088880", "0088885555888800", "0008888888888000", "0000888888880000",
            "0000088888800000", "0000008888000000", "0000020000200000", "0000220000220000" },
        ["Boar"] = new[] {
            "0000030000030000", "0000333333330000", "0003333333333300", "0033383333333330",
            "0333333333333333", "0333333333993333", "0333333333333399", "0033333333333990",
            "0003333333333300", "0000330000330000", "0000330000330000", "0000110000110000" },
        ["Cyclops"] = new[] {
            "0000333333000000", "0003444444300000", "0004499944400000", "0004491944400000",
            "0004499944400000", "0003444444300000", "0000441144000000", "0034444444430000",
            "0344333333443000", "3443333333344300", "3443333333344300", "0443333333344000",
            "0003355555330000", "0003555555530000", "0000440044000000", "0003330033300000" },
        ["Gorgon"] = new[] {
            "0077070070770000", "0727777777277000", "0777727777770000", "0077444477700000",
            "0074499447700000", "0007444470000000", "0000777700000000", "0008888880000000",
            "0088888888000000", "0888888888800000", "0088888888000000", "0007777770000000",
            "0077227777000000", "0777777777770000", "0077777770077700", "0000000000007770" },
        ["Hydra"] = new[] {
            "0077700000077700", "0727700770077270", "0797707227077970", "0077707997077700",
            "0007700770077000", "0007770770777000", "0000777777770000", "0000077777700000",
            "0000772277770000", "0007777777777000", "0077777777777700", "0777707777077770",
            "0777007777007770", "0770007777000770", "0077777777777700", "0007770000777000" }
    };

    private static readonly Dictionary<string, Bitmap> Bitmaps = new(StringComparer.Ordinal);

    /// <summary>Draws a sprite standing on (<paramref name="x"/>, <paramref name="y"/>), as the Windows game does.</summary>
    public static void Draw(DrawingContext g, string name, float x, float y, bool flip, bool boss, float time, bool flash)
    {
        var sprite = Get(name);
        var width = sprite.PixelSize.Width;
        var height = sprite.PixelSize.Height;
        var scale = boss ? 2 : 1;
        var bob = (int)(Math.Sin(time * 9) * 1.2);

        // Mirror first, then move into place: the order GDI+'s prepended
        // transforms give the Windows sprites.
        var transform = Matrix.CreateScale(flip ? -1 : 1, 1) * Matrix.CreateTranslation((int)x, (int)y + bob);
        using (g.PushTransform(transform))
        {
            var target = new Rect(-width * scale / 2, -height * scale + 5, width * scale, height * scale);
            g.DrawImage(sprite, new Rect(0, 0, width, height), target);
            if (flash) g.DrawRectangle(null, FlashPen, target.Translate(new Vector(.5, .5)));
        }
    }

    private static Bitmap Get(string name)
    {
        if (Bitmaps.TryGetValue(name, out var cached)) return cached;

        var rows = Rows[name];
        var width = rows.Max(r => r.Length);
        var height = rows.Length;
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var buffer = bitmap.Lock())
        {
            var line = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                Array.Clear(line);
                for (var x = 0; x < rows[y].Length; x++)
                {
                    // Premultiplied: every colour but the transparent one is
                    // opaque, and the transparent one must be all zeroes -
                    // Colors.Transparent is white with no alpha, which
                    // premultiplied would paint every gap in the sprite white.
                    var colour = Palette[rows[y][x] - '0'];
                    if (colour.A == 0) continue;
                    line[x * 4] = colour.B;
                    line[x * 4 + 1] = colour.G;
                    line[x * 4 + 2] = colour.R;
                    line[x * 4 + 3] = colour.A;
                }

                Marshal.Copy(line, 0, buffer.Address + y * buffer.RowBytes, line.Length);
            }
        }

        Bitmaps[name] = bitmap;
        return bitmap;
    }
}
