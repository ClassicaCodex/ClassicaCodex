namespace ClassicaCodex.Core.Models;

/// <summary>
/// One entry from an edition's critical apparatus, attached to the line it
/// discusses.
///
/// The apparatus is what a printed edition sets in small type at the foot of
/// the page: which manuscripts read what, who conjectured what, which lines
/// are doubted and by whom. It is excluded from the reading text on purpose -
/// anything in TextNode.Text is tokenised, searched, exported and counted, and
/// an editor's surname is not a word of Greek - but it is the scholarship, and
/// throwing it away leaves a reader able to see that a line is bracketed
/// without any way to find out who bracketed it.
/// </summary>
public class ApparatusEntry
{
    public long ApparatusId { get; set; }

    public int EditionId { get; set; }

    /// <summary>
    /// The line this entry discusses. Keyed by citation reference rather than
    /// TextNodeId because apparatus is collected during parsing, before text
    /// nodes have ids, and because a re-ingest renumbers ids while citation
    /// references stay put.
    /// </summary>
    public string CitationRef { get; set; } = string.Empty;

    /// <summary>Order within the line, so several entries on one line keep the source's sequence.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// "variant" - a manuscript reading the editor rejected, from &lt;rdg&gt;.
    /// "note"    - editorial comment, from &lt;note&gt;.
    ///
    /// Perseus mostly uses the second: the whole apparatus entry arrives as
    /// prose ("εἶτʼ οὐ R: εἶτα") rather than as structured lemma-and-variant.
    /// No attempt is made to parse that into fields, because doing so means
    /// guessing at one editor's punctuation conventions and guessing wrong
    /// silently.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The adopted reading this entry concerns, where the source names one in
    /// &lt;lem&gt;.
    ///
    /// Almost always null in practice. A census of the ingested corpus found
    /// 371,601 entries of Kind "note" against 5 of Kind "variant" - and of
    /// those five, four were single characters from a fragmentary text and
    /// only one (Cicero, In Catilinam: turpis / gravis) was a genuine variant.
    /// Structured &lt;app&gt;/&lt;rdg&gt; apparatus is effectively absent from
    /// Perseus and First1KGreek; what they carry is commentary.
    ///
    /// Kept because the schema is right and the code that reads it is already
    /// written - a corpus that does encode an apparatus properly would work
    /// with no changes. But anything relying on these being populated will be
    /// dealing with nulls.
    /// </summary>
    public string? Lemma { get; set; }

    /// <summary>
    /// Manuscript siglum or responsible editor, from @wit or @resp. See Lemma
    /// for why this is usually null; @resp is more often present, and usually
    /// says nothing more specific than "editor".
    /// </summary>
    public string? Witness { get; set; }

    public string Content { get; set; } = string.Empty;
}
