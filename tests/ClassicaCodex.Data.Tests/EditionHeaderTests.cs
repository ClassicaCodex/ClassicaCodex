using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Edition headers moved from "re-read the source file each time" to stored
/// data. That was worth doing because it removed the only remaining reason
/// the app needed the corpus files after ingest - but it means a schema
/// change, and schema changes against a real library are the thing this
/// suite exists to make safe.
/// </summary>
[Collection("Database")]
public class EditionHeaderTests
{
    private static EditionHeader SampleHeader() => new()
    {
        Title = "Homeri Opera",
        Author = "Homer",
        Responsibilities = new[] { "Editor: David B. Monro", "Editor: Thomas W. Allen" },
        Publisher = "Clarendon Press",
        PublicationDate = "1920",
        PublicationPlace = "Oxford",
        SourceDescription = "Homer, Homeri Opera, Oxford, Clarendon Press, 1920",
        EditionStatement = "Editio Tertia",
        Availability = "CC BY-SA 3.0"
    };

    [Fact]
    public async Task SavedHeaderRoundTripsCompletely()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();

        var repo = new EditionHeaderRepository();
        await repo.SaveAsync(editionId, SampleHeader());

        var loaded = await repo.GetAsync(editionId);

        Assert.NotNull(loaded);
        Assert.Equal("Homeri Opera", loaded!.Title);
        Assert.Equal("Homer", loaded.Author);
        Assert.Equal("Clarendon Press", loaded.Publisher);
        Assert.Equal("1920", loaded.PublicationDate);
        Assert.Equal("Oxford", loaded.PublicationPlace);
        Assert.Equal("Editio Tertia", loaded.EditionStatement);
        Assert.Equal("CC BY-SA 3.0", loaded.Availability);
        Assert.Equal("Homer, Homeri Opera, Oxford, Clarendon Press, 1920", loaded.SourceDescription);
    }

    /// <summary>
    /// Responsibilities are a list, and their order is the file's order -
    /// a child table rather than a joined string precisely so that survives.
    /// </summary>
    [Fact]
    public async Task ResponsibilitiesKeepTheirOrder()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();

        var repo = new EditionHeaderRepository();
        await repo.SaveAsync(editionId, SampleHeader());

        var loaded = await repo.GetAsync(editionId);

        Assert.Equal(
            new[] { "Editor: David B. Monro", "Editor: Thomas W. Allen" },
            loaded!.Responsibilities);
    }

    /// <summary>
    /// Re-ingesting after an upstream correction must leave the stored
    /// header matching the file exactly - including dropping fields the new
    /// version no longer has. An update-in-place would strand the old values.
    /// </summary>
    [Fact]
    public async Task SavingAgainReplacesRatherThanMerges()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        var repo = new EditionHeaderRepository();

        await repo.SaveAsync(editionId, SampleHeader());
        await repo.SaveAsync(editionId, new EditionHeader
        {
            Title = "Revised",
            Responsibilities = new[] { "Editor: Someone Else" }
        });

        var loaded = await repo.GetAsync(editionId);

        Assert.Equal("Revised", loaded!.Title);
        Assert.Null(loaded.Publisher);
        Assert.Null(loaded.SourceDescription);
        Assert.Equal(new[] { "Editor: Someone Else" }, loaded.Responsibilities);
        Assert.Equal(1, await db.CountAsync("EditionResponsibilities"));
    }

    [Fact]
    public async Task NoStoredHeaderReturnsNull()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();

        Assert.Null(await new EditionHeaderRepository().GetAsync(editionId));
    }

    [Fact]
    public async Task FreshDatabaseIsStampedAtVersionThree()
    {
        using var db = await TempDatabase.CreateAsync();

        Assert.Equal(3, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("EditionHeaders"));
        Assert.True(await db.TableExistsAsync("EditionResponsibilities"));
    }

    /// <summary>
    /// The upgrade path an existing library actually takes. Nothing is
    /// backfilled - the information was never stored, so there's nothing to
    /// backfill from - but the tables must arrive and the annotations that
    /// migration 2 moved must still be intact afterwards.
    /// </summary>
    [Fact]
    public async Task LegacyDatabaseGainsHeaderTablesWithoutLosingAnnotations()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync(
            $"INSERT INTO Bookmarks (TextNodeId, Note) VALUES ({nodeId}, 'cf. Norseverse thesis');");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(3, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("EditionHeaders"));

        var bookmark = Assert.Single(await new BookmarkRepository().GetAllAsync());
        Assert.Equal("cf. Norseverse thesis", bookmark.Note);

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    /// <summary>
    /// A database already at v2 - which is what anyone who has been running
    /// this build has - takes only the v3 step.
    ///
    /// Built by creating a current database and removing what v3 added,
    /// rather than by stamping the legacy fixture at 2: that fixture has the
    /// v1 table shapes, so calling it v2 would describe a state that can't
    /// exist, skip migration 2, and then fail on an index over columns
    /// migration 2 was supposed to have created.
    /// </summary>
    [Fact]
    public async Task DatabaseAtVersionTwoUpgradesToThree()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(
            "DROP TABLE EditionResponsibilities; DROP TABLE EditionHeaders; PRAGMA user_version = 2;");

        Assert.False(await db.TableExistsAsync("EditionHeaders"));

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(3, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("EditionHeaders"));
        Assert.True(await db.TableExistsAsync("EditionResponsibilities"));
    }
}
