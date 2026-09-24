using System.Text;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.ReM;

/// <summary>One reconstructed line of a ReM text, in both of its readings.</summary>
public sealed record ReMLine(string CitationRef, int SortOrder, string Diplomatic, string Normalised);

/// <summary>
/// One ReM text: a work in one manuscript, with everything the library needs to
/// file it and both readings of every line.
/// </summary>
public sealed record ReMText(
    string Id,
    string Title,
    string? Author,
    string? Repository,
    string? Shelfmark,
    string? Dialect,
    string? Genre,
    string CitationScheme,
    IReadOnlyList<ReMLine> Lines);

/// <summary>
/// Reads one file of the Reference Corpus of Middle High German.
///
/// <b>Why this is not the shared TeiParser.</b> ReM's TEI is a linguistic
/// export, not an edition: the whole text is one &lt;ab&gt;, every token is its
/// own &lt;w&gt;, and the only structure is a stream of empty &lt;pb/&gt;,
/// &lt;cb/&gt; and &lt;lb/&gt; milestones between them. There is no &lt;div&gt;,
/// no &lt;l&gt;, no &lt;p&gt; and no &lt;head&gt; in the entire corpus.
///
/// TeiParser's leaf elements are l, p, said and lg, and &lt;ab&gt; is neither a
/// leaf nor a container of leaves - so pointing it at ReM does not fail. It
/// emits the whole work as ONE passage with the citation "ab1" and reports a
/// successful import. The Rolandslied, 44,051 tokens, would arrive as a single
/// line. A loader that fails loudly would have been safer than that, which is
/// the reason this one exists.
///
/// <b>Three readings of every word.</b> ReM gives each token three ways, and
/// they are not interchangeable:
///
/// <code>
/// &lt;w norm="muozic" lemma="müezic"&gt;muͦzic&lt;/w&gt;
///     element text  muͦzic    what the manuscript has, letter for letter
///     @lemma        müezic   the normalised Middle High German reading
///     @norm         muozic   a simplified ASCII-ish form, for machine matching
/// </code>
///
/// <b>@lemma is not a lemma.</b> It holds the normalised form; ReM's own NEWS
/// file says so. Nothing here may put it in the Lemmas table: a headword column
/// filled with inflected forms would look right in Word Study, would index, and
/// would be wrong throughout. The real lemma, part of speech and morphology are
/// in ReM's Tabular JSON export and are not read here at all.
///
/// So the element text and @lemma become two editions of the work - the
/// manuscript's spelling and the reading text - and the reader picks between
/// them in the edition dropdown. @norm is a matching aid rather than something
/// to read, and is not stored.
/// </summary>
public static class ReMTextLoader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>
    /// ReM writes this wherever a metadata field was not filled in. It is a
    /// value, not an absence, so every read has to strip it or the library
    /// fills up with authors called "-" and shelfmarks called "--".
    /// </summary>
    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() is "-" or "--";

    private static string? Clean(string? value) =>
        IsPlaceholder(value) ? null : CollapseWhitespace(value!);

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static ReMText Load(string path) => Parse(XDocument.Load(path), Path.GetFileNameWithoutExtension(path));

    public static ReMText Parse(XDocument document, string fallbackId)
    {
        var root = document.Root ?? throw new InvalidDataException("empty document");

        var fileDesc = root.Descendants(Tei + "fileDesc").FirstOrDefault();
        var id = Clean(fileDesc?.Attribute(Xml + "id")?.Value) ?? fallbackId;

        var title = Clean(root.Descendants(Tei + "titleStmt").FirstOrDefault()
            ?.Element(Tei + "title")?.Value) ?? id;

        // The author lives in profileDesc/creation, NOT in titleStmt - titleStmt
        // carries only the five respStmt credits for the annotation team. 39 of
        // the 406 texts name one; the rest are "-", which is ReM's placeholder
        // and means anonymous.
        var author = Clean(root.Descendants(Tei + "creation").FirstOrDefault()
            ?.Element(Tei + "persName")?.Value);

        var msIdentifier = root.Descendants(Tei + "msIdentifier").FirstOrDefault();
        var repository = Clean(msIdentifier?.Element(Tei + "repository")?.Value);
        var shelfmark = Clean(msIdentifier?.Element(Tei + "idno")?.Value);

        // Several language elements, from the broadest to the most specific -
        // mhd, oberdeutsch, ostoberdeutsch, bairisch. The last real one is the
        // dialect; the earlier ones are the tree above it.
        var dialect = root.Descendants(Tei + "langUsage").FirstOrDefault()
            ?.Elements(Tei + "language")
            .Select(l => Clean(l.Value))
            .LastOrDefault(v => v != null);

        var genre = Clean(root.Descendants(Tei + "textClass").FirstOrDefault()
            ?.Descendants(Tei + "term").FirstOrDefault(t => (string?)t.Attribute("type") == "text-type")?.Value);

        // Each file says in prose what its primary line numbering counts -
        // "Hs.: Blatt (r/v), Zeile" for a manuscript foliation, "Edition" for an
        // editor's line numbers. Kept as the work's citation scheme so a reader
        // looking at "100v.5" can find out what it means.
        var scheme = Clean(root.Descendants(Tei + "encodingDesc").FirstOrDefault()
            ?.Elements(Tei + "p").FirstOrDefault()?.Value) ?? "Folio.Line";
        const string primaryPrefix = "Primary line breaks:";
        if (scheme.StartsWith(primaryPrefix, StringComparison.Ordinal))
            scheme = scheme[primaryPrefix.Length..].Trim();

        return new ReMText(
            id, title, author, repository, shelfmark, dialect, genre,
            string.IsNullOrWhiteSpace(scheme) ? "Folio.Line" : scheme,
            ReadLines(root, id));
    }

    /// <summary>
    /// Walks the token stream, cutting a line at every primary line break.
    ///
    /// The milestones carry @ed: ed="1" is the reference the text is normally
    /// cited by and ed="2" a secondary one, and they interleave freely. Mixing
    /// them would produce lines that are neither. Where a text has no primary
    /// markers at all the secondary ones are used rather than returning one
    /// enormous line, because an unreadable text is worse than an oddly cited
    /// one.
    /// </summary>
    private static List<ReMLine> ReadLines(XElement root, string textId)
    {
        var body = root.Descendants(Tei + "ab").FirstOrDefault();
        if (body == null) return new List<ReMLine>();

        var edition = PrimaryEdition(body);

        var lines = new List<ReMLine>();
        var diplomatic = new StringBuilder();
        var normalised = new StringBuilder();

        string? page = null, column = null, line = null;
        var joinToPrevious = false;
        var sortOrder = 0;

        void Flush()
        {
            var dipl = CollapseWhitespace(diplomatic.ToString());
            var norm = CollapseWhitespace(normalised.ToString());
            diplomatic.Clear();
            normalised.Clear();
            joinToPrevious = false;

            // A line break with nothing between it and the next one is a blank
            // line of the manuscript, not a passage.
            if (dipl.Length == 0 && norm.Length == 0) return;

            lines.Add(new ReMLine(Citation(page, column, line, lines.Count + 1), ++sortOrder, dipl, norm));
        }

        // Descendants, not Elements. A fifth of the corpus does not sit at the
        // top of the <ab>: 458,891 tokens are inside <q> and 7,672 inside <hi>,
        // which ReM uses as containers spanning many words rather than as
        // markup inside one. Reading only the direct children silently dropped
        // 466,563 of 2,293,068 tokens - no error, no gap in the citations, just
        // a fifth of the text quietly missing from every line that had any.
        //
        // Nothing needs pruning on the way down: a <w>'s own children are
        // <unclear>, <supplied>, <expan> and the like, which are not tokens and
        // not milestones, so they fall through both tests below. TokenText
        // handles what is inside a token.
        foreach (var node in body.Descendants())
        {
            var name = node.Name.LocalName;

            if (name is "lb" or "cb" or "pb")
            {
                // A milestone from the other numbering says nothing about where
                // this line ends.
                if ((string?)node.Attribute("ed") != edition) continue;

                if (name == "pb")
                {
                    Flush();
                    page = Clean(node.Attribute("n")?.Value);
                    column = null;
                    line = null;
                }
                else if (name == "cb")
                {
                    Flush();
                    column = Clean(node.Attribute("n")?.Value);
                }
                else
                {
                    Flush();
                    line = Clean(node.Attribute("n")?.Value);
                }

                continue;
            }

            if (name is not ("w" or "pc")) continue;

            var dipl = TokenText(node);
            var norm = NormalisedText(node, dipl);
            if (dipl.Length == 0 && norm.Length == 0) continue;

            // join="left" attaches this token to the one before, join="right"
            // attaches the next one to this, and "both" does each. It is how ReM
            // records that the manuscript wrote as one word what the reference
            // splits in two, and the reverse.
            var join = (string?)node.Attribute("join");
            var attachLeft = join is "left" or "both";

            if (diplomatic.Length > 0 && !attachLeft && !joinToPrevious) diplomatic.Append(' ');
            if (normalised.Length > 0 && !attachLeft && !joinToPrevious) normalised.Append(' ');

            diplomatic.Append(dipl);
            normalised.Append(norm);

            joinToPrevious = join is "right" or "both";
        }

        Flush();
        return lines;
    }

    /// <summary>
    /// Which @ed the text is cited by. Almost always "1"; a few texts carry only
    /// a secondary numbering, and one that carries neither gets its lines from
    /// whatever markers it does have.
    /// </summary>
    private static string PrimaryEdition(XElement body)
    {
        var breaks = body.Elements(Tei + "lb").ToList();
        if (breaks.Count == 0) return "1";

        if (breaks.Any(b => (string?)b.Attribute("ed") == "1")) return "1";

        return (string?)breaks[0].Attribute("ed") ?? "1";
    }

    /// <summary>
    /// Folio, column and line, with the parts that are not there left out.
    ///
    /// A page numbered "0" is not a page. The 163 texts cited by an editor's
    /// line numbers rather than by the manuscript still carry one &lt;pb&gt;, and
    /// it holds "0" - 151 of them across the corpus. Left in, every line of
    /// Adelbrecht's Johannes Baptista would be cited "0.1", "0.2", where the
    /// edition it is collated against says 1, 2.
    /// </summary>
    private static string Citation(string? page, string? column, string? line, int ordinal)
    {
        var parts = new[] { page, column, line }
            .Where(p => !string.IsNullOrEmpty(p) && p != "0")
            .ToList();

        return parts.Count == 0 ? ordinal.ToString() : string.Join('.', parts);
    }

    /// <summary>
    /// What the manuscript has, with the editorial apparatus inside the token
    /// resolved the way a reader wants it.
    ///
    /// &lt;del&gt; is what the scribe struck out and is dropped - the same
    /// decision Menota's loader makes, and the opposite of the one TeiParser
    /// makes for printed critical editions, where &lt;del&gt; is an editor's
    /// athetesis and belongs in the text with a mark against it. &lt;gap&gt; is
    /// empty and contributes nothing. Everything else - &lt;unclear&gt;,
    /// &lt;supplied&gt;, &lt;expan&gt;, &lt;hi&gt;, &lt;q&gt; - is text a reader
    /// should see.
    /// </summary>
    private static string TokenText(XElement token)
    {
        var text = new StringBuilder();

        foreach (var node in token.DescendantNodesAndSelf())
        {
            if (node is XText content)
            {
                if (node.Parent != null && Excluded(node.Parent)) continue;
                text.Append(content.Value);
            }
        }

        return CollapseWhitespace(text.ToString());

        static bool Excluded(XElement element)
        {
            for (var e = element; e != null; e = e.Parent)
                if (e.Name.LocalName is "del" or "gap") return true;

            return false;
        }
    }

    /// <summary>
    /// The normalised reading. Punctuation carries @lemma="--" rather than a
    /// form, so it keeps the mark itself - dropping it would leave the reading
    /// text unpunctuated where the manuscript is not.
    /// </summary>
    private static string NormalisedText(XElement token, string diplomatic)
    {
        var lemma = (string?)token.Attribute("lemma");
        return IsPlaceholder(lemma) ? diplomatic : CollapseWhitespace(lemma!);
    }
}
