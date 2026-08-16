using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

[Collection("Database")]
public class PassageInquiryTests
{
    [Fact]
    public async Task InquiryUpsertsByStablePassageAndCanLinkAProject()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "Sing, goddess, the anger."));
        var repository = new PassageInquiryRepository();
        var inquiry = new PassageInquiry
        {
            WorkCtsUrn = "urn:w:test1",
            EditionCtsUrn = "urn:e:test1",
            CitationRef = "1.1",
            AuthorName = "Homer",
            WorkTitle = "Iliad",
            Excerpt = "Sing, goddess, the anger.",
            AttentionNote = "The poem begins with emotion rather than a person.",
            DraftQuestion = "What does anger organize in the opening?",
            Direction = PassageInquiryDirection.ReadClosely
        };

        await repository.SaveAsync(inquiry);
        var originalId = inquiry.PassageInquiryId;
        inquiry.AttentionNote = "The invocation makes anger the poem's first subject.";
        inquiry.Direction = PassageInquiryDirection.Research;
        await repository.SaveAsync(inquiry);

        var reopened = await repository.GetAsync("urn:e:test1", "1.1");
        Assert.NotNull(reopened);
        Assert.Equal(originalId, reopened!.PassageInquiryId);
        Assert.Equal(PassageInquiryDirection.Research, reopened.Direction);
        Assert.Equal("The invocation makes anger the poem's first subject.", reopened.AttentionNote);
        Assert.Equal(1, await db.CountAsync("PassageInquiries"));

        var research = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = reopened.DraftQuestion
        };
        await research.SaveProjectAsync(project);
        await repository.LinkProjectAsync(reopened.PassageInquiryId, project.ResearchProjectId);
        Assert.Equal(project.ResearchProjectId,
            (await repository.GetAsync("urn:e:test1", "1.1"))!.ResearchProjectId);
    }

    [Fact]
    public async Task InquirySurvivesAReingestThatChangesEveryRowId()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "Old local wording"));
        var originalNodeId = await db.TextNodeIdAsync(editionId, "1.1");
        // A second edition, seeded after the rows under test and left alone, so that
        // SQLite cannot hand the replacements the same rowids back and let this pass by
        // accident: max(rowid)+1 only moves past the deleted ids while higher ones live.
        var sibling = await db.SeedEditionAsync("test2");
        await db.InsertLinesAsync(sibling, ("1.1", "Another work entirely"));

        var repository = new PassageInquiryRepository();
        await repository.SaveAsync(new PassageInquiry
        {
            WorkCtsUrn = "urn:w:test1",
            EditionCtsUrn = "urn:e:test1",
            CitationRef = "1.1",
            AuthorName = "Homer",
            WorkTitle = "Iliad",
            Excerpt = "Old local wording",
            AttentionNote = "A durable observation",
            DraftQuestion = "Will this note survive re-ingest?"
        });

        // A re-ingest replaces the edition row as well as its text nodes, so every row
        // id this note could have been keyed to is now different. Only the CTS identity
        // survives, which is the claim worth testing - deleting and re-inserting text
        // nodes alone proves nothing, because the table holds no TextNodeId to break.
        await db.ExecuteAsync($"DELETE FROM TextNodes WHERE EditionId = {editionId};");
        await db.ExecuteAsync($"DELETE FROM Editions WHERE EditionId = {editionId};");
        await db.ExecuteAsync(@"INSERT INTO Editions (WorkId, CtsUrn, Kind, Language)
            VALUES ((SELECT WorkId FROM Works WHERE CtsUrn = 'urn:w:test1'),
                    'urn:e:test1', 'Original', 'grc');");
        var reingestedEdition = await db.ScalarAsync<int>(
            "SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:test1';");
        await db.InsertLinesAsync(reingestedEdition, ("1.1", "New local wording"));
        var reingestedNodeId = await db.TextNodeIdAsync(reingestedEdition, "1.1");
        Assert.NotEqual(editionId, reingestedEdition);
        Assert.NotEqual(originalNodeId, reingestedNodeId);

        var reopened = await repository.GetAsync("urn:e:test1", "1.1");
        Assert.NotNull(reopened);
        Assert.Equal("A durable observation", reopened!.AttentionNote);

        // The excerpt is a snapshot taken when the note was written, not a live view of
        // the passage, so it still reads the old wording. Saving again from the live
        // passage is what refreshes it - which is what the form does on every save.
        Assert.Equal("Old local wording", reopened.Excerpt);
        reopened.Excerpt = "New local wording";
        await repository.SaveAsync(reopened);
        Assert.Equal("New local wording",
            (await repository.GetAsync("urn:e:test1", "1.1"))!.Excerpt);
        Assert.Equal(1, await db.CountAsync("PassageInquiries"));
    }

    [Fact]
    public async Task AnInquiryRefusesToSaveWithoutIdentityAttentionOrQuestion()
    {
        using var db = await TempDatabase.CreateAsync();
        var repository = new PassageInquiryRepository();
        static PassageInquiry Complete() => new()
        {
            WorkCtsUrn = "urn:w:test1",
            EditionCtsUrn = "urn:e:test1",
            CitationRef = "1.1",
            AuthorName = "Homer",
            WorkTitle = "Iliad",
            Excerpt = "Sing, goddess, the anger.",
            AttentionNote = "The poem begins with emotion.",
            DraftQuestion = "What does anger organize?"
        };

        var noIdentity = Complete(); noIdentity.CitationRef = "   ";
        var noAttention = Complete(); noAttention.AttentionNote = "   ";
        var noQuestion = Complete(); noQuestion.DraftQuestion = "";

        // The researcher's own words are required before anything is stored. Without
        // these an inquiry would be a bare bookmark, and the promotion path downstream
        // builds a project name out of the draft question.
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(noIdentity));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(noAttention));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(noQuestion));
        Assert.Equal(0L, await db.CountAsync("PassageInquiries"));
    }

    [Fact]
    public async Task AnUnrecognisedStoredDirectionReadsAsNoDirection()
    {
        using var db = await TempDatabase.CreateAsync();
        // A direction written by a later version of the app, or by hand. Reading it must
        // neither throw nor guess: the inquiry comes back with no direction chosen.
        await db.ExecuteAsync(@"INSERT INTO PassageInquiries
            (WorkCtsUrn,EditionCtsUrn,CitationRef,AuthorName,WorkTitle,Excerpt,
             AttentionNote,DraftQuestion,Direction,CreatedUtc,UpdatedUtc)
            VALUES ('urn:w:test1','urn:e:test1','1.1','Homer','Iliad','Sing, goddess.',
                    'Anger comes first.','What does anger organize?','sideways',
                    '2026-08-16T00:00:00.0000000Z','2026-08-16T00:00:00.0000000Z');");

        var stored = await new PassageInquiryRepository().GetAsync("urn:e:test1", "1.1");
        Assert.NotNull(stored);
        Assert.Equal(PassageInquiryDirection.None, stored!.Direction);
    }

    [Fact]
    public async Task LinkingAProjectToAnInquiryThatIsGoneFails()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "A project with nothing to link to"
        };
        await new ResearchRepository().SaveProjectAsync(project);

        // Reporting success here would leave the form believing an inquiry had been
        // promoted while nothing was written, and its rollback would never run.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PassageInquiryRepository().LinkProjectAsync(9999, project.ResearchProjectId));
    }

    [Fact]
    public void GeminiInquiryParserKeepsOnlyUsableQuestions()
    {
        var parsed = GeminiTranslationService.ParsePassageInquirySuggestions("""
            ```json
            [
              {"angle":"Narrative focus","question":"Why begin with anger?","rationale":"It follows the opening noun.","nextStep":"Trace the term through book 1."},
              {"angle":"","question":"Discard me","rationale":"","nextStep":""}
            ]
            ```
            """);

        var suggestion = Assert.Single(parsed);
        Assert.Equal("Narrative focus", suggestion.Angle);
        Assert.Equal("Why begin with anger?", suggestion.Question);
        Assert.Equal("Trace the term through book 1.", suggestion.NextStep);
    }
}
