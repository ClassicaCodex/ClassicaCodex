namespace ClassicaCodex.Core;

/// <summary>
/// What sort of place an entry is - the reason you would have looked it up,
/// rather than an ontology.
///
/// A hundred pins on a Mediterranean-sized map is more than can be read at
/// once, so these exist to be switched off in groups. The categories are
/// therefore chosen to match how a reader thinks about a place while reading,
/// not how a gazetteer would classify it.
///
/// Several places genuinely belong to two: Salamis is an island and a battle,
/// Rhodes is an island and a city on it, Delphi is a sanctuary and a polis. In
/// each case the entry takes the sense a classical text is most often pointing
/// at - Salamis and Rhodes are alphabetically adjacent to that decision and it
/// is still a decision. Where the choice is arguable, the pin is still there;
/// only its colour and which toggle hides it are affected.
/// </summary>
public enum PlaceKind
{
    /// <summary>A settlement. The default, and most of the catalog.</summary>
    City,

    /// <summary>Delphi, Olympia, Delos - named for the sanctuary rather than the town around it.</summary>
    Sanctuary,

    /// <summary>Somewhere a battle is named after: Thermopylae, Actium, Zama.</summary>
    Battlefield,

    /// <summary>An island, country or territory rather than a point: Crete, Bohemia, Muscovy.</summary>
    Region,

