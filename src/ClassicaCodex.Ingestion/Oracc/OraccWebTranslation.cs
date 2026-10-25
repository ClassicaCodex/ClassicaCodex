using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Oracc;

/// <summary>
/// The English translation of an Oracc text, read from the text's page on the
/// website - the one thing the open-data download leaves out (see
/// <see cref="OraccProjectReader"/>).
///
/// ONLY WHERE THE SITE LICENSES IT. A project's download being CC0 says
/// nothing about the translation on its website, which is often a published
/// book's. This is used only for projects whose own pages release their
/// translations under an open licence - see
/// <see cref="OraccProjectChoice.TranslationCredit"/> - and the credit goes
/// onto the edition.
///
/// THE PAGE IS XHTML. Oracc generates it from XML and it parses as XML, so
/// this reads it as a document rather than picking at it with patterns. The
/// page is a table: one row per line of the text, the row's id the same line
/// reference the download gives (Q002705.1l for line 1 of Maqlû I), and the
/// translation in the row's cells classed "xtr". A translation that runs over
/// several lines sits in the first of them with a rowspan; a line printed in
/// two columns - the lists of Šurpu - has a cell for each, read in order.
/// </summary>
public static class OraccWebTranslation
{
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";

    public static string PageUrl(string projectPath, string textId) =>
        $"https://oracc.museum.upenn.edu/{projectPath}/{textId}/html";

    /// <summary>
    /// Each line's translation, by line reference. Lines the translation does
    /// not reach on their own - the later lines of a rowspan, rulings, broken
    /// lines - are absent rather than empty.
    /// </summary>
    public static Dictionary<string, string> ReadLineTranslations(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(html)) return result;

        // The doctype names no DTD, but an XML parser still balks at it; it is
        // the only thing in the page that is not XML.
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", string.Empty, RegexOptions.IgnoreCase);

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(html),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return result;
        }

        foreach (var row in document.Descendants(Xhtml + "tr"))
        {
            var id = (string?)row.Attribute("id");
            if (string.IsNullOrEmpty(id) || !HasClass(row, "l")) continue;

            var cells = row.Elements(Xhtml + "td").Where(td => HasClass(td, "xtr")).ToList();
            if (cells.Count == 0) continue;

            var text = Collapse(string.Join(" ", cells.Select(CellText)));
            if (text.Length > 0) result[id] = text;
        }

        return result;
    }

    private static bool HasClass(XElement e, string name) =>
        ((string?)e.Attribute("class"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name) == true;

    /// <summary>A cell's words, without the "(1)" line label Oracc prints at its head.</summary>
    private static string CellText(XElement cell)
    {
        var sb = new StringBuilder();

        void Walk(XElement e)
        {
            foreach (var node in e.Nodes())
            {
                switch (node)
                {
                    case XText t:
                        sb.Append(t.Value);
                        break;
                    case XElement child when HasClass(child, "xtr-label"):
                        break;
                    case XElement child:
                        Walk(child);
                        break;
                }
            }
        }

        Walk(cell);
        return sb.ToString();
    }

    private static string Collapse(string s)
    {
        s = Regex.Replace(s.Replace(' ', ' '), @"\s+", " ").Trim();

        // Spaces the word markup leaves before punctuation: "Night ,".
        return Regex.Replace(s, @"\s+([,.;:!?)\]])", "$1").Replace("( ", "(").Replace("[ ", "[");
    }
}
