using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// The arcade's painted icons - the Windows game's own, decoded from its .ico
/// files into this folder's Assets (see Arcade.props).
///
/// A missing or damaged picture costs its picture, not the game: the buttons
/// carry their words as well, so a null here leaves a button that reads
/// rather than a window that will not open. The Windows loader throws on a
/// missing resource because there the icons are compiled into the very
/// assembly that asks for them; here they are files that can be left behind.
/// </summary>
internal static class BronzeIcons
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? Bestiary() => Get("Bestiary");
    public static Bitmap? DivineGift() => Get("DivineGift");
    public static Bitmap? Laurels() => Get("Laurels");

    /// <summary>The game's emblem at full size, for its window.</summary>
    public static Bitmap? Emblem() => Load("BronzeAndThunder-256.png");

    /// <summary>One of the button pictures: Journal, NewAdventure, BeginAdventure, SoundOn, SoundOff, OracleHint, CheckPassage, RevealCitation, HighlightPassage.</summary>
    public static Bitmap? Get(string name) => Load($"Bronze{name}.png");

    /// <summary>
    /// An Image of <paramref name="bitmap"/> at <paramref name="size"/> points,
    /// resampled smoothly: the pictures are 64 pixels and drawn at 20 to 32
    /// points, and the default sampling would leave their gold rims ragged.
    /// </summary>
    public static Image Picture(Bitmap? bitmap, double size)
    {
        var image = new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        return image;
    }

    private static Bitmap? Load(string file)
    {
        if (Cache.TryGetValue(file, out var cached)) return cached;

        Bitmap? bitmap = null;
        var uri = new Uri($"avares://ClassicaCodex.Mac/Features/Arcade/Assets/{file}");

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
            // A damaged file is the same as a missing one: the words remain.
        }

        Cache[file] = bitmap;
        return bitmap;
    }
}
