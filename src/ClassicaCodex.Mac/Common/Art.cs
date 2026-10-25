using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// The painted artwork in Assets/Art, made for the Mac (see ART-BRIEF.md in
/// that folder), with a drawn stand-in for anything not delivered yet.
///
/// Every piece is optional. The brief lets the art arrive a file at a time,
/// so nothing here may fail when one is missing: a tile without its picture
/// becomes a gradient medallion with the tool's glyph on it, which is a
/// finished look in its own right rather than a hole.
/// </summary>
public static class Art
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    /// <summary>The bitmap for Assets/Art/<paramref name="fileName"/>, or null if it is not there.</summary>
    public static Bitmap? Load(string fileName)
    {
        if (Cache.TryGetValue(fileName, out var cached)) return cached;

        Bitmap? bitmap = null;
        var uri = new Uri($"avares://ClassicaCodex.Mac/Assets/Art/{fileName}");

        try
        {
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception)
        {
            // A damaged file is the same as a missing one: the stand-in draws.
        }

        Cache[fileName] = bitmap;
        return bitmap;
    }

    /// <summary>The application's icon, for the window and the Dock.</summary>
    public static Bitmap? AppIcon() => Load("app-icon-1024.png");

    /// <summary>
    /// The page texture for an appearance - vellum in Parchment, linen-paper
    /// grain in Lamplight - tiled at half its pixel size so it stays fine on a
    /// Retina display, or null to leave the page its plain colour.
    /// </summary>
    public static IBrush? PageTexture(bool dark)
    {
        var tile = Load(dark ? "lamplight-tile.png" : "parchment-tile.png");
        if (tile == null) return null;

        return new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, tile.PixelSize.Width / 2.0, tile.PixelSize.Height / 2.0, RelativeUnit.Absolute)
        };
    }

    /// <summary>
    /// The tile for a tool: Codex's painting if it has arrived, otherwise a
    /// medallion in one of the palette's four metals with the glyph on it.
    /// </summary>
    public static Control Tile(string tileKey, string glyph, double size)
    {
        var painting = Load($"tile-{tileKey}.png");
        if (painting != null)
        {
            return new Image { Source = painting, Width = size, Height = size, Stretch = Stretch.Uniform };
        }

        var (from, to) = Metal(tileKey);

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.24),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
            },
            BoxShadow = BoxShadows.Parse("0 1 2 0 #33000000, inset 0 1 0 0 #40FFFFFF"),
            Child = new PathIcon
            {
                Data = Glyphs.Get(glyph),
                Width = size * 0.5,
                Height = size * 0.5,
                Foreground = new SolidColorBrush(Color.Parse("#FFF8EC")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    /// <summary>
    /// Bronze, verdigris, lapis or gold, chosen from the key so a tool keeps
    /// its colour from one launch to the next and neighbours tend to differ.
    /// </summary>
    private static (Color From, Color To) Metal(string key)
    {
        var hash = 0;
        foreach (var c in key) hash = unchecked(hash * 31 + c);

        return (Math.Abs(hash) % 4) switch
        {
            0 => (Color.Parse("#C07A3A"), Color.Parse("#7E4A1E")),   // bronze
            1 => (Color.Parse("#5C9E91"), Color.Parse("#2E5F56")),   // verdigris
            2 => (Color.Parse("#4E6FA8"), Color.Parse("#22375E")),   // lapis
            _ => (Color.Parse("#D2AE62"), Color.Parse("#8F6E2C"))    // gold
        };
    }
}
