using System.Text.Json;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Ingestion.AiTranslations;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Putting the Classica Codex AI translations into a library.
///
/// The fixture is two files of the collection's own shape: a translation of
/// three passages of the Iliad, whose Greek the library holds, and one of the
/// Völuspá, whose manuscript it may not. Every translation depends on a text
/// some other collection installs, so most of what can go wrong is about that
/// text: whether it is there, and whether the file's references are its lines.
/// </summary>
[Collection("Database")]
public class AiTranslationsIngestTests
{
    private const string IliadWork = "urn:cts:greekLit:tlg0012.tlg001";
    private const string Iliad = "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2";
    private const string VoluspaWork = "urn:menota:anonymous:voluspa";
    private const string Voluspa = "urn:menota:anonymous:voluspa:gks-2365-4to";

    private const string FirstLine = "Sing, goddess, the wrath of Achilles son of Peleus,";

    /// <summary>Progress that keeps what it is told, in order, on the calling thread.</summary>
    private sealed class Messages : IProgress<string>
    {
        public List<string> All { get; } = new();
        public void Report(string value) => All.Add(value);
    }

    /// <summary>
    /// A source edition as its collection installs one, with sort orders that
    /// are not the lines' positions, so a translation that took its order from
    /// anywhere but the source would show it.
    /// </summary>
    private static async Task<int> SourceAsync(
        string authorUrn, string authorName, string workUrn, string title, string editionUrn, string language,
        params (string Ref, string Text, string Kind)[] lines)
    {
        var authorId = await new AuthorRepository().UpsertAsync(new Author
        {
            CtsUrn = authorUrn, Name = authorName, Namespace = "test", Language = language
        });
        var workId = await new WorkRepository().UpsertAsync(new Work
        {
            AuthorId = authorId, CtsUrn = workUrn, Title = title
        });
        var editionId = await new EditionRepository().UpsertAsync(new Edition
        {
            WorkId = workId, CtsUrn = editionUrn, Kind = EditionKind.Original, Language = language,
            SourcePath = editionUrn, Collection = "test"
        });

        await new TextNodeRepository().BulkInsertAsync(lines.Select((l, i) => new TextNode
        {
            EditionId = editionId, CitationRef = l.Ref, SortOrder = (i + 1) * 10, Text = l.Text, NodeKind = l.Kind
        }).ToList());

        return workId;
    }

    private static Task<int> IliadAsync() => SourceAsync(
        "urn:cts:greekLit:tlg0012", "Homer", IliadWork, "Iliad", Iliad, "grc",
        ("1", "ΙΛΙΑΔΟΣ Α", TextNodeKinds.Head),
        ("1.1", "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος", TextNodeKinds.Line),
        ("1.2", "οὐλομένην, ἣ μυρί᾽ Ἀχαιοῖς ἄλγε᾽ ἔθηκε,", TextNodeKinds.Line));

    private static Task<int> VoluspaAsync() => SourceAsync(
        "urn:menota:anonymous", "Anonymous", VoluspaWork, "Vǫluspá", Voluspa, "non",
        ("1r.1", "Hlióðs bið ek allar", TextNodeKinds.Line),
        ("1r.2", "helgar kindir", TextNodeKinds.Line));

    /// <summary>A file in the collection's format, as FORMAT.md describes it.</summary>
    private static string Translation(
        string workUrn, string source, params (string Ref, string Text)[] lines) =>
        JsonSerializer.Serialize(new
        {
            work_urn = workUrn,
            edition_urn = workUrn + ".ai-claude",
            source_edition = source,
            language = "eng",
            translator = "Claude (AI-generated)",
            model = "claude-opus-5-5",
            made = "2026-10-31",
            updated = "2026-10-31",
            header = new
            {
                title = $"{workUrn}: AI translation (Claude)",
                author = "Test",
                publisher = "Classica Codex AI translations",
                date = "2026",
                source = $"Translated by Claude from the library's {source}.",
                statement = "An AI-generated translation. No specialist has reviewed it.",
                availability = "CC BY-SA 4.0, as its source requires."
            },
            notes = new Dictionary<string, string>(),
            nodes = lines.Select(l => new { @ref = l.Ref, text = l.Text })
        });

