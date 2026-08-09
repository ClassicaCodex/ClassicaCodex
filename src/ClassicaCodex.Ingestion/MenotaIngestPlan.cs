using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// What a manuscript should be broken into, written to disk as JSON beside the
/// XML and confirmed by a person before anything is ingested.
///
/// This exists because the link cannot be derived reliably. A Menota file is a
/// manuscript containing several works; msContents lists them; and in the three
/// manuscripts tested there is no attribute anywhere connecting an msItem to
/// the body div that holds its text. AM 242 fol has two xml:id in the whole
/// file, both on handNote. AM 28 8vo has none. AM 619 4to has 3,614, all on
/// s, note, anchor, name and seg. Its single corresp is corresp="yes" on a
/// bibl - a boolean where a pointer belongs.
///
/// Matching the msItem title against the div's head does not close the gap
/// either. It works for AM 242's Norse-titled works (GYLFAGINNING matches
/// Gylfaginning) and fails for the rest, because Menota's msItem titles are
/// English editorial titles - "First grammatical treatise" - while the heads
/// are Swedish or Old Norwegian. AM 619 has 44 msItems against 117 heads that
/// are Old Norwegian incipits.
///
/// So the planner proposes, and a person confirms. The alternative is
/// silently mis-assigning works to authors, which produces a library that
/// looks right and is not - and once the text is in the database, nothing
/// downstream can tell.
/// </summary>
public sealed class MenotaIngestPlan
{
    public string FileName { get; set; } = string.Empty;

    /// <summary>From msIdentifier/idno - "AM 242 fol".</summary>
    public string ManuscriptId { get; set; } = string.Empty;

    /// <summary>ISO code from teiHeader language/@ident - isl, nor, dan.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>norm, dipl or facs. See MenotaXmlLoader.ChooseReadingLevel.</summary>
    public string ReadingLevel { get; set; } = "dipl";

    /// <summary>
    /// "normalised" or "diplomatic", written to Editions.Orthography.
    ///
    /// This is the column that keeps these texts out of the stylometry pool.
    /// A diplomatic transcription follows each scribe's own spelling rather
    /// than a dictionary, so comparing two of them by word frequency measures
    /// the scribes. None of the three manuscripts tested carries a normalised
    /// level: zero me:norm across roughly 140,000 words.
    /// </summary>
    public string Orthography { get; set; } = "diplomatic";

    /// <summary>
    /// Nothing is ingested until this is true. The planner never sets it.
    /// </summary>
    public bool Confirmed { get; set; }

    public List<MenotaWorkPlan> Works { get; set; } = new();

    /// <summary>
    /// Every author msContents names, whether or not any division matched the
    /// item naming them. Kept so the review can offer the name rather than
    /// make the reviewer read it out of the notes and retype it.
    /// </summary>
    public List<string> DeclaredAuthors { get; set; } = new();

    /// <summary>
    /// What the planner noticed and could not decide - alternative division
    /// depths, msItems with no matching div, divs with no matching msItem.
    /// Read before confirming.
    /// </summary>
    public List<string> Notes { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string PlanPathFor(string xmlPath) =>
        Path.ChangeExtension(xmlPath, ".plan.json");

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions), Encoding.UTF8);

    public static MenotaIngestPlan? Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<MenotaIngestPlan>(File.ReadAllText(path), JsonOptions)
            : null;
}

public sealed class MenotaWorkPlan
{
    /// <summary>
    /// Index paths into the body's div tree - "0/1" is the second child div of
    /// the first child div of body. Several because one work is often several
    /// sibling divs: Alcuin's De virtutibus in AM 619 is 24 div type="part".
    ///
    /// An index path rather than an xml:id because there are no xml:id on divs
    /// to point at. It is fragile against re-downloading a corrected file from
    /// Menota, which is why WordCount is recorded alongside - the ingest
    /// refuses if the count has moved.
    /// </summary>
    public List<string> DivPaths { get; set; } = new();

    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Empty means Anonymous. Only AM 619 names one (Alcuin, on msItem 1);
    /// everything else is either conventional attribution the cataloguer left
    /// off - Snorri for the Edda - or genuinely anonymous.
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Lowercase slug for the minted URN: urn:menota:AM-242-fol:gylfaginning.</summary>
    public string UrnSlug { get; set; } = string.Empty;

