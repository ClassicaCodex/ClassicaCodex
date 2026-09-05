namespace ClassicaCodex.Core;

/// <summary>
/// What is known about a passage of an ancient text, in the terms a reference
/// needs.
///
/// Deliberately not a <see cref="BibliographyRecord"/> to begin with, because
/// the two are not the same shape. A record describes a modern article
/// somebody imported from Zotero: it has authors, a journal, a year. An
/// ancient text cited out of this library has an author with a floruit rather
/// than a publication year, an edition identified by a CTS URN rather than a
/// volume, and a citation reference rather than a page range.
///
/// It becomes a record before it is written, so that both go out through the
/// one writer that already exists and is already read back by this
/// application's own importer.
/// </summary>
public sealed record PassageReference(
    string AuthorName,
    string WorkTitle,
    string? PassageRef = null,
    string? EditionUrn = null,
    string? Translator = null,
    string? CollectionName = null,
    string? AuthorFloruit = null)
{
    /// <summary>Perseus's own reader, which is what resolves a CTS URN.</summary>
    private const string ScaifeReader = "https://scaife.perseus.org/reader/";

    /// <summary>
    /// The same facts as a bibliography record, so BibliographyExport can
    /// write them.
    ///
    /// Two rules govern what does NOT get filled in, and both are about not
    /// inventing scholarship:
    ///
    ///  - No year. A reference manager wants one and an ancient work does not
    ///    have one; the author's floruit goes in the note, where it reads as
    ///    what it is, rather than into Year where it would be read as a date
    ///    of publication. A missing year is honest and a wrong one is not.
    ///  - A URL only where one resolves. CTS URNs resolve at Scaife; Menota's
    ///    identifiers and the Renaissance collection's are not CTS and get no
    ///    link, because a link that goes nowhere is worse than none - it
    ///    looks like one.
    /// </summary>
    public BibliographyRecord ToRecord()
    {
        var notes = new List<string>();
        if (Trimmed(EditionUrn) is { } urn) notes.Add(urn);
        if (Trimmed(Translator) is { } translator) notes.Add($"trans. {translator}");
        if (Trimmed(AuthorFloruit) is { } floruit) notes.Add($"author {floruit}");
        notes.Add("retrieved with Classica Codex");

        // CHAP where a collection is known, BOOK otherwise - and that is a
        // choice about one field rather than about genre. Only the chapter
        // types carry a container in BibTeX, so a plain @book would have
        // dropped the collection silently, and the collection is exactly what
        // separates a work as CSEL prints it from the same work as Migne
        // does. The text really is published within a collection, so the type
        // is honest as well as convenient.
        var entryType = Trimmed(CollectionName) == null ? "BOOK" : "CHAP";

        return new BibliographyRecord(
            ImportFormat: "ClassicaCodex",
            EntryType: entryType,
            CiteKey: CiteKey(),
            Title: Trimmed(WorkTitle) ?? string.Empty,
            Authors: Trimmed(AuthorName) is { } author ? new[] { author } : Array.Empty<string>(),
            Year: null,
            ContainerTitle: Trimmed(CollectionName),
            Volume: null,
            Issue: null,
            // The locator, which is what "pages" means for a text nobody
            // paginates. A hyphenated one is treated as a range by both
            // writers - BibTeX gets an en-dash, RIS a start and an end -
            // which is what a range of lines is.
            Pages: Trimmed(PassageRef),
            Publisher: null,
            Doi: null,
            Url: ResolvableUrl(),
            Isbn: null,
            Abstract: string.Join("; ", notes),
            Keywords: Array.Empty<string>());
    }

    /// <summary>
    /// A key a person can recognise in a .bib file: author, work and passage.
    /// The dots in a locator survive, since a classical reference without
    /// them is a different reference.
    /// </summary>
    public string CiteKey()
    {
        var parts = new[] { AuthorName, WorkTitle, PassageRef }
            .Select(Trimmed)
            .Where(p => p != null)
            .Select(p => Sanitize(p!))
            .Where(p => p.Length > 0)
            .ToList();

        return parts.Count == 0 ? "ClassicaCodex" : string.Join(":", parts);
    }

    public string? ResolvableUrl()
    {
        var urn = Trimmed(EditionUrn);
        return urn != null && urn.StartsWith("urn:cts:", StringComparison.OrdinalIgnoreCase)
            ? ScaifeReader + urn
            : null;
    }

    private static string Sanitize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.') builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        return builder.ToString().Trim('-', '.');
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static partial class BibliographyExport
{
    /// <summary>
    /// Passages as BibTeX, through the same writer the research bibliography
    /// uses - so what this application writes is what it can read back, and
    /// there is one implementation of each format rather than two that drift.
    /// </summary>
    public static string ToBibTeX(IEnumerable<PassageReference> passages) =>
        ToBibTeX(passages.Select(p => p.ToRecord()));

    public static string ToRis(IEnumerable<PassageReference> passages) =>
        ToRis(passages.Select(p => p.ToRecord()));
}
