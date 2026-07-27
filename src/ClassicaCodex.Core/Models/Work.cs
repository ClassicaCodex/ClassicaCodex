namespace ClassicaCodex.Core.Models;

/// <summary>
/// A single work by an author (e.g. the Iliad, Metamorphoses).
/// </summary>
public class Work
{
    public int WorkId { get; set; }

    public int AuthorId { get; set; }

    /// <summary>
    /// Full work-level CTS URN, e.g. "urn:cts:greekLit:tlg0012.tlg001"
    /// </summary>
    public string CtsUrn { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable citation scheme, e.g. "Book.Line" - derived from the
    /// TEI refsDecl / cRefPattern when available, else inferred from div nesting.
    /// </summary>
    public string? CitationScheme { get; set; }
}
