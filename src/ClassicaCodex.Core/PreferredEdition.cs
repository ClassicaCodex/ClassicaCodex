using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

/// <summary>
/// Which edition a work opens on when more than one collection carries it.
///
/// Overlap between collections is normal rather than exceptional: Perseus and
/// the First Thousand Years of Greek both have the Agamemnon, CSEL and
/// Patrologia Latina share a good deal of Augustine. Both editions are worth
/// having - that is the point of holding several collections - but only one of
/// them can be the one that opens, and until now that was whichever sorted
/// first, which is an accident of the descriptor text rather than a judgement
/// about the text.
///
/// People who work seriously with these corpora do have a view about that, and
/// it is usually a view about the collection as a whole rather than about
/// individual works: a preference for Perseus' apparatus, or for the critical
/// editions in CSEL. So the preference is expressed once, per collection, and
/// applied wherever the choice comes up.
///
/// It is a preference and not a filter. The other editions stay in the
/// dropdown, in the same order as before - what changes is only which one is
/// already selected when the work opens.
/// </summary>
public static class PreferredEdition
{
    /// <summary>
    /// The index to select in an already-ordered list of editions: the first
    /// from the preferred collection, or the first overall.
    ///
    /// Falling back rather than failing is the whole design. A preference can
    /// name a collection this work does not appear in, or one that is no
    /// longer installed at all, and neither is an error - it just means this
    /// particular work has nothing to apply the preference to. Returns -1 for
    /// an empty list, which callers already have to handle.
    /// </summary>
    public static int IndexOfDefault(IReadOnlyList<Edition> editions, string? preferredCollection)
    {
        if (editions.Count == 0) return -1;
        if (string.IsNullOrWhiteSpace(preferredCollection)) return 0;

        for (var i = 0; i < editions.Count; i++)
        {
            if (string.Equals(editions[i].Collection, preferredCollection.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }
}
