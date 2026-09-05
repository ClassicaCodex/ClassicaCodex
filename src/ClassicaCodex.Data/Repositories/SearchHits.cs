namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// A page of search results, plus whether there were more of them than the
/// search was willing to return.
///
/// The reason this exists rather than a bare List: every search path in this
/// app caps its result set somewhere (a LIMIT in SQL, a cap on how many
/// inflected forms get expanded into the query), and until now those caps
/// were entirely silent. "5000 matches" and "the first 5000 of who knows how
/// many" look identical to the person reading the screen, and for a research
/// tool that difference matters - a concordance that quietly stops at 5000
/// isn't a concordance, it's a sample nobody was told about.
///
/// Truncated is true if EITHER end got clipped: too many matching lines, or
/// too many inflected forms to put in one query. Both mean the same thing to
/// the reader ("there is more than this"), so they share one flag rather than
/// making callers reason about which cap they hit.
/// </summary>
/// <param name="Rows">
/// Milestone is last because the tuple is positional: appending means every
/// place that builds a row fails to compile until it supplies one, where
/// inserting it beside CitationRef would have let two strings quietly swap.
/// It is null for most of the corpus - see <see cref="Core.Models.TextNode.Milestone"/>.
/// </param>
public sealed record SearchHits(
    List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text,
          string? Milestone)> Rows,
    bool Truncated)
{
    public static SearchHits Empty { get; } = new(new(), false);

    public int Count => Rows.Count;

    /// <summary>
    /// The count as it should be shown to a person - "5000+" when the cap was
    /// hit, a plain number otherwise. Keeps every status label in the UI from
    /// having to spell out the same conditional.
    /// </summary>
    public string DisplayCount => Truncated
        ? $"{Rows.Count}+"
        : Rows.Count.ToString();
}
