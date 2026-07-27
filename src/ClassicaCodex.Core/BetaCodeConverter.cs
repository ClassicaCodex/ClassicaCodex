using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// Converts Beta Code - the ASCII transliteration scheme classical
/// digitization projects have used since the 1970s - into real Unicode
/// Greek. Perseus's LSJ writes both its @key attribute and its &lt;orth&gt;
/// element text in Beta Code (i)a/ for ἰά, for example), not Unicode, so
/// without this conversion every Greek dictionary headword gets indexed
/// under a string no Unicode lookup can ever match.
///
/// Builds each character as a base letter plus Unicode combining diacritics,
/// then lets string.Normalize(FormC) fold that down to the single
/// precomposed codepoint polytonic Greek actually uses - simpler and less
/// error-prone than hand-maintaining a table of every precomposed
/// letter+breathing+accent combination.
/// </summary>
public static class BetaCodeConverter
{
    private static readonly Dictionary<char, char> BaseLetters = new()
    {
        ['a'] = 'α', ['b'] = 'β', ['g'] = 'γ', ['d'] = 'δ', ['e'] = 'ε',
        ['z'] = 'ζ', ['h'] = 'η', ['q'] = 'θ', ['i'] = 'ι', ['k'] = 'κ',
        ['l'] = 'λ', ['m'] = 'μ', ['n'] = 'ν', ['c'] = 'ξ', ['o'] = 'ο',
        ['p'] = 'π', ['r'] = 'ρ', ['s'] = 'σ', ['t'] = 'τ', ['u'] = 'υ',
        ['f'] = 'φ', ['x'] = 'χ', ['y'] = 'ψ', ['w'] = 'ω', ['v'] = 'ϝ'
    };

    // Diacritic markers, each mapped to the Unicode combining mark that
    // follows the base letter. Order emitted matches what NFC normalization
    // expects for these to fold into a single precomposed character.
    private const char SmoothBreathing = ')';
    private const char RoughBreathing = '(';
    private const char Acute = '/';
    private const char Grave = '\\';
    private const char Circumflex = '=';
    private const char IotaSubscript = '|';
    private const char Diaeresis = '+';

    private const string CombSmooth = "\u0313";
    private const string CombRough = "\u0314";
    private const string CombAcute = "\u0301";
    private const string CombGrave = "\u0300";
    private const string CombCircumflex = "\u0342";
    private const string CombIotaSub = "\u0345";
    private const string CombDiaeresis = "\u0308";

    public static string Convert(string betaCode)
    {
        if (string.IsNullOrWhiteSpace(betaCode)) return string.Empty;

        // Trailing homograph numbers (the "1" in i)a/1, meaning "the first
        // of several unrelated words spelled this way") aren't part of the
        // word - strip them before converting.
        var text = betaCode.TrimEnd("0123456789".ToCharArray());

        var sb = new StringBuilder();
        var capitalizeNext = false;

        // Beta Code writes a capital's diacritics *before* the letter (*)a =
        // Ἀ), but a combining mark can only fold onto a base that precedes
        // it. So marks seen between the * and its letter are held here and
        // emitted right after the letter, where NFC can compose them.
        var pendingCapMarks = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '*')
            {
                capitalizeNext = true;
                continue;
            }

            var lower = char.ToLowerInvariant(c);

            if (BaseLetters.TryGetValue(lower, out var greekBase))
            {
                var letter = capitalizeNext ? char.ToUpperInvariant(greekBase) : greekBase;
                sb.Append(letter);
                if (pendingCapMarks.Length > 0)
                {
                    sb.Append(pendingCapMarks);
                    pendingCapMarks.Clear();
                }
                capitalizeNext = false;
                continue;
            }

            var mark = c switch
            {
                SmoothBreathing => CombSmooth,
                RoughBreathing => CombRough,
                Acute => CombAcute,
                Grave => CombGrave,
                Circumflex => CombCircumflex,
                IotaSubscript => CombIotaSub,
                Diaeresis => CombDiaeresis,
                _ => null
            };

            if (mark != null)
            {
                // Before its letter (a pending capital) it's buffered;
                // after its letter it composes in stream order as usual.
                if (capitalizeNext) pendingCapMarks.Append(mark);
                else sb.Append(mark);
                continue;
            }

