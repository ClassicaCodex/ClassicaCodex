namespace ClassicaCodex.Core;

public enum CorpusSnapshotScope { ProjectWork, SameAuthor, EntireCorpus }

public sealed class ResearchCorpusSnapshot
{
    public long ResearchCorpusSnapshotId { get; set; }
    public long ResearchProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CorpusSnapshotScope Scope { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int WorkCount { get; set; }
    public int EditionCount { get; set; }
    public long TextNodeCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public override string ToString() => $"{Name} — {Scope} ({EditionCount} editions)";
}

public sealed class ResearchCorpusSnapshotEntry
{
    public long ResearchCorpusSnapshotEntryId { get; set; }
    public long ResearchCorpusSnapshotId { get; set; }
    public string AuthorCtsUrn { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string WorkCtsUrn { get; set; } = string.Empty;
    public string WorkTitle { get; set; } = string.Empty;
    public string? CitationScheme { get; set; }
    public string AttributionStatus { get; set; } = "accepted";
    public string? AttributionNote { get; set; }
    public bool AttributionSetByUser { get; set; }
    public string? EditionCtsUrn { get; set; }
    public string? EditionKind { get; set; }
    public string? Language { get; set; }
    public string? Translator { get; set; }
    public string? SourcePath { get; set; }
    public string? Orthography { get; set; }
    public long TextNodeCount { get; set; }
    public string? ContentSha256 { get; set; }
}

public sealed record CorpusSnapshotProgress(int Completed, int Total, string CurrentWork);
public sealed record CorpusSnapshotDifference(string Status, string Work, string Edition, string Details);
public sealed record CorpusSnapshotComparison(int Unchanged, int Changed, int Added, int Missing,
    IReadOnlyList<CorpusSnapshotDifference> Differences);
