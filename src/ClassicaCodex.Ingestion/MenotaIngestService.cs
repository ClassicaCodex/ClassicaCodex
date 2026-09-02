using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Ingests a folder of Menota (Medieval Nordic) manuscript XML.
///
/// The mapping onto the existing Author / Work / Edition model:
///
///   Author   the msItem author where the cataloguer gives one, else the
///            conventional attribution a person supplies in the plan, else
///            Anonymous
///   Work     an msItem - Gylfaginning, Skanske lov, Rigsthula
///   Edition  the manuscript witness to that work - "AM 242 fol"
///
/// A manuscript really is an edition in the sense this model already uses: it
/// is one witness to a work, exactly as perseus-grc2 is one edition of the
/// Agamemnon. Adding Codex Regius later gives a second edition of Gylfaginning
/// beside the first, which is what the edition-comparison machinery is for.
///
/// That last point is why the URNs here differ from the shape sketched in the
/// session handoff. urn:menota:AM-242-fol:gylfaginning puts the manuscript in
/// the *work* key, so Codex Regius would mint a second Work rather than a
/// second Edition, and the two witnesses could never be compared. The
/// manuscript belongs in the edition key only:
///
///   Author   urn:menota:snorri-sturluson
///   Work     urn:menota:snorri-sturluson:gylfaginning
///   Edition  urn:menota:snorri-sturluson:gylfaginning:am-242-fol
///
/// Nothing is ingested from a file without a confirmed .plan.json beside it.
/// See MenotaIngestPlan for why that step is not optional.
/// </summary>
public class MenotaIngestService
{
    private static readonly XNamespace Tei = MenotaXmlLoader.Tei;

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly ApparatusRepository _apparatusRepo = new();

    public const string Namespace = "menota";

    /// <summary>Files skipped, with the reason. Surfaced through IngestOutcome.</summary>
    public List<(string FilePath, string Error)> FailedFiles { get; } = new();

    /// <summary>Files that had no plan, and now have an unconfirmed one written.</summary>
    public List<string> PlansWritten { get; } = new();

    public async Task<IngestOutcome> IngestAsync(
        string folder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Menota folder not found: {folder}");

        var entities = MenotaXmlLoader.LoadEntities(folder);
        var planner = new MenotaIngestPlanner();
        var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories);

        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[i];
            progress?.Report($"{Path.GetFileName(path)} ({i + 1}/{files.Length})");

            try
            {
                await IngestFileAsync(path, entities, planner, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FailedFiles.Add((path, ex.Message));
            }
        }

