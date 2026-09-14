using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Where a long passage is cut so it can be read across several rows.
///
/// A reader row cannot exceed 255px - see <see cref="ReaderRowHeight"/> - and
/// about 7% of the passages in a full library want more than that. Until now
/// the surplus was not shown at all. These tests are about the rules for
/// cutting: that nothing is lost, that no word is broken, and that the thing
/// terminates even on text it cannot satisfy.
///
/// The measurement is a stand-in throughout - a fixed number of characters per
/// line - so that what is being tested is the cutting rule and not the font. The
/// real measurer is GDI text layout, which the reader injects.
/// </summary>
public class ReaderRowSplitterTests
{
    /// <summary>
    /// Height of text wrapped at <paramref name="charsPerLine"/> characters,
    /// one unit per line. Wrapping at whole words, as the real one does.
    /// </summary>
    private static ReaderRowSplitter.MeasureHeight Wrapping(int charsPerLine) => text =>
    {
        var lines = 1;
        var column = 0;

        foreach (var word in Words(text))
        {
            if (word == "\n") { lines++; column = 0; continue; }

            if (column > 0 && column + 1 + word.Length > charsPerLine) { lines++; column = word.Length; }
            else column += (column > 0 ? 1 : 0) + word.Length;
        }

        return lines;
    };

    private static IEnumerable<string> Words(string text)
    {
        var current = "";
        foreach (var c in text)
        {
            if (c == '\n') { if (current.Length > 0) { yield return current; current = ""; } yield return "\n"; }
            else if (char.IsWhiteSpace(c)) { if (current.Length > 0) { yield return current; current = ""; } }
            else current += c;
        }

        if (current.Length > 0) yield return current;
    }

    private static string Sentence(int words) =>
        string.Join(" ", Enumerable.Range(0, words).Select(i => $"word{i:D3}"));

    /// <summary>
    /// The invariant everything else rests on. A reader is looking at an
    /// edition; if splitting could drop or add a character, the rows would no
    /// longer be what the editor printed.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(500)]
    public void TheSegmentsPutBackTogetherAreExactlyTheOriginal(int words)
    {
        var text = Sentence(words);

        var segments = ReaderRowSplitter.Split(text, Wrapping(40), maxHeight: 4);

        Assert.Equal(text, string.Concat(segments));
    }

    [Fact]
    public void TextThatAlreadyFitsIsNotSplit()
    {
        var text = Sentence(3);

        var segments = ReaderRowSplitter.Split(text, Wrapping(40), maxHeight: 4);

        Assert.Single(segments);
        Assert.Equal(text, segments[0]);
    }

    [Fact]
    public void EverySegmentFitsWithinTheLimit()
    {
        var measure = Wrapping(40);

        var segments = ReaderRowSplitter.Split(Sentence(400), measure, maxHeight: 4);

        Assert.True(segments.Count > 1);
        foreach (var segment in segments) Assert.True(measure(segment) <= 4, $"segment too tall: {measure(segment)}");
    }

    /// <summary>
    /// No cut ever falls inside a word. A passage broken mid-word would read as
    /// a hyphenation the edition never had.
    /// </summary>
    [Fact]
    public void NoWordIsEverBrokenAcrossTwoSegments()
    {
        var segments = ReaderRowSplitter.Split(Sentence(300), Wrapping(40), maxHeight: 4);

        foreach (var segment in segments)
        {
            foreach (var word in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.Matches(@"^word\d{3}$", word);
            }
        }
    }

    /// <summary>
    /// The Optatianus case, reduced: a single run longer than a row can hold.
    /// The control will clip it either way; what must not happen is a hang, an
    /// empty segment, or a word cut in half to make the arithmetic work.
    /// </summary>
    [Fact]
    public void AWordLongerThanARowIsEmittedWholeRatherThanCutOrLoopedOn()
    {
        var monster = new string('x', 4000);
        var text = $"before {monster} after";

        var segments = ReaderRowSplitter.Split(text, Wrapping(40), maxHeight: 4);

        Assert.Equal(text, string.Concat(segments));
        Assert.Contains(segments, s => s.Contains(monster, StringComparison.Ordinal));
        Assert.DoesNotContain(segments, string.IsNullOrEmpty);
    }

