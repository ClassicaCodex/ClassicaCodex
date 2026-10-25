using System.Globalization;
using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>A candidate that resolved to a real passage, and the rationale words its line does not contain.</summary>
internal sealed record VerifiedEcho(TextNode Node, EchoCandidate Candidate, IReadOnlyList<string> UnsupportedWords);

/// <summary>What came back from the model once it had been checked against the work it was shown.</summary>
internal sealed record EchoVerification(IReadOnlyList<VerifiedEcho> Verified, int UnresolvedCount)
{
    public int UnsupportedCount => Verified.Count(v => v.UnsupportedWords.Count > 0);
}

/// <summary>
/// The honesty checks behind Cross-Language Echo, apart from the window so
/// they can be read and tested on their own. The logic is the Windows
/// CrossLanguageEchoForm's, step for step.
///
/// Two checks, because a model asked for subtle cross-language echoes is
/// exactly where a wrong answer sounds as confident as a right one. First,
/// every citation it reports is looked up in the comparison work's real
/// passages; one that resolves to nothing is dropped and counted, never shown
/// as if it were genuine. Second, the words a rationale offers as evidence
/// are looked for in the line it cites - flagged when absent, never dropped.
/// </summary>
internal static class CrossLanguageEchoCheck
{
    /// <summary>
    /// The comparison text's cap - well under either model's real context
    /// limit. This keeps one request's cost and latency predictable rather than
    /// working round an actual ceiling. Most single works fit easily; the rare
    /// one that does not gets an honest note about how far the search reached,
    /// not a silent cut.
    /// </summary>
    public const int MaxComparisonChars = 250_000;

