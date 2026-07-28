using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// Turns the raw morphological tags stored on Lemma.PartOfSpeech into
/// something a reader can actually use - "v-sppemn-" means nothing without
/// the codebook, but "present medio-passive participle, masculine
/// nominative singular" is the whole point of having lemma data at all.
///
/// Two different tag vocabularies are in play, because the two source
/// corpora don't agree:
///
///   Greek (gcelano):     the 9-character Perseus/AGDT positional tag,
///                        e.g. "v-sppemn-". Each position means a fixed
///                        category; "-" means not applicable. This format
///                        is well documented and decoded fully below.
///
///   Latin (lascivaroma): a shorter, coarser label, e.g. "NOMcom". This is
///                        a part-of-speech category rather than a full
///                        parse, and its exact vocabulary is NOT confirmed
///                        against the real corpus.
///
/// That asymmetry drives the central rule here: this NEVER invents a parse
/// it isn't sure of. An unrecognized tag comes back as IsDecoded=false with
/// the raw text preserved, and callers show the raw tag plainly rather than
/// a confident-looking guess. A wrong parse is worse than no parse - someone
/// reading Homer or learning the language would be actively taught something
/// false, and would have no way to tell.
/// </summary>
public static class MorphologyDecoder
{
    /// <summary>A decoded (or deliberately undecoded) morphological tag.</summary>
    public class Parse
    {
        /// <summary>The tag exactly as stored, always preserved.</summary>
        public string RawTag { get; init; } = string.Empty;

        /// <summary>False when the tag wasn't in a recognized format - callers should show RawTag and not imply a parse.</summary>
        public bool IsDecoded { get; init; }

        /// <summary>Readable parse, e.g. "present medio-passive participle, masculine nominative singular". Empty when IsDecoded is false.</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>Just the part of speech ("verb", "noun"...), when known - useful for grouping even if nothing else decoded.</summary>
        public string? PartOfSpeech { get; init; }

        public override string ToString() => IsDecoded ? Description : RawTag;
    }

    // --- Perseus/AGDT positional tag tables -------------------------------
    // Position order: part of speech, person, number, tense, mood, voice,
    // gender, case, degree.

    private static readonly Dictionary<char, string> PartsOfSpeech = new()
    {
        ['n'] = "noun",
        ['v'] = "verb",
        ['a'] = "adjective",
        ['d'] = "adverb",
        ['l'] = "article",
        ['g'] = "particle",
        ['c'] = "conjunction",
        ['r'] = "preposition",
        ['p'] = "pronoun",
        ['m'] = "numeral",
        ['i'] = "interjection",
        ['e'] = "exclamation",
        ['u'] = "punctuation",
        ['x'] = "unclassified"
    };

    private static readonly Dictionary<char, string> Persons = new()
    {
        ['1'] = "1st person",
        ['2'] = "2nd person",
        ['3'] = "3rd person"
    };

    private static readonly Dictionary<char, string> Numbers = new()
    {
        ['s'] = "singular",
        ['p'] = "plural",
        ['d'] = "dual"
    };

    private static readonly Dictionary<char, string> Tenses = new()
    {
        ['p'] = "present",
        ['i'] = "imperfect",
        ['r'] = "perfect",
        ['l'] = "pluperfect",
        ['t'] = "future perfect",
        ['f'] = "future",
        ['a'] = "aorist"
    };

    private static readonly Dictionary<char, string> Moods = new()
    {
        ['i'] = "indicative",
        ['s'] = "subjunctive",
        ['o'] = "optative",
        ['n'] = "infinitive",
        ['m'] = "imperative",
        ['p'] = "participle",
        ['d'] = "gerund",
        ['g'] = "gerundive",
        ['u'] = "supine"
    };

    private static readonly Dictionary<char, string> Voices = new()
    {
        ['a'] = "active",
        ['p'] = "passive",
        ['m'] = "middle",
        ['e'] = "medio-passive",
        ['d'] = "deponent"
    };

