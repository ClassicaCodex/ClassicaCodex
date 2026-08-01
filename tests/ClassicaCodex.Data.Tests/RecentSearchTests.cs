using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The recent-search list: every search recorded as it is run, capped, with
/// no saving or deleting to do by hand.
///
/// The property worth pinning hardest is that an entry names its author
/// rather than pointing at an id. Ids are assigned by the database and
/// renumber completely when a corpus is re-ingested into a fresh file, so an
/// entry keyed on them would keep working and quietly mean something
/// different - the worst failure available here.
/// </summary>
[Collection("Database")]
public class RecentSearchTests
{
    private static RecentSearch Sample() => new()
    {
        Name = "Prometheus in Greek",
        Query = "Προμηθεύς",
        MatchMode = nameof(SearchMatchMode.WholeWord),
        Languages = "grc",
        OriginalsOnly = true,
        AuthorName = "Aeschylus",
        TagName = "Prometheus",
        BookmarkedOnly = false,
        EraLabel = "Classical (500-323 BCE)"
    };

    [Fact]
    public async Task RecentSearchRoundTripsEveryField()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        await repo.RecordAsync(Sample());
        var loaded = Assert.Single(await repo.GetAllAsync());

        Assert.Equal("Prometheus in Greek", loaded.Name);
        Assert.Equal("Προμηθεύς", loaded.Query);
        Assert.Equal(nameof(SearchMatchMode.WholeWord), loaded.MatchMode);
        Assert.Equal("grc", loaded.Languages);
        Assert.True(loaded.OriginalsOnly);
        Assert.Equal("Aeschylus", loaded.AuthorName);
        Assert.Equal("Prometheus", loaded.TagName);
        Assert.False(loaded.BookmarkedOnly);
        Assert.Equal("Classical (500-323 BCE)", loaded.EraLabel);
    }

    /// <summary>
    /// Three states, not two: originals only, translations only, and both.
    /// Null has to survive as null rather than collapsing into false, which
    /// would silently narrow a search that was meant to cover everything.
    /// </summary>
    [Fact]
    public async Task BothKindsIsStoredAsNullNotFalse()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        await repo.RecordAsync(new RecentSearch { Name = "both", Query = "x", OriginalsOnly = null });
        await repo.RecordAsync(new RecentSearch { Name = "translations", Query = "x", OriginalsOnly = false });

        var all = await repo.GetAllAsync();

        Assert.Null(all.Single(s => s.Name == "both").OriginalsOnly);
        Assert.False(all.Single(s => s.Name == "translations").OriginalsOnly);
    }

    /// <summary>
    /// Running the same search twice moves it up rather than listing it
    /// twice - two entries with the same description would be
    /// indistinguishable in the picker.
    /// </summary>
    [Fact]
    public async Task ReRunningASearchDoesNotDuplicateIt()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        await repo.RecordAsync(Sample());
        var updated = Sample();
        updated.Query = "changed";
        updated.AuthorName = null;
        await repo.RecordAsync(updated);

        var loaded = Assert.Single(await repo.GetAllAsync());

        Assert.Equal("changed", loaded.Query);
        Assert.Null(loaded.AuthorName);
    }

    [Fact]
    public async Task MostRecentlyRunComesFirst()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        foreach (var name in new[] { "first", "second", "third" })
        {
            await repo.RecordAsync(new RecentSearch { Name = name, Query = "x" });
        }

        Assert.Equal(new[] { "third", "second", "first" }, (await repo.GetAllAsync()).Select(s => s.Name));
    }

    /// <summary>
    /// Re-running an older search brings it back to the front, so the list
    /// reflects what was actually used most recently rather than when each
    /// was first seen.
    /// </summary>
    [Fact]
    public async Task ReRunningAnOlderSearchMovesItToTheFront()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        await repo.RecordAsync(new RecentSearch { Name = "older", Query = "a" });
        await repo.RecordAsync(new RecentSearch { Name = "newer", Query = "b" });
        await repo.RecordAsync(new RecentSearch { Name = "older", Query = "a" });

        var all = await repo.GetAllAsync();

        Assert.Equal(new[] { "older", "newer" }, all.Select(s => s.Name));
        Assert.Equal(2, all.Count);
    }

    /// <summary>
    /// The list keeps itself to size - that is the whole reason there is no
    /// delete button.
    /// </summary>
    [Fact]
    public async Task OnlyTheMostRecentAreKept()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        for (var i = 1; i <= RecentSearchRepository.MaxRecent + 5; i++)
        {
            await repo.RecordAsync(new RecentSearch { Name = $"search {i:00}", Query = "x" });
        }

        var all = await repo.GetAllAsync();

        Assert.Equal(RecentSearchRepository.MaxRecent, all.Count);
        Assert.Equal("search 15", all.First().Name);
        Assert.DoesNotContain(all, s => s.Name == "search 01");
    }

    [Fact]
    public async Task ClearingEmptiesTheList()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        await repo.RecordAsync(new RecentSearch { Name = "one", Query = "x" });
        await repo.RecordAsync(new RecentSearch { Name = "two", Query = "x" });
        await repo.ClearAsync();

        Assert.Empty(await repo.GetAllAsync());
    }

    /// <summary>
    /// The durability property. An author id would be meaningless after a
    /// re-ingest into a fresh database; a name still identifies the same
    /// author, and identifies nothing at all when that corpus is absent -
    /// which is the correct answer rather than a wrong match.
    /// </summary>
    [Fact]
    public async Task RecentSearchSurvivesTheLibraryBeingRebuilt()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new RecentSearchRepository();

        await db.SeedFullEditionAsync("aesch", "Aeschylus", "greekLit", "Agamemnon", "Original", "grc");
        await repo.RecordAsync(Sample());

        // Everything the corpus contributed goes; the saved search does not.
        await db.ExecuteAsync("DELETE FROM TextNodes; DELETE FROM Editions; DELETE FROM Works; DELETE FROM Authors;");

        var loaded = Assert.Single(await repo.GetAllAsync());

        Assert.Equal("Aeschylus", loaded.AuthorName);
        Assert.Equal("Classical (500-323 BCE)", loaded.EraLabel);
    }

    [Fact]
    public async Task FreshDatabaseHasTheRecentSearchTable()
    {
        using var db = await TempDatabase.CreateAsync();

        Assert.Equal(6, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("RecentSearches"));
    }

    /// <summary>
    /// The repository run against a database that got here by migrating,
    /// rather than one created fresh - and the gap that let a real bug
    /// through.
    ///
    /// A new database never runs the migrations at all: it gets the current
    /// schema and is stamped at the target version. So every other test here
    /// was exercising a table built by the current DDL, while a real library
    /// had one built by v4 and altered by v5. Those two disagreed - RENAME
    /// TO renames a table and nothing inside it, so the migrated column was
    /// still called SavedSearchId - and every query failed on exactly the
    /// databases that matter while the suite stayed green.
    /// </summary>
    [Fact]
    public async Task RepositoryWorksOnAMigratedDatabaseNotJustAFreshOne()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        await SchemaInitializer.EnsureSchemaAsync();

        var repo = new RecentSearchRepository();
        await repo.RecordAsync(new RecentSearch { Name = "first", Query = "a" });
        await repo.RecordAsync(new RecentSearch { Name = "second", Query = "b" });
        await repo.RecordAsync(new RecentSearch { Name = "first", Query = "a" });

        var all = await repo.GetAllAsync();

        Assert.Equal(new[] { "first", "second" }, all.Select(x => x.Name));
    }

    /// <summary>
    /// A database stamped at an intermediate version, which is the state a
    /// real install lands in and no other test here covers.
    ///
    /// Migrations only run for versions above the one recorded, so a fix
    /// folded into a step that has already executed reaches every database
    /// except the ones needing it. This starts from the exact shape v5 left
    /// behind - table renamed, column not - and asserts the schema converges
    /// anyway.
    /// </summary>
    [Fact]
    public async Task DatabaseStampedAtAnIntermediateVersionStillConverges()
    {
        using var db = await TempDatabase.CreateAsync();

        // Reproduce what migration 5 alone produced, and claim to be at 5.
        await db.ExecuteAsync(@"
            DROP TABLE RecentSearches;
            CREATE TABLE RecentSearches (
                SavedSearchId  INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT NOT NULL,
                Query          TEXT NOT NULL,
                MatchMode      TEXT NOT NULL,
                Languages      TEXT NOT NULL DEFAULT '',
                Corpora        TEXT NOT NULL DEFAULT '',
                OriginalsOnly  INTEGER NULL,
                AuthorName     TEXT NULL,
                TagName        TEXT NULL,
                BookmarkedOnly INTEGER NOT NULL DEFAULT 0,
                EraLabel       TEXT NULL,
                CreatedAt      TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT UQ_SavedSearches_Name UNIQUE (Name));
            INSERT INTO RecentSearches (Name, Query, MatchMode, CreatedAt)
                VALUES ('carried over', 'q', 'Contains', '2026-01-01T00:00:00.0000000');
            PRAGMA user_version = 5;");

        await SchemaInitializer.EnsureSchemaAsync();

        // The remaining step runs, the schema matches, and what was already
        // recorded survives.
        var repo = new RecentSearchRepository();
        Assert.Equal("carried over", Assert.Single(await repo.GetAllAsync()).Name);

        await repo.RecordAsync(new RecentSearch { Name = "added after", Query = "x" });
        Assert.Equal("added after", (await repo.GetAllAsync()).First().Name);
    }

    /// <summary>
    /// The column names have to match whichever way a database arrived at
    /// the current version, or queries written against one shape break on
    /// the other.
    /// </summary>
    [Fact]
    public async Task MigratedAndFreshDatabasesHaveTheSameColumns()
    {
        string[] fresh;
        using (var db = await TempDatabase.CreateAsync())
        {
            fresh = await db.ColumnNamesAsync("RecentSearches");
        }

        using var migrated = await TempDatabase.CreateLegacyAsync();
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(fresh, await migrated.ColumnNamesAsync("RecentSearches"));
    }

    /// <summary>
    /// The upgrade path for a library that already exists. Nothing is
    /// backfilled - there was nowhere to record a search before - but the
    /// table must arrive and the annotations must be untouched.
    /// </summary>
    [Fact]
    public async Task LegacyDatabaseGainsTheTableWithoutLosingAnnotations()
    {
        using var db = await TempDatabase.CreateLegacyAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));
        var nodeId = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync($"INSERT INTO Bookmarks (TextNodeId, Note) VALUES ({nodeId}, 'keep me');");

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(6, await db.ScalarAsync<int>("PRAGMA user_version;"));
        Assert.True(await db.TableExistsAsync("RecentSearches"));
        Assert.Equal("keep me", Assert.Single(await new BookmarkRepository().GetAllAsync()).Note);
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }
}
