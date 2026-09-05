using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What an editor would have printed in the margin.
///
/// The question is never "what is this line's reference" - the passage already
/// knows - but "would anything have been printed here". Almost every test
/// below is therefore about restraint: a mark that repeats on every line is a
/// column of noise a reader stops seeing, and it takes the width it occupies
/// from the text.
/// </summary>
public class CitationMarginTests
{
    private static TextNode Line(string citation, string? milestone = null) =>
        new() { CitationRef = citation, Milestone = milestone, NodeKind = TextNodeKinds.Line };

    private static TextNode Of(string kind, string citation) =>
        new() { CitationRef = citation, NodeKind = kind };

    // ---- structural references: Homer, and most of the corpus -------------

    /// <summary>
    /// Every fifth line, as Oxford prints it. The four between are blank,
    /// which is the whole point.
    /// </summary>
    [Theory]
    [InlineData("1.4", null)]
    [InlineData("1.5", "5")]
    [InlineData("1.6", null)]
    [InlineData("1.9", null)]
    [InlineData("1.10", "10")]
    [InlineData("1.104", null)]
    [InlineData("1.105", "105")]
    public void AStructuralReferenceIsMarkedEveryFifthLine(string citation, string? expected) =>
        Assert.Equal(expected, CitationMargin.MarkFor(Line(citation), Line("1.3")));

    /// <summary>
    /// The top of a work says where the reader is rather than making them
    /// read four lines to find out.
    /// </summary>
    [Fact]
    public void TheFirstLineIsMarked() =>
        Assert.Equal("1.1", CitationMargin.MarkFor(Line("1.1"), null));

    /// <summary>
    /// A new book restarts the count, so the reader is told the book rather
    /// than seeing a bare "5" that means something different from the "5"
    /// above it.
    /// </summary>
    [Fact]
    public void ANewBookIsMarkedInFull() =>
        Assert.Equal("2.1", CitationMargin.MarkFor(Line("2.1"), Line("1.611")));

    /// <summary>
    /// Menota cites a manuscript as "text=F:book=1:letter=9.1". Printing that
    /// in a margin would be a second column; the line number alone still says
    /// where the reader is.
    /// </summary>
    [Fact]
    public void AReferenceTooLongForAMarginFallsBackToTheLineNumber() =>
        Assert.Equal("1", CitationMargin.MarkFor(
            Line("text=F:book=2:letter=9.1"), Line("text=F:book=1:letter=9.40")));

    // ---- what carries no number -------------------------------------------

    /// <summary>An editor numbers lines, not the attributions between them.</summary>
    [Theory]
    [InlineData(TextNodeKinds.Speaker, "2.speaker1")]
    [InlineData(TextNodeKinds.Stage, "2.stage1")]
    [InlineData(TextNodeKinds.Head, "head")]
    public void NothingIsMarkedBesideWhatIsNotALine(string kind, string citation) =>
        Assert.Null(CitationMargin.MarkFor(Of(kind, citation), Line("1.5")));

    /// <summary>
    /// A reference whose last part is not a number cannot be counted to five,
    /// so nothing is marked rather than something arbitrary.
    /// </summary>
    [Fact]
    public void AReferenceThatCannotBeCountedIsNotMarked() =>
        Assert.Null(CitationMargin.MarkFor(Line("1.prologue"), Line("1.prologue")));

    // ---- canonical pagination: Plato and Aristotle ------------------------

    /// <summary>
    /// The mark goes where the section starts and nowhere else - which down
    /// the edge of the Republic gives 327a, 328a, 329a, exactly the sequence
    /// on a printed page.
    /// </summary>
    [Fact]
    public void APaginatedTextIsMarkedWhereTheSectionChanges()
    {
        Assert.Equal("2a", CitationMargin.MarkFor(Line("2.1", "2a"), null));
        Assert.Null(CitationMargin.MarkFor(Line("2.2", "2a"), Line("2.1", "2a")));
        Assert.Equal("2b", CitationMargin.MarkFor(Line("2.3", "2b"), Line("2.2", "2a")));
    }

    /// <summary>
    /// A range is marked where it begins. Perseus divides the Republic a whole
    /// Stephanus page to a paragraph, so the passage is 327a-c; the margin is
    /// saying where the reader is, not how far the passage runs.
    /// </summary>
    [Fact]
    public void ARangeIsMarkedByWhereItStarts() =>
        Assert.Equal("327a", CitationMargin.MarkFor(Line("1.327.1", "327a–c"), null));

    /// <summary>
    /// Two passages whose ranges start in the same section are one mark. The
    /// second is already inside 329e and an editor would not say so twice.
    /// </summary>
    [Fact]
    public void TwoRangesStartingInTheSameSectionAreMarkedOnce() =>
        Assert.Null(CitationMargin.MarkFor(
            Line("1.331.1", "329e–331a"), Line("1.330.1", "329e–330e")));

    [Fact]
    public void ABekkerNumberIsMarkedTheSameWay() =>
        Assert.Equal("1094a1", CitationMargin.MarkFor(
            Line("1.1.1", "1094a1–15"), Line("1.0.1", "1093b20")));

    // ---- the one that makes it usable -------------------------------------

    /// <summary>
    /// A Platonic dialogue puts a speaker between every pair of lines, and a
    /// play alternates speech and attribution throughout. The caller passes
    /// the nearest LINE above rather than the item above, because comparing
    /// against a speaker would find no reference, conclude the section had
    /// changed, and print 2a beside every line of the Euthyphro.
    ///
    /// This test is the contract for that: given the previous line, a second
    /// speech inside 2a is not marked, however many attributions sit between.
    /// </summary>
    [Fact]
    public void AnAttributionBetweenTwoLinesDoesNotReprintTheSection()
    {
        var first = Line("2.1", "2a");
        var speaker = Of(TextNodeKinds.Speaker, "2.speaker2");
        var second = Line("2.2", "2a");

        Assert.Null(CitationMargin.MarkFor(speaker, first));
        Assert.Null(CitationMargin.MarkFor(second, first));
    }

    /// <summary>
    /// Reading down a whole dialogue, the margin is sparse: one mark per
    /// section, not one per line. Sixteen nodes of the Euthyphro's opening
    /// produce three marks.
    /// </summary>
    [Fact]
    public void AWholeOpeningProducesOneMarkPerSection()
    {
        var nodes = new List<TextNode>();
        foreach (var (citation, milestone) in new[]
                 {
                     ("2.1", "2a"), ("2.2", "2a"), ("2.3", "2b"), ("2.4", "2b"),
                     ("2.5", "2b"), ("2.6", "2b"), ("2.7", "2c")
                 })
        {
            nodes.Add(Of(TextNodeKinds.Speaker, $"2.speaker{nodes.Count}"));
            nodes.Add(Line(citation, milestone));
        }

        var marks = new List<string>();
        TextNode? previousLine = null;
        foreach (var node in nodes)
        {
            if (CitationMargin.MarkFor(node, previousLine) is { } mark) marks.Add(mark);
            if (node.NodeKind == TextNodeKinds.Line) previousLine = node;
        }

        Assert.Equal(new[] { "2a", "2b", "2c" }, marks);
    }
}
