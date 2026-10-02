using ClassicaCodex.Core;
using ClassicaCodex.Core.Bfm;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Bfm;

/// <summary>
/// Installs the Base de Français Médiéval: 500 texts of Old and Middle French
/// from the ninth century to the fifteenth, the Chanson de Roland, Chrétien's
/// four romances, Aucassin et Nicolette, the Queste del saint Graal, Renart,
/// Villon, and 281 fabliaux among them.
///
/// <b>Downloaded one text at a time, because that is how it is published.</b>
/// Nakala gives each text its own deposit and its own DOI; there is no archive
/// of the whole corpus. The catalogue of what to fetch ships with the
/// application - see <see cref="BfmCatalogue"/> - so the download is 500
/// straight file requests of known size rather than a walk of somebody else's
/// paginated API.
///
/// <b>Six texts are never installed.</b> The corpus marks each file as libre
/// or restreint in its own header, and the restreint ones may not be
/// redistributed. They are downloaded, read, found to be restricted, and
/// dropped - which is the only order possible, since the marking is inside the
/// file. The outcome says how many.
/// </summary>
public class BfmIngestService
{
    public const string CollectionKey = "bfm";
    public const string Namespace = "bfm";

    private readonly AuthorRepository _authorRepo;
    private readonly WorkRepository _workRepo;
    private readonly EditionRepository _editionRepo;
    private readonly TextNodeRepository _textNodeRepo;
    private readonly LemmaRepository _lemmaRepo;
    private readonly FileDownloadService _downloader;

    public BfmIngestService(
        AuthorRepository? authorRepo = null,
        WorkRepository? workRepo = null,
        EditionRepository? editionRepo = null,
        TextNodeRepository? textNodeRepo = null,
        LemmaRepository? lemmaRepo = null,
        FileDownloadService? downloader = null)
    {
        _authorRepo = authorRepo ?? new AuthorRepository();
        _workRepo = workRepo ?? new WorkRepository();
        _editionRepo = editionRepo ?? new EditionRepository();
        _textNodeRepo = textNodeRepo ?? new TextNodeRepository();
        _lemmaRepo = lemmaRepo ?? new LemmaRepository();
        _downloader = downloader ?? new FileDownloadService();
    }

    public int TextsInstalled { get; private set; }
    public int LinesInstalled { get; private set; }
    public int TextsRestricted { get; private set; }
    public long MappingsLoaded { get; private set; }

    /// <summary>Texts whose file could not be read, with the reason.</summary>
    public List<(string FilePath, string Error)> Failed { get; } = new();

    /// <summary>The language everything in this corpus is recorded under.</summary>
    public const string Language = "fro";

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(root, "bfm");
        Directory.CreateDirectory(folder);

        var texts = BfmCatalogue.Texts;
        if (texts.Count == 0)
        {
            progress.Report("The catalogue of texts is missing from this build - nothing can be fetched.");
            return IngestOutcome.Clean;
        }

        progress.Report($"{texts.Count} texts, {BfmCatalogue.TotalBytes / 1048576.0:F0} MB.");

        var lemmas = new List<Lemma>();

        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = texts[i];
            var path = Path.Combine(folder, entry.FileName);

            try
            {
                // Kept between runs. A corpus of 500 files over a slow
                // connection will be interrupted, and re-fetching 200 MB to
                // resume is the difference between a step that can be run
                // again and one that cannot.
                if (!File.Exists(path) || new FileInfo(path).Length != entry.Bytes)
                {
                    await _downloader.DownloadAsync(
                        entry.DownloadUrl, path, SilentProgress, cancellationToken);
                }

                await InstallAsync(entry, path, lemmas, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failed.Add((entry.FileName, ex.GetBaseException().Message));
            }

            if ((i + 1) % 10 == 0 || i == texts.Count - 1)
            {
                progress.Report(
                    $"[{i + 1}/{texts.Count}] {TextsInstalled} texts, {LinesInstalled:N0} passages");
            }
        }

        if (lemmas.Count > 0)
        {
            // Replaced wholesale, like every other lemma source: these are a
            // mapping from this corpus, and a partial re-run that added to the
            // old set would leave mappings for texts no longer installed.
            await _lemmaRepo.ClearByLanguageAsync(Language, cancellationToken);
            await _lemmaRepo.BulkInsertAsync(lemmas, cancellationToken);
            MappingsLoaded = lemmas.Count;
        }

