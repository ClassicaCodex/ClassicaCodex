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
    ClaimRemoved,
    SourceAttached,
    SourceRemoved,
    PageAnnotationAdded,
    PageAnnotationUpdated,
    PageAnnotationRemoved,
    BibliographyExported,
    CorpusSnapshotCaptured,
    CorpusSnapshotRemoved,
    ReadingItemAdded,
    ReadingItemUpdated,
    ReadingItemRemoved,
    ReadingItemPromoted,
    FindingAdded,
    FindingUpdated,
    FindingRemoved,
    FindingEvidenceChanged,
    FindingAiCandidateGenerated,
    ResearchDossierExported,
    EchoInvestigationSaved,
    EchoResultReviewed,
    EchoResultPromoted,
    EchoParallelClassified,
    EchoParallelAiAnalyzed,
    HypothesisAdded,
    HypothesisUpdated,
    HypothesisRemoved,
    HypothesisAssessmentsChanged,
    ExperimentAdded,
    ExperimentUpdated,
    ExperimentRemoved,
    HypothesisChallengeGenerated,
    EchoInvestigationRemoved
}

/// <summary>Who wrote a research question.</summary>
public enum ResearchQuestionOrigin
{
    Researcher,
    AiProposed
}

/// <summary>A persistent line of inquiry attached to one work.</summary>
public class ResearchProject
{
    public long ResearchProjectId { get; set; }

    /// <summary>
    /// Null when the work has left the library. Research is never destroyed by an
    /// import; the project detaches and reattaches by <see cref="WorkCtsUrn"/> when a
    /// work with the same CTS identity is ingested again.
    /// </summary>
    public int? WorkId { get; set; }

    /// <summary>The work's durable CTS identity, which survives re-ingest as row ids do not.</summary>
    public string? WorkCtsUrn { get; set; }
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

    /// <summary>
    /// Who wrote this question. A model may propose one and a researcher may accept
    /// it, but the dossier has to be able to say which - the sibling entities an AI
    /// proposal creates all record this, and questions used to be the exception.
    /// </summary>
    public ResearchQuestionOrigin Origin { get; set; } = ResearchQuestionOrigin.Researcher;
    public string? AiModel { get; set; }
    public string? AiPrompt { get; set; }
    public DateTime? AiGeneratedUtc { get; set; }

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

/// <summary>A fingerprinted local file linked to an evidence source; file bytes remain outside SQLite.</summary>
public class EvidenceAttachment
{
    public long EvidenceAttachmentId { get; set; }
    public long EvidenceItemId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/pdf";
    public string Sha256 { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime FileModifiedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public override string ToString() => FileName;
}

/// <summary>A page-addressed quotation or note on a local source file.</summary>
public class EvidencePageAnnotation
{
    public long EvidencePageAnnotationId { get; set; }
    public long EvidenceAttachmentId { get; set; }
    public int PageNumber { get; set; } = 1;
    public string? QuotedText { get; set; }
    public string? Note { get; set; }
    public EvidenceJudgment Judgment { get; set; } = EvidenceJudgment.Uncertain;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
