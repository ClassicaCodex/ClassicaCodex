namespace ClassicaCodex.Core.Models;

/// <summary>
/// Maps one inflected word form to a dictionary headword (lemma) - e.g.
/// λόγου -> λόγος, or "amavit" -> "amo". A single form can legitimately map
/// to several lemmas (genuine ambiguity that no lemmatizer fully resolves),
/// so this is a many-to-many relationship rather than a unique key on Form.
/// </summary>
public class Lemma
{
    public long LemmaId { get; set; }

    /// <summary>The inflected form as it appears in a text.</summary>
    public string Form { get; set; } = string.Empty;

    /// <summary>
    /// Accent/diacritic-stripped, lowercased version of Form, used for
    /// matching - Perseus texts aren't perfectly consistent about accents
    /// and precomposed vs combining Unicode, so matching on the bare form is
    /// far more reliable than matching the decorated one.
    /// </summary>
    public string NormalizedForm { get; set; } = string.Empty;

    /// <summary>The dictionary headword.</summary>
    public string Headword { get; set; } = string.Empty;

    /// <summary>"grc" or "lat".</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Morphological tag where available (e.g. "n-s---mn-").</summary>
    public string? PartOfSpeech { get; set; }
}
