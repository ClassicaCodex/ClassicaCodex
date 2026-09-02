using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Ingests Perseus's Renaissance / early-modern English collection, which
/// sits in canonical-engLit's OTHER layout - the pre-CTS one under
/// english-texts/Renaissance/&lt;Author&gt;/.../opensource/*.xml.
///
/// PerseusIngestService can't read this tree: it walks data/ looking for
/// __cts__.xml catalogs, and there are none here. Identity has to be
/// synthesized instead - the folder names the author, the TEI header names
/// the work. That's the whole reason this is a separate service rather than
/// another namespace passed to PerseusIngestService.
///
/// What it deliberately reuses: the exact same TeiParser (already
/// namespace-agnostic, already walks P4's div1/div2 and descends &lt;sp&gt;
/// to &lt;l&gt;/&lt;p&gt; leaves) and the same repository set, so a
/// Renaissance edition reads identically to a Greek one once it's in the DB.
///
/// LICENSING: only opensource/ subfolders are read. The sibling copyright/
/// folders (and the whole NVS/ tree) are the New Variorum and other
/// 19th/20th-century scholarly editions that aren't freely redistributable;
/// Perseus encoded that split on purpose and this respects it. See
/// IsUnderOpensource.
/// </summary>
public class RenaissanceIngestService
{
    private readonly TeiParser _teiParser = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _editionHeaderRepo = new();

    /// <summary>
    /// Files that failed to ingest (bad XML, unrecognized structure) and were
    /// skipped rather than aborting the run. Check after IngestAsync.
    /// </summary>
    public List<(string FilePath, string Error)> FailedFiles { get; } = new();

    /// <summary>
    /// Files left out because the catalogued tree already supplied the same
    /// text under a proper CTS identifier.
    ///
    /// Not a failure and not a loss - the text is in the library, named
    /// better - so this is reported apart from FailedFiles, the way a folder
    /// named from its own headers is. See the de-duplication in IngestAsync.
    /// </summary>
    public List<(string FilePath, string Error)> SupersededByCatalogue { get; } = new();