        progress.Report(
            $"{TextsInstalled} texts, {LinesInstalled:N0} passages, {MappingsLoaded:N0} word-form mappings." +
            (TextsRestricted > 0
                ? $" {TextsRestricted} texts are marked restricted by the corpus and were not installed."
                : string.Empty));

        return IngestOutcome.From(Failed, attempted: texts.Count);
    }

    /// <summary>
    /// The per-file download reports every few kilobytes, and there are 500 of
    /// them; the loop above reports progress at a rate a person can read.
    /// </summary>
    private static readonly IProgress<string> SilentProgress = new Progress<string>(_ => { });

    private async Task InstallAsync(
        BfmTextEntry entry, string path, List<Lemma> lemmas, CancellationToken cancellationToken)
    {
        var text = BfmTextLoader.Load(path);

        if (!text.IsFree)
        {
            TextsRestricted++;
            return;
        }

        if (text.Lines.Count == 0) return;

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            // The corpus is largely anonymous and says so; those texts are
            // filed together rather than under the word "anonyme".
            CtsUrn = entry.NamedAuthor == null ? "urn:bfm:anonymous" : $"urn:bfm:author:{Slug(entry.NamedAuthor)}",
            Name = entry.NamedAuthor ?? "Anonymous",
            Namespace = Namespace,
            Language = Language
        }, cancellationToken);

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = $"urn:bfm:{Slug(entry.FileName)}",
            Title = Title(entry),
            CitationScheme = text.Lines.Any(l => l.IsVerse) ? "Line" : "Section"
        }, cancellationToken);

        var twoReadings = text.HasTwoReadings;

        await InstallEditionAsync(workId, entry, text, "dipl",
            twoReadings ? "diplomatic" : null,
            l => l.Diplomatic, path, cancellationToken);

        // Only where the readings actually differ. Two identical editions in
        // the dropdown is a worse answer than one.
        if (twoReadings)
        {
            await InstallEditionAsync(workId, entry, text, "norm", "normalised",
                l => l.Normalised, path, cancellationToken);
        }

        foreach (var word in text.Lines.SelectMany(l => l.Words))
        {
            if (string.IsNullOrEmpty(word.Headword)) continue;

            lemmas.Add(new Lemma
            {
                Form = word.Form,
                NormalizedForm = WordNormalizer.Normalize(word.Form),
                Headword = word.Headword!,
                Language = Language,
                PartOfSpeech = word.Tag
            });
        }

        TextsInstalled++;
    }

    private async Task InstallEditionAsync(
        int workId, BfmTextEntry entry, BfmText text, string urnSuffix, string? orthography,
        Func<BfmLine, string> reading, string sourcePath, CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:bfm:{Slug(entry.FileName)}:{urnSuffix}",
            Kind = EditionKind.Original,
            Language = Language,
            Translator = null,
            SourcePath = sourcePath,
            Orthography = orthography,
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        var nodes = text.Lines
            .Select((line, index) => new TextNode
            {
                EditionId = editionId,
                CitationRef = line.Citation,
                SortOrder = index,
                Text = reading(line),
                IsAthetized = false,

                // Per line, not per text. This corpus is mostly verse but not
                // entirely, and Aucassin et Nicolette alternates the two by
                // design - it is a chantefable, sung verse against spoken
                // prose, and recording it as one or the other would be wrong
                // half the time.
                IsVerse = line.IsVerse
            })
            .Where(n => n.Text.Length > 0)
            .ToList();

        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
        LinesInstalled += nodes.Count;
    }

    /// <summary>
    /// The title, with its date where there is one. The corpus spans six
    /// centuries and a library tree sorted by title alone puts a ninth-century
    /// sequence between two fifteenth-century farces.
    /// </summary>
    private static string Title(BfmTextEntry entry)
    {
        var title = string.IsNullOrWhiteSpace(entry.Title) ? entry.FileName : entry.Title.Trim();
        return entry.Year is { } year ? $"{title} ({year})" : title;
    }

    private static string Slug(string value)
    {
        var slug = new string(value
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
