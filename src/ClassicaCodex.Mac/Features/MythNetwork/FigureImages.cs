using Avalonia.Media.Imaging;
using ClassicaCodex.Core;
using ClassicaCodex.Data;

namespace ClassicaCodex.Mac.Features.MythNetwork;

/// <summary>
/// Portraits for tags that name a mythological figure, place or object - the
/// Windows FigureImages, drawing the same PNGs.
///
/// Matched on the tag's own text, normalised the way search is, so a tag
/// written "Zeus", "zeus" or with different accentuation all find Zeus.png.
/// Anything without a portrait simply draws without one, which will be most
/// tags in most libraries.
///
/// Two folders are searched. The set shipped with the app is copied beside
/// it, under Figures; a "Figures" folder next to the database is read too and
/// wins, so anyone can add their own or replace one they don't like without
/// touching the application.
/// </summary>
internal static class FigureImages
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static Dictionary<string, string>? _paths;
    private static string? _pathsBuiltFor;

    private static string ShippedDirectory => Path.Combine(AppContext.BaseDirectory, "Figures");

    /// <summary>
    /// Beside the database rather than beside the application, so a portrait
    /// set travels with the library it describes and survives reinstalling.
    /// </summary>
    private static string? UserDirectory
    {
        get
        {
            var database = DatabasePath();
            if (string.IsNullOrWhiteSpace(database)) return null;

            var folder = Path.GetDirectoryName(database);
            return folder == null ? null : Path.Combine(folder, "Figures");
        }
    }

    private static string? DatabasePath()
    {
        try
        {
            return DbConnectionFactory.DatabasePath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the name-to-file map, user folder last so it wins on a clash -
    /// that is what makes a replacement a replacement.
    ///
    /// Rebuilt when the library changes. The Windows app opens one library
    /// for its whole run and so builds this once; the Mac's File menu can
    /// open another, whose own Figures folder is then the one that counts.
    /// </summary>
    private static Dictionary<string, string> Paths()
    {
        var database = DatabasePath();
        if (_paths != null && string.Equals(_pathsBuiltFor, database, StringComparison.Ordinal)) return _paths;

        Reset();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var directory in new[] { ShippedDirectory, UserDirectory })
        {
            if (directory == null || !Directory.Exists(directory)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.png"))
                {
                    var key = WordNormalizer.Normalize(Path.GetFileNameWithoutExtension(file));
                    if (key.Length > 0) map[key] = file;
                }
            }
            catch (Exception)
            {
                // An unreadable folder is a folder with no portraits in it.
            }
        }

        _paths = map;
        _pathsBuiltFor = database;
        return map;
    }

    /// <summary>The portrait for this tag, or null when there isn't one.</summary>
    public static Bitmap? For(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return null;

        var key = WordNormalizer.Normalize(tagName);
        if (key.Length == 0) return null;

        var paths = Paths();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? image = null;
        if (paths.TryGetValue(key, out var path))
        {
            try
            {
                // Read out of the file first, so it isn't held open - a
                // portrait someone drops in should be replaceable without
                // quitting the app.
                using var stream = new MemoryStream(File.ReadAllBytes(path));
                image = new Bitmap(stream);
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
        _pathsBuiltFor = null;
    }
}
