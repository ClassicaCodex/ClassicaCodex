using System.Xml.Linq;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Every textgroup (author) and work folder in a Perseus canonical-*Lit repo
/// carries a __cts__.xml catalog file describing it. This reads those rather
/// than guessing names from folder paths, since folder names are just CTS
/// URN fragments (e.g. "tlg0012") and not human-readable.
/// </summary>
public class CtsCatalogReader
{
    private static readonly XNamespace Ti = "http://chs.harvard.edu/xmlns/cts";

    public record TextGroupInfo(string Urn, string GroupName);
    public record WorkInfo(string Urn, string Title, string? Language);

    /// <summary>Reads a textgroup-level __cts__.xml, e.g. data/tlg0012/__cts__.xml</summary>
    public TextGroupInfo? ReadTextGroup(string cetsFilePath)
    {
        if (!File.Exists(cetsFilePath)) return null;

        // A catalogue that will not parse is a catalogue that is not there, and
        // is answered the same way - with null, for the caller to recover from.
        //
        // It used to throw straight out of an ingest run that has no try/catch
        // above it until the setup step itself, so one malformed file would
        // abandon the whole corpus and take every author sorting after it. None
        // of the 1,314 catalogues in the Perseus repos is malformed today, which
        // is exactly the kind of fact that holds until it doesn't.
        XDocument doc;
        try { doc = XDocument.Load(cetsFilePath); }
        catch (System.Xml.XmlException) { return null; }

        var textGroup = doc.Root;
        if (textGroup == null) return null;

        var urn = textGroup.Attribute("urn")?.Value;
        var groupName = textGroup.Elements(Ti + "groupname")
            .FirstOrDefault(e => e.Attribute(XNamespace.Xml + "lang")?.Value == "eng")
            ?.Value
            ?? textGroup.Elements(Ti + "groupname").FirstOrDefault()?.Value;

        // An empty <ti:groupname/> is reported as an empty name rather than as no
        // catalog, because in a real corpus it means something: the Patrologia Latina
        // uses it for works that genuinely have no author - councils, appendices,
        // anonymous passions - while naming the rest normally. Treating it as a missing
        // catalog would silently drop those texts; treating it as an author called
        // nothing fills the library with unreadable rows.
        //
        // So the distinction is preserved here and the decision left to the caller,
        // which is the only party that knows what its corpus means by it. See
        // PerseusIngestService.UnnamedTextGroupName.
        // No groupname element at all is a malformed catalog and skipped. An element
        // that is present and empty is not the same thing, and is passed on as an empty
        // name for the caller to decide about.
        if (urn == null || groupName == null) return null;

        return new TextGroupInfo(urn, groupName.Trim());
    }

    /// <summary>
    /// Reads a work-level __cts__.xml, e.g. data/tlg0012/tlg001/__cts__.xml.
    /// Unlike the textgroup file, the root element here IS the &lt;ti:work&gt;
    /// node itself (one work per file) - it's not a wrapper containing child
    /// &lt;ti:work&gt; elements. Its children are &lt;ti:edition&gt;/&lt;ti:translation&gt;
    /// nodes describing each text file in the folder, not further works.
    /// </summary>
    public List<WorkInfo> ReadWorks(string cetsFilePath)
    {
        var results = new List<WorkInfo>();
        if (!File.Exists(cetsFilePath)) return results;

        // Unparseable is treated as absent - see ReadTextGroup.
        XDocument doc;
        try { doc = XDocument.Load(cetsFilePath); }
        catch (System.Xml.XmlException) { return results; }

        var work = doc.Root;
        if (work == null || work.Name != Ti + "work") return results;

        var urn = work.Attribute("urn")?.Value;
        if (urn == null) return results;

        var lang = work.Attribute(XNamespace.Xml + "lang")?.Value;

        var title = work.Elements(Ti + "title")
            .FirstOrDefault(e => e.Attribute(XNamespace.Xml + "lang")?.Value == "eng")
            ?.Value
            ?? work.Elements(Ti + "title").FirstOrDefault()?.Value
            ?? urn;

        results.Add(new WorkInfo(urn, title.Trim(), lang));
        return results;
    }
}
