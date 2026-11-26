using System.Globalization;

namespace ClassicaCodex.Core.Quotations;

/// <summary>
/// A short fingerprint of a passage's text, to tell whether character
/// positions recorded against it still fit.
///
/// Thirty-two bits of FNV-1a over the UTF-16 code units, exactly as stored:
/// no folding, because the positions are into the exact string, and a change
/// of one space moves every position after it. Not a security measure - a
/// collision costs at worst an underline in the wrong place - and short,
/// because the shipped table carries one for each of some thirty thousand
/// passages.
/// </summary>
public static class TextFingerprint
{
    public static string Of(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
