using System.Text;
using System.Text.Json;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.AiTranslations;

/// <summary>
/// One file of the collection: the translated passages of one work, each line
/// under its source line's own citation reference, verbatim.
/// </summary>
/// <param name="SourceEdition">The edition it was translated from, which some other collection provides.</param>
public sealed record AiTranslationFile(
    string EditionUrn, string SourceEdition, string Language, string Translator,
    EditionHeader Header, IReadOnlyList<(string CitationRef, string Text)> Lines)
{
    /// <summary>
    /// Reads a file and checks its shape. Throws InvalidDataException, with
    /// the reason as its message, for a file that cannot be installed as it
    /// stands; nothing about the library is consulted here.
    /// </summary>
    public static AiTranslationFile Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a translation file");

        string Required(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString()!.Trim() is { Length: > 0 } text
                ? text
                : throw new InvalidDataException($"it has no \"{name}\"");

        var editionUrn = Required("edition_urn");

        // The upsert below overwrites whatever edition holds this identifier,
        // and the sync after it deletes every line the file does not list. A
        // file naming a real edition here - its source's, by a slip - would
        // replace that text with its own few translated lines, so only an AI
        // translation's identifier is accepted.
        if (!editionUrn.EndsWith(AiTranslationsIngestService.EditionSuffix, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"its edition_urn, {editionUrn}, does not end in {AiTranslationsIngestService.EditionSuffix}");

        // The label is what tells a reader the text is a machine's: the edition
        // list, exports and the coverage note all read it from the translator.
        var translator = Required("translator");
        if (!translator.Contains("(AI-generated)", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"its translator, {translator}, does not say \"(AI-generated)\"");

        var header = root.TryGetProperty("header", out var h) && h.ValueKind == JsonValueKind.Object ? h : default;
        string? HeaderField(string name) =>
            header.ValueKind == JsonValueKind.Object && header.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var lines = new List<(string, string)>();
        if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object
                    || !node.TryGetProperty("ref", out var cite) || cite.ValueKind != JsonValueKind.String
                    || !node.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException($"line {lines.Count + 1} has no ref or no text");
                }

                lines.Add((cite.GetString()!, text.GetString()!));
            }
        }

        if (lines.Count == 0) throw new InvalidDataException("it has no translated lines");

        return new AiTranslationFile(
            editionUrn, Required("source_edition"), Required("language"), translator,
            new EditionHeader
            {
                Title = HeaderField("title"),
                Author = HeaderField("author"),
                Publisher = HeaderField("publisher"),
                PublicationDate = HeaderField("date"),
                SourceDescription = HeaderField("source"),
                EditionStatement = HeaderField("statement"),
                Availability = HeaderField("availability")
            },
            lines);
    }
}

/// <summary>What a folder of translations amounts to against this library.</summary>
/// <param name="Files">Translation files in the folder that could be read.</param>
/// <param name="WithSource">Of those, the ones whose source edition the library holds - the ones that can be installed.</param>
/// <param name="Installed">Of those, the ones whose translation is in the library.</param>
public sealed record AiTranslationsSurvey(int Files, int WithSource, int Installed)
{
    /// <summary>
    /// Every translation that can be installed is. Not true of a library that
    /// holds none of the sources: nothing could be installed there, and a tick
    /// over nothing is the state setup exists to avoid.
    /// </summary>
    public bool IsComplete => WithSource > 0 && Installed == WithSource;
}

/// <summary>
/// Puts the Classica Codex AI translations into the library: English for
/// passages that have no published English, 446 works in twelve languages,
/// made by Claude and checked by a second AI, Codex, and reviewed by no
/// specialist.
///
/// <b>They are stored the way Create Translation stores Gemini's.</b> One
/// translation edition per work, "&lt;work&gt;.ai-claude", whose translator,
/// "Claude (AI-generated)", is what labels it AI-made in the edition list, in
/// exports and in the incomplete-coverage note. Each line is filed under the
/// citation reference of the line it translates, and takes that line's place
/// and kind, so it sits beside its original in the reader. No collection is
/// written, so no collection filter ever hides a translation from the text
/// it translates.
///
/// <b>Each file depends on another collection.</b> A translation is of an
/// edition the library already holds - Perseus, Menota, ReM, the TLA - and a
/// file whose source is not here is passed over and noted, not failed: the
/// reader may simply not have installed that collection. A file naming a line
/// its source does not have is passed over whole, before anything is
/// written, because half a translation is worse than none.
///
/// <b>A re-run changes only what changed.</b> SyncEditionAsync matches lines
/// on their reference, so a corrected line is rewritten in place and keeps
/// its id, and the word index - kept current only where the library has one,
/// for the reason CreateTranslationForm gives - is told about that line and
/// no other.
///
/// The translators' notes on doubtful readings stay in the files; nothing in
/// the reader has a place for a note on a translation.
/// </summary>
public class AiTranslationsIngestService
{
    public const string RepositoryUrl = "https://github.com/ClassicaCodex/ai-translations";

