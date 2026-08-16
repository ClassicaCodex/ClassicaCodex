using System.Text;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

public enum ResearchFindingStatus { Hypothesis, Provisional, Supported, Contested, Rejected }

/// <summary>A researcher-owned proposition synthesized from explicitly linked evidence.</summary>
public sealed class ResearchFinding
{
    public long ResearchFindingId { get; set; }
    public long ResearchProjectId { get; set; }
    public long? ResearchQuestionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
    public ResearchFindingStatus Status { get; set; } = ResearchFindingStatus.Hypothesis;
    public string? ResearcherConclusion { get; set; }
    public string? AiCandidateSynthesis { get; set; }
    public string? AiModel { get; set; }
    public string? AiPrompt { get; set; }
    public DateTime? AiGeneratedUtc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public override string ToString() => Title;
}

public sealed class ResearchFindingEvidenceLink
{
    public long ResearchFindingId { get; set; }
    public long EvidenceItemId { get; set; }
    public EvidenceRelationship Relationship { get; set; } = EvidenceRelationship.Contextualizes;
    public string? Note { get; set; }
}

public sealed record ResearchDossierData(
    ResearchProject Project,
    string WorkTitle,
    string AuthorName,
    IReadOnlyList<ResearchQuestion> Questions,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<ScholarlyClaim> Claims,
    IReadOnlyList<ResearchFinding> Findings,
    IReadOnlyDictionary<long, IReadOnlyList<ResearchFindingEvidenceLink>> FindingLinks,
    IReadOnlyList<ResearchCorpusSnapshot> CorpusSnapshots,
    IReadOnlyList<ResearchLogEntry> Log,
    IReadOnlyList<ResearchHypothesis>? Hypotheses = null,
    IReadOnlyDictionary<long, IReadOnlyList<ResearchHypothesisAssessment>>? HypothesisAssessments = null,
    IReadOnlyList<HypothesisSource>? HypothesisSources = null,
    IReadOnlyList<ResearchExperiment>? Experiments = null);

public static class ResearchDossierExport
{
    public static string ToMarkdown(ResearchDossierData data)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {data.Project.Name}").AppendLine();
        text.AppendLine($"**Work:** {data.AuthorName}, *{data.WorkTitle}*  ");
        text.AppendLine($"**Project status:** {data.Project.Status}  ");
        text.AppendLine($"**Exported:** {DateTime.UtcNow:O}").AppendLine();
        if (!string.IsNullOrWhiteSpace(data.Project.Notes))
            text.AppendLine(data.Project.Notes).AppendLine();

        text.AppendLine("## Research questions").AppendLine();
        foreach (var question in data.Questions.OrderBy(q => q.SortOrder))
            text.AppendLine($"- {question.Text}");

        text.AppendLine().AppendLine("## Findings").AppendLine();
        foreach (var finding in data.Findings.OrderBy(f => f.SortOrder))
        {
            text.AppendLine($"### {finding.Title}").AppendLine();
            text.AppendLine($"**Status:** {finding.Status}").AppendLine();
            text.AppendLine(finding.Statement).AppendLine();
            if (!string.IsNullOrWhiteSpace(finding.ResearcherConclusion))
                text.AppendLine("**Researcher conclusion**").AppendLine().AppendLine(finding.ResearcherConclusion).AppendLine();
            if (!string.IsNullOrWhiteSpace(finding.AiCandidateSynthesis))
            {
                text.AppendLine($"**AI candidate synthesis — {finding.AiModel ?? "model not recorded"}, " +
                                $"{finding.AiGeneratedUtc?.ToString("O") ?? "date not recorded"}**").AppendLine();
                text.AppendLine(finding.AiCandidateSynthesis).AppendLine();
                if (!string.IsNullOrWhiteSpace(finding.AiPrompt))
                    text.AppendLine("<details><summary>AI prompt provenance</summary>").AppendLine()
                        .AppendLine("```text").AppendLine(finding.AiPrompt).AppendLine("```")
                        .AppendLine("</details>").AppendLine();
            }
            if (data.FindingLinks.TryGetValue(finding.ResearchFindingId, out var links))
                foreach (var link in links)
                {
                    var evidence = data.Evidence.FirstOrDefault(e => e.EvidenceItemId == link.EvidenceItemId);
                    if (evidence != null)
                        text.AppendLine($"- **{link.Relationship}:** {evidence.Title} " +
                                        $"[{evidence.CanonicalReference ?? evidence.StableIdentifier ?? "no stable reference"}]");
                }
            text.AppendLine();
        }

