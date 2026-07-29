using System.Globalization;
using System.Text;

namespace ClassicaCodex.UI;

/// <summary>
/// Turns polytonic Greek into a rough Latin-letter spelling an English SAPI
/// voice can actually attempt to pronounce, rather than reading each Greek
/// character by its Unicode name ("alpha, lambda, omicron...") - confirmed
/// on a real machine to be what a stock Windows voice does with Greek
/// script it has no phoneme mapping for at all.
///
/// This is not a scholarly transliteration and doesn't try to be one. The
/// goal is specifically "what spelling, read by an English voice's ordinary
/// English letter-to-sound rules, lands closer to the word than silence or
/// letter names" - not a reversible academic Romanization. Known,
/// deliberate simplifications:
///
///  - Vowel length is lost (epsilon/eta and omicron/omega both collapse to
///    "e" and "o") - representing length in a way an English voice would
///    actually read as length, rather than as an extra unwanted syllable
///    or a silently-ignored diacritic, isn't solvable without hearing real
///    output, so it's not attempted here.
///  - Pitch accent isn't represented at all - English orthography has
///    nothing that maps to it.
///  - Iota subscript is dropped rather than appended - it's frequently
///    silent in modern academic reading practice anyway, so losing it is a
///    small, defensible cost next to the complexity of getting an English
///    voice to do anything sensible with a trailing "i" it wasn't expecting.
///
/// The one piece of this that IS a genuine, well-attested pronunciation
/// rule rather than a convenience: rough breathing becomes a leading "h" on
/// the syllable it marks.
///
/// I can't hear the output of this myself - there's no Windows speech
/// engine available where this was written. Every case here was verified
/// by hand-tracing the algorithm against real words (λόγος, ἄνθρωπος,
/// ἡμέρα, οὗτος), not by listening to it. Treat this as a first attempt to
/// listen to and adjust, the same as the plain offline voice was.
/// </summary>
public static class GreekPhoneticTransliterator
{
    private const char RoughBreathing = '\u0314'; // COMBINING REVERSED COMMA ABOVE

    // Checked before single-letter mapping, since some digraphs need a
    // different Latin spelling than their letters would produce
    // individually - ου as "ou" reads naturally in English; letter-by-letter
    // (ο→o, υ→y) would give "oy", which doesn't.
    private static readonly (char First, char Second, string Latin)[] Digraphs =
    {
        ('ο', 'υ', "ou"),
        ('γ', 'γ', "ng"), ('γ', 'κ', "nk"), ('γ', 'ξ', "nx"), ('γ', 'χ', "nch"),
    };

    private static readonly Dictionary<char, string> SingleLetters = new()
    {
        ['α'] = "a", ['β'] = "b", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e",
        ['ζ'] = "z", ['η'] = "e", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k",
        ['λ'] = "l", ['μ'] = "m", ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o",
        ['π'] = "p", ['ρ'] = "r", ['σ'] = "s", ['ς'] = "s", ['τ'] = "t",
        ['υ'] = "y", ['φ'] = "ph", ['χ'] = "ch", ['ψ'] = "ps", ['ω'] = "o",
    };

    public static string ToPhoneticLatin(string greekText)
    {
        if (string.IsNullOrEmpty(greekText)) return greekText;

        // NFD splits each base letter from its combining accent/breathing
        // marks - the same approach WordNormalizer already uses - so a
        // rough-breathing mark can be found and handled before the base
        // letter is converted, regardless of which precomposed or combining
        // form the source file happened to use.
        var decomposed = greekText.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var i = 0;

        while (i < decomposed.Length)
        {
            var ch = decomposed[i];
            if (!IsGreekLetter(ch))
            {
                result.Append(ch);
                i++;
                continue;
            }

            var (marksEnd, hasRoughBreathing) = ScanMarks(decomposed, i + 1);

            var digraphMatch = TryMatchDigraph(decomposed, ch, marksEnd, hasRoughBreathing);
            if (digraphMatch != null)
            {
                result.Append(char.IsUpper(ch) ? CapitalizeFirst(digraphMatch.Value.Latin) : digraphMatch.Value.Latin);
                i = digraphMatch.Value.NextIndex;
                continue;
            }

            if (SingleLetters.TryGetValue(char.ToLowerInvariant(ch), out var latin))
            {
                var spelled = hasRoughBreathing ? "h" + latin : latin;
                result.Append(char.IsUpper(ch) ? CapitalizeFirst(spelled) : spelled);
            }
            else
            {
                result.Append(ch);
            }

            i = marksEnd;
        }

        return result.ToString();
    }

    /// <summary>Walks past every combining mark on one base letter, reporting whether rough breathing was among them.</summary>
    private static (int NextIndex, bool HasRoughBreathing) ScanMarks(string decomposed, int start)
    {
        var index = start;
        var hasRoughBreathing = false;

        while (index < decomposed.Length
               && CharUnicodeInfo.GetUnicodeCategory(decomposed[index]) == UnicodeCategory.NonSpacingMark)
        {
            if (decomposed[index] == RoughBreathing) hasRoughBreathing = true;
            index++;
        }

        return (index, hasRoughBreathing);
    }

    /// <summary>
    /// Tries to match a two-letter digraph starting at the current base
    /// letter. Rough breathing can legitimately land on either letter of a
    /// diphthong (οὗτος has it on the upsilon, "houtos") - both are checked,
    /// since dropping the second one would silently lose the "h" from
    /// exactly the demonstrative pronoun forms that turn up constantly in
    /// real Greek prose.
    /// </summary>
    private static (string Latin, int NextIndex)? TryMatchDigraph(
        string decomposed, char firstChar, int secondStart, bool firstHasRoughBreathing)
    {
        foreach (var (first, second, latin) in Digraphs)
        {
            if (char.ToLowerInvariant(firstChar) != first) continue;
            if (secondStart >= decomposed.Length) continue;
            if (char.ToLowerInvariant(decomposed[secondStart]) != second) continue;

            var (nextIndex, secondHasRoughBreathing) = ScanMarks(decomposed, secondStart + 1);
            var spelled = (firstHasRoughBreathing || secondHasRoughBreathing) ? "h" + latin : latin;
            return (spelled, nextIndex);
        }

        return null;
    }

    private static string CapitalizeFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static bool IsGreekLetter(char ch) =>
        (ch >= '\u0370' && ch <= '\u03FF') || (ch >= '\u1F00' && ch <= '\u1FFF');
}
