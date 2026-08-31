using System.Text.RegularExpressions;
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
    /// Folders whose CTS catalogue was missing or unreadable, and whose author
    /// and work were reconstructed from the edition files instead.
    ///
    /// Reported separately from <see cref="FailedFiles"/> because the outcome
    /// is different: nothing was lost, but the names came from the TEI header
    /// rather than from the catalogue, so a title here may not be the canonical
    /// one and is worth knowing about.
    ///
    /// This is not hypothetical tidiness. canonical-latinLit ships 65 of 399
    /// work folders with no __cts__.xml at all - canonical-greekLit ships none,
    /// which is why it went unnoticed - and the two `continue`s that used to
    /// handle that dropped 197 edition files without a word. Bede's Historia
    /// ecclesiastica, Cato's De agri cultura, Apicius, Sidonius, Augustine's
    /// letters, the Appendix Vergiliana, Petronius' fragments and Livy's
    /// Periochae were all simply not in the library, and setup reported
    /// "Done - ready."
    /// </summary>
    public List<(string FilePath, string Error)> RecoveredWithoutCatalog { get; } = new();

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

            // No catalogue, or one that would not parse. The edition files are
            // still there and still readable, and they carry the author's name
            // in their own TEI header - so the folder is reconstructed rather
            // than passed over. See RecoveredWithoutCatalog for what used to
            // happen instead.
            if (groupInfo == null)
            {
                groupInfo = DeriveTextGroup(textGroupDir, ns);
                if (groupInfo == null) continue; // no edition files under it either

                RecoveredWithoutCatalog.Add((textGroupDir,
                    $"no readable __cts__.xml for the author; read \"{groupInfo.GroupName}\" from the TEI headers instead"));
            }

            // A catalog that names no author: either the corpus has said what to call
            // that case, or the file is malformed and the folder is passed over. Never
            // an author row with no name - unreadable, unsearchable, and impossible to
            // tell from the next one.
            var groupUrn = groupInfo.Urn;
            var groupName = groupInfo.GroupName;
            if (string.IsNullOrWhiteSpace(groupName))
            {
                if (UnnamedTextGroupAuthor == null)
                {
                    // Passed over, but said out loud. Skipping a folder without
                    // recording it is the failure that hid 197 Latin editions
                    // for as long as it did, and it does not become acceptable
                    // just because this branch reaches it deliberately.
                    FailedFiles.Add((textGroupDir,
                        "the catalogue names no author and the texts name none either, so there is nothing to file them under"));
                    continue;
                }

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

                // Same recovery one level down, and this is where the bulk of
                // it happens: an empty list meant the loop below never ran and
                // every edition file in the folder went uningested, silently.
                if (workInfos.Count == 0)
                {
                    var derived = DeriveWork(workDir, ns);
                    if (derived != null)
                    {
                        workInfos.Add(derived);
                        RecoveredWithoutCatalog.Add((workDir,
                            $"no readable __cts__.xml for the work; read \"{derived.Title}\" from the TEI headers instead"));
                    }
                }

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

    /// <summary>
    /// Reconstructs a textgroup from the edition files under it, for a folder
    /// whose catalogue is missing or unreadable.
    ///
    /// The URN comes from the filenames rather than the folder name. Perseus
    /// edition filenames ARE the CTS URN suffix, so the first dot-segment of
    /// "phi0914.phi0011.perseus-lat2.xml" is the textgroup - which is the same
    /// identity the catalogue would have given, and is right even where a
    /// folder has been renamed.
    ///
    /// The name comes from the TEI titleStmt's author. Where the files name no
    /// author the name is left empty rather than invented, which hands the
    /// decision to UnnamedTextGroupAuthor - the mechanism that already exists
    /// for exactly this case.
    ///
    /// THE FOLDER NAME HAS TO AGREE WITH THE FILENAMES, and that guard is doing
    /// more work than it looks like. A missing catalogue is what keeps
    /// First1KGreek's save/, split/ and volume_xml/ working directories out of
    /// the corpus, and they hold the SAME texts as the textgroups they were
    /// derived from - recovering those the way this recovers a real textgroup
    /// would silently ingest a good part of that corpus two and three times
    /// over, and duplicate texts do not make a Delta run fail, they make it
    /// confident and wrong. See CorpusFolderExclusionTests.
    ///
    /// A genuine textgroup folder is named for its own URN segment, so
    /// phi0692/ holds phi0692.*.xml. A working directory is not: save/ holds
    /// tlg0062.*.xml. The files say which kind of folder they are sitting in.
    /// </summary>
    private static CtsCatalogReader.TextGroupInfo? DeriveTextGroup(string textGroupDir, string ns)
    {
        var files = EditionFilesUnder(textGroupDir, SearchOption.AllDirectories).ToList();
        if (files.Count == 0) return null;

        var group = Path.GetFileNameWithoutExtension(files[0]).Split('.').FirstOrDefault();
        if (string.IsNullOrEmpty(group)) return null;

        if (!string.Equals(group, new DirectoryInfo(textGroupDir).Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var author = files
            .Select(f => TeiHeaderReader.TryRead(f)?.Author)
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        // No author named anywhere: fall back to the printed collection these
        // texts were digitised from, which is the next most specific true thing
        // the files say about themselves.
        //
        // The Appendix Vergiliana is the case, and it names no author because
        // it genuinely has none - its own source calls it "Appendix Vergiliana,
        // sive carmina minora Vergilio adtributa", minor poems ATTRIBUTED to
        // Virgil. Filing eleven poems under Virgil would assert the attribution
        // the title is careful to hedge; filing them under nothing loses them.
        // The collection is what they actually are.
        author ??= files
            .Select(SourceCollectionTitle)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        return new CtsCatalogReader.TextGroupInfo($"urn:cts:{ns}:{group}", author?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// The title of the printed book a TEI file was digitised from -
    /// sourceDesc/biblStruct/monogr/title.
    ///
    /// Trimmed at the first comma, because a Latin monograph title carries its
    /// alternative form after one ("Appendix Vergiliana, sive carmina minora
    /// Vergilio adtributa") and a name in the library's author column wants the
    /// name rather than the subtitle.
    ///
    /// Its own read of the file rather than TeiHeaderReader's SourceDescription,
    /// which collapses the whole sourceDesc - editor, publisher, place, date -
    /// into one line. That is the right shape for showing a reader where a text
    /// came from and the wrong one for naming anything. Only ever called for a
    /// folder with no catalogue, so the extra read is rare.
    /// </summary>
    private static string? SourceCollectionTitle(string filePath)
    {
        try
        {
            var doc = XDocument.Load(filePath);

            var monogr = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "monogr");

            var title = monogr?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "title")?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(title)) return null;

            var comma = title.IndexOf(',');
            return comma > 0 ? title[..comma].Trim() : title;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The same for one work folder. The URN is the first two dot-segments of
    /// an edition filename - "phi0914.phi0011" - and the title is the TEI
    /// titleStmt's, which for these files is a real title rather than a
    /// placeholder: "Ab Urbe Condita, books 1-2 - 1", "De agri cultura",
    /// "Historiam ecclesiasticam gentis Anglorum".
    ///
    /// Falls back to the URN as the title, the same way ReadWorks does when a
    /// catalogue has no title element. A work named by its identifier is worse
    /// than one named properly and much better than one that is not there.
    ///
    /// Same two guards as DeriveTextGroup, for the same reason: only the files
    /// sitting directly in the folder count, and the folder name has to be the
    /// work segment its filenames carry. Without that, save/tlg0062 - a
    /// textgroup folder one level inside a working directory - reads as a work
    /// folder and pulls Lucian in a second time.
    /// </summary>
    private static CtsCatalogReader.WorkInfo? DeriveWork(string workDir, string ns)
    {
        var editionFiles = EditionFilesUnder(workDir, SearchOption.TopDirectoryOnly).ToList();
        if (editionFiles.Count == 0) return null;

        var segments = Path.GetFileNameWithoutExtension(editionFiles[0]).Split('.');
        if (segments.Length < 2) return null;

        if (!string.Equals(segments[1], new DirectoryInfo(workDir).Name, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var urn = $"urn:cts:{ns}:{segments[0]}.{segments[1]}";

        var title = editionFiles
            .Select(f => TeiHeaderReader.TryRead(f)?.Title)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        return new CtsCatalogReader.WorkInfo(urn, title?.Trim() ?? urn, null);
    }

    /// <summary>
    /// The TEI edition files in a folder, catalogues excluded, in a stable
    /// order so two runs over the same folder derive the same names.
    /// </summary>
    private static IEnumerable<string> EditionFilesUnder(string directory, SearchOption depth) =>
        Directory.EnumerateFiles(directory, "*.xml", depth)
            .Where(f => !Path.GetFileName(f).Equals("__cts__.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

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
            //
            // type="commentary" is the third one CTS defines, for the notes
            // and appendices published alongside a text. It is not a
            // translation, but the reader has two panes and this belongs in
            // the second - the one holding whatever is read against the
            // original. Unknown would mean no pane at all.
            if (kind == EditionKind.Unknown)
            {
                var bodyDiv = doc.Descendants(tei + "div")
                    .FirstOrDefault(d =>
                    {
                        var type = d.Attribute("type")?.Value;
                        return type is "edition" or "translation" or "commentary";
                    });

                var divType = bodyDiv?.Attribute("type")?.Value;
                if (divType == "edition") kind = EditionKind.Original;
                else if (divType is "translation" or "commentary") kind = EditionKind.Translation;

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
    /// version marker removed. "tlg0028.tlg005.perseus-eng2" gives "eng";
    /// "phi0448.phi002.digilibLT-lat1" gives "lat".
    ///
    /// That marker can carry a letter as well as a number. A companion volume
    /// published with a text - notes, an appendix, an index - is versioned off
    /// its parent rather than given a number of its own, so the Cambridge
    /// Septuagint Isaiah is 1st1K-eng1 and its notes are 1st1K-eng1a.
    ///
    /// Reading that as three letters plus a number alone got "eng1a" wrong in
    /// the quietest possible way: not a wrong language but no language, which
    /// left the edition's Kind unresolved, which left it in neither reader
    /// dropdown. The text ingested, and searched, and could not be opened -
    /// results pointing into a volume the reader would not show.
    ///
    /// Deliberately narrow: three letters, then nothing, or digits with at
    /// most one letter after them. A looser pattern would start reading the
    /// first three letters of any version identifier as a language code.
    /// </summary>
    private static string? ExtractVersionLanguage(string fileNameWithoutExtension)
    {
        var version = fileNameWithoutExtension.Split('.').LastOrDefault();
        if (string.IsNullOrEmpty(version)) return null;

        var hyphenIndex = version.LastIndexOf('-');
        if (hyphenIndex < 0 || hyphenIndex == version.Length - 1) return null;

        var match = Regex.Match(version[(hyphenIndex + 1)..], @"^([A-Za-z]{3})(\d+[A-Za-z]?)?$");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }
}
