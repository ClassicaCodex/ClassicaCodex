using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Parses a single Perseus TEI edition file into TextNodes.
///
/// Heads-up: Perseus TEI isn't perfectly uniform across ~2,000 files - most
/// use nested &lt;div type="textpart" subtype="..." n="..."&gt; down to a
/// line/section leaf (&lt;l&gt;, &lt;p&gt;, &lt;said&gt;), which is the case
/// this handles well. A minority use flat &lt;milestone&gt; markers instead
/// of nesting; those fall back to one TextNode per top-level div. If you hit
/// a work that comes out wrong, it's almost always one of those milestone-
/// style outliers and worth special-casing when you find it.
///
/// A separate, unrelated wrinkle: some of the older files (more common in
/// canonical-latinLit) were authored assuming an SGML/HTML-style entity set
/// (e.g. &amp;iacute; for i-acute) without declaring those entities anywhere
/// the XML parser can see, so .NET's loader rejects them as "undeclared
/// entity". Parse() resolves the standard ISO-8859-1/HTML4 named entities
/// itself before handing the text to the XML parser, so those files load
/// instead of throwing.
/// </summary>
public class TeiParser
{
    // Element matching is done by local name throughout rather than by
    // namespace, because Perseus carries both TEI P5 (namespaced) and TEI P4
    // (a <TEI.2> root with no namespace) files, and both need to parse.
    private static readonly HashSet<string> LeafElements = new() { "l", "p", "said", "lg" };
    // Entity names, not just [A-Za-z0-9_]: XML allows '.', '-' and ':' in a
    // Name, and Perseus uses them for its metadata entities (&Perseus.publish;,
    // &fund.NEH;). Matching only \w left those unresolved, so the parser hit
    // them as undeclared entities and skipped the whole file.
    private static readonly Regex EntityPattern = new(@"&([A-Za-z_:][\w.:-]*);", RegexOptions.Compiled);

    // The five entities XML itself declares - never touch these.
    private static readonly HashSet<string> StandardXmlEntities = new() { "amp", "lt", "gt", "quot", "apos" };

    // Standard ISO-8859-1 / HTML4 named entities, plus the common HTML4
    // "special characters" set (smart quotes, dashes, ellipsis, etc.) that
    // show up in editorial apparatus. This is what actually fixes files
    // that reference things like &iacute; or &mdash; with no DTD in sight.
    private static readonly Dictionary<string, char> NamedEntities = new()
    {
        ["nbsp"] = '\u00A0', ["iexcl"] = '\u00A1', ["cent"] = '\u00A2', ["pound"] = '\u00A3',
        ["curren"] = '\u00A4', ["yen"] = '\u00A5', ["brvbar"] = '\u00A6', ["sect"] = '\u00A7',
        ["uml"] = '\u00A8', ["copy"] = '\u00A9', ["ordf"] = '\u00AA', ["laquo"] = '\u00AB',
        ["not"] = '\u00AC', ["shy"] = '\u00AD', ["reg"] = '\u00AE', ["macr"] = '\u00AF',
        ["deg"] = '\u00B0', ["plusmn"] = '\u00B1', ["sup2"] = '\u00B2', ["sup3"] = '\u00B3',
        ["acute"] = '\u00B4', ["micro"] = '\u00B5', ["para"] = '\u00B6', ["middot"] = '\u00B7',
        ["cedil"] = '\u00B8', ["sup1"] = '\u00B9', ["ordm"] = '\u00BA', ["raquo"] = '\u00BB',
        ["frac14"] = '\u00BC', ["frac12"] = '\u00BD', ["frac34"] = '\u00BE', ["iquest"] = '\u00BF',
        ["Agrave"] = '\u00C0', ["Aacute"] = '\u00C1', ["Acirc"] = '\u00C2', ["Atilde"] = '\u00C3',
        ["Auml"] = '\u00C4', ["Aring"] = '\u00C5', ["AElig"] = '\u00C6', ["Ccedil"] = '\u00C7',
        ["Egrave"] = '\u00C8', ["Eacute"] = '\u00C9', ["Ecirc"] = '\u00CA', ["Euml"] = '\u00CB',
        ["Igrave"] = '\u00CC', ["Iacute"] = '\u00CD', ["Icirc"] = '\u00CE', ["Iuml"] = '\u00CF',
        ["ETH"] = '\u00D0', ["Ntilde"] = '\u00D1', ["Ograve"] = '\u00D2', ["Oacute"] = '\u00D3',
        ["Ocirc"] = '\u00D4', ["Otilde"] = '\u00D5', ["Ouml"] = '\u00D6', ["times"] = '\u00D7',
        ["Oslash"] = '\u00D8', ["Ugrave"] = '\u00D9', ["Uacute"] = '\u00DA', ["Ucirc"] = '\u00DB',
        ["Uuml"] = '\u00DC', ["Yacute"] = '\u00DD', ["THORN"] = '\u00DE', ["szlig"] = '\u00DF',
        ["agrave"] = '\u00E0', ["aacute"] = '\u00E1', ["acirc"] = '\u00E2', ["atilde"] = '\u00E3',
        ["auml"] = '\u00E4', ["aring"] = '\u00E5', ["aelig"] = '\u00E6', ["ccedil"] = '\u00E7',
        ["egrave"] = '\u00E8', ["eacute"] = '\u00E9', ["ecirc"] = '\u00EA', ["euml"] = '\u00EB',
        ["igrave"] = '\u00EC', ["iacute"] = '\u00ED', ["icirc"] = '\u00EE', ["iuml"] = '\u00EF',
        ["eth"] = '\u00F0', ["ntilde"] = '\u00F1', ["ograve"] = '\u00F2', ["oacute"] = '\u00F3',
        ["ocirc"] = '\u00F4', ["otilde"] = '\u00F5', ["ouml"] = '\u00F6', ["divide"] = '\u00F7',
        ["oslash"] = '\u00F8', ["ugrave"] = '\u00F9', ["uacute"] = '\u00FA', ["ucirc"] = '\u00FB',
        ["uuml"] = '\u00FC', ["yacute"] = '\u00FD', ["thorn"] = '\u00FE', ["yuml"] = '\u00FF',
        // Common HTML4 special characters seen in editorial apparatus
        ["ndash"] = '\u2013', ["mdash"] = '\u2014', ["lsquo"] = '\u2018', ["rsquo"] = '\u2019',
        ["sbquo"] = '\u201A', ["ldquo"] = '\u201C', ["rdquo"] = '\u201D', ["bdquo"] = '\u201E',
        ["dagger"] = '\u2020', ["Dagger"] = '\u2021', ["permil"] = '\u2030', ["lsaquo"] = '\u2039',
        ["rsaquo"] = '\u203A', ["euro"] = '\u20AC', ["trade"] = '\u2122', ["hellip"] = '\u2026',
        ["OElig"] = '\u0152', ["oelig"] = '\u0153', ["Scaron"] = '\u0160', ["scaron"] = '\u0161',
        ["Yuml"] = '\u0178', ["circ"] = '\u02C6', ["tilde"] = '\u02DC'
    };

