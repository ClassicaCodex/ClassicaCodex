using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Geste;

/// <summary>
/// Puts Geste, the corpus of Old French chansons de geste, into the library.
///
/// 34 files, 43,187 verse lines, 1050s-1300s: Floovant, Otinel in five
/// witnesses, Garin le Lorrain in ten, Aspremont, Fierabras, Huon de Bordeaux,
/// Girart de Vienne, Aymeri de Narbonne. Old French, Anglo-Norman and Walloon.
/// Edited by Jean-Baptiste Camps at the Ecole nationale des chartes.
///
/// <b>Two editions per text</b>, the shape Menota and ReM already use, and here
/// it earns its place more than in either: the transcriptions mark every
/// allograph and every abbreviation, so the diplomatic reading is the
/// manuscript letter for letter - "KJ uolt oı̈r chãcũ ꝺe beau ſẽblãt" - against
/// the editor's "ki volt oïr chancun de beau semblant".
///
/// <b>The annotation is real.</b> 112,506 words carry a Tobler-Lommatzsch
/// headword with CATTEX2009 part of speech and morphology: uolt to voloir,
/// ſ̃t to estre1, oı̈r to öir. Unlike ReM, which needed a second 158 MB archive
/// for this, it is in the same files as the text.
/// </summary>
public class GesteIngestService
{
    public const string CollectionKey = "geste";
    public const string Namespace = "geste";

    /// <summary>
    /// Pinned to a commit, not to a branch or to the Zenodo deposit.
    ///
    /// Not the branch, for the reason ReM is pinned to a version: bookmarks and
    /// tags live on (EditionId, CitationRef), and 17 of the 34 files carry no
    /// line numbers at all, so their citations are ordinals - a text gaining a
    /// line at the top would shift every mark in it.
    ///
    /// Not the Zenodo deposit either, which is the first time this library has
    /// preferred a commit over an archival DOI, so the reason matters. The 2019
    /// deposit (10.5281/zenodo.2630574, md5 d0cf956d3c1a1561fdfc1bf52ce22eab)
    /// has a different and worse layout: a flat xml/ directory of 75 files with
    /// a deprec/ folder inside it, mixing superseded copies with current ones.
    /// The xml_gold and xml_silver split this loader relies on came later. A
    /// commit SHA is as immutable as the DOI and describes the corpus as it is
    /// now arranged.
    /// </summary>
    public const string Commit = "71737a20632383cd5c88d9547ba15ea8feb1f641";

    public static string ArchiveUrl => $"https://codeload.github.com/Jean-Baptiste-Camps/Geste/tar.gz/{Commit}";

    public const string ArchiveFileName = "Geste.tar.gz";

    /// <summary>
    /// Only these two directories. The repository also holds xml_src and, in
    /// other revisions, deprec - further copies of the same transcriptions. A
    /// recursive glob would install several of them as separate works.
    /// </summary>
    private static readonly string[] Directories = { "xml_gold", "xml_silver" };

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();