    /// <summary>
    /// The Iliad's translation lists its heading last, so the order it is
    /// filed in can only have come from the Greek.
    /// </summary>
    private static (string Name, string Json) IliadFile(string firstLine = FirstLine) =>
        ("cts_greekLit_tlg0012.tlg001", Translation(IliadWork, Iliad,
            ("1.1", firstLine),
            ("1.2", "the ruinous wrath that brought the Achaeans countless griefs,"),
            ("1", "Iliad, Book 1")));

    private static (string Name, string Json) VoluspaFile() =>
        ("urn_menota_anonymous_voluspa", Translation(VoluspaWork, Voluspa,
            ("1r.1", "I ask a hearing of all"),
            ("1r.2", "the holy kindreds")));

    /// <summary>A folder shaped like the cloned repository, so nothing is downloaded.</summary>
    private static string Folder(params (string Name, string Json)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccx-ai-translations-tests", Guid.NewGuid().ToString("N"));
        Write(root, files);
        return root;
    }

    private static void Write(string root, params (string Name, string Json)[] files)
    {
        var folder = Path.Combine(root, AiTranslationsIngestService.TranslationsFolder);
        Directory.CreateDirectory(folder);
        foreach (var (name, json) in files) File.WriteAllText(Path.Combine(folder, name + ".json"), json);
    }

    private static async Task<Edition?> EditionAsync(string urn) =>
        (await new EditionRepository().GetByCtsUrnsAsync(new[] { urn })).GetValueOrDefault(urn);

    private static async Task<List<TextNode>> LinesAsync(string urn) =>
        await new TextNodeRepository().GetByEditionAsync((await EditionAsync(urn))!.EditionId);

    /// <summary>
    /// Stored the way Create Translation stores Gemini's: a translation under
    /// the source's work, labelled AI-generated by its translator, in no
    /// collection, each line in its original's place and of its original's
    /// kind - the heading stays a heading.
    /// </summary>
    [Fact]
    public async Task AFirstImportFilesEachLineBesideTheLineItTranslates()
    {
        using var db = await TempDatabase.CreateAsync();
        var workId = await IliadAsync();

        var service = new AiTranslationsIngestService();
        var outcome = await service.IngestAsync(Folder(IliadFile(), VoluspaFile()), new Messages());

        Assert.Equal(1, service.Imported);
        Assert.Equal(1, outcome.InstalledCount);

        var edition = Assert.Single(
            await new EditionRepository().GetByWorkAsync(workId), e => e.Kind == EditionKind.Translation);
        Assert.Equal(IliadWork + ".ai-claude", edition.CtsUrn);
        Assert.Equal("Claude (AI-generated)", edition.Translator);
        Assert.Equal("eng", edition.Language);
        Assert.Null(edition.Collection);
        Assert.Null(edition.SourcePath);

        var lines = await LinesAsync(edition.CtsUrn);
        Assert.Equal(new[] { "1", "1.1", "1.2" }, lines.Select(l => l.CitationRef));
        Assert.Equal(new[] { 10, 20, 30 }, lines.Select(l => l.SortOrder));
        Assert.Equal(new[] { TextNodeKinds.Head, TextNodeKinds.Line, TextNodeKinds.Line }, lines.Select(l => l.NodeKind));
        Assert.Equal(FirstLine, lines[1].Text);

        var header = await new EditionHeaderRepository().GetAsync(edition.EditionId);
        Assert.Equal("An AI-generated translation. No specialist has reviewed it.", header!.EditionStatement);
        Assert.Equal("CC BY-SA 4.0, as its source requires.", header.Availability);

        // A library that never built a word index is not given one by this.
        Assert.False(await new WordIndexRepository().HasDataAsync());
    }

    [Fact]
    public async Task ImportingTheSameFilesAgainChangesNothing()
    {
        using var db = await TempDatabase.CreateAsync();
        await IliadAsync();
        var root = Folder(IliadFile(), VoluspaFile());

        await new AiTranslationsIngestService().IngestAsync(root, new Messages());
        var before = (await LinesAsync(IliadWork + ".ai-claude")).Select(l => (l.TextNodeId, l.Text)).ToList();

        var again = new AiTranslationsIngestService();
        var messages = new Messages();
        await again.IngestAsync(root, messages);

        Assert.Equal(0, again.Imported);
        Assert.Equal(0, again.Updated);
        Assert.Equal(1, again.Unchanged);
        Assert.Equal(before, (await LinesAsync(IliadWork + ".ai-claude")).Select(l => (l.TextNodeId, l.Text)));
        Assert.StartsWith("AI translations: 0 imported, 0 updated, 1 unchanged", messages.All[^1]);
    }

