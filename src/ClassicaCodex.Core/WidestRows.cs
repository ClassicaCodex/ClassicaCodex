namespace ClassicaCodex.Core;

/// <summary>
/// Picks which rows of a list are worth measuring when sizing its horizontal
/// scroll extent.
///
/// A list only needs to know its widest row, and measuring text costs a GDI
/// call apiece - so measuring all of them to find one is work proportional to
/// the result set every time the result set changes. Character count is a
/// cheap proxy for width: scan for the longest few, measure only those, take
/// the widest of them.
///
/// The proxy is not exact, because the reading fonts are proportional and
/// "MMMM" sets wider than "iiiiiiiiii". That is what the sample size is for -
/// the widest row in pixels is somewhere in the longest few dozen by
/// character, not necessarily first among them. And the cost of being wrong
/// is bounded and small: an extent short by a few pixels stops the scrollbar
/// slightly early. It cannot lose a row or fail.
/// </summary>
public static class WidestRows
{
    /// <summary>
    /// Enough that the widest row is reliably inside it, few enough that the
    /// measuring is not worth thinking about.
    /// </summary>
    public const int DefaultSampleSize = 64;

    /// <summary>
    /// The longest rows first, at most <paramref name="sampleSize"/> of them,
    /// skipping rows with nothing in them to measure.
    /// </summary>
    public static IReadOnlyList<string> Candidates(
        IEnumerable<string?> rows, int sampleSize = DefaultSampleSize)
    {
        if (rows is null || sampleSize <= 0) return Array.Empty<string>();

        // Bounded rather than an OrderByDescending over everything: these
        // lists can hold a whole search result set, and sorting all of it to
        // read the top of the list is the cost this type exists to avoid.
        var kept = new List<string>(sampleSize);
        var shortestKept = -1;

        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row)) continue;

            if (kept.Count < sampleSize)
            {
                kept.Add(row);
                if (kept.Count == sampleSize)
                {
                    kept.Sort(LongestFirst);
                    shortestKept = kept[^1].Length;
                }
                continue;
            }

            if (row.Length <= shortestKept) continue;

            kept[^1] = row;
            kept.Sort(LongestFirst);
            shortestKept = kept[^1].Length;
        }

        kept.Sort(LongestFirst);
        return kept;
    }

    private static int LongestFirst(string a, string b) => b.Length.CompareTo(a.Length);
}
