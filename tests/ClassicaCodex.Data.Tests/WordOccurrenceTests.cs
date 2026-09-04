using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Pointing at the word in the line that was found.
///
/// Finding a line and pointing at the word in it had drifted apart. The
/// search matches through the word index, which folds accents, breathings,
/// both sigmas and both halves of u/v and i/j; everything that then had to
/// point at the word was doing a literal IndexOf of what was typed. So the
/// search returned rows the highlighter could not mark, and the concordance -
/// whose entire output is the word framed by its context - printed the line
/// with a placeholder where the keyword column should be.
///
/// From the reader's side that looks like the application returning lines
/// that do not contain the word, which is the most reportable-looking thing
/// a search can do.
/// </summary>
public class WordOccurrenceTests
{
    private static List<string> Found(string text, string query) =>
        WordOccurrences.Find(text, WordOccurrences.TargetsFor(query))
            .Select(s => text.Substring(s.Start, s.Length))
            .ToList();

    // ---- the cases the literal matcher could not see ---------------------

    /// <summary>
    /// Nobody types diacritics into a search box, and the search stopped
    /// requiring it. The highlighter has to agree.
    /// </summary>
    [Fact]
    public void AnUnaccentedQueryFindsTheAccentedWord() =>
        Assert.Equal(new[] { "μῆνιν" }, Found("μῆνιν ἄειδε θεά", "μηνιν"));

    [Fact]
    public void TheClassicalSpellingFindsTheEditionPrintingTheOther() =>
        Assert.Equal(new[] { "justitia" }, Found("de justitia et misericordia", "iustitia"));

    [Fact]
    public void AnOrdinarySigmaFindsALunateOne() =>
        Assert.Equal(new[] { "λόγοϲ" }, Found("ὁ λόγοϲ οὗτοϲ", "λόγος"));

    [Fact]
    public void FinalSigmaAndMedialSigmaAreTheSameWord() =>
        Assert.Equal(new[] { "λόγος" }, Found("ὁ λόγος οὗτος", "λογοσ"));

    // ---- and what it must still not do -----------------------------------

    /// <summary>
    /// Whole words. The old literal pass marked "vir" inside "virtutem",
    /// which is right for the substring mode and wrong for this one - the
    /// search that produced the row matched whole words.
    /// </summary>
    [Fact]
    public void AStemInsideALongerWordIsNotAnOccurrence() =>
        Assert.Empty(Found("magna virtutem habet", "vir"));

    [Fact]
    public void AWordThatIsNotThereIsNotFound() =>
        Assert.Empty(Found("arma virumque cano", "pietas"));

    // ---- the span itself -------------------------------------------------

    /// <summary>
    /// The word, not the punctuation stuck to it - a keyword column reading
    /// "λόγος," or "(virtus)" is not the word.
    /// </summary>
    [Theory]
    [InlineData("ὁ λόγος, οὗτος", "λογος", "λόγος")]
    [InlineData("magna (virtus) est", "virtus", "virtus")]
    [InlineData("virtus.", "virtus", "virtus")]
    [InlineData("—virtus—", "virtus", "virtus")]
    public void TheSpanCoversTheWordAndNotWhatIsStuckToIt(string text, string query, string expected) =>
        Assert.Equal(new[] { expected }, Found(text, query));

    /// <summary>
    /// The keyword column shows the word as that edition prints it, which is
    /// the point of a concordance drawn from editions that disagree.
    /// </summary>
    [Fact]
    public void EachOccurrenceIsReportedAsThatLinePrintsIt()
    {
        const string text = "iustitia et justitia";

        Assert.Equal(new[] { "iustitia", "justitia" }, Found(text, "iustitia"));
    }

    [Fact]
    public void EveryOccurrenceInALineIsFoundInOrder()
    {
        var spans = WordOccurrences.Find("virtus et virtus et virtus", WordOccurrences.TargetsFor("virtus"));

        Assert.Equal(3, spans.Count);
        Assert.Equal(spans.Select(s => s.Start).OrderBy(s => s), spans.Select(s => s.Start));
    }

    [Fact]
    public void EveryWordOfAPhraseIsMarked()
    {
        var found = Found("Gallia est omnis divisa in partes", "gallia divisa");

        Assert.Equal(new[] { "Gallia", "divisa" }, found);
    }

    // ---- nothing in, nothing out -----------------------------------------

    [Theory]
    [InlineData("", "virtus")]
    [InlineData("some text", "")]
    [InlineData("some text", "   ")]
    [InlineData("some text", ",,,")]
    public void NothingUsableFindsNothing(string text, string query) => Assert.Empty(Found(text, query));

    [Fact]
    public void NullsAreSafe()
    {
        Assert.Empty(WordOccurrences.Find(null, WordOccurrences.TargetsFor("virtus")));
        Assert.Empty(WordOccurrences.TargetsFor(null));
    }

    /// <summary>
    /// A span has to be usable as a substring of the text it came from -
    /// every caller slices the line with it.
    /// </summary>
    [Fact]
    public void EverySpanLiesInsideTheText()
    {
        const string text = "  ⟨iustitia⟩, et justitia; μῆνιν  ";
        var targets = WordOccurrences.TargetsFor("iustitia μηνιν");

        foreach (var (start, length) in WordOccurrences.Find(text, targets))
        {
            Assert.InRange(start, 0, text.Length - 1);
            Assert.InRange(start + length, start + 1, text.Length);
        }
    }
}
