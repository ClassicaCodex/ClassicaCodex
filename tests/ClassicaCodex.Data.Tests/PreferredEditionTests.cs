using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Which edition a work opens on when several collections carry it.
///
/// The rule has to degrade rather than fail. A preference is set once and then
/// meets thousands of works, most of which have nothing to do with it: the
/// collection is not among this work's editions, or is no longer installed at
/// all. None of those is an error, and any of them producing an empty pane
/// instead of a text would be a far worse outcome than the ordering quirk this
/// exists to fix.
/// </summary>
public class PreferredEditionTests
{
    private static Edition From(string collection, string urn) =>
        new() { CtsUrn = urn, Collection = collection };

    [Fact]
    public void PreferredCollectionWinsOverListOrder()
    {
        var editions = new[]
        {
            From("first1k-greek", "urn:cts:greekLit:tlg0085.tlg005.opp-grc1"),
            From("perseus-greek", "urn:cts:greekLit:tlg0085.tlg005.perseus-grc2")
        };

        Assert.Equal(1, PreferredEdition.IndexOfDefault(editions, "perseus-greek"));
    }

    [Fact]
    public void NoPreferenceKeepsTheFirst()
    {
        var editions = new[] { From("first1k-greek", "a"), From("perseus-greek", "b") };

        Assert.Equal(0, PreferredEdition.IndexOfDefault(editions, null));
        Assert.Equal(0, PreferredEdition.IndexOfDefault(editions, string.Empty));
        Assert.Equal(0, PreferredEdition.IndexOfDefault(editions, "   "));
    }

    /// <summary>
    /// The ordinary case, and the one that has to stay silent: most works are
    /// in exactly one collection, and a preference naming a different one must
    /// leave them opening exactly as they did before.
    /// </summary>
    [Fact]
    public void AWorkThePreferredCollectionDoesNotHaveOpensNormally()
    {
        var editions = new[] { From("csel", "a"), From("patrologia-latina", "b") };

        Assert.Equal(0, PreferredEdition.IndexOfDefault(editions, "perseus-greek"));
    }

    /// <summary>
    /// Editions ingested before collections were stamped have no collection at
    /// all, which must read as "not the preferred one" rather than matching a
    /// preference by way of two nulls being equal.
    /// </summary>
    [Fact]
    public void EditionsWithNoCollectionNeverMatchAPreference()
    {
        var editions = new[]
        {
            new Edition { CtsUrn = "a", Collection = null },
            From("csel", "b")
        };

        Assert.Equal(1, PreferredEdition.IndexOfDefault(editions, "csel"));
        Assert.Equal(0, PreferredEdition.IndexOfDefault(editions, "patrologia-latina"));
    }

    [Fact]
    public void TheFirstOfSeveralFromThePreferredCollectionIsChosen()
    {
        var editions = new[]
        {
            From("perseus-greek", "a"),
            From("csel", "b"),
            From("csel", "c")
        };

        Assert.Equal(1, PreferredEdition.IndexOfDefault(editions, "csel"));
    }

    [Fact]
    public void AnEmptyListHasNothingToSelect()
    {
        Assert.Equal(-1, PreferredEdition.IndexOfDefault(Array.Empty<Edition>(), "csel"));
        Assert.Equal(-1, PreferredEdition.IndexOfDefault(Array.Empty<Edition>(), null));
    }
}
