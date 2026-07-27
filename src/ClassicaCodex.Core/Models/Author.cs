namespace ClassicaCodex.Core.Models;

/// <summary>
/// A classical author (e.g. Homer, Ovid, Vergil).
/// </summary>
public class Author
{
    public int AuthorId { get; set; }

    /// <summary>
    /// CTS namespace + author-level URN fragment, e.g. "urn:cts:greekLit:tlg0012"
    /// This is the stable key we ingest against - re-running ingestion updates
    /// rather than duplicates, keyed on this.
    /// </summary>
    public string CtsUrn { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// "greekLit", "latinLit", etc. - maps to the Perseus repo/namespace this came from.
    /// </summary>
    public string Namespace { get; set; } = string.Empty;

    public string? Language { get; set; }
}
