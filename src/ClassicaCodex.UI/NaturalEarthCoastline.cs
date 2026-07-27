using System.Text.Json;

namespace ClassicaCodex.UI;

/// <summary>
/// Loads real coastline geometry from Natural Earth's ne_110m_land.geojson
/// (public domain), downloaded by the "World Map Data" setup step. When the
/// file is present and parses, the places map draws these instead of the
/// hand-approximated shapes in AncientWorldCoastline; when it's absent or
/// unreadable in any way, the map silently falls back to those - the
/// schematic is a degraded mode, never an error.
///
/// Parsed once per app run and cached, including the failure case - a file
/// that failed to parse once isn't going to parse differently on the next
/// repaint, and the map repaints constantly during pan and zoom.
/// </summary>
public static class NaturalEarthCoastline
{
    /// <summary>
    /// The one canonical location the map reads from - deliberately fixed
    /// (not user-configurable) so MapCanvas and the setup step can never
    /// disagree about where the file lives.
    /// </summary>
    public static string CanonicalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ClassicaCodexData", "map", "ne_110m_land.geojson");

    // Slightly wider than MapCanvas's own bounds, so shapes whose edges
    // cross the map border still draw right up to it instead of vanishing
    // the moment their last in-bounds vertex scrolls past the edge.
    private const double FilterMinLon = -20, FilterMaxLon = 64;
    private const double FilterMinLat = 14, FilterMaxLat = 62;

    private static List<List<(double Lat, double Lon)[]>>? _cache;
    private static bool _loadAttempted;

    /// <summary>
    /// Forget the cached result (including a cached "file wasn't there").
    /// Called by the setup step right after a successful download -
    /// without this, opening the map before downloading would cache the
    /// absence, and the freshly downloaded file would sit unread until
    /// the app restarted.
    /// </summary>
    public static void InvalidateCache()
    {
        _cache = null;
        _loadAttempted = false;
    }

    /// <summary>
    /// The relevant landmasses, as feature -> rings (first ring is the
    /// outer boundary, any further rings are holes), or null if the file
    /// isn't available. Never throws.
    /// </summary>
    public static List<List<(double Lat, double Lon)[]>>? Load()
    {
        if (_loadAttempted) return _cache;
        _loadAttempted = true;

        try
        {
            if (!File.Exists(CanonicalPath)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(CanonicalPath));
            var features = doc.RootElement.GetProperty("features");

            var result = new List<List<(double Lat, double Lon)[]>>();
            foreach (var feature in features.EnumerateArray())
            {
                var geometry = feature.GetProperty("geometry");
                var type = geometry.GetProperty("type").GetString();
                var coordinates = geometry.GetProperty("coordinates");

                if (type == "Polygon")
                {
                    AddPolygonIfRelevant(result, coordinates);
                }
                else if (type == "MultiPolygon")
                {
                    foreach (var polygon in coordinates.EnumerateArray())
                    {
                        AddPolygonIfRelevant(result, polygon);
                    }
                }
            }

            _cache = result.Count > 0 ? result : null;
        }
        catch
        {
            // Any parse problem at all - truncated download, hand-edited
            // file, wrong file entirely - just means the schematic
            // fallback. The map opening must never be what crashes.
            _cache = null;
        }

        return _cache;
    }

    private static void AddPolygonIfRelevant(
        List<List<(double Lat, double Lon)[]>> result, JsonElement polygonRings)
    {
        var rings = new List<(double Lat, double Lon)[]>();
        var anyPointInBounds = false;

        foreach (var ring in polygonRings.EnumerateArray())
        {
            var points = new List<(double Lat, double Lon)>();
            foreach (var pair in ring.EnumerateArray())
            {
                var lon = pair[0].GetDouble();
                var lat = pair[1].GetDouble();
                points.Add((lat, lon));

                if (lon >= FilterMinLon && lon <= FilterMaxLon &&
                    lat >= FilterMinLat && lat <= FilterMaxLat)
                {
                    anyPointInBounds = true;
                }
            }
            if (points.Count >= 3) rings.Add(points.ToArray());
        }

        // Keep the whole polygon (holes included) if ANY vertex falls in
        // the widened bounds - a polygon has to be kept or dropped as a
        // unit, since dropping just its hole rings would fill in seas.
        if (anyPointInBounds && rings.Count > 0) result.Add(rings);
    }
}
