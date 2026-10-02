using System.Formats.Tar;
using System.IO.Compression;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Egyptian;

/// <summary>
/// Puts the Pyramid Texts into the library, in hieroglyphs and in
/// transliteration, from the Universal Dependencies Egyptian-PC treebank.
///
/// The oldest religious literature that survives anywhere: spells carved into
/// the burial chambers of six pyramids at Saqqara between about 2350 and 2200
/// BCE, so that the king might eat, breathe, rise and cross the sky. 519 of
/// them, from Unas - whose pyramid is the first to carry any writing at all -
/// down to Queen Neith.
///
/// <b>Six witnesses, not six books.</b> These pyramids share spells; §16a is
/// in Unas and in Teti and in Pepi. Each is filed as its own work because each
/// is a monument with its own selection and arrangement, which is how
/// Egyptology cites them - but it means the six are not independent texts, and
/// that has a consequence recorded against <see cref="Edition.Orthography"/>
/// below.
///
/// <b>What this does not have is a translation.</b> The treebank carries the
/// signs, the transliteration and a complete grammatical parse, and no
/// rendering into any modern language. Faulkner's translation and Allen's are
/// both in copyright and there is no free one to pair with this, so a reader
/// who does not read Egyptian gets the apparatus and not the sense. The setup
/// step says so before anything is downloaded.
/// </summary>
public class PyramidTextsIngestService
{
    public const string CollectionKey = "pyramid-texts";
    public const string Namespace = "egy";

    /// <summary>
    /// ISO 639-3 "egy" is Egyptian (Ancient), covering every stage down to
    /// Demotic; Old Egyptian has no code of its own. Nothing else in this
    /// library is recorded under it, so there is nothing for it to blend with.
    /// </summary>
    public const string Language = "egy";

    /// <summary>
    /// Pinned to a release tag, for the reason the Dante step is: Universal
    /// Dependencies ships twice a year and revises annotation between
    /// releases. Citations here are built from the spell and section fields,
    /// and a reader's bookmarks are only as stable as the release they were
    /// made against.
    /// </summary>
    public const string Release = "r2.18";

    public static string ArchiveUrl =>
        $"https://codeload.github.com/UniversalDependencies/UD_Egyptian-PC/tar.gz/refs/tags/{Release}";

    public const string ArchiveFileName = "UD_Egyptian-PC.tar.gz";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();

