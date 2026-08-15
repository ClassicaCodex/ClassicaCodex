using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

/// <summary>One fully traceable edge projected into the intertextual atlas.</summary>
public sealed record IntertextualAtlasConnection(
    ResearchProject Project,
    string SourceAuthorName,
    string SourceWorkTitle,
    ResearchEchoInvestigation Investigation,
    ResearchEchoResult Result)
{
    public string SourceLabel => $"{SourceAuthorName} — {SourceWorkTitle}";
    public string TargetLabel => $"{Result.TargetAuthorName} — {Result.TargetWorkTitle}";
    public IReadOnlyList<string> Motifs => (Result.MotifTags ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
