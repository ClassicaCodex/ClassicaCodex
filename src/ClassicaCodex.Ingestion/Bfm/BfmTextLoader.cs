using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Bfm;

/// <summary>One word, with whatever grammar the corpus recorded for it.</summary>
public sealed record BfmWord(string Form, string? Headword, string? Tag);

/// <summary>
/// One citable unit: a verse line in the poems, a paragraph in the prose.
/// </summary>
public sealed record BfmLine(
    string Citation,
    string Diplomatic,
    string Normalised,
    bool IsVerse,
    IReadOnlyList<BfmWord> Words);

public sealed record BfmText(
    string FileStem,
    string Title,
    string? Author,
    string Language,
    string? Availability,
    IReadOnlyList<BfmLine> Lines)
{
    /// <summary>
    /// Whether the scribe's reading and the editor's differ anywhere in this
    /// text. Only then is there a second edition worth making.
    /// </summary>
    public bool HasTwoReadings => Lines.Any(l => !string.Equals(l.Diplomatic, l.Normalised, StringComparison.Ordinal));

    public bool HasLemmas => Lines.Any(l => l.Words.Any(w => !string.IsNullOrEmpty(w.Headword)));

    /// <summary>
    /// Whether the Base de Français Médiéval classes this text as freely
    /// available. See <see cref="BfmTextLoader.IsFreelyAvailable"/>; a text
    /// that is not is never imported.
    /// </summary>
    public bool IsFree => BfmTextLoader.IsFreelyAvailable(Availability);
}

