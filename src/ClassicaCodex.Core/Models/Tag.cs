namespace ClassicaCodex.Core.Models;

/// <summary>
/// A cross-reference tag you attach to text nodes yourself - e.g. "Prometheus",
/// "Flood myth", "Underworld journey" - so you can pull every mention of a
/// figure or theme across every author at once.
/// </summary>
public class Tag
{
    public int TagId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }

    public override string ToString() => Category != null ? $"{Name} ({Category})" : Name;
}

/// <summary>
/// Join between a TextNode and a Tag. Many-to-many.
/// </summary>
public class TextNodeTag
{
    public long TextNodeId { get; set; }

    public int TagId { get; set; }
}