    /// <summary>The msItem @n this was matched to, or empty.</summary>
    public string MsItemN { get; set; } = string.Empty;

    /// <summary>head-text, position, or unmatched. Advisory; recorded so a
    /// confirmed plan says how much of it was guessed.</summary>
    public string MatchBasis { get; set; } = "unmatched";

    public int WordCount { get; set; }

    public bool Include { get; set; } = true;

    /// <summary>
    /// The manuscript's own top-level section this work sits in - AM 619 4to's
    /// div type="major-section" subtype="Alcuin", say.
    ///
    /// Shown as a column in the review, because it is where the boundary
    /// between one attributed work and forty-two anonymous ones is actually
    /// recorded, and a reviewer selecting rows needs to see it on the row
    /// rather than infer it from a note.
    /// </summary>
    public string Section { get; set; } = "";
}

/// <summary>
/// Derives a proposed MenotaIngestPlan from a manuscript. Proposals only -
/// see the class comment on MenotaIngestPlan.
/// </summary>
public sealed class MenotaIngestPlanner
{
    private static readonly XNamespace Tei = MenotaXmlLoader.Tei;

    private sealed record MsItem(string N, string Title, string? Author);

    public MenotaIngestPlan Plan(string xmlPath, XDocument doc)
    {
        var plan = new MenotaIngestPlan
        {
            FileName = Path.GetFileName(xmlPath),
            ManuscriptId = ReadManuscriptId(doc, Path.GetFileNameWithoutExtension(xmlPath)),
            Language = doc.Descendants(Tei + "language").FirstOrDefault()?.Attribute("ident")?.Value ?? ""
        };

        plan.ReadingLevel = MenotaXmlLoader.ChooseReadingLevel(doc, out var coverage, out var missingLevel);
        plan.Orthography = plan.ReadingLevel == "norm" ? "normalised" : "diplomatic";
        plan.Notes.Add($"Reading level {plan.ReadingLevel} at {coverage:P0} coverage.");

        // Below 100% the text is not purely at one level, and the shortfall is
        // stated rather than left inside a percentage. Those words fall back to
        // the next level available, which means a handful of them are spelled
        // as the scribe wrote them in a text that is otherwise normalised.
        if (missingLevel > 0)
        {
            plan.Notes.Add(
                $"{missingLevel:N0} word(s) carry no {plan.ReadingLevel} reading and fall back to the " +
                "next level present, so a few spellings will not match the rest.");
        }

        if (plan.Orthography == "diplomatic")
        {
            plan.Notes.Add(
                "Diplomatic transcription: spelling follows the scribe, not a dictionary. " +
                "Ingested for reading and search; excluded from stylometry by Editions.Orthography.");
        }

        var body = doc.Descendants(Tei + "body").FirstOrDefault();
        if (body == null)
        {
            plan.Notes.Add("No <body> element. Nothing to divide.");
            return plan;
        }

        var scribalHeadings = MenotaXmlLoader.HeadingsUseWordMarkup(doc);

        var msItems = ReadMsItems(doc);
        var leafItems = LeafItems(msItems);
        plan.Notes.Add($"{msItems.Count} msItem(s) in msContents, {leafItems.Count} of them leaves.");

        // Named authors are stated up front rather than left to be discovered
        // in a row that may never match.
        //
        // The catalogue is the only place an author is named, and it reaches a
        // work only through a matched msItem. Where no titles match, no author
        // does either, and everything imports as Anonymous - which is how
        // Alcuin went missing from AM 619 4to the moment matching by catalogue
        // order was withdrawn. Saying so here means the reviewer can put the
        // attribution back on the right row.
        var named = leafItems.Where(m => !string.IsNullOrWhiteSpace(m.Author)).ToList();
        foreach (var item in named)
        {
            plan.Notes.Add($"msContents attributes item {item.N} \"{item.Title}\" to {item.Author}.");
            if (!plan.DeclaredAuthors.Contains(item.Author!, StringComparer.OrdinalIgnoreCase))
                plan.DeclaredAuthors.Add(item.Author!);
        }

        var divisions = LeafDivisions(body);
        if (divisions.Count == 0)
        {
            plan.Notes.Add("No div anywhere in <body> contains <w> elements.");
            return plan;
        }

        // The proposal is checked for coverage, not just for plausibility.
        //
        // An earlier version picked whichever nesting depth had a division
        // count closest to the number of catalogue entries, which on AM 28 8vo
        // chose depth 2 - thirteen divisions, because the manuscript's last
        // top-level chapter happens to contain thirteen short texts. The
        // Skanske lov itself, 211 chapters and 14,096 words, sat at depth 1
        // and was silently left out. Thirteen looked like a good answer beside
        // nine catalogue entries and was not one.
        //
        // Leaf divisions cannot do that: every division that holds text is
        // either in the set or inside something that is. Any residue below
        // 100% is worth seeing, so it is stated rather than checked against a
        // threshold.
        var bodyWords = body.Descendants(Tei + "w").Count();
        var coveredWords = divisions.Sum(d => MenotaXmlLoader.DirectWords(d).Count());
        plan.Notes.Add(
            $"{divisions.Count} division(s) holding text, covering {coveredWords:N0} of " +
            $"{bodyWords:N0} words ({(bodyWords == 0 ? 0 : (double)coveredWords / bodyWords):P1}).");

        // Divs with no word markup are editorial matter, not manuscript text,
        // and are already excluded by LeafDivisions. This is the check that
        // keeps Indrebo's 1931 Nynorsk introduction to AM 619 - 65,000
        // characters of it, under div type="introduction" - out of a corpus of
        // Old Norwegian. It carries no <w> at all, where the manuscript text
        // carries one per word.
        var skipped = body.Descendants(Tei + "div")
            .Count(d => !d.Descendants(Tei + "w").Any() && !d.Descendants(Tei + "div").Any());
        if (skipped > 0)
            plan.Notes.Add($"{skipped} div(s) skipped as editorial matter: no word markup.");

        // With no catalogue entries at all, there is no evidence the manuscript
        // holds more than one work, and divisions are chapters until something
        // says otherwise.
        //
        // Headings do not say otherwise. Holm perg 4 fol has 381 of them and
        // they are chapter rubrics - "Her segir um dauda Vada risa", "Velent
        // drepr dvergana" - narrating one saga straight through. Proposing 381
        // works because 381 divisions carry a heading mistakes the manuscript's
        // internal signposting for a table of contents. The rubrics are not
        // lost: they are ingested as the first line of each part.
        //
        // This is the same reasoning as the single-msItem rule below. The
        // catalogue is the only authority on how many works a manuscript
        // holds, and silence from it is not a count of one per division.
        if (leafItems.Count == 0 && divisions.Count > 1)
        {
            plan.Notes.Add(
                $"No catalogue entries, so all {divisions.Count} divisions are proposed as one work " +
                "named after the manuscript. Split where the works actually begin if it holds more " +
                "than one.");

            BuildWorks(plan, new List<List<XElement>> { divisions }, leafItems, body, plan.ReadingLevel, scribalHeadings);

            if (plan.Works.Count == 1)
            {
                // A manuscript with no catalogue often still announces itself:
                // Modruvallabok's excerpt opens "Saga Ofeigs bandakarls", which
                // is the saga's name and a better title than the shelfmark and
                // folio range it was getting. Where the opening division has no
                // heading - Holm perg 4 fol's first 48 do not - the manuscript
                // identifier remains the only true thing available.
                //
                // A proposal either way: the opening heading of a saga is its
                // title, and the opening heading of a chapter is a chapter's.
                // Nothing here can tell those apart, so the note says which was
                // used.
                var opening = FirstHead(divisions[0], plan.ReadingLevel, scribalHeadings);

                if (opening.Length > 0)
                {
                    plan.Notes.Add($"Titled \"{opening}\" from the opening heading. Rename if that is a chapter rather than the work.");
                    plan.Works[0].Title = opening;
                }
                else
                {
                    plan.Works[0].Title = plan.ManuscriptId;
                }

                plan.Works[0].UrnSlug = Slug(plan.Works[0].Title);
            }

            return plan;
        }

        // A manuscript the catalogue describes as containing exactly one work
        // is one work, however many chapters it is divided into.
        //
        // AM 36 fol and AM 63 fol are both Heimskringla, and both arrived as
        // 216 and 318 separate "works" with chapter headings for titles,
        // because nothing stopped a run of chapters from being read as a run
        // of works. msContents is unambiguous here in a way it is not
        // anywhere else, and taking it at its word costs nothing: a
        // manuscript that turns out to hold more can be split in the review.
        if (leafItems.Count == 1 && divisions.Count > 1)
        {
            plan.Notes.Add(
                $"msContents lists a single work, so all {divisions.Count} divisions are proposed " +
                "as its parts. Split if the manuscript holds more than the catalogue says.");

            BuildWorks(plan, new List<List<XElement>> { divisions }, leafItems, body, plan.ReadingLevel, scribalHeadings);
            return plan;
        }

        // If the manuscript divides far finer than msContents does, sibling
        // runs whose @n restarts at 1 are the likelier work boundaries. AM 28
        // 8vo is the case: a flat list of 200-odd div type="chapter" against
        // nine leaf msItems, with the numbering restarting where Skanske lov
        // gives way to Skanske kirkelov.
        if (leafItems.Count > 0 && divisions.Count > leafItems.Count * 2)
        {
            var grouped = GroupByNumberingRestart(divisions);
            if (grouped.Count > 1 && grouped.Count < divisions.Count)
            {
                plan.Notes.Add(
                    $"Divisions ({divisions.Count}) far exceed msItems ({leafItems.Count}); " +
                    $"grouped into {grouped.Count} run(s) at @n restarts.");
                BuildWorks(plan, grouped, leafItems, body, plan.ReadingLevel, scribalHeadings);
                return plan;
            }
        }

        BuildWorks(plan, divisions.Select(d => new List<XElement> { d }).ToList(), leafItems, body, plan.ReadingLevel, scribalHeadings);
        return plan;
    }

