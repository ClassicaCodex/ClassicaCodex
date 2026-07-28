using System.Formats.Tar;
using System.IO.Compression;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Loads Princeton's WordNet as the English lemma and dictionary data, so
/// the translations already in the library get the same Word Study and
/// lemma-aware search that the Greek and Latin originals have.
///
/// WordNet is shaped differently from the classical lemma corpora and that
/// drives the design here. Those ship an explicit row per attested form,
/// because Greek and Latin inflection is large and irregular enough that
/// listing it is the only workable approach. WordNet lists base forms plus
/// a short exception file for irregular inflections, and leaves regular
/// endings to be stripped by rule at lookup time - see EnglishLemmatizer.
/// So this writes one row per base form and one per irregular exception,
/// and does not attempt to generate "walked", "walking", "walks" itself.
///
/// Licensing is clean: Princeton grants permission to use, copy, modify
/// and distribute the database for any purpose without fee, provided the
/// copyright notice travels with it, which the About screen carries.
/// </summary>
public class WordNetIngestService
{
    private const string DownloadUrl = "https://wordnetcode.princeton.edu/wn3.1.dict.tar.gz";

    // The four word classes WordNet splits its files by. The letter is what
    // appears in the index files' pos column and in the file names.
    private static readonly (string FileSuffix, string PosCode, string PosName)[] WordClasses =
    {
        ("noun", "n", "noun"),
        ("verb", "v", "verb"),
        ("adj", "a", "adjective"),
        ("adv", "r", "adverb")
    };

    public async Task IngestAsync(string destinationRoot, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        var dictDir = await EnsureFilesAsync(destinationRoot, progress, cancellationToken);

        var lemmas = new List<Lemma>();
        var definitions = new List<Definition>();
        var seenLemmaKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (fileSuffix, posCode, posName) in WordClasses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dataPath = Path.Combine(dictDir, $"data.{fileSuffix}");
            var indexPath = Path.Combine(dictDir, $"index.{fileSuffix}");
            if (!File.Exists(dataPath) || !File.Exists(indexPath))
            {
                progress.Report($"Skipping {posName} - files not found in {dictDir}.");
                continue;
            }

            progress.Report($"Reading {posName} definitions...");
            var glossesByOffset = await ReadGlossesAsync(dataPath, cancellationToken);

            progress.Report($"Reading {posName} entries...");
            await foreach (var line in ReadDataLinesAsync(indexPath, cancellationToken))
            {
                // index format: lemma pos synset_cnt p_cnt [ptrs...] sense_cnt
                // tagsense_cnt offset [offset...]
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 3) continue;

                // WordNet joins multi-word entries with underscores; restore
                // the spaces so they read normally and match the text.
                var lemma = fields[0].Replace('_', ' ');

                // One Lemmas row per base form. Regular inflections are
                // handled by EnglishLemmatizer at lookup time rather than
                // enumerated here.
                AddLemma(lemmas, seenLemmaKeys, form: lemma, headword: lemma, posName);

                // Trailing fields are synset offsets, one per sense. Walk
                // back from the end rather than parsing the pointer block,
                // whose length varies per entry.
                var senseCount = ParseIntOrZero(fields[2]);
                if (senseCount <= 0) continue;

                var offsets = fields.Skip(Math.Max(fields.Length - senseCount, 0));
                var senses = new List<string>();
                foreach (var offsetText in offsets)
                {
                    if (glossesByOffset.TryGetValue(offsetText, out var gloss)) senses.Add(gloss);
                }

                if (senses.Count == 0) continue;

                var entry = senses.Count == 1
                    ? $"({posName}) {senses[0]}"
                    : $"({posName})\r\n" + string.Join("\r\n",
                        senses.Select((s, i) => $"{i + 1}. {s}"));

                definitions.Add(new Definition
                {
                    Headword = lemma,
                    NormalizedHeadword = WordNormalizer.NormalizeHeadword(lemma, "eng"),
                    Language = "eng",
                    Entry = entry,
                    Source = "WordNet"
                });
            }

