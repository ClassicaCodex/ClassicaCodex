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

    /// <summary>
    /// What sort of thing this node is: a line of the text, a speech
    /// attribution, a stage direction, a heading, and so on. See
    /// <see cref="TextNodeKinds"/>.
    ///
    /// Everything here is content a reader expects to see on the page, which
    /// is why it lives in TextNodes rather than in the apparatus. But not all
    /// of it is language the author wrote, and anything in Text gets
    /// tokenised, counted and fed to Burrows's Delta. A play read without its
    /// speakers is unreadable; a word-frequency table in which "ΣΩ." is a top
    /// token is wrong. The kind is what lets both be true at once - the
    /// reading view shows every kind, and the frequency-based features filter
    /// to <see cref="TextNodeKinds.Line"/>.
    ///
    /// Measured before this existed: Plato's Gorgias was 4.1% speaker
    /// abbreviations by word count and the Laws 1.9%, because Perseus puts
    /// the attribution in a &lt;label&gt; inside the speech and it was being
    /// flattened into the line. Holinshed's first history is 6.8% headings.
    ///
    /// A string rather than an enum because the value is written by the
    /// parsers from TEI element names and read back by SQL; an unrecognised
    /// kind should degrade to "shown but not counted", not throw.
    /// </summary>
    public string NodeKind { get; set; } = TextNodeKinds.Line;
}

/// <summary>
/// The recognised values of <see cref="TextNode.NodeKind"/>.
///
/// Kept as consts rather than an enum so a parser can pass through a TEI
/// element name this list hasn't anticipated without anything failing. Only
/// <see cref="Line"/> is load-bearing: it is what the frequency-based
/// features count, and anything else is shown and not counted.
/// </summary>
public static class TextNodeKinds
{
    /// <summary>Text the author wrote. The only kind fed to word counts.</summary>
    public const string Line = "line";

    /// <summary>A speech attribution - TEI &lt;speaker&gt;, or a &lt;label&gt; inside a &lt;said&gt;.</summary>
    public const string Speaker = "speaker";

    /// <summary>A stage direction.</summary>
    public const string Stage = "stage";

    /// <summary>A division heading.</summary>
    public const string Head = "head";

    /// <summary>One entry in a dramatis personae.</summary>
    public const string Cast = "cast";

    /// <summary>
    /// Front and back matter around the text proper: dedications, colophons,
    /// signatures, "FINIS". Printed in the book, not part of the work.
    /// </summary>
    public const string Paratext = "paratext";

    /// <summary>
    /// An attribution attached to a text rather than spoken in it - the poet
    /// named at the head of an epigram in the Greek Anthology.
    /// </summary>
    public const string Attribution = "attribution";
}
