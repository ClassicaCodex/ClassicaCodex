using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.Core.Tests;

public class ResearchProjectAuditTests
{
    [Fact]
    public void EmptyProjectExplainsScopeAndCoverageGaps()
    {
        var report = ResearchProjectAudit.Evaluate([], []);

        Assert.Equal(0, report.QuestionCount);
        Assert.Equal(0, report.EvidenceCount);
        Assert.Contains(report.Findings, f => f.Category == "Scope");
        Assert.Contains(report.Findings, f => f.Category == "Coverage");
    }

    [Fact]
    public void AuditFlagsUnlinkedQuestionAndTraceabilityGaps()
    {
        var question = new ResearchQuestion { ResearchQuestionId = 11, Text = "Does the meter differ?" };
        var evidence = new EvidenceItem
        {
            EvidenceItemId = 22,
            Title = "A general impression",
            Judgment = EvidenceJudgment.Uncertain
        };

        var report = ResearchProjectAudit.Evaluate([question], [evidence]);

        Assert.Contains(report.Findings, f => f.ResearchQuestionId == 11 && f.Category == "Coverage");
        Assert.Contains(report.Findings, f => f.EvidenceItemId == 22 && f.Category == "Review");
        Assert.Contains(report.Findings, f => f.EvidenceItemId == 22 && f.Category == "Citation");
        Assert.Contains(report.Findings, f => f.EvidenceItemId == 22 && f.Category == "Provenance");
        Assert.Contains(report.Findings, f => f.EvidenceItemId == 22 && f.Category == "Evidence");
    }

    [Fact]
    public void CompleteOpposingEvidenceProducesNoFindings()
    {
        var question = new ResearchQuestion { ResearchQuestionId = 11, Text = "Does the meter differ?" };
        EvidenceItem Item(long id, EvidenceRelationship relationship) => new()
        {
            EvidenceItemId = id,
            ResearchQuestionId = 11,
            Title = $"Evidence {id}",
            Judgment = EvidenceJudgment.Accepted,
            Relationship = relationship,
            StableIdentifier = $"urn:test:{id}",
            Provenance = "Verified local edition",
            Excerpt = "Observed text"
        };

        var report = ResearchProjectAudit.Evaluate(
            [question], [Item(1, EvidenceRelationship.Supports), Item(2, EvidenceRelationship.Contradicts)]);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void AcceptedOneSidedEvidenceSuggestsFalsification()
    {
        var question = new ResearchQuestion { ResearchQuestionId = 11, Text = "Is the attribution secure?" };
        var evidence = new EvidenceItem
        {
            EvidenceItemId = 22,
            ResearchQuestionId = 11,
            Title = "Supporting result",
            Judgment = EvidenceJudgment.Accepted,
            Relationship = EvidenceRelationship.Supports,
            CanonicalReference = "1.1",
            Provenance = "Edition A",
            Excerpt = "Text"
        };

        var report = ResearchProjectAudit.Evaluate([question], [evidence]);

        var finding = Assert.Single(report.Findings);
        Assert.Equal(ResearchAuditSeverity.Suggestion, finding.Severity);
        Assert.Equal("Falsification", finding.Category);
        Assert.Equal(11, finding.ResearchQuestionId);
    }

    [Fact]
    public void ContextualEvidenceIsNotMisrepresentedAsOneSided()
    {
        var question = new ResearchQuestion { ResearchQuestionId = 11, Text = "What is the dramatic context?" };
        var evidence = new EvidenceItem
        {
            EvidenceItemId = 22,
            ResearchQuestionId = 11,
            Title = "Context",
            Judgment = EvidenceJudgment.Accepted,
            Relationship = EvidenceRelationship.Contextualizes,
            StableIdentifier = "urn:test:22",
            Provenance = "Edition A",
            Excerpt = "Text"
        };

        var report = ResearchProjectAudit.Evaluate([question], [evidence]);

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void AiCandidateRequiresHumanAndGeneratorProvenance()
    {
        var evidence = new EvidenceItem
        {
            EvidenceItemId = 5,
            Title = "AI candidate",
            Origin = EvidenceOrigin.AiCandidate,
            Judgment = EvidenceJudgment.Uncertain,
            StableIdentifier = "urn:test:5",
            Provenance = "Local corpus",
            Excerpt = "Text"
        };

        var report = ResearchProjectAudit.Evaluate([], [evidence]);

        Assert.Contains(report.Findings, f => f.EvidenceItemId == 5 &&
            f.Message.Contains("human verification", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Findings, f => f.EvidenceItemId == 5 && f.Category == "AI provenance");
    }

    [Fact]
    public void UnverifiedUnlinkedClaimIsActionable()
    {
        var claim = new ScholarlyClaim
        {
            ScholarlyClaimId = 17,
            Claimant = "Scholar",
            ClaimText = "A disputed proposition",
            Judgment = EvidenceJudgment.Uncertain
        };

        var report = ResearchProjectAudit.Evaluate([], [], [claim]);

        Assert.Equal(1, report.ClaimCount);
        Assert.Equal(1, report.UncertainClaimCount);
        Assert.Contains(report.Findings, f => f.ScholarlyClaimId == 17 && f.Category == "Claim review");
        Assert.Contains(report.Findings, f => f.ScholarlyClaimId == 17 && f.Category == "Claim source");
        Assert.Contains(report.Findings, f => f.ScholarlyClaimId == 17 && f.Category == "Claim locator");
    }
}
