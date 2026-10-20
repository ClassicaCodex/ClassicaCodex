using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Epigraphy;

namespace ClassicaCodex.Ingestion.Defixiones;

/// <summary>
/// Puts ancient curse tablets into the library: the lead sheets, buried in
/// graves and wells and thrown into sacred springs, on which Greeks and Romans
/// bound a rival's tongue, a thief's body or a charioteer's horses.
///
/// About 840 of them, from three open sources (see <see cref="DefixionesCatalogue"/>):
/// every tablet found in Attica, with English and German translations, from the
/// Inscriptiones Graecae; the Greek tablets of Sicily from I.Sicily; and the
/// Latin tablets of the Roman provinces - the Bath and Uley tablets among them -
/// from the Epigraphic Database Heidelberg.
///
/// <b>What a work is.</b> A region: Attica, Sicily, or a Roman province, and in
/// a province with both Greek and Latin tablets, one work for each. A tablet
/// is a few lines long, and a work per tablet would put eight hundred
/// one-paragraph works in the library tree.
///
/// <b>How a passage is cited.</b> Tablet and line, so 125.6 is line 6 of IG
/// II/III³ 8, 1, 125, and the tablet's own citation is in the margin. Where a
/// tablet's faces or fragments each start again at line 1 the face is added:
/// 12.2.1 is the first line of the second fragment of HD000012. The
/// translations are cited the same way, so they sit beside the lines they
/// translate.
/// </summary>
public class DefixionesIngestService
{
    public const string CollectionKey = "defixiones";
    public const string Namespace = "defix";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly FileDownloadService _downloader;

    public DefixionesIngestService(FileDownloadService? downloader = null) =>
        _downloader = downloader ?? new FileDownloadService();

    public int TabletsInstalled { get; private set; }
    public int LinesInstalled { get; private set; }
    public int WorksInstalled { get; private set; }

    private static readonly IProgress<string> Silent = new Progress<string>(_ => { });

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        var failed = new List<(string, string)>();
        var tablets = new List<CurseTablet>();

        var sources = new (string Folder, IReadOnlyList<string> Ids, Func<string, string> Url, Func<string, System.Xml.Linq.XDocument, CurseTablet?> Read, string Name)[]
        {
            ("attic", DefixionesCatalogue.AtticNumbers, DefixionesCatalogue.AtticUrl, DefixionesLoader.ReadAttic, "Attica (IG)"),
            ("sicily", DefixionesCatalogue.SicilyIds, DefixionesCatalogue.SicilyUrl, DefixionesLoader.ReadSicily, "Sicily"),
            ("heidelberg", DefixionesCatalogue.HeidelbergIds, DefixionesCatalogue.HeidelbergUrl, DefixionesLoader.ReadHeidelberg, "Heidelberg")
        };

        foreach (var source in sources)
        {
            var folder = Path.Combine(root, source.Folder);
            Directory.CreateDirectory(folder);

            for (var i = 0; i < source.Ids.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = source.Ids[i];
                var path = Path.Combine(folder, FileName(id));

                try
                {
                    if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    {
                        if (source.Folder == "heidelberg" && int.Parse(id[2..]) > DefixionesCatalogue.HeidelbergRepositoryLast)
                            await ExtractFromLateDumpAsync(folder, id, path, cancellationToken);
                        else
                            await _downloader.DownloadAsync(source.Url(id), path, Silent, cancellationToken);
                    }

                    var tablet = source.Read(id, DefixionesLoader.LoadXml(path));
                    if (tablet != null) tablets.Add(tablet);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or XmlException or InvalidDataException)
                {
                    failed.Add(($"{source.Name} {id}", ex.GetBaseException().Message));
                }

                if ((i + 1) % 25 == 0 || i == source.Ids.Count - 1)
                    progress.Report($"{source.Name}: {i + 1} of {source.Ids.Count} tablets...");
            }
        }

