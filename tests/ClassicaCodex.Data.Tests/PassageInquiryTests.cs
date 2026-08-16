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
    public async Task InquirySurvivesTextNodeReingest()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "Old local wording"));
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

        await db.ReingestAsync(editionId, ("1.1", "New local wording"));

        var reopened = await repository.GetAsync("urn:e:test1", "1.1");
        Assert.NotNull(reopened);
        Assert.Equal("A durable observation", reopened!.AttentionNote);
        Assert.Equal("Old local wording", reopened.Excerpt);
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
