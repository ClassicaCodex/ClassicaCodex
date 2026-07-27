namespace ClassicaCodex.Core.Models;

/// <summary>
/// A dictionary entry from a lexicon (LSJ for Greek, Lewis &amp; Short for
/// Latin), keyed by headword so Word Study can show what a word means and
/// not merely what its dictionary form is.
/// </summary>
public class Definition
{
    public long DefinitionId { get; set; }

    public string Headword { get; set; } = string.Empty;

    /// <summary>
    /// Accent-stripped, homograph-number-stripped form used for matching -
    /// see WordNormalizer.NormalizeHeadword for why both are needed.
    /// </summary>
    public string NormalizedHeadword { get; set; } = string.Empty;

    /// <summary>"grc" or "lat".</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The entry text, flattened out of the lexicon's markup.</summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>Which lexicon this came from, for attribution in the UI.</summary>
    public string? Source { get; set; }
}
