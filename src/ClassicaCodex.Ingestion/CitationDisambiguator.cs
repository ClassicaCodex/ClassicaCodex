using System.Text;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Makes citation references unique within one edition.
///
/// A citation is an identity, not a label: annotations, bookmarks, tags,
/// apparatus entries and bilingual pairing all resolve through
/// (EditionId, CitationRef). Two nodes sharing a reference means a bookmark
/// that lands on either one, an apparatus note that attaches to both, and a
/// translation that can only ever pair with the first.
///
/// Nothing enforces this in the database - IX_TextNodes_Edition_Citation is a
/// plain index, deliberately, because a unique constraint would turn a
/// collision into a failed ingest rather than a disambiguated reference. That
/// makes it this class's job.
///
/// Collisions arise differently in each corpus and neither is a defect in the
/// source:
///
///   Menota - AM 63 fol has two page breaks both numbered 3, so two different
///   lines are both cited "3.5".
///
///   TEI - sparse numbering. Shakespeare numbers every tenth line, so a scene
///   yields nine references from the positional counter, then "10" from
///   @n="10", then the counter's own "10" one line later. Troilus collides 216
///   times this way, Pericles 152.
///
/// Both paths used to answer this separately: Menota had this logic and the
/// TEI parser had nothing, so 502 references across the corpora pointed at two
/// nodes each and nothing reported it. One implementation, because the
/// off-by-one in Suffix() below was found once and should not have to be found
/// again.
/// </summary>
internal sealed class CitationDisambiguator
{
    private readonly Dictionary<string, int> _used = new(StringComparer.Ordinal);

    /// <summary>
    /// The first use of a reference keeps it unchanged; later ones get a
    /// letter, the way an editor distinguishes 3a from 3b.
    ///
    /// The first-wins order matters: it means the reference a reader is most
    /// likely to cite - the one that appears first in the text - is the plain
    /// one.
    /// </summary>
    public string Unique(string reference)
    {
        if (!_used.TryGetValue(reference, out var seen))
        {
            _used[reference] = 1;
            return reference;
        }

        // Keep going until the letter lands somewhere unoccupied, and record
        // the result so nothing else can land on it either.
        //
        // Editors already number lines with letters - Aeschylus has a real
        // line 1407b in the Agamemnon - so appending "a" to a colliding "1407"
        // can mint a reference that another line in the same edition holds
        // legitimately. That would be worse than the collision it was fixing:
        // two nodes sharing a reference again, and this time one of them
        // looking exactly like an editor's a/b distinction.
        //
        // It does not happen in the present corpora - 506 disambiguations,
        // none of them landing on a real number - which is precisely why it
        // would go unnoticed if a later corpus made it happen.
        var attempt = seen;
        string candidate;

        do
        {
            candidate = $"{reference}{Suffix(attempt - 1)}";
            attempt++;
        }
        while (_used.ContainsKey(candidate));

        _used[reference] = attempt;
        _used[candidate] = 1;
        return candidate;
    }

    /// <summary>Forgets everything seen so far. Called between editions.</summary>
    public void Reset() => _used.Clear();

    /// <summary>
    /// a, b, ... z, aa, ab, and so on.
    ///
    /// This used to clamp at z, so a twenty-seventh collision and every one
    /// after it came out identical - which is the exact duplication this class
    /// exists to prevent, reappearing silently once the range ran out. Nothing
    /// in the Menota corpus collides more than nine times, so it never fired
    /// there; the TEI corpus does reach it.
    /// </summary>
    private static string Suffix(int index)
    {
        var sb = new StringBuilder();

        do
        {
            sb.Insert(0, (char)('a' + index % 26));
            index = index / 26 - 1;
        }
        while (index >= 0);

        return sb.ToString();
    }
}
