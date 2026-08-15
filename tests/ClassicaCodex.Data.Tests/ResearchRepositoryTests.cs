using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

[Collection("Database")]
public class ResearchRepositoryTests
{
    [Fact]
    public async Task ProjectQuestionsAndEvidenceReopenWithCompleteProvenance()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync("rhesus");
        var workId = await db.WorkIdForAsync("rhesus");
        var repo = new ResearchRepository();

        var project = new ResearchProject
        {
            WorkId = workId, Name = "Was Rhesus written by Euripides?",
            Notes = "Test rival explanations before deciding."
        };
        await repo.SaveProjectAsync(project);

        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = "Does the diction depart from the secure plays?", SortOrder = 0
        };
        await repo.SaveQuestionAsync(question);

        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            Title = "Opening scene vocabulary",
            Type = EvidenceType.PrimaryText,
            SourceType = "CTS",
            StableIdentifier = "urn:cts:greekLit:tlg0006.tlg017",
            CanonicalReference = "1-51",
            Provenance = "Perseus Greek edition; local corpus snapshot",
            Excerpt = "A summary, not silently promoted interpretation.",
            Judgment = EvidenceJudgment.Accepted,
            Relationship = EvidenceRelationship.Supports,
            ResearcherNote = "Compare against Alcestis next."
        };
        await repo.SaveEvidenceAsync(evidence);

        evidence.Judgment = EvidenceJudgment.Rejected;
        evidence.Relationship = EvidenceRelationship.Contradicts;
        evidence.ResearcherNote = "Rechecked against the edition; keep as a negative finding.";
        await repo.SaveEvidenceAsync(evidence);

        var reopenedProjects = await new ResearchRepository().GetProjectsForWorkAsync(workId);
        var reopenedQuestions = await new ResearchRepository().GetQuestionsAsync(project.ResearchProjectId);
        var reopenedEvidence = await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId);

        Assert.Single(reopenedProjects);
        Assert.Equal(project.Name, reopenedProjects[0].Name);
        Assert.Single(reopenedQuestions);
        Assert.Equal(question.Text, reopenedQuestions[0].Text);
        var item = Assert.Single(reopenedEvidence);
        Assert.Equal(question.ResearchQuestionId, item.ResearchQuestionId);
        Assert.Equal("CTS", item.SourceType);
        Assert.Equal("urn:cts:greekLit:tlg0006.tlg017", item.StableIdentifier);
        Assert.Equal("1-51", item.CanonicalReference);
        Assert.Equal(EvidenceJudgment.Rejected, item.Judgment);
        Assert.Equal(EvidenceRelationship.Contradicts, item.Relationship);
        Assert.Equal("Rechecked against the edition; keep as a negative finding.", item.ResearcherNote);
    }

    [Fact]
    public async Task MultipleProjectsAreScopedToTheirWorkAndArchiveIsRecoverable()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync("one");
        await db.SeedEditionAsync("two");
        var first = await db.WorkIdForAsync("one");
        var second = await db.WorkIdForAsync("two");
        var repo = new ResearchRepository();

        var a = new ResearchProject { WorkId = first, Name = "Theory A" };
        var b = new ResearchProject { WorkId = first, Name = "Theory B" };
        var other = new ResearchProject { WorkId = second, Name = "Other work" };
        await repo.SaveProjectAsync(a);
        await repo.SaveProjectAsync(b);
        await repo.SaveProjectAsync(other);

        Assert.Equal(2, (await repo.GetProjectsForWorkAsync(first)).Count);
        Assert.Single(await repo.GetProjectsForWorkAsync(second));

        await repo.ArchiveProjectAsync(a.ResearchProjectId);
        Assert.Single(await repo.GetProjectsForWorkAsync(first));
        var includingArchived = await repo.GetProjectsForWorkAsync(first, includeArchived: true);
        Assert.Equal(2, includingArchived.Count);
        Assert.Equal(ResearchProjectStatus.Archived,
            includingArchived.Single(p => p.ResearchProjectId == a.ResearchProjectId).Status);
    }

    [Fact]
    public async Task QuestionsCanBeEditedReorderedAndRemovedWithoutLosingEvidence()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject { WorkId = await db.WorkIdForAsync("test1"), Name = "Theory" };
        await repo.SaveProjectAsync(project);
        var first = new ResearchQuestion { ResearchProjectId = project.ResearchProjectId, Text = "First", SortOrder = 0 };
        var second = new ResearchQuestion { ResearchProjectId = project.ResearchProjectId, Text = "Second", SortOrder = 1 };
        await repo.SaveQuestionAsync(first);
        await repo.SaveQuestionAsync(second);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = first.ResearchQuestionId,
            Title = "Source"
        };
        await repo.SaveEvidenceAsync(evidence);

        first.Text = "First, revised";
        await repo.SaveQuestionAsync(first);
        await repo.ReorderQuestionsAsync(new[] { second.ResearchQuestionId, first.ResearchQuestionId });
        var reordered = await repo.GetQuestionsAsync(project.ResearchProjectId);
        Assert.Equal(new[] { "Second", "First, revised" }, reordered.Select(q => q.Text));

        await repo.DeleteQuestionAsync(first.ResearchQuestionId);
        var kept = Assert.Single(await repo.GetEvidenceAsync(project.ResearchProjectId));
        Assert.Null(kept.ResearchQuestionId);
    }

    [Fact]
    public async Task MigrationFromSeventeenPreservesAttributionAndAddsResearchTables()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync("rhesus");
        var workId = await db.WorkIdForAsync("rhesus");
        var workRepo = new WorkRepository();
        await workRepo.SetAttributionAsync(workId, AttributionStatus.Disputed, "My considered view");

        await db.ExecuteAsync("DROP TABLE EvidenceItems; DROP TABLE ResearchQuestions; DROP TABLE ResearchProjects; PRAGMA user_version=17;");
        await SchemaInitializer.EnsureSchemaAsync();

        var attribution = await workRepo.GetAttributionAsync(workId);
        Assert.Equal(AttributionStatus.Disputed, attribution.Status);
        Assert.Equal("My considered view", attribution.Note);
        Assert.True(attribution.SetByUser);
        Assert.True(await db.TableExistsAsync("ResearchProjects"));
        Assert.Equal(18, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }
}