    /// <summary>
    /// The "[ref] text" block the prompt asks the model to cite back against,
    /// stopping at <see cref="MaxComparisonChars"/> and saying where, rather
    /// than sending part of a work with no sign anything was left out.
    /// </summary>
    public static (string TaggedText, string? TruncatedAtRef) BuildTaggedComparisonText(IReadOnlyList<TextNode> nodes)
    {
        var builder = new StringBuilder();
        string? lastIncludedRef = null;

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text)) continue;

            // The stored reference, deliberately - see GeminiTranslationService's
            // note on the batch prompt. Every candidate that comes back is
            // checked against a real passage by this reference, so what goes
            // out has to be what a lookup matches.
            var line = $"[{node.CitationRef}] {node.Text}\n";
            if (builder.Length + line.Length > MaxComparisonChars) return (builder.ToString(), lastIncludedRef);

            builder.Append(line);
            lastIncludedRef = node.CitationRef;
        }

        return (builder.ToString(), null);
    }

    /// <summary>
    /// The citation check: each reference the model returned, looked up
    /// exactly against the comparison work's own passages - the ones it was
    /// just shown, not a fuzzy match across editions. Anything that does not
    /// resolve is counted and left out.
    /// </summary>
    public static EchoVerification Verify(IReadOnlyList<EchoCandidate> candidates, IReadOnlyList<TextNode> comparisonNodes, string sourceText)
    {
        var byRef = new Dictionary<string, TextNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in comparisonNodes) byRef.TryAdd(node.CitationRef, node);

        var verified = new List<VerifiedEcho>();
        var unresolved = 0;

        foreach (var candidate in candidates)
        {
            // The corpus goes out as "[ref] text" and the prompt asks for the
            // reference "exactly as tagged above", so a model that complies
            // returns "[1.1]" where this index holds "1.1". Matching verbatim
            // made a correct response read as entirely unresolved - the better
            // the model followed the instruction, the worse it looked.
            var citation = candidate.CitationRef.Trim().TrimStart('[').TrimEnd(']');
            if (byRef.TryGetValue(citation, out var node))
                verified.Add(new VerifiedEcho(node, candidate, UnsupportedQuotedWords(candidate.Rationale, node.Text, sourceText)));
            else
                unresolved++;
        }

        return new EchoVerification(verified, unresolved);
    }

    /// <summary>
    /// The status line after a search, part by part as the Windows window
    /// writes it: how many survived, how many are flagged and why, how many
    /// were dropped, and how far into the work the search reached.
    /// </summary>
    public static string Summary(EchoVerification result, string? truncatedAtRef)
    {
        var parts = new List<string>
        {
            result.Verified.Count == 0 ? "No verified echoes found." : $"{result.Verified.Count} verified candidate(s)."
        };

        if (result.UnsupportedCount > 0)
        {
            parts.Add($"{result.UnsupportedCount} marked (!) - the rationale quotes a word that isn't in the line it " +
                      "cites. Often the reasoning belongs to a nearby line; worth reading before trusting it.");
        }

        if (result.UnresolvedCount > 0)
        {
            parts.Add($"{result.UnresolvedCount} citation(s) Gemini mentioned didn't match anything in this work and " +
                      "aren't shown.");
        }

        if (truncatedAtRef != null)
            parts.Add($"Only compared through [{truncatedAtRef}] - the work is longer than this tool sends in one request.");

        return string.Join(" ", parts);
    }

    /// <summary>The flag a row carries when its rationale quotes words its line lacks, or null.</summary>
    public static string? Flag(IReadOnlyList<string> missing) =>
        missing.Count == 0
            ? null
            : $"(!) {string.Join(", ", missing.Take(3))} " + (missing.Count == 1 ? "is" : "are") + " not in this line";

    /// <summary>
    /// Latin letters for the Greek ones, close enough to recognise a word by.
    /// Not a reading transliteration - no breathings, no vowel length, no iota
    /// subscript - because the only question is whether a word the rationale
    /// wrote in Latin letters is the same word as one in the line it cites.
    /// </summary>
    private static readonly Dictionary<char, string> GreekToLatin = new()
    {
        ['α'] = "a", ['β'] = "b", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e",
        ['ζ'] = "z", ['η'] = "e", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k",
        ['λ'] = "l", ['μ'] = "m", ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o",
        ['π'] = "p", ['ρ'] = "r", ['σ'] = "s", ['ς'] = "s", ['τ'] = "t",
        ['υ'] = "u", ['φ'] = "ph", ['χ'] = "ch", ['ψ'] = "ps", ['ω'] = "o"
    };

    /// <summary>
    /// Marks that only a transliterated Greek word is likely to carry. A quoted
    /// Latin-script word without one is taken for the rationale's own English -
    /// a gloss like 'remember', or a source-language dictionary form like
    /// 'muna' - and left alone: neither is a claim about the Greek line, and
    /// flagging them is noise.
    /// </summary>
    private static readonly string[] TransliterationMarks = { "th", "ph", "ch", "ps", "rh" };
    private static readonly string[] TransliterationEndings = { "os", "on", "ai", "ei", "oi", "eus", "ato" };

    private static string Transliterate(string normalized)
    {
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized) sb.Append(GreekToLatin.TryGetValue(c, out var s) ? s : c.ToString());
        return sb.ToString();
    }

    private static IEnumerable<string> Forms(string word)
    {
        var normalized = WordNormalizer.Normalize(word);
        if (normalized.Length == 0) yield break;

        yield return normalized;

        var transliterated = Transliterate(normalized);
        if (transliterated != normalized) yield return transliterated;
    }

    private static HashSet<string> FormsIn(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        foreach (var form in Forms(word))
            set.Add(form);

        return set;
    }

    /// <summary>
    /// Whether a word is close enough to one in the passage to count as the
    /// same word: three shared opening letters and near-equal length. That
    /// covers a rationale citing a dictionary form where the line has an
    /// inflected one - 'noos' against νόον - without stretching to unrelated
    /// words.
    /// </summary>
    private static bool NearAny(string word, HashSet<string> forms)
    {
        if (word.Length < 4) return true;

        foreach (var form in forms)
        {
            if (form.Length < 4) continue;
            if (Math.Abs(form.Length - word.Length) > 1) continue;
            if (string.CompareOrdinal(form, 0, word, 0, 3) == 0) return true;
        }

        return false;
    }

    /// <summary>
    /// The spans a rationale puts in quotation marks, which is where it puts
    /// its evidence.
    ///
    /// An apostrophe between two letters is a possessive or a contraction -
    /// "the Odyssey's theme" - and opening a span on one swallows the English
    /// around it, which then fails every check and flags everything. So a
    /// quote only opens before a letter and only closes after one.
    /// </summary>
    internal static List<string> QuotedSpans(string rationale)
    {
        const string quotes = "'‘’“”\"";

        var spans = new List<string>();
        var current = new StringBuilder();
        var inside = false;

        for (var i = 0; i < rationale.Length; i++)
        {
            var c = rationale[i];
            var previous = i > 0 ? rationale[i - 1] : ' ';
            var next = i + 1 < rationale.Length ? rationale[i + 1] : ' ';

            if (quotes.Contains(c))
            {
                if (char.IsLetter(previous) && char.IsLetter(next))
                {
                    if (inside) current.Append(c);
                    continue;
                }

                if (inside && char.IsLetter(previous))
                {
                    spans.Add(current.ToString());
                    current.Clear();
                    inside = false;
                }
                else if (!inside && char.IsLetter(next))
                {
                    inside = true;
                }

                continue;
            }

            if (inside) current.Append(c);
        }

        if (current.Length > 0) spans.Add(current.ToString());

        return spans;
    }

    private static string LettersOnly(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Whether the words a rationale offers as evidence are in the line it
    /// cites.
    ///
    /// The citation check confirms a reference names a real passage. It does
    /// not confirm the reason given for it. Gemini reasons about a work as a
    /// whole and then pins the reasoning to one line, and the two come apart:
    /// asked for echoes of Vǫluspá's "þau er fremst um man", it cited Odyssey
    /// 1.3 and justified it with μνήσατο and ἔννεπε, which are at 1.29 and
    /// 1.1. The reference was real, the words were real, and neither was in
    /// the line the two were joined at.
    ///
    /// Two passes, because the evidence arrives in two alphabets. A word in the
    /// comparison work's own script is checked wherever it appears. A
    /// Latin-script word is checked only inside quotation marks and only when
    /// it looks transliterated - a rationale writing 'munesthai' is making the
    /// same claim as one writing μνήσασθαι.
    ///
    /// The source passage counts as support too: a rationale properly quotes
    /// the passage it was asked about, and those words are not meant to be in
    /// the comparison line.
    ///
    /// Flagged, never dropped. A rationale may fairly reach for the context -
    /// an invocation two lines up genuinely bears on the line below - so this
    /// marks evidence a reader should check rather than ruling it wrong.
    /// </summary>
    internal static List<string> UnsupportedQuotedWords(string rationale, string lineText, string sourceText)
    {
        if (string.IsNullOrWhiteSpace(rationale)) return new List<string>();

        var lineForms = FormsIn(lineText);
        var sourceForms = FormsIn(sourceText);

        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        bool Supported(string word) =>
            Forms(word).Any(f => lineForms.Contains(f) || sourceForms.Contains(f)
                                 || NearAny(f, lineForms) || NearAny(f, sourceForms));

        foreach (var span in QuotedSpans(rationale))
        foreach (var raw in span.Replace('/', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = LettersOnly(raw);
            if (word.Length < 4) continue;

            var lower = word.ToLowerInvariant();
            if (word.All(char.IsAscii)
                && !TransliterationMarks.Any(m => lower.Contains(m, StringComparison.Ordinal))
                && !TransliterationEndings.Any(e => lower.EndsWith(e, StringComparison.Ordinal)))
                continue;

            if (Supported(word)) continue;
            if (seen.Add(word)) missing.Add(word);
        }

        foreach (var raw in rationale.Replace('/', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = LettersOnly(raw);
            if (word.Length < 3 || word.All(char.IsAscii)) continue;
            if (Supported(word)) continue;
            if (seen.Add(word)) missing.Add(word);
        }

        return missing;
    }
}
