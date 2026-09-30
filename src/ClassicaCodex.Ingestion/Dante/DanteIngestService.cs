using System.Formats.Tar;
using System.IO.Compression;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Dante;

/// <summary>
/// Puts Dante's Commedia into the library, annotated, from the Universal
/// Dependencies Old Italian treebank.
///
/// <b>Not a manuscript collection, and it must not be described as one.</b> The
/// text is Petrocchi's 1994 critical edition, so there is no scribe's spelling
/// here and no second reading - unlike Menota, ReM and Geste, which are all
/// transcriptions. Everything shown to a reader about this step says so.
///
/// It is here for two things the rest of the library cannot offer for Italian.
/// It is completely annotated - a headword, a part of speech and full morphology
/// on all 122,024 words - so Word Study works the moment it finishes. And it is
/// cited as Dante has been cited for seven centuries: Inf. 5.142 is a reference
/// that resolves in any printed edition, which is a firmer citation than
/// anything else in this library has.
///
/// <b>What Italian cannot have.</b> The corpus Old Italian deserves is the
/// Corpus OVI dell'Italiano antico - 3,921 texts, 31.4 million words before 1400,
/// lemmatised and tied to the TLIO dictionary. Its own record at the CNR says
/// there are no files associated with it; it exists only behind a query form.
/// Corpus Taurinense, 259,299 words of lemmatised thirteenth-century Florentine,
/// is the same with a broken website on top. So this is one poem rather than a
/// corpus, and that is not a choice made here - it is the whole of what is
/// openly available.
/// </summary>
public class DanteIngestService
{
    public const string CollectionKey = "dante";
    public const string Namespace = "dante";

    /// <summary>
    /// ISO 639-3 has no separate code for Old Italian - unlike Old French (fro)
    /// and Middle High German (gmh), which is why those collections got codes of
    /// their own. "ita" is the only correct answer, and it is unambiguous here
    /// because nothing modern is in this library to blend with.
    /// </summary>
    public const string Language = "ita";

    /// <summary>
    /// Pinned to a release tag. Universal Dependencies ships twice a year and
    /// revises its annotation between releases; the sent_id values have visibly
    /// changed across versions, and citations here are derived from the Canto and
    /// Verso fields, so a reader's bookmarks are only as stable as the release
    /// they were made against.
    /// </summary>
    public const string Release = "r2.18";

    public static string ArchiveUrl =>
        $"https://codeload.github.com/UniversalDependencies/UD_Italian-Old/tar.gz/refs/tags/{Release}";

    public const string ArchiveFileName = "UD_Italian-Old.tar.gz";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();

    public int VersesInstalled { get; private set; }
    public long MappingsLoaded { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = await EnsureExtractedAsync(root, progress, cancellationToken);

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                $"No CoNLL-U files were found under {root}. This step needs the Universal Dependencies "
                + "Old Italian treebank, which ships three of them.");
        }

        // All three splits together. They are a machine-learning partition and
        // the split cuts through cantos, so reading one would give a Commedia in
        // pieces with about a fifth of its verses missing.
        progress?.Report($"Reading {files.Count} treebank files...");
        var canticles = DanteTextLoader.Load(files);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:dante:alighieri",
            Name = "Dante Alighieri",
            Namespace = Namespace,
            Language = Language
        }, cancellationToken);

        var lemmas = new List<Lemma>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var canticle in canticles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"{canticle.Name}: {canticle.Verses.Count:N0} verses...");

            await InstallAsync(authorId, canticle, cancellationToken);
            VersesInstalled += canticle.Verses.Count;

            foreach (var word in canticle.Verses.SelectMany(v => v.Words))
            {
                var normalized = WordNormalizer.Normalize(word.Form);
                if (normalized.Length == 0) continue;

                var key = $"{normalized}\u0001{word.Headword}\u0001{word.Tag}";
                if (!seen.Add(key)) continue;

                lemmas.Add(new Lemma
                {
                    Form = word.Form,
                    NormalizedForm = normalized,
                    Headword = word.Headword,
                    Language = Language,
                    PartOfSpeech = word.Tag
                });
            }
        }

        if (lemmas.Count > 0)
        {
            await _lemmaRepo.ClearByLanguageAsync(Language, cancellationToken);
            await _lemmaRepo.BulkInsertAsync(lemmas, cancellationToken);
            MappingsLoaded = lemmas.Count;
        }

        return IngestOutcome.From(Array.Empty<(string, string)>(), attempted: files.Count);
    }

    /// <summary>
    /// One work per canticle, which is how the poem is read and cited - nobody
    /// cites the Commedia as a single run of 14,233 lines.
    /// </summary>
    private async Task InstallAsync(int authorId, DanteCanticle canticle, CancellationToken cancellationToken)
    {
        var slug = canticle.Name.ToLowerInvariant();

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = $"urn:dante:commedia:{slug}",

            // Numbered, because the library tree orders an author's works by
            // title and there is no sort column to order them by anything else.
            // Bare names would list the poem as Inferno, Paradiso, Purgatorio -
            // alphabetical, and wrong in a way any reader of Dante sees at once.
            // The numeral also says that the three are one poem, which the bare
            // names leave the reader to know already.
            Title = $"Commedia {RomanNumeral(canticle.Name)}: {canticle.Name}",
            CitationScheme = "Canto.Verso"
        }, cancellationToken);

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:dante:commedia:{slug}:petrocchi",
            Kind = EditionKind.Original,
            Language = Language,

            // No Orthography. The two-reading split the manuscript collections
            // use says something true about them - the scribe's spelling against
            // a modern reading - and there is no such distinction in a critical
            // edition. Setting one here would imply a manuscript layer that does
            // not exist.
            Orthography = null,
            Translator = null,

            // The poem's own address rather than a file path, because there is
            // no one file behind a canticle: the treebank splits it across
            // train, dev and test and this text is the three merged.
            SourcePath = $"urn:dante:commedia:{slug}",

            // Set here, and this is load-bearing. StampCollectionAsync labels
            // editions whose SourcePath sits under the download folder, and
            // these do not - so the stamp matches nothing and only records that
            // the step finished. Without this line the editions would carry no
            // collection at all and Dante would be missing from every collection
            // filter in the application while sitting perfectly well in the
            // library tree.
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        await _textNodeRepo.BulkInsertAsync(
            canticle.Verses.Select((v, i) => new TextNode
            {
                EditionId = editionId,
                CitationRef = $"{v.Canto}.{v.Verso}",
                SortOrder = i + 1,
                Text = v.Text,
                IsAthetized = false,
                IsVerse = true
            }).ToList(),
            cancellationToken);
    }

    private static string RomanNumeral(string canticle) => canticle switch
    {
        "Inferno" => "I",
        "Purgatorio" => "II",
        "Paradiso" => "III",
        _ => "IV"
    };

    /// <summary>
    /// Extracts the archive once and returns the CoNLL-U files in it.
    /// </summary>
    private static async Task<List<string>> EnsureExtractedAsync(
        string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);

        var already = Directory.GetFiles(root, "*.conllu", SearchOption.AllDirectories);
        if (already.Length > 0) return already.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        var archive = Directory.GetFiles(root, "*.tar.gz").FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"The treebank archive was not found in {root}. Expected {ArchiveFileName}.");

        progress?.Report("Extracting the treebank...");

        await using (var file = File.OpenRead(archive))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, root, overwriteFiles: true, cancellationToken);
        }

        return Directory.GetFiles(root, "*.conllu", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
