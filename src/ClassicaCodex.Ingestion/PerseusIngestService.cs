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

    /// <summary>
    /// Files that failed to ingest (bad XML, unrecognized structure, etc.)
    /// and were skipped rather than aborting the whole run. Check this after
    /// IngestAsync completes.
    /// </summary>
    public List<(string FilePath, string Error)> FailedFiles { get; } = new();

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
    }

    private async Task IngestRepoAsync(
        string dataPath, string ns, IProgress<IngestProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dataPath))
            throw new DirectoryNotFoundException($"Repo data folder not found: {dataPath}");

        var textGroupDirs = Directory.GetDirectories(dataPath);
        var totalWorks = textGroupDirs.Length; // rough estimate for progress, refined per-group below
        int worksProcessed = 0;

        foreach (var textGroupDir in textGroupDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupCtsPath = Path.Combine(textGroupDir, "__cts__.xml");
            var groupInfo = _catalogReader.ReadTextGroup(groupCtsPath);
            if (groupInfo == null) continue; // not a valid textgroup folder, skip

            var authorId = await _authorRepo.UpsertAsync(new Author
            {
                CtsUrn = groupInfo.Urn,
                Name = groupInfo.GroupName,
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
