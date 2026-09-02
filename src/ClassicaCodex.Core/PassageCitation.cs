namespace ClassicaCodex.Core;

/// <summary>
/// How a passage's reference is written where a person will read it.
///
/// Perseus puts the full CTS URN in a div's @n attribute for most of the
/// corpus, so the reference this application stores is
/// "urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.1.1" rather than "1.1".
/// Measured against a full library, 1,060,214 of 1,085,843 passages - 97.6% -
/// carry that prefix.
///
/// Storing it is right: the stored form is the durable key that tags,
/// bookmarks and inquiries hang on, and it is what survives a re-ingest.
/// SHOWING it is not. The prefix is the edition's identity, which the reader
/// already knows because they chose the edition, repeated on every line of it;
/// what a classicist wants at the end of a quotation is Od. 1.1.
///
/// It had leaked into the tooltip on every line, search results, the bookmark
/// list, the concordance, tag and echo browsers, export headers, the suggested
/// filename an export opens with, and the citation sent to Claude and Gemini
/// in a translation prompt - so a model was being told the passage sat at
/// "urn:cts:latinLit:phi0474.phi053.perseus-lat1.1.1.1", and an exported PDF
/// arrived called
/// "Homer - Odyssey urn_cts_greekLit_tlg0012.tlg002.perseus-grc2.1.1.pdf".
/// That file is the one that ends up in somebody's essay.
///
/// One shared method rather than a call to ExtractPassageRef at each of the
/// seventy sites, for the reason EditionLabels gives about itself: the sites
/// that forget are the ones nobody looks at, and export was already the place
/// where a label stopped being something you read and became something you
/// pasted.
/// </summary>
public static class PassageCitation
{
    /// <summary>
    /// The reference as a reader should see it - "1.1", "3.stage2",
    /// "text=F:book=1:letter=9.1".
    ///
    /// The stripping itself is PassageAligner's, which is where it has always
    /// lived and which the collation and the linked reading panes already
    /// depend on. A reference that carries no URN, and one this cannot parse,
    /// come back untouched: a reference shown as stored is worse to read and
    /// still true, which is the right way round for this to fail.
    /// </summary>
    public static string Display(string? citationRef) =>
        string.IsNullOrWhiteSpace(citationRef)
            ? string.Empty
            : PassageAligner.ExtractPassageRef(citationRef);

    /// <summary>
    /// The same, bracketed, for the many places that write "[1.1]" beside a
    /// line. Empty for a passage with no reference, rather than an empty pair
    /// of brackets.
    /// </summary>
    public static string Bracketed(string? citationRef)
    {
        var display = Display(citationRef);
        return display.Length == 0 ? string.Empty : $"[{display}]";
    }
}
