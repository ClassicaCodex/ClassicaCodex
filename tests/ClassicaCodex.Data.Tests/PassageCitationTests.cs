using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What a reader is shown where a passage is cited.
///
/// Perseus puts the full CTS URN in a div's @n for most of the corpus, so the
/// stored reference is the URN followed by the passage. Measured against a
/// full library, 1,060,214 of 1,085,843 passages carry it. Storing it is
/// right - it is the key annotations hang on. Showing it was not: it reached
/// the tooltip on every line, search results, bookmarks, the concordance,
/// export headers and the filename an export opens with, so a quotation
/// arrived in someone's essay cited as
/// "urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.1.1" instead of 1.1.
/// </summary>
public class PassageCitationTests
{
    [Theory]
    [InlineData("urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.1.1", "1.1")]
    [InlineData("urn:cts:latinLit:phi0474.phi053.perseus-lat1.1.1.1", "1.1.1")]
    [InlineData("urn:cts:greekLit:tlg0003.tlg001.1st1K-fre1.5.18", "5.18")]
    public void TheUrnPrefixIsNotShown(string stored, string expected) =>
        Assert.Equal(expected, PassageCitation.Display(stored));

    /// <summary>
    /// A reference that is already a plain citation is shown as it is. Roughly
    /// one edition in thirty-four is like this, and the two forms sit side by
    /// side within a single work.
    /// </summary>
    [Theory]
    [InlineData("1.1")]
    [InlineData("18.1")]
    [InlineData("text=F:book=1:letter=9.1")]
    [InlineData("prologue.pr.1")]
    public void APlainReferenceIsLeftAlone(string stored) =>
        Assert.Equal(stored, PassageCitation.Display(stored));

    /// <summary>
    /// The named segments the parser mints for things that are not numbered
    /// lines survive, because they are what the citation points at.
    /// </summary>
    [Fact]
    public void NamedSegmentsSurvive() =>
        Assert.Equal("3.stage2",
            PassageCitation.Display("urn:cts:greekLit:tlg0006.tlg005.perseus-grc2.3.stage2"));

    /// <summary>
    /// Nothing to cite produces nothing, rather than an empty pair of brackets
    /// sitting after an author's name.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentReferenceShowsNothing(string? stored)
    {
        Assert.Equal(string.Empty, PassageCitation.Display(stored));
        Assert.Equal(string.Empty, PassageCitation.Bracketed(stored));
    }

    [Fact]
    public void BracketedWrapsWhatDisplayReturns() =>
        Assert.Equal("[1.1]",
            PassageCitation.Bracketed("urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.1.1"));

    /// <summary>
    /// Display never changes what is stored, and this is the boundary that
    /// matters: tags, bookmarks and inquiries key on the stored reference, and
    /// the Gemini batch translation reconciles the model's reply against it.
    /// A round trip through the formatter must not be mistaken for one.
    /// </summary>
    [Fact]
    public void DisplayIsNotAKey()
    {
        const string stored = "urn:cts:latinLit:phi0959.phi006.perseus-lat2.1.77";

        Assert.NotEqual(stored, PassageCitation.Display(stored));
        Assert.Equal(PassageAligner.ExtractPassageRef(stored), PassageCitation.Display(stored));
    }
}
