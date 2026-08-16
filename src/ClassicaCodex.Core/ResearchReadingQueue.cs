namespace ClassicaCodex.Core;

public enum ResearchReadingKind { CorpusPassage, EvidenceSource, ExternalSource }
public enum ResearchReadingStatus { Queued, Reading, Reviewed, FollowUp }
public enum ResearchReadingPriority { Low, Normal, High }

/// <summary>A candidate for examination, kept distinct from accepted research evidence.</summary>
public sealed class ResearchReadingItem
{
    public long ResearchReadingItemId { get; set; }
    public long ResearchProjectId { get; set; }
    public long? ResearchQuestionId { get; set; }
    public ResearchReadingKind Kind { get; set; }
    public ResearchReadingStatus Status { get; set; } = ResearchReadingStatus.Queued;
    public ResearchReadingPriority Priority { get; set; } = ResearchReadingPriority.Normal;
    public string Title { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? WorkCtsUrn { get; set; }
    public string? EditionCtsUrn { get; set; }
    public string? CitationRef { get; set; }
    public long? LinkedEvidenceItemId { get; set; }
    public string? StableIdentifier { get; set; }
    public string? Locator { get; set; }
    public string? Quotation { get; set; }
    public string? Notes { get; set; }
    public long? PromotedEvidenceItemId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
