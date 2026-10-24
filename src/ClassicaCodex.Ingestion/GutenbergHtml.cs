using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Project Gutenberg's HTML editions, which the Distributed Proofreaders mark
/// up by class - footnotes, page numbers, verse lines, small capitals - and
/// which read as XML once their body is cut out and the void elements closed.
/// </summary>
public static class GutenbergHtml
{
    private static readonly Regex Void = new(@"<(br|hr|img|meta|link|wbr|col|input)\b([^>]*?)/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The ebook's HTML on Gutenberg's mirror, which shelves books by the digits of their number.</summary>
    public static string MirrorUrl(int ebook)
    {
        var digits = ebook.ToString();
        var shelf = string.Join("/", digits[..^1].Select(c => c.ToString()));
        return $"https://gutenberg.pglaf.org/{shelf}/{ebook}/{ebook}-h/{ebook}-h.htm";
    }

    /// <summary>The page's body as XML.</summary>
    public static XElement Body(string html)
    {
        var open = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (open < 0 || close < 0) throw new InvalidDataException("No body in the HTML");

        // The mirror's copies write &nbsp; where Gutenberg's own write the
        // character, and XML knows only its five built-in entities.
        var body = Void.Replace(html[open..(close + "</body>".Length)], "<$1$2/>");
        return XElement.Parse(XmlEntitySanitizer.Sanitize(body), LoadOptions.None);
    }
}
