namespace ClassicaCodex.Core;

/// <summary>
/// How securely a work is attributed to the author it is filed under.
///
/// Three values rather than a flag, because the manuscripts hand down two quite
/// different situations under one heading. *Rhesus* is genuinely contested -
/// serious editors defend it and serious editors reject it. *Definitiones* is
/// filed under Plato because it travelled in his corpus and is nobody's idea of
/// Plato. Collapsing those loses the distinction that matters when deciding
/// what to read, and the one that matters when deciding what to measure.
/// </summary>
public enum AttributionStatus
{
    /// <summary>
    /// No serious doubt. The default, and what almost everything is.
    /// </summary>
    Accepted,

    /// <summary>
    /// Genuinely contested - defended and rejected by editors who know the
    /// material. *Rhesus*, *Alcibiades I*, *Hippias Major*, the *Cynegeticus*.
    ///
    /// These are the interesting ones and the ones a stylometric pool should
    /// usually EXCLUDE while establishing what an author's own works look like,
    /// then be tested against. Leaving them in makes the reference set partly
    /// the thing being tested.
    /// </summary>
    Disputed,

    /// <summary>
    /// Transmitted under the name and generally rejected: the Platonic
    /// *Definitiones*, the Xenophontic *Constitution of the Athenians*,
    /// pseudo-Senecan *Octavia*.
    ///
    /// Still worth having in a library - they are real texts of real interest,
    /// and several are the only surviving witness to something - but a baseline
    /// margin computed with them in it is measuring a corpus its own editors do
    /// not believe in.
    /// </summary>
    Spurious
}

/// <summary>
/// Works whose attribution is doubted, as a starting default.
///
/// Hand-curated, like AuthorEraData and PlaceData, and for the same reason:
/// Perseus and First1KGreek file the spuria under the author without comment,
/// because their job is to transmit what the manuscripts say rather than to
/// adjudicate. So the corpus offers no signal at all, and the alternative to a
/// table like this is presenting *Definitiones* as flatly Platonic.
///
/// WHAT THIS IS AND IS NOT. It is a convenience: the well-known cases, so a new
/// library starts closer to right than a blank slate. It is not an authority
/// and it is deliberately not exhaustive - the Aristotelian corpus alone would
/// need a monograph, and the Hippocratic one is contested nearly end to end.
/// Entries record the mainstream editorial position as of the standard modern
/// editions, and anywhere that position is live rather than settled the entry
/// says Disputed rather than picking a side.
///
/// A user's own judgement always wins: once a work's status has been set by
/// hand it is never overwritten from here, even when this table grows. See
/// Works.AttributionSetByUser.
/// </summary>
public static class DisputedWorkData
{
    /// <param name="Author">Author the work is filed under, matched loosely.</param>
    /// <param name="TitleFragment">
    /// Enough of the title to identify it. Matched case-insensitively as a
    /// substring in either direction, because the same work arrives as
    /// "Cleitophon", "Clitophon" and "Kleitophon" depending on the corpus.
    /// </param>
    /// <param name="Status">Disputed where the question is live, Spurious where it is not.</param>
    /// <param name="Note">Why, in one line, for the reader who has not met the problem.</param>
    /// <param name="AlsoSpelled">
    /// Other names the same work travels under. Listed explicitly rather than
    /// guessed at: transliteration varies by corpus (Cleitophon, Clitophon,
    /// Kleitophon), numbering varies by editor (Alcibiades 1, Alcibiades I,
    /// First Alcibiades), and several works are known by a nickname the
    /// manuscripts never use (the Old Oligarch). A hand-curated table can
    /// simply say so, which is more honest and more auditable than a fuzzy
    /// matcher that will eventually match something it should not.
    /// </param>
    public sealed record Entry(
        string Author,
        string TitleFragment,
        AttributionStatus Status,
        string Note,
        string[]? AlsoSpelled = null)
    {
        /// <summary>Every name this entry answers to.</summary>
        public IEnumerable<string> AllTitles =>
            new[] { TitleFragment }.Concat(AlsoSpelled ?? Array.Empty<string>());
    }

