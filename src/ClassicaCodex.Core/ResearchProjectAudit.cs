using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

public enum ResearchAuditSeverity
{
    Warning,
    Review,
    Suggestion
}

/// <summary>One actionable internal-completeness check for a research project.</summary>
public sealed record ResearchAuditFinding(
    ResearchAuditSeverity Severity,
    string Category,
    string Subject,
    string Message,
    long? ResearchQuestionId = null,
    long? EvidenceItemId = null,
    long? ScholarlyClaimId = null);

public sealed record ResearchAuditReport(
    int QuestionCount,
    int EvidenceCount,
    int UncertainEvidenceCount,
    int ClaimCount,
    int UncertainClaimCount,
    IReadOnlyList<ResearchAuditFinding> Findings);

/// <summary>
/// Audits what ClassicaCodex can establish locally about a project. This does
/// not claim that the bibliography or external evidence universe is complete;
/// it identifies gaps in the records the researcher has actually saved.
/// </summary>
public static class ResearchProjectAudit
{
    public static ResearchAuditReport Evaluate(
        IReadOnlyCollection<ResearchQuestion> questions,
        IReadOnlyCollection<EvidenceItem> evidence,
        IReadOnlyCollection<ScholarlyClaim>? claims = null)
    {
        claims ??= Array.Empty<ScholarlyClaim>();
        var findings = new List<ResearchAuditFinding>();

        if (questions.Count == 0)
        {
            findings.Add(new ResearchAuditFinding(
                ResearchAuditSeverity.Suggestion, "Scope", "Project",
                "Add a research question that states what evidence would change the working theory."));
        }

        if (evidence.Count == 0)
        {
            findings.Add(new ResearchAuditFinding(
                ResearchAuditSeverity.Warning, "Coverage", "Project",
                "No evidence has been saved for this project."));
        }

        foreach (var question in questions.OrderBy(q => q.SortOrder))
        {
            var linked = evidence.Where(e => e.ResearchQuestionId == question.ResearchQuestionId).ToList();
            if (linked.Count == 0)
            {
                findings.Add(new ResearchAuditFinding(
                    ResearchAuditSeverity.Warning, "Coverage", question.Text,
                    "This question has no directly linked evidence.", question.ResearchQuestionId));
                continue;
            }

            if (linked.All(e => e.Judgment == EvidenceJudgment.Uncertain))
            {
                findings.Add(new ResearchAuditFinding(
                    ResearchAuditSeverity.Review, "Review", question.Text,
                    "Every linked item is still uncertain.", question.ResearchQuestionId));
            }

            var accepted = linked.Where(e => e.Judgment == EvidenceJudgment.Accepted).ToList();
            var hasSupport = accepted.Any(e => e.Relationship == EvidenceRelationship.Supports);
            var hasCounterevidence = accepted.Any(e => e.Relationship == EvidenceRelationship.Contradicts);
            if (hasSupport ^ hasCounterevidence)
            {
                var missingSide = hasSupport ? "a counterexample or rival explanation" : "evidence supporting the theory";
                findings.Add(new ResearchAuditFinding(
                    ResearchAuditSeverity.Suggestion, "Falsification", question.Text,
                    $"Accepted evidence is one-sided; deliberately seek {missingSide}.",
                    question.ResearchQuestionId));
            }
        }

        foreach (var item in evidence)
        {
            if (item.Judgment == EvidenceJudgment.Uncertain)
            {
                var message = item.Origin == EvidenceOrigin.AiCandidate
                    ? "AI-proposed evidence still requires human verification."
                    : "This evidence has not received a human judgment.";
                findings.Add(ForEvidence(ResearchAuditSeverity.Review, "Review", item, message));
            }

            if (string.IsNullOrWhiteSpace(item.StableIdentifier) &&
                string.IsNullOrWhiteSpace(item.CanonicalReference))
            {
                findings.Add(ForEvidence(ResearchAuditSeverity.Warning, "Citation", item,
                    "Add a stable identifier or canonical reference so the source can be found again."));
            }

            if (string.IsNullOrWhiteSpace(item.Provenance))
            {
                findings.Add(ForEvidence(ResearchAuditSeverity.Warning, "Provenance", item,
                    "Source and provenance details are missing."));
            }

            if (string.IsNullOrWhiteSpace(item.Excerpt))
            {
                findings.Add(ForEvidence(ResearchAuditSeverity.Warning, "Evidence", item,
                    "Add the raw excerpt or factual summary on which the interpretation rests."));
            }

            if (item.Origin == EvidenceOrigin.AiCandidate &&
                (string.IsNullOrWhiteSpace(item.GeneratorPrompt) || item.GeneratedUtc == null))
            {
                findings.Add(ForEvidence(ResearchAuditSeverity.Warning, "AI provenance", item,
                    "The AI candidate is missing its prompt scope or generation timestamp."));
            }
        }

        foreach (var claim in claims)
        {
            if (claim.Judgment == EvidenceJudgment.Uncertain)
            {
                findings.Add(new ResearchAuditFinding(
                    ResearchAuditSeverity.Review, "Claim review", claim.Claimant,
                    "This scholarly claim has not been verified by the researcher.",
                    claim.ResearchQuestionId, ScholarlyClaimId: claim.ScholarlyClaimId));
            }
            if (claim.SourceEvidenceItemId == null)
            {
                findings.Add(new ResearchAuditFinding(
                    ResearchAuditSeverity.Warning, "Claim source", claim.Claimant,
                    "Link the claim to its scholarly source evidence record.",
                    claim.ResearchQuestionId, ScholarlyClaimId: claim.ScholarlyClaimId));
            }
            if (string.IsNullOrWhiteSpace(claim.Locator))
            {
                findings.Add(new ResearchAuditFinding(
                    ResearchAuditSeverity.Warning, "Claim locator", claim.Claimant,
                    "Add a page, section, or other exact locator for the claim.",
                    claim.ResearchQuestionId, ScholarlyClaimId: claim.ScholarlyClaimId));
            }
        }

        var ordered = findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => f.Category)
            .ThenBy(f => f.Subject)
            .ToList();
        return new ResearchAuditReport(
            questions.Count, evidence.Count,
            evidence.Count(e => e.Judgment == EvidenceJudgment.Uncertain),
            claims.Count, claims.Count(c => c.Judgment == EvidenceJudgment.Uncertain), ordered);
    }

    private static ResearchAuditFinding ForEvidence(
        ResearchAuditSeverity severity, string category, EvidenceItem item, string message) =>
        new(severity, category, item.Title, message, item.ResearchQuestionId, item.EvidenceItemId);
}