    public int TextsInstalled { get; private set; }
    public int LinesInstalled { get; private set; }
    public long MappingsLoaded { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var tree = await EnsureExtractedAsync(root, progress, cancellationToken);
        var files = ChooseFiles(tree);

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                $"No Geste text files were found under {tree}. The archive should hold xml_gold and "
                + "xml_silver directories of TEI files.");
        }

        var skipped = new List<(string FilePath, string Error)>();
        var lemmas = new List<Lemma>();
        var seenLemma = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            progress?.Report($"Reading {Path.GetFileNameWithoutExtension(file)} ({i + 1} of {files.Count})...");

            try
            {
                var text = GesteTextLoader.Load(file);

                if (text.Lines.Count == 0)
                {
                    skipped.Add((file, "no verse lines could be read from this text"));
                    continue;
                }

                await InstallAsync(text, file, cancellationToken);
                TextsInstalled++;
                LinesInstalled += text.Lines.Count;

                CollectLemmas(text, lemmas, seenLemma);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped.Add((file, ex.Message));
            }
        }

        // Written once at the end rather than per text: the corpus is small
        // enough to hold, and clearing per language must happen before the
        // first insert rather than between them.
        if (lemmas.Count > 0)
        {
            progress?.Report($"Loading {lemmas.Count:N0} word-form mappings...");

            foreach (var language in lemmas.Select(l => l.Language).Distinct().ToList())
                await _lemmaRepo.ClearByLanguageAsync(language, cancellationToken);

            await _lemmaRepo.BulkInsertAsync(lemmas, cancellationToken);
            MappingsLoaded = lemmas.Count;
        }

        return IngestOutcome.From(skipped, attempted: files.Count);
    }

    /// <summary>
    /// One file per text, choosing between the variants a text may have.
    ///
    /// Several texts appear twice, as a base file and a _pos or _num one, and
    /// <b>the base file is not always the fuller one</b>. Otinel's ed_OtinG.xml
    /// holds 731 tokens and no verse lines at all, while ed_OtinG_pos.xml holds
    /// 2,193 lines - so a rule preferring the plainer name would have installed
    /// an empty work and thrown the text away. transcr_AnsMetz_U.xml is the same
    /// shape: 354 tokens, no lines.
    ///
    /// So the choice is made by measuring rather than by naming: group the
    /// variants and keep whichever has the most verse lines. It costs a parse of
    /// each candidate, which is a few seconds over 34 files.
    ///
    /// The witnesses are NOT variants of each other - transcr_Otin_A, _B and _M
    /// are three manuscripts of Otinel and all three are installed.
    /// </summary>
    private static List<string> ChooseFiles(string tree)
    {
        var candidates = Directories
            .Select(d => Path.Combine(tree, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.xml"))
            .ToList();

        var best = new Dictionary<string, (string Path, int Lines)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in candidates)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var key = stem;
            foreach (var suffix in new[] { "_pos", "_num" })
                if (key.EndsWith(suffix, StringComparison.Ordinal)) key = key[..^suffix.Length];

            int lines;
            try
            {
                lines = GesteTextLoader.Load(file).Lines.Count;
            }
            catch
            {
                // A file that will not parse cannot win the comparison, but it
                // must still be able to stand as the only candidate so that
                // IngestAsync reports the parse error against it.
                lines = -1;
            }

            if (!best.TryGetValue(key, out var held) || lines > held.Lines)
                best[key] = (file, lines);
        }

        return best.Values
            .Select(v => v.Path)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task InstallAsync(GesteText text, string sourcePath, CancellationToken cancellationToken)
    {
        // Everything anonymous - see the remarks in GesteTextLoader.Parse on why
        // the corpus's own author field is not read.
        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:geste:anonymous",
            Name = "Anonymous",
            Namespace = Namespace,
            Language = text.Language
        }, cancellationToken);

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = $"urn:geste:{Slug(text.Id)}",
            Title = WorkTitle(text),
            CitationScheme = "Line"
        }, cancellationToken);

        await InstallEditionAsync(
            workId, text, "dipl", "diplomatic", sourcePath,
            text.Lines.Select(l => (l.CitationRef, l.SortOrder, l.Diplomatic)), cancellationToken);

        // Only where the two actually differ. The nineteen editions carry
        // sic/corr and no orig/reg, so their two readings would be identical -
        // and two identical editions in the dropdown is a worse answer than one.
        if (text.Lines.Any(l => l.Normalised != l.Diplomatic))
        {
            await InstallEditionAsync(
                workId, text, "norm", "normalised", sourcePath,
                text.Lines.Select(l => (l.CitationRef, l.SortOrder, l.Normalised)), cancellationToken);
        }
    }

    private async Task InstallEditionAsync(
        int workId, GesteText text, string urnSuffix, string orthography, string sourcePath,
        IEnumerable<(string CitationRef, int SortOrder, string Text)> lines,
        CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:geste:{Slug(text.Id)}:{urnSuffix}",
            Kind = EditionKind.Original,
            Language = text.Language,
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
                IsAthetized = false,

                // Every text in this corpus is verse - chansons de geste are
                // laisses of assonanced lines and there is no prose in it. Set
                // from the genre rather than guessed at the line, which is the
                // condition the hexameter scanner asks about.
                IsVerse = true
            }).ToList(),
            cancellationToken);
    }

    /// <summary>
    /// The witness in the title, because the corpus has ten texts called "Garin
    /// le Lorrain" and five called "Otinel" and nothing else distinguishes them
    /// in a library tree.
    /// </summary>
    private static string WorkTitle(GesteText text) =>
        text.Witness == null ? text.Title : $"{text.Title} ({text.Witness})";

    /// <summary>
    /// The mappings a text's own annotation gives, deduplicated on the triple
    /// the way the Greek and Latin lemma ingests are.
    ///
    /// Keyed by the edition's language, not by one code: this corpus is Old
    /// French and Anglo-Norman, and a form looked up under the wrong one would
    /// find nothing.
    /// </summary>
    private static void CollectLemmas(GesteText text, List<Lemma> into, HashSet<string> seen)
    {
        foreach (var word in text.Lines.SelectMany(l => l.Words))
        {
            var normalized = WordNormalizer.Normalize(word.Form);
            if (normalized.Length == 0) continue;

            var key = $"{text.Language}\u0001{normalized}\u0001{word.Headword}\u0001{word.Tag}";
            if (!seen.Add(key)) continue;

            into.Add(new Lemma
            {
                Form = word.Form,
                NormalizedForm = normalized,
                Headword = word.Headword,
                Language = text.Language,
                PartOfSpeech = word.Tag
            });
        }
    }

    /// <summary>
    /// Extracts the gzipped tar once. A re-run over an already-extracted tree
    /// goes straight to reading.
    /// </summary>
    private static async Task<string> EnsureExtractedAsync(
        string root, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);

        // Either directory, not just xml_gold. A tree holding only one of them
        // is still an extracted tree, and demanding the archive again because
        // the other is missing would fail a corpus that is simply arranged
        // differently from the one revision this was written against.
        var existing = ExtractedTree(root);
        if (existing != null) return existing;

        var archive = Directory.GetFiles(root, "*.tar.gz").FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"The Geste archive was not found in {root}. Expected {ArchiveFileName}.");

        progress?.Report("Extracting the corpus...");

        // The whole tree, not only the two text directories: the two files that
        // declare a DOCTYPE reference dtd/abreviations.dtd by a relative path,
        // and the reader resolves it from disk. Extracting selectively would
        // leave those two unparseable.
        await using (var file = File.OpenRead(archive))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, root, overwriteFiles: true, cancellationToken);
        }

        return ExtractedTree(root) ?? root;
    }

    /// <summary>
    /// The folder holding the text directories, wherever the archive put it -
    /// codeload names its root after the commit, so the path is not knowable in
    /// advance.
    /// </summary>
    private static string? ExtractedTree(string root) =>
        Directories
            .SelectMany(d => Directory.Exists(root)
                ? Directory.GetDirectories(root, d, SearchOption.AllDirectories)
                : Array.Empty<string>())
            .Select(d => Path.GetDirectoryName(d)!)
            .FirstOrDefault(d => d != null);

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
