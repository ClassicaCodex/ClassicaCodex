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

        var doc = XDocument.Load(cetsFilePath);
        var textGroup = doc.Root;
        if (textGroup == null) return null;

        var urn = textGroup.Attribute("urn")?.Value;
        var groupName = textGroup.Elements(Ti + "groupname")
            .FirstOrDefault(e => e.Attribute(XNamespace.Xml + "lang")?.Value == "eng")
            ?.Value
            ?? textGroup.Elements(Ti + "groupname").FirstOrDefault()?.Value;

        // A present-but-empty <ti:groupname/> counts as no catalog, not as an author
        // called nothing. Some CTS repositories carry placeholder textgroups whose
        // element is there and blank, and taking that at face value produced a library
        // of nameless authors - rows that cannot be read, searched for, or told apart,
        // and which look like corruption rather than like a corpus that was skipped.
        //
        // Returning null puts them on the same footing as a folder with no catalog at
        // all, which the ingest already knows to pass over.
        if (urn == null || string.IsNullOrWhiteSpace(groupName)) return null;

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

        var doc = XDocument.Load(cetsFilePath);
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
