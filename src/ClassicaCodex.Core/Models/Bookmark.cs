namespace ClassicaCodex.Core.Models;

/// <summary>
/// A note you've pinned to a specific line - e.g. "check this against Ovid's
/// version" or "cf. Norseverse thesis".
/// </summary>
public class Bookmark
{
    public int BookmarkId { get; set; }

    public long TextNodeId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
