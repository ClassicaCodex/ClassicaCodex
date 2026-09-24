using System.IO.Compression;
using System.Text;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.ReM;

/// <summary>
/// Puts the Reference Corpus of Middle High German into the library.
///
/// ReM is 406 texts, 1050-1350, transcribed and annotated by hand at Bochum and
/// Bonn. A ReM text is a work in one manuscript - a witness rather than a work,
/// so the Rolandslied is seven of them - and each is filed as its own work here,
/// under its own ReM identifier. Grouping the witnesses would mean splitting
/// titles like "Pfaffe Konrad: 'Rolandslied' (P)" on punctuation that also
/// occurs inside real titles, and a wrong grouping is harder to see than an
/// ungrouped one.
///
/// <b>Two editions per text.</b> ReM gives every word both as the scribe wrote
/// it and as a normalised reading, and the two are worth having side by side -
/// which is what this library is for. They go in as two editions of the one
/// work, so the edition dropdown switches between them.
///
/// <b>What is deliberately not read.</b> ReM's real annotation - lemma, part of
/// speech, morphology, and the Mittelhochdeutsches Wörterbuch identifiers - is
/// not in the TEI export at all. It is in the Tabular JSON, which nothing here
/// reads. The TEI's @lemma attribute is a false friend: it holds the normalised
/// form, and ReM's own release notes say so. Nothing in this service writes to
/// the Lemmas table, because a headword column filled with inflected forms
/// would make Word Study look right and be wrong. Word Study gates itself on
/// lemma rows existing for a language, so it stays off for Middle High German
/// until a loader that reads the real annotation exists.
/// </summary>
public class ReMIngestService
{
    /// <summary>
    /// The collection key, the namespace and the language code. ISO 639-3 gmh
    /// is Middle High German, which is what ReM's own TEI declares on every
    /// langUsage element.
    /// </summary>
    public const string CollectionKey = "rem";

    public const string Namespace = "rem";
    public const string Language = "gmh";

    /// <summary>
    /// Pinned to a version rather than following the concept DOI, which always
    /// resolves to the newest release.
    ///
    /// ReM renumbers between versions - 2.0 merged two pairs of texts, split
    /// another into four, and removed one outright. Bookmarks and tags are kept
    /// against (edition, citation), so a text whose line numbering shifts under
    /// them detaches a reader's marks silently. Following "latest" would do that
    /// on a day nobody chose. Moving to a new version should be a deliberate
    /// edit here, with the renumbering understood.
    /// </summary>
    public const string ArchiveUrl =
        "https://zenodo.org/records/13982324/files/ReM-v2.1_tei.zip?download=1";

    public const string ArchiveFileName = "ReM-v2.1_tei.zip";
    public const string Version = "2.1";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    public int TextsInstalled { get; private set; }
    public int LinesInstalled { get; private set; }

    /// <summary>
    /// Extracts the archive if it has not been extracted, then reads every text
    /// in it.
    ///
    /// Per file rather than all-or-nothing, the same way the Perseus ingest
    /// works: one unreadable text out of 406 must not throw away the other 405,
    /// and the ones it could not read are named in the outcome rather than
    /// quietly missing.
    /// </summary>
    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var teiFolder = await EnsureExtractedAsync(root, progress, cancellationToken);

