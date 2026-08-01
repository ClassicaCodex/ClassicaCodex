using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The behaviour these tests exist for: a reader tags and bookmarks passages,
/// then a corpus update re-ingests the edition. Their annotations must still
/// be there afterwards, pointing at the same passages.
///
/// Before annotations were re-keyed, none of this worked. The re-ingest's
/// DELETE tripped the foreign key from the annotation tables, the ingest
/// service caught it per-file and filed the edition under failures, and the
/// edition was skipped - so the texts someone had worked with most were
/// exactly the ones that quietly stopped updating.
/// </summary>
[Collection("Database")]
public class AnnotationDurabilityTests
{
    [Fact]
    public async Task ReingestingAnEditionDoesNotFail_WhenPassagesAreTagged()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"), ("1.2", "οὐλομένην"));

        var tags = new TagRepository();
        var tagId = await tags.GetOrCreateAsync("Achilles", "person");
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.1"), tagId);

        // The exact operation that used to throw FOREIGN KEY constraint failed.
        var exception = await Record.ExceptionAsync(() =>
            new EditionRepository().ClearTextNodesAsync(editionId));

        Assert.Null(exception);
    }

    [Fact]
    public async Task TagsSurviveAReingest_AndReattachToTheSamePassage()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"), ("1.2", "οὐλομένην"));

        var tags = new TagRepository();
        var tagId = await tags.GetOrCreateAsync("Achilles", "person");
        var originalNodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await tags.TagTextNodeAsync(originalNodeId, tagId);

        // A second edition, so the re-ingested rows can't simply reclaim the
        // rowids they just freed. SQLite hands out max(rowid)+1, so without
        // something holding a higher id the "new" node can come back with the
        // same number and the test would pass for the wrong reason.
        var other = await db.SeedEditionAsync("other");
        await db.InsertLinesAsync(other, ("1.1", "different work"));

        // A repo update: same citations, revised text, brand new node ids.
        await db.ReingestAsync(editionId, ("1.1", "μῆνιν ἄειδε θεά"), ("1.2", "οὐλομένην"));

        var newNodeId = await db.TextNodeIdAsync(editionId, "1.1");
        Assert.NotEqual(originalNodeId, newNodeId);

        var tagged = await tags.GetByTagAsync("Achilles");

        var hit = Assert.Single(tagged);
        Assert.Equal("1.1", hit.CitationRef);
        Assert.Equal("μῆνιν ἄειδε θεά", hit.Text);
        Assert.Equal(newNodeId, hit.TextNodeId);
    }

    [Fact]
    public async Task BookmarksSurviveAReingest_NoteIntact()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));

        var bookmarks = new BookmarkRepository();
        await bookmarks.AddAsync(await db.TextNodeIdAsync(editionId, "1.1"), "cf. Norseverse thesis");

        await db.ReingestAsync(editionId, ("1.1", "μῆνιν ἄειδε θεά"));

        var all = await bookmarks.GetAllAsync();

        var bookmark = Assert.Single(all);
        Assert.Equal("cf. Norseverse thesis", bookmark.Note);
        Assert.Equal("1.1", bookmark.CitationRef);
        Assert.Equal("μῆνιν ἄειδε θεά", bookmark.Text);
    }

    /// <summary>
    /// An annotation on a citation the new ingest no longer produces is
    /// dormant, not deleted. It stops showing up, and it comes back if a
    /// later ingest restores that citation - which is the property that makes
    /// this safe to run against someone's real library.
    /// </summary>
    [Fact]
    public async Task AnnotationsOnVanishedPassagesAreDormant_NotDestroyed()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "first"), ("1.2", "second"));

        var bookmarks = new BookmarkRepository();
        await bookmarks.AddAsync(await db.TextNodeIdAsync(editionId, "1.2"), "keep me");

        // The new ingest drops 1.2 entirely.
        await db.ReingestAsync(editionId, ("1.1", "first"));

        Assert.Empty(await bookmarks.GetAllAsync());
        Assert.Equal(1, await bookmarks.CountDormantAsync());
        Assert.Equal(1, await db.CountAsync("Bookmarks"));

        // ...and a later ingest that restores 1.2 brings it back untouched.
        await db.ReingestAsync(editionId, ("1.1", "first"), ("1.2", "second again"));

        var restored = Assert.Single(await bookmarks.GetAllAsync());
        Assert.Equal("keep me", restored.Note);
        Assert.Equal("second again", restored.Text);
        Assert.Equal(0, await bookmarks.CountDormantAsync());
    }

    [Fact]
    public async Task TaggingIsIdempotent_AcrossReingests()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "line"));

        var tags = new TagRepository();
        var tagId = await tags.GetOrCreateAsync("Troy", "place");

        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.1"), tagId);
        await db.ReingestAsync(editionId, ("1.1", "line"));
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.1"), tagId);

        Assert.Equal(1, await db.CountAsync("PassageTags"));
    }

    /// <summary>
    /// Two editions can legitimately use the same citation ref - "1.1" means
    /// something different in the Iliad than in the Odyssey - so the passage
    /// key has to include the edition.
    /// </summary>
    [Fact]
    public async Task AnnotationsDoNotLeakBetweenEditionsSharingACitationRef()
    {
        using var db = await TempDatabase.CreateAsync();
        var iliad = await db.SeedEditionAsync("iliad");
        var odyssey = await db.SeedEditionAsync("odyssey");
        await db.InsertLinesAsync(iliad, ("1.1", "wrath"));
        await db.InsertLinesAsync(odyssey, ("1.1", "the man"));

        var tags = new TagRepository();
        var tagId = await tags.GetOrCreateAsync("Achilles", "person");
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(iliad, "1.1"), tagId);

        var tagged = Assert.Single(await tags.GetByTagAsync("Achilles"));
        Assert.Equal("wrath", tagged.Text);
    }

    [Fact]
    public async Task BulkTaggingUsesTheDurableKeyToo()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "alpha"), ("1.2", "beta"), ("1.3", "gamma"));

        var tags = new TagRepository();
        var tagId = await tags.GetOrCreateAsync("Auto", null);

        var ids = new[]
        {
            await db.TextNodeIdAsync(editionId, "1.1"),
            await db.TextNodeIdAsync(editionId, "1.3")
        };
        await tags.BulkTagTextNodesAsync(tagId, ids);

        await db.ReingestAsync(editionId, ("1.1", "alpha"), ("1.2", "beta"), ("1.3", "gamma"));

        var tagged = await tags.GetByTagAsync("Auto");
        Assert.Equal(2, tagged.Count);
        Assert.Contains(tagged, t => t.CitationRef == "1.1");
        Assert.Contains(tagged, t => t.CitationRef == "1.3");
    }
}
