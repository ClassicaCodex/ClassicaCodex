namespace ClassicaCodex.UI;

public enum NodeShape
{
    Circle,
    Square,
    Triangle,
    Diamond,
    Hexagon,
    Star
}

/// <summary>
/// Remembers which shape the user has assigned to each tag category, so the
/// myth network can distinguish gods from heroes from places at a glance
/// rather than by reading every label.
///
/// Categories are free text the user types when tagging, so this can't ship
/// with a fixed list - assignments are made in the Shapes dialog against
/// whatever categories actually exist in the data, and saved alongside the
/// theme preference.
/// </summary>
public static class CategoryShapes
{
    private static readonly Dictionary<string, NodeShape> Assignments =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _loaded;

    /// <summary>Shape for a category, defaulting to Circle for anything unassigned or uncategorized.</summary>
    public static NodeShape For(string? category)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(category)) return NodeShape.Circle;
        return Assignments.TryGetValue(category.Trim(), out var shape) ? shape : NodeShape.Circle;
    }

    public static void Set(string category, NodeShape shape)
    {
        EnsureLoaded();

        if (string.IsNullOrWhiteSpace(category)) return;
        Assignments[category.Trim()] = shape;
        Save();
    }

    public static IReadOnlyDictionary<string, NodeShape> All
    {
        get
        {
            EnsureLoaded();
            return Assignments;
        }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "category-shapes.txt");

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(SettingsPath)) return;

            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                // "category=Shape" - split on the last '=' so a category
                // containing one of its own isn't mangled.
                var separator = line.LastIndexOf('=');
                if (separator <= 0) continue;

                var category = line[..separator].Trim();
                var shapeName = line[(separator + 1)..].Trim();

                if (category.Length > 0 && Enum.TryParse<NodeShape>(shapeName, ignoreCase: true, out var shape))
                {
                    Assignments[category] = shape;
                }
            }
        }
        catch
        {
            // A shape preference isn't worth failing over - everything just
            // falls back to circles.
        }
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllLines(SettingsPath, Assignments.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch
        {
            // Same reasoning as loading - a failed save just means the
            // assignment doesn't survive restart.
        }
    }
}
