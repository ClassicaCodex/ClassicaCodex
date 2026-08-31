using System.Globalization;
using System.Text;

namespace ClassicaCodex.Core.Meter;

/// <summary>How long a syllable is, as far as the letters can say.</summary>
public enum Quantity
{
    /// <summary>
    /// The spelling does not decide it. A vowel before a single consonant is
    /// the common case: "amat" and "amas" look alike, the first a is short in
    /// one and long in the other, and only a dictionary or the metre can say
    /// which. Around half of all syllables land here, which is why scanning
    /// has to be a search rather than a lookup.
    /// </summary>
    Unknown = 0,

    /// <summary>Long by nature (a diphthong) or by position (two consonants after it).</summary>
    Long = 1,

    /// <summary>Short: a vowel standing immediately before another vowel.</summary>
    Short = 2
}

/// <summary>One syllable of a line, with what the spelling forces on it.</summary>
public sealed class ProsodicSyllable
{
    /// <summary>The letters this syllable was read from, for display.</summary>
    public string Text { get; init; } = string.Empty;

    public Quantity Quantity { get; init; }

    /// <summary>
    /// True when this syllable is suppressed by elision and does not count
    /// towards the metre. Kept in the list rather than removed so a reader
    /// can be shown what was elided - that is half of what makes a scansion
    /// legible.
    /// </summary>
    public bool Elided { get; init; }

    /// <summary>Which whitespace-separated word of the line it came from.</summary>
    public int WordIndex { get; init; }
}

/// <summary>
/// Turns a line of Latin into syllables, and says what their spelling forces.
///
/// This is deliberately not a scanner. It answers only the question the
/// letters can answer on their own - is this syllable long by position, short
/// before another vowel, or undecided - and leaves the rest to whatever knows
/// the shape of the line. About half the syllables in any Latin line come
/// back Unknown, because Perseus prints no vowel-length marks and a vowel
/// before a single consonant is as often long as short.
///
/// That division is the whole design. A prosody layer that guessed at the
/// undecided cases would be a dictionary of vowel quantities pretending to be
/// a rule, and would be wrong silently. Handing the metre a set of
/// constraints instead lets the line's own shape resolve them, and lets
/// whatever it cannot resolve be reported as unresolved.
/// </summary>
public static class LatinProsody
{
    private const string Vowels = "aeiouy";

    private const char CombiningMacron = '\u0304';
    private const char CombiningBreve = '\u0306';

    /// <summary>
    /// Always a diphthong when these two letters meet. The other candidates
    /// are not: "eu" is one syllable in "heu" and two in "deus", "ui" is one
    /// in "cui" and two in "fui", "ei" is one in "deinde" and two in "rei".
    /// Those are lexical rather than orthographic, so they are listed by word
    /// below rather than guessed at here.
    /// </summary>
    private static readonly string[] AlwaysDiphthongs = { "ae", "oe", "au" };

    /// <summary>
    /// The words where "eu", "ei" or "ui" really is one syllable. Short
    /// enough to enumerate, and enumerating is the only honest option: no
    /// spelling rule separates "cui" from "fui".
    ///
    /// Greek names in -eus (Orpheus, Theseus, Tydeus) are deliberately
    /// absent. They are two syllables in some positions and three in others -
    /// the same name scans both ways within one poem - so a fixed answer
    /// would be wrong about half the time. Left as two vowels, which makes
    /// the first of them short before a vowel; where the poet wanted the
    /// diphthong the line will fail to scan and be counted as a failure
    /// rather than quietly mis-scanned.
    /// </summary>
    private static readonly HashSet<string> LexicalDiphthongWords = new(StringComparer.Ordinal)
    {
        "heu", "eheu", "seu", "neu", "ceu", "heus",
        "cui", "huic", "hui",
        "deinde", "deinceps", "dein", "hei"
    };

    private const string Mutes = "bcdgptf";
    private const string Liquids = "lr";

