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

    /// <summary>
    /// The orthographic level this edition's text is transcribed at:
    /// "normalised", "diplomatic", or null for a printed edition where the
    /// question doesn't arise.
    ///
    /// This exists to keep diplomatic transcriptions out of anything that
    /// compares word frequencies. A diplomatic text follows each scribe's own
    /// spelling rather than a dictionary, so Delta between two of them
    /// measures the scribes rather than the authors. None of the Menota
    /// manuscripts tested carries a normalised level at all: zero me:norm
    /// across roughly 140,000 words.
    ///
    /// Null rather than "normalised" as the default, because a printed
    /// critical edition is neither - the editor has already made the
    /// orthography consistent, and saying "normalised" here would be claiming
    /// a Menota encoding level the file does not have.
    /// </summary>
    public string? Orthography { get; set; }
}
