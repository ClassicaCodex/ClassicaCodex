using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Pulls the publication metadata out of a TEI file's header.
///
/// The ingest reads the body of every file and used to discard the header
/// entirely, so who edited a text and which printed edition it came from -
/// the things a reader needs before quoting it - were never recorded
/// anywhere. This is what fills EditionHeaders during ingest.
///
/// Two entry points, for two different callers. Read(XDocument) is the
/// ingest path, taking a document that's already been parsed. TryRead(path)
/// re-reads from disk, and exists only for editions ingested before headers
/// were stored: it lets an existing library show this without being
/// re-ingested first, and stops being reached once it has been.
/// </summary>
public static class TeiHeaderReader
{
    /// <summary>
    /// Reads the header straight from the source file - the fallback path,
    /// for editions whose header wasn't captured at ingest.
    ///
    /// Null when the file is missing, unreadable, or not parseable as XML.
    /// All three are ordinary here rather than faults: the corpus folders
    /// are the user's own downloads and may well have been cleaned up, and
    /// nothing else in the app needs them once ingest is done.
    /// </summary>
    public static EditionHeader? TryRead(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;

        try
        {
            // Same sanitising the ingest parser applies: the P4 files carry a
            // DOCTYPE with an external parameter entity that can't be
            // resolved offline, which would otherwise fail the parse outright.
            var sanitized = XmlEntitySanitizer.Sanitize(File.ReadAllText(sourcePath));
            return Read(XDocument.Parse(sanitized, LoadOptions.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the header out of a document that's already been parsed.
    ///
    /// The ingest path uses this rather than TryRead: it has the file open
    /// and parsed already - twice over, in fact, once for the body and once
    /// to inspect the edition - so re-reading it a third time to pull the
    /// header would be pure waste on a run that takes hours.
    /// </summary>
    public static EditionHeader? Read(XDocument doc)
    {
        try
        {
            // Matched by local name, not namespace - P4 files have no
            // namespace and P5 files do, and this needs to read both.
            var header = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "teiHeader");
            if (header == null) return null;

            var fileDesc = Child(header, "fileDesc");
            var titleStmt = fileDesc == null ? null : Child(fileDesc, "titleStmt");
            var publicationStmt = fileDesc == null ? null : Child(fileDesc, "publicationStmt");
            var sourceDesc = fileDesc == null ? null : Child(fileDesc, "sourceDesc");
            var editionStmt = fileDesc == null ? null : Child(fileDesc, "editionStmt");

            var info = new EditionHeader
            {
                Title = titleStmt == null ? null : Text(Child(titleStmt, "title")),
                Author = titleStmt == null ? null : Text(Child(titleStmt, "author")),
                Responsibilities = titleStmt == null
                    ? Array.Empty<string>()
                    : ReadResponsibilities(titleStmt),
                Publisher = publicationStmt == null ? null : Text(Child(publicationStmt, "publisher")),
                PublicationDate = publicationStmt == null ? null : Text(Child(publicationStmt, "date")),
                PublicationPlace = publicationStmt == null ? null : Text(Child(publicationStmt, "pubPlace")),
                Availability = publicationStmt == null ? null : Text(Child(publicationStmt, "availability")),
                EditionStatement = editionStmt == null ? null : CollapseComposite(editionStmt),

                // sourceDesc is usually a <biblStruct> or a prose <p>, so it
                // gets the composite treatment rather than plain Text - see
                // CollapseComposite.
                SourceDescription = sourceDesc == null ? null : CollapseComposite(sourceDesc)
            };

            return info.IsEmpty ? null : info;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    /// <summary>
    /// Each respStmt as "role: name" where the file gives both - the roles
    /// are the interesting part ("Editor", "Translator", "Funder"), and a
    /// bare list of names without them reads as noise.
    /// </summary>
    private static List<string> ReadResponsibilities(XElement titleStmt)
    {
        var results = new List<string>();

        foreach (var respStmt in titleStmt.Descendants().Where(e => e.Name.LocalName == "respStmt"))
        {
            var role = Text(respStmt.Elements().FirstOrDefault(e => e.Name.LocalName == "resp"));
            var names = respStmt.Elements()
                .Where(e => e.Name.LocalName is "name" or "persName")
                .Select(e => Text(e))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (names.Count == 0) continue;

            var joined = string.Join(", ", names);
            results.Add(string.IsNullOrWhiteSpace(role) ? joined : $"{role.TrimEnd(':')}: {joined}");
        }

        // Also the plain editor/funder/sponsor elements some files use
        // instead of a respStmt.
        foreach (var localName in new[] { "editor", "funder", "sponsor", "principal" })
        {
            foreach (var element in titleStmt.Elements().Where(e => e.Name.LocalName == localName))
            {
                var value = Text(element);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    results.Add($"{char.ToUpperInvariant(localName[0])}{localName[1..]}: {value}");
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Text for an element built out of other elements, joined so the pieces
    /// stay legible.
    ///
    /// Taking .Value on a biblStruct concatenates every descendant's text
    /// with no separator at all, because there's no whitespace between the
    /// tags themselves - "&lt;title&gt;Homeri Opera&lt;/title&gt;&lt;author&gt;Homer&lt;/author&gt;"
    /// comes back as "Homeri OperaHomer", and a publisher runs straight into
    /// its year as "Oxford University Press1920". That's the single most
    /// useful line in this whole view, so it's worth assembling properly:
    /// each leaf element's own text, in document order, comma-separated.
    ///
    /// Falls back to plain text for an element with no child elements, which
    /// is the other shape sourceDesc commonly takes - a prose paragraph.
    /// </summary>
    private static string? CollapseComposite(XElement element)
    {
        var leaves = element.Descendants().Where(e => !e.Elements().Any()).ToList();
        if (leaves.Count == 0) return Text(element);

        var parts = new List<string>();
        foreach (var leaf in leaves)
        {
            var value = string.Join(" ", leaf.Value.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            // Repeated values are common - an author named in both the
            // monogr and its imprint, say - and reading the same name twice
            // in one line looks like a bug.
            if (value.Length > 0 && !parts.Contains(value, StringComparer.Ordinal))
            {
                parts.Add(value);
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// An element's text with its internal whitespace collapsed. TEI wraps
    /// freely across lines and indents deeply, so the raw value of anything
    /// larger than a leaf arrives full of newlines and runs of spaces.
    /// </summary>
    private static string? Text(XElement? element)
    {
        if (element == null) return null;

        var value = string.Join(" ", element.Value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return value.Length == 0 ? null : value;
    }
}
