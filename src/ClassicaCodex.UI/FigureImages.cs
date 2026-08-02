using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

/// <summary>
/// Portraits for tags that name a mythological figure, place or object.
///
/// Matched on the tag's own text, normalized the same way search is - so a
/// tag written "Zeus", "zeus" or with different accentuation all find
/// Zeus.png. Anything without a portrait simply renders as it did before,
/// which will be most tags in most libraries.
///
/// Two folders are searched. The set shipped with the app lives beside the
/// other icons; a "Figures" folder next to the database is checked first, so
/// anyone can add their own or replace one they don't like without touching
/// the installation.
/// </summary>
public static class FigureImages
{
    private static readonly Dictionary<string, Image?> Cache = new();
    private static Dictionary<string, string>? _paths;

    private static string ShippedDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Icons", "Figures");

    /// <summary>
    /// Beside the database rather than beside the executable, so a portrait
    /// set travels with the library it describes and survives reinstalling.
    /// </summary>
    private static string? UserDirectory
    {
        get
        {
            var database = DbConnectionFactoryPath();
            if (string.IsNullOrWhiteSpace(database)) return null;

            var folder = Path.GetDirectoryName(database);
            return folder == null ? null : Path.Combine(folder, "Figures");
        }
    }

    private static string? DbConnectionFactoryPath()
    {
        try
        {
            return ClassicaCodex.Data.DbConnectionFactory.DatabasePath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the name-to-file map once, user folder last so it wins on a
    /// clash - that is what makes a replacement a replacement.
    /// </summary>
    private static Dictionary<string, string> Paths()
    {
        if (_paths != null) return _paths;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var directory in new[] { ShippedDirectory, UserDirectory })
        {
            if (directory == null || !Directory.Exists(directory)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
            {
                var key = WordNormalizer.Normalize(Path.GetFileNameWithoutExtension(file));
                if (key.Length > 0) map[key] = file;
            }
        }

        _paths = map;
        return map;
    }

    /// <summary>The portrait for this tag, or null when there isn't one.</summary>
    public static Image? For(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return null;

        var key = WordNormalizer.Normalize(tagName);
        if (key.Length == 0) return null;

        if (Cache.TryGetValue(key, out var cached)) return cached;

        Image? image = null;
        if (Paths().TryGetValue(key, out var path))
        {
            try
            {
                // Copied out of the stream so the file isn't held open - a
                // portrait someone drops in should be replaceable without
                // closing the app.
                using var stream = File.OpenRead(path);
                using var loaded = Image.FromStream(stream);
                image = new Bitmap(loaded);
            }
            catch (Exception)
            {
                image = null;
            }
        }

        Cache[key] = image;
        return image;
    }

    /// <summary>Forgets the map and the loaded images, for when the database moves.</summary>
    public static void Reset()
    {
        foreach (var image in Cache.Values) image?.Dispose();
        Cache.Clear();
        _paths = null;
    }
}
