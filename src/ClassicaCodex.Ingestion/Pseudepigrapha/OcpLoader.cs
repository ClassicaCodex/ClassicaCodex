using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Pseudepigrapha;

/// <summary>
/// Reads one version of a text from the Online Critical Pseudepigrapha's XML,
/// the edition its file names first.
///
/// <b>One reading, not an apparatus.</b> Each verse is a &lt;unit&gt; holding
/// the readings of every manuscript tradition OCP has, option 0 first; the
/// Testament of Solomon carries McCown's text beside the recensions he
/// printed separately (HI, L, VW). Asked for a manuscript siglum, this keeps
/// that one's reading and falls back to option 0, so the result is the
/// editor's continuous text rather than all of them run together.
///
/// <b>Only the versions OCP did not write.</b> Since September 2026 the files
/// also carry English and French "versions" credited to OCP itself, added for
/// thirty-eight texts in one commit and nowhere said to be checked. The
/// caller names the version it wants by title, and these never are.
/// </summary>
public static class OcpLoader
{
    public const string Root =
        "https://raw.githubusercontent.com/OnlineCriticalPseudepigrapha/Online-Critical-Pseudepigrapha/master/static/docs/";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// The passages of one version, cited by the path of div numbers -
    /// "1.1" in the Testament of Solomon, "Reuben.1.1" in the Testaments of
    /// the Twelve Patriarchs - with the source edition's own bibliography.
    /// </summary>
    public static (List<(string Citation, string Text)> Passages, string? Bibliography) Load(
        string path, string versionTitle, string? manuscript = null)
    {
        var document = XDocument.Load(path);
        var version = document.Descendants("version")
            .FirstOrDefault(v => (string?)v.Attribute("title") == versionTitle)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no version titled \"{versionTitle}\".");

        var bibliography = version.Descendants("ms")
            .Where(ms => manuscript == null || (string?)ms.Attribute("abbrev") == manuscript)
            .Select(ms => ms.Element("bibliography")?.Value.Trim())
            .FirstOrDefault(b => !string.IsNullOrEmpty(b));

        var passages = new List<(string, string)>();
        var text = version.Element("text");
        if (text != null) Walk(text, null, manuscript, passages);
        return (passages, bibliography);
    }

    private static void Walk(XElement element, string? prefix, string? manuscript, List<(string, string)> passages)
    {
        foreach (var div in element.Elements("div"))
        {
            var number = ((string?)div.Attribute("number") ?? string.Empty).Trim();
            var citation = prefix == null ? number : $"{prefix}.{number}";

            var units = div.Elements("unit").ToList();
            if (units.Count > 0)
            {
                var verse = string.Join(" ", units.Select(u => Reading(u, manuscript)).Where(r => r.Length > 0));
                if (verse.Length > 0) passages.Add((citation, verse));
            }

            Walk(div, citation, manuscript, passages);
        }
    }

    private static string Reading(XElement unit, string? manuscript)
    {
        var readings = unit.Elements("reading").ToList();
        var chosen = (manuscript == null
                         ? null
                         : readings.FirstOrDefault(r => ((string?)r.Attribute("mss") ?? "")
                             .Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(manuscript)))
                     ?? readings.FirstOrDefault(r => (string?)r.Attribute("option") == "0")
                     ?? readings.FirstOrDefault();

        return chosen == null ? string.Empty : Whitespace.Replace(chosen.Value, " ").Trim();
    }
}