        var files = Directory.GetFiles(teiFolder, "*.xml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                $"No ReM text files were found under {teiFolder}. The archive should extract to a "
                + "folder of numbered XML files (M001.xml and so on).");
        }

        var skipped = new List<(string FilePath, string Error)>();

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            progress?.Report($"Reading {Path.GetFileNameWithoutExtension(file)} ({i + 1} of {files.Count})...");

            try
            {
                var text = ReMTextLoader.Load(file);

                // A text with no readable lines is a problem to report, not a
                // work to create. An edition with nothing in it looks installed
                // and opens empty.
                if (text.Lines.Count == 0)
                {
                    skipped.Add((file, "no lines could be read from this text"));
                    continue;
                }

                await InstallAsync(text, file, cancellationToken);
                TextsInstalled++;
                LinesInstalled += text.Lines.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped.Add((file, ex.Message));
            }
        }

        return IngestOutcome.From(skipped, attempted: files.Count);
    }

    private async Task InstallAsync(ReMText text, string sourcePath, CancellationToken cancellationToken)
    {
        var authorName = text.Author ?? "Anonymous";
        var authorSlug = Slug(authorName);
        var textSlug = Slug(text.Id);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = $"urn:rem:{authorSlug}",
            Name = authorName,
            Namespace = Namespace,
            Language = Language
        }, cancellationToken);

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = $"urn:rem:{authorSlug}:{textSlug}",
            Title = WorkTitle(text),
            CitationScheme = text.CitationScheme
        }, cancellationToken);

        await InstallEditionAsync(
            workId, authorSlug, textSlug, "dipl", "diplomatic", sourcePath,
            text.Lines.Select(l => (l.CitationRef, l.SortOrder, l.Diplomatic)), cancellationToken);

        await InstallEditionAsync(
            workId, authorSlug, textSlug, "norm", "normalised", sourcePath,
            text.Lines.Select(l => (l.CitationRef, l.SortOrder, l.Normalised)), cancellationToken);
    }

    private async Task InstallEditionAsync(
        int workId, string authorSlug, string textSlug, string urnSuffix, string orthography,
        string sourcePath, IEnumerable<(string CitationRef, int SortOrder, string Text)> lines,
        CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:rem:{authorSlug}:{textSlug}:{urnSuffix}",
            Kind = EditionKind.Original,
            Language = Language,
            Translator = null,
            SourcePath = sourcePath,
            Orthography = orthography,
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        await _textNodeRepo.BulkInsertAsync(
            lines.Select(l => new TextNode
            {
                EditionId = editionId,
                CitationRef = l.CitationRef,
                SortOrder = l.SortOrder,
                Text = l.Text,
                IsAthetized = false
            }).ToList(),
            cancellationToken);
    }

    /// <summary>
    /// The manuscript is part of the title, because it is part of what the text
    /// is. ReM's own titles already carry a witness letter for the texts that
    /// have siblings - "Rolandslied (P)" - but the shelfmark says which book
    /// that is, and two works called "Predigten" from different libraries are
    /// otherwise indistinguishable in the library tree.
    /// </summary>
    private static string WorkTitle(ReMText text)
    {
        if (text.Shelfmark == null && text.Repository == null) return text.Title;

        var manuscript = string.Join(", ",
            new[] { text.Repository, text.Shelfmark }.Where(p => !string.IsNullOrWhiteSpace(p)));

        return $"{text.Title} ({manuscript})";
    }

    /// <summary>
    /// Extracts once. A re-run over an already-extracted folder skips straight
    /// to reading, which matters because the archive is 27 MB compressed and
    /// 190 MB out, and re-running a step is the ordinary way to recover from a
    /// half-finished one.
    /// </summary>
    private static async Task<string> EnsureExtractedAsync(
        string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);

        var existing = Directory.GetDirectories(root, "tei", SearchOption.AllDirectories).FirstOrDefault();
        if (existing != null && Directory.GetFiles(existing, "*.xml").Length > 0) return existing;

        var archive = Directory.GetFiles(root, "*.zip").FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"The ReM archive was not found in {root}. Expected {ArchiveFileName}.");

        progress?.Report("Extracting the corpus...");

        await Task.Run(() =>
        {
            // Overwriting rather than failing on an existing entry: a previous
            // run cancelled mid-extract leaves some of the files in place, and
            // the recovery for that should be running the step again.
            using var zip = ZipFile.OpenRead(archive);

            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;

                var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

                // An archive entry naming its way out of the folder it is being
                // extracted into is how a zip writes to somewhere it was never
                // pointed at. ReM's archive does not do this; the check is here
                // because it costs nothing and the alternative is trusting a
                // downloaded file about where it should be written.
                if (!target.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The archive entry {entry.FullName} points outside {root}.");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }, cancellationToken);

        return Directory.GetDirectories(root, "tei", SearchOption.AllDirectories).FirstOrDefault()
            ?? root;
    }

    /// <summary>
    /// A URN segment: lowercase, letters and digits only. ReM identifiers are
    /// already of that shape (M121Y); author names are not (Pfaffe Konrad).
    /// </summary>
    internal static string Slug(string value)
    {
        var slug = new StringBuilder(value.Length);

        foreach (var ch in value.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(ch)) slug.Append(char.ToLowerInvariant(ch));
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }

        return slug.ToString().Trim('-') is { Length: > 0 } cleaned ? cleaned : "unnamed";
    }
}
