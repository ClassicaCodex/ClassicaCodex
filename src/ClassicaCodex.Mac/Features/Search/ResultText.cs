using ClassicaCodex.Core;

namespace ClassicaCodex.Mac.Features.Search;

/// <summary>
/// How much of a passage a result row shows, and where the window onto it
/// sits - the two text helpers of the Windows ListResultHelpers, which the
/// Mac cannot compile because the rest of that file is WinForms.
///
/// On Windows the cut began as a crash fix: GDI+ fails to measure a single
/// run over about 32,000 characters once it contains a soft hyphen or a
/// combining mark, and this corpus has whole Migne and CSEL sections that
/// long. Avalonia does not fail that way, but the cut is kept for what it
/// also was - a row is a pointer to a passage, not the passage, and laying
/// out thirty thousand characters to show the first two lines of them is
/// work the list does for every row it realises. Nothing is lost by it: the
/// untruncated text stays in the result the window holds, which is what
/// copy, the tooltip and the export all read.
/// </summary>
internal static class ResultText
{
    /// <summary>As much of a passage as a single result row shows.</summary>
    public const int RowLimit = 400;

    public static string Row(string? text, int limit = RowLimit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;

        // Not through a surrogate pair, which would leave half a character.
        var cut = limit;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    /// <summary>
    /// The same cut, positioned so a particular stretch of the passage stays
    /// visible.
    ///
    /// Taking the opening of a long passage is the right default, and it is
    /// wrong wherever the row exists to show a hit: the match can sit past
    /// the cut, leaving a row whose text contains no sign of what matched.
    /// The window keeps a little of what precedes the match, so the phrase
    /// reads as a phrase rather than starting mid-word, and marks each cut
    /// end with an ellipsis so it is clear the line was trimmed.
    /// </summary>
    /// <param name="matchStart">Where the match begins, or a negative number if there is none.</param>
    public static string RowAround(string? text, int matchStart, int limit = RowLimit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;
        if (matchStart < 0 || matchStart >= text.Length) return Row(text, limit);

        // Enough lead-in to read the match in context, and the rest after it.
        const int lead = 60;
        var start = Math.Max(0, matchStart - lead);

        // A match longer than the whole window would leave nothing to show;
        // the window still starts at the match so its opening is visible.
        var end = Math.Min(text.Length, start + limit);
        if (end <= matchStart) end = Math.Min(text.Length, matchStart + limit);

        // Never split a surrogate pair at either edge.
        if (start > 0 && char.IsLowSurrogate(text[start])) start++;
        if (end < text.Length && char.IsLowSurrogate(text[end])) end--;

        var window = text[start..end];
        return (start > 0 ? "…" : string.Empty) + window + (end < text.Length ? "…" : string.Empty);
    }

    /// <summary>
    /// Where the first of <paramref name="terms"/> appears, or (-1, 0) if none
    /// of them do - which happens legitimately, since the search matches on a
    /// normalized copy of the text and the passage may spell the word with
    /// different accents.
    ///
    /// The Windows row falls back to the opening of the passage there. This
    /// one asks <see cref="WordOccurrences"/> first, which finds the word
    /// however the edition accents it, because the case the literal search
    /// misses is the default one - a Greek word typed without accents, in a
    /// Plato section thousands of characters long, which otherwise shows its
    /// opening and no sign of the word it was found for.
    /// </summary>
    public static (int Start, int Length) FirstMatch(string text, IReadOnlyList<string> terms, IReadOnlyCollection<string> wordTargets)
    {
        var best = -1;
        var length = 0;

        foreach (var term in terms)
        {
            var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            if (best < 0 || at < best) { best = at; length = term.Length; }
        }

        if (best >= 0) return (best, length);

        var words = WordOccurrences.Find(text, wordTargets);
        return words.Count > 0 ? words[0] : (-1, 0);
    }

    /// <summary>
    /// Both ways of finding the query in the line, because both are right for
    /// a different mode.
    ///
    /// The literal pass is what "anywhere in the line" means, and is the only
    /// one that can mark a stem inside a longer word - typing "virt" and
    /// seeing it lit inside "virtutem".
    ///
    /// The word pass is what the whole-word default actually matched on. That
    /// search goes through the index, which folds accents, both sigmas, and
    /// u/v and i/j - so it returns the line accented however its edition
    /// accents it, and the edition printing "justitia" for a query of
    /// "iustitia". A literal IndexOf finds none of those, so the row came
    /// back with nothing marked in it, which reads as the application having
    /// returned a line that does not contain the word.
    ///
    /// Whole-word mode gets the word pass alone. Running both marked the
    /// letters of the query inside longer words - searching "arm" and seeing
    /// "harm" lit up in a line that matched on a real "arm" elsewhere - which
    /// contradicts the mode the reader chose.
    ///
    /// Returned in order with overlaps removed, so a caller can walk them.
    /// </summary>
    public static List<(int Start, int Length)> HighlightSpans(
        string text, IReadOnlyList<string> terms, IReadOnlyCollection<string> wordTargets, bool wholeWordsOnly)
    {
        var spans = new List<(int Start, int Length)>();

        if (!wholeWordsOnly)
        {
            foreach (var term in terms)
            {
                if (term.Length == 0) continue;

                var idx = 0;
                while ((idx = text.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    spans.Add((idx, term.Length));
                    idx += term.Length;
                }
            }
        }

        spans.AddRange(WordOccurrences.Find(text, wordTargets));
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int Length)>(spans.Count);
        var reached = 0;
        foreach (var span in spans)
        {
            if (span.Start < reached) continue;
            merged.Add(span);
            reached = span.Start + span.Length;
        }

        return merged;
    }
}
