using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class ResearchHypothesisTests
{
    [Fact]
    public async Task HypothesesAssessmentsAndExperimentsRoundTripWithSeparateAiProvenance()
    {
        using var db=await TempDatabase.CreateAsync();await db.SeedEditionAsync("rhesus");
        var research=new ResearchRepository();var project=new ResearchProject{WorkId=await db.WorkIdForAsync("rhesus"),Name="Rhesus attribution"};await research.SaveProjectAsync(project);
        var evidence=new EvidenceItem{ResearchProjectId=project.ResearchProjectId,Title="Delta result",Judgment=EvidenceJudgment.Accepted,Excerpt="Rhesus remains an outlier."};await research.SaveEvidenceAsync(evidence);
        var repo=new ResearchHypothesisRepository();var hypothesis=new ResearchHypothesis{ResearchProjectId=project.ResearchProjectId,Title="Non-Euripidean author",Statement="A different tragedian composed the transmitted play.",Status=ResearchHypothesisStatus.Active};await repo.SaveHypothesisAsync(hypothesis);
        await repo.SaveAssessmentsAsync(hypothesis.ResearchHypothesisId,[new ResearchHypothesisAssessment{ResearchHypothesisId=hypothesis.ResearchHypothesisId,SourceKind=HypothesisSourceKind.Evidence,SourceId=evidence.EvidenceItemId,Relationship=HypothesisRelationship.Supports,Strength=HypothesisStrength.Moderate,ResearcherNote="Diagnostic only if the control set is sound."}]);
        var experiment=new ResearchExperiment{ResearchProjectId=project.ResearchProjectId,ResearchHypothesisId=hypothesis.ResearchHypothesisId,Title="Exclude disputed controls",Method=ResearchExperimentMethod.Stylometry,PredictedOutcome="The outlier persists.",FalsificationCriterion="The distance collapses under accepted-only controls.",Origin=EvidenceOrigin.AiCandidate,AiModel="test-model",AiPrompt="bounded prompt",AiGeneratedUtc=DateTime.UtcNow};await repo.SaveExperimentAsync(experiment);

        var reopened=Assert.Single(await repo.GetHypothesesAsync(project.ResearchProjectId));Assert.Equal("Non-Euripidean author",reopened.Title);
        var assessment=Assert.Single(await repo.GetAssessmentsAsync(reopened.ResearchHypothesisId));Assert.Equal(HypothesisRelationship.Supports,assessment.Relationship);Assert.Equal(HypothesisStrength.Moderate,assessment.Strength);
        Assert.Contains(await repo.GetSourcesAsync(project.ResearchProjectId),s=>s.Kind==HypothesisSourceKind.Evidence&&s.Id==evidence.EvidenceItemId);
        var savedExperiment=Assert.Single(await repo.GetExperimentsAsync(project.ResearchProjectId));Assert.Equal(ResearchExperimentMethod.Stylometry,savedExperiment.Method);Assert.Equal("bounded prompt",savedExperiment.AiPrompt);Assert.Equal(EvidenceOrigin.AiCandidate,savedExperiment.Origin);
    }

    [Fact]
    public async Task ResavingTheMatrixKeepsWhenEachJudgmentWasFirstRecorded()
    {
        using var db=await TempDatabase.CreateAsync();await db.SeedEditionAsync("rhesus");
        var research=new ResearchRepository();var project=new ResearchProject{WorkId=await db.WorkIdForAsync("rhesus"),Name="Provenance"};await research.SaveProjectAsync(project);
        var first=new EvidenceItem{ResearchProjectId=project.ResearchProjectId,Title="Weighed months ago"};await research.SaveEvidenceAsync(first);
        var second=new EvidenceItem{ResearchProjectId=project.ResearchProjectId,Title="Weighed today"};await research.SaveEvidenceAsync(second);
        var repo=new ResearchHypothesisRepository();var hypothesis=new ResearchHypothesis{ResearchProjectId=project.ResearchProjectId,Title="Non-Euripidean author",Statement="A different tragedian composed it."};await repo.SaveHypothesisAsync(hypothesis);

        await repo.SaveAssessmentsAsync(hypothesis.ResearchHypothesisId,[new ResearchHypothesisAssessment{SourceKind=HypothesisSourceKind.Evidence,SourceId=first.EvidenceItemId,Relationship=HypothesisRelationship.Supports,Strength=HypothesisStrength.Moderate}]);
        var originallyRecorded=Assert.Single(await repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId)).CreatedUtc;
        await db.ExecuteAsync($"UPDATE ResearchHypothesisAssessments SET CreatedUtc='2026-01-05T09:00:00.0000000Z' WHERE ResearchHypothesisId={hypothesis.ResearchHypothesisId};");
        Assert.NotEqual(originallyRecorded,DateTime.Parse("2026-08-16T00:00:00Z").ToUniversalTime());

        // Adding a second assessment rewrites the whole matrix, which used to restamp
        // the first one as though the researcher had weighed that evidence today.
        await repo.SaveAssessmentsAsync(hypothesis.ResearchHypothesisId,[
            new ResearchHypothesisAssessment{SourceKind=HypothesisSourceKind.Evidence,SourceId=first.EvidenceItemId,Relationship=HypothesisRelationship.Supports,Strength=HypothesisStrength.Strong},
            new ResearchHypothesisAssessment{SourceKind=HypothesisSourceKind.Evidence,SourceId=second.EvidenceItemId,Relationship=HypothesisRelationship.Contradicts,Strength=HypothesisStrength.Weak}]);

        var reopened=await repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId);
        var carried=reopened.Single(a=>a.SourceId==first.EvidenceItemId);
        var fresh=reopened.Single(a=>a.SourceId==second.EvidenceItemId);

        Assert.Equal(DateTime.Parse("2026-01-05T09:00:00Z").ToUniversalTime(),carried.CreatedUtc.ToUniversalTime());
        Assert.Equal(HypothesisStrength.Strong,carried.Strength);   // the judgment itself still updates
        Assert.True(fresh.CreatedUtc>carried.CreatedUtc);           // and a new one is dated now
    }

    [Fact]
    public async Task DeletingAnAssessedSourceTakesItsAssessmentWithIt()
    {
        using var db=await TempDatabase.CreateAsync();await db.SeedEditionAsync("rhesus");
        var research=new ResearchRepository();var project=new ResearchProject{WorkId=await db.WorkIdForAsync("rhesus"),Name="Rowid reuse"};await research.SaveProjectAsync(project);
        var first=new EvidenceItem{ResearchProjectId=project.ResearchProjectId,Title="The assessed passage"};await research.SaveEvidenceAsync(first);
        var repo=new ResearchHypothesisRepository();var hypothesis=new ResearchHypothesis{ResearchProjectId=project.ResearchProjectId,Title="Non-Euripidean author",Statement="A different tragedian composed it."};await repo.SaveHypothesisAsync(hypothesis);
        await repo.SaveAssessmentsAsync(hypothesis.ResearchHypothesisId,[new ResearchHypothesisAssessment{SourceKind=HypothesisSourceKind.Evidence,SourceId=first.EvidenceItemId,Relationship=HypothesisRelationship.Supports,Strength=HypothesisStrength.Strong,ResearcherNote="Decisive for the diction argument."}]);
        Assert.Single(await repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId));

        // Delete the highest-numbered evidence item and add another: SQLite hands the
        // new row the vacated rowid. While the link was a bare (SourceKind, SourceId)
        // pair the assessment stayed behind and silently became a strong judgment about
        // a passage the researcher had never assessed. The typed foreign key takes it.
        await research.DeleteEvidenceAsync(first.EvidenceItemId);
        var second=new EvidenceItem{ResearchProjectId=project.ResearchProjectId,Title="An unrelated passage"};await research.SaveEvidenceAsync(second);
        Assert.Equal(first.EvidenceItemId,second.EvidenceItemId);

        Assert.Empty(await repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId));
    }

    [Fact]
    public async Task AssessmentCannotLinkAResearchSourceFromAnotherProject()
    {
        using var db=await TempDatabase.CreateAsync();await db.SeedEditionAsync();var research=new ResearchRepository();var work=await db.WorkIdForAsync("test1");
        var first=new ResearchProject{WorkId=work,Name="First"};var second=new ResearchProject{WorkId=work,Name="Second"};await research.SaveProjectAsync(first);await research.SaveProjectAsync(second);
        var foreign=new EvidenceItem{ResearchProjectId=second.ResearchProjectId,Title="Foreign evidence"};await research.SaveEvidenceAsync(foreign);
        var repo=new ResearchHypothesisRepository();var hypothesis=new ResearchHypothesis{ResearchProjectId=first.ResearchProjectId,Title="Local theory",Statement="A local proposition"};await repo.SaveHypothesisAsync(hypothesis);
        await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveAssessmentsAsync(hypothesis.ResearchHypothesisId,[new ResearchHypothesisAssessment{SourceKind=HypothesisSourceKind.Evidence,SourceId=foreign.EvidenceItemId}]));
    }

    [Fact]
    public async Task MigrationFromTwentyEightAddsHypothesisLabWithoutChangingProjects()
    {
        using var db=await TempDatabase.CreateAsync();await db.SeedEditionAsync();var research=new ResearchRepository();var project=new ResearchProject{WorkId=await db.WorkIdForAsync("test1"),Name="Existing project"};await research.SaveProjectAsync(project);
        await db.ExecuteAsync("DROP TABLE ResearchHypothesisAssessments; DROP TABLE ResearchExperiments; DROP TABLE ResearchHypotheses; PRAGMA user_version=28;");

        // Rewinding by hand leaves behind every column a later ALTER migration
        // added, because a fresh database is built from the current schema. This
        // drops them - see TempDatabase.RewindSchemaAsync.
        await db.RewindSchemaAsync(28);
        await SchemaInitializer.EnsureSchemaAsync();
        Assert.Equal("Existing project",Assert.Single(await research.GetProjectsForWorkAsync(project.WorkId!.Value)).Name);Assert.True(await db.TableExistsAsync("ResearchHypotheses"));Assert.True(await db.TableExistsAsync("ResearchExperiments"));Assert.Equal(SchemaInitializer.TargetSchemaVersion,await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public void ChallengeParserKeepsOnlyWellFormedReviewableProposals()
    {
        var parsed=GeminiTranslationService.ParseHypothesisChallengeProposals("""
            ```json
            [{"kind":"rivalHypothesis","title":"Shared tradition","statement":"Both works inherit an older pattern.","rationale":"Explains non-directional similarity."},
             {"kind":"experiment","title":"Control-corpus test","statement":"Compare the motif across tragedy.","method":"CorpusInvestigator","predictedOutcome":"Broad distribution","falsificationCriterion":"The pattern remains unique to the pair."},
             {"kind":"claim","title":"Not a valid proposal","statement":"Ignored"},
             {"kind":"experiment","title":"Missing statement"}]
            ```
            """);
        Assert.Equal(2,parsed.Count);Assert.Equal("rivalHypothesis",parsed[0].Kind);Assert.Equal("CorpusInvestigator",parsed[1].Method);Assert.Equal("The pattern remains unique to the pair.",parsed[1].FalsificationCriterion);
    }

    [Fact]
    public void DossierExportsCompetingHypothesesAndFalsificationCriteria()
    {
        var hypothesis=new ResearchHypothesis{ResearchHypothesisId=7,Title="Shared convention",Statement="The resemblance is generic.",Status=ResearchHypothesisStatus.Active};
        var experiment=new ResearchExperiment{Title="Tragic control corpus",Method=ResearchExperimentMethod.CorpusInvestigator,FalsificationCriterion="The pattern is unique to the reviewed pair."};
        var markdown=ResearchDossierExport.ToMarkdown(new ResearchDossierData(new ResearchProject{Name="Theory"},"Rhesus","Euripides",[],[],[],[],new Dictionary<long,IReadOnlyList<ResearchFindingEvidenceLink>>(),[],[],[hypothesis],new Dictionary<long,IReadOnlyList<ResearchHypothesisAssessment>>(),[],[experiment]));
        Assert.Contains("## Competing hypotheses",markdown);Assert.Contains("Shared convention",markdown);Assert.Contains("Would count against it: The pattern is unique",markdown);
    }
}
