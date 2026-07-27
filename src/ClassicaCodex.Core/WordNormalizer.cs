using System.Globalization;
using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// Normalizes a Greek or Latin word form for matching: strips accents and
/// breathings, folds final sigma, and lowercases.
///
/// This matters more than it might sound. Perseus texts (and the lemma data
/// published against them) aren't perfectly consistent about accentuation,
/// or about precomposed vs combining Unicode for the same character - so
/// ᾳ can be one codepoint in one file and two in another. Matching on the
/// bare letters sidesteps all of that. The cost is a small amount of real
/// ambiguity (a few Greek pairs are distinguished only by accent), which is
/// the right trade for a reading tool.
/// </summary>
public static class WordNormalizer
{
    public static string Normalize(string word)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;

        // NFD splits precomposed characters into base letter + combining
        // marks, so the marks can simply be dropped.
        var decomposed = word.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (!char.IsLetter(ch)) continue;

            var lower = char.ToLowerInvariant(ch);

            // Final sigma and medial sigma are the same letter positionally,
            // so fold them together or λόγος won't match λόγοσ-stemmed data.
            if (lower == 'ς') lower = 'σ';

            sb.Append(lower);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalizes a dictionary headword for lookup, on top of the standard
    /// normalization.
    ///
    /// Two extra problems show up when matching lemma headwords against
    /// lexicon keys:
    ///
    /// 1. Homograph numbering. Lemma data marks separate dictionary words
    ///    that share a spelling as liber1/liber2, but the lexicon numbers
    ///    them differently (or not at all), and there's no reliable mapping
    ///    between the two schemes. Stripping the digits means a lookup can
    ///    return several entries - which is the honest outcome, since we
    ///    genuinely can't tell which numbered sense was meant.
    ///
    /// 2. Latin u/v and i/j. These were one letter each in antiquity, and
    ///    editions differ: lemma data may say "uos" where the lexicon says
    ///    "vos". Folding both directions makes the two agree.
    /// </summary>
    public static string NormalizeHeadword(string headword, string language)
    {
        if (string.IsNullOrEmpty(headword)) return string.Empty;

        var trimmed = headword.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (trimmed.Length == 0) trimmed = headword;

        var normalized = Normalize(trimmed);

        if (string.Equals(language, "lat", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace('v', 'u').Replace('j', 'i');
        }

        return normalized;
    }
}
