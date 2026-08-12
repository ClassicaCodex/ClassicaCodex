using System.Text;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Surveys a folder of Menota XML and reports what is in it.
///
/// Deliberately read-only. Menota encodes at up to three orthographic levels
/// and not every text carries all three, so before anything is ingested the
/// question is which levels are actually present - a corpus mixing normalised
/// and diplomatic text would be measuring scribes rather than authors, and
/// that failure is invisible once the text is in the database.
///
/// Also read-only because this parser has never run against a real Menota
/// file. It was written from the handbook. A survey that turns out to be wrong
/// costs a confusing report; an ingest that turns out to be wrong costs a
/// corpus that looks fine and isn't.
/// </summary>
public class MenotaCorpusReport
{
    public static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    public static readonly XNamespace Me = "http://www.menota.org/ns/1.0";

    public class FileSummary
    {
        public string FileName = string.Empty;
        public string? Title;
        public string? Language;
        public int WordElements;
        public int WithFacsimile;
        public int WithDiplomatic;
        public int WithNormalised;
        public int WithLemma;
        public int UnresolvedEntities;
        public HashSet<string> DistinctUnresolved = new(StringComparer.Ordinal);
        public string? Error;

        /// <summary>
        /// Whether this file can be used for anything that compares word
        /// frequencies. Normalised is the level whose orthography follows
        /// dictionaries rather than the manuscript; without it, spelling
        /// varies by scribe and every frequency count varies with it.
        /// </summary>
        public bool UsableForStylometry => Error == null && WordElements > 0
                                           && WithNormalised >= WordElements * 0.9;
    }

    // The DOCTYPE stripping, the MUFI entity table and menota-entities.txt
    // loading all moved to MenotaXmlLoader when the ingest was written, so the
    // survey and the ingest read a file identically. They must: a survey that
    // resolves an entity the ingest does not would describe a corpus that
    // differs from the one imported, and nothing would show the difference.

    public List<FileSummary> Survey(string folder, IProgress<string>? progress = null)
    {
        var results = new List<FileSummary>();

        if (!Directory.Exists(folder)) return results;

        var entities = MenotaXmlLoader.LoadEntities(folder);
        var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories);
        var index = 0;

        foreach (var path in files)
        {
            index++;
            progress?.Report($"Reading {Path.GetFileName(path)} ({index}/{files.Length})");

            var summary = new FileSummary { FileName = Path.GetFileName(path) };

            try
            {
                var load = MenotaXmlLoader.Load(path, entities);

                summary.UnresolvedEntities = load.UnresolvedEntities;
                summary.DistinctUnresolved = load.DistinctUnresolved;

                if (!load.Ok)
                {
                    summary.Error = load.Error ?? "could not be parsed";
                    results.Add(summary);
                    continue;
                }

                var doc = load.Document!;

                summary.Title = doc.Descendants(Tei + "title").FirstOrDefault()?.Value.Trim();
                summary.Language = doc.Descendants(Tei + "language").FirstOrDefault()?.Attribute("ident")?.Value;

                // Descendants, not Elements. Menota encodes the orthographic
                // levels two ways - as direct children of <w>, and wrapped in
                // a <choice> - and a single file mixes them. AM 619 4to has
                // 434 choice-wrapped words among 61,617 direct ones; AM 28 8vo
                // is choice-wrapped throughout, 16,945 of 16,947.
                //
                // Reading only direct children reported AM 28 as carrying no
                // diplomatic level at all. It did not change the verdict on
                // the three manuscripts tested, because me:norm is genuinely
                // absent from all of them and UsableForStylometry turns on
                // norm - but the first normalised manuscript encoded this way
                // would have been reported diplomatic and wrongly excluded.
                //
                // LevelElement rather than .Any(), so an empty placeholder does
                // not count as a reading. Holm perg 4 fol carries a
                // <me:norm/> on 115,232 words and text in only 8,243 of them;
                // counted by presence it reports as fully normalised and passes
                // UsableForStylometry, which would put a manuscript that is 93%
                // empty at that level into a Delta run.
                foreach (var w in doc.Descendants(Tei + "w"))
                {
                    summary.WordElements++;
                    if (MenotaXmlLoader.LevelElement(w, "facs") != null) summary.WithFacsimile++;
                    if (MenotaXmlLoader.LevelElement(w, "dipl") != null) summary.WithDiplomatic++;
                    if (MenotaXmlLoader.LevelElement(w, "norm") != null) summary.WithNormalised++;
                    if (w.Attribute("lemma") != null) summary.WithLemma++;
                }
            }
            catch (Exception ex)
            {
                summary.Error = ex.Message;
            }

            results.Add(summary);
        }