    /// <summary>
    /// Stems where the u of "su" is a consonant, spelled from the s.
    ///
    /// Latin has three places where a written u before a vowel is no vowel at
    /// all: after q always, after g when an n comes first, and after s in
    /// this handful of words. The first two are rules and are handled where
    /// the letters are read; this one is a list, because nothing separates
    /// the swa- of suauis from the su-a of suus, suam, suorum - and those are
    /// far too common to put a branch on.
    ///
    /// Listed by stem rather than by word so an inflection or a prefix comes
    /// free: persuadeo matches suad- as readily as suadeo does. "suas" is
    /// deliberately absent even though suasit belongs here, because suas is
    /// also the accusative plural of suus and is two syllables; the perfect
    /// stems that are safe to name are spelled out instead.
    /// </summary>
    private static readonly string[] ConsonantalSuStems =
    {
        "suad", "suav", "suau", "suasi", "suasu", "suasa", "suaso",
        "suesc", "suet", "suev"
    };

    /// <summary>
    /// Words whose ae or oe is two syllables rather than a diphthong - and
    /// words spelled the same way whose ae or oe is a diphthong after all.
    ///
    /// aer is the Greek aer, three letters and two syllables; aerumna and
    /// aeratus come from aes and have the diphthong. poeta is a Greek poet
    /// and two vowels; poena is Latin and one. The spellings are identical
    /// for as far as any rule can see, so both readings are offered here and
    /// the line settles it.
    ///
    /// This is why the entry is a prefix and not a word list. A list would
    /// have to be right about which of two indistinguishable words is meant,
    /// which is a question about vocabulary, not about letters.
    /// </summary>
    private static readonly string[] PossibleDiaeresisPrefixes = { "aer", "poe" };

    /// <summary>
    /// Syllabifies a line and marks what the spelling decides.
    ///
    /// Elided syllables are marked, not dropped: the caller needs the
    /// metrical sequence without them, and a reader needs to see them.
    /// </summary>
    public static IReadOnlyList<ProsodicSyllable> Syllabify(string line) =>
        Syllabifications(line)[0];

    /// <summary>
    /// How many readings of one line's spelling will be offered at most. Two
    /// to the sixth, so a line with more than six ambiguous letters is read
    /// in fewer ways than it strictly has - reached by two lines in fifteen
    /// thousand, and the cap is what keeps a pathological line from costing
    /// a thousand times what an ordinary one does.
    /// </summary>
    private const int MaxAmbiguousLetters = 6;

    /// <summary>
    /// Every way this line's letters can be read, the plainest one first.
    ///
    /// There is one ambiguity worth branching on. A u before a vowel and
    /// after l, r or n is sometimes the vowel of cru-or and sometimes the v
    /// of sil-va, and the spelling of a critical edition - which writes both
    /// as u - cannot tell them apart. Neither can a rule: fluuius and uoluit
    /// have the same letters in the same places and different syllable
    /// counts.
    ///
    /// So both readings are produced and the metre is left to choose, the
    /// same way it chooses between a dactyl and a spondee. A word list would
    /// be the alternative, and would be a list of every Latin verb in -luere
    /// and -ruere against every noun in -luus and -ruus, wrong at its edges
    /// and silent about being wrong.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ProsodicSyllable>> Syllabifications(string line)
    {
        var words = Tokenize(line);
        if (words.Count == 0)
        {
            return new[] { (IReadOnlyList<ProsodicSyllable>)Array.Empty<ProsodicSyllable>() };
        }

        // A first pass taking the plain reading everywhere, which both
        // produces that reading and records where the choices are.
        var ambiguous = new List<Choice>();
        var plain = Build(words, null, ambiguous);

        var choices = Math.Min(ambiguous.Count, MaxAmbiguousLetters);
        if (choices == 0) return new[] { plain };

        var readings = new List<IReadOnlyList<ProsodicSyllable>> { plain };

        for (var mask = 1; mask < 1 << choices; mask++)
        {
            var taken = new HashSet<Choice>();
            for (var bit = 0; bit < choices; bit++)
            {
                if ((mask & (1 << bit)) != 0) taken.Add(ambiguous[bit]);
            }

            readings.Add(Build(words, taken, null));
        }

        return readings;
    }

