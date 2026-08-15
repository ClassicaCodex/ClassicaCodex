using ClassicaCodex.Core;

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

    /// <summary>
    /// How securely this work is attributed to the author it is filed under.
    ///
    /// Defaults to Accepted, which is what almost everything is. A built-in
    /// catalog seeds the well-known exceptions on ingest; see DisputedWorkData.
    /// </summary>
    public AttributionStatus AttributionStatus { get; set; } = AttributionStatus.Accepted;

    /// <summary>Why the attribution is doubted, in one line.</summary>
    public string? AttributionNote { get; set; }

    /// <summary>
    /// Whether a person set this rather than the catalog.
    ///
    /// THE POINT OF THE WHOLE DESIGN. Defaults come from a hand-curated table
    /// that will grow, and corpora get re-ingested. Without this flag, either
    /// of those would silently overwrite a judgement somebody made on purpose -
    /// so a decision could never be made to stick, and the feature would be
    /// worse than not having it. Once true, nothing automatic touches this
    /// work's attribution again.
    /// </summary>
    public bool AttributionSetByUser { get; set; }

    /// <summary>Convenience for the common filter.</summary>
    public bool IsDoubted => AttributionStatus != AttributionStatus.Accepted;
}
