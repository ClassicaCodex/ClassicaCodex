using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The timeline's date table, which is hand-curated and now large enough that its own
/// failure modes are worth pinning rather than trusting to care.
/// </summary>
public class AuthorEraDataTests
{
    /// <summary>
    /// Two entries under one key would silently shadow each other - whichever came
    /// first would answer for both, and nothing would say so. The table has grown past
    /// two hundred and fifty entries and is edited by hand.
    /// </summary>
    [Fact]
    public void NoAuthorIsListedTwice()
    {
        var duplicates = AuthorEraData.Keys
            .GroupBy(k => k.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// A start after its end is a typo that produces a bar the timeline draws backwards
    /// or not at all, and a sign error is easy to make in a table where BCE is negative.
    /// </summary>
    [Fact]
    public void EveryRangeRunsForwards()
    {
        var backwards = AuthorEraData.Keys
            .Select(k => (Key: k, Dates: AuthorEraData.Lookup(k)))
            .Where(e => e.Dates != null && e.Dates!.Value.StartYear > e.Dates.Value.EndYear)
            .Select(e => e.Key)
            .ToList();

        Assert.Empty(backwards);
    }

    /// <summary>
    /// The exact-match pass has to beat the substring pass, which is the whole reason it
    /// exists. Augustine of Canterbury is a different man from Augustine of Hippo, and
    /// his name contains the other's key - so without exact matching first he would
    /// inherit dates two centuries out.
    /// </summary>
    [Fact]
    public void AnExactNameBeatsAShorterOneItContains()
    {
        var hippo = AuthorEraData.Lookup("Augustinus");
        var canterbury = AuthorEraData.Lookup("Augustinus Apostolus Anglorum");

        Assert.NotNull(hippo);
        Assert.NotNull(canterbury);
        Assert.Equal(354, hippo!.Value.StartYear);
        Assert.Equal(530, canterbury!.Value.StartYear);
    }

    /// <summary>
    /// The names the new collections actually use, read from their own catalogues. A
    /// date keyed on a name no corpus uses is dead weight that looks like coverage.
    /// </summary>
    [Theory]
    [InlineData("Sanctus Ambrosius")]       // CSEL
    [InlineData("Ambrosius")]               // Patrologia Latina
    [InlineData("Cyprian Saint, Bishop of Carthage")]
    [InlineData("Hilary, Saint, Bishop of Poitiers")]
    [InlineData("Jean Bodin")]
    public void NamesUsedByTheNewCollectionsResolve(string catalogName)
    {
        Assert.NotNull(AuthorEraData.Lookup(catalogName));
    }
}
