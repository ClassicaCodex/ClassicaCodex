namespace ClassicaCodex.Core;

public enum ResearchHypothesisStatus { Active, Supported, Disfavored, Retired }
public enum HypothesisSourceKind { Evidence, Finding, ScholarlyClaim, EchoResult }
public enum HypothesisRelationship { Supports, Contradicts, Contextualizes, DoesNotDiscriminate }
public enum HypothesisStrength { Weak, Moderate, Strong }
public enum ResearchExperimentMethod { Stylometry, CorpusInvestigator, ParallelStudio, Bibliography, ReadingQueue, Manual }
public enum ResearchExperimentStatus { Planned, InProgress, Completed, Abandoned }

public sealed class ResearchHypothesis
{
    public long ResearchHypothesisId { get; set; }
    public long ResearchProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
    public ResearchHypothesisStatus Status { get; set; } = ResearchHypothesisStatus.Active;
    public Models.EvidenceOrigin Origin { get; set; } = Models.EvidenceOrigin.Manual;
    public string? ResearcherNote { get; set; }
    public string? AiModel { get; set; }
    public string? AiPrompt { get; set; }
    public DateTime? AiGeneratedUtc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public override string ToString() => $"{Title} — {Status}";
}

public sealed class ResearchHypothesisAssessment
{
    public long ResearchHypothesisAssessmentId { get; set; }
    public long ResearchHypothesisId { get; set; }
    public HypothesisSourceKind SourceKind { get; set; }
    public long SourceId { get; set; }
    public HypothesisRelationship Relationship { get; set; } = HypothesisRelationship.Contextualizes;
    public HypothesisStrength Strength { get; set; } = HypothesisStrength.Moderate;
    public string? ResearcherNote { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed record HypothesisSource(
    HypothesisSourceKind Kind, long Id, string Title, string Detail, string ReviewState);

public sealed class ResearchExperiment
{
    public long ResearchExperimentId { get; set; }
    public long ResearchProjectId { get; set; }
    public long? ResearchHypothesisId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ResearchExperimentMethod Method { get; set; } = ResearchExperimentMethod.Manual;
    public ResearchExperimentStatus Status { get; set; } = ResearchExperimentStatus.Planned;
    public string? PredictedOutcome { get; set; }
    public string? FalsificationCriterion { get; set; }
    public string? ResearcherNote { get; set; }
    public Models.EvidenceOrigin Origin { get; set; } = Models.EvidenceOrigin.Manual;
    public string? AiModel { get; set; }
    public string? AiPrompt { get; set; }
    public DateTime? AiGeneratedUtc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed record HypothesisChallengeProposal(
    string Kind, string Title, string Statement, string Rationale, string Method,
    string PredictedOutcome, string FalsificationCriterion);

public sealed record GeminiHypothesisChallengeResult(
    string Model, string PromptProvenance, IReadOnlyList<HypothesisChallengeProposal> Proposals);
