namespace ClassicaCodex.Core;

/// <summary>
/// Approximate modern coordinates for well-known ancient places, used only
/// by the places map. Like AuthorEraData, this is a separate hand-curated
/// reference table, fuzzy-matched against whatever you named your place tags
/// - it's not derived from the corpus (Perseus's texts don't carry
/// geocoding). Coverage is necessarily partial; some ancient site locations
/// are themselves disputed (Zama, for instance) and given only a rough best
/// guess here.
///
/// Extended alongside the Renaissance and First1KGreek corpora: Byzantine
/// cities relevant to the newly-dated authors on the Timeline (Nicaea,
/// Nicomedia, Trebizond), and places named in Shakespeare, Holinshed, and
/// Hakluyt (Venice, Verona, Elsinore, Edinburgh, Dublin, Moscow, and a
/// couple of Hakluyt's own trade-route stops - Aleppo, Astrakhan - that
/// happened to already fall inside the map's existing range).
///
/// Deliberately not attempted: Hakluyt's most distant destinations - India,
/// the Americas, the Arctic passages - which would need a far larger
/// eastward and westward stretch than a few new points justify, and would
/// shrink the classical Mediterranean cluster that's the map's main reason
/// for existing. MapCanvas's viewport grew only as far north as this batch
/// actually needs (see its own MaxLat comment) - not into genuinely new
/// territory the hand-drawn fallback coastline was never meant to cover.
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

        // Byzantine cities, added alongside First1KGreek's newly-dated
        // authors - Nicaea and Nicomedia for Eustratius and Michael of
        // Ephesus's circle, Trebizond for the wider Byzantine world.
        ("Nicaea", 40.4300, 29.7200),
        ("Nicomedia", 40.7700, 29.9200),
        ("Trebizond", 41.0000, 39.7200),

        // Renaissance / early modern, added alongside canonical-engLit.
        // Country-name aliases (Scotland, Ireland, Denmark, Muscovy) share
        // a coordinate with their representative city, the same pattern
        // already used above for Byzantium/Constantinople - someone tagging
        // the play's setting is at least as likely to write the country as
        // the city.
        ("Venice", 45.4408, 12.3155),
        ("Verona", 45.4384, 10.9916),
        ("Prague", 50.0755, 14.4378),
        ("Bohemia", 50.0755, 14.4378),
        ("Elsinore", 56.0360, 12.6147),
        ("Denmark", 56.0360, 12.6147),
        ("Edinburgh", 55.9533, -3.1883),
        ("Scotland", 55.9533, -3.1883),
        ("Dublin", 53.3498, -6.2603),
        ("Ireland", 53.3498, -6.2603),
        ("Navarre", 42.8169, -1.6432),
        ("Moscow", 55.7558, 37.6173),
        ("Muscovy", 55.7558, 37.6173),

        // Two of Hakluyt's own trade-route waypoints - Aleppo also turns up
        // in Shakespeare (the witches' sailor in Macbeth 1.3) - that needed
        // no expansion of the map's range at all; both already sit inside
        // the existing box.
        ("Aleppo", 36.2021, 37.1343),
        ("Astrakhan", 46.3497, 48.0408),
		
		// ---- Herodotus, from the Getty TGN records Perseus embeds in the text.
        // Harvested from tlg0016.tlg001.perseus-eng2: 424 distinct places, 344 with
        // coordinates, of which 49 were already covered here. These are the 100 new
        // ones mentioned five times or more that fall inside MapCanvas's viewport.
        // The source writes them [lon,lat]; they are (lat, lon) below - verified
        // against the 38 places both sources name, mean error 0.70 degrees.
        ("Hellas", 39.0000, 22.0000),         // 210x, nation
        ("Libya", 25.0000, 17.0000),          // 100x, nation
        ("Nile", 30.1660, 31.1000),           // 72x, river
        ("Persia", 32.0000, 53.0000),         // 72x, nation
        ("Samos", 37.7500, 26.8000),          // 71x, island
        ("Attica", 38.8300, 23.5000),         // 57x, department
        ("Peloponnese", 37.5000, 22.0000),    // 54x, region
        ("Thessaly", 39.5000, 22.2500),       // 53x, region
        ("Aegina", 37.7500, 23.4330),         // 48x, inhabited place
        ("Lacedaemon", 37.8300, 22.4160),     // 45x, inhabited place
        ("Artemisium", 39.0083, 23.2417),     // 39x, Perseus
        ("Sicily", 37.5000, 14.0000),         // 29x, region
        ("Pontus", 42.0000, 38.0000),         // 24x, sea
        ("Syria", 35.0000, 38.0000),          // 23x, nation
        ("Arabia", 25.0000, 45.0000),         // 22x, region (general
        ("Chios", 38.3660, 26.0000),          // 22x, island
        ("Euboea", 38.5660, 23.8330),         // 22x, island
        ("Tegea", 37.5000, 22.4000),          // 22x, Perseus
        ("Abydos", 40.2000, 26.4160),         // 19x, deserted settlement
        ("Lydia", 38.6830, 27.5160),          // 18x, region (general
        ("Naxos", 37.8160, 15.2830),          // 18x, deserted settlement
        ("Croton", 39.0833, 17.1333),         // 17x, Perseus
        ("Eleusis", 38.0417, 23.5583),        // 16x, Perseus
        ("Lemnos", 39.9160, 25.2500),         // 16x, island
        ("Mykale", 38.1000, 26.8667),         // 16x, Perseus
        ("Sicyon", 37.9833, 22.7250),         // 16x, Perseus
        ("Thera", 36.4000, 25.4330),          // 16x, island
        ("Eretria", 38.3917, 23.8083),        // 15x, Perseus
        ("Athos", 40.1660, 24.3160),          // 14x, inhabited place
        ("Bubastis", 30.5660, 31.5160),       // 14x, deserted settlement
        ("Dodona", 39.5500, 20.8000),         // 14x, Perseus
        ("Italy", 42.8330, 12.8330),          // 14x, nation
        ("Arcadia", 37.5830, 22.2500),        // 13x, department
        ("Buto", 31.2000, 30.7330),           // 13x, deserted settlement
        ("Cilicia", 36.6660, 34.3330),        // 13x, region (general
        ("Elis", 37.8833, 21.4000),           // 13x, Perseus
        ("Maeander", 37.4660, 27.1830),       // 13x, river
        ("Abdera", 40.9833, 24.9667),         // 12x, Perseus
        ("Lesbos", 39.1660, 26.3330),         // 12x, island
        ("Phocaea", 38.6660, 26.7500),        // 12x, inhabited place
        ("Trachis", 38.8000, 22.5500),        // 12x, Perseus
        ("Apollonia", 38.0167, 14.5833),      // 11x, Perseus
        ("Barce", 32.5000, 20.8330),          // 11x, inhabited place
        ("Caria", 37.5000, 28.0000),          // 11x, region (general
        ("Cyme", 38.6333, 24.1167),           // 11x, Perseus
        ("Heliopolis", 30.1000, 31.3330),     // 11x, deserted settlement
        ("Pallene", 38.0500, 23.8833),        // 11x, Perseus
        ("Tanaïs", 47.1000, 39.4330),         // 11x, inhabited place
        ("Zancle", 38.1833, 15.5667),         // 11x, Perseus
        ("Euphrates", 31.8300, 47.5000),      // 10x, river
        ("Mytilene", 39.1000, 26.5500),       // 10x, Perseus
        ("Thasos", 40.7830, 24.7160),         // 10x, deserted settlement
        ("Therma", 40.6330, 22.9330),         // 10x, inhabited place
        ("Andros", 37.8160, 24.9000),         // 9x, inhabited place
        ("Aphetae", 39.1167, 23.1167),        // 9x, Perseus
        ("Chalcis", 38.4667, 23.6083),        // 9x, Perseus
        ("Gela", 37.0667, 14.2500),           // 9x, Perseus
        ("Magnesia", 39.2500, 22.7500),       // 9x, department
        ("Megara", 38.0000, 23.3500),         // 9x, Perseus
        ("Branchidae", 37.3500, 27.2330),     // 8x, historic site
        ("Corcyra", 39.6330, 19.9160),        // 8x, inhabited place
        ("Orchomenus", 37.7160, 22.3000),     // 8x, inhabited place
        ("Sestus", 40.2833, 26.4000),         // 8x, Perseus
        ("Sybaris", 39.7500, 16.4833),        // 8x, Perseus
        ("Tanagra", 38.3083, 23.6000),        // 8x, Perseus
        ("Troezen", 37.5000, 23.3750),        // 8x, Perseus
        ("Arabian Gulf", 25.5830, 53.8300),   // 7x, gulf
        ("Atarneus", 39.0500, 26.9500),       // 7x, Perseus
        ("Cyzicus", 40.4167, 27.9000),        // 7x, Perseus
        ("Euxine", 42.0000, 38.0000),         // 7x, sea
        ("Euxine sea", 42.0000, 38.0000),     // 7x, sea
        ("Khemmis", 26.5660, 31.7330),        // 7x, inhabited place
        ("Melissa", 39.3000, 17.0333),        // 7x, Perseus
        ("Naucratis", 30.9000, 30.5830),      // 7x, inhabited place
        ("Palestine", 31.9160, 35.3330),      // 7x, region (general
        ("Proconnesus", 40.6330, 27.6160),    // 7x, island
        ("Achaea", 38.2500, 21.7500),         // 6x, department
        ("Cappadocia", 38.5000, 36.0000),     // 6x, region (general
        ("Caucasus", 42.0000, 46.8330),       // 6x, mountain range
        ("Eion", 40.7333, 23.8833),           // 6x, Perseus
        ("Laconia", 37.0000, 22.5830),        // 6x, department
        ("Phasis", 42.1830, 41.6830),         // 6x, inhabited place
        ("Priene", 37.6333, 27.2833),         // 6x, Perseus
        ("Rhegium", 38.1000, 15.6500),        // 6x, inhabited place
        ("Tiryns", 37.6000, 22.8167),         // 6x, Perseus
        ("lake Moeris", 29.4660, 30.6660),    // 6x, salt lake
        ("Abae", 38.5917, 22.9583),           // 5x, Perseus
        ("Aegean", 38.5000, 25.0000),         // 5x, sea
        ("Axius", 40.5830, 22.8330),          // 5x, river
        ("Clazomenae", 38.3167, 26.7833),     // 5x, Perseus
        ("Colophon", 38.1167, 27.1333),       // 5x, Perseus
        ("Cos", 36.8917, 27.3000),            // 5x, Perseus
        ("Hermione", 37.3833, 23.2583),       // 5x, Perseus
        ("Ilion", 39.9500, 26.2500),          // 5x, deserted settlement
        ("Lampsacus", 40.3660, 26.7000),      // 5x, inhabited place
        ("Lindus", 36.0833, 28.1083),         // 5x, Perseus
        ("Olynthus", 40.3000, 23.3667),       // 5x, Perseus
        ("Panionion", 37.6833, 27.1167),      // 5x, Perseus
        ("Paros", 37.1000, 25.2000),          // 5x, island
        ("Tigris", 31.0000, 47.4160),         // 5x, river
    };

    /// <summary>
    /// Best-effort fuzzy match against whatever you named a place tag.
    /// Checks for an exact match across every entry first, before any
    /// substring check runs - the same fix AuthorEraData needed once its own
    /// table grew dense enough for real collisions (three people all named
    /// some form of Heraclitus). Nothing here collides today as far as I
    /// checked, but a straight substring-only match doesn't get safer as
    /// more entries are added - it gets riskier - so this closes the same
    /// class of bug before it has a chance to show up.
    /// </summary>
    public static (double Lat, double Lon)? Lookup(string placeName)
    {
        var normalized = placeName.Trim();

        foreach (var (key, lat, lon) in Entries)
        {
            if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase))
                return (lat, lon);
        }

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