    /// <summary>
    /// Text with no break opportunity at all - the grid poem's shape. One
    /// segment, returned rather than refused.
    /// </summary>
    [Fact]
    public void TextWithNoSpacesComesBackAsOneSegment()
    {
        var text = new string('y', 9000);

        var segments = ReaderRowSplitter.Split(text, Wrapping(40), maxHeight: 4);

        Assert.Single(segments);
        Assert.Equal(text, segments[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \r\n  ")]
    public void EmptyOrBlankTextIsLeftAlone(string blank)
    {
        var segments = ReaderRowSplitter.Split(blank, Wrapping(40), maxHeight: 1);

        Assert.Single(segments);
        Assert.Equal(blank, segments[0]);
    }

    /// <summary>
    /// A passage carrying its own line breaks - many do - still reassembles
    /// exactly, and the breaks are not moved.
    /// </summary>
    [Fact]
    public void LineBreaksInsideAPassageSurviveTheSplit()
    {
        var text = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"line{i:D2} of the passage"));

        var segments = ReaderRowSplitter.Split(text, Wrapping(40), maxHeight: 3);

        Assert.Equal(text, string.Concat(segments));
        Assert.True(segments.Count > 1);
    }

    /// <summary>
    /// Greek with combining diacritics is the corpus's normal case, and the cut
    /// rule must not fall between a letter and the marks that belong to it -
    /// which it cannot, since it only ever cuts at whitespace.
    /// </summary>
    [Fact]
    public void GreekWithCombiningMarksIsOnlyEverCutAtSpaces()
    {
        var text = string.Join(" ", Enumerable.Repeat("μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος", 40));

        var segments = ReaderRowSplitter.Split(text, Wrapping(30), maxHeight: 3);

        Assert.Equal(text, string.Concat(segments));
        foreach (var segment in segments)
        {
            Assert.False(char.IsWhiteSpace(segment[0]), "a segment began with whitespace");
        }
    }

    /// <summary>
    /// Height by character count rather than by word, so that a single
    /// unbroken run really is too tall.
    ///
    /// <see cref="Wrapping"/> cannot express that - it never breaks a word, so
    /// it calls a run of any length one line - and the real measurer does:
    /// ReaderRowHeight.CannotFit answers "taller than a row" from the narrowest
    /// glyph in the font, without laying anything out. That answer is what
    /// reaches the case below, so a test that cannot produce it cannot reach
    /// the case either.
    /// </summary>
    private static ReaderRowSplitter.MeasureHeight ByLength(int charsPerLine) => text =>
        Math.Max(1, (text.Length + charsPerLine - 1) / charsPerLine);

    /// <summary>
    /// Nothing fits, and the answer is the run rather than an exception.
    ///
    /// The bisection searches over cut positions, and the caller has already
    /// advanced past every cut at or before the piece being measured. When the
    /// very first candidate overflows, the floor of that search used to fall
    /// back to zero - behind the piece - and the length went negative, which
    /// threw out of Substring.
    ///
    /// Which did not surface as a crash. SetPassagesAsync catches everything
    /// and falls back to one row per passage for the WHOLE WORK, so a single
    /// passage of this shape silently turned splitting off for the edition
    /// containing it and put the 255px clipping back, with nothing said. One
    /// edition in the real library reaches it: Optatianus Porfyrius, whose
    /// grid poems are an unbroken run of 1,170 characters.
    /// </summary>
    [Fact]
    public void AnUnbreakableRunTooTallForARowIsReturnedRatherThanThrown()
    {
        var text = "AB CD " + new string('Q', 1170) + " EF";

        var segments = ReaderRowSplitter.Split(text, ByLength(40), maxHeight: 4);

        Assert.Equal(text, string.Concat(segments));
        Assert.Contains(segments, s => s.Contains(new string('Q', 1170), StringComparison.Ordinal));
    }

    /// <summary>
    /// The same fault reached the other way, which the first report missed: an
    /// unbreakable run at the END of the passage leaves no cut after the piece
    /// at all, so the forward search never runs and the bisection walked the
    /// whole list backwards instead.
    /// </summary>
    [Fact]
    public void AnUnbreakableRunAtTheEndOfAPassageIsAlsoReturnedRatherThanThrown()
    {
        var text = "aa bb " + new string('Q', 2000);

        var segments = ReaderRowSplitter.Split(text, ByLength(40), maxHeight: 4);

        Assert.Equal(text, string.Concat(segments));
        Assert.DoesNotContain(segments, string.IsNullOrEmpty);
    }

    /// <summary>
    /// And a passage carrying its own hard breaks, which is the shape that
    /// first showed the fault: the breaks make a piece too tall without any
    /// single word being long.
    /// </summary>
    [Fact]
    public void APassageWhoseOwnLineBreaksOverflowARowStillReassembles()
    {
        var text = "alpha beta" + new string('\n', 14) + "gamma" + new string('\n', 14) + "delta epsilon zeta";

        var segments = ReaderRowSplitter.Split(text, Wrapping(40), maxHeight: 10);

        Assert.Equal(text, string.Concat(segments));
        Assert.DoesNotContain(segments, string.IsNullOrEmpty);
    }

    /// <summary>
    /// The common case must stay cheap: most passages fit, and asking about one
    /// that fits should cost exactly one measurement, since the reader does this
    /// for every row of a work that can run to thirty thousand of them.
    /// </summary>
    [Fact]
    public void APassageThatFitsCostsASingleMeasurement()
    {
        var calls = 0;
        var measure = Wrapping(40);

        ReaderRowSplitter.Split(Sentence(5), text => { calls++; return measure(text); }, maxHeight: 4);

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// And a passage that does not fit must not cost a measurement per
    /// character. The search is over cut points, so the count grows with the
    /// logarithm of them, not with the length of the text.
    /// </summary>
    [Fact]
    public void ALongPassageIsMeasuredFarFewerTimesThanItHasWords()
    {
        var calls = 0;
        var measure = Wrapping(40);

        var segments = ReaderRowSplitter.Split(Sentence(2000), text => { calls++; return measure(text); }, maxHeight: 4);

        Assert.True(segments.Count > 20);
        Assert.True(calls < 2000, $"measured {calls} times for 2,000 words");
    }
}
