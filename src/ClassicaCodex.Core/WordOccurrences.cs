namespace ClassicaCodex.Core;

/// <summary>
/// Where in a line the word a reader searched for actually sits.
///
/// Finding a line and pointing at the word in it had drifted apart. The
/// search matches through the word index, which folds accents, breathings,
/// both sigmas, and both halves of u/v and i/j - so a query typed without
/// accents finds the line that has them, and "iustitia" finds the edition
/// printing "justitia". Everything that then had to point at the word was
/// still doing a literal, case-insensitive IndexOf of what was typed.
///
/// So the search returned rows and the highlighter had nothing to highlight,
/// and the concordance - whose entire output is the word framed by its
/// context - fell back to printing the line with "(stemmed match)" where the
/// keyword column should be. From the reader's side that looks like the
/// application returning lines that do not contain the word: the single most
/// reportable-looking thing a search can do.
///
/// Matching here is deliberately the same shape the index build uses -
/// whitespace-delimited tokens, each run through WordNormalizer - because
/// anything else would point at a different set of words from the one that
/// selected the line.
/// </summary>
public static class WordOccurrences
{
    /// <summary>
    /// Every normalized spelling a query could have matched, which is what
    /// the index was asked for and therefore what may be sitting in the line.
    /// </summary>
    public static HashSet<string> TargetsFor(string? query)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query)) return targets;

        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = WordNormalizer.Normalize(word);
            if (normalized.Length == 0) continue;

            foreach (var spelling in SpellingVariants.Of(normalized)) targets.Add(spelling);
        }

        return targets;
    }

    /// <summary>
    /// The spans of <paramref name="text"/> holding one of
    /// <paramref name="targets"/>, in order, without overlaps.
    ///
    /// A span covers the word as printed and not the punctuation around it:
    /// the token is split off by whitespace so that normalizing it agrees
    /// with the index, and the span is then pulled in to the letters, so
    /// highlighting "λόγος," marks the word and leaves the comma alone.
    /// </summary>
    public static List<(int Start, int Length)> Find(string? text, IReadOnlyCollection<string> targets)
    {
        var spans = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(text) || targets.Count == 0) return spans;

        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;

            var token = text[start..i];
            if (!targets.Contains(WordNormalizer.Normalize(token))) continue;

            // In to the letters. A token that is entirely punctuation cannot
            // reach here, since it would have normalized to nothing and no
            // target is empty.
            var from = start;
            var to = i - 1;
            while (from <= to && !char.IsLetter(text[from])) from++;
            while (to >= from && !char.IsLetter(text[to])) to--;

            if (from <= to) spans.Add((from, to - from + 1));
        }

        return spans;
    }
}
