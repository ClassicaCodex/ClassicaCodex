using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The places map's coordinate table, hand-maintained and now past 240 entries.
/// </summary>
public class PlaceDataTests
{
    // The map's own viewport, from MapCanvas. A place outside it is not drawn, so an
    // entry beyond these bounds is a pin nobody will ever see.
    private const double MinLon = -12, MaxLon = 56, MinLat = 22, MaxLat = 59;

    [Fact]
    public void NoPlaceIsListedTwice()
    {
        var duplicates = PlaceData.Keys
            .GroupBy(k => k.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Every pin has to fall inside the map it is drawn on. A transposed latitude and
    /// longitude, or a sign dropped from a western coordinate, puts a place off the
    /// canvas entirely - and an absent pin looks exactly like a place nobody tagged.
    /// </summary>
    [Fact]
    public void EveryPlaceFallsInsideTheMap()
    {
        var offMap = PlaceData.Keys
            .Select(k => (Key: k, At: PlaceData.Lookup(k)))
            .Where(p => p.At != null &&
                        (p.At!.Value.Lat < MinLat || p.At.Value.Lat > MaxLat ||
                         p.At.Value.Lon < MinLon || p.At.Value.Lon > MaxLon))
            .Select(p => p.Key)
            .ToList();

        Assert.Empty(offMap);
    }

    /// <summary>
    /// The sees the Church Fathers are named after, since that is how that literature
    /// identifies its authors - Bishop of Hippo, of Arles, of Poetovio.
    /// </summary>
    [Theory]
    [InlineData("Hippo Regius")]
    [InlineData("Arles")]
    [InlineData("Poetovio")]
    [InlineData("Monte Cassino")]
    [InlineData("Canterbury")]
    public void SeesNamedByTheNewCollectionsResolve(string place)
    {
        Assert.NotNull(PlaceData.Lookup(place));
    }

    /// <summary>
    /// A shorter form of a longer name still finds it, which is what lets a tag reading
    /// "Hippo" reach the entry keyed "Hippo Regius" without a second row for it.
    /// </summary>
    [Fact]
    public void AShorterFormOfANameStillResolves()
    {
        var full = PlaceData.Lookup("Hippo Regius");
        var short_ = PlaceData.Lookup("Hippo");

        Assert.NotNull(full);
        Assert.NotNull(short_);
        Assert.Equal(full!.Value.Lat, short_!.Value.Lat, 3);
    }
}
