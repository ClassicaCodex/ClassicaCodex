namespace ClassicaCodex.Core.Models;

public enum ResearchProjectStatus
{
    Active,
    OnHold,
    Concluded,
    Archived
}

public enum EvidenceType
{
    PrimaryText,
    Scholarship,
    Manuscript,
    Papyrology,
    Epigraphy,
    Numismatics,
    MaterialCulture,
    Archaeology,
    Place,
    Linguistic,
    Stylometric,
    Other
}

public enum EvidenceJudgment
{
    Uncertain,
    Accepted,
    Rejected
}

public enum EvidenceRelationship
{
    Contextualizes,
    Supports,
    Contradicts,
    Supersedes
}

public enum EvidenceOrigin
{
    Manual,
    ClassicaCodexAnalysis,
    AiCandidate
}

public enum ResearchLogEntryKind
{
    ManualNote,
    ProjectCreated,
    ProjectUpdated,
    StatusChanged,
    QuestionAdded,
    QuestionUpdated,
    QuestionRemoved,
    QuestionsReordered,
    EvidenceAdded,
    EvidenceUpdated,
    EvidenceRemoved,
    ClaimAdded,
    ClaimUpdated,
    ClaimRemoved
}

/// <summary>A persistent line of inquiry attached to one work.</summary>
public class ResearchProject
{
    public long ResearchProjectId { get; set; }
    public int WorkId { get; set; }
    public string Name { get; set; } = string.Empty;
    public ResearchProjectStatus Status { get; set; } = ResearchProjectStatus.Active;
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public override string ToString() => Name;
}

/// <summary>A question whose answer the project is trying to establish.</summary>
public class ResearchQuestion
{
    public long ResearchQuestionId { get; set; }
    public long ResearchProjectId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public override string ToString() => Text;
}

/// <summary>
/// A source or observation kept distinct from its human review and notes.
/// Structured identifiers remain useful even when display wording changes.
/// </summary>
public class EvidenceItem
{
    public long EvidenceItemId { get; set; }
    public long ResearchProjectId { get; set; }
    public long? ResearchQuestionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public EvidenceType Type { get; set; } = EvidenceType.PrimaryText;
    public string? SourceType { get; set; }
    public string? StableIdentifier { get; set; }
    public string? CanonicalReference { get; set; }
    public string? Provenance { get; set; }
    public string? Excerpt { get; set; }
    public EvidenceJudgment Judgment { get; set; } = EvidenceJudgment.Uncertain;
    public EvidenceRelationship Relationship { get; set; } = EvidenceRelationship.Contextualizes;
    public string? ResearcherNote { get; set; }
    public EvidenceOrigin Origin { get; set; } = EvidenceOrigin.Manual;
    public string? Interpretation { get; set; }
    public string? InterpretationAuthor { get; set; }
    public string? GeneratorPrompt { get; set; }
    public DateTime? GeneratedUtc { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>An append-only account of a project's research decisions and changes.</summary>
public class ResearchLogEntry
{
    public long ResearchLogEntryId { get; set; }
    public long ResearchProjectId { get; set; }
    public ResearchLogEntryKind Kind { get; set; } = ResearchLogEntryKind.ManualNote;
    public string Summary { get; set; } = string.Empty;
    public string? Details { get; set; }
    public long? ResearchQuestionId { get; set; }
    public long? EvidenceItemId { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// A proposition attributed to a scholar or scholarly source. The claim is
/// distinct from both the source evidence record and the researcher's review.
/// </summary>
public class ScholarlyClaim
{
    public long ScholarlyClaimId { get; set; }
    public long ResearchProjectId { get; set; }
    public long? ResearchQuestionId { get; set; }
    public long? SourceEvidenceItemId { get; set; }
    public string Claimant { get; set; } = string.Empty;
    public string ClaimText { get; set; } = string.Empty;
    public string? Locator { get; set; }
    public EvidenceRelationship Relationship { get; set; } = EvidenceRelationship.Contextualizes;
    public EvidenceJudgment Judgment { get; set; } = EvidenceJudgment.Uncertain;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