    public class ParsedNode
    {
        public string CitationRef { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Whether an element is a division container. Covers TEI P5's
    /// &lt;div&gt; and TEI P4's numbered &lt;div1&gt;...&lt;div6&gt;, since
    /// Perseus carries both vintages.
    /// </summary>
    private static bool IsDivElement(XElement element)
    {
        var name = element.Name.LocalName;
        if (string.Equals(name, "div", StringComparison.OrdinalIgnoreCase)) return true;

        return name.Length == 4
            && name.StartsWith("div", StringComparison.OrdinalIgnoreCase)
            && char.IsDigit(name[3]);
    }

    public List<ParsedNode> Parse(string xmlFilePath)
    {
        var raw = File.ReadAllText(xmlFilePath);
        var sanitized = SanitizeEntities(raw);
        var doc = XDocument.Parse(sanitized, LoadOptions.None);

        // Matched by local name rather than the TEI P5 namespace. The older
        // Perseus files are TEI P4 - a <TEI.2> root with no namespace at all
        // (the same vintage as the LSJ lexicon files) - so a namespaced
        // lookup finds nothing there and the parser returns empty before
        // reading a single line. Falling back through <text> and finally the
        // root keeps unusual shapes readable rather than silently blank.
        var body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "body")
                   ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "text")
                   ?? doc.Root;
        if (body == null) return new List<ParsedNode>();

        var nodes = new List<ParsedNode>();
        int sortCounter = 0;
        WalkDiv(body, new List<string>(), nodes, ref sortCounter, new Dictionary<string, int>());

        // Fallback: nothing leaf-like was found (milestone-style text) - take
        // each top-level div's flattened text as a single node so nothing is
        // silently dropped.
        if (nodes.Count == 0)
        {
            var topDivs = body.Elements().Where(IsDivElement).ToList();
            foreach (var div in topDivs)
            {
                var n = div.Attribute("n")?.Value ?? (sortCounter + 1).ToString();
                var text = FlattenText(div);
                if (string.IsNullOrWhiteSpace(text)) continue;

                nodes.Add(new ParsedNode
                {
                    CitationRef = n,
                    SortOrder = sortCounter++,
                    Text = text
                });
            }
        }

