using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What happens to the category when a tag that already exists is asked for
/// again with a different one.
///
/// Auto-Tag has a category box, and it used to be discarded in silence for
/// any tag that already existed - type "goddess" over an "Athena" already
/// filed under "god", tag five hundred lines, and nothing anywhere said the
/// category had not changed.
///
/// The answer is not simply to apply it. That box carries a default of "god",
/// so re-tagging an existing "Troy" while the default is still showing would
/// quietly reclassify a place as a deity, and the categories drive the shapes
/// on the myth network. Keeping the existing one is the safer of the two, and
/// the caller is now told which category the tag actually has so it can say
/// so.
/// </summary>
[Collection("Database")]
public class TagCategoryTests
{
    [Fact]
    public async Task AnExistingCategoryIsKeptRatherThanOverwritten()
    {
        using var db = await TempDatabase.CreateAsync();
        var tags = new TagRepository();

        var first = await tags.GetOrCreateAsync("Athena", "god");
        var second = await tags.GetOrCreateAsync("Athena", "goddess");

        Assert.Equal(first.TagId, second.TagId);
        Assert.Equal("god", second.Category);
    }

    /// <summary>
    /// The caller has to be able to tell that its category was not used, or
    /// it cannot say so - which was the whole complaint.
    /// </summary>
    [Fact]
    public async Task TheCategoryActuallyStoredIsReportedBack()
    {
        using var db = await TempDatabase.CreateAsync();
        var tags = new TagRepository();

        await tags.GetOrCreateAsync("Troy", "place");
        var again = await tags.GetOrCreateAsync("Troy", "god");

        Assert.NotEqual("god", again.Category);
        Assert.Equal("place", again.Category);
    }

    /// <summary>
    /// Filling a gap cannot destroy anything, so a tag with no category yet
    /// does take the one supplied.
    /// </summary>
    [Fact]
    public async Task ATagWithNoCategoryTakesTheOneOffered()
    {
        using var db = await TempDatabase.CreateAsync();
        var tags = new TagRepository();

        var created = await tags.GetOrCreateAsync("Scamander", null);
        Assert.Null(created.Category);

        var filled = await tags.GetOrCreateAsync("Scamander", "river");

        Assert.Equal(created.TagId, filled.TagId);
        Assert.Equal("river", filled.Category);
    }

    [Fact]
    public async Task ANewTagKeepsTheCategoryItWasCreatedWith()
    {
        using var db = await TempDatabase.CreateAsync();

        var created = await new TagRepository().GetOrCreateAsync("Hector", "person");

        Assert.True(created.TagId > 0);
        Assert.Equal("person", created.Category);
    }
}