/// <summary>
/// Reads one text of the Base de Français Médiéval.
///
/// <b>Four shapes, one walk.</b> The corpus is TEI throughout but not one
/// encoding, and the differences are structural rather than cosmetic:
///
///   the fabliaux          verse in &lt;l n&gt;, every word a &lt;w&gt;, and
///                         &lt;choice&gt; on nearly every line - the scribe's
///                         reading and the editor's, side by side
///   verse elsewhere       no &lt;l&gt; at all; the line is whatever falls
///                         between two &lt;lb n&gt; milestones, and the words
///                         carry a lemma and a part of speech
///   tokenised prose       &lt;p n&gt; paragraphs of &lt;w&gt;
///   plain prose           &lt;p n&gt; paragraphs of running text with no
///                         tokens at all, only unnumbered &lt;lb/&gt; where
///                         the printed page broke
///
/// Walking in document order and flushing whenever the citation changes
/// handles all four, because what differs between them is only which element
/// sets the citation and whether the text arrives inside &lt;w&gt; or loose.
///
/// Measured over the 500 files: 281 are fabliaux, 112 are verse of the second
/// kind, 25 are tokenised prose and 78 are plain prose; 23 carry lemmas and
/// 311 carry a second reading.
/// </summary>
public static class BfmTextLoader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Xml = XNamespace.Xml;

    /// <summary>
    /// What the scribe put on the parchment: the original spelling, the
    /// abbreviation as written, the error uncorrected. The expansion of an
    /// abbreviation is left out, because the scribe did not write it.
    /// </summary>
    private static readonly HashSet<string> DiplomaticOnly =
        new(StringComparer.Ordinal) { "orig", "abbr", "sic", "am" };

    /// <summary>
    /// What the editor reads: the regularised spelling, the abbreviation
    /// spelled out, the correction, and the letters supplied where the page is
    /// damaged.
    /// </summary>
    private static readonly HashSet<string> NormalisedOnly =
        new(StringComparer.Ordinal) { "reg", "expan", "corr", "ex", "supplied" };

    /// <summary>
    /// Never part of the text. A note is the editor talking about the text
    /// rather than any of it, and letting one through would put a modern
    /// French sentence inside an Old French line - where it would be indexed,
    /// searched and counted as though a scribe had written it.
    /// </summary>
    private static readonly HashSet<string> NeverText =
        new(StringComparer.Ordinal) { "note", "teiHeader", "fw", "figDesc" };

    /// <summary>
    /// Elements that do not produce text themselves but whose children do, and
    /// which are not allowed to end a word.
    /// </summary>
    private const string WordElement = "w";
    private const string PunctuationElement = "pc";

    public static BfmText Load(string path)
    {
        using var reader = XmlReader.Create(path, ReaderSettings());
        return Parse(XDocument.Load(reader), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// DTD processing is on and entities are allowed to expand because some of
    /// these files declare their own; the resolver is the local one, so
    /// nothing is fetched from the network while reading a file off disk.
    /// </summary>
    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = new XmlUrlResolver(),
        MaxCharactersFromEntities = 20_000_000
    };

    public static BfmText Parse(XDocument document, string fileStem)
    {
        var root = document.Root ?? throw new InvalidDataException($"{fileStem} has no root element.");

        var lines = new List<BfmLine>();
        var body = root.Descendants(Tei + "body").FirstOrDefault();

        if (body != null) ReadBody(body, lines);

        return new BfmText(
            fileStem,
            TitleOf(root) ?? fileStem,
            AuthorOf(root),
            LanguageOf(root),
            AvailabilityOf(root),
            lines);
    }

    /// <summary>
    /// The availability class the corpus stamps on every file, as
    /// <c>&lt;ab type="availability_bfm" subtype="libre_1a"&gt;</c>.
    /// Null where the file carries the element without a class, which is how
    /// all 281 fabliaux are marked.
    /// </summary>
    public static string? AvailabilityOf(XElement root) =>
        root.Descendants(Tei + "ab")
            .Where(a => (string?)a.Attribute("type") == "availability_bfm")
            .Select(a => (string?)a.Attribute("subtype"))
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));

    /// <summary>
    /// <b>Six of the 500 texts may not be redistributed, and this is what
    /// keeps them out.</b> The corpus classes each text as libre or restreint:
    /// measured over the whole download, 208 are libre in one of six degrees,
    /// 6 are restreint, and the rest carry the element with no class at all.
    ///
    /// An unclassified text is treated as free. That is not an assumption
    /// about the unclassified ones in general - it is what the fabliaux are,
    /// and they are 281 of the 500: a separate deposit, published whole under
    /// the same Licence Ouverte, with the element present and the class simply
    /// not used. Anything explicitly restreint is refused.
    /// </summary>
    public static bool IsFreelyAvailable(string? availability) =>
        availability == null ||
        !availability.StartsWith("restreint", StringComparison.OrdinalIgnoreCase);

    private static string? TitleOf(XElement root) =>
        Clean(root.Descendants(Tei + "titleStmt").Elements(Tei + "title").FirstOrDefault()?.Value);

    /// <summary>
    /// The author, where the file names one. Much of this corpus is anonymous
    /// and says so in the word "anonyme", which is not a person and is dropped.
    /// </summary>
    private static string? AuthorOf(XElement root)
    {
        var author = Clean(root.Descendants(Tei + "titleStmt").Elements(Tei + "author").FirstOrDefault()?.Value);

        return string.IsNullOrEmpty(author) || author.Equals("anonyme", StringComparison.OrdinalIgnoreCase)
            ? null
            : author;
    }

    internal static string LanguageOf(XElement root)
    {
        var declared = root.Descendants(Tei + "language")
            .Select(l => (string?)l.Attribute("ident"))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        // fro is Old French, which is what nearly all of this is; the header
        // is trusted where it says something else.
        return string.IsNullOrWhiteSpace(declared) ? "fro" : declared!.Trim();
    }

    private static void ReadBody(XElement body, List<BfmLine> lines)
    {
        var state = new Builder(lines);
        Walk(body, state);
        state.Flush();
    }

    /// <summary>
    /// Walks the body in document order, letting the structural elements set
    /// the citation and everything else contribute text.
    ///
    /// The order matters and is the reason this is a hand-written walk rather
    /// than a query: a milestone like <c>&lt;lb n="3"/&gt;</c> means "the line
    /// that follows", so a line is only complete when the next one starts.
    /// </summary>
    private static void Walk(XElement element, Builder state)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    // Whitespace between elements is layout, not content, and
                    // letting it through is not merely untidy: it arrives as
                    // its own run, so it ends the elision after <w>d'</w> and
                    // separates a <pc> comma from the word it belongs to.
                    // That produced "d' Angleure" and "vient ," in the first
                    // run over the corpus.
                    //
                    // In the untokenised prose the text node IS the content,
                    // and it keeps its own internal spacing.
                    if (!string.IsNullOrWhiteSpace(text.Value)) state.Append(text.Value, joinRight: false);
                    break;

                case XElement child:
                    WalkElement(child, state);
                    break;
            }
        }
    }

    private static void WalkElement(XElement child, Builder state)
    {
        var name = child.Name.LocalName;

        if (NeverText.Contains(name)) return;

        switch (name)
        {
            case "l":
                // A verse line in the fabliaux. Its own number is the
                // citation, and the <lb/> milestones inside it are where the
                // manuscript broke the line, not where the verse did.
                state.StartUnit(Number(child), isVerse: true, nested: true);
                Walk(child, state);
                state.EndNestedUnit();
                return;

            case "lb":
                // Numbered, this IS the verse line - those texts have no <l>
                // at all. Unnumbered, it is where the printed page broke, and
                // the prose runs straight through it.
                //
                // The distinction is the corpus's own and it is sharp: the
                // verse writes <lb n="1"/> and the prose writes <lb/>. Reading
                // every <lb/> as a line turned the Seigneur d'Anglure's prose
                // travel diary into 2,652 numbered "verses".
                //
                // Inside an <l> it is the manuscript's own line break, which a
                // verse can span, and must not start a citation either.
                if (Number(child) != null && !state.InsideNestedUnit)
                {
                    state.StartUnit(Number(child), isVerse: true, nested: false);
                }

                return;

            case "p":
                state.StartUnit(Number(child), isVerse: false, nested: false);
                Walk(child, state);
                return;

            case WordElement:
                // Punctuation is a word here. The fabliaux use <pc>, but
                // everywhere else a comma is <w type="PONfbl">,</w> - a token
                // like any other, tagged with a part of speech that happens to
                // be punctuation. Spacing it like a word gives "vient , mene".
                if (IsPunctuationTag((string?)child.Attribute("type")))
                {
                    state.AppendPunctuation(Reading(child, DiplomaticOnly), Reading(child, NormalisedOnly));
                    return;
                }

                state.Append(Reading(child, DiplomaticOnly), Reading(child, NormalisedOnly),
                    joinRight: (string?)child.Attribute("join") == "right",
                    word: WordOf(child));
                return;

            case PunctuationElement:
                state.AppendPunctuation(Reading(child, DiplomaticOnly), Reading(child, NormalisedOnly));
                return;

            case "gap":
                // A hole in the page. Marked rather than passed over, so a
                // line that is half there does not read as a whole one.
                state.Append("[...]", joinRight: false);
                return;

            case "pb":
            case "milestone":
            case "cb":
                return;

            default:
                Walk(child, state);
                return;
        }
    }

    private static string? Number(XElement element) => Clean((string?)element.Attribute("n"));

    /// <summary>
    /// The corpus tags punctuation two ways and both have to be caught: "pon"
    /// alone, which is the single commonest tag in the whole corpus at 307,126
    /// occurrences, and the finer PONfbl (weak), PONfrt (strong), PONpga
    /// and PONpdr for the two halves of a bracket. One prefix, compared without
    /// regard to case, covers them.
    /// </summary>
    private static bool IsPunctuationTag(string? type) =>
        type != null && type.StartsWith("PON", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Marks that open rather than close, and so belong to the word after
    /// them rather than the word before.
    /// </summary>
    private static readonly HashSet<string> OpeningMarks =
        new(StringComparer.Ordinal) { "(", "[", "{", "«", "¿", "¡", "“", "‘" };

    /// <summary>
    /// One reading of a word, keeping only the branches that belong to it.
    ///
    /// <c>&lt;ex&gt;</c> is the one that matters most and the one that is not
    /// inside a <c>&lt;choice&gt;</c>: it is the editor spelling out an
    /// abbreviation, 96,872 times across the corpus. Keeping it in the
    /// diplomatic reading would put letters on the page that the scribe
    /// abbreviated away, which is the whole distinction the two readings
    /// exist to record. "S|emp|&lt;ex&gt;re&lt;/ex&gt;|s" is <i>Semps</i> as
    /// written and <i>sempres</i> as read.
    /// </summary>
    private static string Reading(XElement element, HashSet<string> keep)
    {
        var text = new StringBuilder();
        Collect(element, keep, text);
        return Collapse(text.ToString());
    }

    private static void Collect(XElement element, HashSet<string> keep, StringBuilder into)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    into.Append(text.Value);
                    break;

                case XElement child:
                    var name = child.Name.LocalName;
                    if (NeverText.Contains(name)) break;

                    // A branch belonging to the other reading is skipped
                    // whole; anything that is not a branch at all - hi,
                    // foreign, num, the choice wrapper itself - contributes
                    // its children to both.
                    var otherReading = keep == DiplomaticOnly ? NormalisedOnly : DiplomaticOnly;
                    if (otherReading.Contains(name)) break;

                    Collect(child, keep, into);
                    break;
            }
        }
    }

    private static BfmWord? WordOf(XElement word)
    {
        var form = Reading(word, NormalisedOnly);
        if (form.Length == 0) return null;

        var headword = Clean((string?)word.Attribute("lemma"));
        var tag = Clean((string?)word.Attribute("type"));

        return headword == null && tag == null ? null : new BfmWord(form, headword, tag);
    }

    private static string? Clean(string? value)
    {
        var collapsed = Collapse(value ?? string.Empty);
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Accumulates one citable unit at a time, in both readings at once.
    ///
    /// Both are built together rather than by walking twice, because the
    /// spacing between words is decided by the same rules for both and
    /// deciding it twice is how two readings of one line end up differing by a
    /// space and being stored as a genuine variant.
    /// </summary>
    private sealed class Builder
    {
        private readonly List<BfmLine> _lines;
        private readonly StringBuilder _diplomatic = new();
        private readonly StringBuilder _normalised = new();
        private readonly List<BfmWord> _words = new();

        private string? _citation;
        private bool _isVerse;

        /// <summary>
        /// Whether the next thing appended closes up against what is already
        /// there, kept separately for the two readings because they do not
        /// always agree.
        ///
        /// <b>join="right" is the reason.</b> It marks where the manuscript
        /// leaves no space between two words - these scribes separate words
        /// irregularly, and the fabliaux transcribe what is on the page. So
        /// the scribe's reading of the opening of Aloul is "Leflabel" and the
        /// editor's is "Le flabel", and applying the attribute to both would
        /// put 30,680 glued-together words into the reading that is meant to
        /// be ordinary French - and into the word index with them.
        ///
        /// Elision is different and applies to both: a word ending in an
        /// apostrophe closes up in anybody's reading.
        /// </summary>
        private bool _joinDiplomatic;

        private bool _joinNormalised;

        private int _nesting;
        private int _unnumbered;

        internal Builder(List<BfmLine> lines) => _lines = lines;

        internal bool InsideNestedUnit => _nesting > 0;

        internal void StartUnit(string? citation, bool isVerse, bool nested)
        {
            Flush();

            _citation = citation ?? $"[{++_unnumbered}]";
            _isVerse = isVerse;
            _joinDiplomatic = false;
            _joinNormalised = false;

            if (nested) _nesting++;
        }

        internal void EndNestedUnit()
        {
            if (_nesting > 0) _nesting--;
        }

        internal void Append(string text, bool joinRight) => Append(text, text, joinRight, null);

        internal void Append(string diplomatic, string normalised, bool joinRight, BfmWord? word)
        {
            var any = diplomatic.Length > 0 || normalised.Length > 0;
            if (!any)
            {
                // A word that is entirely the other reading - <orig/> against
                // a <reg>,</reg> - still ends the run, or the words either
                // side of it would be glued together.
                return;
            }

            AppendTo(_diplomatic, diplomatic, _joinDiplomatic);
            AppendTo(_normalised, normalised, _joinNormalised);

            var elided = EndsOpen(diplomatic) || EndsOpen(normalised);
            _joinDiplomatic = joinRight || elided;
            _joinNormalised = elided;

            if (word != null) _words.Add(word);
        }

        /// <summary>
        /// Punctuation attaches to the word before it, the way it is printed.
        /// </summary>
        internal void AppendPunctuation(string diplomatic, string normalised)
        {
            if (diplomatic.Length == 0 && normalised.Length == 0) return;

            // An opening bracket or quotation mark takes the space before it
            // and gives none after; everything else does the opposite.
            if (OpeningMarks.Contains(diplomatic) || OpeningMarks.Contains(normalised))
            {
                AppendTo(_diplomatic, diplomatic, _joinDiplomatic);
                AppendTo(_normalised, normalised, _joinNormalised);
                _joinDiplomatic = true;
                _joinNormalised = true;
                return;
            }

            _diplomatic.Append(diplomatic);
            _normalised.Append(normalised);
            _joinDiplomatic = false;
            _joinNormalised = false;
        }

        /// <summary>An apostrophe at the end of a word is an elision, and the next word closes up to it.</summary>
        private static bool EndsOpen(string text) =>
            text.EndsWith('\'') || text.EndsWith('’');

        private static void AppendTo(StringBuilder into, string text, bool join)
        {
            if (text.Length == 0) return;
            if (into.Length > 0 && !join) into.Append(' ');
            into.Append(text);
        }

        internal void Flush()
        {
            var diplomatic = Collapse(_diplomatic.ToString());
            var normalised = Collapse(_normalised.ToString());

            if (diplomatic.Length > 0 || normalised.Length > 0)
            {
                _lines.Add(new BfmLine(
                    _citation ?? $"[{++_unnumbered}]",
                    diplomatic,
                    normalised.Length > 0 ? normalised : diplomatic,
                    _isVerse,
                    _words.ToList()));
            }

            _diplomatic.Clear();
            _normalised.Clear();
            _words.Clear();
        }
    }
}