        return results;
    }

    /// <summary>
    /// The survey as text for the setup step's log.
    ///
    /// Written to be read by someone deciding whether the download was worth
    /// it, so it leads with the counts that decide that and says plainly when
    /// the answer is no.
    /// </summary>
    public static string Format(List<FileSummary> files)
    {
        if (files.Count == 0)
        {
            return "No XML files found in that folder.";
        }

        var sb = new StringBuilder();
        var readable = files.Where(f => f.Error == null).ToList();
        var failed = files.Where(f => f.Error != null).ToList();
        var withWords = readable.Where(f => f.WordElements > 0).ToList();
        var usable = readable.Where(f => f.UsableForStylometry).ToList();

        sb.AppendLine($"{files.Count} XML file(s) found.");
        if (failed.Count > 0) sb.AppendLine($"  {failed.Count} could not be parsed as XML.");

        if (withWords.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("None of them contain <w> elements in the Menota namespace.");
            sb.AppendLine("Either these are not Menota files, or they use an encoding this");
            sb.AppendLine("build does not recognise. Nothing has been imported.");
            return sb.ToString();
        }

        sb.AppendLine($"  {withWords.Count} contain Menota word-level markup.");
        sb.AppendLine($"  {usable.Count} carry a normalised reading for nearly every word.");

        var unresolved = readable.Where(f => f.UnresolvedEntities > 0).ToList();
        if (unresolved.Count > 0)
        {
            var distinct = unresolved.SelectMany(f => f.DistinctUnresolved).Distinct().Count();
            sb.AppendLine();
            sb.AppendLine($"  {unresolved.Count} file(s) use {distinct} special characters this build");
            sb.AppendLine("  cannot resolve, shown as \uFFFD. They are MUFI entities defined in");
            sb.AppendLine("  menota.org/menota-entities.txt - save that file into this folder");
            sb.AppendLine("  and run again to resolve them.");
        }

        if (usable.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("NONE of these carry a normalised reading. They are diplomatic");
            sb.AppendLine("transcriptions: the spelling follows each scribe's own practice rather");
            sb.AppendLine("than a dictionary. They are perfectly good to read, and the lemma");
            sb.AppendLine("counts below show which are annotated - but comparing them by word");
            sb.AppendLine("frequency would measure the scribes, not the authors.");
        }
        sb.AppendLine();
        sb.AppendLine("Per file:");

        foreach (var f in files.OrderByDescending(f => f.WordElements))
        {
            if (f.Error != null)
            {
                sb.AppendLine($"  {f.FileName}  -  could not read: {f.Error}");
                continue;
            }

            if (f.WordElements == 0)
            {
                sb.AppendLine($"  {f.FileName}  -  no Menota word markup");
                continue;
            }

            var pctNorm = 100.0 * f.WithNormalised / f.WordElements;
            var pctLemma = 100.0 * f.WithLemma / f.WordElements;

            sb.AppendLine(
                $"  {f.FileName}  [{f.Language ?? "?"}]  {f.WordElements:N0} words, " +
                $"{pctNorm:F0}% normalised, {pctLemma:F0}% lemmatised" +
                (f.UsableForStylometry ? "" : "  <- normalised level incomplete"));

            if (!string.IsNullOrWhiteSpace(f.Title)) sb.AppendLine($"      {f.Title}");
        }

        sb.AppendLine();
        sb.AppendLine("This is a survey of the folder, not an import. The import runs next, and");
        sb.AppendLine("asks you to review what it found in each manuscript before writing it.");
        sb.AppendLine("The reading levels above are worth a look first: a corpus that mixes");
        sb.AppendLine("normalised and diplomatic text produces results that look fine and are");
        sb.AppendLine("not.");

        return sb.ToString();
    }
}
