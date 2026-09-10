using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// How tall a row of the reader is allowed to be.
///
/// Found by reading the measurement code rather than reported: a Win32
/// owner-draw-variable list box keeps each item's height in one byte, and the
/// reader was handing it heights up to 75,636 for a 468,865-character passage.
/// Everything above 255 came back as height % 256, so a paragraph wanting
/// 300px was given 44 - two lines of a twelve-line paragraph - while the
/// longer one beneath it, wanting 511px, was given all 255 and looked fine.
/// Measured against a bare ListBox to be sure of the shape of it:
///
///     asked   255 -> stored 255      asked   400 -> stored 144
///     asked   256 -> stored   0      asked   511 -> stored 255
///     asked   257 -> stored   1      asked 21,861 -> stored 101
///     asked   300 -> stored  44      asked 75,636 -> stored 116
///
/// At a 737px pane that affected 7.2% of the source passages in this library
/// and 7.5% of the translated ones - about 169,000 passages, each showing an
/// arbitrary slice of itself with nothing to say so.
///
/// Capping cannot make a long passage readable in a control that holds 255px;
/// what it does is replace an arbitrary slice with the largest one available,
/// and let the row be marked so the reader knows to reach for Copy to
/// Clipboard. Reading such a passage whole needs the row split into several,
/// which the panes cannot do while they sync by row index.
/// </summary>
public class ReaderRowHeightTests
{
    /// <summary>The limit itself, as the control enforces it.</summary>
    [Theory]
    [InlineData(20, 20)]
    [InlineData(254, 254)]
    [InlineData(255, 255)]
    [InlineData(256, 255)]
    [InlineData(300, 255)]
    [InlineData(511, 255)]
    [InlineData(21861, 255)]
    [InlineData(75636, 255)]
    public void NoRowIsEverTallerThanTheControlCanStore(int uncapped, int expected)
    {
        Assert.Equal(expected, ReaderRowHeight.Cap(uncapped));
    }

    /// <summary>
    /// The heights that used to wrap round, and what they wrapped to. Not a
    /// test of our code - a record of why the cap exists, so nobody removes it
    /// believing 255 to be an arbitrary choice.
    ///
    /// 511 is in the table because it is the one height that came out right by
    /// accident: 511 % 256 is 255, so that paragraph looked correct while the
    /// shorter one above it did not. Coincidences like that are why the symptom
    /// took reading the measurement code to find rather than looking at a page.
    /// </summary>
    [Theory]
    [InlineData(256, 0)]
    [InlineData(257, 1)]
    [InlineData(300, 44)]
    [InlineData(400, 144)]
    [InlineData(511, 255)]
    [InlineData(21861, 101)]
    [InlineData(75636, 116)]
    public void AboveTheLimitTheControlUsedToStoreTheHeightModuloTwoFiftySix(int asked, int stored)
    {
        Assert.Equal(stored, asked % 256);
        Assert.Equal(255, ReaderRowHeight.Cap(asked));
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(255, false)]
    [InlineData(256, true)]
    [InlineData(75636, true)]
    public void ARowIsTruncatedExactlyWhenItWantedMoreThanTheLimit(int uncapped, bool truncated)
    {
        Assert.Equal(truncated, ReaderRowHeight.ExceedsMax(uncapped));
    }

    /// <summary>
    /// The bound has to be a floor. Claiming more lines than the text can
    /// occupy is the one way this arithmetic could lose text that would have
    /// fitted, so it is checked against layout rather than argued: 4 characters
    /// of a 5px-narrowest font across a 100px pane cannot need a second line.
    /// </summary>
    [Theory]
    [InlineData(0, 5, 100, 0)]
    [InlineData(4, 5, 100, 1)]
    [InlineData(20, 5, 100, 1)]
    [InlineData(21, 5, 100, 2)]
    [InlineData(40, 5, 100, 2)]
    [InlineData(41, 5, 100, 3)]
    public void TheLineCountIsTheFewestLinesTheTextCouldPossiblyOccupy(
        int length, int narrowestGlyph, int width, long expected)
    {
        Assert.Equal(expected, ReaderRowHeight.MinimumLines(length, narrowestGlyph, width));
    }

    /// <summary>
    /// A 468,865-character passage does not need measuring to know it will not
    /// fit; that is the point of the bound, since measuring it is what cost
    /// milliseconds a row.
    /// </summary>
    [Fact]
    public void TheLongestPassageInTheLibraryIsRuledOutWithoutMeasuringIt()
    {
        Assert.True(ReaderRowHeight.CannotFit(468865, narrowestGlyphWidth: 5, width: 737, fontHeight: 24));
    }

    /// <summary>
    /// And a line of verse is not, however narrow the pane - otherwise the
    /// early exit would be capping rows that fit perfectly well.
    /// </summary>
    [Theory]
    [InlineData(40, 380)]
    [InlineData(60, 500)]
    [InlineData(80, 737)]
    public void AnOrdinaryLineOfVerseIsNotRuledOut(int length, int width)
    {
        Assert.False(ReaderRowHeight.CannotFit(length, narrowestGlyphWidth: 5, width: width, fontHeight: 24));
    }

    /// <summary>
    /// Ten lines of a 24px font fit inside 255px and eleven do not, so the
    /// bound has to turn over between them rather than somewhere nearby.
    /// </summary>
    [Fact]
    public void TheThresholdSitsWhereTheRowActuallyRunsOut()
    {
        const int fontHeight = 24;
        const int width = 500;
        const int glyph = 5;

        var tenLines = 10 * width / glyph;
        var elevenLines = 11 * width / glyph;

        Assert.Equal(10, ReaderRowHeight.MinimumLines(tenLines, glyph, width));
        Assert.False(ReaderRowHeight.CannotFit(tenLines, glyph, width, fontHeight));

        Assert.Equal(11, ReaderRowHeight.MinimumLines(elevenLines, glyph, width));
        Assert.True(ReaderRowHeight.CannotFit(elevenLines, glyph, width, fontHeight));
    }

    /// <summary>
    /// A larger reader font fits fewer lines in the same row, so the same
    /// passage that fits at 13pt has to be ruled out at 28pt - the maximum the
    /// font-size dialog offers.
    /// </summary>
    [Fact]
    public void AReaderWhoEnlargesTheFontRunsOutOfRowSooner()
    {
        const int length = 900;

        Assert.False(ReaderRowHeight.CannotFit(length, 5, 737, fontHeight: 24));
        Assert.True(ReaderRowHeight.CannotFit(length, 5, 737, fontHeight: 48));
    }

    /// <summary>
    /// Degenerate inputs reach this from a pane mid-layout, where the width can
    /// be zero and the font height not yet known. Dividing by either would take
    /// the reader down.
    /// </summary>
    [Fact]
    public void APaneWithNoWidthYetDoesNotDivideByZero()
    {
        Assert.Equal(0, ReaderRowHeight.MinimumLines(0, 0, 0));
        Assert.Equal(100, ReaderRowHeight.MinimumLines(100, 0, 0));
        Assert.False(ReaderRowHeight.CannotFit(100, 0, 0, 0));
        Assert.True(ReaderRowHeight.CannotFit(100_000, 0, 0, 0));
    }
}
