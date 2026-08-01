namespace ClassicaCodex.Core.Models;

/// <summary>
/// What an edition's TEI file states about its own publication - who edited
/// it, which printed edition it was digitised from, publisher, year, licence.
///
/// Separate from Edition itself because it's descriptive rather than
/// structural: nothing in the reader, search, or ingest paths joins against
/// any of it. Every field is optional, and genuinely so - this corpus spans
/// TEI P4 and P5, several contributing projects, and a century of editorial
/// convention, so no element is guaranteed present in any given file. Null
/// means "the file didn't say", never "there is nothing".
/// </summary>
public class EditionHeader
{
    public int EditionId { get; set; }

    public string? Title { get; set; }

    public string? Author { get; set; }

    /// <summary>
    /// Editors, translators, funders and the rest, each already formatted as
    /// "role: name" where the file gave both. Ordered as the file listed
    /// them.
    /// </summary>
    public IReadOnlyList<string> Responsibilities { get; set; } = Array.Empty<string>();

    public string? Publisher { get; set; }

    public string? PublicationDate { get; set; }

    public string? PublicationPlace { get; set; }

    /// <summary>
    /// The printed book behind the digital text. Usually the single most
    /// useful line here - it's what tells a reader which edition they're
    /// actually quoting.
    /// </summary>
    public string? SourceDescription { get; set; }

    public string? EditionStatement { get; set; }

    /// <summary>Licence or availability text, where the file states one.</summary>
    public string? Availability { get; set; }

    public bool IsEmpty =>
        Title == null && Author == null && Responsibilities.Count == 0 && Publisher == null
        && PublicationDate == null && PublicationPlace == null && SourceDescription == null
        && EditionStatement == null && Availability == null;
}
