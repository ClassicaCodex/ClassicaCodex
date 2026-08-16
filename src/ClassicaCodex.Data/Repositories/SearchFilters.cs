namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// How a passage's text should be matched.
/// </summary>
public enum SearchMatchMode
{
    /// <summary>
    /// Anywhere in a line, as typed. "arm" finds "arma" and also "harm" -
    /// the broadest and the default, because it's the one that never
    /// silently misses something.
    /// </summary>
    Contains,

    /// <summary>
    /// Bounded by non-letters on both sides, so "arm" finds "arm" but not
    /// "arma". The narrowest, and the one to reach for when a short query is
    /// drowning in incidental substring hits.
    /// </summary>
    WholeWord,

    /// <summary>
    /// Every word of the query must appear somewhere in the line, in any
    /// order and not necessarily adjacent - "Zeus Athena" finds lines naming
    /// both. Turns the search box into a way of asking about co-occurrence,
    /// which the corpus rewards.
    /// </summary>
    AllWords
}

/// <summary>
/// Everything the search form can narrow a search by.
///
/// Every field is optional and an unset field means "don't narrow by this",
/// so the default instance searches the whole library exactly as the old
/// single search box did. That matters: the filters are here to let someone
/// ask a sharper question, not to make the simple question harder to ask.
/// </summary>
public sealed class SearchFilters
{
    public string Query { get; set; } = string.Empty;

    public SearchMatchMode MatchMode { get; set; } = SearchMatchMode.Contains;

    /// <summary>
    /// Edition languages to include - "grc", "lat", "eng". Empty means all.
    /// Held as a set rather than a single value because "Greek or Latin but
    /// not the English translations" is a normal thing to want.
    /// </summary>
    public HashSet<string> Languages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Corpus namespaces to include - "greekLit", "latinLit", "engLit".
    /// Empty means all.
    /// </summary>
    public HashSet<string> Corpora { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which downloaded collections to search, named by the folder each was
    /// ingested from. Empty means all.
    ///
    /// Deliberately not the same axis as <see cref="Corpora"/> above, which asks
    /// what language tradition a text belongs to. Both CSEL and the classical
    /// Latin texts are "latinLit", and both Greek collections are "greekLit", so
    /// the namespace cannot answer "search only CSEL" - the question someone asks
    /// the moment they have installed more than one collection in a language.
    ///
    /// The download folder is what separates them, and it is already trusted for
    /// exactly this: every setup step decides whether its collection is installed
    /// by counting editions whose source path sits under its own folder.
    /// </summary>
    public HashSet<string> Collections { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True to search only original-language editions, false for only
    /// translations, null for both. Distinct from the language filter: an
    /// English original (Shakespeare) and an English translation of Homer
    /// are both "eng" but are not the same kind of thing.
    /// </summary>
    public bool? OriginalsOnly { get; set; }

    /// <summary>Restrict to one author. Null for all.</summary>
    public int? AuthorId { get; set; }

    /// <summary>Restrict to one work. Null for all.</summary>
    public int? WorkId { get; set; }

    /// <summary>
    /// Only passages carrying this tag. Null for all. Searching inside your
    /// own tagged material is a different question from searching the
    /// library, and one the app previously had no way to ask.
    /// </summary>
    public string? TagName { get; set; }

    /// <summary>Only passages that have at least one bookmark on them.</summary>
    public bool BookmarkedOnly { get; set; }

    /// <summary>
    /// Author ids to restrict to, from the era filter. Null means no era
    /// restriction; empty means the era matched no authors at all, which is
    /// a real answer and not the same as "don't filter".
    ///
    /// Resolved by the caller rather than in SQL because author dates aren't
    /// in the database - they're a curated lookup table in the UI layer.
    /// </summary>
    public IReadOnlyCollection<int>? EraAuthorIds { get; set; }

    public int MaxResults { get; set; } = TextNodeRepository.DefaultMaxResults;

    public bool HasAnyNarrowing =>
        Languages.Count > 0 || Corpora.Count > 0 || OriginalsOnly != null || AuthorId != null
        || WorkId != null || TagName != null || BookmarkedOnly || EraAuthorIds != null
        || MatchMode != SearchMatchMode.Contains;
}
