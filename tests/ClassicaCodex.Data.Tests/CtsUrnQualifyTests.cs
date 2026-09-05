using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Putting an edition's identifier back into a citable form.
///
/// Editions are stored with the namespace stripped - the Aeneid's Perseus text
/// is "phi0690.phi003.perseus-lat1", not
/// "urn:cts:latinLit:phi0690.phi003.perseus-lat1". Fine as a key, useless in a
/// citation: nothing in it says which namespace it belongs to and a reader
/// cannot resolve it. The work above it keeps the whole thing, so the namespace
/// is not lost, only stored one row up.
///
/// The risk in reassembling them is asserting a namespace that was never known,
/// which would be worse than the truncation - so everything here is about
/// refusing to guess.
/// </summary>
public class CtsUrnQualifyTests
{
    [Fact]
    public void AStrippedEditionGetsItsWorkNamespaceBack() =>
        Assert.Equal(
            "urn:cts:latinLit:phi0690.phi003.perseus-lat1",
            CtsUrns.Qualify("urn:cts:latinLit:phi0690.phi003", "phi0690.phi003.perseus-lat1"));

    /// <summary>
    /// The case the whole thing is for: CSEL and Migne ship one work, and the
    /// two identifiers have to come out different and complete.
    /// </summary>
    [Fact]
    public void TheTwoEditionsOfOneWorkStayDistinct()
    {
        const string work = "urn:cts:latinLit:stoa0096.stoa003";

        Assert.Equal("urn:cts:latinLit:stoa0096.stoa003.opp-lat2",
            CtsUrns.Qualify(work, "stoa0096.stoa003.opp-lat2"));
        Assert.Equal("urn:cts:latinLit:stoa0096.stoa003.opp-lat1",
            CtsUrns.Qualify(work, "stoa0096.stoa003.opp-lat1"));
    }

    /// <summary>
    /// Menota stores its identifiers whole and in its own scheme. Nothing to
    /// add, and adding anything would corrupt them.
    /// </summary>
    [Fact]
    public void AnAlreadyCompleteIdentifierIsUntouched() =>
        Assert.Equal(
            "urn:menota:anonymous:eddic-poems:gks-2365-4to",
            CtsUrns.Qualify("urn:cts:latinLit:x.y", "urn:menota:anonymous:eddic-poems:gks-2365-4to"));

    /// <summary>
    /// The Renaissance collection is not CTS at all. Left exactly as it is
    /// rather than given a namespace it does not have.
    /// </summary>
    [Fact]
    public void ANonCtsIdentifierIsLeftAlone() =>
        Assert.Equal("engLit:renaissance:abbott:abbott:opensource",
            CtsUrns.Qualify("engLit:renaissance:abbott", "engLit:renaissance:abbott:abbott:opensource"));

    /// <summary>
    /// An edition that does not extend this work is not this work's edition,
    /// and must not be given its namespace on a coincidence of shape.
    /// </summary>
    [Fact]
    public void AnEditionThatDoesNotBelongToTheWorkIsNotQualified() =>
        Assert.Equal("phi0959.phi006.perseus-lat1",
            CtsUrns.Qualify("urn:cts:latinLit:phi0690.phi003", "phi0959.phi006.perseus-lat1"));

    /// <summary>
    /// A prefix match is not enough either - "phi0690.phi0031" starts with the
    /// work id but is a different work, so the separator has to be there.
    /// </summary>
    [Fact]
    public void APrefixWithoutTheSeparatorIsNotAMatch() =>
        Assert.Equal("phi0690.phi0031.perseus-lat1",
            CtsUrns.Qualify("urn:cts:latinLit:phi0690.phi003", "phi0690.phi0031.perseus-lat1"));

    [Theory]
    [InlineData(null, "phi0690.phi003.perseus-lat1", "phi0690.phi003.perseus-lat1")]
    [InlineData("", "phi0690.phi003.perseus-lat1", "phi0690.phi003.perseus-lat1")]
    [InlineData("not-a-urn", "phi0690.phi003.perseus-lat1", "phi0690.phi003.perseus-lat1")]
    [InlineData("urn:cts:latinLit", "phi0690.phi003.perseus-lat1", "phi0690.phi003.perseus-lat1")]
    [InlineData("urn:cts:latinLit:phi0690.phi003", null, "")]
    [InlineData("urn:cts:latinLit:phi0690.phi003", "   ", "")]
    public void NothingUsableInMeansNothingInvented(string? work, string? edition, string expected) =>
        Assert.Equal(expected, CtsUrns.Qualify(work, edition));

    /// <summary>
    /// Whitespace either side is trimmed - the work URNs four Patrologia
    /// catalogue files ship with a trailing space are repaired on upgrade, but
    /// nothing here should depend on that having happened.
    /// </summary>
    [Fact]
    public void SurroundingWhitespaceDoesNotDefeatIt() =>
        Assert.Equal("urn:cts:latinLit:stoa0223.stoa001.opp-lat1",
            CtsUrns.Qualify(" urn:cts:latinLit:stoa0223.stoa001 ", " stoa0223.stoa001.opp-lat1 "));
}
