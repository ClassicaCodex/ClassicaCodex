namespace ClassicaCodex.Core;

/// <summary>
/// Reduces an inflected English word to the base forms it might come from,
/// so a search or a Word Study lookup can find the dictionary entry.
///
/// This exists because English lemma data has a different shape from the
/// Greek and Latin data. Those corpora ship an explicit row for every
/// attested form - every case of every noun, every person of every tense -
/// because the inflection is genuinely irregular and enormous. WordNet
/// instead lists only base forms plus a short exception list for irregular
/// inflections (went/go, geese/goose), and expects the *caller* to strip
/// regular endings. That stripping is what this does.
///
/// The rules below are WordNet's own "Morphy" detachment rules. They
/// over-generate on purpose: "sses" yields both "ss" and "sse", and only
/// one will match a real entry. Candidates that don't exist in the lemma
/// table simply find nothing, so a wrong guess costs a lookup, not a wrong
/// answer.
/// </summary>
public static class EnglishLemmatizer
{
    // Suffix -> replacement, by word class. Order matters within each set:
    // longer, more specific endings are tried first so "-ches" isn't
    // shadowed by the bare "-s" rule.
    private static readonly (string Suffix, string Replacement)[] NounRules =
    {
        ("ches", "ch"),
        ("shes", "sh"),
        ("ses", "s"),
        ("xes", "x"),
        ("zes", "z"),
        ("ies", "y"),
        ("men", "man"),
        ("s", "")
    };

    private static readonly (string Suffix, string Replacement)[] VerbRules =
    {
        ("ies", "y"),
        ("ing", "e"),
        ("ing", ""),
        ("ed", "e"),
        ("ed", ""),
        ("es", "e"),
        ("es", ""),
        ("s", "")
    };

    private static readonly (string Suffix, string Replacement)[] AdjectiveRules =
    {
        ("est", "e"),
        ("est", ""),
        ("er", "e"),
        ("er", "")
    };

    /// <summary>
    /// Every base form the given word might reduce to, most likely first,
    /// with the word itself always included so an uninflected word still
    /// resolves. Callers try these in order against the lemma table and
    /// take the ones that exist.
    ///
    /// Deliberately returns candidates rather than a single answer: English
    /// is genuinely ambiguous here. "axes" is the plural of both "axe" and
    /// "axis", and "saw" is a noun as well as the past tense of "see" -
    /// picking one would silently hide the other, and this app already
    /// shows every candidate headword for Greek and Latin rather than
    /// guessing between them.
    /// </summary>
    public static IReadOnlyList<string> CandidateLemmas(string word)
    {
        var normalized = word.Trim().ToLowerInvariant();
        if (normalized.Length == 0) return Array.Empty<string>();

        // Ordinal comparison and insertion order preserved: the word itself
        // must stay first so an exact match wins over a stripped guess.
        var candidates = new List<string> { normalized };
        var seen = new HashSet<string>(StringComparer.Ordinal) { normalized };

        foreach (var ruleSet in new[] { NounRules, VerbRules, AdjectiveRules })
        {
            foreach (var (suffix, replacement) in ruleSet)
            {
                if (!normalized.EndsWith(suffix, StringComparison.Ordinal)) continue;

                var stem = normalized[..^suffix.Length] + replacement;

                // A single leftover letter is never a real English lemma and
                // just wastes a lookup ("as" -> "a", "is" -> "i").
                if (stem.Length < 2) continue;
                if (seen.Add(stem)) candidates.Add(stem);
            }
        }

        // Doubled final consonant before -ed/-ing: "stopped" -> "stop",
        // "running" -> "run". Handled separately because it's a change to
        // the stem rather than a suffix swap.
        foreach (var suffix in new[] { "ed", "ing" })
        {
            if (!normalized.EndsWith(suffix, StringComparison.Ordinal)) continue;

            var stem = normalized[..^suffix.Length];
            if (stem.Length >= 3 && stem[^1] == stem[^2] && !"aeiou".Contains(stem[^1]))
            {
                var undoubled = stem[..^1];
                if (seen.Add(undoubled)) candidates.Add(undoubled);
            }
        }

        return candidates;
    }
}
