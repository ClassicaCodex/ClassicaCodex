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
    public async Task ResearchLogRecordsChangesAndRetainsManualNotesAfterRelatedRowsAreRemoved()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Authorship inquiry"
        };
        await repo.SaveProjectAsync(project);
        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId, Text = "Does the meter differ?"
        };
        await repo.SaveQuestionAsync(question);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            Title = "Resolved-position frequency"
        };
        await repo.SaveEvidenceAsync(evidence);
        await repo.AddResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = project.ResearchProjectId,
            Summary = "Check the same measure against the Cyclops",
            Details = "A rival genre explanation needs a positive control."
        });

        await repo.DeleteEvidenceAsync(evidence.EvidenceItemId);
        await repo.DeleteQuestionAsync(question.ResearchQuestionId);

        var reopened = await new ResearchRepository().GetResearchLogAsync(project.ResearchProjectId);
        Assert.Contains(reopened, e => e.Kind == ResearchLogEntryKind.ProjectCreated);
        Assert.Contains(reopened, e => e.Kind == ResearchLogEntryKind.QuestionAdded);
        Assert.Contains(reopened, e => e.Kind == ResearchLogEntryKind.EvidenceAdded);
        Assert.Contains(reopened, e => e.Kind == ResearchLogEntryKind.EvidenceRemoved);
        Assert.Contains(reopened, e => e.Kind == ResearchLogEntryKind.QuestionRemoved);
        var note = Assert.Single(reopened, e => e.Kind == ResearchLogEntryKind.ManualNote);
        Assert.Equal("Check the same measure against the Cyclops", note.Summary);
        Assert.Equal("A rival genre explanation needs a positive control.", note.Details);
        Assert.All(reopened, e => Assert.Equal(project.ResearchProjectId, e.ResearchProjectId));
    }

    [Fact]
    public async Task MigrationFromSeventeenPreservesAttributionAndAddsResearchTables()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync("rhesus");
        var workId = await db.WorkIdForAsync("rhesus");
        var workRepo = new WorkRepository();
        await workRepo.SetAttributionAsync(workId, AttributionStatus.Disputed, "My considered view");

        await db.ExecuteAsync("DROP TABLE ScholarlyClaims; DROP TABLE EvidenceGenerationMetadata; DROP TABLE ResearchLogEntries; DROP TABLE EvidenceItems; DROP TABLE ResearchQuestions; DROP TABLE ResearchProjects; PRAGMA user_version=17;");
        await SchemaInitializer.EnsureSchemaAsync();

        var attribution = await workRepo.GetAttributionAsync(workId);
        Assert.Equal(AttributionStatus.Disputed, attribution.Status);
        Assert.Equal("My considered view", attribution.Note);
        Assert.True(attribution.SetByUser);
        Assert.True(await db.TableExistsAsync("ResearchProjects"));
        Assert.True(await db.TableExistsAsync("ResearchLogEntries"));
        Assert.True(await db.TableExistsAsync("EvidenceGenerationMetadata"));
        Assert.True(await db.TableExistsAsync("ScholarlyClaims"));
        Assert.Equal(21, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task MigrationFromEighteenPreservesExistingResearchAndAddsTheLog()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Existing project"
        };
        await repo.SaveProjectAsync(project);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId, Title = "Existing evidence",
            StableIdentifier = "urn:cts:test"
        };
        await repo.SaveEvidenceAsync(evidence);

        await db.ExecuteAsync("DROP TABLE ScholarlyClaims; DROP TABLE EvidenceGenerationMetadata; DROP TABLE ResearchLogEntries; PRAGMA user_version=18;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project",
            Assert.Single(await repo.GetProjectsForWorkAsync(project.WorkId)).Name);
        Assert.Equal("urn:cts:test",
            Assert.Single(await repo.GetEvidenceAsync(project.ResearchProjectId)).StableIdentifier);
        Assert.True(await db.TableExistsAsync("ResearchLogEntries"));
        Assert.True(await db.TableExistsAsync("EvidenceGenerationMetadata"));
        Assert.True(await db.TableExistsAsync("ScholarlyClaims"));
        Assert.Equal(21, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task GeneratedEvidenceKeepsRawTextInterpretationAndGeneratorProvenanceSeparate()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Theory"
        };
        await repo.SaveProjectAsync(project);
        var generated = DateTime.UtcNow;
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "AI candidate: unusual diction",
            Excerpt = "The exact locally verified corpus text.",
            Judgment = EvidenceJudgment.Uncertain,
            Origin = EvidenceOrigin.AiCandidate,
            Interpretation = "Gemini's proposed significance, still unverified.",
            InterpretationAuthor = "Gemini (test-model)",
            GeneratorPrompt = "Theory, questions, corpus scope, and instructions.",
            GeneratedUtc = generated
        };
        await repo.SaveEvidenceAsync(evidence);

        var reopened = Assert.Single(await repo.GetEvidenceAsync(project.ResearchProjectId));
        Assert.Equal(EvidenceOrigin.AiCandidate, reopened.Origin);
        Assert.Equal("The exact locally verified corpus text.", reopened.Excerpt);
        Assert.Equal("Gemini's proposed significance, still unverified.", reopened.Interpretation);
        Assert.Equal("Gemini (test-model)", reopened.InterpretationAuthor);
        Assert.Equal("Theory, questions, corpus scope, and instructions.", reopened.GeneratorPrompt);
        Assert.Equal(generated.ToString("O"), reopened.GeneratedUtc?.ToString("O"));
        Assert.Equal(EvidenceJudgment.Uncertain, reopened.Judgment);
    }

    [Fact]
    public async Task ScholarlyClaimsPersistSourceStanceVerificationAndLocator()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Authorship inquiry"
        };
        await repo.SaveProjectAsync(project);
        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId, Text = "How do scholars explain the diction?"
        };
        await repo.SaveQuestionAsync(question);
        var source = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "Smith 2024",
            Type = EvidenceType.Scholarship,
            StableIdentifier = "doi:10.1234/example"
        };
        await repo.SaveEvidenceAsync(source);
        var claim = new ScholarlyClaim
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            SourceEvidenceItemId = source.EvidenceItemId,
            Claimant = "Smith",
            ClaimText = "The unusual diction reflects genre rather than authorship.",
            Locator = "pp. 42-44",
            Relationship = EvidenceRelationship.Contradicts,
            Judgment = EvidenceJudgment.Accepted,
            Notes = "Compare the stated parallels directly."
        };

        await repo.SaveScholarlyClaimAsync(claim);
        claim.Judgment = EvidenceJudgment.Uncertain;
        claim.Notes = "Verification reopened after checking the sample size.";
        await repo.SaveScholarlyClaimAsync(claim);

        var reopened = Assert.Single(await new ResearchRepository()
            .GetScholarlyClaimsAsync(project.ResearchProjectId));
        Assert.Equal(question.ResearchQuestionId, reopened.ResearchQuestionId);
        Assert.Equal(source.EvidenceItemId, reopened.SourceEvidenceItemId);
        Assert.Equal("Smith", reopened.Claimant);
        Assert.Equal("pp. 42-44", reopened.Locator);
        Assert.Equal(EvidenceRelationship.Contradicts, reopened.Relationship);
        Assert.Equal(EvidenceJudgment.Uncertain, reopened.Judgment);
        Assert.Equal("Verification reopened after checking the sample size.", reopened.Notes);

        var log = await repo.GetResearchLogAsync(project.ResearchProjectId);
        Assert.Contains(log, e => e.Kind == ResearchLogEntryKind.ClaimAdded);
        Assert.Contains(log, e => e.Kind == ResearchLogEntryKind.ClaimUpdated);
    }

    [Fact]
    public async Task ClaimSurvivesRemovedQuestionAndSourceThenCanBeRemoved()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Theory"
        };
        await repo.SaveProjectAsync(project);
        var question = new ResearchQuestion { ResearchProjectId = project.ResearchProjectId, Text = "Question" };
        await repo.SaveQuestionAsync(question);
        var source = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId, Title = "Article", Type = EvidenceType.Scholarship
        };
        await repo.SaveEvidenceAsync(source);
        var claim = new ScholarlyClaim
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            SourceEvidenceItemId = source.EvidenceItemId,
            Claimant = "Scholar", ClaimText = "A proposition"
        };
        await repo.SaveScholarlyClaimAsync(claim);

        await repo.DeleteQuestionAsync(question.ResearchQuestionId);
        await repo.DeleteEvidenceAsync(source.EvidenceItemId);

        var retained = Assert.Single(await repo.GetScholarlyClaimsAsync(project.ResearchProjectId));
        Assert.Null(retained.ResearchQuestionId);
        Assert.Null(retained.SourceEvidenceItemId);
        await repo.DeleteScholarlyClaimAsync(retained.ScholarlyClaimId);
        Assert.Empty(await repo.GetScholarlyClaimsAsync(project.ResearchProjectId));
        Assert.Contains(await repo.GetResearchLogAsync(project.ResearchProjectId),
            e => e.Kind == ResearchLogEntryKind.ClaimRemoved);
    }

    [Fact]
    public async Task ScholarlyClaimRejectsLinksFromAnotherProject()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var workId = await db.WorkIdForAsync("test1");
        var first = new ResearchProject { WorkId = workId, Name = "First" };
        var second = new ResearchProject { WorkId = workId, Name = "Second" };
        await repo.SaveProjectAsync(first);
        await repo.SaveProjectAsync(second);
        var foreignQuestion = new ResearchQuestion
        {
            ResearchProjectId = second.ResearchProjectId, Text = "Other project question"
        };
        await repo.SaveQuestionAsync(foreignQuestion);

        var claim = new ScholarlyClaim
        {
            ResearchProjectId = first.ResearchProjectId,
            ResearchQuestionId = foreignQuestion.ResearchQuestionId,
            Claimant = "Scholar", ClaimText = "Claim"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveScholarlyClaimAsync(claim));
    }

    [Fact]
    public async Task MigrationFromTwentyAddsClaimsWithoutChangingResearchData()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Existing project"
        };
        await repo.SaveProjectAsync(project);

        await db.ExecuteAsync("DROP TABLE ScholarlyClaims; PRAGMA user_version=20;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(
            await repo.GetProjectsForWorkAsync(project.WorkId)).Name);
        Assert.True(await db.TableExistsAsync("ScholarlyClaims"));
        Assert.Equal(21, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }
}
