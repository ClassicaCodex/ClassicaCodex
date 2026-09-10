using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Marking a list of passages with the tags they happen to carry - what the
/// places map does to its search results.
///
/// The lookup used to hand SQLite the passage ids and let it do the narrowing.
/// That read well and scaled badly: the planner drove the join from Tags, so
/// the work was one random lookup into the 2.34-million-row TextNodes table
/// per tag per id, and clicking a well-attested place cost 1,636 ms. The cost
/// multiplied by the number of tags in the library - two, here - so it was a
/// stall that would have become a hang for anyone who used tags much.
///
/// It now reads every tagged passage and narrows in memory. These tests are
/// about the part that must not have changed: which passages come back, with
/// which names, in which order.
/// </summary>
[Collection("Database")]
public class TaggedPassageLookupTests
{
    [Fact]
    public async Task OnlyTheAskedForPassagesComeBack()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"), ("1.2", "οὐλομένην"), ("1.3", "ἐξ οὗ δὴ"));

        var tags = new TagRepository();
        var achilles = (await tags.GetOrCreateAsync("Achilles", "person")).TagId;
        var first = await db.TextNodeIdAsync(editionId, "1.1");
        var second = await db.TextNodeIdAsync(editionId, "1.2");
        await tags.TagTextNodeAsync(first, achilles);
        await tags.TagTextNodeAsync(second, achilles);

        var found = await tags.GetTagNamesForNodesAsync(new[] { first });

        Assert.Single(found);
        Assert.True(found.ContainsKey(first));
        Assert.False(found.ContainsKey(second));
    }

    /// <summary>
    /// The whole point of the method: an untagged passage in the list is
    /// simply absent from the answer, not present with an empty list.
    /// </summary>
    [Fact]
    public async Task AnUntaggedPassageIsAbsentRatherThanEmpty()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"), ("1.2", "οὐλομένην"));

        var tags = new TagRepository();
        var tagId = (await tags.GetOrCreateAsync("Achilles", "person")).TagId;
        var tagged = await db.TextNodeIdAsync(editionId, "1.1");
        var untagged = await db.TextNodeIdAsync(editionId, "1.2");
        await tags.TagTextNodeAsync(tagged, tagId);

        var found = await tags.GetTagNamesForNodesAsync(new[] { tagged, untagged });

        Assert.Equal(new[] { "Achilles" }, found[tagged]);
        Assert.False(found.ContainsKey(untagged));
    }

    /// <summary>
    /// Several tags on one passage come back together and in name order,
    /// which is what the ORDER BY was for and what the markers read off.
    /// </summary>
    [Fact]
    public async Task SeveralTagsOnOnePassageComeBackInNameOrder()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));

        var tags = new TagRepository();
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        foreach (var name in new[] { "wrath", "Achilles", "proem" })
        {
            await tags.TagTextNodeAsync(nodeId, (await tags.GetOrCreateAsync(name, "theme")).TagId);
        }

        var found = await tags.GetTagNamesForNodesAsync(new[] { nodeId });

        Assert.Equal(new[] { "Achilles", "proem", "wrath" }, found[nodeId]);
    }

    /// <summary>
    /// Asking about nothing does no work and returns nothing - the map calls
    /// this with the results of a search that found none.
    /// </summary>
    [Fact]
    public async Task AnEmptyRequestReturnsNothing()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));

        var tags = new TagRepository();
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.1"),
            (await tags.GetOrCreateAsync("Achilles", "person")).TagId);

        Assert.Empty(await tags.GetTagNamesForNodesAsync(Array.Empty<long>()));
    }

    /// <summary>
    /// A library with no tags at all - every library, until someone makes the
    /// first one. The map is clickable throughout.
    /// </summary>
    [Fact]
    public async Task ALibraryWithNoTagsAnswersWithNothing()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));

        var found = await new TagRepository().GetTagNamesForNodesAsync(
            new[] { await db.TextNodeIdAsync(editionId, "1.1") });

        Assert.Empty(found);
    }

    /// <summary>
    /// Tags are stored against edition and citation reference rather than
    /// against a passage id, so that they survive a re-ingest. Where an
    /// edition genuinely repeats a citation - and this corpus does - that one
    /// tag belongs to every passage carrying the reference, and the narrowing
    /// must not lose the ones the caller asked about.
    /// </summary>
    [Fact]
    public async Task ARepeatedCitationTagsEveryPassageThatCarriesIt()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "first half"), ("1.1", "second half"));

        var tags = new TagRepository();
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await tags.TagTextNodeAsync(nodeId, (await tags.GetOrCreateAsync("proem", "theme")).TagId);

        var all = await db.TextNodeIdsAsync(editionId, "1.1");
        Assert.Equal(2, all.Count);

        var found = await tags.GetTagNamesForNodesAsync(all);

        Assert.Equal(2, found.Count);
        foreach (var id in all) Assert.Equal(new[] { "proem" }, found[id]);
    }
}