        // Last resort: a structure this parser doesn't recognize at all.
        // Take the whole body as one node rather than reporting an empty
        // edition - weak citations beat unreadable text.
        if (nodes.Count == 0)
        {
            var wholeText = FlattenText(body);
            if (!string.IsNullOrWhiteSpace(wholeText))
            {
                nodes.Add(new ParsedNode { CitationRef = "1", SortOrder = 0, Text = wholeText });
            }
        }

        return nodes;
    }

    /// <summary>
    /// Resolves undeclared named entities (e.g. &amp;iacute;) to their actual
    /// Unicode character before the XML parser ever sees them. Numeric
    /// entities (&amp;#233;) and the five standard XML entities are left
    /// alone since the parser already understands those natively. Anything
    /// not in the lookup table is dropped rather than guessed at - losing an
    /// obscure character is safer than a wrong one, and it keeps the whole
    /// file from failing to load over one unknown entity.
    /// </summary>
    private static string SanitizeEntities(string xml)
    {
        return EntityPattern.Replace(xml, match =>
        {
            var name = match.Groups[1].Value;

            if (StandardXmlEntities.Contains(name)) return match.Value;
            if (name.StartsWith('#')) return match.Value; // numeric entity, leave as-is

            return NamedEntities.TryGetValue(name, out var ch) ? ch.ToString() : string.Empty;
        });
    }

    /// <summary>
    /// Walks a division tree collecting leaf text.
    ///
    /// <paramref name="leafCounters"/> tracks how many unnumbered leaves have
    /// been seen under each citation trail. TEI P5 files usually number every
    /// line or section, but P4 files typically number only the divisions -
    /// so every paragraph inside &lt;div1 n="1"&gt; would otherwise be handed
    /// the identical ref "1". Duplicated refs break bilingual pairing (only
    /// the first paragraph of each division can ever match) and make citation
    /// jumps ambiguous, so unnumbered leaves get a positional ref instead:
    /// 1.1, 1.2, 1.3 and so on. Leaves that do carry an @n keep it untouched.
    /// </summary>
    private void WalkDiv(
        XElement element,
        List<string> citationTrail,
        List<ParsedNode> nodes,
        ref int sortCounter,
        Dictionary<string, int> leafCounters)
    {
        foreach (var child in element.Elements())
        {
            if (IsDivElement(child))
            {
                var n = child.Attribute("n")?.Value;
                var nextTrail = new List<string>(citationTrail);
                if (!string.IsNullOrEmpty(n)) nextTrail.Add(n);

                WalkDiv(child, nextTrail, nodes, ref sortCounter, leafCounters);
            }
            else if (LeafElements.Contains(child.Name.LocalName))
            {
                var n = child.Attribute("n")?.Value;
                var trail = new List<string>(citationTrail);

                if (!string.IsNullOrEmpty(n))
                {
                    trail.Add(n);
                }
                else
                {
                    var trailKey = string.Join(".", citationTrail);
                    leafCounters.TryGetValue(trailKey, out var seen);
                    leafCounters[trailKey] = seen + 1;
                    trail.Add((seen + 1).ToString());
                }

                var text = FlattenText(child);
                if (string.IsNullOrWhiteSpace(text)) continue;

                nodes.Add(new ParsedNode
                {
                    CitationRef = CapCitationRef(string.Join(".", trail)),
                    SortOrder = sortCounter++,
                    Text = text.Trim()
                });
            }
            else
            {
                // Unrecognized wrapper element (e.g. <sp>, <head>) - descend
                // into it without adding to the citation trail.
                WalkDiv(child, citationTrail, nodes, ref sortCounter, leafCounters);
            }
        }
    }

    private static string FlattenText(XElement element)
    {
        var sb = new StringBuilder();
        foreach (var node in element.DescendantNodes())
        {
            if (node is XText text) sb.Append(text.Value).Append(' ');
        }
        return CollapseWhitespace(sb.ToString());
    }

    private static string CollapseWhitespace(string input)
    {
        return string.Join(' ', input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Most works cite by plain numbers ("1.1"), but a handful of Perseus
    /// texts (some Aeschines/Demosthenes orations) use whole descriptive
    /// phrases as a div's @n attribute instead. Cap defensively so this can
    /// never overflow the CitationRef column no matter how it's sized.
    /// </summary>
    private static string CapCitationRef(string citationRef)
    {
        const int maxLength = 900;
        return citationRef.Length <= maxLength ? citationRef : citationRef.Substring(0, maxLength);
    }

    public IReadOnlyList<TextNode> ToTextNodes(int editionId, List<ParsedNode> parsed)
    {
        return parsed.Select(p => new TextNode
        {
            EditionId = editionId,
            CitationRef = p.CitationRef,
            SortOrder = p.SortOrder,
            Text = p.Text
        }).ToList();
    }
}
