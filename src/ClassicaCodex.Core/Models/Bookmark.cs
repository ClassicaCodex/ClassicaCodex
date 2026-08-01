namespace ClassicaCodex.Core.Models;

/// <summary>
/// A note you've pinned to a specific line - e.g. "check this against Ovid's
/// version" or "cf. Norseverse thesis".
/// </summary>
public class Bookmark
{
    public int BookmarkId { get; set; }

    /// <summary>
    /// Bookmarks are pinned to a passage - (EditionId, CitationRef) - rather
    /// than to a TextNodeId, so they survive a re-ingest that renumbers every
    /// node. See SchemaInitializer's PassageTags comment.
    /// </summary>
    public int EditionId { get; set; }

    public string CitationRef { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
