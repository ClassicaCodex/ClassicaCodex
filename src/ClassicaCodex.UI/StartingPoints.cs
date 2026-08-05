using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// Which works are reasonable to translate first, and which are not.
///
/// Nothing else in this application knows that texts differ enormously in
/// difficulty. The library tree presents Aeschylus and Xenophon identically,
/// so someone who cannot yet read the script picks by name recognition and
/// lands on choral lyric - compressed syntax, rare vocabulary, and a damaged
/// manuscript tradition that professional editors still argue over. The
/// lesson they take from that is that they cannot do it, rather than that
/// they chose badly.
///
/// This is editorial judgement, not data, so it is a hand-written list rather
/// than anything derived. It reflects the order these texts are conventionally
/// taught in - Xenophon and Caesar have been the standard first continuous
/// prose in each language for well over a century - and it is deliberately
/// short. A list of forty works would be another thing to choose from, which
/// is the problem it exists to solve.
/// </summary>
public static class StartingPoints
{
    /// <summary>
    /// One recommendation. Matched against the library by author and title
    /// text rather than by CTS URN, because the same work carries different
    /// URNs across the Perseus, Open Greek and Latin, and First1KGreek
    /// corpora, and which of those is installed varies per reader.
    /// </summary>
    public sealed record Suggestion(
        string Language,
        string AuthorKey,
        string[] TitleKeys,
        string Display,
        string Why);

    /// <summary>
    /// Ordered easiest first within each language. The ordering is the
    /// content: someone reading this list top to bottom should be reading it
    /// in the order they would meet these texts in a classroom.
    /// </summary>
    public static readonly IReadOnlyList<Suggestion> All = new[]
    {
        // --- Greek ---
        new Suggestion("grc", "aesop", new[] { "fab" },
            "Aesop, Fables",
            "A complete story in five or six lines. Short enough that finishing one is a real finish, which matters more at the start than it sounds."),

        new Suggestion("grc", "xenophon", new[] { "anabasis" },
            "Xenophon, Anabasis",
            "The standard first continuous Greek prose for a century and a half. Plain narrative, straightforward word order, and a small vocabulary that repeats as the march goes on."),

        new Suggestion("grc", "apollodorus", new[] { "library", "bibliotheca" },
            "Apollodorus, Library",
            "Myth after myth in flat summary prose. The sentence patterns repeat heavily, and you already know most of the stories, which lets you check yourself."),

        new Suggestion("grc", "lysias", new[] { "" },
            "Lysias, Speeches",
            "Clear, plain Attic written to be understood by a jury on first hearing. The speeches are short enough to see the shape of a whole argument."),

        new Suggestion("grc", "plato", new[] { "apology", "crito", "euthyphro" },
            "Plato, Apology / Crito / Euthyphro",
            "Conversational Attic in short exchanges. Harder than Xenophon, but the dialogue form means a sentence rarely runs long."),

        new Suggestion("grc", "lucian", new[] { "" },
            "Lucian, Dialogues",
            "Deliberately plain Attic written centuries after the fact, so the style is imitative and regular. Funny, too, which helps on a bad evening."),

        new Suggestion("grc", "homer", new[] { "iliad", "odyssey" },
            "Homer, Iliad / Odyssey",
            "A special case. The dialect is unfamiliar and it is verse, but the formulaic repetition means phrases you decode once recur for hundreds of lines. Worth trying once you have some prose behind you."),

        // --- Latin ---
        new Suggestion("lat", "eutropius", new[] { "" },
            "Eutropius, Breviarium",
            "Late Latin written to be simple on purpose - a potted history for readers who found Livy hard. Often the very first Latin text a student meets."),

        new Suggestion("lat", "caesar", new[] { "gall", "bell" },
            "Caesar, Gallic War",
            "The Latin counterpart to Xenophon and the standard starting point. Direct word order, a narrow military vocabulary, and short declarative sentences."),

        new Suggestion("lat", "nepos", new[] { "" },
            "Cornelius Nepos, Lives",
            "Short biographies in simple syntax. Each life is self-contained, so you can stop after one without leaving anything unfinished."),

        new Suggestion("lat", "cicero", new[] { "catilinam", "catiline", "amicitia", "senectute" },
            "Cicero, Catilinarians / On Friendship / On Old Age",
            "The periodic sentence starts here, and it is a real step up - but these are the four most-taught Cicero texts precisely because the step is manageable."),

        new Suggestion("lat", "ovid", new[] { "metamorphos" },
            "Ovid, Metamorphoses",
            "The most approachable Latin verse. The stories carry you, and Ovid's word order is far kinder than Vergil's.")
    };

    /// <summary>
    /// Works worth being warned off rather than silently allowed to fail at.
    ///
    /// Named explicitly because the alternative is a reader concluding the
    /// difficulty was theirs. These are hard for professionals.
    /// </summary>
    public const string HardWorksNote =
        "Worth saving for later: Aeschylus, Pindar, Sophocles and Thucydides in Greek; " +
        "Tacitus, Persius, Lucretius and Horace's Odes in Latin. Greek choral lyric and " +
        "Latin satire are genuinely difficult for professional scholars, and the text " +
        "itself is often uncertain. Starting there tells you nothing about whether you " +
        "can learn the language.";

    /// <summary>
    /// The suggestions that are actually in this library, paired with the work
    /// they matched.
    ///
    /// Which corpora a reader installed decides what is here, so this returns
    /// what was found rather than the full list with gaps. An entry pointing
    /// at something that isn't there is worse than a shorter list.
    /// </summary>
    public static List<(Suggestion Suggestion, Work Work)> AvailableIn(
        IReadOnlyDictionary<int, List<Work>> worksByAuthor,
        IReadOnlyDictionary<int, string> authorNames)
    {
        var found = new List<(Suggestion, Work)>();

        foreach (var suggestion in All)
        {
            foreach (var (authorId, works) in worksByAuthor)
            {
                if (!authorNames.TryGetValue(authorId, out var authorName)) continue;
                if (!Contains(authorName, suggestion.AuthorKey)) continue;

                // An empty title key means any work by this author will do -
                // used where the recommendation is the author's whole output
                // (Lysias, Nepos) rather than one title.
                var match = suggestion.TitleKeys.Any(k => k.Length == 0)
                    ? works.FirstOrDefault()
                    : works.FirstOrDefault(w => suggestion.TitleKeys.Any(k => Contains(w.Title, k)));

                if (match == null) continue;

                found.Add((suggestion, match));
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Case-insensitive substring test. Kept as one method so the matching
    /// rule is in a single place if it ever needs to handle accents - Perseus
    /// author names are Latin-script even for Greek authors, so it does not
    /// need to today.
    /// </summary>
    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
