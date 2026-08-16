using System.Xml.Linq;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

public record IngestProgress(string CurrentAuthor, string CurrentWork, int WorksProcessed, int TotalWorks);

/// <summary>
/// Walks a locally-cloned Perseus canonical-*Lit repo (e.g.
/// canonical-greekLit or canonical-latinLit) and ingests every
/// author/work/edition it finds into the database.
///
/// Clone the repos yourself first:
///   git clone https://github.com/PerseusDL/canonical-greekLit
///   git clone https://github.com/PerseusDL/canonical-latinLit
/// then point RepoRootPaths at each repo's "data" folder.
/// </summary>
public class PerseusIngestService
{
    private readonly CtsCatalogReader _catalogReader = new();
    private readonly TeiParser _teiParser = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly EditionHeaderRepository _editionHeaderRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly ApparatusRepository _apparatusRepo = new();

    /// <summary>
    /// Files that failed to ingest (bad XML, unrecognized structure, etc.)
    /// and were skipped rather than aborting the whole run. Check this after
    /// IngestAsync completes.
    /// </summary>
    public List<(string FilePath, string Error)> FailedFiles { get; } = new();

    /// <summary>
    /// The one author to file every textgroup under whose catalog names nobody. Null -
    /// the default - passes them over, which is right for a corpus where a missing name
    /// means a malformed file.
    ///
    /// The Patrologia Latina is the case for setting it: it leaves the name empty for
    /// works that have no author to give - the Council of Carthage, an appendix to
    /// Cyprian, an anonymous passion - and names everything else normally. Those are
    /// texts worth having, and the corpus supplies its own word for the case, writing
    /// "Incertus" wherever it does fill the name in.
    ///
    /// One shared author rather than one per textgroup, because there are roughly eight
    /// hundred of them: filed separately they would be eight hundred identical rows in
    /// the library tree, which is a worse answer than either dropping them or naming
    /// them nothing. Their works keep their own URNs, so a note still binds to the
    /// passage it was written about; only the author they hang under is shared.
    ///
    /// The URN is this application's own grouping key rather than a published CTS
    /// identifier - there is nothing to cite here, and the alternative is no row at all.
    /// </summary>
    public (string Urn, string Name)? UnnamedTextGroupAuthor { get; set; }

    /// <summary>
    /// Whether textgroups naming the same author should share one author row. Off by
    /// default: in a catalogued corpus every author has one textgroup, and a repeated
    /// name is likelier to be two people than one.
    ///
    /// On for the Patrologia Latina, where it is the other way round. Migne's volumes
    /// return to an author again and again, and each appearance became its own
    /// textgroup - Alphanus of Benevento six times, Anonymus nine. Left alone the
    /// library tree lists the same name over and over with a work or two under each,
    /// which is not a catalogue so much as a pile.
    ///
    /// Matching is on the name as printed, case and spacing aside. It will not join
    /// two spellings of one man, and it will join two men who share a name - but a
    /// corpus that repeats "Anonymus" nine times is telling you which of those errors
    /// it actually contains.
    /// </summary>
    public bool MergeAuthorsByName { get; set; }

    private static string NormaliseAuthorName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <param name="repoDataPaths">
    /// Path(s) to the "data" folder inside each cloned repo, e.g.
    /// "C:\src\canonical-greekLit\data" and "C:\src\canonical-latinLit\data".
    /// </param>
    public async Task IngestAsync(
        IEnumerable<(string DataPath, string Namespace)> repoDataPaths,
        IProgress<IngestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var (dataPath, ns) in repoDataPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await IngestRepoAsync(dataPath, ns, progress, cancellationToken);
        }

        // Seed attribution from the built-in catalog, and say so.
        //
        // Perseus files the spuria under the author without comment - correctly,
        // since its job is to transmit what the manuscripts say - so a freshly
        // ingested Plato presents Definitiones as flatly Platonic. This marks
        // the well-known cases and leaves anything the reader has judged for
        // themselves alone.
        //
        // Reported rather than silent: reclassifying works in somebody's library
        // without telling them is not a courtesy.
        var marked = await new WorkRepository().ApplyCatalogDefaultsAsync(cancellationToken);