    public int PyramidsInstalled { get; private set; }
    public int UtterancesInstalled { get; private set; }
    public long MappingsLoaded { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = await EnsureExtractedAsync(root, progress, cancellationToken);

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                $"No CoNLL-U files were found under {root}. This step needs the Universal Dependencies "
                + "Egyptian-PC treebank, which ships three of them.");
        }

        progress?.Report($"Reading {files.Count} treebank files...");
        var witnesses = PyramidTextLoader.Load(files);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            // The spells are anonymous and ancient, and the kings whose
            // pyramids carry them did not write them. "Pyramid Texts" as the
            // library's grouping is what the corpus is actually called; filing
            // six monuments under "Anonymous" would put them among every other
            // anonymous work in the library instead of beside each other.
            CtsUrn = "urn:egy:pyramid-texts",
            Name = "Pyramid Texts",
            Namespace = Namespace,
            Language = Language
        }, cancellationToken);

        var lemmas = new List<Lemma>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var witness in witnesses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"{witness.Title}: {witness.Utterances.Count:N0} passages...");

            await InstallAsync(authorId, witness, cancellationToken);

            PyramidsInstalled++;
            UtterancesInstalled += witness.Utterances.Count;

            foreach (var word in witness.Utterances.SelectMany(u => u.Words))
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

        progress?.Report(
            $"{PyramidsInstalled} pyramids, {UtterancesInstalled:N0} passages, "
            + $"{MappingsLoaded:N0} word-form mappings.");

        return IngestOutcome.From(Array.Empty<(string, string)>(), attempted: files.Count);
    }

    private async Task InstallAsync(
        int authorId, PyramidWitness witness, CancellationToken cancellationToken)
    {
        var slug = witness.King.ToLowerInvariant();

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = $"urn:egy:pyramid-texts:{slug}",
            Title = witness.Title,

            // Spell and section, which is how these are cited: Pyr. §16a is in
            // Sethe's numbering, and the spells Sethe did not number carry
            // Allen's.
            CitationScheme = "Spell.Section"
        }, cancellationToken);

        await InstallEditionAsync(
            workId, witness, "hiero", "hieroglyphs", u => u.Hieroglyphs, cancellationToken);

        await InstallEditionAsync(
            workId, witness, "translit", "transliteration", u => u.Transliteration, cancellationToken);
    }

    private async Task InstallEditionAsync(
        int workId, PyramidWitness witness, string urnSuffix, string orthography,
        Func<PyramidUtterance, string> reading, CancellationToken cancellationToken)
    {
        var slug = witness.King.ToLowerInvariant();

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:egy:pyramid-texts:{slug}:{urnSuffix}",
            Kind = EditionKind.Original,
            Language = Language,
            Translator = null,

            // No single file is behind a pyramid: the treebank splits its
            // sentences across train, dev and test, and this text is the three
            // merged. Same reasoning as the Dante step.
            SourcePath = $"urn:egy:pyramid-texts:{slug}",

            // Set here rather than left to StampCollectionAsync, which labels
            // editions whose SourcePath sits under the download folder. These
            // do not, so without this line they would carry no collection and
            // be missing from every collection filter while sitting perfectly
            // well in the library tree.
            Collection = CollectionKey,

            // <b>Both readings are deliberately kept out of stylometry.</b>
            // The edition query for word-frequency work admits only a null or
            // "normalised" orthography, so naming the script here excludes
            // both - which is the right answer twice over. A string of signs
            // is not word-comparable at all; and the transliteration, though
            // it is perfectly good words, belongs to six witnesses that share
            // their spells, so a Delta between Unas and Teti would measure how
            // many spells the two pyramids have in common and report it as a
            // distance between authors.
            //
            // It also makes the two tell each other apart in the edition
            // dropdown - see EditionLabels - which is the difference between
            // "Egyptian (hieroglyphs)" and two identical entries.
            Orthography = orthography
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        var nodes = witness.Utterances
            .Select((utterance, i) => new TextNode
            {
                EditionId = editionId,
                CitationRef = utterance.Citation,
                SortOrder = i + 1,
                Text = reading(utterance),
                IsAthetized = false,

                // These are spells, in a formal register full of parallelism,
                // and Egyptology sets them as verse. The treebank records no
                // metre and this claims none; it is how they are printed.
                IsVerse = true
            })
            .Where(n => n.Text.Length > 0)
            .ToList();

        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
    }

    /// <summary>
    /// Extracts the archive once and returns the CoNLL-U files that are part
    /// of the release.
    ///
    /// <b>Not every .conllu in the archive is the corpus.</b> The treebank
    /// ships a <c>not-to-release</c> folder holding <c>master.conllu</c>, the
    /// editors' working file: 2,182 more sentences, overlapping the released
    /// ones and not validated for the release. Reading every .conllu under the
    /// folder - which is what the Dante step does, correctly, because that
    /// treebank has no such folder - would give 5,271 sentences instead of
    /// 3,089, a corpus 71% larger than the one the editors published, with
    /// duplicates in it and no error anywhere.
    /// </summary>
    private static async Task<List<string>> EnsureExtractedAsync(
        string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);

        var already = Released(root);
        if (already.Count > 0) return already;

        var archive = Directory.GetFiles(root, "*.tar.gz").FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"The treebank archive was not found in {root}. Expected {ArchiveFileName}.");

        progress?.Report("Extracting the treebank...");

        await using (var file = File.OpenRead(archive))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, root, overwriteFiles: true, cancellationToken);
        }

        return Released(root);
    }

    /// <summary>
    /// The released CoNLL-U files: the train, dev and test splits, and nothing
    /// from the editors' working folder.
    /// </summary>
    public static List<string> Released(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*.conllu", SearchOption.AllDirectories)
                .Where(IsReleased)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

    public static bool IsReleased(string path) =>
        !path.Replace('\\', '/').Contains("/not-to-release/", StringComparison.OrdinalIgnoreCase);
}
