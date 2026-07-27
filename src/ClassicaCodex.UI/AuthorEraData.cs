namespace ClassicaCodex.UI;

/// <summary>
/// Approximate floruit/lifespan years for well-known classical authors, used
/// only by the timeline view. Perseus's own catalog metadata doesn't include
/// dates, so this is a separate, hand-curated best-effort reference table -
/// not derived from the ingested corpus. Years are negative for BCE.
///
/// Treat everything here as rough consensus estimates, not settled fact -
/// several of these (Homer and Hesiod especially) are genuinely disputed by
/// a century or more among scholars. Good enough for "who could plausibly
/// have read whom", not good enough to cite.
///
/// Coverage is necessarily partial - only major/well-attested authors are
/// included. Anything not in this table shows up as "unknown era" in the
/// timeline rather than being guessed at.
/// </summary>
public static class AuthorEraData
{
    private static readonly (string Key, int StartYear, int EndYear)[] Entries =
    {
        ("Homer", -750, -650),
        ("Hesiod", -750, -650),
        ("Homeric Hymns", -700, -500),
        ("Archilochus", -680, -640),
        ("Sappho", -630, -570),
        ("Alcaeus", -620, -580),
        ("Aesop", -620, -560),
        ("Solon", -630, -560),
        ("Pindar", -518, -438),
        ("Bacchylides", -520, -450),
        ("Aeschylus", -525, -456),
        ("Sophocles", -496, -406),
        ("Euripides", -480, -406),
        ("Herodotus", -484, -425),
        ("Thucydides", -460, -400),
        ("Aristophanes", -446, -386),
        ("Antiphon", -480, -411),
        ("Andocides", -440, -390),
        ("Lysias", -445, -380),
        ("Isocrates", -436, -338),
        ("Isaeus", -420, -340),
        ("Plato", -428, -348),
        ("Xenophon", -430, -354),
        ("Aeschines", -389, -314),
        ("Demosthenes", -384, -322),
        ("Demades", -380, -319),
        ("Dinarchus", -361, -291),
        ("Hyperides", -390, -322),
        ("Aristotle", -384, -322),
        ("Theophrastus", -371, -287),
        ("Menander", -342, -290),
        ("Demetrius of Phaleron", -350, -280),
        ("Euclid", -325, -265),
        ("Callimachus", -310, -240),
        ("Aratus Solensis", -315, -240),
        ("Apollonius Rhodius", -295, -215),
        ("Theocritus", -300, -260),
        ("Lycophron", -320, -280),
        ("Colluthus of Lycopolis", 480, 520),
        ("Polybius", -200, -118),
        ("Agathemerus", 200, 300),
        ("Diodorus Siculus", -90, -30),
        ("Dionysius of Halicarnassus", -60, 7),
        ("Strabo", -64, 24),
        ("Chariton of Aphrodisias", -50, 50),
        ("Achilles Tatius", 100, 200),
        ("Longus", 200, 300),
        ("Plutarch", 46, 120),
        ("Lucian of Samosata", 125, 180),
        ("Pausanias", 110, 180),
        ("Appianus of Alexandria", 95, 165),
        ("Arrian", 86, 160),
        ("Cassius Dio Cocceianus", 155, 235),
        ("Athenaeus of Naucratis", 170, 230),
        ("Diogenes Laertius", 180, 240),
        ("Aelian", 175, 235),
        ("Aristides, Aelius", 117, 181),
        ("Dio Chrysostom", 40, 115),
        ("Epictetus", 55, 135),
        ("Galen", 129, 216),
        ("Hippocrates", -460, -370),
        ("Harpocration", 100, 200),
        ("Claudius Ptolemaeus", 100, 170),
        ("Aretaeus of Cappadocia", 50, 130),
        ("Asclepiodotus", -50, 1),
        ("Aeneas Tacticus", -400, -350),
        ("Callistratus", 250, 350),
        ("Greek Anthology", -300, 600),
        ("Hermas", 100, 160),
        ("Barnabae Epistula", 70, 135),
        ("Clemens Romanus", 35, 99),
        ("Ignatius of Antioch", 35, 108),
        ("Didache", 50, 120),
        ("Clement of Alexandria", 150, 215),
        ("Origen", 184, 253),
        ("Eusebius of Caesarea", 260, 340),
        ("Basil, Saint, Bishop of Caesarea", 330, 379),
        ("John of Damascus, Saint", 675, 749),
        ("Julian, Emperor of Rome", 331, 363),
        ("Augustus Emperor of Rome", -63, 14),

        // Latin
        ("Livius Andronicus", -284, -204),
        ("Plautus", -254, -184),
        ("Terence", -195, -159),
        ("Cato, Marcus Porcius", -234, -149),
        ("Lucretius", -99, -55),
        ("Catullus, C. Valerius", -84, -54),
        ("Cicero, Marcus Tullius", -106, -43),
        ("Julius Caesar", -100, -44),
        ("Sallust", -86, -35),
        ("Virgil", -70, -19),
        ("Vergil", -70, -19),
        ("Horace", -65, -8),
        ("Ovid", -43, 17),
        ("Livy", -59, 17),
        ("Tibullus", -55, -19),
        ("Propertius", -50, -15),
        ("Vitruvius", -80, -15),
        ("Celsus, Aulus Cornelius", -25, 50),
        ("Seneca", -4, 65),
        ("Petronius", 27, 66),
        ("Lucan", 39, 65),
        ("Pliny", 23, 79),
        ("Quintilian", 35, 100),
        ("Martial", 38, 104),
        ("Statius", 45, 96),
        ("Juvenal", 55, 127),
        ("Tacitus", 56, 120),
        ("Suetonius", 69, 122),
        ("Florus, Lucius Annaeus", 74, 130),
        ("Curtius Rufus, Quintus", 1, 100),
        ("Columella, Lucius Junius Moderatus", 4, 70),
        ("Apuleius", 124, 170),
        ("Gellius, Aulus", 125, 180),
        ("Ammianus Marcellinus", 330, 400),
        ("Claudian", 370, 404),
        ("Ausonius, Decimus Magnus", 310, 395),
        ("Boethius", 477, 524),
        ("Jerome, Saint", 347, 420),

        // Jewish/other
        ("Flavius Josephus", 37, 100),
    };

    /// <summary>
    /// Best-effort fuzzy match against the ingested author's exact name
    /// (which comes straight from Perseus's catalog and varies in format -
    /// "Cicero, Marcus Tullius", "Virgil", "Josephus, Flavius", etc.). Tries
    /// each reference key as a substring of the name or vice versa, so
    /// either ordering matches.
    /// </summary>
    public static (int StartYear, int EndYear)? Lookup(string authorName)
    {
        var normalized = authorName.Trim();

        foreach (var (key, start, end) in Entries)
        {
            if (normalized.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return (start, end);
            }
        }

        return null;
    }

    public static string FormatYear(int year)
    {
        return year < 0 ? $"{-year} BCE" : $"{year} CE";
    }
}