    /// <summary>
    /// A corrected line is rewritten where it stands: it keeps its id, which
    /// is what bookmarks and the word index hang on, and the index - where
    /// the library has one - forgets the old words and learns the new.
    /// </summary>
    [Fact]
    public async Task AChangedLineIsRewrittenInPlaceAndReindexed()
    {
        using var db = await TempDatabase.CreateAsync();
        await IliadAsync();
        var root = Folder(IliadFile(), VoluspaFile());

        await new AiTranslationsIngestService().IngestAsync(root, new Messages());
        await new WordIndexService().BuildAsync();

        var before = await LinesAsync(IliadWork + ".ai-claude");
        Write(root, IliadFile("Sing, goddess, of the anger of Achilles, Peleus' son,"));

        var service = new AiTranslationsIngestService();
        await service.IngestAsync(root, new Messages());

        Assert.Equal(1, service.Updated);
        Assert.Equal(0, service.Unchanged);

        var after = await LinesAsync(IliadWork + ".ai-claude");
        Assert.Equal(before.Select(l => l.TextNodeId), after.Select(l => l.TextNodeId));
        Assert.Equal("Sing, goddess, of the anger of Achilles, Peleus' son,", after[1].Text);
        Assert.Equal(before[2].Text, after[2].Text);

        var line = after[1].TextNodeId;
        Assert.Equal(1, await db.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM WordIndex WHERE NormalizedWord = 'anger' AND TextNodeId = {line};"));
        Assert.Equal(0, await db.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM WordIndex WHERE NormalizedWord = 'wrath' AND TextNodeId = {line};"));
    }

    /// <summary>
    /// A translation of a text this library does not hold is passed over and
    /// noted, not failed: the reader may not have installed that collection.
    /// It goes in the outcome's second list, which is logged without the skip
    /// dialog that calls a file malformed upstream. And once the text arrives,
    /// the step stops calling itself complete until it is run again.
    /// </summary>
    [Fact]
    public async Task AFileWhoseSourceIsNotInTheLibraryIsSkippedAndReported()
    {
        using var db = await TempDatabase.CreateAsync();
        await IliadAsync();
        var root = Folder(IliadFile(), VoluspaFile());

        var service = new AiTranslationsIngestService();
        var messages = new Messages();
        var outcome = await service.IngestAsync(root, messages);

        var (path, reason) = Assert.Single(service.WithoutSource);
        Assert.Equal("urn_menota_anonymous_voluspa.json", Path.GetFileName(path));
        Assert.Contains(Voluspa, reason);
        Assert.Null(await EditionAsync(VoluspaWork + ".ai-claude"));

        Assert.Empty(outcome.SkippedFiles);
        Assert.Equal(path, Assert.Single(outcome.RecoveredFolders).FilePath);
        Assert.False(outcome.SkipsAreWorthInterrupting);
        Assert.Contains("1 skipped because the text they translate is not in this library", messages.All[^1]);

        Assert.True((await new AiTranslationsIngestService().SurveyAsync(root)).IsComplete);

        await VoluspaAsync();
        Assert.Equal(new AiTranslationsSurvey(2, 2, 1), await new AiTranslationsIngestService().SurveyAsync(root));

        var again = new AiTranslationsIngestService();
        await again.IngestAsync(root, new Messages());
        Assert.Equal(1, again.Imported);
        Assert.Equal(1, again.Unchanged);
        Assert.Empty(again.WithoutSource);
        Assert.Equal(new[] { "1r.1", "1r.2" }, (await LinesAsync(VoluspaWork + ".ai-claude")).Select(l => l.CitationRef));
        Assert.True((await new AiTranslationsIngestService().SurveyAsync(root)).IsComplete);
    }

    /// <summary>
    /// One reference that is not a line of the source and none of the file
    /// goes in: not its good lines, not an empty edition, and not a
    /// correction to a translation already installed.
    /// </summary>
    [Fact]
    public async Task AFileWithAReferenceItsSourceLacksIsSkippedWhole()
    {
        using var db = await TempDatabase.CreateAsync();
        await IliadAsync();
        await VoluspaAsync();
        var root = Folder(IliadFile());

        await new AiTranslationsIngestService().IngestAsync(root, new Messages());

        // The installed Iliad gains a correction and a line the Greek does not
        // have; the Völuspá arrives with one good line and one bad.
        Write(root,
            ("cts_greekLit_tlg0012.tlg001", Translation(IliadWork, Iliad,
                ("1.1", "A correction that must not land,"),
                ("1.611", "a line past the end of the book"))),
            ("urn_menota_anonymous_voluspa", Translation(VoluspaWork, Voluspa,
                ("1r.1", "I ask a hearing of all"),
                ("9v.99", "a line the manuscript does not have"))));

        var service = new AiTranslationsIngestService();
        var outcome = await service.IngestAsync(root, new Messages());

        Assert.Equal(0, service.Imported + service.Updated + service.Unchanged);
        Assert.Equal(2, service.Failed.Count);
        Assert.Contains(service.Failed, f => f.Error == $"1 of its references are not lines of {Iliad}, the first 1.611");
        Assert.Contains(service.Failed, f => f.Error == $"1 of its references are not lines of {Voluspa}, the first 9v.99");
        Assert.Equal(2, outcome.SkippedFiles.Count);

        Assert.Null(await EditionAsync(VoluspaWork + ".ai-claude"));
        Assert.Equal(FirstLine, (await LinesAsync(IliadWork + ".ai-claude"))[1].Text);
        Assert.False((await new AiTranslationsIngestService().SurveyAsync(root)).IsComplete);
    }

    /// <summary>
    /// The upsert overwrites whatever holds the identifier and the sync
    /// deletes every line the file does not list, so a file whose own
    /// identifier named its source would replace the Greek with three lines of
    /// English. Only an AI translation's identifier, and an AI label, are
    /// accepted.
    /// </summary>
    [Fact]
    public async Task AFileThatWouldOverwriteARealEditionIsRefused()
    {
        using var db = await TempDatabase.CreateAsync();
        await IliadAsync();

        var json = Translation(IliadWork, Iliad, ("1.1", FirstLine))
            .Replace($"\"edition_urn\":\"{IliadWork}.ai-claude\"", $"\"edition_urn\":\"{Iliad}\"", StringComparison.Ordinal);
        var unlabelled = Translation(VoluspaWork, Iliad, ("1.1", FirstLine))
            .Replace("Claude (AI-generated)", "Claude", StringComparison.Ordinal);

        var service = new AiTranslationsIngestService();
        var outcome = await service.IngestAsync(
            Folder(("overwrites-the-greek", json), ("unlabelled", unlabelled)), new Messages());

        Assert.Equal(2, outcome.SkippedFiles.Count);
        Assert.Equal(3, (await LinesAsync(Iliad)).Count);
        Assert.Equal(EditionKind.Original, (await EditionAsync(Iliad))!.Kind);
        Assert.Null(await EditionAsync(VoluspaWork + ".ai-claude"));
    }

    /// <summary>
    /// With none of the texts in the library nothing can be installed, and the
    /// step says what to install instead of reporting itself ready - and its
    /// tick stays unearned.
    /// </summary>
    [Fact]
    public async Task WithNoneOfTheSourcesTheStepSaysWhatToInstallFirst()
    {
        using var db = await TempDatabase.CreateAsync();
        var root = Folder(IliadFile(), VoluspaFile());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new AiTranslationsIngestService().IngestAsync(root, new Messages()));
        Assert.Contains("Install the collections", error.Message);

        Assert.Equal(new AiTranslationsSurvey(2, 0, 0), await new AiTranslationsIngestService().SurveyAsync(root));
        Assert.False((await new AiTranslationsIngestService().SurveyAsync(root)).IsComplete);

        // And a folder that has not been fetched is nothing to install, not an error.
        var empty = Path.Combine(Path.GetTempPath(), "ccx-ai-translations-tests", Guid.NewGuid().ToString("N"));
        Assert.Equal(new AiTranslationsSurvey(0, 0, 0), await new AiTranslationsIngestService().SurveyAsync(empty));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => new AiTranslationsIngestService().IngestAsync(empty, new Messages()));
    }
}
