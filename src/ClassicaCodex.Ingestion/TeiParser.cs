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
    ///
    /// &lt;note&gt; WAS IN THIS LIST AND SHOULD NOT HAVE BEEN. WalkDiv routes a
    /// note to the apparatus and never makes a TextNode from one, so a note is
    /// precisely an element this set's own definition excludes - and counting
    /// it re-opened the hole the family branch was written to close. An element
    /// carrying a footnote was sent down the descent branch, WalkDiv reads only
    /// child elements, and the element's own words went nowhere:
    ///
    ///   &lt;speaker&gt;Ἀφροδίτη&lt;note&gt;ΑΦΡΟΔΙΤΗ vulg.: HPΑ MSS.&lt;/note&gt;&lt;/speaker&gt;
    ///
    /// - the speaker vanishes and no Speaker node is emitted at all. 135
    /// elements across the four corpora, most of them Lucian's speakers (17 in
    /// canonical-greekLit, 12 in First1KGreek, 2 in canonical-latinLit) and
    /// the rest &lt;quote&gt;, &lt;cit&gt;, &lt;hi&gt; and &lt;docAuthor&gt;
    /// carrying an editor's note.
    ///
    /// Nothing is lost by the change: EmitBlock collects the element's
    /// apparatus on the way past, so the note still reaches the Editor's Notes
    /// pane - now keyed to a citation that has a line behind it rather than to
    /// a "noteN" reference of its own.
    /// </summary>
    private static readonly HashSet<string> HandledElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "l", "p", "said", "lg", "head", "castItem", "stage", "item"
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
    /// The elements that mean "this is a line of verse". See
    /// <see cref="TextNode.IsVerse"/> for why this is asked separately from
    /// <see cref="KindFor"/> rather than being one more kind.
    ///
    /// &lt;lg&gt; is here for the case the leaf branch actually sees: a verse
    /// group whose text sits directly in it rather than in &lt;l&gt;
    /// children. A group that does hold lines is descended into well before
    /// this is asked, and each line answers for itself.
    ///
    /// Deliberately not inferred from anything but the markup. A prose
    /// paragraph that happens to scan is not verse, an unmarked verse text is
    /// not something this can recover, and guessing from the text would make
    /// the flag mean "looked like verse to us" - which is a measurement, and
    /// belongs to whatever does the measuring, not to the parser.
    /// </summary>
    private static bool IsVerseElement(string localName) =>
        string.Equals(localName, "l", StringComparison.OrdinalIgnoreCase)
        || string.Equals(localName, "lg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an element contains anything WalkDiv would recognise.
    ///
    /// This is the test that decides between descending and emitting. A
    /// &lt;castGroup&gt; holds &lt;castItem&gt;s and must be descended into
    /// or its entries collapse into one blob; a &lt;trailer&gt; holds only
    /// its own words and must be emitted or it is lost. Both are elements
    /// this parser has no branch for, and only their contents tell them
    /// apart.
    ///
    /// A descendant sitting inside an editorial subtree does not count. WalkDiv
    /// skips those subtrees wholesale, so an &lt;l&gt; inside a &lt;note&gt; is
    /// not a line this element can be descended into to reach - it is part of
    /// the editor's prose. Counting it sent the element down the descent branch
    /// to find nothing, which is the same loss the &lt;note&gt; entry in
    /// HandledElements used to cause, arriving one level lower down.
    /// </summary>
    private static bool HasHandledDescendant(XElement element) =>
        element.Descendants().Any(d =>
            (HandledElements.Contains(d.Name.LocalName) || IsDivElement(d))
            && !d.Ancestors()
                 .TakeWhile(a => a != element)
                 .Any(a => EditorialElements.Contains(a.Name.LocalName)));

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
    /// Elements that name a thing rather than say something. A
    /// &lt;reg&gt; inside one of these is the authority-file form of the name,
    /// not a word of the text.
    /// </summary>
    private static readonly HashSet<string> NamingElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "placeName", "persName", "rs", "orgName", "geogName"
    };

    /// <summary>
    /// Whether a &lt;reg&gt; is a gazetteer entry rather than reading text.
    ///
    /// &lt;reg&gt; normally holds the regularised form of a word and IS the
    /// text - 35,059 of them across the corpora do exactly that, almost all in
    /// canonical-latinLit, where Perseus lower-cases a sentence's first word
    /// and marks it &lt;reg&gt;etsi&lt;/reg&gt;. Skipping those would delete a
    /// word from the start of thousands of sentences.
    ///
    /// The English Herodotus uses the same element for something else
    /// entirely. Every place name carries its Getty Thesaurus record inline,
    /// with the text of Herodotus as the sibling or the tail:
    ///
    ///   &lt;name key="tgn,7016142" type="place"&gt;
    ///     &lt;reg&gt;Bodrum [27.466,37.5] (inhabited place), Mugla Ili, Ege
    ///           kiyilari, Turkey, Asia &lt;/reg&gt;
    ///     &lt;placeName key="tgn,7016142"&gt;Halicarnassus&lt;/placeName&gt;
    ///   &lt;/name&gt;
    ///
    ///   &lt;name key="tgn,7008330" type="place"&gt;
    ///     &lt;reg&gt;Etruria (region (general)), Italy, Europe&lt;/reg&gt;Tyrrhenia
    ///   &lt;/name&gt;
    ///
    /// Taken as text, Herodotus 1.1 opened "Bodrum [27.466,37.5] (inhabited
    /// place), Mugla Ili, Ege kiyilari, Turkey, Asia Halicarnassus" - modern
    /// Turkish place names and decimal coordinates tokenised and counted as
    /// Greek historiography in English translation.
    ///
    /// The parent element is the whole test, and it separates the two uses
    /// cleanly over all four corpora: all 4,305 gazetteer entries sit inside a
    /// &lt;name&gt;, and not one of the 35,059 regularisations does - their
    /// parents are &lt;p&gt;, &lt;q&gt;, &lt;said&gt;, &lt;quote&gt;,
    /// &lt;seg&gt;, &lt;l&gt;, &lt;hi&gt;, &lt;note&gt;, &lt;choice&gt;.
    /// Matching on @key="tgn,..." instead would have worked here and broken on
    /// the next authority file Perseus cites.
    ///
    /// The content test is belt and braces. Nothing in the corpora is a
    /// &lt;name&gt; holding only its &lt;reg&gt; - all 4,305 have the reading
    /// text beside it - but if one existed, skipping would leave the name
    /// blank, and a gazetteer string in the text is a smaller loss than a
    /// place with no name at all.
    /// </summary>
    private static bool IsNameAuthorityForm(XElement reg)
    {
        var parent = reg.Parent;
        if (parent == null || !NamingElements.Contains(parent.Name.LocalName)) return false;

        return parent.Nodes().Any(n => n != reg && CarriesText(n));
    }

    private static bool CarriesText(XNode node) => node switch
    {
        XText text => !string.IsNullOrWhiteSpace(text.Value),
        XElement element => !string.IsNullOrWhiteSpace(element.Value),
        _ => false
    };

    /// <summary>
    /// What an &lt;expan&gt; contributes to the reading text: its
    /// &lt;abbr&gt; if it holds one, and otherwise nothing.
    ///
    /// An &lt;expan&gt; is the editor's expansion of an abbreviation the
    /// manuscripts print. Both were being taken, so Nepos read "affinitatem
    /// Publii P. Sulpicii" - the praenomen twice, once expanded and once
    /// abbreviated. Four encodings, all in canonical-latinLit, and the
    /// abbreviation sits in a different place in each:
    ///
    ///   &lt;expan&gt;&lt;abbr&gt;acturu's&lt;/abbr&gt;&lt;ex&gt;acturus es&lt;/ex&gt;&lt;/expan&gt;   304
    ///   &lt;abbr&gt;M.&lt;expan&gt;&lt;ex&gt;Marci&lt;/ex&gt;&lt;/expan&gt;&lt;/abbr&gt;              98
    ///   &lt;abbr&gt;&lt;expan&gt;&lt;ex&gt;Titus&lt;/ex&gt;&lt;/expan&gt;T.&lt;/abbr&gt;              79
    ///   &lt;abbr&gt;&lt;expan&gt;commentus es&lt;/expan&gt;commentu's&lt;/abbr&gt;         15
    ///
    /// Only the first keeps the abbreviation inside the &lt;expan&gt;; in the
    /// other three it is the enclosing &lt;abbr&gt;'s own text or the
    /// &lt;expan&gt;'s tail, and is therefore reached anyway once the
    /// &lt;expan&gt; itself contributes nothing. One rule covers all four:
    /// take the &lt;abbr&gt; within, or take nothing.
    ///
    /// The expansion is not discarded - ExtractApparatus records it as an
    /// editor's note against the same citation, keyed to the abbreviation as
    /// its lemma. Same treatment as &lt;app&gt;: the adopted reading in the
    /// text, the alternative in the Editor's Notes.
    /// </summary>
    private static XElement? ReadingOfExpansion(XElement element)
    {
        if (!string.Equals(element.Name.LocalName, "expan", StringComparison.OrdinalIgnoreCase))
            return element;

        return element.Descendants().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, "abbr", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The expanded reading an &lt;expan&gt; carries, without the abbreviation
    /// it expands - so "acturus es" rather than "acturu's acturus es".
    /// </summary>
    private static string ExpandedReading(XElement expan)
    {
        var copy = new XElement(expan);
        copy.Descendants().Where(e =>
            string.Equals(e.Name.LocalName, "abbr", StringComparison.OrdinalIgnoreCase))
            .ToList()
            .ForEach(e => e.Remove());

        return FlattenText(copy);
    }

    /// <summary>
    /// The abbreviation an &lt;expan&gt; expands, for the apparatus entry's
    /// lemma. Either inside the element or in the &lt;abbr&gt; that wraps it;
    /// the wrapper flattens to the abbreviation alone now that the
    /// &lt;expan&gt; inside it contributes nothing.
    /// </summary>
    private static string? AbbreviatedForm(XElement expan)
    {
        var inner = ReadingOfExpansion(expan);
        if (inner != null) return FlattenText(inner);

        var wrapper = expan.Ancestors().FirstOrDefault(a =>
            string.Equals(a.Name.LocalName, "abbr", StringComparison.OrdinalIgnoreCase));

        var text = wrapper == null ? null : FlattenText(wrapper);
        return string.IsNullOrWhiteSpace(text) ? null : text;
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
    ///
    /// The pairs are positional: reg/orig, abbr/expan, corr/sic.
    ///
    /// &lt;abbr&gt; is preferred over &lt;expan&gt;, which is the reverse of
    /// the other two pairs and deliberate: the abbreviation is what the
    /// manuscripts print and the expansion is the editor's, so the expansion
    /// belongs in the Editor's Notes rather than in the text. See
    /// ReadingOfExpansion. 192 &lt;choice&gt;-wrapped pairs across the corpora
    /// read the other way before this, which disagreed with the 498 that are
    /// not wrapped in a &lt;choice&gt; at all - the same abbreviation resolving
    /// two different ways inside one edition depending on how it happened to
    /// be marked up.
    /// </summary>
    private static readonly string[] PreferredChoiceReadings = { "reg", "abbr", "corr" };

    /// <summary>
    /// The counterparts to <see cref="PreferredChoiceReadings"/> - kept only
    /// when no preferred sibling exists.
    /// </summary>
    private static readonly string[] FallbackChoiceReadings = { "orig", "expan", "sic" };

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

        /// <summary>See <see cref="TextNode.IsVerse"/>.</summary>
        public bool IsVerse { get; set; }
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

            // The expanded form of an abbreviation. The text now carries the
            // abbreviation the manuscripts print, so the editor's expansion is
            // recorded here instead of being dropped - keyed to the
            // abbreviation as its lemma, the way an <app> is keyed to the
            // reading it discusses.
            if (string.Equals(name, "expan", StringComparison.OrdinalIgnoreCase))
            {
                // Inside an <app> or a <note> it is part of that entry's own
                // wording and was taken with it.
                if (el.Ancestors().Any(a =>
                        string.Equals(a.Name.LocalName, "app", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a.Name.LocalName, "note", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var expansion = ExpandedReading(el);
                if (!CarriesInformation(expansion)) continue;

                entries.Add(new ParsedApparatus
                {
                    CitationRef = citationRef,
                    SortOrder = order++,
                    Kind = "note",
                    Lemma = AbbreviatedForm(el),
                    Content = expansion
                });

                continue;
            }

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
    /// <summary>
    /// Whether any of this element's READING text is athetized.
    ///
    /// A &lt;del&gt; anywhere in the subtree used to be enough, which flagged
    /// lines whose only deletion sat inside a note or an apparatus entry -
    /// text FlattenText correctly excludes, so the reader was told a line was
    /// athetized on the strength of words it was not showing. A Euripides line
    /// was marked deleted because a note beside it quoted the play's title,
    /// "Χορός Αἰχμαλωτίδων Γυναικών". 16 nodes across 10 editions, against
    /// 6,259 flagged correctly.
    ///
    /// The test has to match FlattenText's idea of what the text is, so a
    /// deletion counts only when nothing editorial stands between it and this
    /// element.
    ///
    /// One known limit: a &lt;del&gt; inside an &lt;app&gt;'s &lt;lem&gt; IS
    /// reading text - FlattenText takes the lemma - but reads as editorial
    /// here and would be missed. Nothing in the corpora does this (zero
    /// occurrences, checked), so the simpler test is the one that can be
    /// verified; a corpus that did it would need the ancestor walk to mirror
    /// FlattenElement's app and choice handling instead.
    /// </summary>
    private static bool ContainsAthetizedText(XElement element) =>
        element.Descendants().Any(e =>
            string.Equals(e.Name.LocalName, "del", StringComparison.OrdinalIgnoreCase)
            && !e.Ancestors()
                 .TakeWhile(a => a != element)
                 .Any(a => EditorialElements.Contains(a.Name.LocalName)));

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

    /// <summary>
    /// Keeps citation references distinct within one edition.
    ///
    /// TEI numbering is often sparse: Shakespeare marks every tenth line, so a
    /// scene produces "1".."9" from the positional counter, then "10" from
    /// @n="10", then the counter's own "10" on the next line. Both nodes went
    /// in, because IX_TextNodes_Edition_Citation is not a unique index, and
    /// nothing reported it - 502 references across the corpora pointed at two
    /// nodes each. Troilus alone collided 216 times.
    /// </summary>
    private readonly CitationDisambiguator _citations = new();

    /// <summary>
    /// Apparatus found on an element that produced no text node, waiting for
    /// one to attach to.
    ///
    /// FlattenText excludes notes and apparatus, as it must, so an element
    /// holding nothing else flattens to nothing and is skipped - and the
    /// apparatus went with it, because ExtractApparatus only ran after that
    /// early return. 106 of them across 16 editions, and they are not
    /// throwaway: Polybius records an entire lost book in one,
    /// "Nihil huius libri superest", inside a &lt;p&gt; with no other text.
    ///
    /// Carried to the next node rather than keyed to a reference of its own.
    /// The empty element mints no citation, so a reference invented for it
    /// would resolve to nothing - a bookmark or an apparatus lookup would find
    /// a citation with no line behind it, which is worse than the loss it was
    /// fixing. The Menota path answers the same question the same way, and the
    /// two should not disagree.
    /// </summary>
    private readonly List<ParsedApparatus> _pendingApparatus = new();

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
        _pendingApparatus.Clear();
        _citations.Reset();
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
                    CitationRef = _citations.Unique(n),
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

        // Anything still waiting had no node after it - the file ended, or
        // every element that followed was textless too. Attached to the last
        // node instead, so a closing note lands beside the line it follows
        // rather than being dropped for want of a successor.
        //
        // Placed after both fallbacks deliberately: they are the parse's last
        // chance to produce a node, and if they do, the apparatus has
        // somewhere to go. If nothing at all was readable there is no
        // reference to attach to and the entries are dropped - a citation
        // minted for a node that does not exist would resolve to nothing,
        // which is worse than the loss.
        if (_pendingApparatus.Count > 0 && nodes.Count > 0)
        {
            var last = nodes[^1].CitationRef;
            var tail = _apparatus.Count(a => a.CitationRef == last);

            foreach (var carried in _pendingApparatus)
            {
                carried.CitationRef = last;
                carried.SortOrder = tail++;
            }

            _apparatus.AddRange(_pendingApparatus);
        }

        _pendingApparatus.Clear();

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
            else if (string.Equals(child.Name.LocalName, "lg", StringComparison.OrdinalIgnoreCase)
                     && child.Descendants().Any(d =>
                         string.Equals(d.Name.LocalName, "l", StringComparison.OrdinalIgnoreCase)))
            {
                // A verse group holding lines is a container, not a leaf.
                //
                // <lg> is in LeafElements and was tested before anything else,
                // so a stanza was flattened into one node and the <l> inside it
                // never became nodes at all. Perseus numbers those lines:
                // Theocritus' Idylls are <lg> wrapping <l n="1">, <l n="2">,
                // and all 1,142 of those numbers were discarded, so Theocritus
                // 1.1 could not be cited - the whole of Idyll 1 was one node.
                // 11,224 lines across 108 editions.
                //
                // Tested by contents rather than by attribute: @type="stanza"
                // appears on some and not others, and 26 verse groups in the
                // corpora nest a stanza inside a poem. Descending handles the
                // nesting without a branch of its own, since the inner group
                // holds lines too and the citable unit is the line either way.
                //
                // An <lg> with no <l> inside - a stanza whose text sits
                // directly in it - still falls through to the leaf branch
                // below and keeps the reference it has always had.
                WalkDiv(child, citationTrail, nodes, ref sortCounter, leafCounters);
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
                if (string.IsNullOrWhiteSpace(text))
                {
                    // No text, so no node and no citation - but the apparatus
                    // is often the whole point of such a line. Held for the
                    // next node.
                    _pendingApparatus.AddRange(ExtractApparatus(child, string.Empty));
                    continue;
                }

                // Disambiguated before the apparatus is keyed to it, so a note
                // on the second "10" attaches to that line and not to both.
                var leafRef = _citations.Unique(CapCitationRef(string.Join(".", trail)));
                AttachApparatus(child, leafRef);

                nodes.Add(new ParsedNode
                {
                    IsAthetized = ContainsAthetizedText(child),
                    CitationRef = leafRef,
                    SortOrder = sortCounter++,
                    Text = text.Trim(),
                    NodeKind = TextNodeKinds.Line,
                    IsVerse = IsVerseElement(child.Name.LocalName)
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
    /// <summary>
    /// Keys an element's apparatus to the node just emitted, and lets anything
    /// carried from an earlier textless element ride along with it.
    ///
    /// Carried entries go first: they come from earlier in the document, and
    /// SortOrder is what the Editor's Notes pane reads to put a citation's
    /// entries in order. The whole group is renumbered together, because
    /// ExtractApparatus counts from zero on every call and two calls landing
    /// on one reference would otherwise both claim entry 0.
    /// </summary>
    private void AttachApparatus(XElement element, string reference)
    {
        var entries = new List<ParsedApparatus>();

        foreach (var carried in _pendingApparatus)
        {
            carried.CitationRef = reference;
            entries.Add(carried);
        }

        _pendingApparatus.Clear();
        entries.AddRange(ExtractApparatus(element, reference));

        for (var i = 0; i < entries.Count; i++) entries[i].SortOrder = i;

        _apparatus.AddRange(entries);
    }

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
        if (string.IsNullOrWhiteSpace(text))
        {
            // A heading or stage direction that is nothing but its note. Same
            // treatment as an empty leaf: the apparatus waits for a node.
            _pendingApparatus.AddRange(ExtractApparatus(element, string.Empty));
            return;
        }

        var reference = _citations.Unique(
            CapCitationRef(string.Join(".", new List<string>(citationTrail) { segment })));
        AttachApparatus(element, reference);

        nodes.Add(new ParsedNode
        {
            CitationRef = reference,
            SortOrder = sortCounter++,
            Text = text.Trim(),
            NodeKind = kind
        });
    }

    /// <summary>
    /// Elements whose boundary is a break in the text even when the source
    /// leaves no whitespace at it.
    ///
    /// The default is now to trust the source's own spacing (see
    /// <see cref="AppendText"/>), which is right for markup that sits INSIDE a
    /// word - &lt;add&gt;, &lt;del&gt;, &lt;hi&gt;, &lt;num&gt;. It is wrong for
    /// markup that IS a boundary. A &lt;castItem&gt; holding
    /// &lt;role&gt;LEAR&lt;/role&gt;&lt;roleDesc&gt;King of Britain&lt;/roleDesc&gt;
    /// with nothing between them would otherwise flatten to "LEARKing".
    ///
    /// &lt;expan&gt; and &lt;ex&gt; WERE HERE and are not any more. They were
    /// holding apart two readings of the same word that were both being taken -
    /// &lt;expan&gt;&lt;ex&gt;Publii&lt;/ex&gt;&lt;/expan&gt;P., which had to
    /// stay two tokens rather than fuse into "PubliiP.". Now that only the
    /// abbreviation is read (see ReadingOfExpansion) there is no second reading
    /// to hold apart: an &lt;expan&gt; that contributes nothing already sets the
    /// interrupted flag by the length check, and one that contributes its
    /// &lt;abbr&gt; is a single word like any other. Removing them changes no
    /// leaf in any of the four corpora - checked, zero - and leaving them would
    /// have meant a rule whose stated reason no longer exists.
    /// </summary>
    private static readonly HashSet<string> BlockBoundaryElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "castItem", "stage", "item", "role", "roleDesc", "l", "p", "lg",
        "said", "speaker", "label", "sp", "trailer", "closer", "opener", "ab",
        "salute", "signed", "dateline", "byline", "docAuthor", "docDate", "list"
    };

    private static string FlattenText(XElement element)
    {
        var sb = new StringBuilder();
        var interrupted = false;
        FlattenElement(element, sb, ref interrupted);
        return CollapseWhitespace(sb.ToString());
    }

    /// <summary>
    /// Appends one run of source text, inserting a separator only where one is
    /// needed and the source does not already supply it.
    ///
    /// This replaces an unconditional <c>.Append(' ')</c> after every text
    /// node. That unconditional space was invisible wherever the source had
    /// whitespace at the same point - which is most places - and wrong
    /// wherever it did not:
    ///
    ///     Ἀγάθων&lt;add&gt;ος&lt;/add&gt;   ->  "Ἀγάθων ος"
    ///     ἀ&lt;del&gt;μφι&lt;/del&gt;γνοεῖν ->  "ἀ μφι γνοεῖν"
    ///     qui&lt;add&gt;c&lt;/add&gt;quam     ->  "qui c quam"
    ///     ἡ &lt;num&gt;ΑΒ&lt;/num&gt;.       ->  "ἡ ΑΒ ."
    ///
    /// TokenizeLine is a bare whitespace split, so each of those went into the
    /// word index exactly as shown: a real word destroyed and two fragments
    /// invented in its place. Measured over the four corpora, 215,471 tokens
    /// were being created this way - 19,264 of them genuine letter-to-letter
    /// word splits, the rest punctuation detached from the word it follows.
    /// The fragments are short function-word-shaped strings ("ος", "que",
    /// "c"), which is the worst possible shape for anything frequency-based.
    ///
    /// <paramref name="interrupted"/> carries the one case the source's own
    /// spacing cannot answer: an element that contributed no text at all sat
    /// between these two runs. That is either a break marker (&lt;lb/&gt;,
    /// &lt;pb/&gt;, &lt;milestone/&gt;) or an element that was skipped as
    /// editorial (&lt;note&gt;, &lt;gap/&gt;), and the two want opposite
    /// treatment:
    ///
    ///     word&lt;lb/&gt;next          -> "word next"  (fusing would be wrong)
    ///     autem&lt;note&gt;…&lt;/note&gt;, ut -> "autem, ut"  (breaking would be wrong)
    ///
    /// Deciding by element type would need a list of every marker every corpus
    /// uses. Deciding by what is on either side needs nothing: a separator
    /// goes in only where omitting it would run two word characters together.
    /// Punctuation stays attached to its word either way. 859 empty
    /// &lt;note&gt; anchors, 5,894 &lt;milestone/&gt; and 4,445 &lt;lb/&gt; sit
    /// tight against text on both sides across the corpora, so both halves of
    /// this carry weight.
    /// </summary>
    private static void AppendText(StringBuilder sb, string value, ref bool interrupted)
    {
        if (value.Length == 0) return;

        if (interrupted
            && sb.Length > 0
            && char.IsLetterOrDigit(sb[sb.Length - 1])
            && char.IsLetterOrDigit(value[0]))
        {
            sb.Append(' ');
        }

        sb.Append(value);
        interrupted = false;
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
    ///
    /// Every branch now falls through to the same two lines at the bottom
    /// rather than each one returning, because whether a child produced text
    /// is not known until it has been walked, and it is what decides whether
    /// the next run of text needs a separator in front of it.
    /// </summary>
    private static void FlattenElement(XElement element, StringBuilder sb, ref bool interrupted)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                AppendText(sb, text.Value, ref interrupted);
                continue;
            }

            if (node is not XElement child) continue;

            var name = child.Name.LocalName;
            var mark = sb.Length;

            // A speaker tag is emitted as its own node by WalkDiv, so it must
            // not also appear inside the speech - otherwise Plato reads
            // "ΣΩ. ΣΩ. ἐξ ἀγορᾶς..." and the tag is still counted as a word.
            if (string.Equals(name, "label", StringComparison.OrdinalIgnoreCase)
                && IsSpeakerLabel(child))
            {
                // Nothing taken; the length check below marks the gap.
            }
            else if (string.Equals(name, "reg", StringComparison.OrdinalIgnoreCase)
                     && IsNameAuthorityForm(child))
            {
                // A gazetteer entry, not a word of the text. See
                // IsNameAuthorityForm.
            }
            else if (string.Equals(name, "app", StringComparison.OrdinalIgnoreCase))
            {
                // Take only the adopted reading, if the entry names one.
                foreach (var lem in child.Elements().Where(e =>
                             string.Equals(e.Name.LocalName, "lem", StringComparison.OrdinalIgnoreCase)))
                {
                    FlattenElement(lem, sb, ref interrupted);
                }
            }
            else if (string.Equals(name, "expan", StringComparison.OrdinalIgnoreCase))
            {
                // The abbreviation is the text; the expansion is the editor's
                // and goes to the apparatus. See ReadingOfExpansion.
                var reading = ReadingOfExpansion(child);
                if (reading != null) FlattenElement(reading, sb, ref interrupted);
            }
            else if (string.Equals(name, "choice", StringComparison.OrdinalIgnoreCase))
            {
                var chosen =
                    PreferredChoiceReadings
                        .Select(pref => child.Elements().FirstOrDefault(e =>
                            string.Equals(e.Name.LocalName, pref, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault(e => e != null)
                    ?? FallbackChoiceReadings
                        .Select(fb => child.Elements().FirstOrDefault(e =>
                            string.Equals(e.Name.LocalName, fb, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault(e => e != null)
                    // Neither list matched, so this is a pairing this parser has
                    // not met. Take the first reading rather than none.
                    //
                    // <choice> means "here are alternatives, pick one", so the
                    // first is always A reading of the text even when it is not
                    // the one a scholar would choose - and the alternative is
                    // dropping every word in the element. Menota's manuscript
                    // encoding is exactly this shape:
                    //
                    //   <choice><me:facs>hæꝩir</me:facs><me:dipl>hefir</me:dipl>
                    //           <me:norm>hefir</me:norm></choice>
                    //
                    // 158,700 of them in the ten manuscripts, none matching any
                    // name in either list. MenotaXmlLoader handles that corpus
                    // and reads the levels properly, so nothing routes a Menota
                    // file here today - but if one ever arrives it should come
                    // out at the wrong orthographic level, not blank. Every
                    // <choice> in the Perseus corpora resolves through the two
                    // lists above, so this changes nothing there.
                    ?? child.Elements().FirstOrDefault();

                // A <choice> whose only child is an <expan> resolves to it, and
                // an <expan> is still not the reading - 9 of them wrap the
                // <abbr> that is.
                var chosenReading = chosen == null ? null : ReadingOfExpansion(chosen);
                if (chosenReading != null) FlattenElement(chosenReading, sb, ref interrupted);
            }
            else if (!EditorialElements.Contains(name))
            {
                FlattenElement(child, sb, ref interrupted);
            }

            if (sb.Length == mark)
            {
                // Contributed nothing: a break marker, or a subtree that was
                // skipped. Either way the runs on each side of it were not
                // adjacent in the source.
                interrupted = true;
            }
            else if (BlockBoundaryElements.Contains(name))
            {
                interrupted = true;
            }
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
            NodeKind = p.NodeKind,
            IsVerse = p.IsVerse
        }).ToList();
    }
}
