namespace ClassicaCodex.Core.Models;

public enum EditionKind
{
    Original,
    Translation,
    Unknown
}

/// <summary>
/// One edition of a work - the original Greek/Latin, or a specific translation.
/// A work can have multiple editions (e.g. original + several English translations).
/// </summary>
public class Edition
{
    public int EditionId { get; set; }

    public int WorkId { get; set; }

    /// <summary>
    /// Full edition-level CTS URN, e.g. "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2"
    /// </summary>
    public string CtsUrn { get; set; } = string.Empty;

    public EditionKind Kind { get; set; } = EditionKind.Unknown;

    public string? Language { get; set; }

    public string? Translator { get; set; }

    /// <summary>
    /// Path (relative to the Perseus repo root) this edition was ingested from,
    /// kept for re-ingestion / diffing.
    /// </summary>
    public string? SourcePath { get; set; }
}
