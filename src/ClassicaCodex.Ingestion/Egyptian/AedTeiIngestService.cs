using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Egyptian;

/// <summary>
/// Puts Egyptian texts from the Thesaurus Linguae Aegyptiae into the library:
/// the Book of the Dead, the medical papyri, the literature, the letters and
/// the late funerary and temple papyri, in transliteration with the TLA's
/// German translation beside them, and in hieroglyphs where the TLA encoded
/// them.
///
/// The source is AED-TEI, Simon Schweitzer's TEI conversion of the January
/// 2018 snapshot of the Berlin-Leipzig dictionary project's database, released
/// under CC BY-SA 4.0 - the same licence as the Pyramid Texts already here.
///
/// <b>What a work is.</b> One document: a papyrus, a stela, a letter. The
/// Papyrus of Nu is a work, and its spells are passages grouped under their
/// spell numbers. See <see cref="AedTeiLoader"/> for why the wall-by-wall
/// subcorpora are left out.
///
/// <b>How a passage is cited.</b> By the text's number within its document and
/// the sentence's number within the text - for the Book of the Dead, the spell
/// number itself, so the twelfth sentence of spell 125 in the Papyrus of Nu is
/// 125.12. The text's own TLA title ("Tb 125", "Eb 185") is the passage's
/// milestone, which is how it is cited in print.
/// </summary>
public class AedTeiIngestService
{
    public const string CollectionKey = "tla-egyptian";
    public const string Namespace = "egy";
    public const string Language = "egy";
    public const string TranslationLanguage = "deu";

    /// <summary>
    /// The dictionary's entries are stored under this name, so a re-run
    /// replaces them and nothing else (LSJ and Lewis and Short have their own).
    /// </summary>
    public const string DefinitionSource = "TLA dictionary (AED-TEI)";

    /// <summary>
    /// Pinned to the release tag. The repository has one, v1.0, and the TLA
    /// snapshot behind it is fixed (January 2018), so the citations built here
    /// are stable for as long as the tag is.
    /// </summary>
    public const string Release = "v1.0";

    public static string ArchiveUrl =>
        $"https://codeload.github.com/simondschweitzer/aed-tei/tar.gz/refs/tags/{Release}";

    public const string ArchiveFileName = "aed-tei-1.0.tar.gz";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();

    public int WorksInstalled { get; private set; }
    public int TextsInstalled { get; private set; }
    public int SentencesInstalled { get; private set; }
    public int MappingsLoaded { get; private set; }
    public int DefinitionsLoaded { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var release = await EnsureExtractedAsync(root, progress, cancellationToken);

        progress?.Report("Reading the TLA texts...");
        var problems = new List<(string Text, string Problem)>();
        var texts = AedTeiLoader.Load(release, progress, problems);
        if (texts.Count == 0)
        {
            throw new InvalidDataException(
                $"No texts were found in {release}. This step needs the AED-TEI release, which holds "
                + "a concordance file and a folder of TEI files.");
        }

        progress?.Report("Reading the dictionary...");
        var dictionary = AedTeiLoader.ReadDictionary(Path.Combine(release, "files", AedTeiLoader.DictionaryFileName));

        var groups = texts
            .GroupBy(t => (t.Subcorpus, t.ObjectName))
            .OrderBy(g => g.Key.Subcorpus, StringComparer.Ordinal)
            .ThenBy(g => AedTeiLoader.NaturalKey(g.Key.ObjectName), StringComparer.Ordinal)
            .ToList();

        var authorIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedSlugs = new HashSet<string>(StringComparer.Ordinal);
        // The release has files that are not well-formed XML: a text whose base
        // file is broken is skipped, and one whose hieroglyph or translation
        // file is broken arrives without that layer. Both are reported, so the
        // setup wizard's skip report says what is missing and why.
        var failed = new List<(string, string)>(problems);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (subcorpus, objectName) = group.Key;

            try
            {
                if (!authorIds.TryGetValue(subcorpus, out var authorId))
                {
                    authorId = await _authorRepo.UpsertAsync(new Author
                    {
                        // Grouped by the TLA's own subcorpora - "Book of the
                        // Dead", "Medical papyri" - which is how these texts are
                        // known; none has an author in the sense a Greek text does.
                        CtsUrn = $"urn:egy:tla:{subcorpus}",
                        Name = AedTeiLoader.Subcorpora[subcorpus],
                        Namespace = Namespace,
                        Language = Language
                    }, cancellationToken);
                    authorIds[subcorpus] = authorId;
                }

                await InstallAsync(authorId, subcorpus, objectName, group.ToList(), usedSlugs, cancellationToken);
                WorksInstalled++;
                if (WorksInstalled % 100 == 0)
                    progress?.Report($"{WorksInstalled:N0} documents, {SentencesInstalled:N0} passages...");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add(($"{subcorpus}: {objectName}", ex.GetBaseException().Message));
            }
        }