    /// <summary>
    /// Segments, elides and decides one reading of the line.
    ///
    /// Each word becomes an alternating run of consonant clusters and vowel
    /// nuclei. Quantity is settled afterwards, over the whole line, because a
    /// cluster that closes a syllable often starts in the next word: "et
    /// tuba" makes the "et" long, and no word-at-a-time pass can see that.
    /// </summary>
    private static IReadOnlyList<ProsodicSyllable> Build(
        List<Word> words,
        HashSet<Choice>? taken,
        List<Choice>? recordAmbiguous)
    {
        var units = new List<Unit>();
        for (var w = 0; w < words.Count; w++)
        {
            Segment(words[w], w, units, taken, recordAmbiguous);
        }

        MarkElisions(units, words, taken, recordAmbiguous);
        return Assemble(units);
    }

    /// <summary>
    /// Letters only, lowercased, with the spelling conventions that are
    /// really one letter folded together.
    ///
    /// "j" is a printing convention for consonantal i and never appears in
    /// Perseus' own Latin, but it does appear in text pasted in from
    /// elsewhere. "v" is kept distinct from "u": where it is written it is
    /// always a consonant, which is exactly the information a text printing
    /// "uirumque" has thrown away. Nothing here recovers that case - a text
    /// with no v at all will scan worse, and should be counted separately
    /// rather than silently patched.
    ///
    /// Macrons and breves are honoured where a text carries them. Almost no
    /// Perseus Latin does, but a school text or a hand-typed line might, and
    /// discarding a marked quantity in order to re-derive it from position
    /// would be perverse.
    /// </summary>
    private static List<Word> Tokenize(string line)
    {
        var words = new List<Word>();
        var letters = new StringBuilder();
        var marks = new List<Quantity>();
        var capitalised = false;

        void Flush()
        {
            if (letters.Length == 0) return;
            words.Add(new Word(letters.ToString(), marks.ToArray(), capitalised));
            letters.Clear();
            marks.Clear();
            capitalised = false;
        }

        foreach (var raw in line.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
            {
                // A combining macron or breve belongs to the letter just
                // written, which is the last entry in marks.
                if (marks.Count > 0)
                {
                    if (raw == CombiningMacron) marks[^1] = Quantity.Long;
                    else if (raw == CombiningBreve) marks[^1] = Quantity.Short;
                }

                continue;
            }

            // A capital V is the letter u. Roman capitals had no separate U,
            // so a text setting a word in capitals - or merely capitalising
            // the first letter of a line - writes V for both the vowel and
            // the consonant: "Vt" is ut, "NOBISCVM DEVS" is nobiscum deus,
            // and "Virtus" is uirtus. Folding it to u and letting the
            // consonantal-u rules decide gets all three right, and gets a
            // modern text setting "Volvere" right too, because an initial u
            // before a vowel is consonantal either way.
            //
            // Lowercase v is left alone. Where a text bothers to write it,
            // it has said something this would throw away.
            var c = raw == 'V' ? 'u' : char.ToLowerInvariant(raw);
            if (c == 'j') c = 'i';

            if (c >= 'a' && c <= 'z')
            {
                // Whether the word is a proper name, near enough. Three of
                // the exceptions this scanner has to allow for live almost
                // entirely in Greek and Hebrew names, and a capital is the
                // only mark the text puts on one. It over-reports at the
                // start of a line, where every word is capitalised - which
                // costs a branch and never a wrong answer.
                if (letters.Length == 0) capitalised = char.IsUpper(raw);

                letters.Append(c);
                marks.Add(Quantity.Unknown);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return words;
    }

    private sealed record Word(string Letters, Quantity[] Marks, bool Capitalised);

    /// <summary>
    /// One place where the letters can be read two ways, and which reading
    /// the plain pass took.
    ///
    /// A choice held in the chosen set means "take the other one". The plain
    /// reading is always the empty set, so it is the first one offered and
    /// nothing about it changed when these were added.
    /// </summary>
    private readonly record struct Choice(int Word, int Letter, ChoiceKind Kind);

    private enum ChoiceKind
    {
        /// <summary>A u after l, r or n: the v of sil-ua, or the vowel of cru-or.</summary>
        ConsonantalU,

        /// <summary>Two vowels read as one syllable: Pen-theus against Pen-the-us.</summary>
        Synizesis,

        /// <summary>An initial i read as a vowel: I-ulus against Ju-lus.</summary>
        VocalicI,

        /// <summary>An elision the poet declined to make.</summary>
        Hiatus,

        /// <summary>An ae or oe read as two syllables: a-er against aer.</summary>
        Diaeresis
    }

    /// <summary>A vowel nucleus, or the consonants between two of them.</summary>
    private sealed class Unit
    {
        public bool IsNucleus;
        public string Text = string.Empty;

        /// <summary>Consonant units only: the letters that count for position.</summary>
        public string Consonants = string.Empty;

        public bool IsDiphthong;
        public Quantity Marked = Quantity.Unknown;
        public int WordIndex;

        /// <summary>Nuclei only: where in its word the vowel started.</summary>
        public int LetterIndex;

        public bool Elided;

        /// <summary>Set on the trailing -m that goes with an elided vowel.</summary>
        public bool Suppressed;

        /// <summary>
        /// Set where a vowel stands before another vowel and is not shortened
        /// by it. The first vowel of a diaeresis is the case: the a of aer is
        /// long and the o of poeta is short, and both stand in front of a
        /// vowel, so the rule that would otherwise decide them decides
        /// nothing here.
        /// </summary>
        public bool NoCorreption;
    }

    private static void Segment(
        Word word,
        int wordIndex,
        List<Unit> units,
        HashSet<Choice>? taken,
        List<Choice>? recordAmbiguous)
    {
        var s = word.Letters;
        var i = 0;

        // Whether the last thing emitted for this word was a vowel. The raw
        // letter before is not the same question and gives the wrong answer:
        // in "iuuenis" the u at position 1 follows the letter i, but that i
        // was consonantal, so the u is the vowel of ju- and not the v of -ve-.
        var afterVowel = false;

        while (i < s.Length)
        {
            var c = s[i];

            if (Vowels.IndexOf(c) >= 0)
            {
                // i and u are each written as a vowel and each sometimes a
                // consonant - the letters j and v are a later invention, and
                // a critical edition of a Latin text usually declines to use
                // them. So "uoluit" is volvit, "iuuenis" is juvenis, and a
                // scanner that reads every u as a vowel counts six syllables
                // in a word that has three.
                //
                // Both are consonantal in the same two places: at the start
                // of a word before a vowel (uita, iam) and standing between
                // two vowels (nouus, maior). They differ in weight. An
                // intervocalic i counts double and closes the syllable in
                // front of it - which is why Troiae opens the Aeneid with a
                // long syllable - while an intervocalic v is a single
                // consonant and closes nothing.
                //
                // Not caught: a v after l, r or n, as in soluere, seruus,
                // silua. There the letters are genuinely ambiguous - fluuius
                // and cruor have a real vowel in the same position - and no
                // rule separates them. Those words are read with a vowel too
                // many, and the lines holding them fail to scan rather than
                // scanning wrongly.
                // A u before a vowel and after l, r or n could be either the
                // v of silua or the vowel of cruor. Recorded rather than
                // decided; the caller offers both readings to the metre.
                // The su of suauis, which behaves exactly as qu does - and
                // that includes counting as one consonant rather than two.
                // The s is already down; the u simply goes, adding nothing,
                // or "tibi suauis" would close the -bi that has to stay open.
                if (c == 'u' && i > 0 && s[i - 1] == 's' && IsConsonantalSu(s, i))
                {
                    i++;
                    afterVowel = false;
                    continue;
                }

                if (c == 'u' && !afterVowel && i > 0 && i + 1 < s.Length
                    && "lrn".IndexOf(s[i - 1]) >= 0 && Vowels.IndexOf(s[i + 1]) >= 0)
                {
                    var choice = new Choice(wordIndex, i, ChoiceKind.ConsonantalU);
                    recordAmbiguous?.Add(choice);

                    if (taken?.Contains(choice) == true)
                    {
                        Append(units, "u", wordIndex);
                        i++;
                        afterVowel = false;
                        continue;
                    }
                }

                // An initial i before a vowel, in a name. Iulus is Ju-lus in
                // one line of the Aeneid and I-u-lus in the next, and the
                // same is true of half the Greek and Hebrew names the poets
                // borrowed - so the reading is the poet's, not the
                // spelling's, and both are offered.
                //
                // Names whose vowel is not in doubt are settled in
                // VocalicInitialI above and never reach this, because a
                // branch that is always taken the same way is only a slower
                // way of being right.
                if (c == 'i' && i == 0 && word.Capitalised
                    && i + 1 < s.Length && Vowels.IndexOf(s[i + 1]) >= 0
                    && !VocalicInitialI.Contains(s))
                {
                    var choice = new Choice(wordIndex, i, ChoiceKind.VocalicI);
                    recordAmbiguous?.Add(choice);

                    if (taken?.Contains(choice) == true)
                    {
                        units.Add(new Unit
                        {
                            IsNucleus = true,
                            Text = "i",
                            Marked = word.Marks[i],
                            WordIndex = wordIndex,
                            LetterIndex = i
                        });
                        i++;
                        afterVowel = true;
                        continue;
                    }
                }

                // Synizesis: two vowels run together into one syllable.
                // Pentheus is Pen-theus where the letters say Pen-the-us, and
                // Orpheus, Theseus, Tydeus and the rest of the Greek names in
                // -eus do the same - but not reliably, and not always in the
                // same poem, which is why this is a branch and not a rule.
                //
                // Confined to an e before another vowel in a capitalised
                // word. That is where the whole -eus declension lives -
                // Penthea, Orphei, Theseo - and keeping it there is what
                // stops "deus" and "meus", which do not do this, from
                // doubling the readings of every line they appear in.
                if (c == 'e' && word.Capitalised
                    && i + 1 < s.Length && Vowels.IndexOf(s[i + 1]) >= 0
                    && DiphthongLength(s, i) == 1)
                {
                    var choice = new Choice(wordIndex, i, ChoiceKind.Synizesis);
                    recordAmbiguous?.Add(choice);

                    if (taken?.Contains(choice) == true)
                    {
                        units.Add(new Unit
                        {
                            IsNucleus = true,
                            Text = s.Substring(i, 2),
                            IsDiphthong = true,
                            Marked = word.Marks[i],
                            WordIndex = wordIndex,
                            LetterIndex = i
                        });
                        i += 2;
                        afterVowel = true;
                        continue;
                    }
                }

                if (c is 'i' or 'u' && IsConsonantal(word, s, i, afterVowel))
                {
                    units.Add(new Unit
                    {
                        Consonants = c == 'i' && afterVowel ? "ii" : c.ToString(),
                        WordIndex = wordIndex
                    });
                    i++;
                    afterVowel = false;
                    continue;
                }

                var length = DiphthongLength(s, i);
                var split = false;

                // An ae or oe that might be two vowels - aer, poeta - against
                // the same letters that might be one - aerumna, poena.
                if (length == 2 && CanBeDiaeresis(s, i))
                {
                    var choice = new Choice(wordIndex, i, ChoiceKind.Diaeresis);
                    recordAmbiguous?.Add(choice);

                    if (taken?.Contains(choice) == true)
                    {
                        length = 1;
                        split = true;
                    }
                }

                units.Add(new Unit
                {
                    IsNucleus = true,
                    Text = s.Substring(i, length),
                    IsDiphthong = length == 2,
                    Marked = word.Marks[i],
                    WordIndex = wordIndex,
                    LetterIndex = i,
                    NoCorreption = split
                });
                i += length;
                afterVowel = true;
                continue;
            }

            // qu is one consonant and its u is no vowel at all; so is the gu
            // of "sanguis" and "lingua", where an n comes first.
            if ((c == 'q' || (c == 'g' && i > 0 && s[i - 1] == 'n'))
                && i + 2 < s.Length && s[i + 1] == 'u' && Vowels.IndexOf(s[i + 2]) >= 0)
            {
                Append(units, c.ToString(), wordIndex);
                i += 2;
                afterVowel = false;
                continue;
            }

            // h is written and not pronounced: it neither closes a syllable
            // nor blocks an elision. It does not make the letter after it
            // post-vocalic either, so afterVowel is left alone.
            if (c == 'h')
            {
                i++;
                continue;
            }

            // x and z are double consonants and close the syllable before
            // them on their own.
            Append(units, c is 'x' or 'z' ? new string(c, 2) : c.ToString(), wordIndex);
            i++;
            afterVowel = false;
        }
    }

    private static void Append(List<Unit> units, string consonants, int wordIndex)
    {
        if (units.Count > 0 && !units[^1].IsNucleus && units[^1].WordIndex == wordIndex)
        {
            units[^1].Consonants += consonants;
            return;
        }

        units.Add(new Unit { Consonants = consonants, WordIndex = wordIndex });
    }

    /// <summary>
    /// Words opening with an i that is a vowel despite a vowel following it.
    ///
    /// Hebrew names taken into Latin through Greek keep their own syllable
    /// count, and the Christian poets scan them accordingly: Juvencus needs
    /// three syllables in Iesus, not two, and lines carrying the name are a
    /// syllable short without it. The list is short because the evidence for
    /// each entry has to be a line that will not otherwise scan.
    /// </summary>
    private static readonly HashSet<string> VocalicInitialI = new(StringComparer.Ordinal)
    {
        "iesus", "iesu", "iesum", "iesui", "iesuque"
    };

    /// <summary>
    /// Prefixes that leave the letter after them behaving as if it began a
    /// word, which for an i before a vowel means behaving as a consonant.
    ///
    /// coniunx is con + iunx and is read con-junx, two syllables; iniuria is
    /// in + iuria and is read in-ju-ri-a. Without this the i is taken for a
    /// vowel because a consonant stands in front of it, the word gains a
    /// syllable, and the line stops scanning. coniux and coniuge alone
    /// account for a run of the failures in Juvencus.
    ///
    /// Longest match wins, so "in" does not claim a word that begins
    /// "inter". The rule fires only when an i and then a vowel follow, which
    /// keeps it away from inanis, exire and the many compounds where the i is
    /// simply the stem's own vowel.
    /// </summary>
    private static readonly string[] CompoundPrefixes =
    {
        "circum", "trans", "inter", "prae", "con", "dis", "per", "sub",
        "ab", "ad", "de", "ex", "in", "ob", "re", "se"
    };

    private static bool IsConsonantal(Word word, string s, int i, bool afterVowel)
    {
        if (i + 1 >= s.Length || Vowels.IndexOf(s[i + 1]) < 0) return false;
        if (i == 0) return !(s[i] == 'i' && VocalicInitialI.Contains(word.Letters));
        if (afterVowel) return true;
        return s[i] == 'i' && FollowsCompoundPrefix(s, i);
    }

    private static bool FollowsCompoundPrefix(string s, int i)
    {
        foreach (var prefix in CompoundPrefixes)
        {
            if (prefix.Length != i) continue;
            if (string.CompareOrdinal(s, 0, prefix, 0, i) == 0) return true;
        }

        return false;
    }

    private static bool IsConsonantalSu(string s, int i)
    {
        foreach (var stem in ConsonantalSuStems)
        {
            if (i - 1 + stem.Length > s.Length) continue;
            if (string.CompareOrdinal(s, i - 1, stem, 0, stem.Length) == 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the two vowels at this position are one of the pairs that a
    /// handful of words read separately. The pair must open the word, or open
    /// it after a single consonant - the position it holds in aer and in
    /// poeta.
    /// </summary>
    private static bool CanBeDiaeresis(string s, int i)
    {
        if (i + 1 >= s.Length) return false;

        var pair = s.Substring(i, 2);
        if (pair != "ae" && pair != "oe") return false;

        foreach (var prefix in PossibleDiaeresisPrefixes)
        {
            if (s.Length < prefix.Length || i + 2 > prefix.Length) continue;
            if (string.CompareOrdinal(s, 0, prefix, 0, prefix.Length) != 0) continue;

            // And the pair has to be the one inside the prefix: the "ae" at
            // the front of aer, the "oe" after the p of poe.
            if (string.CompareOrdinal(prefix, i, pair, 0, 2) == 0) return true;
        }

        return false;
    }

    private static int DiphthongLength(string s, int i)
    {
        if (i + 1 >= s.Length) return 1;
        var pair = s.Substring(i, 2);

        // "ae" and "oe" are two syllables in a handful of words - aer, poeta,
        // and their relatives - where a printed text marks a diaeresis this
        // one has already stripped. Left as diphthongs deliberately: the
        // alternative is a word list that would be wrong more often than the
        // rule it replaced.
        if (Array.IndexOf(AlwaysDiphthongs, pair) >= 0) return 2;

        if (pair is "eu" or "ei" or "ui" && LexicalDiphthongWords.Contains(s)) return 2;
        return 1;
    }

    /// <summary>
    /// Suppresses every syllable that elides into the next word.
    ///
    /// A word ending in a vowel, a diphthong, or a vowel plus -m loses that
    /// syllable when the next word begins with a vowel or with h. The
    /// consonants before it stay: "multum ille" is three syllables,
    /// mul-til-le, so the t of multum is still there closing a syllable even
    /// though the um it belonged to has gone.
    ///
    /// Prodelision - "amata est" read as amatast rather than amat' est - is
    /// treated as ordinary elision. The two disagree about which vowel is
    /// lost and agree about everything the metre can see: one syllable fewer,
    /// ending in the same consonants.
    /// </summary>
    private static void MarkElisions(
        List<Unit> units,
        List<Word> words,
        HashSet<Choice>? taken,
        List<Choice>? recordAmbiguous)
    {
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (!unit.IsNucleus) continue;

            // The nucleus must be last in its word, give or take a final -m.
            var trailingM = -1;
            var next = i + 1;

            if (next < units.Count && !units[next].IsNucleus
                && units[next].WordIndex == unit.WordIndex)
            {
                if (units[next].Consonants != "m") continue;
                trailingM = next;
                next++;
            }

            if (next >= units.Count) continue;
            if (units[next].WordIndex == unit.WordIndex) continue;

            // The next word must open with a vowel, and its first unit being
            // a nucleus is that test: an initial h was dropped at
            // segmentation and an initial consonantal i became a consonant
            // unit, so "iam" correctly blocks elision and "hora" correctly
            // does not.
            if (!units[next].IsNucleus) continue;

            // Hiatus: the elision is available and the poet declines it, so
            // the vowel stands. Virgil does it in "Dardanio Anchisae" and
            // "dona Aeneae", and both show the pattern - it is overwhelmingly
            // a Greek name that gets the courtesy, which is the only handle
            // there is on something the letters cannot show at all.
            //
            // So the branch is offered where a capitalised word follows and
            // nowhere else. Offering it at every elision would be more
            // faithful and useless: there are five thousand elisions in the
            // Aeneid and a line holding four of them would be read sixteen
            // ways before anything else was decided.
            if (words[units[next].WordIndex].Capitalised)
            {
                var choice = new Choice(unit.WordIndex, unit.LetterIndex, ChoiceKind.Hiatus);
                recordAmbiguous?.Add(choice);
                if (taken?.Contains(choice) == true) continue;
            }

            unit.Elided = true;
            if (trailingM >= 0) units[trailingM].Suppressed = true;
        }
    }

    /// <summary>
    /// Walks the units and decides each nucleus from the consonants that
    /// follow it - which may run on into the next word.
    /// </summary>
    private static List<ProsodicSyllable> Assemble(List<Unit> units)
    {
        var result = new List<ProsodicSyllable>();

        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (!unit.IsNucleus) continue;

            if (unit.Elided)
            {
                result.Add(new ProsodicSyllable
                {
                    Text = unit.Text,
                    Quantity = Quantity.Unknown,
                    Elided = true,
                    WordIndex = unit.WordIndex
                });
                continue;
            }

            var cluster = new StringBuilder();
            var clusterWord = -1;
            var oneWordCluster = true;
            var nextNucleus = -1;

            for (var j = i + 1; j < units.Count; j++)
            {
                if (units[j].IsNucleus)
                {
                    // An elided vowel is not there to stop the cluster.
                    if (units[j].Elided) continue;
                    nextNucleus = j;
                    break;
                }

                if (units[j].Suppressed || units[j].Consonants.Length == 0) continue;

                if (clusterWord < 0) clusterWord = units[j].WordIndex;
                else if (clusterWord != units[j].WordIndex) oneWordCluster = false;

                cluster.Append(units[j].Consonants);
            }

            result.Add(new ProsodicSyllable
            {
                Text = unit.Text,
                Quantity = Decide(unit, cluster.ToString(), oneWordCluster, nextNucleus, units),
                WordIndex = unit.WordIndex
            });
        }

        return result;
    }

    /// <summary>Whether nothing further in this unit's word follows it.</summary>
    private static bool IsWordFinal(List<Unit> units, int index)
    {
        for (var i = index + 1; i < units.Count; i++)
        {
            if (units[i].WordIndex != units[index].WordIndex) return true;
            if (units[i].Consonants.Length > 0 || units[i].IsNucleus) return false;
        }

        return true;
    }

    private static Quantity Decide(
        Unit unit, string cluster, bool clusterInOneWord, int nextNucleus, List<Unit> units)
    {
        if (unit.Marked != Quantity.Unknown) return unit.Marked;
        if (unit.IsDiphthong) return Quantity.Long;

        if (cluster.Length >= 2)
        {
            // Mute plus liquid is the one cluster that need not close the
            // syllable in front of it: the poet may take it either way, and
            // Virgil takes it both.
            //
            // What matters is that the two consonants belong to one word, not
            // that they belong to the vowel's word. "fonte fluentes" leaves
            // the -te short, because fl- opens the next word whole and the
            // syllable before it stays open; "ad ripam" does not, because
            // there the d and the r are in different words and the syllable
            // closes on the d. Requiring the cluster to sit in the vowel's
            // own word conflated the two and forced a long syllable wherever
            // a word began with cl-, cr-, pr-, tr- or fl-, which in this
            // corpus is often enough to be the single largest source of lines
            // that would not scan.
            if (cluster.Length == 2 && clusterInOneWord
                && Mutes.IndexOf(cluster[0]) >= 0 && Liquids.IndexOf(cluster[1]) >= 0)
            {
                return Quantity.Unknown;
            }

            return Quantity.Long;
        }

        if (cluster.Length == 1) return Quantity.Unknown;

        // Nothing between this vowel and the next: vocalis ante vocalem
        // corripitur, a vowel before a vowel is shortened. Only within a
        // word - across a word boundary the first vowel would have elided,
        // and where it did not this is hiatus, which decides nothing.
        if (nextNucleus >= 0 && units[nextNucleus].WordIndex == unit.WordIndex)
        {
            // Except at the end of a word in -ai, where the rule cannot be
            // trusted either way. The archaic first-declension genitive is
            // -aï with a long a - militiai, patriai, animai, which Lucretius
            // uses constantly - and a Greek nominative plural in -ai is short
            // in the same position. Nothing in the spelling separates them.
            //
            // So this returns Unknown rather than trading one wrong certainty
            // for another. That is the whole point of the distinction: Short
            // here was not a guess that sometimes missed, it was an assertion
            // that eliminated the correct scansion outright, and a line
            // holding one of these could not scan at all.
            if (unit.Text == "a" && units[nextNucleus].Text == "i"
                && IsWordFinal(units, nextNucleus))
            {
                return Quantity.Unknown;
            }

            // The first half of a diaeresis, for the same reason: long in
            // aer, short in poeta, and standing before a vowel in both.
            if (unit.NoCorreption) return Quantity.Unknown;

            return Quantity.Short;
        }

        return Quantity.Unknown;
    }
}