        if (marked > 0)
        {
            progress?.Report(new IngestProgress(
                "Attribution",
                $"Marked {marked} work(s) whose attribution is doubted",
                0,
                0));
        }
    }

    private async Task IngestRepoAsync(
        string dataPath, string ns, IProgress<IngestProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dataPath))
            throw new DirectoryNotFoundException($"Repo data folder not found: {dataPath}");

        var textGroupDirs = Directory.GetDirectories(dataPath);
        var totalWorks = textGroupDirs.Length; // rough estimate for progress, refined per-group below
        int worksProcessed = 0;

        // Which author row a given name already has, so repeats join it. Seeded from
        // the library rather than from this run alone, so a collection arriving second
        // joins the authors the first one created instead of shadowing them.
        Dictionary<string, string>? authorUrnByName = null;
        if (MergeAuthorsByName)
        {
            authorUrnByName = (await _authorRepo.GetAllAsync(cancellationToken))
                .Where(a => string.Equals(a.Namespace, ns, StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => NormaliseAuthorName(a.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().CtsUrn, StringComparer.Ordinal);
        }

        foreach (var textGroupDir in textGroupDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupCtsPath = Path.Combine(textGroupDir, "__cts__.xml");
            var groupInfo = _catalogReader.ReadTextGroup(groupCtsPath);
            if (groupInfo == null) continue; // not a valid textgroup folder, skip

            // A catalog that names no author: either the corpus has said what to call
            // that case, or the file is malformed and the folder is passed over. Never
            // an author row with no name - unreadable, unsearchable, and impossible to
            // tell from the next one.
            var groupUrn = groupInfo.Urn;
            var groupName = groupInfo.GroupName;
            if (string.IsNullOrWhiteSpace(groupName))
            {
                if (UnnamedTextGroupAuthor == null) continue;
                (groupUrn, groupName) = UnnamedTextGroupAuthor.Value;
            }

            // Two textgroups naming the same author are that author, not two of them.
            // The first URN seen wins and the rest join it, so their works gather under
            // one row instead of six identical ones.
            if (authorUrnByName != null)
            {
                var key = NormaliseAuthorName(groupName);
                if (authorUrnByName.TryGetValue(key, out var existingUrn)) groupUrn = existingUrn;
                else authorUrnByName[key] = groupUrn;
            }

            var authorId = await _authorRepo.UpsertAsync(new Author
            {
                CtsUrn = groupUrn,
                Name = groupName,
                Namespace = ns
            }, cancellationToken);

            var workDirs = Directory.GetDirectories(textGroupDir);
            foreach (var workDir in workDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var workCtsPath = Path.Combine(workDir, "__cts__.xml");
                var workInfos = _catalogReader.ReadWorks(workCtsPath);

                foreach (var workInfo in workInfos)
                {
                    progress?.Report(new IngestProgress(groupInfo.GroupName, workInfo.Title, worksProcessed, totalWorks));

                    var workId = await _workRepo.UpsertAsync(new Work
                    {
                        AuthorId = authorId,
                        CtsUrn = workInfo.Urn,
                        Title = workInfo.Title
                    }, cancellationToken);

                    await IngestEditionsForWorkAsync(workDir, workId, ns, cancellationToken);
                }

                worksProcessed++;
            }
        }
    }

    private async Task IngestEditionsForWorkAsync(string workDir, int workId, string corpusNamespace, CancellationToken cancellationToken)
    {
        var editionFiles = Directory.GetFiles(workDir, "*.xml")
            .Where(f => !Path.GetFileName(f).Equals("__cts__.xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var editionFile in editionFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var editionUrn = DeriveEditionUrn(editionFile);
                var (kind, language, translator, header) = InspectEdition(editionFile, corpusNamespace);

                var editionId = await _editionRepo.UpsertAsync(new Edition
                {
                    WorkId = workId,
                    CtsUrn = editionUrn,
                    Kind = kind,
                    Language = language,
                    Translator = translator,
                    SourcePath = editionFile
                }, cancellationToken);

                // Clear and re-insert so re-running ingestion after a repo
                // update doesn't leave stale/duplicate text nodes behind.
                await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

                if (header != null)
                {
                    await _editionHeaderRepo.SaveAsync(editionId, header, cancellationToken);
                }

                var parsed = _teiParser.Parse(editionFile);
                var nodes = _teiParser.ToTextNodes(editionId, parsed);
                await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);

                // Apparatus comes from the same parse - LastApparatus describes
                // the file just read - and is replaced wholesale for the same
                // reason the text nodes are cleared above.
                await _apparatusRepo.ReplaceForEditionAsync(
                    editionId, _teiParser.ToApparatusEntries(editionId), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One malformed file (bad XML, an entity we don't recognize,
                // an unexpected structure) shouldn't take down a multi-hour
                // ingest run. The Edition row (if it got created) stays with
                // whatever TextNodes it has - rerun ingest later to retry
                // just the files that failed.
                FailedFiles.Add((editionFile, ex.Message));
            }
        }
    }

    private static string DeriveEditionUrn(string filePath)
    {
        // Perseus edition filenames are themselves the CTS URN suffix, e.g.
        // "tlg0012.tlg001.perseus-grc2.xml" -> "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2"
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return fileName; // stored as-is; good enough as a unique key even without the full urn: prefix
    }

    /// <summary>
    /// Determines whether an edition file is an original-language text or a
    /// translation, and in which language.
    ///
    /// The decisive signal is the CTS version identifier in the filename -
    /// perseus-grc2, perseus-eng1, digilibLT-lat1 and so on - checked
    /// against the corpus the file came from. A greekLit text whose version
    /// says "grc" is the original; anything else there is a translation, and
    /// the same logic inverted for latinLit. That also correctly handles the
    /// Latin translations of Greek works Perseus carries, which are
    /// translations despite being in a "original-looking" language.
    ///
    /// It deliberately does NOT trust the header's langUsage. That element
    /// lists every language appearing anywhere in a document, in no
    /// particular order, so an English translation that quotes Greek freely
    /// (Antiphon's, for one) declares grc among its languages and gets read
    /// as a Greek original - which put the translation in the original pane
    /// and left the translation pane empty.
    /// </summary>
    private static (EditionKind Kind, string? Language, string? Translator, EditionHeader? Header) InspectEdition(
        string filePath, string corpusNamespace)
    {
        var versionLanguage = ExtractVersionLanguage(Path.GetFileNameWithoutExtension(filePath));

        var corpusLanguage = corpusNamespace switch
        {
            "greekLit" => "grc",
            "latinLit" => "lat",
            // The Renaissance and early-modern collection. Without this the
            // namespace falls through to null, which leaves both the edition
            // Kind unresolved and the language unset - and an edition with no
            // language can't reach the English lemma data at all, since
            // English and Latin are only distinguishable by that column.
            "engLit" => "eng",
            _ => null
        };

        var kind = EditionKind.Unknown;
        if (versionLanguage != null && corpusLanguage != null)
        {
            kind = string.Equals(versionLanguage, corpusLanguage, StringComparison.OrdinalIgnoreCase)
                ? EditionKind.Original
                : EditionKind.Translation;
        }

        string? translator = null;
        EditionHeader? header = null;

        try
        {
            var doc = XDocument.Load(filePath);
            XNamespace tei = "http://www.tei-c.org/ns/1.0";

            // Taken from the document this method already loaded rather than
            // re-reading the file: an ingest run opens each file twice as it
            // is, and a third pass purely for the header would be waste
            // measured in hours across a full corpus.
            header = TeiHeaderReader.Read(doc);

            // Translator, when present, usually lives in the titleStmt.
            translator = doc.Descendants(tei + "titleStmt")
                .Descendants(tei + "editor")
                .FirstOrDefault(e => (e.Attribute("role")?.Value ?? "").Contains("translator", StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim();

            // Fallback only if the filename gave nothing: TEI marks the body
            // div as type="edition" or type="translation", which is a real
            // statement about the text rather than a list of languages
            // occurring in it.
            if (kind == EditionKind.Unknown)
            {
                var bodyDiv = doc.Descendants(tei + "div")
                    .FirstOrDefault(d =>
                    {
                        var type = d.Attribute("type")?.Value;
                        return type is "edition" or "translation";
                    });

                var divType = bodyDiv?.Attribute("type")?.Value;
                if (divType == "edition") kind = EditionKind.Original;
                else if (divType == "translation") kind = EditionKind.Translation;

                versionLanguage ??= bodyDiv?.Attribute(XNamespace.Xml + "lang")?.Value;
            }
        }
        catch
        {
            // Malformed XML shouldn't kill the whole ingest run - whatever
            // the filename already told us still stands, and a missing
            // translator name is not worth failing over.
        }

        return (kind, versionLanguage, translator, header);
    }

    /// <summary>
    /// Pulls the language code out of a CTS version identifier: the last
    /// dot-segment of the name, after its final hyphen, with the trailing
    /// version number removed. "tlg0028.tlg005.perseus-eng2" gives "eng";
    /// "phi0448.phi002.digilibLT-lat1" gives "lat".
    /// </summary>
    private static string? ExtractVersionLanguage(string fileNameWithoutExtension)
    {
        var version = fileNameWithoutExtension.Split('.').LastOrDefault();
        if (string.IsNullOrEmpty(version)) return null;

        var hyphenIndex = version.LastIndexOf('-');
        if (hyphenIndex < 0 || hyphenIndex == version.Length - 1) return null;

        var code = version[(hyphenIndex + 1)..].TrimEnd("0123456789".ToCharArray());
        return code.Length == 3 ? code.ToLowerInvariant() : null;
    }
}
