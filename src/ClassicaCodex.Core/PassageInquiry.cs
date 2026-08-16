namespace ClassicaCodex.Core;

/// <summary>The deliberately small first direction chosen for a passage inquiry.</summary>
public enum PassageInquiryDirection
{
    None,
    ReadClosely,
    Compare,
    Research
}

/// <summary>
/// A human note begun from one citable passage, before it becomes a formal
/// Research Bench project. CTS identities keep it attached across re-ingest.
/// </summary>
public sealed class PassageInquiry
{
    public long PassageInquiryId { get; set; }
    public string WorkCtsUrn { get; set; } = string.Empty;
    public string EditionCtsUrn { get; set; } = string.Empty;
    public string CitationRef { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string WorkTitle { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string AttentionNote { get; set; } = string.Empty;
    public string DraftQuestion { get; set; } = string.Empty;
    public PassageInquiryDirection Direction { get; set; }
    public long? ResearchProjectId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>An optional AI prompt offered only after the reader chooses Research.</summary>
public sealed record PassageInquirySuggestion(
    string Angle, string Question, string Rationale, string NextStep);

public sealed record GeminiPassageInquiryResult(
    string Model, string PromptProvenance, IReadOnlyList<PassageInquirySuggestion> Suggestions);