        await LoadDictionaryAsync(texts, dictionary, cancellationToken);

        progress?.Report(
            $"{WorksInstalled:N0} documents, {TextsInstalled:N0} texts, {SentencesInstalled:N0} passages, "
            + $"{MappingsLoaded:N0} new word-form mappings, {DefinitionsLoaded:N0} dictionary entries.");

        return IngestOutcome.From(failed, attempted: groups.Count);
    }

    private async Task InstallAsync(
        int authorId, string subcorpus, string objectName, List<AedText> texts,
        HashSet<string> usedSlugs, CancellationToken cancellationToken)
    {
        var ordered = Order(subcorpus, texts);
        var slug = UniqueSlug(subcorpus, objectName, usedSlugs);
        var workUrn = $"urn:egy:tla:{subcorpus}:{slug}";

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = workUrn,
            Title = objectName,
            CitationScheme = subcorpus == "tb" ? "Spell.Sentence" : "Text.Sentence"
        }, cancellationToken);

        var rows = new List<(string Citation, string Milestone, AedSentence Sentence)>();
        foreach (var (number, text) in ordered)
        {
            for (var i = 0; i < text.Sentences.Count; i++)
                rows.Add(($"{number}.{i + 1}", text.Title, text.Sentences[i]));
            TextsInstalled++;
        }

        SentencesInstalled += rows.Count;
        var editors = texts.SelectMany(t => t.Editors).Distinct().ToList();

        await InstallEditionAsync(workId, $"{workUrn}:translit", EditionKind.Original, Language,
            "transliteration", null, rows, r => r.Sentence.Transliteration, cancellationToken);

        // Hieroglyphs only where the TLA encoded them for most of the document:
        // a sign edition that is blank for most passages would look broken
        // rather than incomplete.
        if (rows.Count(r => r.Sentence.Hieroglyphs != null) * 2 >= rows.Count)
        {
            await InstallEditionAsync(workId, $"{workUrn}:hiero", EditionKind.Original, Language,
                "hieroglyphs", null, rows, r => r.Sentence.Hieroglyphs ?? string.Empty, cancellationToken);
        }

        if (rows.Any(r => r.Sentence.German != null))
        {
            await InstallEditionAsync(workId, $"{workUrn}:deu", EditionKind.Translation, TranslationLanguage,
                null, (editors.Count > 0 ? string.Join(", ", editors.Take(4)) : "Thesaurus Linguae Aegyptiae") + " (German, TLA)",
                rows, r => r.Sentence.German ?? string.Empty, cancellationToken);
        }
    }

    private async Task InstallEditionAsync(
        int workId, string editionUrn, EditionKind kind, string language, string? orthography, string? translator,
        List<(string Citation, string Milestone, AedSentence Sentence)> rows,
        Func<(string Citation, string Milestone, AedSentence Sentence), string> reading,
        CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = editionUrn,
            Kind = kind,
            Language = language,
            Translator = translator,
            SourcePath = editionUrn,
            Collection = CollectionKey,

            // Named for the same reason as the Pyramid Texts: the edition
            // dropdown needs the two Egyptian readings told apart, and naming
            // the script keeps both out of stylometry, where signs are not
            // words and a transliteration's spelling conventions are the
            // editors', not the scribe's.
            Orthography = orthography
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        var nodes = rows
            .Select((r, i) => new TextNode
            {
                EditionId = editionId,
                CitationRef = r.Citation,
                SortOrder = i + 1,
                Text = reading(r),
                Milestone = r.Milestone,
                IsAthetized = false,
                IsVerse = false
            })
            .Where(n => n.Text.Length > 0)
            .ToList();

        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
    }

    /// <summary>
    /// The texts of one document in reading order, each with the number its
    /// passages are cited under. For the Book of the Dead that is the spell
    /// number, so long as each spell occurs once in the manuscript (a second
    /// copy is "28-2"); anything else is numbered in order of its title.
    /// </summary>
    public static List<(string Number, AedText Text)> Order(string subcorpus, List<AedText> texts)
    {
        var sorted = texts
            .OrderBy(t => AedTeiLoader.NaturalKey(t.Title), StringComparer.Ordinal)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        if (subcorpus == "tb")
        {
            var spells = sorted.Select(t => (Text: t, Spell: AedTeiLoader.SpellNumber(t.Title))).ToList();

            // Most texts in a Book of the Dead papyrus are spells; a few are a
            // hymn or a vignette caption copied alongside. If most are spells,
            // the spells are cited by number - in number order, as they are
            // read and compared, and a second copy of one in the same papyrus
            // is "28-2" - and the rest follow as T1, T2. A papyrus that is
            // mostly something else is simply numbered in order.
            if (spells.Count(s => s.Spell != null) * 2 >= spells.Count)
            {
                var result = new List<(string, AedText)>();
                var used = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var (text, spell) in spells
                             .Where(s => s.Spell != null)
                             .OrderBy(s => int.Parse(new string(s.Spell!.TakeWhile(char.IsDigit).ToArray())))
                             .ThenBy(s => s.Spell, StringComparer.Ordinal))
                {
                    used[spell!] = used.TryGetValue(spell!, out var n) ? n + 1 : 1;
                    result.Add((used[spell!] == 1 ? spell! : $"{spell}-{used[spell!]}", text));
                }

                var other = 0;
                foreach (var (text, _) in spells.Where(s => s.Spell == null))
                    result.Add(($"T{++other}", text));

                return result;
            }
        }

        return sorted.Select((t, i) => ((i + 1).ToString(), t)).ToList();
    }

    private static readonly Regex NotSlug = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// A readable, stable identifier for a document: its name folded to ASCII
    /// ("pLondon BM EA 10477 (pNu)" is "plondon-bm-ea-10477-pnu"), numbered if
    /// two documents in a subcorpus fold to the same thing.
    /// </summary>
    public static string UniqueSlug(string subcorpus, string objectName, HashSet<string> used)
    {
        var folded = new StringBuilder();
        foreach (var ch in objectName.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                folded.Append(char.ToLowerInvariant(ch));
        }

        var slug = NotSlug.Replace(folded.ToString(), "-").Trim('-');
        if (slug.Length == 0) slug = "document";
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');

        var candidate = slug;
        for (var n = 2; !used.Add($"{subcorpus}:{candidate}"); n++) candidate = $"{slug}-{n}";
        return candidate;
    }

    /// <summary>
    /// Word Study's two tables for Egyptian: every word form mapped to its
    /// dictionary headword, and the dictionary's German meaning for each
    /// headword the texts use.
    /// </summary>
    private async Task LoadDictionaryAsync(
        List<AedText> texts, Dictionary<string, AedDictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        var lemmas = new List<Lemma>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var word in texts.SelectMany(t => t.Sentences).SelectMany(s => s.Words))
        {
            if (word.LemmaId == null || !dictionary.TryGetValue(word.LemmaId, out var entry)) continue;

            var normalized = WordNormalizer.Normalize(word.Form);
            if (normalized.Length == 0) continue;

            used.Add(entry.Id);
            if (!seen.Add($"{word.Form}\u0001{entry.Headword}\u0001{entry.PartOfSpeech}")) continue;

            lemmas.Add(new Lemma
            {
                Form = word.Form,
                NormalizedForm = normalized,
                Headword = entry.Headword,
                Language = Language,
                PartOfSpeech = entry.PartOfSpeech
            });
        }

        MappingsLoaded = await _lemmaRepo.InsertMissingAsync(Language, lemmas, cancellationToken);

        var definitions = used
            .Select(id => dictionary[id])
            .Where(e => !string.IsNullOrEmpty(e.German))
            .Select(e => new Definition
            {
                Headword = e.Headword,
                NormalizedHeadword = WordNormalizer.Normalize(e.Headword),
                Language = Language,
                Entry = e.Reference != null ? $"{e.German} ({e.Reference})" : e.German!,
                Source = DefinitionSource
            })
            .Where(d => d.NormalizedHeadword.Length > 0)
            .ToList();

        await _definitionRepo.ClearBySourceAsync(DefinitionSource, cancellationToken);
        await _definitionRepo.BulkInsertAsync(definitions, cancellationToken);
        DefinitionsLoaded = definitions.Count;
    }

    /// <summary>
    /// Extracts the release once, and returns the folder that holds its
    /// concordance and <c>files</c> folder (the archive wraps them in
    /// <c>aed-tei-1.0</c>).
    /// </summary>
    private static async Task<string> EnsureExtractedAsync(
        string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);

        var found = FindRelease(root);
        if (found != null) return found;

        var archive = Directory.GetFiles(root, "*.tar.gz").FirstOrDefault()
            ?? throw new FileNotFoundException($"The AED-TEI archive was not found in {root}. Expected {ArchiveFileName}.");

        progress?.Report("Extracting the TLA corpus (about 56,000 files)...");
        await using (var file = File.OpenRead(archive))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, root, overwriteFiles: true, cancellationToken);
        }

        return FindRelease(root)
            ?? throw new InvalidDataException($"The archive in {root} did not contain {AedTeiLoader.ConcordanceFileName}.");
    }

    public static string? FindRelease(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, AedTeiLoader.ConcordanceFileName, SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(d => d != null && Directory.Exists(Path.Combine(d, "files")))
            : null;
}
