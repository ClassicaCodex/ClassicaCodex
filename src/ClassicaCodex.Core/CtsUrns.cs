namespace ClassicaCodex.Core;

/// <summary>
/// Putting an edition's identifier back into a form somebody can cite.
///
/// Editions are stored with the namespace stripped off: the Aeneid's Perseus
/// text is "phi0690.phi003.perseus-lat1" rather than
/// "urn:cts:latinLit:phi0690.phi003.perseus-lat1". That is fine as a key, since
/// nothing collides, and useless in a citation - a reader who copies it cannot
/// resolve it, and nothing in the string says which namespace it belongs to.
///
/// The work above it does keep the whole thing, so the namespace is not lost,
/// only stored one row up. This puts the two back together.
///
/// Deliberately conservative. It returns the edition's identifier untouched
/// unless the work's is a full CTS URN and the edition's genuinely extends it -
/// so Menota's "urn:menota:..." identifiers, which are already complete, and
/// the Renaissance collection's "engLit:renaissance:..." ones, which are not
/// CTS at all, are left alone rather than being given a namespace that would be
/// a guess. On the current library that qualifies 5,050 of 5,382 editions and
/// invents nothing for the rest.
/// </summary>
public static class CtsUrns
{
    private const string Prefix = "urn:cts:";

    /// <summary>
    /// The edition's identifier with its namespace restored, where that can be
    /// done from the work's without guessing.
    /// </summary>
    public static string Qualify(string? workUrn, string? editionUrn)
    {
        if (string.IsNullOrWhiteSpace(editionUrn)) return string.Empty;

        var edition = editionUrn.Trim();

        // Already complete - Menota's, and anything a future collection stores
        // in full.
        if (edition.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)) return edition;

        if (string.IsNullOrWhiteSpace(workUrn)) return edition;
        var work = workUrn.Trim();
        if (!work.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return edition;

        // urn:cts:latinLit:phi0690.phi003 -> namespace "latinLit", work id
        // "phi0690.phi003". The identifier is whatever follows the namespace,
        // and a CTS namespace carries no colon of its own.
        var afterPrefix = work[Prefix.Length..];
        var colon = afterPrefix.IndexOf(':');
        if (colon <= 0 || colon == afterPrefix.Length - 1) return edition;

        var ns = afterPrefix[..colon];
        var workId = afterPrefix[(colon + 1)..];
        if (workId.Length == 0) return edition;

        // The edition has to be this work's, not merely similar to it. Anything
        // else and the namespace would be asserted rather than known.
        if (!edition.StartsWith(workId + ".", StringComparison.Ordinal)) return edition;

        return $"{Prefix}{ns}:{edition}";
    }
}
