using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// Turns reading text into the token stream Delta counts.
///
/// Lifted out of StylometryForm unchanged. It lives here because the
/// validation and perturbation experiments need to tokenise thousands of
/// times without a window open, and because a measure this sensitive to
/// tokenisation should be testable directly rather than through a form.
/// </summary>
public static class StylometryTokenizer
{
    private static readonly Regex WordPattern = new(@"\p{L}+", RegexOptions.Compiled);

    /// <summary>
    /// Removes elision apostrophes so that δ' and δε count as the same token.
    ///
    /// This is a correctness fix, not a preference. Whether an elided form is
    /// written δ' or δε is a decision made by a nineteenth-century editor, not
    /// by the author, and Perseus is not consistent about it across ~2,000
    /// files. Left in place it becomes the single highest-weighted feature in a
    /// Greek run - i.e. the analysis measures which editor prepared the text.
    ///
    /// It has to happen here rather than being left to the regex or to
    /// WordNormalizer, because both let it through:
    ///
    ///   - The regex above matches \p{L}+, and U+02BC MODIFIER LETTER
    ///     APOSTROPHE is Unicode category Lm, which is a Letter category. So
    ///     \p{L} captures it as part of the word.
    ///   - WordNormalizer.Normalize filters on char.IsLetter, which returns
    ///     true for Lm for the same reason. It strips combining marks and folds
    ///     final sigma, but the apostrophe survives.
    ///
    /// U+2019 (Pf) and U+0027 (Po) are punctuation and would be dropped by
    /// either filter - which is the actual problem, since it means the same
    /// word tokenizes differently depending on which codepoint an edition
    /// happens to use. All three are removed here so the treatment is uniform.
    ///
    /// WHAT THIS DOES NOT DO. Stripping the mark maps δ' to δ, not to δε. The
    /// elided and unelided forms of the same word therefore remain separate
    /// features. That is deliberate, and it is the smaller problem: the
    /// confound being fixed is CROSS-EDITION inconsistency, and merging δ into
    /// δέ would need a restoration table, some of which is genuinely ambiguous.
    /// Elision is metrically conditioned, so how often a poet elides is
    /// plausibly a real stylistic feature. Leaving the forms separate keeps
    /// that signal; merging them would discard it. Not resolved here.
    /// </summary>
    public static string StripElisionMarks(string token) => token
        .Replace("\u02BC", string.Empty)   // MODIFIER LETTER APOSTROPHE (Lm - survives \p{L} and char.IsLetter)
        .Replace("\u2019", string.Empty)   // RIGHT SINGLE QUOTATION MARK (Pf)
        .Replace("\u1FBD", string.Empty)   // GREEK KORONIS (Sk)
        .Replace("'", string.Empty);       // ASCII APOSTROPHE (Po)

    /// <summary>
    /// One token, folded or merely lower-cased.
    ///
    /// With accent folding on, ἦ / ἥ / ᾗ collapse to a single token, which
    /// removes inconsistent accentuation across Perseus editions but merges
    /// genuinely distinct function words. With it off, the distinctions survive
    /// along with whatever inconsistency the editions carry. Neither is
    /// correct, which is why it is a setting rather than a constant.
    /// </summary>
    public static string NormalizeToken(string raw, bool foldAccents)
    {
        var token = foldAccents ? WordNormalizer.Normalize(raw) : raw.ToLowerInvariant();
        return StripElisionMarks(token);
    }

    /// <summary>
    /// Every token in a piece of reading text, in order.
    /// </summary>
    public static List<string> Tokenize(string text, bool foldAccents)
    {
        var tokens = new List<string>();

        foreach (Match m in WordPattern.Matches(text))
        {
            var w = NormalizeToken(m.Value, foldAccents);

            // Normalize drops everything that isn't a letter, so a token
            // consisting only of marks can come back empty. Counting those
            // would inflate the denominator with nothing.
            if (w.Length == 0) continue;
            tokens.Add(w);
        }

        return tokens;
    }
}
