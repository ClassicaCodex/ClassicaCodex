using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

/// <summary>
/// What to print in the margin beside a line, the way a printed edition does.
///
/// The reference a passage carries has until now been visible only by hovering
/// over it, which is the wrong place for the one piece of information a reader
/// needs in order to cite what they are reading, or to find their way back to
/// it. An edition solves this by putting the reference in the margin - and
/// crucially, by not putting it beside every line. Oxford's Homer marks every
/// fifth line; a Stephanus letter appears once, where the section starts, and
/// not again until the next one.
///
/// So the question this answers is not "what is this line's reference" - the
/// passage already knows that - but "would an editor have printed anything
/// here". Both rules below come from what the text itself carries:
///
/// <b>Where a text has canonical pagination</b> - Plato, Aristotle - the mark
/// goes wherever that changes, and nowhere else. Reading the Republic gives a
/// margin of 327a, 328a, 329a, which is exactly the sequence down the edge of
/// a printed page.
///
/// <b>Everywhere else</b> the reference is structural - Homer's book and line -
/// and the mark goes on every fifth line, plus wherever the part above the
/// line number changes, because that is a new book and the count restarts.
///
/// Nothing is marked beside a speaker attribution or a stage direction. Those
/// are not lines an editor numbers.
/// </summary>
public static class CitationMargin
{
    /// <summary>How often a structural reference is marked, as in print.</summary>
    private const int Every = 5;

    /// <summary>
    /// Wide enough for anything an edition prints in a margin: the longest
    /// Bekker mark is "1094a15" at seven, and a book and line - "5.211" -
    /// is shorter still. Past this the margin would be taking width from the
    /// text to print something no edition prints. Menota cites a manuscript as
    /// "text=F:book=1:letter=9.1", and a column of those is not a margin.
    /// </summary>
    public const int MaxLength = 8;

    /// <summary>
    /// The mark for this line given the one above it, or null where an editor
    /// would have left the margin blank.
    /// </summary>
    /// <param name="previousLine">
    /// The nearest line ABOVE this one, skipping anything that is not a line,
    /// or null at the top of a work.
    ///
    /// Skipping matters, and the caller does it because only the caller can:
    /// a play alternates speech and attribution, and a Platonic dialogue puts
    /// a speaker between every pair of lines. Comparing against the item
    /// immediately above would find a speaker every time, conclude the
    /// reference had changed, and print 2a beside every line of the Euthyphro -
    /// which is the noise this exists to avoid.
    /// </param>
    public static string? MarkFor(TextNode? node, TextNode? previousLine)
    {
        if (node == null) return null;
        if (!string.Equals(node.NodeKind, TextNodeKinds.Line, StringComparison.Ordinal)) return null;

        if (!string.IsNullOrWhiteSpace(node.Milestone))
        {
            var start = StartOf(node.Milestone);
            return start.Length == 0 || start == StartOf(previousLine?.Milestone) ? null : start;
        }

        var reference = PassageCitation.Display(node.CitationRef);
        if (reference.Length == 0) return null;

        var (prefix, number) = Split(reference);
        if (number == null) return null;

        // A line that follows a paginated one - the end of a Stephanus text
        // running into something else - starts its own count from here.
        var previousReference = string.IsNullOrWhiteSpace(previousLine?.Milestone)
            ? PassageCitation.Display(previousLine?.CitationRef)
            : string.Empty;
        var (previousPrefix, previousNumber) = Split(previousReference);

        // A new book, section or letter: the count below it starts again, so
        // the reader is told where they now are rather than waiting up to five
        // lines to find out.
        if (!string.Equals(prefix, previousPrefix, StringComparison.Ordinal) || previousNumber == null)
            return reference.Length <= MaxLength ? reference : number.Value.ToString();

        return number.Value % Every == 0 ? number.Value.ToString() : null;
    }

    /// <summary>
    /// A range is marked where it begins. "327a-c" spans three sections but
    /// starts in one, and the margin is saying where the reader is, not how far
    /// the passage runs.
    /// </summary>
    private static string StartOf(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return string.Empty;

        var value = milestone.Trim();
        var dash = value.IndexOf('–');
        if (dash > 0) value = value[..dash];
        return value.Length <= MaxLength ? value : string.Empty;
    }

    /// <summary>
    /// Splits a reference into the part that identifies the division and the
    /// line number within it: "1.104" is book 1, line 104.
    ///
    /// A last segment that is not a number means this is not something a
    /// margin can count - "3.speaker2", "1.head" - and the caller marks
    /// nothing.
    /// </summary>
    private static (string Prefix, int? Number) Split(string reference)
    {
        if (reference.Length == 0) return (string.Empty, null);

        var lastDot = reference.LastIndexOf('.');
        var tail = lastDot >= 0 ? reference[(lastDot + 1)..] : reference;

        return int.TryParse(tail, out var number) && number >= 0
            ? (lastDot >= 0 ? reference[..lastDot] : string.Empty, number)
            : (reference, null);
    }
}