        // A region whose tablets are in both languages - Dacia, the Pannonias -
        // becomes a Greek work and a Latin one, each under its own author, so
        // that a Latin tablet is never filed, searched or counted as Greek.
        var mixed = tablets.GroupBy(t => (t.Source, t.Group))
            .Where(g => g.Select(t => t.Language).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var authors = new Dictionary<string, int>(StringComparer.Ordinal);
        var installed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in tablets.GroupBy(t => (t.Source, t.Group, t.Language))
                     .OrderBy(g => g.Key.Source).ThenBy(g => g.Key.Group).ThenBy(g => g.Key.Language))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var list = group.OrderBy(t => NumberKey(t.Number), StringComparer.Ordinal).ToList();
            var language = group.Key.Language;

            if (!authors.TryGetValue(language, out var authorId))
            {
                authorId = await _authorRepo.UpsertAsync(new Author
                {
                    CtsUrn = $"urn:defix:{language}",
                    Name = language == "grc" ? "Greek curse tablets" : "Latin curse tablets",
                    Namespace = Namespace,
                    Language = language
                }, cancellationToken);
                authors[language] = authorId;
            }

            var isMixed = mixed.Contains((group.Key.Source, group.Key.Group));
            installed.UnionWith(await InstallWorkAsync(
                authorId, group.Key.Source, group.Key.Group, language, isMixed, list, cancellationToken));
            WorksInstalled++;
        }

        // Editions an earlier run made and this one did not - a region whose
        // tablets have since been split by language, or renamed - are taken
        // out, or they would sit in the library beside their replacements. Only
        // when every source answered: a run that lost a source to the network
        // must not delete what that source installed last time.
        if (failed.Count == 0)
        {
            var workIds = await _editionRepo.GetWorkIdsForCollectionsAsync(new[] { CollectionKey }, cancellationToken);
            foreach (var workId in workIds)
            {
                foreach (var edition in await _editionRepo.GetByWorkAsync(workId, cancellationToken))
                {
                    if (edition.Collection == CollectionKey && !installed.Contains(edition.CtsUrn))
                        await _editionRepo.DeleteEditionAsync(edition.EditionId, cancellationToken);
                }
            }
        }

        progress.Report($"{TabletsInstalled} curse tablets in {WorksInstalled} works, {LinesInstalled:N0} lines.");
        return IngestOutcome.From(failed, attempted: sources.Sum(s => s.Ids.Count));
    }

    /// <returns>The URNs of the editions it installed.</returns>
    private async Task<List<string>> InstallWorkAsync(
        int authorId, string source, string group, string language, bool mixed, List<CurseTablet> tablets,
        CancellationToken cancellationToken)
    {
        var region = group == DefixionesLoader.UnknownProvince ? "an unknown province" : group;
        var (urn, title) = source switch
        {
            "attic" => ("urn:defix:attica", "Curse tablets from Attica (IG II/III³ 8, 1)"),
            "sicily" => ("urn:defix:sicily", "Curse tablets from Sicily (I.Sicily)"),
            _ => ($"urn:defix:edh:{Slug(group)}", $"Curse tablets from {region} (Epigraphic Database Heidelberg)")
        };

        if (mixed)
        {
            urn = $"{urn}-{language}";
            title = title.Replace(" (", $", in {(language == "grc" ? "Greek" : "Latin")} (");
        }

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = urn,
            Title = title,
            CitationScheme = "Tablet.Line"
        }, cancellationToken);

        var original = new List<TextNode>();
        var translations = new Dictionary<string, List<TextNode>>(StringComparer.Ordinal);
        var translators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var tablet in tablets)
        {
            var token = Regex.Replace(tablet.Number, @"\s+", string.Empty);
            var byPart = tablet.Lines.GroupBy(l => l.Line).Any(g => g.Count() > 1);
            string Ref(EpiDocLine l) => byPart ? $"{token}.{l.Part}.{l.Line}" : $"{token}.{l.Line}";

            var milestone = tablet.Place != null && tablet.Source != "attic" ? $"{tablet.Label}, {tablet.Place}" : tablet.Label;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in tablet.Lines)
            {
                var citation = Unique(Ref(line), seen);
                original.Add(new TextNode { CitationRef = citation, Text = line.Text, Milestone = milestone });
            }

            foreach (var (lang, (translator, lines)) in tablet.Translations)
            {
                if (!translations.TryGetValue(lang, out var list)) translations[lang] = list = new List<TextNode>();
                if (!translators.TryGetValue(lang, out var names)) translators[lang] = names = new HashSet<string>();
                if (!string.IsNullOrWhiteSpace(translator)) names.Add(translator.TrimStart('#'));

                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in lines)
                    list.Add(new TextNode { CitationRef = Unique(Ref(line), used), Text = line.Text, Milestone = milestone });
            }

            TabletsInstalled++;
        }

        LinesInstalled += original.Count;

        var urns = new List<string> { $"{urn}:{language}" };
        await InstallEditionAsync(workId, urns[0], EditionKind.Original, language, null, original, cancellationToken);
        foreach (var (lang, nodes) in translations)
        {
            var names = translators[lang];
            var translator = (names.Count > 0 ? string.Join(", ", names.Take(3)) : "the editors") + $" ({LanguageName(lang)})";
            urns.Add($"{urn}:{lang}");
            await InstallEditionAsync(workId, urns[^1], EditionKind.Translation, lang, translator, nodes, cancellationToken);
        }

        return urns;
    }

    private async Task InstallEditionAsync(
        int workId, string urn, EditionKind kind, string language, string? translator, List<TextNode> nodes,
        CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = urn,
            Kind = kind,
            Language = language,
            Translator = translator,
            SourcePath = urn,
            Collection = CollectionKey,

            // Kept out of stylometry: these are formulae copied from tablet to
            // tablet by professional writers, often one hand for many clients,
            // and a region is not an author. "Inscription" keeps them out of
            // the word-frequency query the way a script name keeps the
            // Egyptian readings out.
            Orthography = kind == EditionKind.Original ? "inscription" : null
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].EditionId = editionId;
            nodes[i].SortOrder = i + 1;
        }

        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
    }

    /// <summary>
    /// A Heidelberg tablet numbered past the end of the repository, taken from
    /// the database's dump of its last inscriptions. The zip is fetched once
    /// and kept beside the tablets, so a second run reads it from disk.
    /// </summary>
    private async Task ExtractFromLateDumpAsync(string folder, string id, string path, CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(folder, "edhEpidocDump_HD080001-HD082828.zip");
        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
            await _downloader.DownloadAsync(DefixionesCatalogue.HeidelbergLateDumpUrl, zipPath, Silent, cancellationToken);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals($"{id}.xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"{id} is not in the Heidelberg dump");
        entry.ExtractToFile(path, overwrite: true);
    }

    /// <summary>A citation made unique within its tablet by numbering a repeat ("5", then "5-2").</summary>
    private static string Unique(string citation, HashSet<string> seen)
    {
        var candidate = citation;
        for (var n = 2; !seen.Add(candidate); n++) candidate = $"{citation}-{n}";
        return candidate;
    }

    private static string FileName(string id) =>
        Regex.Replace(id, @"[^A-Za-z0-9]+", "_").Trim('_') + ".xml";

    /// <summary>Numeric order for tablet numbers that can carry a part ("175 I", "175 II").</summary>
    public static string NumberKey(string number)
    {
        var m = Regex.Match(number, @"^(\d+)(.*)$");
        return m.Success ? m.Groups[1].Value.PadLeft(8, '0') + m.Groups[2].Value.Trim() : "~" + number;
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }

        return Regex.Replace(sb.ToString(), "[^a-z0-9]+", "-").Trim('-');
    }

    private static string LanguageName(string code) => code switch
    {
        "eng" => "English",
        "deu" => "German",
        "fra" => "French",
        "ita" => "Italian",
        "spa" => "Spanish",
        _ => code
    };
}
