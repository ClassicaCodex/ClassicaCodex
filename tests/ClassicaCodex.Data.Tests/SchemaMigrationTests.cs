using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The migration runs against databases that already hold someone's tags and
/// bookmarks - the least replaceable thing in the file, since the texts can
/// always be downloaded again. These tests build a real v1 database, put real
/// annotations in it, and check that upgrading keeps every one of them.
/// </summary>
[Collection("Database")]
public class SchemaMigrationTests
{
    [Fact]
    public async Task FreshDatabaseIsStampedAtTheCurrentVersion()
    {
        using var db = await TempDatabase.CreateAsync();

        Assert.Equal(6, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task EnsureSchemaIsIdempotent()
    {
        using var db = await TempDatabase.CreateAsync();

        await SchemaInitializer.EnsureSchemaAsync();
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(6, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("PassageTags"));
    }

    [Fact]
    public async Task LegacyDatabaseIsUpgraded_AndTheOldTableIsGone()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        Assert.True(await db.TableExistsAsync("TextNodeTags"));

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(6, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("PassageTags"));
        Assert.False(await db.TableExistsAsync("TextNodeTags"));
    }

    [Fact]
    public async Task MigrationCarriesTagsAcross_ResolvedToTheirPassage()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"), ("1.2", "οὐλομένην"));

        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync($@"
            INSERT INTO Tags (Name, Category) VALUES ('Achilles', 'person');
            INSERT INTO TextNodeTags (TextNodeId, TagId)
            VALUES ({nodeId}, (SELECT TagId FROM Tags WHERE Name = 'Achilles'));");

        await SchemaInitializer.EnsureSchemaAsync();

        var tagged = Assert.Single(await new TagRepository().GetByTagAsync("Achilles"));
        Assert.Equal("1.1", tagged.CitationRef);
        Assert.Equal("μῆνιν ἄειδε", tagged.Text);
    }

    [Fact]
    public async Task MigrationCarriesBookmarksAcross_NoteAndTimestampIntact()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));

        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync(
            $"INSERT INTO Bookmarks (BookmarkId, TextNodeId, Note, CreatedAt) " +
            $"VALUES (7, {nodeId}, 'cf. Norseverse thesis', '2026-01-02 03:04:05');");

        await SchemaInitializer.EnsureSchemaAsync();

        var bookmark = Assert.Single(await new BookmarkRepository().GetAllAsync());
        Assert.Equal(7, bookmark.BookmarkId);          // ids are carried, not reassigned
        Assert.Equal("cf. Norseverse thesis", bookmark.Note);
        Assert.Equal("1.1", bookmark.CitationRef);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), bookmark.CreatedAt);
    }

    /// <summary>
    /// The migration's INNER JOIN drops annotations whose text node is
    /// already gone. That's deliberate - they were dangling and could never
    /// have displayed - but it must not take the intact ones with them.
    /// </summary>
    [Fact]
    public async Task DanglingAnnotationsAreDropped_ValidOnesAreNot()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "real line"));
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");

        await db.ExecuteAsync($"INSERT INTO Bookmarks (TextNodeId, Note) VALUES ({nodeId}, 'valid');");

        // 999999 has no matching TextNode. Written with enforcement off,
        // because that's how such a row comes to exist in a real file:
        // SQLite's foreign keys are per-connection and off unless something
        // turns them on.
        await db.ExecuteUnenforcedAsync(
            "INSERT INTO Bookmarks (TextNodeId, Note) VALUES (999999, 'dangling');");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.CountAsync("Bookmarks"));
        var bookmark = Assert.Single(await new BookmarkRepository().GetAllAsync());
        Assert.Equal("valid", bookmark.Note);
    }

    /// <summary>
    /// Several text nodes can share a citation ref. The new primary key
    /// collapses them to one tag on that passage, which is what "this passage
    /// is tagged" meant all along - but it must not error on the duplicate.
    /// </summary>
    [Fact]
    public async Task DuplicateCitationRefsCollapseToOneTag()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "first half"), ("1.1", "second half"));

        await db.ExecuteAsync(@"
            INSERT INTO Tags (Name, Category) VALUES ('Achilles', 'person');
            INSERT INTO TextNodeTags (TextNodeId, TagId)
            SELECT tn.TextNodeId, (SELECT TagId FROM Tags WHERE Name = 'Achilles')
            FROM TextNodes tn;");

        Assert.Equal(2, await db.CountAsync("TextNodeTags"));

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.CountAsync("PassageTags"));
    }

    /// <summary>
    /// A file that already had migration 1 (the redundant-index drop) but
    /// nothing after it. Installs really do sit at intermediate versions, so
    /// the remaining steps must run on their own rather than only as part of
    /// a 0-to-current sweep.
    /// </summary>
    [Fact]
    public async Task DatabaseAlreadyAtVersionOneUpgradesToCurrent()
    {
        using var db = await TempDatabase.CreateLegacyAsync(userVersion: 1);
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "line"));
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync($"INSERT INTO Bookmarks (TextNodeId, Note) VALUES ({nodeId}, 'note');");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(6, await db.ScalarAsync<int>("PRAGMA user_version;"));
        var bookmark = Assert.Single(await new BookmarkRepository().GetAllAsync());
        Assert.Equal("note", bookmark.Note);
    }

    [Fact]
    public async Task MigrationOneDropsTheRedundantTagsNameIndex()
    {
        using var db = await TempDatabase.CreateLegacyAsync();

        Assert.Equal(1, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Tags_Name';"));

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(0, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Tags_Name';"));
    }

    /// <summary>
    /// Migrations turn foreign key enforcement off to rebuild tables, and the
    /// pragma is per-connection - so a connection opened afterwards must
    /// still have it on, or every constraint in the schema goes decorative
    /// for the rest of the session.
    /// </summary>
    [Fact]
    public async Task ForeignKeysAreStillEnforcedAfterMigrating()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.ScalarAsync<long>("PRAGMA foreign_keys;"));

        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO PassageTags (EditionId, CitationRef, TagId) VALUES (1, '1.1', 999999);";

        await Assert.ThrowsAnyAsync<Exception>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task MigrationLeavesNoBrokenForeignKeys()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "line"));
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync($@"
            INSERT INTO Tags (Name, Category) VALUES ('Achilles', 'person');
            INSERT INTO TextNodeTags (TextNodeId, TagId)
            VALUES ({nodeId}, (SELECT TagId FROM Tags WHERE Name = 'Achilles'));
            INSERT INTO Bookmarks (TextNodeId, Note) VALUES ({nodeId}, 'note');");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(0, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }
}
