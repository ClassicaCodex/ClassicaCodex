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
    /// Elements WalkDiv can reach a TextNode through. An element that is
    /// none of these and contains none of them has no way to produce a node
    /// by being descended into, so it is emitted whole instead - see the
    /// family branch in WalkDiv.
    ///
    /// &lt;item&gt; is here without having a branch of its own: it is emitted
    /// by the family rule, but a &lt;list&gt; wrapping several of them must
    /// still be descended into rather than flattened into one node.
    /// </summary>
    private static readonly HashSet<string> HandledElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "l", "p", "said", "lg", "head", "castItem", "stage", "item", "note"
    };

    /// <summary>
    /// What a block element's text is, for
    /// <see cref="TextNode.NodeKind"/>. Anything not listed is treated as
    /// running text and counted.
    ///
    /// The default direction matters. An unlisted element is far more likely
    /// to be prose this parser hasn't met than to be an annotation, and
    /// getting that wrong in the "don't count it" direction is the worse
    /// error: an edition whose entire body is one unrecognised block would
    /// report zero countable words and hand the stylometry an empty text.
    /// Erring towards Line means an unknown element behaves exactly as it
    /// did when it was being flattened into a line, which is the status quo.
    /// </summary>
    private static readonly Dictionary<string, string> BlockKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["speaker"] = TextNodeKinds.Speaker,
        ["stage"] = TextNodeKinds.Stage,
        ["head"] = TextNodeKinds.Head,
        ["label"] = TextNodeKinds.Head,
        ["title"] = TextNodeKinds.Head,
        ["castItem"] = TextNodeKinds.Cast,
        ["trailer"] = TextNodeKinds.Paratext,
        ["closer"] = TextNodeKinds.Paratext,
        ["opener"] = TextNodeKinds.Paratext,
        ["salute"] = TextNodeKinds.Paratext,
        ["signed"] = TextNodeKinds.Paratext,
        ["dateline"] = TextNodeKinds.Paratext,
        ["byline"] = TextNodeKinds.Paratext,
        ["docAuthor"] = TextNodeKinds.Attribution,
        ["docDate"] = TextNodeKinds.Attribution
    };

    private static string KindFor(string localName) =>
        BlockKinds.TryGetValue(localName, out var kind) ? kind : TextNodeKinds.Line;

    /// <summary>
    /// Whether an element contains anything WalkDiv would recognise.
    ///
    /// This is the test that decides between descending and emitting. A
    /// &lt;castGroup&gt; holds &lt;castItem&gt;s and must be descended into
    /// or its entries collapse into one blob; a &lt;trailer&gt; holds only
    /// its own words and must be emitted or it is lost. Both are elements
    /// this parser has no branch for, and only their contents tell them
    /// apart.
    /// </summary>
    private static bool HasHandledDescendant(XElement element) =>
        element.Descendants().Any(d =>
            HandledElements.Contains(d.Name.LocalName) || IsDivElement(d));

    /// <summary>
    /// Whether a &lt;label&gt; is the speaker tag of the speech it sits in.
    ///
    /// Perseus encodes dramatic dialogue one way and Platonic dialogue
    /// another. Tragedy and comedy use &lt;sp&gt;&lt;speaker&gt;; the
    /// dialogues put the attribution in a &lt;label&gt; inside the
    /// &lt;said&gt; - "ΣΩ.", "Soc.", "ΚΑΛ." - which then flattens into the
    /// line and is tokenised as a word. Measured over canonical-greekLit:
    /// 4.1% of Gorgias and 1.9% of the Laws by word count.
    ///
    /// Both halves of the test are load-bearing, and each was arrived at by
    /// running it over all 1,612 Greek editions:
    ///
    ///   @who alone misfires on 4 entries - the Symposium wraps section
    ///   summaries ("The Speech of Pausanias") in a &lt;label&gt; inside a
    ///   &lt;said who="..."&gt;, and those are headings, not attributions.
    ///
    ///   The shape test alone misfires on 2,113 - Josephus numbers his
    ///   paragraphs α. β. γ. in a &lt;label&gt; inside a &lt;p&gt;, which
    ///   looks exactly like an abbreviated speaker and is not one.
    ///
    /// Together they classify 25,820 labels and nothing else in the corpus.
    ///
    /// A length rule alone was tried and rejected: "Hermogenes." is eleven
    /// characters and a speaker, "ΣΩ." is three, and "The Speech of
    /// Socrates" is not one at any length.
    /// </summary>
    private static bool IsSpeakerLabel(XElement label)
    {
        var said = label.Ancestors().FirstOrDefault(a =>
            string.Equals(a.Name.LocalName, "said", StringComparison.OrdinalIgnoreCase));

        if (said?.Attributes().Any(a =>
                string.Equals(a.Name.LocalName, "who", StringComparison.OrdinalIgnoreCase)) != true)
        {
            return false;
        }

        var text = FlattenText(label);
        if (text.Length == 0 || text.Length > 12) return false;

        // A name, so at least one letter. Nothing in the corpora needs this
        // - there are no letterless labels inside a speech - but without it
        // the All() below is vacuously true for "1." and a numbered label
        // would read as a speaker the first time one appeared.
        if (!text.Any(char.IsLetter)) return false;

        return text.EndsWith('.') || text.Where(char.IsLetter).All(char.IsUpper);
    }

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
        "gap",        // lacuna marker; carries an editorial description, never text
        "certainty",
        "respStmt",
        "teiHeader",  // body extraction should exclude it already; belt and braces
        "milestone",
        "fw"          // forme work: running headers, catchwords, signatures
    };

    // <del> WAS IN THIS LIST AND SHOULD NOT HAVE BEEN.
    //
    // In a manuscript transcription <del> marks something the scribe struck
    // out, which is reasonably editorial. In a printed critical edition of a
    // classical text it marks an ATHETIZED line - text that is transmitted in
    // the manuscripts, that editors suspect is interpolated, and that is
    // printed in square brackets rather than removed. It is part of the text a
    // reader expects to see.
    //
    // Agamemnon 7 is encoded:
    //     <l n="7"><del>ἀστέρας, ὅταν φθίνωσιν, ἀντολάς τε τῶν</del>.</l>
    //
    // With <del> skipped the line came through as a bare full stop: the entire
    // Greek sits inside the element and only the trailing punctuation is
    // outside it. Fourteen lines of that one play were affected, 157 characters
    // of Greek, and the same encoding appears in the translations.
    //
    // The distinction that matters for this corpus: apparatus is commentary
    // ABOUT the text and belongs out; an athetized line IS the text, disputed.
    //
    // Not yet done: athetized lines arrive indistinguishable from accepted
    // ones. Printed editions bracket them, and the reader should see that
    // difference. Doing it properly means carrying a flag through TextNodes
    // rather than injecting brackets into the text, which would then be
    // tokenised and searched.

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

        /// <summary>
        /// Set when the source line contains a &lt;del&gt;, i.e. text the
        /// editor bracketed as suspected interpolation. See TextNode for why
        /// this is a flag rather than brackets in Text.
        /// </summary>
        public bool IsAthetized { get; set; }

        /// <summary>See <see cref="TextNode.NodeKind"/>.</summary>
        public string NodeKind { get; set; } = TextNodeKinds.Line;
    }

    /// <summary>
    /// One entry from the critical apparatus, attached to the line it
    /// discusses. See the migration 12 comment for why this is stored apart
    /// from the reading text.
    /// </summary>
    public class ParsedApparatus
    {
        public string CitationRef { get; set; } = string.Empty;
        public int SortOrder { get; set; }

        /// <summary>"variant" for a rejected manuscript reading, "note" for editorial comment.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>The adopted reading this entry is about, where the source names one.</summary>
        public string? Lemma { get; set; }

        /// <summary>Manuscript siglum or responsible editor, from @wit or @resp.</summary>
        public string? Witness { get; set; }

        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pulls the apparatus out of one leaf element.
    ///
    /// TEI encodes this two ways and both appear in the corpus:
    ///
    ///   &lt;app&gt;&lt;lem&gt;adopted&lt;/lem&gt;&lt;rdg wit="M"&gt;variant&lt;/rdg&gt;&lt;/app&gt;
    ///       structured - the adopted reading and its rejected alternatives
    ///
    ///   &lt;note resp="editor"&gt;εἶτʼ οὐ R: εἶτα&lt;/note&gt;
    ///       unstructured - the whole apparatus entry as prose, which is what
    ///       Perseus mostly uses. No attempt is made to parse it into lemma
    ///       and variant; that would mean guessing at an editor's punctuation
    ///       conventions, and guessing wrong silently is worse than showing
    ///       the entry as written.
    ///
    /// Content is flattened with the same editorial filter used for reading
    /// text, so a &lt;foreign&gt; wrapper around a Greek variant contributes its
    /// text while a nested &lt;bibl&gt; does not.
    /// </summary>
    /// <summary>
    /// Whether an apparatus entry says anything.
    ///
    /// Some editions put the footnote MARKER in a &lt;note&gt; rather than the
    /// note: the German Thucydides yields entries whose entire content is "1",
    /// "2", "3", and the English one yields a bare full stop. Stored, they
    /// become rows in the reader's notes list that say nothing and cannot be
    /// clicked through to anything.
    ///
    /// The test is a single letter anywhere in the content. Digits and
    /// punctuation alone are a marker; a siglum like "M" or an abbreviation
    /// like "cf." is real and passes. Deliberately permissive - the cost of
    /// keeping a marginal entry is one line in a list, while dropping a real
    /// note loses scholarship silently.
    /// </summary>
    private static bool CarriesInformation(string content) =>
        !string.IsNullOrWhiteSpace(content) && content.Any(char.IsLetter);

    private static List<ParsedApparatus> ExtractApparatus(XElement leaf, string citationRef)
    {
        var entries = new List<ParsedApparatus>();
        var order = 0;

        foreach (var el in leaf.Descendants())
        {
            var name = el.Name.LocalName;

            if (string.Equals(name, "app", StringComparison.OrdinalIgnoreCase))
            {
                var lem = el.Elements().FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, "lem", StringComparison.OrdinalIgnoreCase));
                var lemText = lem == null ? null : FlattenText(lem);

                foreach (var rdg in el.Elements().Where(e =>
                             string.Equals(e.Name.LocalName, "rdg", StringComparison.OrdinalIgnoreCase)))
                {
                    var content = FlattenText(rdg);
                    if (!CarriesInformation(content)) continue;

                    entries.Add(new ParsedApparatus
                    {
                        CitationRef = citationRef,
                        SortOrder = order++,
                        Kind = "variant",
                        Lemma = string.IsNullOrWhiteSpace(lemText) ? null : lemText,
                        Witness = (string?)rdg.Attribute("wit") ?? (string?)rdg.Attribute("resp"),
                        Content = content
                    });
                }

                continue;
            }

            if (!string.Equals(name, "note", StringComparison.OrdinalIgnoreCase)) continue;

            // A note nested inside an <app> is part of that entry and was
            // already covered above; taking it again would duplicate it.
            if (el.Ancestors().Any(a => string.Equals(a.Name.LocalName, "app", StringComparison.OrdinalIgnoreCase)))
                continue;

            var noteText = FlattenText(el);
            if (!CarriesInformation(noteText)) continue;

            entries.Add(new ParsedApparatus
            {
                CitationRef = citationRef,
                SortOrder = order++,
                Kind = "note",
                Witness = (string?)el.Attribute("resp"),
                Content = noteText
            });
        }

        return entries;
    }

    /// <summary>
    /// Whether an element encloses any athetized text.
    ///
    /// Checked on the whole subtree rather than direct children: a line can be
    /// &lt;l&gt;&lt;quote&gt;&lt;del&gt;...&lt;/del&gt;&lt;/quote&gt;&lt;/l&gt;,
    /// and Athenaeus nests it inside &lt;add&gt;.
    /// </summary>
    private static bool ContainsAthetizedText(XElement element) =>
        element.Descendants().Any(e =>
            string.Equals(e.Name.LocalName, "del", StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Apparatus collected by the most recent Parse/ParseXml call.
    ///
    /// Exposed as state rather than returned alongside the nodes because
    /// WalkDiv is recursive and already threads four parameters; a fifth
    /// out-list through every call site buys nothing. Reset at the start of
    /// each parse, so it always describes the file just read.
    /// </summary>
    public IReadOnlyList<ParsedApparatus> LastApparatus => _apparatus;

    private readonly List<ParsedApparatus> _apparatus = new();

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
        _apparatus.Clear();
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
            // A note sitting between the divisions rather than inside a line.
            //
            // ExtractApparatus only ever sees leaves, so a note at this level
            // reached neither it nor the text and was dropped outright.
            // Hecuba's dramatis personae is one of these: Coleridge's cast
            // list, wrapped in <note resp="Coleridge">, listing the Ghost of
            // Polydorus, Hecuba, the Chorus of Captive Trojan Women and the
            // rest - the whole cast, invisible.
            //
            // It goes to the apparatus rather than the text because that is
            // what it is: an editor's addition, named as his, not Euripides.
            // The Editor's Notes pane is exactly the place for it, and routing
            // it there leaves EditorialElements untouched - the skip that
            // keeps 17,000 characters of Agamemnon's apparatus out of the word
            // counts still holds for every note inside a line.
            if (string.Equals(child.Name.LocalName, "note", StringComparison.OrdinalIgnoreCase))
            {
                var noteText = FlattenText(child);
                if (!CarriesInformation(noteText)) continue;

                var noteKey = string.Join(".", citationTrail) + ":note";
                leafCounters.TryGetValue(noteKey, out var notesSeen);
                leafCounters[noteKey] = notesSeen + 1;

                var noteTrail = new List<string>(citationTrail) { $"note{notesSeen + 1}" };

                _apparatus.Add(new ParsedApparatus
                {
                    CitationRef = CapCitationRef(string.Join(".", noteTrail)),
                    SortOrder = notesSeen,
                    Kind = "note",
                    Witness = (string?)child.Attribute("resp"),
                    Content = noteText
                });

                continue;
            }

            if (EditorialElements.Contains(child.Name.LocalName)) continue;

            if (IsDivElement(child))
            {
                var n = child.Attribute("n")?.Value;
                var nextTrail = new List<string>(citationTrail);
                if (!string.IsNullOrEmpty(n)) nextTrail.Add(n);

                WalkDiv(child, nextTrail, nodes, ref sortCounter, leafCounters);
            }
            else if (string.Equals(child.Name.LocalName, "head", StringComparison.OrdinalIgnoreCase))
            {
                // A <head> is not a citable leaf, but its text is real content
                // and was previously lost outright: head is neither a div nor a
                // leaf, so the fallback branch below descended into it, found no
                // child elements, and emitted nothing.
                //
                // Often the head merely repeats the work's title. Sometimes it
                // does not. In Adrianus of Tyre's Declamatio the head carries
                // the entire declamation theme - the premise the speech argues
                // against - and dropping it removes the only statement of what
                // the text is about.
                //
                // The reference gets a "head" segment rather than a number from
                // the leaf counter. Consuming a counter slot would renumber
                // every sibling leaf after it, and annotations resolve through
                // (EditionId, CitationRef) - existing bookmarks and tags on
                // those works would silently point at the wrong line. A named
                // segment is unambiguous and leaves the numbering alone.
                //
                // Second and subsequent heads under one trail are numbered; the
                // first is plain "head", which is the overwhelmingly common case
                // and reads better in a citation.
                var headsSeen = NextSegmentNumber(citationTrail, "head", leafCounters);
                EmitBlock(child, citationTrail, headsSeen == 1 ? "head" : $"head{headsSeen}",
                          TextNodeKinds.Head, nodes, ref sortCounter);
            }
            else if (string.Equals(child.Name.LocalName, "castItem", StringComparison.OrdinalIgnoreCase))
            {
                // The dramatis personae. Like <head>, a cast entry is neither a
                // div nor a leaf, so the fallback branch below descended into
                // it, found only <role> and <roleDesc> - which are not leaves
                // either - and emitted nothing at all.
                //
                // King Lear lost all 24 of its cast entries that way. What
                // survived was the two <castGroup> headings, "Servants to
                // Cornwall" and "Daughters to Lear", because those are <head>
                // elements - so the reader showed a dramatis personae listing
                // two group labels and not one character.
                var castSeen = NextSegmentNumber(citationTrail, "cast", leafCounters);
                EmitBlock(child, citationTrail, $"cast{castSeen}",
                          TextNodeKinds.Cast, nodes, ref sortCounter);
            }
            else if (string.Equals(child.Name.LocalName, "stage", StringComparison.OrdinalIgnoreCase))
            {
                // Stage directions, which sit beside the speeches rather than
                // inside them and so met the same fate as the cast list: not a
                // div, not a leaf, descended into, nothing emitted.
                //
                // Hecuba has 48 of them, none inside an <sp>, and they carry
                // the whole staging - "Before Agamemnon's tent in the Greek
                // camp upon the shore of the Thracian Chersonese", "The Ghost
                // vanishes", "The Chorus of captive Trojan women enters". A
                // play read without them is a play with the exits and
                // entrances removed. King Lear has 291.
                var stageSeen = NextSegmentNumber(citationTrail, "stage", leafCounters);
                EmitBlock(child, citationTrail, $"stage{stageSeen}",
                          TextNodeKinds.Stage, nodes, ref sortCounter);
            }
            else if (LeafElements.Contains(child.Name.LocalName))
            {
                // Any speaker tag inside this speech comes out first, so it
                // precedes the words spoken in reading order. See
                // IsSpeakerLabel: Perseus puts Plato's attributions inside the
                // <said> rather than beside it, and FlattenElement drops them
                // from the line so they are not counted twice.
                foreach (var label in child.Descendants().Where(d =>
                             string.Equals(d.Name.LocalName, "label", StringComparison.OrdinalIgnoreCase)
                             && IsSpeakerLabel(d)))
                {
                    var speakerSeen = NextSegmentNumber(citationTrail, "speaker", leafCounters);
                    EmitBlock(label, citationTrail, $"speaker{speakerSeen}",
                              TextNodeKinds.Speaker, nodes, ref sortCounter);
                }

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

                var leafRef = CapCitationRef(string.Join(".", trail));
                _apparatus.AddRange(ExtractApparatus(child, leafRef));

                nodes.Add(new ParsedNode
                {
                    IsAthetized = ContainsAthetizedText(child),
                    CitationRef = leafRef,
                    SortOrder = sortCounter++,
                    Text = text.Trim(),
                    NodeKind = TextNodeKinds.Line
                });
            }
            else if (!HasHandledDescendant(child))
            {
                // An element with no branch of its own and nothing inside it
                // that has one. Descending, which is what used to happen, can
                // only reach a dead end - so its words were lost outright.
                //
                // This is the same failure that took King Lear's cast list and
                // Hecuba's stage directions, and it had more members than
                // those two. Measured across the Perseus Greek, Latin and
                // English corpora: <speaker> in every play (42,448 in Greek
                // alone), <item> in Holinshed's lists, <trailer>, <closer>,
                // <opener>, <salute>, <signed>, <dateline>, <label>, <ab>,
                // <docAuthor> - the Greek Anthology's poet attributions, so
                // the epigrams read without knowing who wrote them.
                //
                // Handled as a family rather than one branch per element,
                // because the list of elements is open and the test is not:
                // if nothing inside can be reached, the element itself is the
                // content. An element that DOES contain something handled -
                // <castGroup> around its <castItem>s, <sp> around its lines -
                // still falls through to the descent below.
                //
                // The reference segment is the element's own name, so a
                // citation says what it points at: "1.2.speaker1", "3.item4".
                var name = child.Name.LocalName;
                var seen = NextSegmentNumber(citationTrail, name.ToLowerInvariant(), leafCounters);
                EmitBlock(child, citationTrail, $"{name.ToLowerInvariant()}{seen}",
                          KindFor(name), nodes, ref sortCounter);
            }
            else
            {
                // Unrecognized wrapper element (e.g. <sp>, <castGroup>) -
                // descend into it without adding to the citation trail.
                WalkDiv(child, citationTrail, nodes, ref sortCounter, leafCounters);
            }
        }
    }

    /// <summary>
    /// Reserves the next number for a named reference segment under this
    /// trail - "head", "cast3", "speaker12".
    ///
    /// Named segments rather than numbers from the leaf counter, for the
    /// reason &lt;head&gt; has always used one: consuming a counter slot
    /// renumbers every sibling leaf after it, and annotations resolve through
    /// (EditionId, CitationRef), so existing bookmarks and tags would
    /// silently move to a different line. The counters dictionary is shared,
    /// but each segment name keys its own entry, so they cannot collide with
    /// the line numbering or with each other.
    /// </summary>
    private static int NextSegmentNumber(
        List<string> citationTrail, string segmentName, Dictionary<string, int> leafCounters)
    {
        var key = string.Join(".", citationTrail) + ":" + segmentName;
        leafCounters.TryGetValue(key, out var seen);
        leafCounters[key] = seen + 1;
        return seen + 1;
    }

    /// <summary>
    /// Emits one non-leaf block as a TextNode under a named reference
    /// segment, and collects any apparatus it carries.
    ///
    /// The apparatus collection is the part that is easy to leave out.
    /// ExtractApparatus used to run only on leaves, so a &lt;note&gt; inside
    /// a heading reached neither the text (FlattenText skips notes, as it
    /// must) nor the Editor's Notes pane. 24 notes and 14,422 characters
    /// across the Greek corpus, 11,025 of them in three notes on headings in
    /// the German Thucydides.
    /// </summary>
    private void EmitBlock(
        XElement element,
        List<string> citationTrail,
        string segment,
        string kind,
        List<ParsedNode> nodes,
        ref int sortCounter)
    {
        var text = FlattenText(element);
        if (string.IsNullOrWhiteSpace(text)) return;

        var reference = CapCitationRef(string.Join(".", new List<string>(citationTrail) { segment }));
        _apparatus.AddRange(ExtractApparatus(element, reference));

        nodes.Add(new ParsedNode
        {
            CitationRef = reference,
            SortOrder = sortCounter++,
            Text = text.Trim(),
            NodeKind = kind
        });
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

            // A speaker tag is emitted as its own node by WalkDiv, so it must
            // not also appear inside the speech - otherwise Plato reads
            // "ΣΩ. ΣΩ. ἐξ ἀγορᾶς..." and the tag is still counted as a word.
            if (string.Equals(name, "label", StringComparison.OrdinalIgnoreCase)
                && IsSpeakerLabel(child))
            {
                continue;
            }

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

    public IReadOnlyList<ApparatusEntry> ToApparatusEntries(int editionId)
    {
        return _apparatus.Select(a => new ApparatusEntry
        {
            EditionId = editionId,
            CitationRef = a.CitationRef,
            SortOrder = a.SortOrder,
            Kind = a.Kind,
            Lemma = a.Lemma,
            Witness = a.Witness,
            Content = a.Content
        }).ToList();
    }

    public IReadOnlyList<TextNode> ToTextNodes(int editionId, List<ParsedNode> parsed)
    {
        return parsed.Select(p => new TextNode
        {
            EditionId = editionId,
            CitationRef = p.CitationRef,
            SortOrder = p.SortOrder,
            Text = p.Text,
            IsAthetized = p.IsAthetized,
            NodeKind = p.NodeKind
        }).ToList();
    }
}
