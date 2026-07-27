namespace ClassicaCodex.Core.Models;

/// <summary>
/// The smallest citable unit of text within an edition - typically a line,
/// section, or "card", depending on the work's citation scheme.
/// </summary>
public class TextNode
{
    public long TextNodeId { get; set; }

    public int EditionId { get; set; }

    /// <summary>
    /// Citation path within the work, e.g. "1.1" for Book 1, Line 1.
    /// Reconstructed from the nested TEI &lt;div&gt; @n attributes.
    /// </summary>
    public string CitationRef { get; set; } = string.Empty;

    /// <summary>
    /// Sort key so nodes come back in document order even though CitationRef
    /// is a string (e.g. "1.9" would otherwise sort after "1.10").
    /// </summary>
    public int SortOrder { get; set; }

    public string Text { get; set; } = string.Empty;
}
