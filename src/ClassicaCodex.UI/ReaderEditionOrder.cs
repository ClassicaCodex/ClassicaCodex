using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// The order the reader offers a work's editions in, and which it opens by
/// default - in one place, because a dictionary citation has to open the
/// same edition the reader would, and two copies of the rule would drift.
/// </summary>
public static class ReaderEditionOrder
{
    /// <summary>As the edition picker lists them: by what tells them apart, then by identifier.</summary>
    public static List<Edition> Ordered(IEnumerable<Edition> editions) =>
        editions
            .OrderBy(EditionLabels.Descriptor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.CtsUrn, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// The same, with the edition the reader opens by default moved to the
    /// front: the preferred collection's, or the first.
    /// </summary>
    public static List<Edition> DefaultFirst(IEnumerable<Edition> editions, string? preferredCollection)
    {
        var ordered = Ordered(editions);
        var index = PreferredEdition.IndexOfDefault(ordered, preferredCollection);
        if (index <= 0) return ordered;

        var preferred = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(0, preferred);
        return ordered;
    }
}