    private static readonly Entry[] Entries =
    {
        // ---- Plato. The appendix travelled with the corpus from antiquity;
        // Thrasyllus' tetralogies already carried most of it, and the Alexandrian
        // scholars were rejecting parts of it then.
        new("Plato", "Definitiones", AttributionStatus.Spurious,
            "A glossary of terms appended to the corpus; rejected since antiquity.", new[] { "Definitions", "Horoi" }),
        new("Plato", "Alcibiades 2", AttributionStatus.Spurious,
            "Second Alcibiades - generally rejected, on style and on doctrine.", new[] { "Alcibiades II", "Second Alcibiades", "Alcibiades minor" }),
        new("Plato", "Hipparchus", AttributionStatus.Spurious,
            "Transmitted in the corpus, rejected by most modern editors."),
        new("Plato", "Lovers", AttributionStatus.Spurious,
            "Rival Lovers - short, and generally taken as not Plato's.", new[] { "Rival Lovers", "Amatores", "Erastae" }),
        new("Plato", "Theages", AttributionStatus.Spurious,
            "Rejected by most editors, though it was read as Platonic in antiquity."),
        new("Plato", "Minos", AttributionStatus.Spurious,
            "Generally rejected; sometimes read as a prologue to the Laws by another hand."),
        new("Plato", "Epinomis", AttributionStatus.Disputed,
            "An appendix to the Laws, ascribed in antiquity to Philip of Opus. Still argued."),
        new("Plato", "Alcibiades 1", AttributionStatus.Disputed,
            "First Alcibiades - defended and rejected by serious editors alike.", new[] { "Alcibiades I", "First Alcibiades", "Alcibiades maior" }),
        new("Plato", "Hippias Major", AttributionStatus.Disputed,
            "Greater Hippias - authenticity argued since Ast rejected it in 1816.", new[] { "Greater Hippias", "Hippias maior" }),
        new("Plato", "Cleitophon", AttributionStatus.Disputed,
            "Very short and unlike the rest; genuine, a fragment, or neither.", new[] { "Clitophon", "Kleitophon" }),
        new("Plato", "Axiochus", AttributionStatus.Spurious,
            "One of the later additions to the corpus; not Plato's."),
        new("Plato", "Eryxias", AttributionStatus.Spurious,
            "Appended to the corpus, rejected."),
        new("Plato", "Demodocus", AttributionStatus.Spurious,
            "Appended to the corpus, rejected."),
        new("Plato", "Sisyphus", AttributionStatus.Spurious,
            "Appended to the corpus, rejected."),
        new("Plato", "On Justice", AttributionStatus.Spurious,
            "One of the short spuria of the appendix."),
        new("Plato", "On Virtue", AttributionStatus.Spurious,
            "One of the short spuria of the appendix."),
        new("Plato", "Halcyon", AttributionStatus.Spurious,
            "Also transmitted among Lucian's works, which is its own argument.", new[] { "Alcyon" }),

        // ---- Euripides. Rhesus is the reason this library has a stylometry
        // bench at all; see docs/stylometry-notes.md for what the bench could
        // and could not say about it.
        new("Euripides", "Rhesus", AttributionStatus.Disputed,
            "Doubted since the ancient hypothesis reported the question; argued ever since."),

        // ---- Xenophon.
        new("Xenophon", "Constitution of the Athenians", AttributionStatus.Spurious,
            "The Old Oligarch - transmitted with Xenophon, certainly not his.", new[] { "Athenaion Politeia", "Old Oligarch", "Respublica Atheniensium" }),
        new("Xenophon", "Cynegeticus", AttributionStatus.Disputed,
            "On Hunting - the proem especially is doubted.", new[] { "On Hunting" }),

        // ---- Seneca.
        new("Seneca", "Octavia", AttributionStatus.Spurious,
            "Dramatises Seneca's own death, which settles it."),
        new("Seneca", "Hercules Oetaeus", AttributionStatus.Disputed,
            "Twice the length of the other tragedies and metrically unlike them."),

        // ---- Homer and the hymns. The hymns are not Homer's in any modern
        // sense, but they are filed under the name everywhere.
        new("Homer", "Batrachomyomachia", AttributionStatus.Spurious,
            "Battle of Frogs and Mice - a much later parody.", new[] { "Battle of Frogs and Mice" }),
        new("Homer", "Hymns", AttributionStatus.Spurious,
            "The Homeric Hymns are a collection of various dates and hands.", new[] { "Homeric Hymns" }),

        // ---- Lucian.
        new("Lucian", "Amores", AttributionStatus.Disputed,
            "Also transmitted separately; commonly taken as later."),
        new("Lucian", "Ass", AttributionStatus.Disputed,
            "Lucius, or The Ass - its relation to Apuleius is unresolved.", new[] { "Lucius", "Onos" }),

        // ---- Anacreon.
        new("Anacreon", "Anacreontea", AttributionStatus.Spurious,
            "Imitations of Anacreon spanning several centuries after him."),
    };

