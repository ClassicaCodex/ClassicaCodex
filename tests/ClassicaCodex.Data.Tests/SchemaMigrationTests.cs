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

        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task EnsureSchemaIsIdempotent()
    {
        using var db = await TempDatabase.CreateAsync();

        await SchemaInitializer.EnsureSchemaAsync();
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("PassageTags"));
    }

    [Fact]
    public async Task LegacyDatabaseIsUpgraded_AndTheOldTableIsGone()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        Assert.True(await db.TableExistsAsync("TextNodeTags"));

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
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

        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
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

    /// <summary>
    /// Every table a migration creates must also be in SchemaStatements.
    ///
    /// A NEW database never runs a migration - InitializeAsync treats it as
    /// already current and builds it from SchemaStatements alone. So a table
    /// added only to Migrations exists on every upgraded library and on no
    /// fresh one, and the failure is invisible until somebody starts from an
    /// empty file.
    ///
    /// SavedSearches sat in that gap from v4 until this test was written, and
    /// StylometryExperiments joined it the same day. Rather than checking a
    /// list by eye, this creates a fresh database the way the application does
    /// and asks SQLite what is actually in it.
    /// </summary>
    [Fact]
    public async Task AFreshDatabaseHasEveryTableTheMigrationsWouldCreate()
    {
        using var db = await TempDatabase.CreateAsync();

        var expected = new[]
        {
            "Authors", "Works", "Editions", "TextNodes", "Tags", "PassageTags",
            "Bookmarks", "EditionHeaders", "EditionResponsibilities", "SavedSearches",
            "RecentSearches", "FavoriteWorks", "ApparatusEntries", "Artifacts",
            "ArtifactImages", "Lemmas", "Definitions",
            "StylometryRuns", "StylometryRunResults", "StylometryRunFeatures",
            "StylometryExperiments", "StylometryExperimentRows",
            "ResearchProjects", "ResearchQuestions", "EvidenceItems", "ResearchLogEntries"
        };

        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

        var present = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) present.Add(reader.GetString(0));
        }

        var missing = expected.Where(t => !present.Contains(t)).ToList();

        Assert.True(missing.Count == 0,
            $"a fresh database is missing: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The lowest version a CURRENT database can honestly be rewound to.
    ///
    /// RewindSchemaAsync drops columns; it cannot resurrect a table that a
    /// migration renamed or dropped. Migrations 2, 5 and 6 restructure -
    /// TextNodeTags becomes PassageTags, Bookmarks is rebuilt on
    /// (EditionId, CitationRef) - so a current file rewound below 6 claims a
    /// state it cannot represent, and the migration fails looking for a table
    /// that this database never had.
    ///
    /// MOVE THIS if a future migration restructures rather than adds. It would
    /// sit above the rewind point, run against a database already in its new
    /// shape, and fail the same way.
    /// </summary>
    private const int LowestRewindableVersion = 6;

    /// <summary>
    /// Rewinding a current database to the oldest state it can represent and
    /// upgrading it again must work - which it only does if RewindSchemaAsync
    /// knows about every column the migrations above that point add.
    ///
    /// A fresh database is built from SchemaStatements, the CURRENT shape, so
    /// every ALTER-added column is already there. Rewinding the version stamp
    /// without dropping those columns makes a file that claims to be v6 while
    /// carrying v16 columns, and the migration then fails re-adding one -
    /// which is exactly what NearestCount did.
    ///
    /// The other rewind tests each stop at some intermediate version and so
    /// exercise only the migrations above it. This one starts below every
    /// column-adding migration in the file (the earliest is v9), so an
    /// omission fails here whatever version introduced it, instead of waiting
    /// for someone to write a test that happens to rewind past it.
    /// </summary>
    [Fact]
    public async Task ARewindToTheOldestRepresentableVersionStillUpgrades()
    {
        using var db = await TempDatabase.CreateAsync();

        await db.RewindSchemaAsync(LowestRewindableVersion);
        Assert.Equal(LowestRewindableVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));

        // Throws if any migration re-adds something SchemaStatements already
        // created and RewindSchemaAsync did not remove.
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(SchemaInitializer.TargetSchemaVersion,
            await db.ScalarAsync<int>("PRAGMA user_version;"));
    }

    /// <summary>
    /// A fresh database must have every COLUMN the migrations add, not just
    /// every table.
    ///
    /// The table-level guard written alongside this one did not catch the Works
    /// attribution columns: they went into migration 17 and into the model, and
    /// the edit meant to add them to the fresh-database CREATE silently did not
    /// match. Works already existed, so the table check passed, and twelve
    /// tests failed on "no such column" instead.
    ///
    /// Reads TempDatabase.MigrationAddedColumns, the same list RewindSchemaAsync
    /// uses, so a new ALTER migration cannot satisfy one check and be forgotten
    /// by the other.
    /// </summary>
    [Fact]
    public async Task AFreshDatabaseHasEveryMigrationColumn()
    {
        using var db = await TempDatabase.CreateAsync();

        var missing = new List<string>();

        foreach (var (_, table, column) in TempDatabase.MigrationAddedColumns)
        {
            if (!(await db.ColumnNamesAsync(table)).Contains(column))
                missing.Add($"{table}.{column}");
        }

        Assert.True(missing.Count == 0,
            $"a fresh database is missing: {string.Join(", ", missing)}");
    }
}