    /// <summary>The repository's folder of translations, one JSON file per work.</summary>
    public const string TranslationsFolder = "translations";

    /// <summary>What every translation's identifier ends in: its work's, and this.</summary>
    public const string EditionSuffix = ".ai-claude";

    /// <summary>What the clone has to write out. The translations are JSON, not TEI.</summary>
    public static readonly IReadOnlyList<string> FileExtensions = new[] { ".json" };

    private readonly EditionRepository _editionRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly WordIndexRepository _wordIndexRepo = new();
    private readonly WordIndexService _wordIndex = new();

    public int Imported { get; private set; }
    public int Updated { get; private set; }
    public int Unchanged { get; private set; }

    /// <summary>Files whose source edition is not in this library. Nothing is wrong with them.</summary>
    public List<(string FilePath, string Reason)> WithoutSource { get; } = new();

    /// <summary>Files that cannot be installed: unreadable, or naming a line their source does not have.</summary>
    public List<(string FilePath, string Error)> Failed { get; } = new();

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        var paths = Files(root);
        if (paths.Count == 0)
        {
            throw new InvalidDataException(
                $"No translations were found in {Path.Combine(root, TranslationsFolder)}. This step needs the "
                + "ai-translations repository, which holds a translations folder of JSON files.");
        }

