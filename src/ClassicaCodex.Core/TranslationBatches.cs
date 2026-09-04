namespace ClassicaCodex.Core;

/// <summary>
/// How much of a work to send for translation in one request.
///
/// This was a fixed count of lines, and a line is not a fixed amount of text.
/// In verse it is a line of verse - about forty characters. In prose that has
/// been divided into sections, which is most of this corpus, one "line" is a
/// whole section: Julian's average is a thousand characters and his longest is
/// 5,726. So twenty-five lines meant a thousand characters of Homer and 22,812
/// of Julian, out of the same constant.
///
/// The second one does not come back. Translating that much Greek means
/// generating about as much English, token by token, and the request times out
/// before it finishes - so the first batch failed, and since a failed batch
/// stopped the whole run, the feature simply did not work for those works. It
/// looked like a Julian problem. Measured across the library, 25 lines is over
/// 12,000 characters for 1,182 of 2,922 works, and under 2,000 for only 566:
/// the shape it was tuned for is the minority.
///
/// So a batch is now bounded by both, and whichever runs out first ends it.
/// Verse is unaffected - twenty-five short lines are nowhere near the
/// character budget, and that is the case the line count was tuned on.
/// </summary>
public static class TranslationBatches
{
    /// <summary>
    /// Most lines in one request. Unchanged, and still the binding limit for
    /// verse: this is the number a real 802-line work was watched running on.
    /// </summary>
    public const int MaxLines = 25;

    /// <summary>
    /// Most source characters in one request.
    ///
    /// Measured against the live API on Julian's Misopogon, which is ordinary
    /// prose of this kind: 5,107 characters came back in 24s and 9,958 in 37s,
    /// while 22,812 - what the line count alone produced - did not come back
    /// at all. A batch of this size leaves room for the variance the same
    /// measurements showed, where 2,885 characters once took 48s.
    /// </summary>
    public const int MaxCharacters = 6000;

    /// <summary>
    /// Groups items into batches under both limits, in order.
    ///
    /// A single item longer than the whole budget goes in a batch of its own
    /// rather than being dropped or split: splitting it would send the model
    /// half a sentence, and dropping it would silently lose a passage. It may
    /// well fail on its own, and that is then one line reported as missing
    /// rather than a work that cannot be started.
    /// </summary>
    public static List<List<T>> Plan<T>(
        IEnumerable<T> items,
        Func<T, int> lengthOf,
        int maxLines = MaxLines,
        int maxCharacters = MaxCharacters)
    {
        var batches = new List<List<T>>();
        var current = new List<T>();
        var characters = 0;

        foreach (var item in items)
        {
            var length = Math.Max(0, lengthOf(item));

            // Never on an empty batch, so an over-budget item still travels.
            if (current.Count > 0 &&
                (current.Count >= maxLines || characters + length > maxCharacters))
            {
                batches.Add(current);
                current = new List<T>();
                characters = 0;
            }

            current.Add(item);
            characters += length;
        }

        if (current.Count > 0) batches.Add(current);
        return batches;
    }
}