            // Irregular inflections - the forms no rule can derive.
            var excPath = Path.Combine(dictDir, $"{fileSuffix}.exc");
            if (File.Exists(excPath))
            {
                progress.Report($"Reading {posName} irregular forms...");
                await foreach (var line in ReadDataLinesAsync(excPath, cancellationToken))
                {
                    // exc format: inflected_form base_form [base_form...]
                    var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 2) continue;

                    var inflected = fields[0].Replace('_', ' ');
                    foreach (var baseForm in fields.Skip(1))
                    {
                        AddLemma(lemmas, seenLemmaKeys, inflected, baseForm.Replace('_', ' '), posName);
                    }
                }
            }

            progress.Report($"{posName}: {lemmas.Count:N0} forms, {definitions.Count:N0} entries so far.");
        }

        if (lemmas.Count == 0)
        {
            throw new InvalidOperationException(
                $"No WordNet entries were read from {dictDir}. Expected files like index.noun and data.noun there.");
        }

        progress.Report($"Saving {lemmas.Count:N0} English forms...");
        await new LemmaRepository().BulkInsertAsync(lemmas, cancellationToken);

        progress.Report($"Saving {definitions.Count:N0} English definitions...");
        await new DefinitionRepository().BulkInsertAsync(definitions, cancellationToken);

        progress.Report($"Done - {lemmas.Count:N0} forms, {definitions.Count:N0} definitions.");
    }

    private static void AddLemma(
        List<Lemma> lemmas, HashSet<string> seen, string form, string headword, string posName)
    {
        var normalized = WordNormalizer.Normalize(form);
        if (normalized.Length == 0) return;

        // The same form can be listed under several word classes; keep each
        // distinct combination, since which class it is affects the parse.
        var key = $"{normalized}\u0001{headword}\u0001{posName}";
        if (!seen.Add(key)) return;

        lemmas.Add(new Lemma
        {
            Form = form,
            NormalizedForm = normalized,
            Headword = headword,
            Language = "eng",
            PartOfSpeech = posName
        });
    }

    /// <summary>
    /// Maps each synset offset to its gloss - the definition text, which in
    /// the data files follows a " | " separator at the end of the line.
    /// </summary>
    private static async Task<Dictionary<string, string>> ReadGlossesAsync(
        string dataPath, CancellationToken cancellationToken)
    {
        var glosses = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var line in ReadDataLinesAsync(dataPath, cancellationToken))
        {
            var separator = line.IndexOf('|');
            if (separator < 0) continue;

            var offset = line[..line.IndexOf(' ')];
            var gloss = line[(separator + 1)..].Trim();
            if (offset.Length > 0 && gloss.Length > 0) glosses[offset] = gloss;
        }

        return glosses;
    }

    /// <summary>
    /// Yields the real lines of a WordNet file. Both the index and data
    /// files open with a license header whose lines begin with two spaces -
    /// deliberately, so the binary search WordNet's own tools use skips
    /// them - and those must not be parsed as entries.
    /// </summary>
    private static async IAsyncEnumerable<string> ReadDataLinesAsync(
        string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 || line.StartsWith("  ", StringComparison.Ordinal)) continue;
            yield return line;
        }
    }

    private static int ParseIntOrZero(string text) => int.TryParse(text, out var value) ? value : 0;

    /// <summary>
    /// Ensures the WordNet database files are on disk, downloading and
    /// unpacking them if not, and returns the folder holding them.
    /// Re-running the setup step therefore doesn't re-download.
    /// </summary>
    private static async Task<string> EnsureFilesAsync(
        string destinationRoot, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var existing = FindDictDirectory(destinationRoot);
        if (existing != null)
        {
            progress.Report("WordNet files already present - skipping download.");
            return existing;
        }

        Directory.CreateDirectory(destinationRoot);
        var archivePath = Path.Combine(destinationRoot, "wordnet.tar.gz");

        progress.Report("Downloading WordNet...");
        try
        {
            await new FileDownloadService().DownloadAsync(DownloadUrl, archivePath, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Couldn't download WordNet from {DownloadUrl}: {ex.Message}\r\n\r\n" +
                "If that address has moved, the database files can be fetched manually from " +
                "wordnet.princeton.edu and unpacked into:\r\n" + destinationRoot, ex);
        }

        progress.Report("Unpacking WordNet...");
        await using (var fileStream = File.OpenRead(archivePath))
        await using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, destinationRoot, overwriteFiles: true, cancellationToken);
        }

        // The archive is only needed to unpack; keeping it wastes disk.
        TryDelete(archivePath);

        return FindDictDirectory(destinationRoot)
               ?? throw new InvalidOperationException(
                   $"WordNet unpacked, but no folder containing index.noun was found under {destinationRoot}.");
    }

    /// <summary>
    /// Finds the folder actually holding the database files. The archive
    /// nests them under a versioned directory whose name varies by release,
    /// so this looks for a known file rather than assuming a path.
    /// </summary>
    private static string? FindDictDirectory(string root)
    {
        if (!Directory.Exists(root)) return null;
        if (File.Exists(Path.Combine(root, "index.noun"))) return root;

        return Directory.EnumerateFiles(root, "index.noun", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(d => d != null);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* a leftover archive is harmless - not worth failing the step over */ }
    }
}