            // Anything else (spaces, punctuation Beta Code doesn't define,
            // stray markup leftovers) passes through unchanged rather than
            // being silently dropped, so a partially-unrecognized entry
            // stays readable instead of losing characters. Flush any pending
            // capital marks first so they aren't stranded.
            if (pendingCapMarks.Length > 0)
            {
                sb.Append(pendingCapMarks);
                pendingCapMarks.Clear();
            }
            capitalizeNext = false;
            sb.Append(c);
        }

        if (pendingCapMarks.Length > 0) sb.Append(pendingCapMarks);

        var composed = sb.ToString().Normalize(NormalizationForm.FormC);

        // Final sigma: Beta Code just uses 's' throughout and leaves
        // position-dependent sigma to the reader. Converting the
        // word-final one to ς is purely for display readability - lookups
        // already fold σ/ς together via WordNormalizer regardless.
        if (composed.Length > 0 && composed[^1] == 'σ')
        {
            composed = composed[..^1] + 'ς';
        }

        return composed;
    }

    // A diacritic mark sitting immediately after a base letter is the one
    // signature of Beta Code Greek that English gloss text never produces -
    // it's what tells "o(" and "lo/gos" apart from "partisan" or "(q.v.)".
    private static readonly char[] Markers =
        { SmoothBreathing, RoughBreathing, Acute, Grave, Circumflex, IotaSubscript, Diaeresis };

    private const string LeadingWrappers = "([{\u00AB\"'\u2018\u201C";
    private const string TrailingSafePunct = ".,;:\u00B7!?";

    // A closer is only stripped when its matching opener was stripped from
    // the front, so a genuine word-final breathing (o( = ὁ, a) = ἀ) is never
    // mistaken for a closing bracket and lost.
    private static readonly Dictionary<char, char> CloserToOpener = new()
    {
        [')'] = '(', [']'] = '[', ['}'] = '{',
        ['\u00BB'] = '\u00AB', ['"'] = '"', ['\''] = '\'',
        ['\u2019'] = '\u2018', ['\u201D'] = '\u201C'
    };

    // Vowel-length marks (breve / macron). Real Beta Code, but pronunciation
    // aids rather than letters - dropped so a body entry reads as words, not
    // "qli^b-i/as".
    private static readonly char[] LengthMarks = { '^', '_' };

    /// <summary>
    /// Converts the Beta Code Greek inside a mixed string - a lexicon entry
    /// whose body interleaves Beta Code Greek with English glosses - and
    /// leaves the non-Greek text exactly as it was.
    ///
    /// The whole string can't be run through <see cref="Convert"/>: every
    /// Latin letter maps to a Greek one, so "partisan" would come back
    /// "παρτισαν". Instead each whitespace-delimited token is converted only
    /// when it actually looks like Beta Code - see <see cref="LooksLikeBetaCode"/> -
    /// so English words, sense letters (A.), and bare parentheticals like
    /// "(q.v.)" pass through untouched.
    /// </summary>
    public static string ConvertMixed(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                sb.Append(text[i++]);
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            var token = text[start..i];

            sb.Append(LooksLikeBetaCode(token) ? ConvertToken(token) : token);
        }

        return sb.ToString();
    }

    private static bool IsBaseLetter(char c) => BaseLetters.ContainsKey(char.ToLowerInvariant(c));

    private static bool LooksLikeBetaCode(string token)
    {
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];

            // Diacritic right after a base letter - the reliable tell.
            if (Array.IndexOf(Markers, c) >= 0 && i > 0 && IsBaseLetter(token[i - 1]))
                return true;

            // Capitalization marker introducing the letter it capitalizes.
            if (c == '*' && i + 1 < token.Length &&
                (IsBaseLetter(token[i + 1]) || Array.IndexOf(Markers, token[i + 1]) >= 0))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Peels balanced wrapping punctuation off a Greek token before
    /// conversion and puts it back after, so "(lo/gos)," keeps its brackets
    /// as brackets instead of turning them into stray breathing marks, and
    /// so <see cref="Convert"/> sees the real word end and picks the right
    /// final sigma. Length marks inside the core are dropped.
    /// </summary>
    private static string ConvertToken(string token)
    {
        var lead = 0;
        while (lead < token.Length && LeadingWrappers.IndexOf(token[lead]) >= 0) lead++;

        var trail = token.Length;
        while (trail > lead)
        {
            var c = token[trail - 1];
            if (TrailingSafePunct.IndexOf(c) >= 0) { trail--; continue; }
            if (CloserToOpener.TryGetValue(c, out var opener) &&
                token[..lead].IndexOf(opener) >= 0) { trail--; continue; }
            break;
        }

        var core = token[lead..trail];
        if (core.Length == 0) return token;

        foreach (var mark in LengthMarks)
            if (core.IndexOf(mark) >= 0) core = core.Replace(mark.ToString(), string.Empty);

        return token[..lead] + Convert(core) + token[trail..];
    }
}