    /// <summary>
    /// The catalog's opinion about a work, or null when it has none - which is
    /// the answer for almost everything.
    ///
    /// Both author and title must match. Title matching is substring in either
    /// direction and case-insensitive, because the same work arrives as
    /// "Cleitophon" from one corpus and "Clitophon" from another, and as
    /// "Alcibiades 1" or "Alcibiades I" or "First Alcibiades" depending on who
    /// catalogued it.
    /// </summary>
    public static Entry? Lookup(string authorName, string workTitle)
    {
        if (string.IsNullOrWhiteSpace(authorName) || string.IsNullOrWhiteSpace(workTitle))
            return null;

        var author = authorName.Trim();
        var titleWords = Words(workTitle);

        foreach (var entry in Entries)
        {
            if (!author.Contains(entry.Author, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var candidate in entry.AllTitles)
            {
                if (ContainsWords(titleWords, Words(candidate))) return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// A title split into lowercase word tokens, punctuation dropped.
    /// </summary>
    private static string[] Words(string title) =>
        title.ToLowerInvariant()
             .Split(new[] { ' ', ',', '.', ':', ';', '\'', '"', '(', ')', '[', ']', '-', '\u2014', '\u2013' },
                    StringSplitOptions.RemoveEmptyEntries)
             .ToArray();

    /// <summary>
    /// Whether the catalog's words appear as a run inside the title's words.
    ///
    /// WORDS, NOT SUBSTRINGS, AND ONLY IN THIS DIRECTION. The first version
    /// matched substrings either way round, which marked Plato's *Ion* as
    /// spurious - "ion" sits inside "Definitiones", and inside "Constitution of
    /// the Athenians" as well. A genuine dialogue quietly reclassified, and
    /// dropped from any pool filtered on attribution.
    ///
    /// Matching only fragment-inside-title also drops the reverse case, where a
    /// short title matched a longer catalog entry. That cost the ability to
    /// recognise a work called just "Alcibiades" as Alcibiades I - which is the
    /// right trade, because it could equally be Alcibiades II and guessing
    /// between them is exactly the sort of thing this table should not do.
    /// </summary>
    private static bool ContainsWords(string[] title, string[] fragment)
    {
        if (fragment.Length == 0 || fragment.Length > title.Length) return false;

        for (var start = 0; start + fragment.Length <= title.Length; start++)
        {
            var all = true;

            for (var i = 0; i < fragment.Length; i++)
            {
                if (title[start + i] != fragment[i]) { all = false; break; }
            }

            if (all) return true;
        }

        return false;
    }

    /// <summary>Everything the catalog knows, for the settings screen that lists it.</summary>
    public static IReadOnlyList<Entry> All() => Entries;
}