    /// <summary>
    /// A river, sea, lake or gulf: the Nile, the Euxine, lake Moeris.
    ///
    /// Its own kind rather than a Region because a river reads nothing like a
    /// territory while reading, and because the Herodotus material brought
    /// eleven of them at once - enough that they would have crowded the map
    /// under any other label.
    ///
    /// The pin marks a single representative point, which for a river is
    /// necessarily arbitrary: the Nile's is its delta, the Euphrates' its lower
    /// course. A pin is the wrong shape for a thing hundreds of miles long, and
    /// these are here to be found rather than to be traced.
    /// </summary>
    Water
}

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
    private static readonly (string Key, double Lat, double Lon, PlaceKind Kind)[] Entries =
    {
        ("Athens", 37.9838, 23.7275, PlaceKind.City),
        ("Sparta", 37.0733, 22.4235, PlaceKind.City),
        ("Corinth", 37.9061, 22.8781, PlaceKind.City),
        ("Thebes, Greece", 38.3212, 23.3195, PlaceKind.City),
        ("Boeotian Thebes", 38.3212, 23.3195, PlaceKind.City),
        ("Delphi", 38.4824, 22.5010, PlaceKind.Sanctuary),
        ("Olympia", 37.6383, 21.6300, PlaceKind.Sanctuary),
        ("Argos", 37.6333, 22.7333, PlaceKind.City),
        ("Mycenae", 37.7311, 22.7558, PlaceKind.City),
        ("Troy", 39.9576, 26.2386, PlaceKind.City),
        ("Ilium", 39.9576, 26.2386, PlaceKind.City),
        ("Ephesus", 37.9410, 27.3417, PlaceKind.City),
        ("Miletus", 37.5297, 27.2775, PlaceKind.City),
        ("Halicarnassus", 37.0344, 27.4305, PlaceKind.City),
        ("Pergamon", 39.1325, 27.1848, PlaceKind.City),
        ("Byzantium", 41.0082, 28.9784, PlaceKind.City),
        ("Constantinople", 41.0082, 28.9784, PlaceKind.City),
        ("Rhodes", 36.4341, 28.2176, PlaceKind.Region),
        ("Knossos", 35.2980, 25.1633, PlaceKind.City),
        ("Crete", 35.2401, 24.8093, PlaceKind.Region),
        ("Salamis, Cyprus", 35.1856, 33.9036, PlaceKind.City),
        ("Cyprus", 35.1264, 33.4299, PlaceKind.Region),
        ("Delos", 37.3963, 25.2697, PlaceKind.Sanctuary),
        ("Ithaca", 38.3785, 20.7101, PlaceKind.Region),
        ("Thermopylae", 38.7980, 22.5360, PlaceKind.Battlefield),
        ("Marathon", 38.1621, 23.9631, PlaceKind.Battlefield),
        ("Salamis", 37.9647, 23.4914, PlaceKind.Battlefield),
        ("Plataea", 38.2167, 23.2667, PlaceKind.Battlefield),
        ("Chaeronea", 38.4989, 22.8422, PlaceKind.Battlefield),
        ("Syracuse", 37.0755, 15.2866, PlaceKind.City),
        ("Messana", 38.1938, 15.5540, PlaceKind.City),
        ("Tarentum", 40.4744, 17.2300, PlaceKind.City),
        ("Neapolis", 40.8518, 14.2681, PlaceKind.City),
        ("Naples", 40.8518, 14.2681, PlaceKind.City),
        ("Pompeii", 40.7461, 14.4989, PlaceKind.City),
        ("Herculaneum", 40.8058, 14.3486, PlaceKind.City),
        ("Rome", 41.9028, 12.4964, PlaceKind.City),
        ("Ostia", 41.7554, 12.2926, PlaceKind.City),
        ("Veii", 42.0167, 12.4000, PlaceKind.City),
        ("Capua", 41.0764, 14.2136, PlaceKind.City),
        ("Cumae", 40.8483, 14.0564, PlaceKind.City),
        ("Ravenna", 44.4184, 12.2035, PlaceKind.City),
        ("Mediolanum", 45.4642, 9.1900, PlaceKind.City),
        ("Milan", 45.4642, 9.1900, PlaceKind.City),
        ("Massalia", 43.2965, 5.3698, PlaceKind.City),
        ("Marseille", 43.2965, 5.3698, PlaceKind.City),
        ("Gades", 36.5271, -6.2886, PlaceKind.City),
        ("Numantia", 41.8103, -2.4283, PlaceKind.Battlefield),
        ("Carthage", 36.8528, 10.3233, PlaceKind.City),
        ("Utica", 37.0544, 10.0632, PlaceKind.City),
        ("Cyrene", 32.8250, 21.8583, PlaceKind.City),
        ("Alexandria", 31.2001, 29.9187, PlaceKind.City),
        ("Memphis, Egypt", 29.8450, 31.2500, PlaceKind.City),
        ("Egyptian Thebes", 25.7188, 32.6396, PlaceKind.City),
        ("Jerusalem", 31.7683, 35.2137, PlaceKind.City),
        ("Antioch", 36.2021, 36.1603, PlaceKind.City),
        ("Damascus", 33.5138, 36.2765, PlaceKind.City),
        ("Babylon", 32.5364, 44.4200, PlaceKind.City),
        ("Persepolis", 29.9354, 52.8916, PlaceKind.City),
        ("Susa", 32.1942, 48.2606, PlaceKind.City),
        ("Ecbatana", 34.7983, 48.5148, PlaceKind.City),
        ("Sardis", 38.4877, 28.0402, PlaceKind.City),
        ("Smyrna", 38.4192, 27.1287, PlaceKind.City),
        ("Pella", 40.7622, 22.5192, PlaceKind.City),
        ("Thessalonica", 40.6401, 22.9444, PlaceKind.City),
        ("Pharsalus", 39.2975, 22.3856, PlaceKind.Battlefield),
        ("Actium", 38.9500, 20.7333, PlaceKind.Battlefield),
        ("Philippi", 41.0122, 24.2867, PlaceKind.Battlefield),
        ("Zama", 36.1667, 9.1167, PlaceKind.Battlefield),
        ("Londinium", 51.5074, -0.1278, PlaceKind.City),
        ("London", 51.5074, -0.1278, PlaceKind.City),
        ("Lutetia", 48.8566, 2.3522, PlaceKind.City),
        ("Paris", 48.8566, 2.3522, PlaceKind.City),
        ("Colonia Agrippina", 50.9375, 6.9603, PlaceKind.City),
        ("Cologne", 50.9375, 6.9603, PlaceKind.City),
        ("Vindobona", 48.2082, 16.3738, PlaceKind.City),
        ("Vienna", 48.2082, 16.3738, PlaceKind.City),
        ("Byblos", 34.1230, 35.6481, PlaceKind.City),
        ("Tyre", 33.2704, 35.2038, PlaceKind.City),
        ("Sidon", 33.5571, 35.3729, PlaceKind.City),
        ("Nineveh", 36.3600, 43.1500, PlaceKind.City),
        ("Ur", 30.9626, 46.1027, PlaceKind.City),
        ("Nicaea", 40.4300, 29.7200, PlaceKind.City),
        ("Nicomedia", 40.7700, 29.9200, PlaceKind.City),
        ("Trebizond", 41.0000, 39.7200, PlaceKind.City),
        ("Venice", 45.4408, 12.3155, PlaceKind.City),
        ("Verona", 45.4384, 10.9916, PlaceKind.City),
        ("Prague", 50.0755, 14.4378, PlaceKind.City),
        ("Bohemia", 50.0755, 14.4378, PlaceKind.Region),
        ("Elsinore", 56.0360, 12.6147, PlaceKind.City),
        ("Denmark", 56.0360, 12.6147, PlaceKind.Region),
        ("Edinburgh", 55.9533, -3.1883, PlaceKind.City),
        ("Scotland", 55.9533, -3.1883, PlaceKind.Region),
        ("Dublin", 53.3498, -6.2603, PlaceKind.City),
        ("Ireland", 53.3498, -6.2603, PlaceKind.Region),
        ("Navarre", 42.8169, -1.6432, PlaceKind.Region),
        ("Moscow", 55.7558, 37.6173, PlaceKind.City),
        ("Muscovy", 55.7558, 37.6173, PlaceKind.Region),
        ("Aleppo", 36.2021, 37.1343, PlaceKind.City),
        ("Astrakhan", 46.3497, 48.0408, PlaceKind.City),

        // ---- Herodotus, from the Getty TGN records Perseus embeds in the text.
        // Harvested from tlg0016.tlg001.perseus-eng2: 424 distinct places, 344 with
        // coordinates, of which 49 were already covered here. These are the 100 new
        // ones mentioned five times or more that fall inside MapCanvas's viewport.
        // The source writes them [lon,lat]; they are (lat, lon) below - verified
        // against the 38 places both sources name, mean error 0.70 degrees.
        //
        // Kinds come from the TGN type on each record rather than from judgement,
        // except for Artemisium and Mykale. TGN has those as ordinary Perseus
        // places; in Herodotus they are battles, which is what anyone reading him
        // would be looking them up for.
        //
        // Three coordinates below are nudged a kilometre or two onto an entry
        // already in the table - Ilion onto Troy, Zancle onto Messana, Therma
        // onto Thessalonica. They are the same site under an older or a Greek
        // name, and All() deduplicates on an exact coordinate, so without this
        // each would draw a second pin a pixel from the first. It is the same
        // convention Troy/Ilium and Byzantium/Constantinople already follow.
        //
        // Thermopylae and Trachis are 1.2 km apart and BOTH KEPT: the pass and
        // the town are different places that a reader of Book 7 needs to tell
        // apart, even though the pins touch.
        ("Hellas", 39.0000, 22.0000, PlaceKind.Region),
        ("Libya", 25.0000, 17.0000, PlaceKind.Region),
        ("Nile", 30.1660, 31.1000, PlaceKind.Water),
        ("Persia", 32.0000, 53.0000, PlaceKind.Region),
        ("Samos", 37.7500, 26.8000, PlaceKind.Region),
        ("Attica", 38.8300, 23.5000, PlaceKind.Region),
        ("Peloponnese", 37.5000, 22.0000, PlaceKind.Region),
        ("Thessaly", 39.5000, 22.2500, PlaceKind.Region),
        ("Aegina", 37.7500, 23.4330, PlaceKind.City),
        ("Lacedaemon", 37.8300, 22.4160, PlaceKind.City),
        ("Artemisium", 39.0083, 23.2417, PlaceKind.Battlefield),
        ("Sicily", 37.5000, 14.0000, PlaceKind.Region),
        ("Pontus", 42.0000, 38.0000, PlaceKind.Water),
        ("Syria", 35.0000, 38.0000, PlaceKind.Region),
        ("Arabia", 25.0000, 45.0000, PlaceKind.Region),
        ("Chios", 38.3660, 26.0000, PlaceKind.Region),
        ("Euboea", 38.5660, 23.8330, PlaceKind.Region),
        ("Tegea", 37.5000, 22.4000, PlaceKind.City),
        ("Abydos", 40.2000, 26.4160, PlaceKind.City),
        ("Lydia", 38.6830, 27.5160, PlaceKind.Region),
        ("Naxos", 37.8160, 15.2830, PlaceKind.City),
        ("Croton", 39.0833, 17.1333, PlaceKind.City),
        ("Eleusis", 38.0417, 23.5583, PlaceKind.City),
        ("Lemnos", 39.9160, 25.2500, PlaceKind.Region),
        ("Mykale", 38.1000, 26.8667, PlaceKind.Battlefield),
        ("Sicyon", 37.9833, 22.7250, PlaceKind.City),
        ("Thera", 36.4000, 25.4330, PlaceKind.Region),
        ("Eretria", 38.3917, 23.8083, PlaceKind.City),
        ("Athos", 40.1660, 24.3160, PlaceKind.City),
        ("Bubastis", 30.5660, 31.5160, PlaceKind.City),
        ("Dodona", 39.5500, 20.8000, PlaceKind.Sanctuary),
        ("Italy", 42.8330, 12.8330, PlaceKind.Region),
        ("Arcadia", 37.5830, 22.2500, PlaceKind.Region),
        ("Buto", 31.2000, 30.7330, PlaceKind.City),
        ("Cilicia", 36.6660, 34.3330, PlaceKind.Region),
        ("Elis", 37.8833, 21.4000, PlaceKind.City),
        ("Maeander", 37.4660, 27.1830, PlaceKind.Water),
        ("Abdera", 40.9833, 24.9667, PlaceKind.City),
        ("Lesbos", 39.1660, 26.3330, PlaceKind.Region),
        ("Phocaea", 38.6660, 26.7500, PlaceKind.City),
        ("Trachis", 38.8000, 22.5500, PlaceKind.City),
        ("Apollonia", 38.0167, 14.5833, PlaceKind.City),
        ("Barce", 32.5000, 20.8330, PlaceKind.City),
        ("Caria", 37.5000, 28.0000, PlaceKind.Region),
        ("Cyme", 38.6333, 24.1167, PlaceKind.City),
        ("Heliopolis", 30.1000, 31.3330, PlaceKind.City),
        ("Pallene", 38.0500, 23.8833, PlaceKind.City),
        ("Tanaïs", 47.1000, 39.4330, PlaceKind.City),
        ("Zancle", 38.1938, 15.5540, PlaceKind.City),       // aligned to Messana above
        ("Euphrates", 31.8300, 47.5000, PlaceKind.Water),
        ("Mytilene", 39.1000, 26.5500, PlaceKind.City),
        ("Thasos", 40.7830, 24.7160, PlaceKind.City),
        ("Therma", 40.6401, 22.9444, PlaceKind.City),       // aligned to Thessalonica above
        ("Andros", 37.8160, 24.9000, PlaceKind.City),
        ("Aphetae", 39.1167, 23.1167, PlaceKind.City),
        ("Chalcis", 38.4667, 23.6083, PlaceKind.City),
        ("Gela", 37.0667, 14.2500, PlaceKind.City),
        ("Magnesia", 39.2500, 22.7500, PlaceKind.Region),
        ("Megara", 38.0000, 23.3500, PlaceKind.City),
        ("Branchidae", 37.3500, 27.2330, PlaceKind.Sanctuary),
        ("Corcyra", 39.6330, 19.9160, PlaceKind.City),
        ("Orchomenus", 37.7160, 22.3000, PlaceKind.City),
        ("Sestus", 40.2833, 26.4000, PlaceKind.City),
        ("Sybaris", 39.7500, 16.4833, PlaceKind.City),
        ("Tanagra", 38.3083, 23.6000, PlaceKind.City),
        ("Troezen", 37.5000, 23.3750, PlaceKind.City),
        ("Arabian Gulf", 25.5830, 53.8300, PlaceKind.Water),
        ("Atarneus", 39.0500, 26.9500, PlaceKind.City),
        ("Cyzicus", 40.4167, 27.9000, PlaceKind.City),
        ("Euxine", 42.0000, 38.0000, PlaceKind.Water),
        ("Euxine sea", 42.0000, 38.0000, PlaceKind.Water),
        ("Khemmis", 26.5660, 31.7330, PlaceKind.City),
        ("Melissa", 39.3000, 17.0333, PlaceKind.City),
        ("Naucratis", 30.9000, 30.5830, PlaceKind.City),
        ("Palestine", 31.9160, 35.3330, PlaceKind.Region),
        ("Proconnesus", 40.6330, 27.6160, PlaceKind.Region),
        ("Achaea", 38.2500, 21.7500, PlaceKind.Region),
        ("Cappadocia", 38.5000, 36.0000, PlaceKind.Region),
        ("Caucasus", 42.0000, 46.8330, PlaceKind.Region),
        ("Eion", 40.7333, 23.8833, PlaceKind.City),
        ("Laconia", 37.0000, 22.5830, PlaceKind.Region),
        ("Phasis", 42.1830, 41.6830, PlaceKind.City),
        ("Priene", 37.6333, 27.2833, PlaceKind.City),
        ("Rhegium", 38.1000, 15.6500, PlaceKind.City),
        ("Tiryns", 37.6000, 22.8167, PlaceKind.City),
        ("lake Moeris", 29.4660, 30.6660, PlaceKind.Water),
        ("Abae", 38.5917, 22.9583, PlaceKind.Sanctuary),
        ("Aegean", 38.5000, 25.0000, PlaceKind.Water),
        ("Axius", 40.5830, 22.8330, PlaceKind.Water),
        ("Clazomenae", 38.3167, 26.7833, PlaceKind.City),
        ("Colophon", 38.1167, 27.1333, PlaceKind.City),
        ("Cos", 36.8917, 27.3000, PlaceKind.City),
        ("Hermione", 37.3833, 23.2583, PlaceKind.City),
        ("Ilion", 39.9576, 26.2386, PlaceKind.City),        // aligned to Troy/Ilium above
        ("Lampsacus", 40.3660, 26.7000, PlaceKind.City),
        ("Lindus", 36.0833, 28.1083, PlaceKind.City),
        ("Olynthus", 40.3000, 23.3667, PlaceKind.City),
        ("Panionion", 37.6833, 27.1167, PlaceKind.Sanctuary),
        ("Paros", 37.1000, 25.2000, PlaceKind.Region),
        ("Tigris", 31.0000, 47.4160, PlaceKind.Water),
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
        var found = LookupWithKind(placeName);
        return found == null ? null : (found.Value.Lat, found.Value.Lon);
    }

    /// <summary>
    /// As <see cref="Lookup"/>, and also what sort of place it is - so a pin
    /// built from one of your tags can be coloured and filtered the same way as
    /// one from the catalog.
    /// </summary>
    public static (double Lat, double Lon, PlaceKind Kind)? LookupWithKind(string placeName)
    {
        var normalized = placeName.Trim();

        foreach (var (key, lat, lon, kind) in Entries)
        {
            if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase))
                return (lat, lon, kind);
        }

        foreach (var (key, lat, lon, kind) in Entries)
        {
            if (normalized.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return (lat, lon, kind);
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
    public static IReadOnlyList<(string Name, double Lat, double Lon, PlaceKind Kind)> All()
    {
        var seenCoordinates = new HashSet<(double, double)>();
        var result = new List<(string, double, double, PlaceKind)>();

        foreach (var (key, lat, lon, kind) in Entries)
        {
            if (seenCoordinates.Add((lat, lon)))
            {
                result.Add((key, lat, lon, kind));
            }
        }

        return result;
    }
}