    // The DOCTYPE plus its internal subset. These P4 files reference an
    // external Perseus DTD driver via a parameter entity (%PersDrama;,
    // %PersProse;, %PersVerse;, %PersDict;) that can't be resolved offline.
    // Every GENERAL entity the body uses is already turned into a literal
    // character by TeiParser's sanitizer before parsing, so the DTD supplies
    // nothing the parser still needs - dropping the whole declaration
    // sidesteps the external-resolution question entirely. Kept scoped to
    // this service so the Greek/Latin parse path is left exactly as it was.
    private static readonly Regex DoctypePattern = new(
        @"<!DOCTYPE[^>\[]*(\[[\s\S]*?\])?[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <param name="renaissanceRootPath">
    /// The "Renaissance" folder inside the fetched english-texts destination,
    /// e.g. &lt;dataRoot&gt;\english-texts\Renaissance.
    /// </param>
    public async Task IngestAsync(
        string renaissanceRootPath,
        IProgress<IngestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(renaissanceRootPath))
            throw new DirectoryNotFoundException($"Renaissance folder not found: {renaissanceRootPath}");

        // Sidney and James I also arrive via the CTS data/ pass that runs
        // before this one, so those two folders would otherwise create a
        // second author row apiece. Reuse the existing row's key when the
        // display name matches (case-insensitively) so their works just join
        // the author already in the tree. A non-match simply falls through to
        // a fresh row - no worse than not de-duping.
        var existingEngLitByName = (await _authorRepo.GetAllAsync(cancellationToken))
            .Where(a => string.Equals(a.Namespace, "engLit", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => a.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().CtsUrn);

        var authorDirs = Directory.GetDirectories(renaissanceRootPath);
        var totalFiles = Directory.EnumerateFiles(renaissanceRootPath, "*.xml", SearchOption.AllDirectories)
            .Count(IsUnderOpensource);
        int worksProcessed = 0;

        foreach (var authorDir in authorDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Every opensource file anywhere beneath this author folder - the
            // extra middle level (Holinshed\scotland\opensource) and the flat
            // case (Shakespeare\opensource) both fall out of a recursive walk.
            var files = Directory.EnumerateFiles(authorDir, "*.xml", SearchOption.AllDirectories)
                .Where(IsUnderOpensource)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0) continue; // copyright-only author (e.g. Bacon, Dyce, NVS) - skip

            var folderName = Path.GetFileName(authorDir);

            // Read every header once up front so the author's display name can
            // be the header's <author> when the whole folder agrees on one
            // (-> "William Shakespeare"), and the humanized folder otherwise
            // (-> "Holinshed", whose files name different source authors).
            var headers = new List<(string File, string? Title, string? HeaderAuthor)>();
            foreach (var file in files)
            {
                var (title, author) = TryReadHeader(file);
                headers.Add((file, title, author));
            }

            var authorName = ResolveAuthorDisplayName(folderName, headers.Select(h => h.HeaderAuthor));

            var authorKey = existingEngLitByName.TryGetValue(authorName.Trim().ToLowerInvariant(), out var existingUrn)
                ? existingUrn
                : $"engLit:renaissance:{Slug(folderName)}";

            var authorId = await _authorRepo.UpsertAsync(new Author
            {
                CtsUrn = authorKey,
                Name = authorName,
                Namespace = "engLit",
                Language = "eng"
            }, cancellationToken);

            // The same de-duplication as the author rows above, one level
            // down, and for the same reason: the CTS pass runs first and this
            // tree overlaps it.
            //
            // Perseus keeps this collection in two layouts at once - the
            // modern data/ tree with real CTS identifiers, and this older
            // pre-CTS one - and is migrating texts from the second into the
            // first. Where a text has already made the move it is in both, and
            // nothing in the two identifiers says so: Sidney's Defence of
            // Poesie arrives as sidney.defence.perseus-eng1 from one and
            // engLit:renaissance:sidney:defense:opensource from the other. It
            // was going in twice - 73 passages and 108,000 characters counted
            // once each way, listed twice under one author, and doubled in
            // every word count and frequency measure built on the text.
            //
            // One work today. The number grows with every text Perseus
            // migrates, which is the reason to do this by rule rather than by
            // naming the file.
            //
            // Matched on title within this author, and only against works that
            // did NOT come from this tree - a work whose identifier starts
            // engLit:renaissance: is one of ours from a previous run, and
            // skipping those would stop a re-import updating anything.
            var fromCatalogue = (await _workRepo.GetByAuthorAsync(authorId, cancellationToken))
                .Where(w => !w.CtsUrn.StartsWith("engLit:renaissance:", StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Title.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (file, title, _) in headers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var workTitle = string.IsNullOrWhiteSpace(title)
                    ? Humanize(Path.GetFileNameWithoutExtension(file))
                    : title!.Trim();

                progress?.Report(new IngestProgress(authorName, workTitle, worksProcessed, totalFiles));

                if (fromCatalogue.Contains(workTitle))
                {
                    // Already in from the catalogued tree, under a real CTS
                    // identifier. That one is the keeper.
                    SupersededByCatalogue.Add((file,
                        $"\"{workTitle}\" is already in the library from the catalogued tree, which " +
                        "names it properly. This older copy of the same text was left out."));
                    worksProcessed++;
                    continue;
                }

                try
                {
                    // Path from the author folder down, minus the opensource
                    // segment, disambiguates same-named files across subfolders
                    // (Holinshed has england/, ireland/ AND scotland/
                    // description.xml). This is the stable upsert key.
                    var workKey = BuildWorkKey(renaissanceRootPath, file);

                    var workId = await _workRepo.UpsertAsync(new Work
                    {
                        AuthorId = authorId,
                        CtsUrn = $"engLit:renaissance:{workKey}",
                        Title = workTitle
                    }, cancellationToken);

                    var editionId = await _editionRepo.UpsertAsync(new Edition
                    {
                        WorkId = workId,
                        CtsUrn = $"engLit:renaissance:{workKey}:opensource",
                        // These are English originals, not translations of a
                        // classical text, so the two-pane reader treats them as
                        // the original side; Language=eng is what reaches the
                        // WordNet lemma data (English and Latin are only
                        // distinguishable by this column).
                        Kind = EditionKind.Original,
                        Language = "eng",
                        Translator = null,
                        SourcePath = file
                    }, cancellationToken);

                    await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

                    // Parsed once and used twice: the body becomes text
                    // nodes, the header becomes the edition's publication
                    // metadata. These are P4 files, so the DOCTYPE has to
                    // come off before either.
                    var raw = File.ReadAllText(file);

                    var header = TeiHeaderReader.Read(
                        XDocument.Parse(XmlEntitySanitizer.Sanitize(StripDoctype(raw))));

                    if (header != null)
                    {
                        await _editionHeaderRepo.SaveAsync(editionId, header, cancellationToken);
                    }

                    var parsed = _teiParser.ParseXml(StripDoctype(raw));
                    var nodes = _teiParser.ToTextNodes(editionId, parsed);
                    await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One malformed file shouldn't abort the run - same policy
                    // as PerseusIngestService. Rerun later to retry the skips.
                    FailedFiles.Add((file, ex.Message));
                }

                worksProcessed++;
            }
        }
    }

    /// <summary>
    /// True only for files whose path contains an "opensource" folder segment.
    /// A plain substring check would wrongly admit a hypothetical file named
    /// "opensource-notes.xml"; matching a whole path segment is the point.
    /// </summary>
    private static bool IsUnderOpensource(string filePath)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("opensource", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pulls &lt;title&gt; and &lt;author&gt; out of the TEI titleStmt. Reads
    /// by local name so P4 (no namespace) and any stray P5 both work. Returns
    /// nulls rather than throwing - a header we can't read just means the
    /// title falls back to the filename and the author to the folder.
    /// </summary>
    private (string? Title, string? Author) TryReadHeader(string filePath)
    {
        try
        {
            var sanitized = XmlEntitySanitizer.Sanitize(StripDoctype(File.ReadAllText(filePath)));
            var doc = XDocument.Parse(sanitized);

            var titleStmt = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "titleStmt");
            var title = titleStmt?.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
            var author = titleStmt?.Elements().FirstOrDefault(e => e.Name.LocalName == "author")?.Value;

            return (Clean(title), Clean(author));
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// The folder's author display name: the header &lt;author&gt; when every
    /// file that has one agrees, otherwise the humanized folder name. Agreement
    /// avoids the Holinshed problem - its files credit different source authors
    /// in their headers, so grouping by any single one would be wrong; the
    /// folder is the honest grouping there.
    /// </summary>
    private static string ResolveAuthorDisplayName(string folderName, IEnumerable<string?> headerAuthors)
    {
        var distinct = headerAuthors
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count == 1 ? distinct[0] : Humanize(folderName);
    }

    /// <summary>
    /// Author-folder-relative path with the "opensource" segment and the .xml
    /// extension removed, colon-joined: Holinshed\scotland\opensource\history.xml
    /// -> "Holinshed:scotland:history".
    /// </summary>
    private static string BuildWorkKey(string renaissanceRoot, string filePath)
    {
        var relative = Path.GetRelativePath(renaissanceRoot, filePath);
        var segments = relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(s => !s.Equals("opensource", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (segments.Count > 0)
            segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]);

        return string.Join(":", segments.Select(Slug));
    }

    private static string StripDoctype(string xml) => DoctypePattern.Replace(xml, string.Empty);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Underscores to spaces, for folder names used as display text.</summary>
    private static string Humanize(string raw) => raw.Replace('_', ' ').Trim();

    /// <summary>Lowercased, spaces/underscores collapsed - for stable urn-ish keys.</summary>
    private static string Slug(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '_' or '-') sb.Append('_');
        }
        return sb.ToString();
    }
}
