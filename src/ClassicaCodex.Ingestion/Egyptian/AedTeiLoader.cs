using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Egyptian;

/// <summary>One word of an Egyptian text: its transliteration and the dictionary entry it belongs to.</summary>
public sealed record AedWord(string Form, string? LemmaId, string? Features);

/// <summary>
/// One sentence, as the Thesaurus Linguae Aegyptiae divides a text: the
/// transliteration, the German translation the TLA gives it, the hieroglyphs
/// where the TLA has encoded them, and the manuscript line it begins on.
/// </summary>
public sealed record AedSentence(
    string Id,
    string Transliteration,
    string? German,
    string? Hieroglyphs,
    string? Line,
    IReadOnlyList<AedWord> Words);

/// <summary>One text of the corpus - a spell, a letter, a story, a medical recipe - and what is known of its source.</summary>
public sealed record AedText(
    string Id,
    string Subcorpus,
    string ObjectName,
    string Title,
    IReadOnlyList<string> Editors,
    IReadOnlyList<AedSentence> Sentences);

/// <summary>A headword of the TLA dictionary, with its part of speech and German meaning.</summary>
public sealed record AedDictionaryEntry(string Id, string Headword, string? PartOfSpeech, string? German, string? Reference);

/// <summary>
/// Reads Egyptian texts out of AED-TEI, Simon Schweitzer's TEI conversion of
/// the January 2018 snapshot of the Thesaurus Linguae Aegyptiae - the
/// Berlin-Leipzig dictionary project's corpus - released under CC BY-SA 4.0.
///
/// <b>Four files per text.</b> The base file carries the sentences and their
/// words, each word pointing into the TLA dictionary. A <c>_st</c> file gives
/// the German translation of each sentence and a <c>_hiero</c> file its
/// hieroglyphs, both pointing back at the base file's sentence ids. The
/// fourth, <c>_wt</c>, glosses each word; Word Study takes that from the
/// dictionary instead, so it is not read.
///
/// <b>What a work is.</b> The TLA files every text under an object - the
/// papyrus, stela or wall it is written on - and this conversion keeps the
/// object's own name but not the object above it. For a papyrus that is
/// exactly right: the Papyrus of Nu is an object, and its spells are its texts.
/// For a tomb it is not: the conversion knows a text is on "Westwand" and not
/// whose west wall. So only the subcorpora whose objects are whole documents
/// are read (<see cref="Subcorpora"/>), and the wall-by-wall ones are left
/// out rather than filed as hundreds of works called "West wall".
///
/// <b>The editorial marks are inside the words.</b> A restored sign, a
/// damaged one, an erasure is marked within the word it belongs to, and is
/// rendered here in the brackets Egyptologists print: [restored], ⸢damaged⸣,
/// {superfluous}, ⸌added⸍, ⟦erased⟧, and (?) after an uncertain reading. A
/// lost stretch between words, marked as a gap, is […].
/// </summary>
public static class AedTeiLoader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Xml = XNamespace.Xml;

    /// <summary>
    /// The subcorpora read, with the name each is shown under. These are the
    /// ones whose objects are whole documents - a manuscript, a stela, a
    /// letter, a rock inscription - so that one object makes one sensible work.
    ///
    /// Left out, because their objects are surfaces of a building whose name
    /// the conversion does not keep: the Old Kingdom tomb inscriptions
    /// (bbawgrabinschriften), the TLA's own Pyramid Texts (bbawpyramidentexte,
    /// which the library already has, parsed, from Universal Dependencies),
    /// the Amarna and Ramesside monuments, and the Late Period tombs.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Subcorpora = new Dictionary<string, string>
    {
        ["tb"] = "Book of the Dead",
        ["sawmedizin"] = "Medical papyri",
        ["sawlit"] = "Literary texts",
        ["bbawtotenlit"] = "Late funerary papyri",
        ["bbawtempelbib"] = "Temple library papyri",
        ["bbawarchive"] = "Papyrus archives",
        ["bbawbriefe"] = "Letters",
        ["bbawfelsinschriften"] = "Rock inscriptions",
        ["bbawhistbiospzt"] = "Late Period historical inscriptions",
        ["tuebingerstelen"] = "Stelae in Tübingen",
        ["smaek"] = "Objects in the Munich Egyptian Museum"
    };

    public const string ConcordanceFileName = "concordance_name_text_id.csv";
    public const string DictionaryFileName = "dictionary.xml";

    /// <summary>
    /// The texts of the chosen subcorpora, read from an extracted AED-TEI
    /// release. <paramref name="root"/> is the folder holding the concordance
    /// and the <c>files</c> folder.
    /// </summary>
    public static List<AedText> Load(
        string root, IProgress<string>? progress = null, List<(string Text, string Problem)>? problems = null)
    {
        var filesDir = Path.Combine(root, "files");
        var concordance = ReadConcordance(Path.Combine(root, ConcordanceFileName));
        var texts = new List<AedText>();

        var chosen = concordance.Where(c => Subcorpora.ContainsKey(c.Subcorpus)).ToList();
        for (var i = 0; i < chosen.Count; i++)
        {
            var entry = chosen[i];
            var basePath = Path.Combine(filesDir, entry.Id + ".xml");
            if (!File.Exists(basePath)) continue;

            try
            {
                var text = ReadText(entry, basePath,
                    Path.Combine(filesDir, entry.Id + "_st.xml"),
                    Path.Combine(filesDir, entry.Id + "_hiero.xml"),
                    problems);
                if (text.Sentences.Count > 0) texts.Add(text);
            }
            catch (XmlException ex)
            {
                // A base file that is not well-formed loses its text, and only
                // its text; the rest of the corpus is not held hostage to it.
                problems?.Add(($"{entry.ObjectName} // {entry.Title}", $"not well-formed: {ex.Message}"));
            }

            if ((i + 1) % 250 == 0) progress?.Report($"Read {i + 1:N0} of {chosen.Count:N0} texts...");
        }

        return texts;
    }

    /// <summary>One line of the concordance: <c>subcorpus:object//text</c>, a tab, the text id.</summary>
    public sealed record ConcordanceEntry(string Id, string Subcorpus, string ObjectName, string Title);

    /// <summary>
    /// The concordance names every text as <c>subcorpus:object//text</c>. The
    /// subcorpus is a plain identifier, so the first colon ends it; an object
    /// name can itself contain a colon ("Liebeslied Nr. 31-47: pChester Beatty
    /// I"), so nothing after the first one is split on. Names are stored with
    /// HTML entities (<c>&amp;quot;Bremner Rhind&amp;quot;</c>).
    /// </summary>
    public static List<ConcordanceEntry> ReadConcordance(string path)
    {
        var entries = new List<ConcordanceEntry>();
        foreach (var raw in File.ReadLines(path, Encoding.UTF8))
        {
            var tab = raw.LastIndexOf('\t');
            if (tab <= 0) continue;

            var name = WebUtility.HtmlDecode(raw[..tab]);
            var id = raw[(tab + 1)..].Trim();
            var colon = name.IndexOf(':');
            if (colon <= 0 || id.Length == 0) continue;

            var rest = name[(colon + 1)..];
            var split = rest.IndexOf("//", StringComparison.Ordinal);
            var objectName = split >= 0 ? rest[..split] : rest;
            var title = split >= 0 ? rest[(split + 2)..] : rest;

            entries.Add(new ConcordanceEntry(id, name[..colon], CleanTitle(objectName), CleanTitle(title)));
        }

        return entries;
    }

    public static AedText ReadText(
        ConcordanceEntry entry, string basePath, string translationPath, string hieroglyphPath,
        List<(string Text, string Problem)>? problems = null)
    {
        var doc = LoadXml(basePath);
        var german = ReadStandoff(entry, translationPath, RenderPlain, "translation", problems);
        var signs = ReadStandoff(entry, hieroglyphPath, RenderSigns, "hieroglyphs", problems);

        var editors = doc.Descendants(Tei + "respStmt")
            .Where(r => (string?)r.Element(Tei + "resp") == "edition by")
            .SelectMany(r => r.Elements(Tei + "name"))
            .Select(n => n.Value.Trim())
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();

        var sentences = new List<AedSentence>();
        var body = doc.Descendants(Tei + "body").FirstOrDefault();
        string? line = null;

        if (body != null)
        {
            foreach (var s in body.Descendants(Tei + "s"))
            {
                var id = (string?)s.Attribute(Xml + "id") ?? string.Empty;
                var (text, words, firstLine, lastLine) = RenderSentence(s);
                line = firstLine ?? line;

                if (text.Length > 0)
                {
                    sentences.Add(new AedSentence(
                        id,
                        text,
                        german.TryGetValue(id, out var g) && g.Length > 0 ? g : null,
                        signs.TryGetValue(id, out var h) && h.Length > 0 ? h : null,
                        firstLine ?? line,
                        words));
                }

                line = lastLine ?? line;
            }
        }

        return new AedText(entry.Id, entry.Subcorpus, entry.ObjectName, entry.Title, editors, sentences);
    }

    /// <summary>
    /// One base-file sentence: its words joined into the transliteration, the
    /// words themselves for the dictionary, and the manuscript lines it starts
    /// and ends on. A line break inside a sentence is a page fact, not a word
    /// boundary, so it adds nothing to the text.
    /// </summary>
    private static (string Text, List<AedWord> Words, string? FirstLine, string? LastLine) RenderSentence(XElement s)
    {
        var parts = new List<string>();
        var words = new List<AedWord>();
        string? first = null, last = null;
        var sawWord = false;

        foreach (var node in s.Elements())
        {
            var name = node.Name.LocalName;
            if (name == "lb")
            {
                var n = ((string?)node.Attribute("n"))?.Trim();
                if (!string.IsNullOrEmpty(n))
                {
                    if (!sawWord && first == null) first = n;
                    last = n;
                }
            }
            else if (name == "gap")
            {
                if (parts.Count == 0 || parts[^1] != "[…]") parts.Add("[…]");
            }
            else if (name == "w")
            {
                sawWord = true;
                var shown = RenderWord(node);
                if (shown.Length == 0) continue;
                parts.Add(shown);

                var bare = Bare(node);
                var lemma = ((string?)node.Attribute("lemmaRef"))?.Replace("tla:", string.Empty);
                var feats = (string?)node.Element(Tei + "fs")?.Attribute("feats");
                if (bare.Length > 0) words.Add(new AedWord(bare, lemma, feats));
            }
        }

        return (string.Join(" ", parts), words, first, last);
    }

    /// <summary>A word as Egyptologists print it, with the editors' brackets.</summary>
    public static string RenderWord(XElement w)
    {
        var sb = new StringBuilder();
        foreach (var node in w.Nodes()) Append(sb, node);
        return Collapse(sb.ToString());
    }

    private static void Append(StringBuilder sb, XNode node)
    {
        if (node is XText t)
        {
            sb.Append(t.Value);
            return;
        }

        if (node is not XElement e || e.Name.LocalName == "fs") return;

        var inner = new StringBuilder();
        foreach (var child in e.Nodes()) Append(inner, child);
        var content = inner.ToString();

        switch (e.Name.LocalName)
        {
            case "supplied": sb.Append('[').Append(content).Append(']'); break;
            case "damage": sb.Append('⸢').Append(content).Append('⸣'); break;
            case "surplus": sb.Append('{').Append(content).Append('}'); break;
            case "add": sb.Append('⸌').Append(content).Append('⸍'); break;
            case "del": sb.Append('⟦').Append(content).Append('⟧'); break;
            case "unclear": sb.Append(content).Append("(?)"); break;
            default: sb.Append(content); break;
        }
    }

    /// <summary>The word's letters without any brackets - what the dictionary maps.</summary>
    public static string Bare(XElement w) =>
        Collapse(string.Concat(w.DescendantNodes().OfType<XText>()
            .Where(t => t.Parent?.Name.LocalName != "fs")
            .Select(t => t.Value)));

    private static string RenderPlain(XElement s) => Collapse(s.Value);

    /// <summary>
    /// The hieroglyphs of a sentence: its words' signs, a space between words.
    /// Only the signs are kept - an uncertain group is still the group - so the
    /// markup around them adds nothing here.
    /// </summary>
    private static string RenderSigns(XElement s) =>
        string.Join(" ", s.Elements(Tei + "w")
            .Select(w => Signs(Collapse(w.Value)))
            .Where(v => v.Length > 0));

    private static readonly Regex NoUnicodeForTheseWritings =
        new(@"Keine Entsprechung in Unicode für diese Graphien", RegexOptions.Compiled);

    private static readonly Regex NoUnicodeForCodes =
        new(@"Keine Entsprechung in Unicode für\s+(\S+)", RegexOptions.Compiled);

    /// <summary>
    /// Where a sign group has no Unicode character, the TLA wrote a German
    /// sentence into the hieroglyph field instead: "Keine Entsprechung in
    /// Unicode für D58-X1-N37A" (no Unicode equivalent for), about 25,000 times
    /// across the release. Shown as the group's Gardiner codes in angle
    /// brackets, which tell an Egyptologist exactly what is there, or as a
    /// single box where the TLA gave no codes ("für diese Graphien").
    /// </summary>
    public static string Signs(string raw)
    {
        var s = NoUnicodeForTheseWritings.Replace(raw, "□");
        return NoUnicodeForCodes.Replace(s, m => $"⟨{m.Groups[1].Value}⟩");
    }

    /// <summary>
    /// A translation or hieroglyph file, read so that a broken one costs only
    /// itself. Some <c>_hiero</c> files in the release are not well-formed (an
    /// <c>unclear</c> element closed in the wrong place); the text is still
    /// worth having in transliteration and German without its signs.
    /// </summary>
    private static Dictionary<string, string> ReadStandoff(
        ConcordanceEntry entry, string path, Func<XElement, string> render, string layer,
        List<(string Text, string Problem)>? problems)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return ReadSentenceMap(LoadXml(path), render);
        }
        catch (XmlException ex)
        {
            problems?.Add(($"{entry.ObjectName} // {entry.Title}", $"{layer} not well-formed, left out: {ex.Message}"));
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Sentence id in the base file (the <c>src:</c> pointer) to its rendering in a standoff file.</summary>
    private static Dictionary<string, string> ReadSentenceMap(XDocument doc, Func<XElement, string> render)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var body = doc.Descendants(Tei + "body").FirstOrDefault();
        if (body == null) return map;

        foreach (var s in body.Descendants(Tei + "s"))
        {
            var corresp = (string?)s.Attribute("corresp");
            if (string.IsNullOrEmpty(corresp)) continue;
            var id = corresp.StartsWith("src:", StringComparison.Ordinal) ? corresp[4..] : corresp;
            map[id] = render(s);
        }

        return map;
    }

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private static string Collapse(string s) => Spaces.Replace(s, " ").Trim();

    private static XDocument LoadXml(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// The TLA dictionary: headword, part of speech and German meaning for
    /// each id a word points at. 30,000-odd entries.
    /// </summary>
    public static Dictionary<string, AedDictionaryEntry> ReadDictionary(string path)
    {
        var entries = new Dictionary<string, AedDictionaryEntry>(StringComparer.Ordinal);
        if (!File.Exists(path)) return entries;

        var doc = LoadXml(path);
        foreach (var e in doc.Descendants(Tei + "entry"))
        {
            var id = ((string?)e.Attribute(Xml + "id"))?.Trim();
            var headword = Collapse(e.Element(Tei + "form")?.Element(Tei + "orth")?.Value ?? string.Empty);
            if (string.IsNullOrEmpty(id) || headword.Length == 0) continue;

            var pos = e.Element(Tei + "gramGrp")?.Element(Tei + "term")?.Value.Trim();
            var sense = e.Element(Tei + "sense");
            var german = sense?.Elements(Tei + "cit")
                .Where(c => (string?)c.Attribute("type") == "translation")
                .Select(c => Collapse(c.Value))
                .FirstOrDefault(v => v.Length > 0);
            var reference = sense?.Elements(Tei + "cit")
                .Select(c => c.Element(Tei + "bibl")?.Value.Trim())
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));

            entries[id] = new AedDictionaryEntry(id, headword, string.IsNullOrEmpty(pos) ? null : pos, german, reference);
        }

        return entries;
    }

    private static readonly Regex SpellTitle = new(@"^\(?\s*Tb\s+0*(\d+)\s*([A-Z])?\b", RegexOptions.Compiled);

    /// <summary>
    /// The number a Book of the Dead spell is cited by - "Tb 017" is spell 17,
    /// "Tb 038 A" is 38A, and the Papyrus of Nu's "(Tb 190)" is 190 - or null
    /// when the title is not a spell at all ("Anbetung des Osiris", a hymn
    /// copied into a Book of the Dead papyrus).
    /// </summary>
    public static string? SpellNumber(string title)
    {
        var m = SpellTitle.Match(title);
        if (!m.Success) return null;
        return m.Groups[2].Success ? m.Groups[1].Value + m.Groups[2].Value : m.Groups[1].Value;
    }

    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Orders texts within a document by the first number in their titles, so
    /// a gloss on Ebers 185 sits beside Ebers 185 rather than after Ebers 877,
    /// and then by the title with every number padded (Eb 185 before Eb 1000).
    /// </summary>
    public static string NaturalKey(string title)
    {
        var first = Digits.Match(title);
        var padded = Digits.Replace(title, m => m.Value.PadLeft(8, '0'));
        return first.Success ? $"{first.Value.PadLeft(8, '0')}\u0001{padded}" : $"~\u0001{padded}";
    }

    private static readonly Regex DollarMarks = new(@"\$([^$]*)\$", RegexOptions.Compiled);

    /// <summary>
    /// A title as a reader should see it. The TLA marks transliteration inside
    /// a German title with dollar signs ("$nfr$-Glosse zu Eb 185"); the word is
    /// kept and the marks dropped.
    /// </summary>
    public static string CleanTitle(string title) => Collapse(DollarMarks.Replace(title, "$1"));
}
