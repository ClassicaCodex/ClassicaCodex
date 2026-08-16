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
            WorkId = workId,
            Name = "Was Rhesus written by Euripides?",
            Notes = "Test rival explanations before deciding."
        };
        await repo.SaveProjectAsync(project);

        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = "Does the diction depart from the secure plays?",
            SortOrder = 0
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

        // Archiving is meant to be reversible, and the Bench's Restore button is this
        // call. Without it "retained, not deleted" is only true of the database.
        await repo.SetProjectStatusAsync(a.ResearchProjectId, ResearchProjectStatus.Active);
        var restored = await repo.GetProjectsForWorkAsync(first);
        Assert.Equal(2, restored.Count);
        Assert.Equal(ResearchProjectStatus.Active,
            restored.Single(p => p.ResearchProjectId == a.ResearchProjectId).Status);
    }

    [Fact]
    public async Task IncompleteProjectRollbackRemovesTheWholeAggregate()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var workId = await db.WorkIdForAsync("test1");
        var repo = new ResearchRepository();
        var project = new ResearchProject { WorkId = workId, Name = "Interrupted blueprint" };
        await repo.SaveProjectAsync(project);
        await repo.SaveQuestionAsync(new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = "A partially created question"
        });

        await repo.DeleteIncompleteProjectAsync(project.ResearchProjectId);

        Assert.Empty(await repo.GetProjectsForWorkAsync(workId, includeArchived: true));
        Assert.Equal(0, await db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM ResearchQuestions WHERE ResearchProjectId={project.ResearchProjectId};"));
        Assert.Equal(0, await db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM ResearchLogEntries WHERE ResearchProjectId={project.ResearchProjectId};"));
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
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Authorship inquiry"
        };
        await repo.SaveProjectAsync(project);
        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = "Does the meter differ?"
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

        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; DROP TABLE ResearchCorpusSnapshotEntries; DROP TABLE ResearchCorpusSnapshots; DROP TABLE EvidenceBibliographyMetadata; DROP TABLE EvidencePageAnnotations; DROP TABLE EvidenceAttachments; DROP TABLE ScholarlyClaims; DROP TABLE EvidenceGenerationMetadata; DROP TABLE ResearchLogEntries; DROP TABLE EvidenceItems; DROP TABLE ResearchQuestions; DROP TABLE ResearchProjects; PRAGMA user_version=17;");
        await SchemaInitializer.EnsureSchemaAsync();

        var attribution = await workRepo.GetAttributionAsync(workId);
        Assert.Equal(AttributionStatus.Disputed, attribution.Status);
        Assert.Equal("My considered view", attribution.Note);
        Assert.True(attribution.SetByUser);
        Assert.True(await db.TableExistsAsync("ResearchProjects"));
        Assert.True(await db.TableExistsAsync("ResearchLogEntries"));
        Assert.True(await db.TableExistsAsync("EvidenceGenerationMetadata"));
        Assert.True(await db.TableExistsAsync("ScholarlyClaims"));
        Assert.True(await db.TableExistsAsync("EvidenceAttachments"));
        Assert.True(await db.TableExistsAsync("EvidencePageAnnotations"));
        Assert.True(await db.TableExistsAsync("EvidenceBibliographyMetadata"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshots"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshotEntries"));
        Assert.True(await db.TableExistsAsync("ResearchReadingItems"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task MigrationFromEighteenPreservesExistingResearchAndAddsTheLog()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Existing project"
        };
        await repo.SaveProjectAsync(project);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "Existing evidence",
            StableIdentifier = "urn:cts:test"
        };
        await repo.SaveEvidenceAsync(evidence);

        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; DROP TABLE ResearchCorpusSnapshotEntries; DROP TABLE ResearchCorpusSnapshots; DROP TABLE EvidenceBibliographyMetadata; DROP TABLE EvidencePageAnnotations; DROP TABLE EvidenceAttachments; DROP TABLE ScholarlyClaims; DROP TABLE EvidenceGenerationMetadata; DROP TABLE ResearchLogEntries; PRAGMA user_version=18;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project",
            Assert.Single(await repo.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.Equal("urn:cts:test",
            Assert.Single(await repo.GetEvidenceAsync(project.ResearchProjectId)).StableIdentifier);
        Assert.True(await db.TableExistsAsync("ResearchLogEntries"));
        Assert.True(await db.TableExistsAsync("EvidenceGenerationMetadata"));
        Assert.True(await db.TableExistsAsync("ScholarlyClaims"));
        Assert.True(await db.TableExistsAsync("EvidenceAttachments"));
        Assert.True(await db.TableExistsAsync("EvidencePageAnnotations"));
        Assert.True(await db.TableExistsAsync("EvidenceBibliographyMetadata"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshots"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshotEntries"));
        Assert.True(await db.TableExistsAsync("ResearchReadingItems"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task GeneratedEvidenceKeepsRawTextInterpretationAndGeneratorProvenanceSeparate()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Theory"
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
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Authorship inquiry"
        };
        await repo.SaveProjectAsync(project);
        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = "How do scholars explain the diction?"
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
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Theory"
        };
        await repo.SaveProjectAsync(project);
        var question = new ResearchQuestion { ResearchProjectId = project.ResearchProjectId, Text = "Question" };
        await repo.SaveQuestionAsync(question);
        var source = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "Article",
            Type = EvidenceType.Scholarship
        };
        await repo.SaveEvidenceAsync(source);
        var claim = new ScholarlyClaim
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            SourceEvidenceItemId = source.EvidenceItemId,
            Claimant = "Scholar",
            ClaimText = "A proposition"
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
            ResearchProjectId = second.ResearchProjectId,
            Text = "Other project question"
        };
        await repo.SaveQuestionAsync(foreignQuestion);

        var claim = new ScholarlyClaim
        {
            ResearchProjectId = first.ResearchProjectId,
            ResearchQuestionId = foreignQuestion.ResearchQuestionId,
            Claimant = "Scholar",
            ClaimText = "Claim"
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
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Existing project"
        };
        await repo.SaveProjectAsync(project);

        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; DROP TABLE ResearchCorpusSnapshotEntries; DROP TABLE ResearchCorpusSnapshots; DROP TABLE EvidenceBibliographyMetadata; DROP TABLE EvidencePageAnnotations; DROP TABLE EvidenceAttachments; DROP TABLE ScholarlyClaims; PRAGMA user_version=20;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(
            await repo.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.True(await db.TableExistsAsync("ScholarlyClaims"));
        Assert.True(await db.TableExistsAsync("EvidenceAttachments"));
        Assert.True(await db.TableExistsAsync("EvidencePageAnnotations"));
        Assert.True(await db.TableExistsAsync("EvidenceBibliographyMetadata"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshots"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task SourceAttachmentsAndPageNotesPersistAndWriteResearchLog()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var research = new ResearchRepository();
        var sources = new ResearchSourceRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Source reading"
        };
        await research.SaveProjectAsync(project);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "A local article",
            Type = EvidenceType.Scholarship
        };
        await research.SaveEvidenceAsync(evidence);
        var attachment = new EvidenceAttachment
        {
            EvidenceItemId = evidence.EvidenceItemId,
            FilePath = @"C:\sources\article.pdf",
            FileName = "article.pdf",
            MediaType = "application/pdf",
            Sha256 = new string('a', 64),
            FileSize = 12345,
            FileModifiedUtc = DateTime.UtcNow
        };
        await sources.SaveAttachmentAsync(attachment);
        var note = new EvidencePageAnnotation
        {
            EvidenceAttachmentId = attachment.EvidenceAttachmentId,
            PageNumber = 42,
            QuotedText = "The transmitted title is not decisive.",
            Note = "Compare the manuscript discussion.",
            Judgment = EvidenceJudgment.Uncertain
        };
        await sources.SaveAnnotationAsync(note);
        note.Judgment = EvidenceJudgment.Accepted;
        note.Note = "Verified against the printed page.";
        await sources.SaveAnnotationAsync(note);

        var reopenedAttachment = Assert.Single(await new ResearchSourceRepository()
            .GetAttachmentsAsync(evidence.EvidenceItemId));
        Assert.Equal(attachment.Sha256, reopenedAttachment.Sha256);
        var reopenedNote = Assert.Single(await new ResearchSourceRepository()
            .GetAnnotationsAsync(reopenedAttachment.EvidenceAttachmentId));
        Assert.Equal(42, reopenedNote.PageNumber);
        Assert.Equal(EvidenceJudgment.Accepted, reopenedNote.Judgment);
        Assert.Equal("Verified against the printed page.", reopenedNote.Note);

        var log = await research.GetResearchLogAsync(project.ResearchProjectId);
        Assert.Contains(log, e => e.Kind == ResearchLogEntryKind.SourceAttached);
        Assert.Contains(log, e => e.Kind == ResearchLogEntryKind.PageAnnotationAdded);
        Assert.Contains(log, e => e.Kind == ResearchLogEntryKind.PageAnnotationUpdated);

        await sources.DeleteAttachmentAsync(attachment.EvidenceAttachmentId);
        Assert.Empty(await sources.GetAnnotationsAsync(attachment.EvidenceAttachmentId));
        Assert.Contains(await research.GetResearchLogAsync(project.ResearchProjectId),
            e => e.Kind == ResearchLogEntryKind.SourceRemoved);
    }

    [Fact]
    public async Task MigrationFromTwentyOneAddsSourceTablesWithoutChangingResearchData()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Existing project"
        };
        await repo.SaveProjectAsync(project);

        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; DROP TABLE ResearchCorpusSnapshotEntries; DROP TABLE ResearchCorpusSnapshots; DROP TABLE EvidenceBibliographyMetadata; DROP TABLE EvidencePageAnnotations; DROP TABLE EvidenceAttachments; PRAGMA user_version=21;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(
            await repo.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.True(await db.TableExistsAsync("EvidenceAttachments"));
        Assert.True(await db.TableExistsAsync("EvidencePageAnnotations"));
        Assert.True(await db.TableExistsAsync("EvidenceBibliographyMetadata"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshots"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task BibliographyMetadataPersistsStructuredFieldsAndCascadesWithEvidence()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var research = new ResearchRepository();
        var bibliography = new ResearchBibliographyRepository();
        var project = new ResearchProject { WorkId = await db.WorkIdForAsync("test1"), Name = "Sources" };
        await research.SaveProjectAsync(project);
        var evidence = new EvidenceItem { ResearchProjectId = project.ResearchProjectId, Title = "Smith 2024", Type = EvidenceType.Scholarship };
        await research.SaveEvidenceAsync(evidence);
        var metadata = EvidenceBibliographyMetadata.FromRecord(evidence.EvidenceItemId,
            new BibliographyRecord("BibTeX", "ARTICLE", "smith2024", "The Rhesus Question",
                ["Smith, Jane", "Jones, Alex"], "2024", "Classical Quarterly", "74", "2", "100-119",
                null, "10.1234/Example", null, null, "Abstract", ["authorship", "tragedy"]));

        await bibliography.SaveAsync(metadata);
        metadata.CiteKey = "smith2024rhesus";
        await bibliography.SaveAsync(metadata);

        var reopened = Assert.Single(await new ResearchBibliographyRepository().GetForProjectAsync(project.ResearchProjectId));
        Assert.True(reopened.IsStored); Assert.Equal("smith2024rhesus", reopened.CiteKey);
        Assert.Equal(new[] { "Smith, Jane", "Jones, Alex" }, reopened.Authors);
        Assert.Equal(new[] { "authorship", "tragedy" }, reopened.Keywords);
        Assert.Equal("10.1234/example", reopened.Doi);

        await research.DeleteEvidenceAsync(evidence.EvidenceItemId);
        Assert.Empty(await bibliography.GetForProjectAsync(project.ResearchProjectId));
        Assert.Equal(0, await db.CountAsync("EvidenceBibliographyMetadata"));
    }

    [Fact]
    public async Task MigrationFromTwentyTwoAddsBibliographyMetadataWithoutChangingResearchData()
    {
        using var db = await TempDatabase.CreateAsync(); await db.SeedEditionAsync();
        var repo = new ResearchRepository(); var project = new ResearchProject { WorkId = await db.WorkIdForAsync("test1"), Name = "Existing project" };
        await repo.SaveProjectAsync(project);
        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; DROP TABLE ResearchCorpusSnapshotEntries; DROP TABLE ResearchCorpusSnapshots; DROP TABLE EvidenceBibliographyMetadata; PRAGMA user_version=22;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(await repo.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.True(await db.TableExistsAsync("EvidenceBibliographyMetadata"));
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshots"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task EarlierFlattenedBibliographyImportRecoversItsCiteKey()
    {
        using var db = await TempDatabase.CreateAsync(); await db.SeedEditionAsync();
        var research = new ResearchRepository(); var project = new ResearchProject { WorkId = await db.WorkIdForAsync("test1"), Name = "Sources" };
        await research.SaveProjectAsync(project);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "Smith 2024 — Rhesus",
            Type = EvidenceType.Scholarship,
            Provenance = "Smith, Jane. (2024). Rhesus. Imported from BibTeX metadata; cite key smith2024."
        };
        await research.SaveEvidenceAsync(evidence);

        var recovered = Assert.Single(await new ResearchBibliographyRepository().GetForProjectAsync(project.ResearchProjectId));

        Assert.False(recovered.IsStored);
        Assert.Equal("smith2024", recovered.CiteKey);
    }

    [Fact]
    public async Task CorpusSnapshotDetectsTextAttributionAndEditionDrift()
    {
        using var db = await TempDatabase.CreateAsync(); var editionId = await db.SeedEditionAsync("rhesus");
        await db.InsertLinesAsync(editionId, ("1", "πρῶτον κείμενον"), ("2", "δεύτερον κείμενον"));
        var workId = await db.WorkIdForAsync("rhesus"); var research = new ResearchRepository();
        var project = new ResearchProject { WorkId = workId, Name = "Rhesus authorship" }; await research.SaveProjectAsync(project);
        var snapshots = new ResearchCorpusSnapshotRepository();

        var snapshot = await snapshots.CaptureAsync(project.ResearchProjectId, "Baseline", CorpusSnapshotScope.ProjectWork, "4.2.0", "Before re-ingest");
        var unchanged = await snapshots.CompareAsync(snapshot);

        Assert.Equal(1, snapshot.WorkCount); Assert.Equal(1, snapshot.EditionCount); Assert.Equal(2, snapshot.TextNodeCount);
        Assert.Equal(1, unchanged.Unchanged); Assert.Empty(unchanged.Differences);
        var frozen = Assert.Single(await snapshots.GetEntriesAsync(snapshot.ResearchCorpusSnapshotId));
        Assert.Equal(64, frozen.ContentSha256!.Length); Assert.Equal("4.2.0", snapshot.AppVersion);

        await db.ExecuteAsync($"UPDATE TextNodes SET Text='changed text' WHERE EditionId={editionId} AND CitationRef='2';");
        await new WorkRepository().SetAttributionAsync(workId, AttributionStatus.Disputed, "Reconsidered");
        await db.SeedSiblingEditionAsync("rhesus", "rhesus-translation", "Translation", "eng", "Tester");
        var drift = await snapshots.CompareAsync(snapshot);

        Assert.Equal(1, drift.Changed); Assert.Equal(1, drift.Added); Assert.Equal(0, drift.Missing);
        Assert.Contains(drift.Differences, d => d.Status == "Changed" && d.Details.Contains("fingerprint changed"));
        Assert.Contains(drift.Differences, d => d.Status == "Changed" && d.Details.Contains("attribution accepted → disputed"));
        Assert.Contains(await research.GetResearchLogAsync(project.ResearchProjectId), e => e.Kind == ResearchLogEntryKind.CorpusSnapshotCaptured);

        await snapshots.DeleteAsync(snapshot.ResearchCorpusSnapshotId);
        Assert.Empty(await snapshots.GetSnapshotsAsync(project.ResearchProjectId));
        Assert.Equal(0, await db.CountAsync("ResearchCorpusSnapshotEntries"));
        Assert.Contains(await research.GetResearchLogAsync(project.ResearchProjectId), e => e.Kind == ResearchLogEntryKind.CorpusSnapshotRemoved);
    }

    [Fact]
    public async Task SnapshotScopesStayWithinWorkAuthorOrEntireCorpus()
    {
        using var db = await TempDatabase.CreateAsync(); await db.SeedEditionAsync("one"); await db.SeedEditionAsync("other-author");
        var firstWork = await db.WorkIdForAsync("one");
        await db.ExecuteAsync($@"INSERT INTO Works(AuthorId,CtsUrn,Title) SELECT AuthorId,'urn:w:sibling','Sibling' FROM Works WHERE WorkId={firstWork};
            INSERT INTO Editions(WorkId,CtsUrn,Kind,Language) VALUES((SELECT WorkId FROM Works WHERE CtsUrn='urn:w:sibling'),'urn:e:sibling','Original','grc');");
        var research = new ResearchRepository(); var project = new ResearchProject { WorkId = firstWork, Name = "Scope" }; await research.SaveProjectAsync(project);
        var repo = new ResearchCorpusSnapshotRepository();

        var work = await repo.CaptureAsync(project.ResearchProjectId, "Work", CorpusSnapshotScope.ProjectWork, "test");
        var author = await repo.CaptureAsync(project.ResearchProjectId, "Author", CorpusSnapshotScope.SameAuthor, "test");
        var corpus = await repo.CaptureAsync(project.ResearchProjectId, "Corpus", CorpusSnapshotScope.EntireCorpus, "test");

        Assert.Equal(1, work.WorkCount); Assert.Equal(2, author.WorkCount); Assert.Equal(3, corpus.WorkCount);
    }

    [Fact]
    public async Task MigrationFromTwentyThreeAddsCorpusSnapshotsWithoutChangingResearchData()
    {
        using var db = await TempDatabase.CreateAsync(); await db.SeedEditionAsync(); var repo = new ResearchRepository();
        var project = new ResearchProject { WorkId = await db.WorkIdForAsync("test1"), Name = "Existing project" }; await repo.SaveProjectAsync(project);
        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; DROP TABLE ResearchCorpusSnapshotEntries; DROP TABLE ResearchCorpusSnapshots; PRAGMA user_version=23;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(await repo.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshots")); Assert.True(await db.TableExistsAsync("ResearchCorpusSnapshotEntries"));
        Assert.True(await db.TableExistsAsync("ResearchReadingItems"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task ReadingQueueReopensAndPromotionKeepsTheHumanReviewBoundary()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync("rhesus");
        var research = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("rhesus"),
            Name = "Rhesus attribution"
        };
        await research.SaveProjectAsync(project);
        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = "What does the opening scene contribute?"
        };
        await research.SaveQuestionAsync(question);
        var queue = new ResearchReadingQueueRepository();
        var reading = new ResearchReadingItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            Kind = ResearchReadingKind.CorpusPassage,
            Status = ResearchReadingStatus.Reviewed,
            Priority = ResearchReadingPriority.High,
            Title = "Rhesus 1–10",
            Purpose = "Test the diction of the watch scene.",
            WorkCtsUrn = "urn:cts:test:rhesus",
            EditionCtsUrn = "urn:cts:test:rhesus.edition",
            CitationRef = "1",
            Quotation = "A citable passage",
            Notes = "Human reading note"
        };

        await queue.SaveAsync(reading);
        var reopened = Assert.Single(await new ResearchReadingQueueRepository().GetAsync(project.ResearchProjectId));
        Assert.Equal(ResearchReadingPriority.High, reopened.Priority);
        Assert.Equal("A citable passage", reopened.Quotation);
        Assert.Equal("Human reading note", reopened.Notes);
        Assert.Null(reopened.PromotedEvidenceItemId);

        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = question.ResearchQuestionId,
            Title = reopened.Title,
            Excerpt = reopened.Quotation,
            Origin = EvidenceOrigin.Manual
        };
        await research.SaveEvidenceAsync(evidence);
        await queue.MarkPromotedAsync(reading.ResearchReadingItemId, evidence.EvidenceItemId);

        reopened = Assert.Single(await queue.GetAsync(project.ResearchProjectId));
        Assert.Equal(evidence.EvidenceItemId, reopened.PromotedEvidenceItemId);
        Assert.Equal(ResearchReadingStatus.Reviewed, reopened.Status);
        Assert.Contains(await research.GetResearchLogAsync(project.ResearchProjectId),
            entry => entry.Kind == ResearchLogEntryKind.ReadingItemPromoted);

        await research.DeleteEvidenceAsync(evidence.EvidenceItemId);
        reopened = Assert.Single(await queue.GetAsync(project.ResearchProjectId));
        Assert.Null(reopened.PromotedEvidenceItemId);
    }

    [Fact]
    public async Task ReadingNotesDoNotRoundTripIntoTheQuotationColumn()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var research = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Field mapping"
        };
        await research.SaveProjectAsync(project);
        var queue = new ResearchReadingQueueRepository();
        var reading = new ResearchReadingItem
        {
            ResearchProjectId = project.ResearchProjectId,
            Kind = ResearchReadingKind.ExternalSource,
            Title = "Notes-only source",
            Notes = "This belongs only in reading notes."
        };

        await queue.SaveAsync(reading);

        var reopened = Assert.Single(await queue.GetAsync(project.ResearchProjectId));
        Assert.Null(reopened.Quotation);
        Assert.Equal("This belongs only in reading notes.", reopened.Notes);
    }

    [Fact]
    public async Task ReadingQueueRejectsLinksAcrossResearchProjects()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var workId = await db.WorkIdForAsync("test1");
        var research = new ResearchRepository();
        var first = new ResearchProject { WorkId = workId, Name = "First" };
        var second = new ResearchProject { WorkId = workId, Name = "Second" };
        await research.SaveProjectAsync(first);
        await research.SaveProjectAsync(second);
        var foreignQuestion = new ResearchQuestion { ResearchProjectId = second.ResearchProjectId, Text = "Foreign" };
        await research.SaveQuestionAsync(foreignQuestion);

        var item = new ResearchReadingItem
        {
            ResearchProjectId = first.ResearchProjectId,
            ResearchQuestionId = foreignQuestion.ResearchQuestionId,
            Kind = ResearchReadingKind.ExternalSource,
            Title = "Should fail"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => new ResearchReadingQueueRepository().SaveAsync(item));
    }

    [Fact]
    public async Task MigrationFromTwentyFourAddsTheReadingQueueWithoutChangingResearchData()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var research = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"),
            Name = "Existing project"
        };
        await research.SaveProjectAsync(project);
        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; DROP TABLE ResearchReadingItems; PRAGMA user_version=24;");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(await research.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.True(await db.TableExistsAsync("ResearchReadingItems"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task FindingsPersistHumanAndAiSynthesisSeparatelyWithEvidenceRoles()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync("rhesus");
        var research = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("rhesus"), Name = "Rhesus attribution"
        };
        await research.SaveProjectAsync(project);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId, Title = "Stylometric distance",
            Excerpt = "Rhesus is an outlier.", Judgment = EvidenceJudgment.Accepted
        };
        await research.SaveEvidenceAsync(evidence);
        var repo = new ResearchFindingRepository();
        var finding = new ResearchFinding
        {
            ResearchProjectId = project.ResearchProjectId,
            Title = "Authorship remains contested",
            Statement = "The current evidence does not securely support Euripidean authorship.",
            Status = ResearchFindingStatus.Contested,
            ResearcherConclusion = "The stylometry is suggestive but not independently decisive.",
            AiCandidateSynthesis = "A provisional model-generated counterargument.",
            AiModel = "test-model",
            AiPrompt = "Exact test prompt",
            AiGeneratedUtc = DateTime.UtcNow
        };
        await repo.SaveAsync(finding);
        await repo.SaveLinksAsync(finding.ResearchFindingId,
        [
            new ResearchFindingEvidenceLink
            {
                ResearchFindingId = finding.ResearchFindingId,
                EvidenceItemId = evidence.EvidenceItemId,
                Relationship = EvidenceRelationship.Supports
            }
        ]);

        var reopened = Assert.Single(await new ResearchFindingRepository().GetAsync(project.ResearchProjectId));
        Assert.Equal(ResearchFindingStatus.Contested, reopened.Status);
        Assert.Equal("The stylometry is suggestive but not independently decisive.", reopened.ResearcherConclusion);
        Assert.Equal("A provisional model-generated counterargument.", reopened.AiCandidateSynthesis);
        Assert.Equal("Exact test prompt", reopened.AiPrompt);
        Assert.Equal(EvidenceRelationship.Supports,
            Assert.Single(await repo.GetLinksAsync(finding.ResearchFindingId)).Relationship);

        await research.DeleteEvidenceAsync(evidence.EvidenceItemId);
        Assert.Empty(await repo.GetLinksAsync(finding.ResearchFindingId));
        Assert.Single(await repo.GetAsync(project.ResearchProjectId));
    }

    [Fact]
    public async Task FindingEvidenceCannotCrossProjectBoundaries()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var workId = await db.WorkIdForAsync("test1");
        var research = new ResearchRepository();
        var first = new ResearchProject { WorkId = workId, Name = "First" };
        var second = new ResearchProject { WorkId = workId, Name = "Second" };
        await research.SaveProjectAsync(first);
        await research.SaveProjectAsync(second);
        var foreignEvidence = new EvidenceItem { ResearchProjectId = second.ResearchProjectId, Title = "Foreign" };
        await research.SaveEvidenceAsync(foreignEvidence);
        var repo = new ResearchFindingRepository();
        var finding = new ResearchFinding
        {
            ResearchProjectId = first.ResearchProjectId, Title = "Finding", Statement = "Proposition"
        };
        await repo.SaveAsync(finding);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveLinksAsync(finding.ResearchFindingId,
        [
            new ResearchFindingEvidenceLink
            {
                ResearchFindingId = finding.ResearchFindingId,
                EvidenceItemId = foreignEvidence.EvidenceItemId
            }
        ]));
    }

    [Fact]
    public async Task MigrationFromTwentyFiveAddsFindingsWithoutChangingResearchData()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.SeedEditionAsync();
        var research = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = await db.WorkIdForAsync("test1"), Name = "Existing project"
        };
        await research.SaveProjectAsync(project);
        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; DROP TABLE ResearchFindingEvidence; DROP TABLE ResearchFindings; PRAGMA user_version=25;");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal("Existing project", Assert.Single(await research.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);
        Assert.True(await db.TableExistsAsync("ResearchFindings"));
        Assert.True(await db.TableExistsAsync("ResearchFindingEvidence"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public void ResearchDossierLabelsAiCandidateApartFromResearcherConclusion()
    {
        var project = new ResearchProject { Name = "Theory", Status = ResearchProjectStatus.Active };
        var finding = new ResearchFinding
        {
            ResearchFindingId = 1, Title = "Finding", Statement = "A proposition",
            ResearcherConclusion = "Human conclusion", AiCandidateSynthesis = "Machine candidate",
            AiModel = "test-model", AiGeneratedUtc = DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime()
        };
        var markdown = ResearchDossierExport.ToMarkdown(new ResearchDossierData(project, "Rhesus", "Euripides",
            [], [], [], [finding], new Dictionary<long, IReadOnlyList<ResearchFindingEvidenceLink>>(), [], []));

        Assert.Contains("Researcher conclusion", markdown);
        Assert.Contains("Human conclusion", markdown);
        Assert.Contains("AI candidate synthesis", markdown);
        Assert.Contains("Machine candidate", markdown);
        Assert.Contains("test-model", markdown);
    }

    [Fact]
    public async Task EchoInvestigationPreservesStablePassagesAiProvenanceAndReview()
    {
        using var db = await TempDatabase.CreateAsync();
        var sourceEdition = await db.SeedFullEditionAsync("source", "Euripides", "greekLit", "Rhesus", "Original", "grc");
        var targetEdition = await db.SeedFullEditionAsync("target", "Aeschylus", "greekLit", "Agamemnon", "Original", "grc");
        await db.InsertLinesAsync(sourceEdition, ("1", "source passage"));
        await db.InsertLinesAsync(targetEdition, ("2", "target passage"));
        var sourceNode = await db.TextNodeIdAsync(sourceEdition, "1");
        var targetNode = await db.TextNodeIdAsync(targetEdition, "2");
        var text = new TextNodeRepository();
        var source = Assert.IsType<PassageResearchIdentity>(await text.GetPassageResearchIdentityAsync(sourceNode));
        var research = new ResearchRepository();
        var project = new ResearchProject { WorkId = source.WorkId, Name = "Authorship and reception" };
        await research.SaveProjectAsync(project);

        var repo = new ResearchEchoRepository();
        var saved = await repo.SaveCaptureAsync(project.ResearchProjectId, null, null,
            new EchoCaptureRequest(ResearchEchoMethod.AiCrossLanguage, source, "Possible tragic echo",
                "Aeschylus, Agamemnon", "citations resolved locally", "gemini-test", "full prompt",
                DateTime.Parse("2026-08-15T12:00:00Z").ToUniversalTime(),
                [new EchoCaptureCandidate(await db.WorkIdForAsync("target"), targetNode, "Aeschylus", "Agamemnon", "2", "target passage", null, "medium", "shared image")]));

        var reopened = Assert.Single(await repo.GetInvestigationsAsync(project.ResearchProjectId));
        Assert.Equal(saved.ResearchEchoInvestigationId, reopened.ResearchEchoInvestigationId);
        Assert.Equal("urn:e:source", reopened.SourceEditionCtsUrn);
        Assert.Equal("grc", reopened.SourceLanguage);
        Assert.Equal("gemini-test", reopened.AiModel);
        Assert.Equal("full prompt", reopened.AiPrompt);
        var result = Assert.Single(await repo.GetResultsAsync(reopened.ResearchEchoInvestigationId));
        Assert.Equal("urn:e:target", result.TargetEditionCtsUrn);
        Assert.Equal("grc", result.TargetLanguage);
        Assert.Equal("shared image", result.Rationale);

        await repo.SaveReviewAsync(result.ResearchEchoResultId, ResearchEchoDisposition.Accepted, "Worth comparing closely");
        result = Assert.Single(await repo.GetResultsAsync(reopened.ResearchEchoInvestigationId));
        Assert.Equal(ResearchEchoDisposition.Accepted, result.Disposition);
        Assert.Equal("Worth comparing closely", result.ResearcherNote);

        await repo.SaveParallelClassificationAsync(result.ResearchEchoResultId,
            ResearchEchoConnectionType.Imagistic, ResearchEchoDirectionality.Unknown,
            "night watch, deception", "The dramatic function differs.");
        var ai = new GeminiParallelAnalysisResult("gemini-test", "bounded prompt", "Cautious summary",
            "Both stage a nocturnal watch", "Different speakers", "No secure verbal overlap",
            "Generic tragic convention", "Check chronology and surrounding scenes", "night watch",
            "Imagistic", "Unknown");
        var analysisId = await repo.SaveParallelAnalysisAsync(result.ResearchEchoResultId, ai);
        result = Assert.Single(await repo.GetResultsAsync(reopened.ResearchEchoInvestigationId));
        Assert.Equal(ResearchEchoConnectionType.Imagistic, result.ConnectionType);
        Assert.Equal("night watch, deception", result.MotifTags);
        var storedAnalysis = Assert.Single(await repo.GetParallelAnalysesAsync(result.ResearchEchoResultId));
        Assert.Equal(analysisId, storedAnalysis.ResearchEchoParallelAnalysisId);
        Assert.Equal("Generic tragic convention", storedAnalysis.AlternativeExplanations);
        Assert.Equal(ResearchEchoConnectionType.Imagistic, storedAnalysis.SuggestedConnectionType);
        var atlas = Assert.Single(await repo.GetAtlasConnectionsAsync(project.ResearchProjectId));
        Assert.Equal("Euripides — Rhesus", atlas.SourceLabel);
        Assert.Equal("Aeschylus — Agamemnon", atlas.TargetLabel);
        Assert.Equal(["night watch", "deception"], atlas.Motifs);

        var evidence = new EvidenceItem { ResearchProjectId = project.ResearchProjectId, Title = "Paired passages" };
        var evidenceId = await research.SaveEvidenceAsync(evidence);
        await repo.MarkPromotedAsync(result.ResearchEchoResultId, evidenceId);
        Assert.Equal(evidenceId, Assert.Single(await repo.GetResultsAsync(reopened.ResearchEchoInvestigationId)).EvidenceItemId);

        await db.ReingestAsync(sourceEdition, ("1", "source passage"));
        var resolved = await text.ResolvePassageNavigationAsync(
            reopened.SourceTextNodeId, reopened.SourceEditionCtsUrn, reopened.SourceCitationRef, reopened.SourceText);
        Assert.NotNull(resolved);
        Assert.NotEqual(sourceNode, resolved.Value.TextNodeId);
    }

    [Fact]
    public async Task MigrationFromTwentySixAddsEchoInvestigationTables()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync("DROP TABLE ResearchEchoParallelAnalyses; DROP TABLE ResearchEchoResults; DROP TABLE ResearchEchoInvestigations; PRAGMA user_version=26;");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.True(await db.TableExistsAsync("ResearchEchoInvestigations"));
        Assert.True(await db.TableExistsAsync("ResearchEchoResults"));
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task MigrationFromTwentySevenAddsParallelPassageStudioData()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"DROP TABLE ResearchEchoParallelAnalyses;
            ALTER TABLE ResearchEchoInvestigations DROP COLUMN SourceLanguage;
            ALTER TABLE ResearchEchoResults DROP COLUMN TargetLanguage;
            ALTER TABLE ResearchEchoResults DROP COLUMN ConnectionType;
            ALTER TABLE ResearchEchoResults DROP COLUMN Directionality;
            ALTER TABLE ResearchEchoResults DROP COLUMN MotifTags;
            ALTER TABLE ResearchEchoResults DROP COLUMN ParallelNote;
            PRAGMA user_version=27;");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.True(await db.TableExistsAsync("ResearchEchoParallelAnalyses"));
        var resultColumns = await db.ColumnNamesAsync("ResearchEchoResults");
        Assert.Contains("ConnectionType", resultColumns);
        Assert.Contains("MotifTags", resultColumns);
        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public void ParallelAnalysisParserRetainsGroundedSectionsAndSuggestions()
    {
        var parsed = GeminiTranslationService.ParseParallelAnalysis(
            """{"summary":"Qualified","sharedFeatures":"night image","importantDifferences":"different function","lexicalObservations":"none","alternativeExplanations":"genre","verificationTasks":"check context","suggestedMotifs":"night watch","suggestedConnectionType":"Imagistic","suggestedDirectionality":"Unknown"}""",
            "model", "prompt");

        Assert.Equal("Qualified", parsed.Summary);
        Assert.Equal("genre", parsed.AlternativeExplanations);
        Assert.Equal("Imagistic", parsed.SuggestedConnectionType);
        Assert.Equal("prompt", parsed.PromptProvenance);
    }

    [Fact]
    public async Task DeletingAWorkDetachesItsProjectsRatherThanFailing()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync("rhesus");
        var workId = await db.WorkIdForAsync("rhesus");
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = workId, WorkCtsUrn = "urn:w:rhesus", Name = "Was Rhesus written by Euripides?"
        };
        await repo.SaveProjectAsync(project);

        // Exactly what a Menota re-ingest does when it replaces the last edition of a
        // work. Before schema 31 this threw a raw constraint error, aborted the import
        // part-way, and left the researcher no way to clear it.
        await db.ExecuteAsync($"DELETE FROM Editions WHERE EditionId = {editionId};");
        await db.ExecuteAsync($"DELETE FROM Works WHERE WorkId = {workId};");

        Assert.Equal(1L, await db.CountAsync("ResearchProjects"));
        Assert.Equal(0L, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM ResearchProjects WHERE WorkId IS NOT NULL;"));
        Assert.Empty(await repo.GetProjectsForWorkAsync(workId));
    }

    [Fact]
    public async Task AReimportedWorkReadoptsTheProjectsItLeftBehind()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync("rhesus");
        await db.SeedEditionAsync("alcestis"); // so the re-import cannot reuse the vacated row id
        var workId = await db.WorkIdForAsync("rhesus");
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = workId, WorkCtsUrn = "urn:w:rhesus", Name = "Rhesus diction"
        };
        await repo.SaveProjectAsync(project);

        await db.ExecuteAsync($"DELETE FROM Editions WHERE EditionId = {editionId};");
        await db.ExecuteAsync($"DELETE FROM Works WHERE WorkId = {workId};");
        await db.ExecuteAsync(@"INSERT INTO Works (AuthorId, CtsUrn, Title)
            VALUES ((SELECT AuthorId FROM Authors WHERE CtsUrn = 'urn:a:rhesus'), 'urn:w:rhesus', 'Iliad');");
        var reimportedWorkId = await db.WorkIdForAsync("rhesus");
        Assert.NotEqual(workId, reimportedWorkId);

        // The row id is new, so the CTS identity is the only thing that can reconnect
        // the two - which is the whole reason WorkCtsUrn is stored on the project.
        var reattached = Assert.Single(
            await repo.GetProjectsForWorkAsync(reimportedWorkId, workCtsUrn: "urn:w:rhesus"));
        Assert.Equal(project.ResearchProjectId, reattached.ResearchProjectId);
        Assert.Equal(reimportedWorkId, reattached.WorkId);
    }

    [Fact]
    public async Task ADetachedProjectSaysItsWorkIsGoneRatherThanThatItDoesNotExist()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync("rhesus");
        var workId = await db.WorkIdForAsync("rhesus");
        var repo = new ResearchRepository();
        var project = new ResearchProject
        {
            WorkId = workId, WorkCtsUrn = "urn:w:rhesus", Name = "Rhesus diction"
        };
        await repo.SaveProjectAsync(project);
        await db.ExecuteAsync($"DELETE FROM Editions WHERE EditionId = {editionId};");
        await db.ExecuteAsync($"DELETE FROM Works WHERE WorkId = {workId};");

        // A NULL WorkId used to be read as "no such project", which sends the
        // researcher looking for something that is sitting right there.
        var snapshots = new ResearchCorpusSnapshotRepository();
        var error = await Assert.ThrowsAsync<ArgumentException>(() => snapshots.CaptureAsync(
            project.ResearchProjectId, "Baseline", CorpusSnapshotScope.ProjectWork, "test"));
        Assert.Contains("not attached to a work", error.Message);
    }

    [Fact]
    public async Task TheAtlasStillShowsAProjectWhoseWorkHasLeftTheLibrary()
    {
        using var db = await TempDatabase.CreateAsync();
        var sourceEdition = await db.SeedFullEditionAsync("source", "Euripides", "greekLit", "Rhesus", "Original", "grc");
        var targetEdition = await db.SeedFullEditionAsync("target", "Aeschylus", "greekLit", "Agamemnon", "Original", "grc");
        await db.InsertLinesAsync(sourceEdition, ("1", "source passage"));
        await db.InsertLinesAsync(targetEdition, ("2", "target passage"));
        var sourceNode = await db.TextNodeIdAsync(sourceEdition, "1");
        var targetNode = await db.TextNodeIdAsync(targetEdition, "2");
        var source = Assert.IsType<PassageResearchIdentity>(
            await new TextNodeRepository().GetPassageResearchIdentityAsync(sourceNode));
        var sourceWorkId = await db.WorkIdForAsync("source");
        var project = new ResearchProject
        {
            WorkId = sourceWorkId, WorkCtsUrn = "urn:w:source", Name = "Authorship and reception"
        };
        await new ResearchRepository().SaveProjectAsync(project);

        var repo = new ResearchEchoRepository();
        await repo.SaveCaptureAsync(project.ResearchProjectId, null, null,
            new EchoCaptureRequest(ResearchEchoMethod.AiCrossLanguage, source, "Possible tragic echo",
                "Aeschylus, Agamemnon", "citations resolved locally", "gemini-test", "full prompt",
                DateTime.Parse("2026-08-15T12:00:00Z").ToUniversalTime(),
                [new EchoCaptureCandidate(await db.WorkIdForAsync("target"), targetNode,
                    "Aeschylus", "Agamemnon", "2", "target passage", null, "medium", "shared image")]));
        Assert.Single(await repo.GetAtlasConnectionsAsync(project.ResearchProjectId));

        await db.ExecuteAsync($"DELETE FROM TextNodes WHERE EditionId = {sourceEdition};");
        await db.ExecuteAsync($"DELETE FROM Editions WHERE EditionId = {sourceEdition};");
        await db.ExecuteAsync($"DELETE FROM Works WHERE WorkId = {sourceWorkId};");

        // The Bench lists projects by WorkId and so cannot show a detached one at all.
        // If the Atlas dropped it too, migration 31 would be preserving research that
        // nothing in the application could reach.
        var detached = Assert.Single(await repo.GetAtlasConnectionsAsync(project.ResearchProjectId));
        Assert.Null(detached.Project.WorkId);
        Assert.Contains("no longer in the library", detached.SourceLabel);
    }
}