    private void BuildWorks(
        MenotaIngestPlan plan,
        List<List<XElement>> groups,
        List<MsItem> leafItems,
        XElement body,
        string level,
        bool scribalHeadingsOnly)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Falling back to catalogue order is only defensible when there are as
        // many divisions as entries. Where the counts differ, the nth division
        // is not the nth entry - something earlier is untranscribed, or two
        // entries share a division - and matching by position slides every
        // title after that point onto the wrong text.
        //
        // Holm D 4 is the case: 26 catalogue entries, 15 divisions, and the
        // first row came out titled "List of contents" over 35,424 words.
        // Every title on it was plausible, none was checkable, and a wrong
        // title that looks right is worse than none.
        var positionIsTrustworthy = groups.Count == leafItems.Count;
        if (!positionIsTrustworthy && leafItems.Count > 0)
        {
            plan.Notes.Add(
                $"{groups.Count} division(s) against {leafItems.Count} catalogue entries - not the same " +
                "count, so titles are taken from headings only, never from catalogue order.");
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var head = FirstHead(group[0], level, scribalHeadingsOnly);
            var wordCount = group.Sum(d => d.Descendants(Tei + "w").Count());

            var match = MatchMsItem(head, leafItems, used);
            string title;
            string basis;

            if (match != null)
            {
                title = match.Title;
                basis = "head-text";
                used.Add(match.N);
            }
            else if (positionIsTrustworthy && i < leafItems.Count && !used.Contains(leafItems[i].N))
            {
                match = leafItems[i];
                title = match.Title;
                basis = "position";
                used.Add(match.N);
            }
            else
            {
                title = !string.IsNullOrWhiteSpace(head) ? Tidy(head) : $"Untitled section {i + 1}";
                basis = "unmatched";
            }

            plan.Works.Add(new MenotaWorkPlan
            {
                Section = SectionOf(group[0], body),
                DivPaths = group.Select(d => PathOf(d, body)).ToList(),
                Title = title,
                Author = match?.Author ?? "",
                UrnSlug = Slug(title),
                MsItemN = match?.N ?? "",
                MatchBasis = basis,
                WordCount = wordCount
            });
        }

