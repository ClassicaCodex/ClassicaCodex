namespace ClassicaCodex.UI;

/// <summary>
/// Rough landmass silhouettes for the places map background - Iberia,
/// Italy's boot, the Peloponnese's hand shape, and so on.
///
/// This is NOT surveyed coastline data. It's hand-approximated from general
/// geographic knowledge, aiming for "recognizable at a glance" rather than
/// cartographic accuracy - the same self-contained, no-external-dependency
/// spirit MapCanvas's own grid projection already uses, just extended to
/// actual landmass shapes instead of a bare lat/lon grid. Coastlines here
/// can be off by tens of kilometers in places; treat every shape as
/// illustrative, not something to measure a real distance against.
/// </summary>
public static class AncientWorldCoastline
{
    /// <summary>Each entry is one landmass, as a closed ring of (Lat, Lon) points.</summary>
    public static readonly (double Lat, double Lon)[][] Landmasses =
    {
        // Iberian Peninsula
        new (double, double)[]
        {
            (43.7, -7.9), (43.4, -2.0), (42.8, 0.3), (41.4, 2.2), (39.5, 0.2),
            (38.3, -0.3), (36.7, -2.5), (36.0, -5.4), (37.0, -7.4), (38.7, -9.4),
            (41.1, -8.8), (43.0, -9.0), (43.7, -7.9)
        },

        // France / Gaul
        new (double, double)[]
        {
            (43.4, -1.8), (44.6, -1.2), (47.2, -2.5), (48.6, -4.5), (49.7, -1.6),
            (51.0, 1.8), (49.9, 4.7), (48.0, 7.6), (45.9, 7.0), (43.9, 4.8),
            (43.3, 3.0), (42.8, 0.3), (43.4, -1.8)
        },

        // British Isles (deliberately minimal - at the map's edge, not the focus)
        new (double, double)[]
        {
            (50.0, -5.5), (51.5, -3.4), (53.4, -3.0), (55.8, -4.9), (57.6, -4.0),
            (58.6, -3.0), (57.0, -2.0), (54.6, -0.5), (52.9, 1.3), (51.5, 0.1),
            (50.7, -1.3), (50.0, -5.5)
        },

        // Italy - the boot, plus enough of the Adriatic side to read as one.
        // (A previous version of this polygon self-intersected near the
        // heel/toe transition - traced out of order, the path crossed
        // itself, which made GDI+'s fill rule punch alternating cutouts
        // into what should have been one solid shape. This one traces the
        // coast in a single consistent direction: down the west coast,
        // around the toe, up through the arch to the heel, then back up
        // the Adriatic side - never doubling back over itself.)
        new (double, double)[]
        {
            (45.8, 7.7), (44.4, 8.9), (43.6, 10.0), (41.9, 12.5), (40.85, 14.25),
            (39.3, 16.25), (38.1, 15.9), (38.9, 16.6), (40.1, 17.2), (40.4, 17.9),
            (40.6, 18.4), (41.1, 16.9), (42.5, 14.3), (43.6, 13.5), (44.5, 12.3),
            (45.4, 12.3), (45.8, 7.7)
        },

        // Sicily
        new (double, double)[]
        {
            (38.2, 12.4), (38.15, 13.4), (38.2, 15.2), (37.05, 15.3), (36.7, 14.3),
            (37.3, 12.8), (38.2, 12.4)
        },

        // Sardinia
        new (double, double)[]
        {
            (41.25, 9.1), (40.9, 9.7), (39.9, 9.6), (39.1, 9.5), (38.9, 8.4),
            (39.7, 8.5), (40.6, 8.4), (41.25, 9.1)
        },

        // Corsica
        new (double, double)[] { (43.0, 9.4), (42.6, 9.5), (41.6, 9.2), (41.4, 8.8), (42.3, 9.0), (43.0, 9.4) },

        // Greece mainland (simplified - deeply indented in reality)
        new (double, double)[]
        {
            (39.7, 20.0), (40.6, 21.0), (40.9, 22.9), (40.2, 24.3), (39.3, 23.7),
            (38.4, 23.6), (37.9, 22.9), (38.2, 21.8), (39.0, 20.7), (39.7, 20.0)
        },

        // Peloponnese - the "hand" shape south of the Isthmus of Corinth
        new (double, double)[]
        {
            (38.2, 21.8), (38.0, 22.1), (37.6, 22.8), (37.3, 23.2), (36.7, 23.05),
            (36.4, 22.5), (36.8, 21.9), (37.0, 21.7), (37.6, 21.4), (38.2, 21.8)
        },

        // Crete
        new (double, double)[]
        {
            (35.5, 24.0), (35.35, 25.0), (35.3, 26.1), (34.95, 25.7), (35.0, 24.6),
            (35.5, 24.0)
        },

        // Anatolia / Asia Minor
        new (double, double)[]
        {
            (41.0, 29.0), (42.0, 35.2), (41.1, 38.5), (39.7, 41.0), (37.0, 38.0),
            (36.8, 35.8), (36.9, 30.7), (36.7, 27.9), (38.4, 27.1), (39.1, 27.2),
            (40.4, 27.0), (41.0, 29.0)
        },

        // Cyprus
        new (double, double)[] { (35.3, 32.3), (35.7, 33.4), (35.7, 34.6), (34.9, 34.0), (34.6, 33.0), (35.3, 32.3) },

        // Levant coast + a bit of inland depth
        new (double, double)[]
        {
            (36.2, 36.2), (35.5, 35.9), (34.4, 35.9), (33.3, 35.2), (32.8, 35.5),
            (31.5, 34.4), (31.0, 34.3), (31.8, 36.0), (33.5, 36.3), (34.9, 36.0),
            (36.2, 36.2)
        },

        // Egypt - the Nile delta and a thin strip down the valley
        new (double, double)[]
        {
            (31.5, 34.4), (31.2, 29.9), (31.4, 27.5), (29.0, 25.5), (24.0, 32.0),
            (25.7, 32.6), (27.2, 31.2), (29.8, 31.3), (31.0, 30.8), (31.5, 34.4)
        },

        // North Africa coast - Cyrenaica to the Strait of Gibraltar
        new (double, double)[]
        {
            (32.8, 21.9), (31.2, 18.0), (32.9, 13.2), (33.9, 11.0), (36.8, 10.2),
            (37.3, 9.6), (36.9, 7.8), (36.7, 3.0), (35.9, -1.5), (35.8, -5.4),
            (34.0, -5.0), (32.0, 8.0), (30.0, 15.0), (31.0, 20.0), (32.8, 21.9)
        },

        // Black Sea's southern/western rim (rough - most of the sea itself is
        // off the map's north edge, this is just enough to read as a coast)
        new (double, double)[]
        {
            (41.0, 29.0), (42.5, 27.5), (43.4, 28.2), (44.2, 28.6), (45.2, 29.6),
            (46.6, 30.7), (45.4, 33.5), (44.6, 34.0), (44.4, 33.4), (42.0, 35.2),
            (41.0, 29.0)
        },

        // Mesopotamia and the Persian Gulf (rough - Babylon, Ur, Susa, Nineveh,
        // Persepolis, and Ecbatana all sit within or near this outline)
        new (double, double)[]
        {
            (37.1, 38.8), (36.4, 43.2), (34.8, 48.5), (32.2, 48.3), (29.9, 52.9),
            (26.6, 56.0), (27.0, 50.5), (29.6, 48.8), (30.0, 47.9), (31.0, 46.1),
            (33.3, 44.4), (35.0, 40.5), (37.1, 38.8)
        }
    };
}