        var files = new List<(string Path, AiTranslationFile File)>();
        foreach (var path in paths)
        {
            try
            {
                files.Add((path, AiTranslationFile.Load(path)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed.Add((path, ex.Message));
            }
        }

        // Every source and every translation asked about at once: it is the
        // same question for each file, and one query answers all of them.
        var held = await _editionRepo.GetByCtsUrnsAsync(
            files.SelectMany(f => new[] { f.File.SourceEdition, f.File.EditionUrn }).ToList(), cancellationToken);

        var indexed = await _wordIndexRepo.HasDataAsync(cancellationToken);

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (path, file) = files[i];

            if (i > 0 && i % 50 == 0) progress.Report($"{i:N0} of {files.Count:N0} translations...");

            if (!held.TryGetValue(file.SourceEdition, out var source))
            {
                WithoutSource.Add((path, $"its source, {file.SourceEdition}, is not in this library"));
                continue;
            }

            try
            {
                await InstallAsync(file, source, held.GetValueOrDefault(file.EditionUrn), indexed, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed.Add((path, ex.GetBaseException().Message));
            }
        }

        if (WithoutSource.Count > 0 && WithoutSource.Count == files.Count)
        {
            throw new InvalidOperationException(
                "None of these translations can be installed yet: the texts they translate are not in this "
                + "library. Install the collections they come from - the Greek and Latin texts, Menota, the "
                + "Middle High German, the Egyptian texts and the rest - and run this step again.");
        }

        progress.Report(Summary());
        foreach (var (path, error) in Failed) progress.Report($"Not installed: {Path.GetFileName(path)} - {error}");

        // A file without its source goes in the outcome's second list, not its
        // first. The skip report says the first list's files are malformed
        // upstream and that there is nothing to fix at this end, which is true
        // of a file naming a line its source lacks and false of one whose
        // source collection the reader can install. The second list is logged
        // and counted on the status line without opening a dialog.
        return IngestOutcome.From(Failed, WithoutSource, attempted: paths.Count - WithoutSource.Count);
    }

    /// <summary>The counts a run ends with, and why anything was left out.</summary>
    public string Summary()
    {
        var text = new StringBuilder(
            $"AI translations: {Imported:N0} imported, {Updated:N0} updated, {Unchanged:N0} unchanged");

        if (WithoutSource.Count > 0)
            text.Append($"; {WithoutSource.Count:N0} skipped because the text they translate is not in this library");

        if (Failed.Count > 0)
            text.Append($"; {Failed.Count:N0} skipped as unreadable or naming lines their source does not have");

        return text.Append('.').ToString();
    }

    /// <summary>
    /// How many of the folder's translations this library can hold and does -
    /// what decides the setup step's tick. A folder that is not there yet
    /// reads as nothing to install.
    /// </summary>
    public async Task<AiTranslationsSurvey> SurveyAsync(string root, CancellationToken cancellationToken = default)
    {
        var files = new List<AiTranslationFile>();
        foreach (var path in Files(root))
        {
            try
            {
                files.Add(AiTranslationFile.Load(path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A file that cannot be read names no source, so it is not one
                // this library could hold. The ingest reports it.
            }
        }

        if (files.Count == 0) return new AiTranslationsSurvey(0, 0, 0);

        var held = await _editionRepo.GetByCtsUrnsAsync(
            files.SelectMany(f => new[] { f.SourceEdition, f.EditionUrn }).ToList(), cancellationToken);

        var withSource = files.Where(f => held.ContainsKey(f.SourceEdition)).ToList();
        return new AiTranslationsSurvey(files.Count, withSource.Count, withSource.Count(f => held.ContainsKey(f.EditionUrn)));
    }

    private async Task InstallAsync(
        AiTranslationFile file, Edition source, Edition? existing, bool indexed,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        // Each source line's place and kind by its reference - the first, in
        // reading order, where a reference repeats.
        var sourceLines = new Dictionary<string, TextNode>(StringComparer.Ordinal);
        foreach (var node in await _textNodeRepo.GetByEditionAsync(source.EditionId, cancellationToken: cancellationToken))
            sourceLines.TryAdd(node.CitationRef, node);

        // Checked before anything is written, so a file is installed whole or
        // not at all.
        var unknown = file.Lines.Select(l => l.CitationRef).Where(c => !sourceLines.ContainsKey(c)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidDataException(
                $"{unknown.Count:N0} of its references are not lines of {file.SourceEdition}, the first {unknown[0]}");
        }

        var lines = file.Lines
            .Select(l => new TextNode
            {
                CitationRef = l.CitationRef,
                SortOrder = sourceLines[l.CitationRef].SortOrder,
                NodeKind = sourceLines[l.CitationRef].NodeKind,
                Text = l.Text
            })
            .OrderBy(l => l.SortOrder)
            .ToList();

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = source.WorkId,
            CtsUrn = file.EditionUrn,
            Kind = EditionKind.Translation,
            Language = file.Language,
            Translator = file.Translator,
            SourcePath = null,
            Collection = null
        }, cancellationToken);

        foreach (var line in lines) line.EditionId = editionId;

        var storedHeader = existing == null ? null : await _headerRepo.GetAsync(existing.EditionId, cancellationToken);
        var headerChanged = storedHeader == null || !SameHeader(storedHeader, file.Header);
        if (headerChanged) await _headerRepo.SaveAsync(editionId, file.Header, cancellationToken);

        var changes = await _textNodeRepo.SyncEditionAsync(editionId, lines, cancellationToken);
        if (changes.Any && indexed) await _wordIndex.ApplyChangesAsync(changes, cancellationToken);

        var title = file.Header.Title ?? file.EditionUrn;
        if (existing == null)
        {
            Imported++;
            progress.Report($"{title}: {lines.Count:N0} lines");
            return;
        }

        var editionChanged = existing.WorkId != source.WorkId
                             || existing.Kind != EditionKind.Translation
                             || existing.Language != file.Language
                             || existing.Translator != file.Translator;

        if (changes.Any || headerChanged || editionChanged)
        {
            Updated++;
            progress.Report(
                $"{title}: {changes.Added.Count:N0} lines added, {changes.Rewritten.Count:N0} rewritten, "
                + $"{changes.Removed.Count:N0} removed");
        }
        else
        {
            Unchanged++;
        }
    }

    private static List<string> Files(string root)
    {
        var folder = Path.Combine(root, TranslationsFolder);
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToList()
            : new List<string>();
    }

    private static bool SameHeader(EditionHeader stored, EditionHeader wanted) =>
        stored.Title == wanted.Title
        && stored.Author == wanted.Author
        && stored.Publisher == wanted.Publisher
        && stored.PublicationDate == wanted.PublicationDate
        && stored.PublicationPlace == wanted.PublicationPlace
        && stored.SourceDescription == wanted.SourceDescription
        && stored.EditionStatement == wanted.EditionStatement
        && stored.Availability == wanted.Availability
        && stored.Responsibilities.Count == 0;
}