        return IngestOutcome.From(FailedFiles);
    }

    private async Task IngestFileAsync(
        string path,
        IReadOnlyDictionary<string, string> entities,
        MenotaIngestPlanner planner,
        CancellationToken cancellationToken)
    {
        var load = MenotaXmlLoader.Load(path, entities);
        if (!load.Ok)
        {
            FailedFiles.Add((path, load.Error ?? "could not be parsed"));
            return;
        }

        var doc = load.Document!;
        var planPath = MenotaIngestPlan.PlanPathFor(path);
        var plan = MenotaIngestPlan.Load(planPath);

        // A plan whose titles carry a replacement character was made before
        // menota-entities.txt was in the folder, and is not to be believed
        // however confirmed it says it is. U+FFFD is not a character any
        // manuscript is written in - it is what this application substitutes
        // for an entity it could not resolve - so its presence in a title is
        // proof of when the plan was made rather than of what the manuscript
        // says.
        //
        // Refused rather than silently regenerated: the plan also carries
        // merges, splits and renames somebody decided by hand, and throwing
        // those away without asking would cost more than the titles are worth.
        // Deleting the file is one action and is named here.
        if (plan != null && plan.Works.Any(w => w.Title.Contains('�')))
        {
            FailedFiles.Add((path,
                $"{Path.GetFileName(planPath)} was written before menota-entities.txt was saved, so its " +
                "titles lost their thorns and accented vowels. Delete that file and import again to " +
                "rebuild it - any merges or renames in it will need making again."));
            return;
        }

        if (plan == null)
        {
            planner.Plan(path, doc).Save(planPath);
            PlansWritten.Add(planPath);
            FailedFiles.Add((path,
                $"no ingest plan. One has been written to {Path.GetFileName(planPath)} - " +
                "check the titles and authors, set Confirmed to true, and run again."));
            return;
        }

        if (!plan.Confirmed)
        {
            FailedFiles.Add((path,
                $"{Path.GetFileName(planPath)} is not confirmed. Nothing ingested from this file."));
            return;
        }

        var body = doc.Descendants(Tei + "body").FirstOrDefault();
        if (body == null)
        {
            FailedFiles.Add((path, "no <body> element"));
            return;
        }

        var msSlug = MenotaIngestPlanner.Slug(plan.ManuscriptId);

        // What this file put in the library last time, so that anything the
        // confirmed plan no longer produces can be taken out again.
        var previous = await _editionRepo.GetBySourcePathAsync(path, cancellationToken);
        var minted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var punctuationIsWordDivider = PunctuationIsWordDivider(doc);
        var scribalHeadings = MenotaXmlLoader.HeadingsUseWordMarkup(doc);

        foreach (var work in plan.Works.Where(w => w.Include))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var divs = work.DivPaths
                .Select(p => ResolvePath(body, p))
                .Where(d => d != null)
                .Cast<XElement>()
                .ToList();

            if (divs.Count == 0)
            {
                FailedFiles.Add((path, $"\"{work.Title}\": none of its DivPaths resolve. Delete the plan to regenerate."));
                continue;
            }

            // The plan points at divs by index path, because Menota gives
            // nothing else to point at. Re-downloading a corrected file from
            // menota.org can move those indices silently, so the word count
            // recorded when the plan was written is checked against the file
            // now. A plan that has drifted is refused rather than ingested
            // against whatever happens to sit at that index today.
            var actualWords = divs.Sum(d => MenotaXmlLoader.DirectWords(d).Count());
            if (work.WordCount > 0 && actualWords != work.WordCount)
            {
                FailedFiles.Add((path,
                    $"\"{work.Title}\": plan expects {work.WordCount:N0} words at those divs, file has " +
                    $"{actualWords:N0}. The file has changed since the plan was written. Delete the plan to regenerate."));
                continue;
            }

            var authorName = string.IsNullOrWhiteSpace(work.Author) ? "Anonymous" : work.Author.Trim();
            var authorSlug = MenotaIngestPlanner.Slug(authorName);
            var workSlug = string.IsNullOrWhiteSpace(work.UrnSlug)
                ? MenotaIngestPlanner.Slug(work.Title)
                : work.UrnSlug;

            var authorId = await _authorRepo.UpsertAsync(new Author
            {
                CtsUrn = $"urn:menota:{authorSlug}",
                Name = authorName,
                Namespace = Namespace,
                Language = plan.Language
            }, cancellationToken);

            var apparatus = new List<ApparatusEntry>();

            var nodes = ExtractNodes(
                divs, plan.ReadingLevel, punctuationIsWordDivider, scribalHeadings, work.Title,
                apparatus, out var usesVerseLines);

            var workId = await _workRepo.UpsertAsync(new Work
            {
                AuthorId = authorId,
                CtsUrn = $"urn:menota:{authorSlug}:{workSlug}",
                Title = work.Title,
                CitationScheme = usesVerseLines ? "Section.Line" : "Folio.Line"
            }, cancellationToken);

            var editionId = await _editionRepo.UpsertAsync(new Edition
            {
                WorkId = workId,
                CtsUrn = $"urn:menota:{authorSlug}:{workSlug}:{msSlug}",
                Kind = EditionKind.Original,
                Language = plan.Language,
                Translator = null,
                SourcePath = path,
                Orthography = plan.Orthography
            }, cancellationToken);

            minted.Add($"urn:menota:{authorSlug}:{workSlug}:{msSlug}");

            await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

            await _textNodeRepo.BulkInsertAsync(
                nodes.Select(n => new TextNode
                {
                    EditionId = editionId,
                    CitationRef = n.CitationRef,
                    SortOrder = n.SortOrder,
                    Text = n.Text,
                    // Menota <del> is scribal deletion, not athetesis - see
                    // WordText - so no line here is ever flagged.
                    IsAthetized = false
                }).ToList(),
                cancellationToken);

            foreach (var entry in apparatus) entry.EditionId = editionId;
            await _apparatusRepo.ReplaceForEditionAsync(editionId, apparatus, cancellationToken);

            ApparatusEntries += apparatus.Count;
        }

        // Editions this file produced before and does not produce now.
        //
        // Merging thirty-five chapters into one work mints one URN where there
        // were thirty-five; without this the old thirty-five remain, and the
        // library shows both the merged work and every part of it. Re-importing
        // could not fix it and deleting the database was the only way out.
        //
        // Scoped to this manuscript's own SourcePath, so nothing another file
        // or another corpus put there is at risk.
        foreach (var stale in previous.Where(e => !minted.Contains(e.CtsUrn)))
        {
            await _editionRepo.DeleteEditionAsync(stale.EditionId, cancellationToken);
            RemovedEditions++;
        }
    }

    /// <summary>
    /// How many editions were removed because the confirmed plan no longer
    /// produces them. Reported so a re-import that quietly halves the library
    /// says so.
    /// </summary>
    public int RemovedEditions { get; private set; }

    /// <summary>Editorial notes captured as apparatus rather than read as text.</summary>
    public int ApparatusEntries { get; private set; }

    /// <summary>
    /// The running position within a work, passed between the line builders.
    ///
    /// A plain "ref int" cannot be captured by the local Flush() inside
    /// AddManuscriptLines, and threading the value back out of three methods
    /// by return is more moving parts than one small object.
    /// </summary>
    private sealed class Counter
    {
        private int _value;

        public int Next() => _value++;

        /// <summary>
        /// The last page break seen, and the line reached on it.
        ///
        /// Held for the whole work rather than per division, because a division
        /// that opens with no page break of its own opens on whatever page the
        /// previous one ended on - the scribe did not start a new leaf because
        /// the editor started a new chapter. Reset per division, twelve of AM
        /// 619 4to's 110 divisions contain no &lt;pb&gt; at all and cited
        /// themselves as "137.30" - division 137, line 30, in a manuscript of
        /// 113 leaves, sitting in a list where every neighbour is a folio. A
        /// reference that looks like one you can follow into the manuscript and
        /// is not is worse than an honest sequential one.
        ///
        /// The line number carries with it for the same reason. Restarting it
        /// mid-page made a second "67r.6" behind the real one, which Unique()
        /// then disambiguated to "67r.6a" - a letter that reads as the
        /// editor's a/b distinction and was only ever a collision.
        /// </summary>
        public string Folio { get; set; } = "";

        public int LineNumber { get; set; }

        /// <summary>
        /// Makes a citation reference unique within the work.
        ///
        /// Folio numbers are not always distinct: AM 63 fol has two page
        /// breaks both numbered 3, so two different lines would both be cited
        /// "3.5" - and an apparatus entry keyed by citation would then attach
        /// to both. The first keeps the plain reference and later ones get a
        /// letter, the way an editor distinguishes 3a from 3b.
        /// </summary>
        /// <summary>
        /// Shared with the TEI path. Both corpora collide for different
        /// reasons - repeated folio numbers here, sparse @n numbering there -
        /// and both want the same answer, so the letter scheme lives in one
        /// place rather than two.
        /// </summary>
        private readonly CitationDisambiguator _disambiguator = new();

        public string Unique(string reference) => _disambiguator.Unique(reference);

    }

    private sealed record ParsedLine(string CitationRef, int SortOrder, string Text);

    /// <summary>
    /// Whether this manuscript's punctuation marks are word dividers rather
    /// than punctuation.
    ///
    /// Codex Runicus separates its words with a two-dot mark, encoded as
    /// &lt;pc type="runic"&gt;: 16,898 of them against 16,947 words, one after
    /// nearly every word. Appended to the preceding word the way real
    /// punctuation should be, every token in the manuscript became "Thaet:"
    /// rather than "Thaet", which is invisible in the reading pane and ruins
    /// search, concordance and lemma lookup for the whole text.
    ///
    /// Decided by density rather than by @type, because the ratio is what the
    /// claim actually rests on and it holds for any scribe with the same habit
    /// under any label. AM 28 runs at 0.997 marks per word, AM 242 at 0.07,
    /// AM 619 at 0.11.
    ///
    /// Marks inside a &lt;w&gt; are excluded, because LineText does not emit
    /// them - they belong to the word's own reading. Counting a population
    /// other than the one the decision governs cannot help, though no
    /// manuscript here is near the threshold either way: Holm perg 4 fol has
    /// 1,565 of them and moves from 0.101 to 0.088.
    /// </summary>
    public static bool PunctuationIsWordDivider(XDocument doc)
    {
        var words = doc.Descendants(Tei + "w").Count(w => !MenotaXmlLoader.IsEditorial(w));
        if (words == 0) return false;

        var marks = doc.Descendants(Tei + "pc")
                        .Count(m => !MenotaXmlLoader.IsEditorial(m)
                                    && !m.Ancestors(Tei + "w").Any())
                    + doc.Descendants(MenotaXmlLoader.Me + "punct")
                        .Count(m => !MenotaXmlLoader.IsEditorial(m)
                                    && !m.Ancestors(Tei + "w").Any());

        return (double)marks / words > 0.5;
    }

    /// <summary>
    /// Letters and digits only, for comparing a heading with a title that may
    /// have been retyped in the review with different spacing or punctuation.
    /// </summary>
    private static string Normalise(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Turns a work's divisions into citable lines.
    ///
    /// The unit is decided per division, not per manuscript, because a saga in
    /// prose with a few stanzas quoted in it is both kinds of text at once.
    /// Where verse lines outnumber manuscript lines the division is verse and
    /// &lt;l&gt; is the unit; otherwise the manuscript's own ruled lines are,
    /// marked by &lt;lb&gt;.
    ///
    /// &lt;p&gt; is almost never the right unit here and was the one being
    /// used. Holm D 4 encodes 197,498 words in fifteen divisions and
    /// twenty-two paragraphs, so De lucidario arrived as a single text node of
    /// 21,869 words with all 1,037 of its editorial notes stranded on line 1.
    /// Laxdoela saga was twenty-eight nodes averaging 2,210 words. At
    /// manuscript-line granularity those become 30,265 and 11,124 lines of
    /// five or six words - which is what the scribe wrote, what Menota's own
    /// citations use, and what an apparatus entry can actually point at.
    /// </summary>
    private static List<ParsedLine> ExtractNodes(
        List<XElement> divs, string level, bool punctuationIsWordDivider, bool scribalHeadings,
        string workTitle, List<ApparatusEntry> apparatus, out bool usesVerseLines)
    {
        var nodes = new List<ParsedLine>();
        var sort = new Counter();
        usesVerseLines = false;

        for (var d = 0; d < divs.Count; d++)
        {
            var div = divs[d];

            var section = div.Attribute("n")?.Value;
            if (string.IsNullOrWhiteSpace(section)) section = (d + 1).ToString();

            var heads = div.Elements(Tei + "head").ToList();
            var head = heads
                .FirstOrDefault(h => !scribalHeadings || h.Descendants(Tei + "w").Any());

            if (heads.Count > 0)
            {
                var headText = head == null
                    ? ""
                    : MenotaXmlLoader.WordsText(head, level, !punctuationIsWordDivider);

                var duplicatesTitle = string.Equals(
                    Normalise(headText), Normalise(workTitle), StringComparison.OrdinalIgnoreCase);

                // Through Unique() for the same reason the line references
                // are. AM 63 fol's Heimskringla 3 is nine sagas, each
                // restarting its chapter numbering at 1, so nine different
                // chapter headings all cited themselves "1.h" - and eight
                // apparatus entries from eight different sagas arrived at
                // one reference looking like duplicates of each other.
                var headRef = sort.Unique($"{section}.h");

                if (headText.Length > 0 && !duplicatesTitle)
                    nodes.Add(new ParsedLine(headRef, sort.Next(), headText));

                // Collected whether or not the heading became a node of its
                // own, and from every heading rather than the one chosen to be
                // read.
                //
                // A heading is suppressed when it repeats the work's title -
                // which for Menota is most of them, because the title was taken
                // from the heading in the first place - and its apparatus was
                // being dropped with it. The line walk skips headings on the
                // way past, so nothing downstream picked them up: 12 of AM 619
                // 4to's 410 notes vanished between the file and the library
                // without appearing in any count.
                foreach (var h in heads)
                    CollectApparatus(h, level, headRef, apparatus);
            }

            var verseLines = div.Descendants(Tei + "l")
                .Count(c => MenotaXmlLoader.BelongsTo(c, div));

            var manuscriptLines = div.Descendants(Tei + "lb")
                .Count(c => MenotaXmlLoader.BelongsTo(c, div));

            if (verseLines > manuscriptLines)
            {
                usesVerseLines = true;
                AddVerseLines(div, section, level, punctuationIsWordDivider, apparatus, nodes, sort);
            }
            else if (manuscriptLines > 0)
            {
                AddManuscriptLines(div, section, level, punctuationIsWordDivider, apparatus, nodes, sort);
            }
            else
            {
                AddParagraphs(div, section, level, punctuationIsWordDivider, apparatus, nodes, sort);
            }
        }

        return nodes;
    }

    private static void AddVerseLines(
        XElement div, string section, string level, bool punctuationIsWordDivider,
        List<ApparatusEntry> apparatus, List<ParsedLine> nodes, Counter sort)
    {
        var containers = div.Descendants(Tei + "l")
            .Where(c => MenotaXmlLoader.BelongsTo(c, div)).ToList();

        for (var i = 0; i < containers.Count; i++)
        {
            var text = LineText(containers[i], level, punctuationIsWordDivider);
            if (text.Length == 0) continue;

            var lineRef = containers[i].Attribute("n")?.Value;
            if (string.IsNullOrWhiteSpace(lineRef)) lineRef = (i + 1).ToString();

            var citation = $"{section}.{lineRef}";
            nodes.Add(new ParsedLine(citation, sort.Next(), text));
            CollectApparatus(containers[i], level, citation, apparatus);
        }
    }

    private static void AddParagraphs(
        XElement div, string section, string level, bool punctuationIsWordDivider,
        List<ApparatusEntry> apparatus, List<ParsedLine> nodes, Counter sort)
    {
        var containers = div.Descendants(Tei + "p")
            .Where(c => MenotaXmlLoader.BelongsTo(c, div)).ToList();

        if (containers.Count == 0) containers = new List<XElement> { div };

        for (var i = 0; i < containers.Count; i++)
        {
            var text = LineText(containers[i], level, punctuationIsWordDivider);
            if (text.Length == 0) continue;

            var citation = $"{section}.{i + 1}";
            nodes.Add(new ParsedLine(citation, sort.Next(), text));
            CollectApparatus(containers[i], level, citation, apparatus);
        }
    }

    /// <summary>
    /// Walks a division once, cutting a new line at every &lt;lb&gt; and
    /// citing by folio where &lt;pb&gt; gives one - "12r.14" rather than a
    /// running count that means nothing outside this database.
    ///
    /// Notes are attached to whichever line was open when they appeared, which
    /// is where the editor wrote them - or, where that line holds no text, to
    /// the next one that does.
    /// </summary>
    private static void AddManuscriptLines(
        XElement div, string section, string level, bool punctuationIsWordDivider,
        List<ApparatusEntry> apparatus, List<ParsedLine> nodes, Counter sort)
    {
        var buffer = new StringBuilder();
        var pending = new List<XElement>();

        var citation = "";
        var lastReference = "";
        var lastOrder = 0;

        void Flush()
        {
            var text = MenotaXmlLoader.Collapse(buffer.ToString());
            buffer.Clear();

            // Held for the next line that has text, rather than dropped.
            //
            // Clearing here discarded exactly the notes worth keeping. A note
            // about an absence sits where there is no text to sit on: "A leaf
            // is missing between fols. 62v and 63r", "The preserved text
            // continues here", "This is the first leaf in the inserted quire
            // 69r-72v", "The rest of the line is blank". Nine of AM 619 4to's
            // notes went this way, and they were the lacunae, the inserted
            // quire and the worn page - the structural apparatus of the
            // manuscript, selected for by the very rule that threw it away.
            if (text.Length == 0) return;

            var reference = sort.Unique(citation.Length > 0 ? citation : $"{section}.{sort.LineNumber}");
            nodes.Add(new ParsedLine(reference, sort.Next(), text));
            lastReference = reference;

            var order = 0;
            foreach (var note in pending) AddApparatus(note, level, reference, order++, apparatus);
            pending.Clear();
            lastOrder = order;
        }

        foreach (var el in div.Descendants())
        {
            // Content belonging to a nested division is that division's to
            // read, not this one's.
            //
            // A division that holds prose of its own as well as chapters is a
            // division in the plan - it has to be, or its prose is lost - and
            // walking all of its descendants made it read its chapters over
            // again on top of them. AM 63 fol has one: Ólaáfs saga kyrra, four
            // words of its own above eight chapters, which re-read every line
            // and every note in those chapters. 46 apparatus entries beyond
            // what the file contains, and the saga's text twice over.
            if (!MenotaXmlLoader.BelongsTo(el, div)) continue;

            // The heading is already a node of its own. Menota puts <lb>
            // inside headings - AM 63 fol has one inside a heading's editorial
            // note - so walking into it emitted "Uphaf Magnús konongs goða"
            // as the heading and then again, split across two manuscript
            // lines, immediately below.
            if (el.Ancestors(Tei + "head").Any() || el.Name == Tei + "head") continue;

            if (el.Name == Tei + "pb")
            {
                // Menota marks the pages of printed editions with <pb> as well
                // as the manuscript's own leaves, telling them apart by @ed.
                // AM 619 4to carries 164 of its own and 170 of Indrebø's 1931
                // edition, interleaved; Holm perg 4 fol carries 258 of its own
                // against 665 of Bertelsen's. Taken indiscriminately they gave
                // citations like "161.11" - page 161 of a book published in
                // 1931 - sitting in a list beside "69r.2", a leaf of the
                // manuscript, in the same shape and with nothing to tell them
                // apart.
                //
                // A missing @ed is the manuscript's own: AM 28 8vo marks all
                // 270 of its pages that way and means its own leaves.
                var edition = (string?)el.Attribute("ed");
                if (!string.IsNullOrWhiteSpace(edition)
                    && !string.Equals(edition, "ms", StringComparison.OrdinalIgnoreCase))
                    continue;

                var next = el.Attribute("n")?.Value ?? sort.Folio;

                // Counting lines from the top of each page, not from the top
                // of the division.
                //
                // AM 63 fol's 14,156 line breaks carry no number of their own,
                // so the number has to be counted here - and counted across a
                // whole division it produced references like "2.34", which
                // reads as folio 2 line 34 and actually meant the thirty-fourth
                // line of a chapter that happens to reach folio 2. A citation
                // nobody can follow back into the manuscript is worse than a
                // sequential one, because it looks like it can be.
                if (!string.Equals(next, sort.Folio, StringComparison.Ordinal)) sort.LineNumber = 0;

                sort.Folio = next;
                continue;
            }

            if (el.Name == Tei + "lb")
            {
                Flush();

                sort.LineNumber++;
                var n = el.Attribute("n")?.Value;
                var line = string.IsNullOrWhiteSpace(n) ? sort.LineNumber.ToString() : n;
                citation = sort.Folio.Length > 0 ? $"{sort.Folio}.{line}" : $"{section}.{line}";
                continue;
            }

            if (el.Name == Tei + "note")
            {
                // Held rather than read: the note belongs to the line, not in
                // it. See CollectApparatus.
                if (IsApparatus(el)) pending.Add(el);
                continue;
            }

            if (MenotaXmlLoader.IsEditorial(el)) continue;

            if (el.Name == Tei + "w")
            {
                var word = WordText(el, level);
                if (word.Length == 0) continue;
                if (buffer.Length > 0) buffer.Append(' ');
                buffer.Append(word);
            }
            else if (el.Name == Tei + "pc" || el.Name == MenotaXmlLoader.Me + "punct")
            {
                if (punctuationIsWordDivider) continue;

                // A mark INSIDE a <w> is part of that word's reading and was
                // already taken with it - this is a flat Descendants() walk, so
                // the word and the marks within it are both visited.
                //
                // Holm perg 4 fol writes its Roman numerals
                // <w><choice><me:dipl><pc>.</pc>íí<pc>.</pc></me:dipl>…</choice></w>,
                // and every one of those periods was emitted twice: once as
                // part of ".íí." and again on its own. 1,565 in that manuscript
                // and 40 in AM 619 4to.
                //
                // Skipping rather than appending is right even where the chosen
                // level does NOT contain the mark, because Append puts it at
                // the end of the buffer rather than at its place inside the
                // word - a mark the normalised reading omits would land after
                // the word instead of inside it, which is a guess the source
                // does not support.
                if (el.Ancestors(Tei + "w").Any()) continue;

                var mark = WordText(el, level);
                if (mark.Length > 0) buffer.Append(mark);
            }
        }

        Flush();

        // Notes that came after the division's last line of text. Nothing
        // follows them to carry forward to, so they go on the line that does
        // precede them - a note about a blank line at the foot of a chapter
        // belongs to that chapter, not to nothing.
        if (pending.Count > 0 && lastReference.Length > 0)
        {
            // Continuing the last line's numbering rather than restarting at
            // zero, or a note about the blank foot of a chapter sorts ahead of
            // the notes on the line it follows.
            var order = lastOrder;
            foreach (var note in pending)
                AddApparatus(note, level, lastReference, order++, apparatus);
        }
    }

    /// <summary>
    /// Turns the editorial notes on a line into apparatus entries.
    ///
    /// Menota's notes are real apparatus with real witnesses - "riodanda :
    /// riothandi (Ms. AM 18 fol.)" - which is more than the Perseus corpus
    /// carries: a census there found 371,601 commentary notes against five
    /// structured variants, only one of them genuine. So Lemma and Witness,
    /// documented on ApparatusEntry as effectively dead columns, finally have
    /// something to hold.
    ///
    /// Parsed conservatively. The witness is taken only from a trailing
    /// parenthesis, and the lemma only when the note is exactly two readings
    /// separated by a colon; anything else is stored whole as a note. Guessing
    /// harder would mean inferring one editor's punctuation habits and being
    /// wrong without any sign of it.
    /// </summary>
    /// <summary>
    /// A siglum at the end of a note - "(Ms. AM 18 fol.)".
    /// </summary>
    private static readonly Regex TrailingParenthesis =
        new(@"\(([^)]+)\)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// A colon standing as a separator between two readings, rather than one
    /// inside a word or a reference: at the start of the note, or with space
    /// around it. Menota's editors write "Uphaf : Vphaf Sogo" and ": Ferth
    /// Magnusar konongs" - a spaced colon and a leading one - while the stray
    /// colons in prose notes are "d:36" and "s. 106:1", tight against their
    /// neighbours.
    /// </summary>
    private static readonly Regex SeparatorColon =
        new(@"(?:^|\s):(?:\s|$)", RegexOptions.Compiled);

    /// <summary>
    /// Whether a note is apparatus at all.
    ///
    /// &lt;note type="location"&gt; is not. Möðruvallabók puts one inside every
    /// single &lt;w&gt;, holding the word's position in the manuscript -
    /// 114ra410 for folio 114r, column a, line 41, word 0. There are 11,275 in
    /// Bandamanna saga and 61,897 in Laxdœla, one per word, and every one of
    /// them was being stored as an editor's note and shown in the apparatus
    /// pane against its line. 73,172 entries, none of them apparatus.
    ///
    /// This is the same shape of mistake as the inline apparatus read as text,
    /// running the other way: markup that carries a coordinate rather than a
    /// comment, in an element whose name says comment.
    ///
    /// Public, with AddApparatus below, so the tests can exercise the rules
    /// directly rather than through a whole ingest - the same reason
    /// CountWords and ResolvePath are.
    /// </summary>
    public static bool IsApparatus(XElement note) =>
        (string?)note.Attribute("type") != "location";

    private static void CollectApparatus(
        XElement container, string level, string citationRef, List<ApparatusEntry> apparatus)
    {
        var order = 0;

        // As in LineText: a division collects its own notes, not its
        // chapters'.
        var scope = container.Name == Tei + "div" ? container : null;

        foreach (var note in container.Descendants(Tei + "note").Where(IsApparatus))
        {
            if (scope != null && !MenotaXmlLoader.BelongsTo(note, scope)) continue;

            // The heading's notes were collected with the heading. See
            // ExtractNodes.
            if (scope != null && note.Ancestors(Tei + "head").Any()) continue;

            AddApparatus(note, level, citationRef, order++, apparatus);
        }
    }

    /// <summary>
    /// Classifies one note.
    ///
    /// A variant is a note carrying word-marked readings with a separator colon
    /// between them. Both halves of that test are load-bearing, and each was
    /// arrived at by running it over all ten manuscripts:
    ///
    /// - Word markup alone is not enough. Holm D 4 and AM 619 write prose notes
    ///   with colons in them - "Ny text: Ivan Lejonriddaren", "The latin
    ///   original: martyrium" - where the colon introduces a label, not a
    ///   reading. 379 of them.
    /// - The colon alone is not enough either, and counting readings is not the
    ///   test it looks like. The previous rule - exactly two readings and a
    ///   colon anywhere - classified 2,883 of AM 63 fol's 4,157 variants and
    ///   filed the other 1,274 as prose, because a variant may be one word
    ///   against three ("Uphaf : Vphaf Sogo") or a whole clause against
    ///   another.
    ///
    /// Together they classify 4,157 of AM 63 fol and nothing else anywhere in
    /// the corpus - no note misfiled as a variant, no variant misfiled as a
    /// note.
    /// </summary>
    public static void AddApparatus(
        XElement note, string level, string citationRef, int order, List<ApparatusEntry> apparatus)
    {
        var content = NoteText(note, level);
        if (content.Length == 0) return;

        var hasReadings = note.Descendants(Tei + "w")
            .Any(w => MenotaXmlLoader.WordsText(w, level).Length > 0);

        // The siglum comes off before the colon test, or a parenthesis
        // containing one - "utg. s. 106:1 läser..." - would make a variant of a
        // prose note.
        var siglum = TrailingParenthesis.Match(content);
        var body = siglum.Success ? content[..siglum.Index].TrimEnd() : content;

        var variant = (string?)note.Attribute("type") == "variant"
                      || (hasReadings && SeparatorColon.IsMatch(body));

        // @resp is the editor the file itself names - AM 619's GI1931, Holm
        // perg 4's Bertelsen190511 - and is what the Perseus path already reads
        // into this column.
        var resp = (string?)note.Attribute("resp");

        string? witness = null;
        if (!string.IsNullOrWhiteSpace(resp)) witness = resp.Trim();
        else if (variant && siglum.Success) witness = siglum.Groups[1].Value.Trim();

        // A witness lifted out of the prose is removed from it. Left in, it
        // shows twice in the apparatus pane - once from the field and once at
        // the end of the note it was read from. Only when it was lifted: the
        // parentheses in Holm D 4's notes are the editor talking ("adv",
        // "kanske pga en lagning..."), and belong to the sentence.
        var text = variant && siglum.Success && body.Length > 0 ? body : content;

        string? lemma = null;
        if (variant)
        {
            var colon = SeparatorColon.Match(body);
            if (colon.Success)
            {
                var left = body[..colon.Index].Trim();

                // Nothing before the colon means the other manuscript has text
                // this one lacks. There is no adopted reading to record, and
                // taking the first word after the colon - which counting
                // readings would do - would file the addition itself as the
                // lemma. 272 of these in AM 63 fol.
                if (left.Length > 0) lemma = left;
            }
        }

        apparatus.Add(new ApparatusEntry
        {
            CitationRef = citationRef,
            SortOrder = order,
            Kind = variant ? "variant" : "note",
            Lemma = lemma,
            Witness = witness,
            Content = text
        });
    }

    /// <summary>
    /// A note's text: its words at the reading level, its plain text as
    /// written. Both matter - the readings are the variants and the plain text
    /// is the separator and the siglum that say what they mean.
    /// </summary>
    private static string NoteText(XElement note, string level)
    {
        var sb = new StringBuilder();

        foreach (var node in note.DescendantNodes())
        {
            if (node is XElement el && el.Name == Tei + "w")
            {
                sb.Append(' ').Append(MenotaXmlLoader.WordsText(el, level));
            }
            else if (node is XText text && text.Parent?.Name != Tei + "w"
                     && !text.Ancestors(Tei + "w").Any())
            {
                sb.Append(text.Value);
            }
        }

        return MenotaXmlLoader.Collapse(sb.ToString());
    }

    public static string LineText(XElement container, string level, bool punctuationIsWordDivider)
    {
        var sb = new StringBuilder();

        // When the container is a division rather than a paragraph or a verse
        // line, its nested divisions read themselves. See AddManuscriptLines.
        var scope = container.Name == Tei + "div" ? container : null;

        foreach (var el in container.Descendants())
        {
            if (scope != null && !MenotaXmlLoader.BelongsTo(el, scope)) continue;

            // The heading is already a node of its own. See ExtractNodes.
            if (scope != null && (el.Name == Tei + "head" || el.Ancestors(Tei + "head").Any()))
                continue;

            // A variant reading collated from another manuscript is not a word
            // of this one. See MenotaXmlLoader.IsEditorial.
            if (MenotaXmlLoader.IsEditorial(el)) continue;

            if (el.Name == Tei + "w")
            {
                var word = WordText(el, level);
                if (word.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(word);
            }
            else if (el.Name == Tei + "pc" || el.Name == MenotaXmlLoader.Me + "punct")
            {
                // A word divider has already done its job by the time the
                // words are separated by spaces, so it is dropped. Real
                // punctuation joins the preceding word instead of standing
                // alone, so that splitting the stored line on whitespace
                // yields words rather than a stream of full stops.
                if (punctuationIsWordDivider) continue;

                // Already carried by the word it sits in - this walk visits the
                // <w> and its contents both. See the same guard in LineText.
                if (el.Ancestors(Tei + "w").Any()) continue;

                var mark = WordText(el, level);
                if (mark.Length > 0) sb.Append(mark);
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// One word at the chosen orthographic level.
    ///
    /// &lt;del&gt; is dropped. This is the reverse of the rule that applies to
    /// the printed critical editions in the Perseus corpus, where &lt;del&gt;
    /// marks a line the editor athetized and the text is still the text. In a
    /// manuscript transcription &lt;del&gt; means the scribe struck it out, and
    /// it is not part of the text being read. AM 242 has 68, AM 28 has 485.
    /// The element is the same; the convention is not.
    /// </summary>
    private static string WordText(XElement word, string level)
    {
        if (word.Ancestors(Tei + "del").Any()) return string.Empty;

        // LevelElement, not a raw Descendants().FirstOrDefault(): an empty
        // <me:norm/> placeholder is not a normalised reading, and stopping at
        // one here is what emptied Holm perg 4 fol.
        var target = MenotaXmlLoader.LevelElement(word, level)
                     ?? MenotaXmlLoader.LevelElement(word, "dipl")
                     ?? MenotaXmlLoader.LevelElement(word, "facs");

        if (target == null)
            return MenotaXmlLoader.Collapse(word.Value);

        return MenotaXmlLoader.Collapse(TextExcludingDeletions(target));
    }

    /// <summary>
    /// An element's text with every &lt;del&gt; subtree left out, at any depth.
    ///
    /// Everything else comes through, which is the point: &lt;ex&gt;, the
    /// editorial expansion of an abbreviation, is part of the word and AM 242
    /// has 23,211 of them; &lt;c&gt; wrapping a decorated initial is part of
    /// the word too.
    ///
    /// This used to test target.Elements(Tei + "del"), which sees direct
    /// children only, so a deletion nested one level down - a &lt;del&gt;
    /// inside a &lt;c&gt; inside the orthographic level - was invisible to the
    /// test and .Value then pulled the deleted letters into the word. Even
    /// when a direct &lt;del&gt; did trigger the filter, .Value on a sibling
    /// could still carry a nested one through. Whether the Menota corpus
    /// contains that shape is unverified - a recursive walk costs nothing and
    /// removes the question.
    ///
    /// It also fixes a quieter fault in the same expression: the old filter
    /// called ToString() on text nodes, and XText.ToString() serializes rather
    /// than returning the text, so a word containing an ampersand came out as
    /// "&amp;amp;". Reading .Value gives the characters themselves.
    ///
    /// A word wholly inside a &lt;del&gt; never reaches here - WordText
    /// returns empty for those before looking at the level at all.
    /// </summary>
    private static string TextExcludingDeletions(XElement element)
    {
        var text = new StringBuilder();

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText value:
                    text.Append(value.Value);
                    break;
                case XElement child when child.Name != Tei + "del":
                    text.Append(TextExcludingDeletions(child));
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Counts the words a work's divisions actually hold, so the review
    /// dialog can restate it after a merge or split instead of showing a
    /// figure inherited from the proposal.
    /// </summary>
    public static int CountWords(XElement body, MenotaWorkPlan work) =>
        work.DivPaths
            .Select(p => ResolvePath(body, p))
            .Where(d => d != null)
            .Sum(d => MenotaXmlLoader.DirectWords(d!).Count());

    public static XElement? ResolvePath(XElement body, string path)
    {
        var node = body;

        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var index)) return null;
            var children = node.Elements(Tei + "div").ToList();
            if (index < 0 || index >= children.Count) return null;
            node = children[index];
        }

        return node == body ? null : node;
    }
}
