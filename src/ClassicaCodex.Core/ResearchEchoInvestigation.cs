using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

public enum ResearchEchoMethod { RareWordOverlap, ReceptionChronology, AiCrossLanguage, AiCorpusInvestigation }
public enum ResearchEchoDisposition { Pending, Accepted, Rejected }
public enum ResearchEchoConnectionType { Unclassified, Verbal, Thematic, Imagistic, Narrative, Structural, GenericConvention, Reception, Coincidental }
public enum ResearchEchoDirectionality { Unknown, SourceToTarget, TargetToSource, CommonSource, SharedTradition, ChronologicallyImpossible }

public sealed record PassageResearchIdentity(
    int WorkId, long TextNodeId, int EditionId, string AuthorName, string WorkTitle,
    string WorkCtsUrn, string EditionCtsUrn, string CitationRef, string Text, string? Language, string? Milestone = null);

public sealed class ResearchEchoInvestigation
{
    public long ResearchEchoInvestigationId { get; set; }
    public long ResearchProjectId { get; set; }
    public long? ResearchQuestionId { get; set; }
    public long? ResearchFindingId { get; set; }
    public ResearchEchoMethod Method { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SourceWorkId { get; set; }
    public long SourceTextNodeId { get; set; }
    public string SourceWorkCtsUrn { get; set; } = string.Empty;
    public string SourceEditionCtsUrn { get; set; } = string.Empty;
    public string SourceCitationRef { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;

    /// <summary>
    /// How the source passage is cited - see <see cref="Models.TextNode.Milestone"/>.
    /// Resolved from the line when the record is read, not stored with it, so
    /// an echo captured before this existed shows it too, and a re-ingest that
    /// renumbers nodes cannot leave it stale.
    /// </summary>
    public string? SourceMilestone { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetScope { get; set; }
    public string? Settings { get; set; }
    public string? AiModel { get; set; }
    public string? AiPrompt { get; set; }
    public DateTime? AiGeneratedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public override string ToString() => $"{Title} — {Method}";
}

public sealed class ResearchEchoResult
{
    public long ResearchEchoResultId { get; set; }
    public long ResearchEchoInvestigationId { get; set; }
    public int TargetWorkId { get; set; }
    public long TargetTextNodeId { get; set; }
    public string TargetAuthorName { get; set; } = string.Empty;
    public string TargetWorkTitle { get; set; } = string.Empty;
    public string TargetWorkCtsUrn { get; set; } = string.Empty;
    public string TargetEditionCtsUrn { get; set; } = string.Empty;
    public string TargetCitationRef { get; set; } = string.Empty;
    public string TargetText { get; set; } = string.Empty;

    /// <summary>How the target passage is cited. Resolved on read, like SourceMilestone.</summary>
    public string? TargetMilestone { get; set; }
    public string? TargetLanguage { get; set; }
    public double? Score { get; set; }
    public string? ScoreLabel { get; set; }
    public string? Rationale { get; set; }
    public ResearchEchoDisposition Disposition { get; set; } = ResearchEchoDisposition.Pending;
    public string? ResearcherNote { get; set; }
    public ResearchEchoConnectionType ConnectionType { get; set; }
    public ResearchEchoDirectionality Directionality { get; set; }
    public string? MotifTags { get; set; }
    public string? ParallelNote { get; set; }
    public long? EvidenceItemId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>An immutable AI reading of one pair, retained separately from human classification.</summary>
public sealed class ResearchEchoParallelAnalysis
{
    public long ResearchEchoParallelAnalysisId { get; set; }
    public long ResearchEchoResultId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? SharedFeatures { get; set; }
    public string? ImportantDifferences { get; set; }
    public string? LexicalObservations { get; set; }
    public string? AlternativeExplanations { get; set; }
    public string? VerificationTasks { get; set; }
    public string? SuggestedMotifs { get; set; }
    public ResearchEchoConnectionType SuggestedConnectionType { get; set; }
    public ResearchEchoDirectionality SuggestedDirectionality { get; set; }
    public DateTime CreatedUtc { get; set; }
    public override string ToString() => $"{CreatedUtc.ToLocalTime():g} — {Model}";
}

public sealed record GeminiParallelAnalysisResult(
    string Model, string PromptProvenance, string Summary, string SharedFeatures,
    string ImportantDifferences, string LexicalObservations, string AlternativeExplanations,
    string VerificationTasks, string SuggestedMotifs, string SuggestedConnectionType,
    string SuggestedDirectionality);

/// <summary>A model-proposed corpus hit whose opaque key must resolve against the exact local prompt corpus.</summary>
public sealed record CorpusInvestigationCandidate(
    string CandidateKey, string Role, string Confidence, string Rationale, string SuggestedMotifs);

public sealed record GeminiCorpusInvestigationResult(
    string Model, string PromptProvenance, IReadOnlyList<CorpusInvestigationCandidate> Candidates);

/// <summary>One verified candidate exported by an existing echo result window.</summary>
public sealed record EchoCaptureCandidate(
    int WorkId, long TextNodeId, string AuthorName, string WorkTitle,
    string CitationRef, string Text, double? Score, string? ScoreLabel, string? Rationale);

public sealed record EchoCaptureRequest(
    ResearchEchoMethod Method,
    PassageResearchIdentity Source,
    string Title,
    string? TargetScope,
    string? Settings,
    string? AiModel,
    string? AiPrompt,
    DateTime? AiGeneratedUtc,
    IReadOnlyList<EchoCaptureCandidate> Candidates);
