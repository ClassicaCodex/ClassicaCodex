using System.IO.Compression;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.ReM;

public record ReMLemmaProgress(string CurrentFile, int FilesProcessed, int TotalFiles, long MappingsLoaded);

/// <summary>
/// Loads ReM's own annotation as lemma data, so Middle High German gets Word
/// Study, the morphology search and vocabulary profiles.
///
/// <b>This is the first collection in the library that brings its own.</b>
/// Greek, Latin and English each need a separate lemma download from a
/// different project, and the Latin one is two gigabytes. ReM annotated its own
/// corpus, so the mappings come from the same 406 texts the reader is reading -
/// which also means they cover it completely rather than approximately.
///
/// Read straight out of the archive rather than extracted first. The JSON is
/// 158 MB compressed and 1.35 GB out, and nothing needs it on disk twice: only
/// five fields of each token are wanted and they are read once.
/// </summary>
public class ReMLemmaIngestService
{
    /// <summary>
    /// Pinned to the same release the texts come from. A lemma set built from a
    /// different version than the text it is meant to explain would be wrong in
    /// a way nothing would report - see ReMIngestService for what ReM changes
    /// between versions.
    /// </summary>
    public const string ArchiveUrl =
        "https://zenodo.org/records/13982324/files/ReM-v2.1_json.zip?download=1";

    public const string ArchiveFileName = "ReM-v2.1_json.zip";

    private readonly LemmaRepository _lemmaRepo = new();

    public long MappingsLoaded { get; private set; }

    /// <summary>
    /// How many annotated tokens carried a Mittelhochdeutsches Wörterbuch
    /// identifier. Counted in tokens, not in stored rows: the rows are
    /// deduplicated across the corpus and this is a statement about how
    /// completely the corpus is keyed to the dictionary, which only means
    /// anything against the number of tokens.
    /// </summary>
    public long WithDictionaryId { get; private set; }

    /// <summary>Annotated tokens read, whether or not they produced a new row.</summary>
    public long TokensRead { get; private set; }

    public async Task IngestAsync(
        string root,
        IProgress<ReMLemmaProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var archive = Directory.GetFiles(root, "*.zip").FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"The ReM annotation archive was not found in {root}. Expected {ArchiveFileName}.");

        // This language's mappings only. The Greek, Latin and English sets are
        // separate multi-minute downloads and re-running this step must not
        // take them with it.
        await _lemmaRepo.ClearByLanguageAsync(ReMIngestService.Language, cancellationToken);

        using var zip = ZipFile.OpenRead(archive);

        var entries = zip.Entries
            .Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(archive)} holds no .json files. This step needs ReM's Tabular JSON "
                + "distribution, which is a different download from the texts.");
        }

        // Deduplicated across the whole corpus, not per file. The same
        // form/headword/parse triple recurs constantly - "der" appears tens of
        // thousands of times - and storing it once is the difference between a
        // few hundred thousand rows and two and a quarter million.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var batch = new List<Lemma>(BatchSize);

        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = entries[i];
            progress?.Report(new ReMLemmaProgress(
                Path.GetFileNameWithoutExtension(entry.Name), i, entries.Count, MappingsLoaded));

            await using var stream = entry.Open();

            foreach (var annotation in ReMLemmaLoader.Read(stream))
            {
                TokensRead++;
                if (annotation.DictionaryId != null) WithDictionaryId++;

                // Both spellings the library holds the text under. Where they
                // normalise to the same string the second is dropped by the
                // dedupe below rather than by a special case here.
                foreach (var form in new[] { annotation.DiplomaticForm, annotation.NormalisedForm })
                {
                    if (form.Length == 0) continue;

                    var normalized = WordNormalizer.Normalize(form);
                    if (normalized.Length == 0) continue;

                    var key = $"{normalized}\u0001{annotation.Headword}\u0001{annotation.Tag}";
                    if (!seen.Add(key)) continue;

                    batch.Add(new Lemma
                    {
                        Form = form,
                        NormalizedForm = normalized,
                        Headword = annotation.Headword,
                        Language = ReMIngestService.Language,
                        PartOfSpeech = annotation.Tag
                    });

                    if (batch.Count >= BatchSize)
                    {
                        await _lemmaRepo.BulkInsertAsync(batch, cancellationToken);
                        MappingsLoaded += batch.Count;
                        batch.Clear();
                    }
                }
            }
        }

        if (batch.Count > 0)
        {
            await _lemmaRepo.BulkInsertAsync(batch, cancellationToken);
            MappingsLoaded += batch.Count;
        }

        progress?.Report(new ReMLemmaProgress("Done", entries.Count, entries.Count, MappingsLoaded));
    }

    private const int BatchSize = 20000;
}