        var hypotheses = data.Hypotheses ?? [];
        var assessments = data.HypothesisAssessments ?? new Dictionary<long, IReadOnlyList<ResearchHypothesisAssessment>>();
        var sources = data.HypothesisSources ?? [];
        text.AppendLine("## Competing hypotheses").AppendLine();
        if (hypotheses.Count == 0) text.AppendLine("No competing hypotheses have been recorded.").AppendLine();
        foreach (var hypothesis in hypotheses.OrderBy(h => h.SortOrder))
        {
            text.AppendLine($"### {hypothesis.Title}").AppendLine();
            text.AppendLine($"**{hypothesis.Status} · origin: {hypothesis.Origin}**").AppendLine();
            text.AppendLine(hypothesis.Statement).AppendLine();
            if (!string.IsNullOrWhiteSpace(hypothesis.ResearcherNote)) text.AppendLine($"Researcher note: {hypothesis.ResearcherNote}").AppendLine();
            if (assessments.TryGetValue(hypothesis.ResearchHypothesisId, out var hypothesisLinks))
                foreach (var link in hypothesisLinks)
                {
                    var source = sources.FirstOrDefault(s => s.Kind == link.SourceKind && s.Id == link.SourceId);
                    text.AppendLine($"- **{link.Relationship} / {link.Strength}:** {source?.Title ?? $"{link.SourceKind} {link.SourceId}"}" +
                                    (string.IsNullOrWhiteSpace(link.ResearcherNote) ? "" : $" — {link.ResearcherNote}"));
                }
            text.AppendLine();
        }

        text.AppendLine("## Falsification experiments").AppendLine();
        foreach (var experiment in (data.Experiments ?? []).OrderBy(e => e.SortOrder))
        {
            text.AppendLine($"### {experiment.Title}").AppendLine();
            text.AppendLine($"**{experiment.Status} · {experiment.Method} · origin: {experiment.Origin}**").AppendLine();
            if (!string.IsNullOrWhiteSpace(experiment.PredictedOutcome)) text.AppendLine($"Predicted outcome: {experiment.PredictedOutcome}  ");
            if (!string.IsNullOrWhiteSpace(experiment.FalsificationCriterion)) text.AppendLine($"Would count against it: {experiment.FalsificationCriterion}  ");
            if (!string.IsNullOrWhiteSpace(experiment.ResearcherNote)) text.AppendLine().AppendLine(experiment.ResearcherNote).AppendLine();
        }

        text.AppendLine("## Corpus snapshots").AppendLine();
        if (data.CorpusSnapshots.Count == 0)
            text.AppendLine("No reproducibility snapshot has been captured for this project.").AppendLine();
        foreach (var snapshot in data.CorpusSnapshots.OrderByDescending(s => s.CreatedUtc))
            text.AppendLine($"- **{snapshot.Name}** — {snapshot.Scope}; {snapshot.WorkCount} work(s); " +
                            $"{snapshot.EditionCount} edition(s); {snapshot.TextNodeCount:N0} text nodes; " +
                            $"app {snapshot.AppVersion}; captured {snapshot.CreatedUtc:O}");

        text.AppendLine("## Evidence register").AppendLine();
        foreach (var item in data.Evidence.OrderBy(e => e.SortOrder))
        {
            text.AppendLine($"### {item.Title}").AppendLine();
            text.AppendLine($"**{item.Judgment} · {item.Relationship} · {item.Type} · origin: {item.Origin}**  ");
            text.AppendLine($"Reference: {item.CanonicalReference ?? item.StableIdentifier ?? "not recorded"}  ");
            if (!string.IsNullOrWhiteSpace(item.Excerpt)) text.AppendLine().AppendLine(item.Excerpt).AppendLine();
            if (!string.IsNullOrWhiteSpace(item.ResearcherNote)) text.AppendLine($"Researcher note: {item.ResearcherNote}").AppendLine();
        }

        text.AppendLine("## Scholarly claims").AppendLine();
        foreach (var claim in data.Claims.OrderBy(c => c.SortOrder))
            text.AppendLine($"- **{claim.Claimant}:** {claim.ClaimText} " +
                            $"({claim.Locator ?? "no locator"}; {claim.Judgment}; {claim.Relationship})");

        text.AppendLine().AppendLine("## Research log").AppendLine();
        foreach (var entry in data.Log.OrderBy(e => e.CreatedUtc))
            text.AppendLine($"- {entry.CreatedUtc:O} — **{entry.Kind}:** {entry.Summary}");
        return text.ToString();
    }
}
