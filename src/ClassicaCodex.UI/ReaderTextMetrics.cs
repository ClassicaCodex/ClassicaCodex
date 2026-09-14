namespace ClassicaCodex.UI;

/// <summary>
/// A cheap, approximate stand-in for text layout, used to guess where a
/// passage should be cut before anything is measured for real.
///
/// Cutting a passage into rows means asking "how tall is this piece?" over and
/// over, and a GDI measurement costs about a millisecond whatever the string's
/// length - so the cost of splitting is the number of questions asked, not the
/// size of them. Asked of GDI directly, splitting a full prose edition took
/// around a minute. Asked of this first, and of GDI only to confirm the answer,
/// it costs about one measurement per row - which is the measurement the row's
/// height needed anyway.
///
/// It is an estimate and is treated as one. Measured against real layout over
/// twelve hundred passages: Palatino Linotype agreed exactly 97.7% of the time
/// and was never more than a line out; Georgia agreed 72.5% of the time and was
/// occasionally three lines out. Both err in both directions, so nothing here
/// is safe to act on unchecked - the caller cuts short of the real limit and
/// then confirms with a real measurement.
/// </summary>
internal sealed class ReaderTextMetrics
{
    private readonly Dictionary<char, double> _advance;
    private readonly double _fallback;
    private readonly double _space;

    private ReaderTextMetrics(Dictionary<char, double> advance, double fallback, double space, int lineHeight)
    {
        _advance = advance;
        _fallback = fallback;
        _space = space;
        LineHeight = lineHeight;
    }

    internal int LineHeight { get; }

    /// <summary>
    /// Builds the table for one font from the characters actually present in
    /// the text about to be shown.
    ///
    /// Each character is measured as a long repeat and divided, so that the
    /// per-call padding GDI adds is spread across fifty characters instead of
    /// being counted as part of one. The cost is one measurement per distinct
    /// character - about 200 to 300 for an edition, 9 to 17 milliseconds, once.
    ///
    /// Safe on a worker thread: text measurement off the UI thread was verified
    /// over 6,233 passages at two widths in both reader fonts with no
    /// disagreement, and four threads sharing one font produced none either.
    /// </summary>
    internal static ReaderTextMetrics Build(Font font, IEnumerable<char> characters, MeasureWidth measure)
    {
        var advance = new Dictionary<char, double>();

        foreach (var c in characters)
        {
            if (char.IsControl(c) || advance.ContainsKey(c)) continue;

            advance[c] = measure(new string(c, RepeatCount)) / (double)RepeatCount;
        }

        var fallback = advance.Count > 0 ? advance.Values.Average() : font.Size;
        var space = advance.TryGetValue(' ', out var s) ? s : fallback / 2;

        return new ReaderTextMetrics(advance, fallback, space, font.Height);
    }

    /// <summary>Width of a string, as the caller's font measures it.</summary>
    internal delegate int MeasureWidth(string text);

    private const int RepeatCount = 50;

    /// <summary>
    /// Roughly how many lines this text takes when wrapped at
    /// <paramref name="width"/>, by adding up advances and breaking at words -
    /// which is what the real layout does, minus the kerning and hinting this
    /// cannot see.
    /// </summary>
    internal int EstimateLines(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return 1;

        var lines = 1;
        double column = 0;
        double word = 0;
        var haveWord = false;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                if (haveWord) Place(ref lines, ref column, word, width);
                haveWord = false;
                word = 0;
                lines++;
                column = 0;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (haveWord) Place(ref lines, ref column, word, width);
                haveWord = false;
                word = 0;
                continue;
            }

            word += _advance.TryGetValue(c, out var w) ? w : _fallback;
            haveWord = true;
        }

        if (haveWord) Place(ref lines, ref column, word, width);

        return lines;
    }

    /// <summary>Estimated height, in the same units row heights are kept in.</summary>
    internal int EstimateHeight(string text, int width) => EstimateLines(text, width) * LineHeight + 6;

    private void Place(ref int lines, ref double column, double word, int width)
    {
        if (column > 0 && column + _space + word > width)
        {
            lines++;
            column = word;
            return;
        }

        column += (column > 0 ? _space : 0) + word;
    }
}
