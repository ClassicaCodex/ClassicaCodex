namespace ClassicaCodex.Core;

/// <summary>
/// Approximate modern coordinates for well-known ancient places, used only
/// by the places map. Like AuthorEraData, this is a separate hand-curated
/// reference table, fuzzy-matched against whatever you named your place tags
/// - it's not derived from the corpus (Perseus's texts don't carry
/// geocoding). Coverage is necessarily partial; some ancient site locations
/// are themselves disputed (Zama, for instance) and given only a rough best
/// guess here.
/// </summary>
public static class PlaceData
{
    private static readonly (string Key, double Lat, double Lon)[] Entries =
    {
        ("Athens", 37.9838, 23.7275),
        ("Sparta", 37.0733, 22.4235),
        ("Corinth", 37.9061, 22.8781),
        ("Thebes, Greece", 38.3212, 23.3195),
        ("Boeotian Thebes", 38.3212, 23.3195),
        ("Delphi", 38.4824, 22.5010),
        ("Olympia", 37.6383, 21.6300),
        ("Argos", 37.6333, 22.7333),
        ("Mycenae", 37.7311, 22.7558),
        ("Troy", 39.9576, 26.2386),
        ("Ilium", 39.9576, 26.2386),
        ("Ephesus", 37.9410, 27.3417),
        ("Miletus", 37.5297, 27.2775),
        ("Halicarnassus", 37.0344, 27.4305),
        ("Pergamon", 39.1325, 27.1848),
        ("Byzantium", 41.0082, 28.9784),
        ("Constantinople", 41.0082, 28.9784),
        ("Rhodes", 36.4341, 28.2176),
        ("Knossos", 35.2980, 25.1633),
        ("Crete", 35.2401, 24.8093),
        ("Salamis, Cyprus", 35.1856, 33.9036),
        ("Cyprus", 35.1264, 33.4299),
        ("Delos", 37.3963, 25.2697),
        ("Ithaca", 38.3785, 20.7101),
        ("Thermopylae", 38.7980, 22.5360),
        ("Marathon", 38.1621, 23.9631),
        ("Salamis", 37.9647, 23.4914),
        ("Plataea", 38.2167, 23.2667),
        ("Chaeronea", 38.4989, 22.8422),
        ("Syracuse", 37.0755, 15.2866),
        ("Messana", 38.1938, 15.5540),
        ("Tarentum", 40.4744, 17.2300),
        ("Neapolis", 40.8518, 14.2681),
        ("Naples", 40.8518, 14.2681),
        ("Pompeii", 40.7461, 14.4989),
        ("Herculaneum", 40.8058, 14.3486),
        ("Rome", 41.9028, 12.4964),
        ("Ostia", 41.7554, 12.2926),
        ("Veii", 42.0167, 12.4000),
        ("Capua", 41.0764, 14.2136),
        ("Cumae", 40.8483, 14.0564),
        ("Ravenna", 44.4184, 12.2035),
        ("Mediolanum", 45.4642, 9.1900),
        ("Milan", 45.4642, 9.1900),
        ("Massalia", 43.2965, 5.3698),
        ("Marseille", 43.2965, 5.3698),
        ("Gades", 36.5271, -6.2886),
        ("Numantia", 41.8103, -2.4283),
        ("Carthage", 36.8528, 10.3233),
        ("Utica", 37.0544, 10.0632),
        ("Cyrene", 32.8250, 21.8583),
        ("Alexandria", 31.2001, 29.9187),
        ("Memphis, Egypt", 29.8450, 31.2500),
        ("Egyptian Thebes", 25.7188, 32.6396),
        ("Jerusalem", 31.7683, 35.2137),
        ("Antioch", 36.2021, 36.1603),
        ("Damascus", 33.5138, 36.2765),
        ("Babylon", 32.5364, 44.4200),
        ("Persepolis", 29.9354, 52.8916),
        ("Susa", 32.1942, 48.2606),
        ("Ecbatana", 34.7983, 48.5148),
        ("Sardis", 38.4877, 28.0402),
        ("Smyrna", 38.4192, 27.1287),
        ("Pella", 40.7622, 22.5192),
        ("Thessalonica", 40.6401, 22.9444),
        ("Pharsalus", 39.2975, 22.3856),
        ("Actium", 38.9500, 20.7333),
        ("Philippi", 41.0122, 24.2867),
        ("Zama", 36.1667, 9.1167),
        ("Londinium", 51.5074, -0.1278),
        ("London", 51.5074, -0.1278),
        ("Lutetia", 48.8566, 2.3522),
        ("Paris", 48.8566, 2.3522),
        ("Colonia Agrippina", 50.9375, 6.9603),
        ("Cologne", 50.9375, 6.9603),
        ("Vindobona", 48.2082, 16.3738),
        ("Vienna", 48.2082, 16.3738),
        ("Byblos", 34.1230, 35.6481),
        ("Tyre", 33.2704, 35.2038),
        ("Sidon", 33.5571, 35.3729),
        ("Nineveh", 36.3600, 43.1500),
        ("Ur", 30.9626, 46.1027),
    };

    /// <summary>Best-effort fuzzy match against whatever you named a place tag.</summary>
    public static (double Lat, double Lon)? Lookup(string placeName)
    {
        var normalized = placeName.Trim();

        foreach (var (key, lat, lon) in Entries)
        {
            if (normalized.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return (lat, lon);
            }
        }

        return null;
    }

    /// <summary>
    /// Every known place, one entry per unique coordinate. Several entries
    /// above share a coordinate on purpose - Troy/Ilium, Thebes/Boeotian
    /// Thebes - so a fuzzy tag match catches either name. Enumerating them
    /// without deduplicating would draw two stacked pins on the same spot;
    /// this keeps whichever name was listed first for each location.
    /// </summary>
    public static IReadOnlyList<(string Name, double Lat, double Lon)> All()
    {
        var seenCoordinates = new HashSet<(double, double)>();
        var result = new List<(string, double, double)>();

        foreach (var (key, lat, lon) in Entries)
        {
            if (seenCoordinates.Add((lat, lon)))
            {
                result.Add((key, lat, lon));
            }
        }

        return result;
    }
}
