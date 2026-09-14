namespace ClassicaCodex.UI;

/// <summary>
/// Cuts a passage into pieces each short enough to be a row of the reader.
///
/// A row cannot be taller than <see cref="ReaderRowHeight.Max"/> - that is a
/// Win32 limit, not a choice - and a great many passages in this corpus are
/// taller than that. Until now the surplus was simply not shown. Splitting the
/// passage across several rows is what makes it readable, and this is the part
/// that decides where the cuts fall.
///
/// The measurement is injected rather than called directly, for two reasons.
/// The rules about where a cut may fall are arithmetic and can be tested
/// without a font, a device context or a window; and the caller measures text
/// on a worker thread, so this must not reach for anything belonging to a
/// control.
/// </summary>
internal static class ReaderRowSplitter
{
    /// <summary>
    /// How tall <paramref name="text"/> would be, wrapped at the width the
    /// caller has in mind. The splitter never asks about anything else, which
    /// is what keeps it testable.
    /// </summary>
    internal delegate int MeasureHeight(string text);

    /// <summary>
    /// The pieces of <paramref name="text"/>, in order, each measuring no more
    /// than <paramref name="maxHeight"/> where that is possible at all.
    ///
    /// Concatenating the result gives back the original text exactly. Nothing
    /// is trimmed, inserted or normalised: this decides where to cut and
    /// nothing else, so that what the reader sees across the rows is what the
    /// edition says, character for character. The tests hold that invariant
    /// because it is the one that keeps a split passage honest.
    ///
    /// A passage that already fits comes back as a single piece and costs one
    /// measurement, which matters because most passages are that case.
    /// </summary>
    /// <param name="mayFitWhole">
    /// False when the caller already knows the passage is too tall - it has
    /// cheaper ways of telling, see <see cref="ReaderRowHeight.CannotFit"/>.
    /// Measuring a passage whole is cheap exactly when it fits and expensive
    /// exactly when it does not, so the one upfront check is worth making only
    /// when the answer might be yes.
    /// </param>
    internal static List<string> Split(string text, MeasureHeight measure, int maxHeight, bool mayFitWhole = true)
    {
        if (string.IsNullOrEmpty(text)) return new List<string> { text };

        // The common case, and the reason this is the first thing tried: most
        // passages fit, and a passage that fits is by definition short enough to
        // be cheap to measure.
        if (mayFitWhole && measure(text) <= maxHeight) return new List<string> { text };

        var cuts = AllowedCuts(text);
        var segments = new List<string>();
        var start = 0;
        var nextCut = 0;

        while (start < text.Length)
        {
            while (nextCut < cuts.Count && cuts[nextCut] <= start) nextCut++;

            var length = LongestFittingLength(text, start, cuts, nextCut, measure, maxHeight);

            // Nothing fits, which means a single unbroken run is itself too
            // tall - a word longer than the row, or the grid poem whose longest
            // run is 334 characters. Emitting the whole run as one row is the
            // honest answer: it is what the control will clip, and cutting a
            // word in half to make it fit would be inventing a line break the
            // edition does not have. Also the guard that stops this looping.
            if (length <= 0) length = RunLength(text, start);

            segments.Add(text.Substring(start, length));
            start += length;
        }

        return segments;
    }

    /// <summary>
    /// The longest piece starting at <paramref name="start"/> that fits, cut at
    /// a point where a cut is allowed. Zero when not even the first allowed cut
    /// fits.
    ///
    /// Reached forwards, doubling the stride, rather than by bisecting what is
    /// left. That is the whole performance story of this class: a row holds
    /// about ten lines, so the answer is always a few hundred characters, and
    /// bisecting a forty-thousand-character passage begins by measuring twenty
    /// thousand of them. Every string measured here is at most about twice the
    /// answer, whatever the passage's length. Measured over seven real
    /// editions, that is the difference between a minute and a few seconds.
    ///
    /// Wrapped height only ever grows as text is added, which is what makes
    /// both the stride and the bisection that finishes it valid.
    /// </summary>
    private static int LongestFittingLength(
        string text, int start, List<int> cuts, int firstCut, MeasureHeight measure, int maxHeight)
    {
        var lastFitting = -1;
        var firstFailing = cuts.Count;

        for (var stride = 1; firstCut + stride - 1 < cuts.Count; stride *= 2)
        {
            var candidate = firstCut + stride - 1;

            if (measure(text[start..cuts[candidate]]) <= maxHeight) lastFitting = candidate;
            else { firstFailing = candidate; break; }
        }

        // Everything up to the last cut fits, so the tail may fit whole. It is
        // the one measurement of a long string this makes, and only ever on the
        // final piece.
        if (firstFailing == cuts.Count && measure(text[start..]) <= maxHeight) return text.Length - start;

        var low = lastFitting + 1;
        var high = firstFailing - 1;
        var best = lastFitting >= 0 ? cuts[lastFitting] - start : 0;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var length = cuts[middle] - start;

            if (measure(text.Substring(start, length)) <= maxHeight)
            {
                best = length;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }

    /// <summary>
    /// Where a cut may fall: at the start of each word after the first, and
    /// after each line break.
    ///
    /// Expressed as the index the NEXT piece would begin at, so that the piece
    /// before it carries the whitespace away with it and the concatenation
    /// still comes back whole. A cut is never offered inside a word, so a split
    /// passage never shows half of one.
    /// </summary>
    private static List<int> AllowedCuts(string text)
    {
        var cuts = new List<int>();
        var inWhitespace = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                inWhitespace = true;
                continue;
            }

            // The first non-space after a run of spaces, and never index zero -
            // a cut there would produce an empty piece and no progress.
            if (inWhitespace && i > 0) cuts.Add(i);
            inWhitespace = false;
        }

        return cuts;
    }

    /// <summary>
    /// The unbreakable run at <paramref name="start"/>: its word plus any
    /// whitespace following it, or the rest of the text when there is no
    /// further word. Always at least one character, so a caller using it to
    /// make progress always makes some.
    /// </summary>
    private static int RunLength(string text, int start)
    {
        var i = start;

        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;

        return Math.Max(i - start, 1);
    }
}
