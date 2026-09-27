using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Geste;

/// <summary>One verse line, in both readings, with the annotation its words carry.</summary>
public sealed record GesteLine(
    string CitationRef,
    int SortOrder,
    string Diplomatic,
    string Normalised,
    IReadOnlyList<GesteWord> Words);

/// <summary>
/// One annotated word. Lemma is a real Old French dictionary headword with
/// Tobler-Lommatzsch homograph numbering - estre1, roi2 - and not, as in ReM's
/// TEI, a normalised form wearing the name.
/// </summary>
public sealed record GesteWord(string Form, string Headword, string? Tag);

/// <summary>One text: a chanson de geste in one witness.</summary>
public sealed record GesteText(
    string Id,
    string Title,
    string? Author,
    string? Witness,
    string Language,
    bool IsTranscription,
    IReadOnlyList<GesteLine> Lines);

/// <summary>
/// Reads one file of Geste, Jean-Baptiste Camps's corpus of chansons de geste.
///
/// TEI P5, verse, with real linguistic annotation: &lt;lg type="laisse"&gt;
/// holding &lt;l n&gt; holding &lt;w lemma pos msd&gt;. Closer to something the
/// shared TeiParser could read than ReM was - it has real &lt;l&gt; elements -
/// but not close enough, for four reasons this class exists to handle.
///
/// <b>Every figure in this file was measured against the pinned corpus</b>
/// (34 files, commit 71737a2), because the published description of it differs
/// from the files in several places that would have produced silent damage.
/// </summary>
public static class GesteTextLoader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    /// <summary>
    /// Elements inside a &lt;choice&gt; that give the manuscript's own reading,
    /// against those that give an editor's resolution of it.
    ///
    /// &lt;corr&gt; is in BOTH lists and that is deliberate. orig/reg and
    /// abbr/expan are a manuscript fact and its modern rendering - two readings
    /// of the same text, which is what the two editions are for. sic/corr is
    /// something else: an editor correcting an error in the printed source they
    /// were transcribing. There is no sense in which the sic is "what the
    /// manuscript has", so presenting it as a diplomatic reading would invent a
    /// variant. Measured: ed_FloovG has 110 corr/sic pairs and no orig/reg at
    /// all, while transcr_Otin_B has 12,154 orig/reg - the two kinds of markup
    /// belong to the editions and the transcriptions respectively.
    /// </summary>
    private static readonly string[] DiplomaticBranches = { "orig", "abbr", "corr" };

    private static readonly string[] NormalisedBranches = { "reg", "expan", "corr" };

    /// <summary>
    /// Two of the 34 files declare a DOCTYPE with 98 entities that expand to
    /// markup - &amp;d-oncial; becomes a &lt;choice&gt; allograph pair. .NET
    /// refuses them by default ("Reference to undeclared entity 'd-oncial'"),
    /// and the entities cannot simply be stripped: doing so would take the
    /// letterforms with them.
    ///
    /// The DTD travels in the archive at dtd/abreviations.dtd and the DOCTYPE
    /// points at it relatively, so an XmlUrlResolver finds it as long as the
    /// tree is extracted whole. The entity expansion limit is generous but
    /// finite rather than unlimited: the archive is pinned and checksummed, but
    /// an unbounded expansion on a downloaded file is not a thing to leave open.
    /// </summary>
    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = new XmlUrlResolver(),
        MaxCharactersFromEntities = 20_000_000
    };

    public static GesteText Load(string path)
    {
        using var reader = XmlReader.Create(path, ReaderSettings());
        return Parse(XDocument.Load(reader), Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>
    /// <b>The identity comes from the filename, never from xml:id.</b> Three of
    /// the 34 files carry someone else's: ed_HuonG.xml says
    /// xml:id="ed_GuiBourgG", ed_OtinG_pos.xml says "ed_OtinG" and
    /// ed_DoonRocheM_num.xml says "ed_DoonRocheM" - and in two of those three
    /// cases the file it names also exists. Keying on xml:id would give two
    /// works one CtsUrn and silently install one of them over the other: Huon
    /// de Bordeaux, 10,495 lines, would have overwritten Gui de Bourgogne.
    /// </summary>
    public static GesteText Parse(XDocument document, string fileStem)
    {
        var root = document.Root ?? throw new InvalidDataException("empty document");

        var title = Clean(root.Descendants(Tei + "titleStmt").FirstOrDefault()
            ?.Element(Tei + "title")?.Value) ?? fileStem;

        return new GesteText(
            fileStem,
            title,
            // No author, deliberately, and this is a decision rather than a
            // shortcut. titleStmt/author holds exactly four values across the 34
            // files: "Anonyme" three times, and "Batova Ekaterina" once - who is
            // the modern encoder of that file, not the author of Girart de
            // Vienne. Elsewhere in the same files <author> carries Paul Meyer,
            // Giulio Bertoni, R. Menendez Pidal and A. Scheler, the nineteenth
            // and twentieth-century editors of the printed sources.
            //
            // The field mixes modern editors with medieval poets and cannot be
            // told apart mechanically. Filing Paul Meyer as the author of a
            // chanson de geste is wrong in a way a reader of this library would
            // see at once; "Anonymous" is true of the genre and of very nearly
            // every text in it. Girart de Vienne really is by Bertrand de
            // Bar-sur-Aube, and saying so is a per-text editorial judgement that
            // belongs in the Attribution dialog, where a reader can see it and
            // change it, rather than in an ingest service guessing from a field
            // already shown to be unreliable.
            null,
            WitnessFrom(fileStem),
            LanguageOf(root),
            fileStem.StartsWith("transcr_", StringComparison.Ordinal),
            ReadLines(root));
    }

    /// <summary>
    /// The witness sigil, taken from the filename because nothing else carries
    /// it reliably.
    ///
    /// Ten files are titled "Garin le Lorrain" and five "Otinel". Unlike ReM,
    /// whose titles carry the witness - "Rolandslied (P)" - Geste's do not, and
    /// msIdentifier is present on only 12 of the 32. Without this the library
    /// tree shows ten identical rows and the reader cannot tell which
    /// manuscript they are opening. The sigil is the filename's last
    /// underscore-separated part for a transcription (transcr_Otin_B -> B) and
    /// the trailing capitals of an edition's stem (ed_GarLorrMe1a -> Me1a).
    /// </summary>
    public static string? WitnessFrom(string fileStem)
    {
        var stem = fileStem;
        foreach (var suffix in new[] { "_pos", "_num" })
            if (stem.EndsWith(suffix, StringComparison.Ordinal)) stem = stem[..^suffix.Length];

        if (stem.StartsWith("transcr_", StringComparison.Ordinal))
        {
            var parts = stem.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? parts[^1] : null;
        }

        if (!stem.StartsWith("ed_", StringComparison.Ordinal)) return null;

        // Trailing sigil: the run of characters from the last capital that is
        // followed only by capitals, digits and lowercase run-on. GarLorrMe1a
        // -> Me1a, FloovG -> G, GuiBourgG -> G.
        var body = stem["ed_".Length..];
        for (var i = body.Length - 1; i > 0; i--)
        {
            if (!char.IsUpper(body[i])) continue;
            var candidate = body[i..];
            return candidate.Length <= 5 ? candidate : null;
        }

        return null;
    }

    /// <summary>
    /// The language, per file, which is the thing ReM did not need.
    ///
    /// Measured: fro 27 files, xno Anglo-Norman 7, wln Walloon 5, and one
    /// "fro-lorrain" which is not an ISO code. Several files declare more than
    /// one. Anglo-Norman and Walloon are separate ISO 639-3 languages, not
    /// dialects of French as far as a language column is concerned, so they are
    /// kept - a search scoped to Old French should not silently return
    /// Anglo-Norman. The regional qualifier is dropped to its base code,
    /// because "fro-lorrain" would sit in the language column as a value
    /// nothing else in the library uses or understands.
    /// </summary>
    internal static string LanguageOf(XElement root)
    {
        var declared = root.Descendants(Tei + "language")
            .Select(l => Clean((string?)l.Attribute("ident")))
            .FirstOrDefault(v => v != null);

        if (declared == null) return "fro";

        var dash = declared.IndexOf('-');
        return dash > 0 ? declared[..dash] : declared;
    }

    /// <summary>
    /// One passage per verse line.
    ///
    /// <b>Citations fall back to the ordinal far more often than the corpus's
    /// own description suggests.</b> 17 of the 34 files carry no @n on any
    /// &lt;l&gt; at all - it is the common case, not the exception - so the
    /// fallback is load-bearing rather than defensive.
    /// </summary>
    private static List<GesteLine> ReadLines(XElement root)
    {
        var lines = new List<GesteLine>();
        var body = root.Descendants(Tei + "body").FirstOrDefault();
        if (body == null) return lines;

        var order = 0;
        string? page = null;

        foreach (var element in body.Descendants())
        {
            var name = element.Name.LocalName;

            // Folio, tracked so a citation can name it where the line number
            // cannot stand alone.
            if (name == "pb")
            {
                page = Clean((string?)element.Attribute("n"));
                continue;
            }

            if (name != "l") continue;

            order++;

            var diplomatic = Reading(element, DiplomaticBranches);
            var normalised = Reading(element, NormalisedBranches);
            if (diplomatic.Length == 0 && normalised.Length == 0) continue;

            var number = Clean((string?)element.Attribute("n"));
            var citation = number ?? (page == null ? order.ToString() : $"{page}.{order}");

            lines.Add(new GesteLine(citation, order, diplomatic, normalised, WordsOf(element)));
        }

        return lines;
    }

    /// <summary>
    /// One reading of a line, taking the chosen branch of every
    /// &lt;choice&gt; and skipping the other.
    ///
    /// Written as a walk rather than as .Value because .Value concatenates both
    /// branches: the two DTD-bearing files expand &amp;d-oncial; into a choice
    /// pair, and reading transcr_GarLorr_X with .Value produces "dꝺevuañt" -
    /// every allograph twice, in one word.
    ///
    /// The whole subtree is walked, not the direct children. 45% of tokens are
    /// nested: 9,816 of 21,624 in ed_FloovG and 18,989 of 37,774 in
    /// ed_GuiBourgG sit inside persName, forename, num, hi or choice. ReM had
    /// the same trap at a fifth of its corpus and it cost a day to find, because
    /// a loader that drops nested tokens produces text that reads perfectly
    /// well and is missing half of itself.
    /// </summary>
    private static string Reading(XElement line, string[] branches)
    {
        var text = new StringBuilder();
        Walk(line, text, branches);
        return Collapse(text.ToString());
    }

    private static void Walk(XElement element, StringBuilder into, string[] branches)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText content:
                    into.Append(content.Value);
                    break;

                case XElement child when child.Name.LocalName == "choice":
                    // The branch this reading wants, or - if the choice offers
                    // neither - nothing, rather than both.
                    var chosen = branches
                        .Select(b => child.Element(Tei + b))
                        .FirstOrDefault(e => e != null);

                    if (chosen != null) Walk(chosen, into, branches);
                    break;

                case XElement child when child.Name.LocalName is "note" or "gap":
                    // An editor's note is not part of the line, and a gap is
                    // empty.
                    break;

                case XElement child:
                    Walk(child, into, branches);

                    // A word boundary the markup implies: <w> elements sit
                    // adjacent with no whitespace between them in several files.
                    if (child.Name.LocalName is "w" or "pc") into.Append(' ');
                    break;
            }
        }
    }

    /// <summary>
    /// The annotated words of a line.
    ///
    /// <b>Empty is not absent.</b> ed_OtinG_pos.xml carries lemma="" on all
    /// 15,908 of its tokens and no @pos or @msd at all. A non-null test would
    /// make the empty string the commonest headword in Old French, and Word
    /// Study would offer it with 15,908 attested forms behind it.
    /// </summary>
    private static List<GesteWord> WordsOf(XElement line)
    {
        var words = new List<GesteWord>();

        foreach (var w in line.Descendants(Tei + "w"))
        {
            var headword = Clean((string?)w.Attribute("lemma"));
            if (headword == null) continue;

            var form = Collapse(Reading(w, DiplomaticBranches));
            if (form.Length == 0) continue;

            var pos = Clean((string?)w.Attribute("pos")) ?? Clean((string?)w.Attribute("type"));
            var msd = Clean((string?)w.Attribute("msd"));

            words.Add(new GesteWord(form, headword, (pos, msd) switch
            {
                (not null, not null) => $"{pos} {msd}",
                (not null, null) => pos,
                (null, not null) => msd,
                _ => null
            }));
        }

        return words;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var collapsed = Collapse(value);
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