    private static readonly Dictionary<char, string> Genders = new()
    {
        ['m'] = "masculine",
        ['f'] = "feminine",
        ['n'] = "neuter",
        ['c'] = "common"
    };

    private static readonly Dictionary<char, string> Cases = new()
    {
        ['n'] = "nominative",
        ['g'] = "genitive",
        ['d'] = "dative",
        ['a'] = "accusative",
        ['v'] = "vocative",
        ['l'] = "locative",
        ['b'] = "ablative"
    };

    private static readonly Dictionary<char, string> Degrees = new()
    {
        ['p'] = "positive",
        ['c'] = "comparative",
        ['s'] = "superlative"
    };

    /// <summary>
    /// The word classes WordNet uses, exactly as WordNetIngestService
    /// stores them. English morphology is thin enough that the word class
    /// is the whole useful parse - there's no case, no aspect, no voice to
    /// report - so these are stored and shown as plain words rather than
    /// encoded positionally like the Greek and Latin tags.
    /// </summary>
    private static readonly HashSet<string> EnglishPartsOfSpeech =
        new(StringComparer.Ordinal) { "noun", "verb", "adjective", "adverb" };

    /// <summary>
    /// The handful of Latin part-of-speech prefixes confident enough to
    /// name. Deliberately short: the lascivaroma tag vocabulary hasn't been
    /// checked against the real corpus, so anything not listed here comes
    /// back undecoded with its raw tag intact rather than guessed at.
    /// </summary>
    private static readonly Dictionary<string, string> LatinPosPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOMcom"] = "common noun",
        ["NOMpro"] = "proper noun",
        ["VER"] = "verb",
        ["ADJ"] = "adjective",
        ["ADV"] = "adverb",
        ["PRE"] = "preposition",
        ["CON"] = "conjunction",
        ["PRO"] = "pronoun",
        ["INT"] = "interjection",
        ["NUM"] = "numeral"
    };

    /// <summary>
    /// Pronoun and adverb subcategories that the two-character part-of-speech
    /// field can carry. Deliberately partial: the corpus also contains codes
    /// like "ne", "pa", "vc", "gm" and "ae" whose meanings aren't confirmed,
    /// and those fall back to the plain category from the first character -
    /// "pronoun" rather than a guessed-at refinement. Less specific is fine;
    /// wrong is not.
    /// </summary>
    private static readonly Dictionary<string, string> PosSubcategories = new(StringComparer.Ordinal)
    {
        ["pd"] = "demonstrative pronoun",
        ["pr"] = "relative pronoun",
        ["pp"] = "personal pronoun",
        ["ps"] = "possessive pronoun",
        ["pi"] = "interrogative pronoun",
        ["pk"] = "reflexive pronoun",
        ["pc"] = "reciprocal pronoun",
        ["dd"] = "demonstrative adverb",
        ["dr"] = "relative adverb",
        ["di"] = "interrogative adverb"
    };

    /// <summary>
    /// The two tag layouts this corpus actually uses, confirmed by counting
    /// them in the source XML: roughly 48,000 nine-character tags (on the
    /// token elements) and 32,000 ten-character ones (on the lemma
    /// elements). They are the same eight grammatical fields either way -
    /// the longer form just carries a two-character part of speech, so
    /// "v-sppemn-" and "v--sppemn-" are the identical analysis.
    /// </summary>
    public const int AgdtTagLength = 9;
    public const int ExtendedTagLength = 10;

    public static Parse Decode(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return new Parse { RawTag = string.Empty, IsDecoded = false };
        }

        var tag = rawTag.Trim();

        if (tag.Length == AgdtTagLength) return DecodePositional(tag, posFieldWidth: 1);
        if (tag.Length == ExtendedTagLength) return DecodePositional(tag, posFieldWidth: 2);

        // English tags are already the plain word, since WordNet classifies
        // only by word class and English inflection is too thin to warrant
        // the positional scheme the classical corpora use. Checked before
        // the Latin prefixes below because "verb" happens to start with the
        // Latin code "VER" - it would decode by coincidence, while "noun"
        // matched nothing and displayed raw. Same data, inconsistent
        // presentation; naming them here makes it deliberate.
        if (EnglishPartsOfSpeech.Contains(tag))
        {
            return new Parse
            {
                RawTag = tag,
                IsDecoded = true,
                Description = tag,
                PartOfSpeech = tag
            };
        }

        // Not positional - try the Latin part-of-speech labels. Longest
        // prefix first, so "NOMcom" isn't shadowed by a shorter "NOM" if
        // one is ever added.
        foreach (var (prefix, meaning) in LatinPosPrefixes.OrderByDescending(p => p.Key.Length))
        {
            if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return new Parse
                {
                    RawTag = tag,
                    IsDecoded = true,
                    Description = meaning,
                    PartOfSpeech = meaning
                };
            }
        }

        return new Parse { RawTag = tag, IsDecoded = false };
    }

    /// <summary>
    /// Reads the positional tag and assembles the pieces in the order a
    /// grammar actually states them, which differs by part of speech: a
    /// finite verb reads tense-voice-mood then person/number, while a noun
    /// reads gender-case-number. Assembling positionally instead would give
    /// technically-complete but unreadable output.
    ///
    /// posFieldWidth is where the eight grammatical fields begin - 1 for the
    /// nine-character form, 2 for the ten-character one. Everything after
    /// the part of speech is identical between them.
    /// </summary>
    private static Parse DecodePositional(string tag, int posFieldWidth)
    {
        // Position 1 is the signature: every genuine tag names a part of
        // speech there, and it is never "-". Length alone is far too weak a
        // test - a tag from some other vocabulary can partially collide with
        // these tables and produce a confident-looking parse that is simply
        // wrong ("NOMcom123" otherwise reads as optative middle). Requiring
        // a valid part of speech rejects those outright, and an unrecognized
        // tag showing as raw text costs the reader nothing, while a
        // fabricated parse actively misleads.
        var pos = Lookup(PartsOfSpeech, tag[0]);
        if (pos == null) return new Parse { RawTag = tag, IsDecoded = false };

        // A two-character part of speech may refine the category - "pd" is a
        // demonstrative pronoun rather than just a pronoun. Unknown
        // refinements keep the plain category rather than being guessed at.
        if (posFieldWidth == 2 && PosSubcategories.TryGetValue(tag[..2], out var refined))
        {
            pos = refined;
        }

        var f = posFieldWidth;
        var person = Lookup(Persons, tag[f]);
        var number = Lookup(Numbers, tag[f + 1]);
        var tense = Lookup(Tenses, tag[f + 2]);
        var mood = Lookup(Moods, tag[f + 3]);
        var voice = Lookup(Voices, tag[f + 4]);
        var gender = Lookup(Genders, tag[f + 5]);
        var grammaticalCase = Lookup(Cases, tag[f + 6]);
        var degree = Lookup(Degrees, tag[f + 7]);

        var sb = new StringBuilder();

        var isParticiple = mood == "participle";
        var isInfinitive = mood == "infinitive";
        var isFiniteVerb = pos == "verb" && !isParticiple && !isInfinitive;

        if (isParticiple || isInfinitive)
        {
            // "present medio-passive participle" - tense and voice qualify
            // the form itself, so they lead.
            AppendWords(sb, tense, voice, mood);

            // A participle also declines; an infinitive doesn't.
            if (isParticiple)
            {
                AppendClause(sb, gender, grammaticalCase, number);
            }
        }
        else if (isFiniteVerb)
        {
            // "aorist active indicative, 3rd person singular"
            AppendWords(sb, tense, voice, mood);
            AppendClause(sb, person, number);
        }
        else
        {
            // Nouns, adjectives, articles, pronouns: gender-case-number,
            // with degree trailing for adjectives and adverbs.
            AppendWords(sb, gender, grammaticalCase, number);

            // Positive degree is the unmarked default and adds nothing;
            // comparative and superlative are worth stating.
            if (degree is "comparative" or "superlative")
            {
                AppendClause(sb, degree);
            }

            // If nothing declined resolved, at least name the part of speech
            // so the entry isn't blank (particles, conjunctions, punctuation).
            if (sb.Length == 0 && pos != null) sb.Append(pos);
        }

        // Lead with the part of speech when it isn't already implied by the
        // words chosen above - "noun: masculine nominative singular" reads
        // better than a bare "masculine nominative singular".
        var description = sb.ToString();
        if (pos != null && !isFiniteVerb && !isParticiple && !isInfinitive && description != pos)
        {
            description = $"{pos}: {description}";
        }

        return new Parse
        {
            RawTag = tag,
            IsDecoded = description.Length > 0,
            Description = description,
            PartOfSpeech = pos
        };
    }

    private static string? Lookup(Dictionary<char, string> table, char code) =>
        table.TryGetValue(code, out var value) ? value : null;

    /// <summary>Appends space-separated parts, skipping any that didn't resolve.</summary>
    private static void AppendWords(StringBuilder sb, params string?[] parts)
    {
        foreach (var part in parts)
        {
            if (part == null) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(part);
        }
    }

    /// <summary>Appends a comma-separated clause, skipping any parts that didn't resolve.</summary>
    private static void AppendClause(StringBuilder sb, params string?[] parts)
    {
        var clause = new StringBuilder();
        foreach (var part in parts)
        {
            if (part == null) continue;
            if (clause.Length > 0) clause.Append(' ');
            clause.Append(part);
        }

        if (clause.Length == 0) return;
        if (sb.Length > 0) sb.Append(", ");
        sb.Append(clause);
    }

    // --- Search support ---------------------------------------------------

    /// <summary>
    /// The categories a morphology search can filter on, in AGDT position
    /// order. Exposed so the search UI builds itself from the same tables
    /// the decoder uses - a category can't drift between what's searchable
    /// and what's displayable.
    /// </summary>
    public static IReadOnlyList<(string Label, int Position, IReadOnlyDictionary<char, string> Options)> SearchableCategories { get; } =
        new List<(string, int, IReadOnlyDictionary<char, string>)>
        {
            ("Part of speech", 0, PartsOfSpeech),
            ("Person", 1, Persons),
            ("Number", 2, Numbers),
            ("Tense", 3, Tenses),
            ("Mood", 4, Moods),
            ("Voice", 5, Voices),
            ("Gender", 6, Genders),
            ("Case", 7, Cases),
            ("Degree", 8, Degrees)
        };

    /// <summary>
    /// Builds SQLite GLOB patterns matching tags with the given positions
    /// fixed and the rest free - selections of {0:'v', 4:'o'} become
    /// "v???o????" and "v????o????", i.e. any optative verb in either
    /// layout.
    ///
    /// Both are returned because both layouts are present in the corpus in
    /// large numbers, and a pattern built for one silently matches none of
    /// the other. The second position of the ten-character form is the
    /// part-of-speech subcategory and is never constrained - a search for
    /// "pronoun" should find demonstrative and relative pronouns alike.
    ///
    /// GLOB rather than LIKE deliberately: '?' matches exactly one
    /// character, so positions stay aligned, and GLOB is case-sensitive,
    /// so a lowercase Greek pattern can't accidentally match an uppercase
    /// Latin tag of the same length.
    /// </summary>
    public static (string NineChar, string TenChar) BuildGlobPatterns(IReadOnlyDictionary<int, char> selections)
    {
        var nine = new char[AgdtTagLength];
        for (var i = 0; i < AgdtTagLength; i++)
        {
            nine[i] = selections.TryGetValue(i, out var code) ? code : '?';
        }

        var ten = new char[ExtendedTagLength];
        ten[0] = selections.TryGetValue(0, out var posCode) ? posCode : '?';
        ten[1] = '?';
        for (var i = 1; i < AgdtTagLength; i++)
        {
            ten[i + 1] = selections.TryGetValue(i, out var code) ? code : '?';
        }

        return (new string(nine), new string(ten));
    }
}