        var unmatched = leafItems.Where(m => !used.Contains(m.N)).ToList();
        foreach (var m in unmatched)
        {
            var by = string.IsNullOrWhiteSpace(m.Author) ? "" : $" ({m.Author})";
            plan.Notes.Add($"msItem {m.N} \"{m.Title}\"{by} has no division. Not transcribed, or mis-divided.");
        }

        var guessed = plan.Works.Count(w => w.MatchBasis != "head-text");
        if (guessed > 0)
            plan.Notes.Add($"{guessed} of {plan.Works.Count} title(s) not matched on head text. Check these first.");
    }

    /// <summary>
    /// Every div that holds text and does not merely contain other divs that
    /// do - the finest division the manuscript itself marks.
    ///
    /// A div whose word-bearing children are divs is a container and yields to
    /// them; a div with text of its own is a division. Between them these
    /// account for essentially every word in the body, which is the property
    /// that matters: a proposal that quietly omits half a manuscript is worse
    /// than one that divides it too finely, because too finely is visible in
    /// the review and omitted is not.
    /// </summary>
    private static List<XElement> LeafDivisions(XElement body)
    {
        var result = new List<XElement>();

        void Walk(XElement el)
        {
            foreach (var div in el.Elements(Tei + "div"))
            {
                if (!div.Descendants(Tei + "w").Any()) continue;

                // A div can be both: a section with prose of its own and
                // chapters beneath it. Treating it as a container only, as an
                // earlier version did, silently discarded its own text - 5,530
                // words of Holm perg 4 fol, which showed up as a coverage
                // figure of 95.2% and nothing else.
                if (MenotaXmlLoader.DirectWords(div).Any()) result.Add(div);

                Walk(div);
            }
        }

        Walk(body);
        return result;
    }

    /// <summary>
    /// Splits a run of sibling divs wherever the @n numbering restarts at or
    /// below a previous value. Divs with no @n join the run in progress -
    /// AM 28 has plenty, and a missing number is a gap in the manuscript
    /// rather than a new work.
    /// </summary>
    private static List<List<XElement>> GroupByNumberingRestart(List<XElement> divs)
    {
        var groups = new List<List<XElement>>();
        var current = new List<XElement>();
        var previous = int.MinValue;

        foreach (var div in divs)
        {
            var raw = div.Attribute("n")?.Value;
            var hasNumber = int.TryParse(raw, out var n);

            if (hasNumber && n <= previous && current.Count > 0)
            {
                groups.Add(current);
                current = new List<XElement>();
            }

            current.Add(div);
            if (hasNumber) previous = n;
        }

        if (current.Count > 0) groups.Add(current);
        return groups;
    }

    private static List<MsItem> ReadMsItems(XDocument doc)
    {
        return doc.Descendants(Tei + "msItem")
            .Select(item => new MsItem(
                item.Attribute("n")?.Value ?? "",
                Tidy(item.Elements(Tei + "title").FirstOrDefault()?.Value ?? ""),
                Tidy(item.Elements(Tei + "author").FirstOrDefault()?.Value ?? "") is { Length: > 0 } a ? a : null))
            .Where(m => m.Title.Length > 0)
            .ToList();
    }

    /// <summary>
    /// msItems nest - 2 is "Homilies" and 2.1 to 2.42 are the homilies. Only
    /// the leaves name works; the parents name the collection.
    /// </summary>
    private static List<MsItem> LeafItems(List<MsItem> all)
    {
        var leaves = all
            .Where(m => !all.Any(other => other.N != m.N && other.N.StartsWith(m.N + ".", StringComparison.Ordinal)))
            .ToList();

        // An author declared on the parent applies to its leaves - Alcuin sits
        // on msItem 1 in AM 619, not on anything below it.
        return leaves.Select(leaf =>
        {
            if (leaf.Author != null) return leaf;
            var parent = all.FirstOrDefault(p =>
                p.N != leaf.N && leaf.N.StartsWith(p.N + ".", StringComparison.Ordinal) && p.Author != null);
            return parent == null ? leaf : leaf with { Author = parent.Author };
        }).ToList();
    }

    private static MsItem? MatchMsItem(string head, List<MsItem> items, HashSet<string> used)
    {
        if (string.IsNullOrWhiteSpace(head)) return null;
        var key = Normalise(head);
        if (key.Length == 0) return null;

        return items.FirstOrDefault(m => !used.Contains(m.N) && Normalise(m.Title) == key);
    }

    /// <summary>
    /// A division's heading, read at the manuscript's own orthographic level.
    ///
    /// Goes through MenotaXmlLoader.WordsText rather than .Value because a
    /// heading usually carries the same word markup as the text - see the
    /// comment there for what .Value does to it.
    ///
    /// The first heading only. A division often has several: a chapter number,
    /// the title, and sometimes an editor's note about a variant reading in
    /// another manuscript.
    /// </summary>
    private static string FirstHead(XElement div, string level, bool scribalOnly)
    {
        var heads = div.Elements(Tei + "head");

        // Where the manuscript distinguishes them, the editor's headings are
        // not titles. See MenotaXmlLoader.HeadingsUseWordMarkup.
        if (scribalOnly) heads = heads.Where(h => h.Descendants(Tei + "w").Any());

        var head = heads.FirstOrDefault();
        return head == null ? "" : Tidy(MenotaXmlLoader.WordsText(head, level));
    }

    private static string ReadManuscriptId(XDocument doc, string fallback)
    {
        var idno = doc.Descendants(Tei + "msIdentifier")
            .Elements(Tei + "idno")
            .FirstOrDefault()?.Value;

        return string.IsNullOrWhiteSpace(idno) ? fallback : Tidy(idno);
    }

    /// <summary>
    /// The type and subtype of the top-level div this division sits under.
    ///
    /// AM 619 4to labels its own structure - major-section/Alcuin, then
    /// major-section/homilies - and that label is the difference between an
    /// attribution and a mis-attribution. Empty where the manuscript's
    /// top-level divs are simply its chapters, which is most of them.
    /// </summary>
    private static string SectionOf(XElement div, XElement body)
    {
        var top = div;
        while (top.Parent != null && top.Parent != body) top = top.Parent;
        if (top == body) return "";

        var label = string.Join("/", new[] { top.Attribute("type")?.Value, top.Attribute("subtype")?.Value }
            .Where(v => !string.IsNullOrWhiteSpace(v)));

        // A section that holds only this division tells the reviewer nothing
        // they cannot see in the row itself.
        var siblings = top.DescendantsAndSelf(Tei + "div").Count(d => d.Descendants(Tei + "w").Any());
        return siblings > 1 ? label : "";
    }

    /// <summary>Index path of a div relative to body: "0/2/1".</summary>
    private static string PathOf(XElement div, XElement body)
    {
        var parts = new List<string>();
        var node = div;

        while (node != null && node != body)
        {
            var parent = node.Parent;
            if (parent == null) break;
            var index = parent.Elements(Tei + "div").ToList().IndexOf(node);
            parts.Insert(0, index.ToString());
            node = parent;
        }

        return string.Join("/", parts);
    }

    private static string Tidy(string s) =>
        string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Normalise(string s) =>
        new string(Tidy(s).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in Tidy(title).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == ' ' && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }

        return sb.ToString().Trim('-') is { Length: > 0 } s ? s : "untitled";
    }
}
