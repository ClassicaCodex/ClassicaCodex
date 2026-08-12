using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Gets a Menota file into an XDocument without touching the network.
///
/// Extracted from MenotaCorpusReport so the survey and the ingest read files
/// identically. They must: a survey that resolves an entity the ingest does
/// not would report a corpus that differs from the one imported, and the
/// difference would be invisible.
/// </summary>
public static class MenotaXmlLoader
{
    public static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    public static readonly XNamespace Me = "http://www.menota.org/ns/1.0";

    /// <summary>
    /// Menota files declare a DOCTYPE whose internal subset pulls in a remote
    /// entity file from menota.org. Left in place, parsing either fails on the
    /// first undefined entity or silently makes a network request - neither
    /// acceptable in an offline reader.
    /// </summary>
    private static readonly Regex DoctypeSubset =
        new("<!DOCTYPE\\s+TEI\\s*\\[.*?\\]\\s*>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex EntityRef =
        new("&([A-Za-z][A-Za-z0-9._-]*);", RegexOptions.Compiled);

    private static readonly Regex EntityDecl =
        new("<!ENTITY\\s+(\\S+)\\s+\"([^\"]*)\"", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly HashSet<string> XmlBuiltinEntities =
        new(StringComparer.Ordinal) { "amp", "lt", "gt", "quot", "apos" };

    public sealed class LoadResult
    {
        public XDocument? Document;
        public int UnresolvedEntities;
        public HashSet<string> DistinctUnresolved = new(StringComparer.Ordinal);
        public string? Error;

        public bool Ok => Error == null && Document != null;
    }

    /// <summary>
    /// Reads menota-entities.txt from the folder, if the user saved one there.
    /// These are MUFI character entities - twodotPM for a two-dot punctuation
    /// mark, et for a Tironian et. Seventy-odd distinct ones in a single
    /// manuscript, twenty thousand references.
    /// </summary>
    public static Dictionary<string, string> LoadEntities(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(folder, "menota-entities.txt");
        if (!File.Exists(path)) return map;

        foreach (Match m in EntityDecl.Matches(File.ReadAllText(path)))
            map[m.Groups[1].Value] = m.Groups[2].Value;

        return map;
    }

    public static LoadResult Load(string filePath, IReadOnlyDictionary<string, string> entities)
    {
        var result = new LoadResult();

        try
        {
            var cleaned = DoctypeSubset.Replace(File.ReadAllText(filePath), string.Empty);

            cleaned = EntityRef.Replace(cleaned, m =>
            {
                var name = m.Groups[1].Value;
                if (XmlBuiltinEntities.Contains(name)) return m.Value;
                if (entities.TryGetValue(name, out var replacement)) return replacement;

                // Substituted rather than dropped, so the character's absence
                // stays visible instead of silently closing a gap in the word.
                result.UnresolvedEntities++;
                result.DistinctUnresolved.Add(name);
                return "\uFFFD";
            });

            result.Document = XDocument.Parse(cleaned);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// The element holding one orthographic level of a word, or null if the
    /// word does not carry that level.
    ///
    /// Descendants, not Elements. Menota encodes the levels two ways - as
    /// direct children of &lt;w&gt;, and wrapped in a &lt;choice&gt; - and a
    /// single file mixes them. AM 619 4to has 434 choice-wrapped words among
    /// 61,617 direct ones; AM 28 8vo is choice-wrapped throughout. Reading
    /// only direct children reports AM 28 as having no diplomatic level at
    /// all, which is what MenotaCorpusReport currently does.
    ///
    /// An EMPTY level element counts as not carrying the level. Holm perg 4
    /// fol transcribes at the diplomatic level and leaves the other two as
    /// placeholders - &lt;me:facs/&gt;&lt;me:norm/&gt;, present and empty, on
    /// 114,684 and 106,989 of its 115,241 words. Treated as present, they
    /// broke the manuscript three ways at once: ChooseReadingLevel saw 100%
    /// normalised coverage and picked "norm"; the ?? fallbacks below stopped
    /// at the empty element instead of falling through to the diplomatic
    /// reading, because "" is not null; and WordText then returned nothing for
    /// 93% of the words, so they were skipped. The manuscript ingested as 7%
    /// of itself and reported full coverage while doing it.
    ///
    /// &lt;ex&gt; inside a level is the editorial expansion of an
    /// abbreviation, and .Value includes it - which is right: the diplomatic
    /// reading of an abbreviated word is the expanded word.
    /// </summary>
    public static XElement? LevelElement(XElement word, string level) =>
        word.Descendants(Me + level).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Value));

    /// <summary>
    /// The text of one orthographic level of a word, or null if the word does
    /// not carry that level. See <see cref="LevelElement"/>.
    /// </summary>
    public static string? Level(XElement word, string level) => LevelElement(word, level)?.Value;

    /// <summary>
    /// The readable text of an element that contains word markup, at one
    /// orthographic level.
    ///
    /// Never use .Value on anything containing &lt;w&gt;. A Menota word holds
    /// its facsimile, diplomatic and normalised readings as sibling children,
    /// so .Value returns every level of every word run together: a heading
    /// reading "Vpphaf" comes back as "VpphafVpphaf", and one with an
    /// editorial note in it as Fr(o)SveiniJarli"mgl"Fr(o)SveiniJarli.
    ///
    /// This is why so few headings matched their catalogue titles. AM 619 4to
    /// concealed it by carrying only the diplomatic level - one reading per
    /// word, so concatenating them looked like plain text - and AM 242 fol
    /// concealed it by having plain-text headings with no word markup at all.
    /// Every manuscript carrying both facs and dipl in its headings showed it
    /// at once.
    /// </summary>
    /// <summary>
    /// Whether this element is inside an editorial note rather than in the
    /// text the scribe wrote.
    ///
    /// Menota puts variant readings from other manuscripts inside
    /// &lt;note&gt;, marked up as words exactly like the text around them:
    ///
    ///   &lt;w&gt;riodanda&lt;/w&gt;
    ///   &lt;note&gt;&lt;w&gt;riodanda&lt;/w&gt; : &lt;w&gt;riothandi&lt;/w&gt; (Ms. AM 18 fol.)&lt;/note&gt;
    ///
    /// Read as running text that gives "riodanda riodanda riothandi", with a
    /// reading from a different manuscript spliced into this one. AM 63 fol
    /// has 4,159 such notes carrying 10,632 words - 11% of what was being
    /// counted as its text.
    ///
    /// This is the same failure as the Perseus corpus's inline apparatus, in a
    /// different encoding: an editor's collation sitting inside the text
    /// stream, indistinguishable from it unless you check the ancestry.
    /// </summary>
    public static bool IsEditorial(XElement el) =>
        el.Ancestors(Tei + "note").Any();

    public static string WordsText(XElement container, string level)
    {
        var words = container.Descendants(Tei + "w")
            .Where(w => !IsEditorial(w) || container.Name == Tei + "note")
            .ToList();
        if (words.Count == 0) return Collapse(container.Value);

        var parts = new List<string>();

        foreach (var word in words)
        {
            // <del> in a manuscript transcription is the scribe's own
            // deletion, not an editor's athetesis, and is not part of the
            // text being read.
            if (word.Ancestors(Tei + "del").Any()) continue;

            var text = Level(word, level)
                       ?? Level(word, "dipl")
                       ?? Level(word, "facs")
                       ?? word.Value;

            text = Collapse(text);
            if (text.Length > 0) parts.Add(text);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Normalises whitespace and drops invisible formatting characters -
    /// Menota wraps marks in U+2060 WORD JOINER, which has no width and no
    /// glyph and would otherwise sit inside stored tokens where nothing could
    /// show it and no typed search term could match it.
    /// </summary>
    public static string Collapse(string s)
    {
        var visible = new string(s.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.Format).ToArray());

        return string.Join(" ", visible.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// A division's first heading, at the given orthographic level, skipping
    /// editorial ones where the manuscript distinguishes them.
    /// </summary>
    public static string FirstHeading(XElement div, string level, bool scribalOnly)
    {
        var heads = div.Elements(Tei + "head");
        if (scribalOnly) heads = heads.Where(h => h.Descendants(Tei + "w").Any());

        var head = heads.FirstOrDefault();
        return head == null ? "" : Collapse(WordsText(head, level));
    }

    /// <summary>
    /// Whether this manuscript's headings carry word markup.
    ///
    /// Decides which headings are the scribe's and which are the editor's. A
    /// transcribed heading is tagged word by word like the rest of the text;
    /// an editorial one is plain prose, because the editor is not being
    /// transcribed. AM 619 4to has 117 headings, 79 with word markup and 38
    /// without - and the 38 are things like "Denne overskriften star ikke i
    /// den gammelnorske" (this heading is not in the Old Norwegian), which
    /// arrived in the library as work titles.
    ///
    /// Answered per manuscript because some encode no headings with word
    /// markup at all. AM 242 fol's headings are plain text and are the real
    /// titles - GYLFAGINNING, SKALDSKAPARMAL - so the test must be whether
    /// this file distinguishes the two, not whether a given heading has words
    /// in it.
    /// </summary>
    public static bool HeadingsUseWordMarkup(XDocument doc) =>
        doc.Descendants(Tei + "head")
            .Any(h => h.Descendants(Tei + "w").Any(w => !IsEditorial(w)));

    /// <summary>
    /// The words held directly by a division, excluding those belonging to
    /// divisions nested inside it.
    ///
    /// Descendants(w) counts a container's children as its own, which double
    /// counts; Elements(w) misses everything, because words live inside p, l
    /// and s rather than directly under div. Nearest-ancestor is the test that
    /// actually means "belongs to this division".
    /// </summary>
    public static IEnumerable<XElement> DirectWords(XElement div) =>
        div.Descendants(Tei + "w")
            .Where(w => !IsEditorial(w))
            .Where(w => w.Ancestors(Tei + "div").FirstOrDefault() == div);

    /// <summary>
    /// Whether a text container - a p, l or lg - belongs to this division
    /// rather than to one nested inside it.
    /// </summary>
    public static bool BelongsTo(XElement container, XElement div) =>
        container.Ancestors(Tei + "div").FirstOrDefault() == div;

    /// <summary>
    /// Which orthographic level a document actually carries throughout, in
    /// preference order. Returned once per manuscript rather than decided per
    /// word: a text assembled from normalised words where they exist and
    /// diplomatic ones where they don't has an orthography belonging to no
    /// scribe and no dictionary, and nothing downstream could tell.
    /// </summary>
    /// <param name="missing">
    /// How many words lack the chosen level. Not zero as often as one would
    /// like - AM 132 fol has a section at 99% - and those words are read at
    /// whatever level they do carry, so a normalised text ends up with a few
    /// diplomatic spellings in it. Small, but it should be visible rather than
    /// rounded away.
    /// </param>
    public static string ChooseReadingLevel(XDocument doc, out double coverage, out int missing)
    {
        var words = doc.Descendants(Tei + "w").Where(w => !IsEditorial(w)).ToList();
        coverage = 0;
        missing = 0;
        if (words.Count == 0) return "dipl";

        foreach (var level in new[] { "norm", "dipl", "facs" })
        {
            var have = words.Count(w => Level(w, level) != null);
            var pct = (double)have / words.Count;
            if (pct >= 0.9)
            {
                coverage = pct;
                missing = words.Count - have;
                return level;
            }
        }

        var withDipl = words.Count(w => Level(w, "dipl") != null);
        coverage = (double)withDipl / words.Count;
        missing = words.Count - withDipl;
        return "dipl";
    }
}
