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

    /// <summary>
    /// True when the editor bracketed some or all of this line as suspected
    /// interpolation - text the manuscripts transmit but which the editor
    /// doubts belongs to the author. TEI marks it with &lt;del&gt;.
    ///
    /// Stored as a flag rather than by inserting brackets into Text, because
    /// Text is what gets tokenised, searched, exported and counted. Brackets
    /// in the string would end up in word-frequency tables and search results.
    ///
    /// The flag is per line, not per word. An editor sometimes brackets a
    /// single word within an otherwise accepted line, and this cannot
    /// distinguish that from a wholly athetized line. Marking the line is
    /// honest about there being a doubt here; it does not claim to say
    /// exactly where.
    /// </summary>
    public bool IsAthetized { get; set; }
}
