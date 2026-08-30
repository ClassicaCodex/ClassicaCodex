namespace ClassicaCodex.Core.Models;

/// <summary>
/// A named search someone wants to run again - the query text and every
/// filter alongside it.
///
/// Stored by name rather than by id throughout: the author is kept as a
/// name, the era as its label, and neither is resolved until the search is
/// loaded. That matters because a saved search is meant to outlive the
/// library it was written against. Author ids are assigned by the database
/// and change completely when a corpus is re-ingested into a fresh file,
/// so a search keyed on them would silently start pointing at a different
/// author - the same lesson the annotations taught, applied before it can
/// bite rather than after.
/// </summary>
public class RecentSearch
{
    public int RecentSearchId { get; set; }

    /// <summary>What the user called it. Unique - saving over an existing name replaces it.</summary>
    public string Name { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    /// <summary>Stored as the enum name, so a reordering of the enum can't rewrite saved searches.</summary>
    public string MatchMode { get; set; } = "Contains";

    /// <summary>Comma-separated language codes; empty means no language filter.</summary>
    public string Languages { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated corpus namespaces - "greekLit", "latinLit" - or empty
    /// for no namespace filter.
    ///
    /// ALWAYS EMPTY TODAY, AND NOT DEAD. SearchFilters.Corpora is a real,
    /// tested filter on the author's namespace; what it lacks is a control in
    /// the search window, so nothing ever puts a value here. The column stays
    /// because it is the place that filter's setting would be persisted the
    /// day it gets one, and dropping it would mean rebuilding this table now
    /// and rebuilding it again later.
    ///
    /// It is NOT the collection filter. Namespace and collection are different
    /// axes and that is the whole reason Collections below exists: CSEL and
    /// classical Latin are both "latinLit", so "search only CSEL" cannot be
    /// said in namespaces at all. Anything meaning a collection goes there.
    /// </summary>
    public string Corpora { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated collection keys - "csel", "patrologia-latina" - or empty for no
    /// collection filter. Keys rather than ids, like the author name and era label
    /// beside them, so the entry still means the same thing after a re-ingest.
    /// </summary>
    public string Collections { get; set; } = string.Empty;

    /// <summary>True for originals only, false for translations only, null for both.</summary>
    public bool? OriginalsOnly { get; set; }

    /// <summary>Author name, not id - see the note on this class.</summary>
    public string? AuthorName { get; set; }

    public string? TagName { get; set; }

    public bool BookmarkedOnly { get; set; }

    /// <summary>The era's label as shown in the picker, resolved to authors at load time.</summary>
    public string? EraLabel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public override string ToString() => Name;
}
