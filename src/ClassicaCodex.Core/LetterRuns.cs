using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// Finds the runs of letters in a piece of text, counting a letter as a
/// letter whatever plane it lives on.
///
/// This exists because of a bug the corpus has now grown into twice.
///
/// The first time, the word scanners matched [a-zA-Z]. That silently made
/// them English-only - Greek text contains no ASCII letters at all, so no
/// words were ever extracted and the features built on them returned nothing
/// for every Greek passage. The fix was \p{L}, which matches a letter in any
/// script. See the note on TextNodeRepository.WordPattern, where that repair
/// is recorded.
///
/// \p{L} has the same hole one plane up. .NET strings are UTF-16, and .NET's
/// regex engine and char methods both work on 16-bit code units rather than
/// on characters. Every Egyptian hieroglyph (U+13000 and up) and every
/// cuneiform sign (U+12000 and up) is outside the Basic Multilingual Plane,
/// so each one arrives as a surrogate pair - two code units, neither of which
/// is a letter on its own. Measured:
///
///     input   "alpha [hieroglyph][hieroglyph] beta [cuneiform][cuneiform]"
///     \p{L}+  matches "alpha" and "beta". Nothing else.
///     IsLetter filter keeps "alphabeta".
///
/// Both scripts ARE letters - Unicode category OtherLetter, the same category
/// as a Chinese character. .NET simply cannot see them through a 16-bit char.
/// So the failure is not a refusal, it is a silence: the index builds
/// cleanly, reports success, and contains nothing. That is the shape of
/// failure that survives manual testing, because you have to already know the
/// right answer to notice it is missing.
///
/// Enumerating runes instead fixes it for every script at once, including
/// ones the corpus has not met yet. A rune is a whole Unicode scalar value,
/// so a surrogate pair is one rune rather than two halves.
///
/// Rune.IsLetter accepts exactly the categories \p{L} does - Lu, Ll, Lt, Lm,
/// Lo - so for text that was already working, this changes nothing. That is
/// pinned by test rather than asserted here.
/// </summary>
public static class LetterRuns
{
    /// <summary>
    /// Every maximal run of letters in <paramref name="text"/>, in order.
    ///
    /// Start and Length are in UTF-16 code units, so they index straight back
    /// into the string. LetterCount is in letters, which is what a
    /// "words of at least four letters" rule means and is NOT the same number
    /// once a script lives outside the BMP: two hieroglyphs have Length 4 and
    /// LetterCount 2. Callers filtering on length want LetterCount.
    /// </summary>
    public static IEnumerable<(int Start, int Length, int LetterCount)> Scan(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var index = 0;
        while (index < text.Length)
        {
            if (!TryRuneAt(text, index, out var rune, out var width) || !Rune.IsLetter(rune))
            {
                // Step by the whole rune even when it is not a letter, so a
                // lone astral symbol cannot leave the walk standing on half
                // of itself.
                index += width;
                continue;
            }

            var start = index;
            var letters = 0;

            while (index < text.Length
                   && TryRuneAt(text, index, out var next, out var nextWidth)
                   && Rune.IsLetter(next))
            {
                letters++;
                index += nextWidth;
            }

            yield return (start, index - start, letters);
        }
    }

    /// <summary>
    /// The text of every letter run, which is what the tokenizers want.
    /// </summary>
    public static IEnumerable<string> Words(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var (start, length, _) in Scan(text)) yield return text.Substring(start, length);
    }

    /// <summary>
    /// How many letters a string holds, which is not its Length once a script
    /// lives outside the BMP: two hieroglyphs have Length 4 and 2 letters.
    /// Any rule phrased as "at least N letters" wants this number.
    /// </summary>
    public static int CountLetters(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var count = 0;
        foreach (var rune in text.EnumerateRunes())
            if (Rune.IsLetter(rune)) count++;

        return count;
    }

    /// <summary>
    /// The rune beginning at <paramref name="index"/>, and how many code units
    /// it occupies.
    ///
    /// Rune.TryCreate fails on an unpaired surrogate rather than throwing, and
    /// real corpora do contain them - a passage truncated mid-pair by an
    /// exporter, or a file that lost its last byte. Width is still reported as
    /// 1 in that case so the walk advances and cannot spin.
    /// </summary>
    private static bool TryRuneAt(string text, int index, out Rune rune, out int width)
    {
        var first = text[index];

        if (char.IsHighSurrogate(first) && index + 1 < text.Length
            && char.IsLowSurrogate(text[index + 1])
            && Rune.TryCreate(first, text[index + 1], out rune))
        {
            width = 2;
            return true;
        }

        width = 1;
        return Rune.TryCreate(first, out rune);
    }

    /// <summary>
    /// Trims a span inward to the first and last letter it contains, by whole
    /// runes. Returns false when the span holds no letters at all.
    ///
    /// Used to put a highlight on the letters of a matched word rather than on
    /// the punctuation around it. Walking this by char loses an astral word
    /// entirely: both halves of every pair fail char.IsLetter, so the two ends
    /// meet in the middle and the match is found but never drawn.
    /// </summary>
    public static bool TrimToLetters(string text, int start, int endInclusive, out int trimmedStart, out int trimmedEndInclusive)
    {
        var from = start;
        var to = endInclusive;

        while (from <= to)
        {
            if (TryRuneAt(text, from, out var rune, out var width) && Rune.IsLetter(rune)) break;
            from += width;
        }

        while (to >= from)
        {
            // `to` is an inclusive end, so it may be the low half of a pair.
            // Step back to the pair's start to test it, but leave `to` where
            // it is - the caller wants the last code unit of the last letter,
            // not the first.
            var runeStart = char.IsLowSurrogate(text[to]) && to > from ? to - 1 : to;
            if (TryRuneAt(text, runeStart, out var rune, out _) && Rune.IsLetter(rune)) break;
            to = runeStart - 1;
        }

        trimmedStart = from;
        trimmedEndInclusive = to;
        return from <= to;
    }
}
