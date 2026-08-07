using System.Text;
using System.Xml.Linq;
using ClassicaCodex.Core;
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

    /// <summary>
    /// Elements whose content is editorial commentary about the text rather
    /// than the text itself, and which must never reach a TextNode.
    ///
    /// This is not a tidiness measure. First1KGreek encodes the critical
    /// apparatus inline, so without this filter an Aeschylus line arrives
    /// carrying manuscript sigla and nineteenth-century editors' surnames:
    /// "seclusit Pauw", "fort. δεσποτουμένου Dübner", "ἀντίνους Wecklein",
    /// "F1 V Fa: δ' ἦν M". Those then get tokenised and counted as words. A
    /// First1KGreek Agamemnon came out roughly a third longer in characters
    /// than the Perseus text of the same play on the same number of lines,
    /// entirely from apparatus, which is enough to move any frequency-based
    /// measure computed over the corpus.
    ///
    /// Both parse paths need it. WalkDiv's fallback branch descends into any
    /// element it does not recognise, so an unfiltered &lt;app&gt; has its
    /// inner &lt;l&gt; promoted to a citable leaf of its own; and FlattenText
    /// collects every descendant text node, so an &lt;app&gt; sitting inline
    /// within a real line contributes its variants to that line.
    ///
    /// Deliberately excluded from this list:
    ///   lem   - the reading the editor adopted, i.e. the text (see below)
    ///   add   - a scribal addition present in the witness
    ///   speaker, head, label - editorially supplied in places, but part of
    ///           the received text as printed, and removing them would change
    ///           what the reader sees rather than only what is counted
    /// </summary>
    private static readonly HashSet<string> EditorialElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",        // apparatus entry (handled specially - see FlattenElement)
        "rdg",        // variant reading
        "rdgGrp",     // group of variant readings
        "note",       // editorial note
        "witness",    // witness description
        "witDetail",
        "listWit",
        "bibl",       // bibliographic reference
        "biblStruct",
        "listBibl",
        "figDesc",    // figure description
        "desc",
        "gap",        // lacuna marker with editorial description
        "del",        // text marked deleted
        "certainty",
        "respStmt",
        "teiHeader",  // body extraction should exclude it already; belt and braces
        "milestone",
        "fw"          // forme work: running headers, catchwords, signatures
    };

    /// <summary>
    /// Inside &lt;choice&gt;, the readings to prefer, in order. TEI pairs an
    /// original with a normalised alternative and expects the consumer to
    /// choose one; taking both concatenates a word with its own correction.
    /// </summary>
    private static readonly string[] PreferredChoiceReadings = { "reg", "expan", "corr" };

    /// <summary>
    /// The counterparts to <see cref="PreferredChoiceReadings"/> - kept only
    /// when no preferred sibling exists.
    /// </summary>
    private static readonly string[] FallbackChoiceReadings = { "orig", "abbr", "sic" };

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

    public List<ParsedNode> Parse(string xmlFilePath) => ParseXml(File.ReadAllText(xmlFilePath));

    /// <summary>
    /// Same as <see cref="Parse"/> but takes the raw XML directly instead of a
    /// file path. Lets a caller pre-process first - the Renaissance importer
    /// strips the P4 DOCTYPE (whose external parameter entity can't be resolved
    /// offline) before handing the text over, keeping that concern out of the
    /// Greek/Latin file path entirely.
    /// </summary>
    public List<ParsedNode> ParseXml(string rawXml)
    {
        var sanitized = SanitizeEntities(rawXml);
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
    // Entity resolution lives in one place - ClassicaCodex.Core.XmlEntitySanitizer -
    // so the text ingest and the lexicon ingest can't drift apart on which
    // named entities they know. They had drifted: this copy was missing the
    // macron/breve vowels the Core table carries, so &omacr;/&amacr; and the
    // like were dropped from text bodies while the lexica resolved them fine.
    private static string SanitizeEntities(string xml) => XmlEntitySanitizer.Sanitize(xml);

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
            // Before anything else: an editorial element is not part of the
            // text and must not become a node. The fallback branch at the
            // bottom of this loop descends into whatever it does not
            // recognise, so an unfiltered <app> or <note> containing an <l>
            // would have that line promoted to a citable leaf - which is how
            // "seclusit Pauw" ended up occupying its own line of Agamemnon.
            //
            // <app> is skipped wholesale here rather than mined for its <lem>,
            // unlike in FlattenElement. At this level an <app> is a sibling of
            // the lines rather than inline within one, which in practice means
            // a block of apparatus rather than a reading belonging to a
            // specific line. Taking its <lem> would insert text at a position
            // in the work where it does not belong.
            if (EditorialElements.Contains(child.Name.LocalName)) continue;

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
        FlattenElement(element, sb);
        return CollapseWhitespace(sb.ToString());
    }

    /// <summary>
    /// Walks an element's content, appending text but stepping around
    /// editorial apparatus.
    ///
    /// Recursive rather than the flat DescendantNodes() pass this replaces,
    /// because skipping a subtree requires knowing which subtree a text node
    /// sits in - and DescendantNodes() has already thrown that away.
    ///
    /// &lt;app&gt; gets special handling instead of a plain skip. An apparatus
    /// entry usually contains a &lt;lem&gt;, the reading the editor actually
    /// adopted, which IS the text at that point; the &lt;rdg&gt; siblings are
    /// the rejected alternatives. Dropping the whole element would silently
    /// delete words from the line. Where there is no &lt;lem&gt; - the entry
    /// records only variants - nothing is taken.
    /// </summary>
    private static void FlattenElement(XElement element, StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                sb.Append(text.Value).Append(' ');
                continue;
            }

            if (node is not XElement child) continue;

            var name = child.Name.LocalName;

            if (string.Equals(name, "app", StringComparison.OrdinalIgnoreCase))
            {
                // Take only the adopted reading, if the entry names one.
                foreach (var lem in child.Elements().Where(e =>
                             string.Equals(e.Name.LocalName, "lem", StringComparison.OrdinalIgnoreCase)))
                {
                    FlattenElement(lem, sb);
                }
                continue;
            }

            if (string.Equals(name, "choice", StringComparison.OrdinalIgnoreCase))
            {
                var chosen =
                    PreferredChoiceReadings
                        .Select(pref => child.Elements().FirstOrDefault(e =>
                            string.Equals(e.Name.LocalName, pref, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault(e => e != null)
                    ?? FallbackChoiceReadings
                        .Select(fb => child.Elements().FirstOrDefault(e =>
                            string.Equals(e.Name.LocalName, fb, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault(e => e != null);

                if (chosen != null) FlattenElement(chosen, sb);
                continue;
            }

            if (EditorialElements.Contains(name)) continue;

            FlattenElement(child, sb);
        }
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
