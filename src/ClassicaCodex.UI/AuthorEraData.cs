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

        // Renaissance / early modern English, added with the canonical-engLit
        // opensource corpus. Same rough-consensus caveat as everything above.
        // Keyed on a short/surname form so Lookup's substring match catches the
        // fuller catalog and header names ("Sir Philip Sidney", "Henry
        // Peacham.", "William Shakespeare"). The modern secondary scholars in
        // that corpus (E. A. Abbott, M. W. MacCallum) and the editorial
        // overview are intentionally left undated - they write about these
        // authors, they aren't of their era.
        ("Shakespeare", 1564, 1616),
        ("Marlowe", 1564, 1593),
        ("Holinshed", 1525, 1580),
        ("Hakluyt", 1553, 1616),
        ("Sidney", 1554, 1586),
        ("James I", 1566, 1625),
        ("Wilson", 1524, 1581),
        ("Peacham", 1547, 1634),

        // Post-classical Greek, added with the First1KGreek corpus. Keyed on
        // the exact groupname string from that corpus's own catalog files
        // (verified against a real clone, not guessed) rather than a short
        // form - this corpus is dense with homonyms in a way nothing above
        // was (three people named Heraclitus, three named Iamblichus, several
        // Dionysii), so an exact key plus the exact-match-first change to
        // Lookup above is what keeps these from bleeding into each other or
        // into an existing classical entry.
        //
        // Two collision directions turned up, and only one is fixable within
        // this substring-fallback design:
        //  - Bare "Aristophanes" above is the comic playwright; the corpus
        //    separately has "Aristophanes of Byzantium", a different,
        //    2nd-century BCE grammarian, whose fuller name contains the
        //    shorter key as a substring. Fixed by giving him his own exact
        //    entry below, verified separately - exact match now intercepts
        //    him before the playwright's entry ever gets a chance to.
        //  - The reverse direction has no clean fix: the corpus has THREE
        //    separate people named bare "Heraclitus" or built on it
        //    ("Heraclitus", "Heraclitus of Ephesus", "Heraclitus
        //    Paradoxographus" - three different CTS text-group IDs, checked
        //    directly). Adding a correctly-dated "Heraclitus of Ephesus" (the
        //    famous pre-Socratic, otherwise easy to verify) would have that
        //    longer key match bare "Heraclitus" via the OTHER substring
        //    direction (key contains name), silently handing a different,
        //    unidentified person the philosopher's 6th-century BCE dates.
        //    Since I can't confidently identify or date that bare entry
        //    either, there's no safe way to add one without corrupting the
        //    other - so none of the three are dated here, and the famous
        //    Heraclitus shows as "unknown era" rather than risk it.
        //
        // Deliberately not attempted for the same reasons or on confidence
        // grounds: the many Anonymi/Pseudo-/Scholia-in- entries, collectively-
        // or pseudonymously-authored works (Suda, Hermetica, Oracula
        // Sibyllina, Testamentum Abrahae), scripture (Septuaginta, New
        // Testament, Hebrew Bible), and most minor astrologers/grammarians/
        // epistolographers. One acknowledged residual case: "Ammonius" below
        // is Ammonius Hermiae, the far more commonly cited Ammonius in this
        // corpus - but "Ammonius Grammaticus", a different and, to me,
        // undatable person, will inherit his dates via substring fallback the
        // same way Aristophanes of Byzantium would have. Left as-is because
        // dropping "Ammonius" entirely loses far more accuracy than this one
        // mislabeled entry costs.
        ("Aristophanes of Byzantium", -257, -180),
        ("Plotinus", 204, 270),
        ("Porphyrius", 234, 305),
        ("Iamblichus (Scr. Erot.)", 120, 180),
        ("Iamblichus", 245, 325),
        ("Proclus", 412, 485),
        ("Simplicius", 490, 560),
        ("Syrianus", 375, 437),
        ("Olympiodorus", 495, 570),
        ("Ammonius", 435, 517),
        ("Themistius", 317, 388),
        ("Alexander of Aphrodisias", 160, 220),
        ("John Philoponus", 490, 570),
        ("David the Invincible", 490, 560),
        ("Eustratius", 1050, 1120),
        ("Michael of Ephesus", 1090, 1155),
        ("Sophonias", 1260, 1320),
        ("Asclepius", 480, 550),
        ("Aspasius", 100, 150),

        ("Athanasius", 296, 373),
        ("Justin Martyr", 100, 165),
        ("Irenaeus, Saint, Bishop of Lyon", 130, 202),
        ("Athenagoras", 133, 190),
        ("Theophilus", 120, 183),
        ("Tatianus", 120, 180),
        ("Hippolytus", 170, 235),
        ("Methodius", 250, 311),
        ("Epiphanius", 310, 403),
        ("Gregory of Nazianzus", 329, 390),
        ("Cyril of Alexandria", 376, 444),
        ("Marcellus of Ankara", 280, 374),

        ("Socrates Scholasticus", 380, 450),
        ("Sozomenus", 400, 450),
        ("Evagrius, Scholasticus", 536, 594),
        ("Philostorgius", 368, 439),

        ("Anna Comnena", 1083, 1153),
        ("Joannes Zonaras", 1070, 1140),
        ("Gregory II, of Cyprus, Patriarch of Constantinople", 1241, 1290),

        ("Archimedes", -287, -212),
        ("Apollonius of Perga", -240, -190),
        ("Aristarchus of Samos", -310, -230),
        ("Hipparchus", -190, -120),
        ("Diophantus Alexandrinus", 200, 284),
        ("Hero of Alexandria", 10, 70),
        ("Pappus Alexandrinus", 290, 350),
        ("Theon Smyrnaeus", 70, 135),
        ("Nicomachus of Gerasa", 60, 120),
        ("Autolycus", -360, -290),

        ("Musonius Rufus", 30, 101),
        ("Sextus Empiricus", 160, 210),
        ("Maximus of Tyre", 125, 185),
        ("Chrysippus", -279, -206),
        ("Cleanthes", -330, -230),
        ("Xenocrates of Chalcedon", -396, -314),
    };

    /// <summary>
    /// Best-effort fuzzy match against the ingested author's exact name
    /// (which comes straight from Perseus's catalog and varies in format -
    /// "Cicero, Marcus Tullius", "Virgil", "Josephus, Flavius", etc.). Tries
    /// each reference key as a substring of the name or vice versa, so
    /// either ordering matches.
    ///
    /// Checks for an exact match across every entry first, before any
    /// substring check runs. Added once the corpus started including
    /// authors like the three different people named Heraclitus, or the
    /// three named Iamblichus - a generic key like "Heraclitus" (added years
    /// ago for the pre-Socratic) is a substring of "Heraclitus of Ephesus"
    /// and "Heraclitus Paradoxographus" too, so without this, whichever of
    /// those three came first in Entries would silently hand its dates to
    /// the other two. Every entry added since is keyed on the author's full,
    /// exact catalog name for exactly this reason - substring matching still
    /// runs afterward, unchanged, for older entries deliberately keyed on a
    /// short form ("Sidney" catching "Sir Philip Sidney").
    /// </summary>
    public static (int StartYear, int EndYear)? Lookup(string authorName)
    {
        var normalized = authorName.Trim();

        foreach (var (key, start, end) in Entries)
        {
            if (string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase))
                return (start, end);
        }

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
