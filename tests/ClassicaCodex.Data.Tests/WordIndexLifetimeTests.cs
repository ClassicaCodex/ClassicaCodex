using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The word index has to be cleaned up through the passages it points at,
/// which means it has to be cleaned up before they are deleted.
///
/// WordIndex is a WITHOUT ROWID table of (word, passage id) pairs with no
/// foreign key - nothing enforces that the id still exists, and there is no
/// column saying which edition an entry belongs to. The only way to find an
/// edition's entries is to look its passages up. Delete the passages first and
/// its entries are unreachable for good.
///
/// Which is what happened: ClearTextNodesAsync deleted the passages, and
/// WordIndexRepository.DeleteByEditionAsync - written for exactly this cleanup
/// and resolving ids exactly that way - then matched nothing. 1,983,234
/// stranded entries in the full library, 2.8% of the index, accumulated from
/// re-ingests and from the translation workbench, which clears and re-inserts
/// on every save.
///
/// No wrong answers came of it: a search asks the index for passage ids and
/// joins them back, so ids that no longer exist drop out, and 6,684 hits
/// across fourteen Latin and Greek searches confirmed none of them lacked the
/// word. But SQLite hands out row ids as max+1 of what is currently present,
/// so a stranded id is one deletion pattern away from belonging to some other
/// passage - and then the index would claim a word for a line that never had
/// it.
/// </summary>
[Collection("Database")]
public class WordIndexLifetimeTests
{
    private static async Task IndexAsync(TempDatabase db, int editionId)
    {
        await db.ExecuteAsync($@"
            INSERT INTO WordIndex (NormalizedWord, TextNodeId)
            SELECT 'arma', TextNodeId FROM TextNodes WHERE EditionId = {editionId};");
    }

    private static Task<long> IndexCountAsync(TempDatabase db) =>
        db.ScalarAsync<long>("SELECT COUNT(*) FROM WordIndex;");

    private static Task<long> StrandedCountAsync(TempDatabase db) =>
        db.ScalarAsync<long>(
            @"SELECT COUNT(*) FROM WordIndex wi
              WHERE NOT EXISTS (SELECT 1 FROM TextNodes tn WHERE tn.TextNodeId = wi.TextNodeId);");

    [Fact]
    public async Task ClearingAnEditionsPassagesAlsoClearsItsIndexEntries()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "arma uirumque cano"), ("1.2", "Troiae qui primus"));
        await IndexAsync(db, editionId);
        Assert.Equal(2, await IndexCountAsync(db));

        await new EditionRepository().ClearTextNodesAsync(editionId);

        Assert.Equal(0, await IndexCountAsync(db));
        Assert.Equal(0, await StrandedCountAsync(db));
    }

    /// <summary>
    /// The re-ingest shape: clear, re-insert, and no residue from the round
    /// before. The passages come back with fresh ids, so entries pointing at
    /// the old ones would be stranded rather than merely duplicated.
    /// </summary>
    [Fact]
    public async Task ReingestingAnEditionLeavesNothingStranded()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();

        for (var round = 0; round < 3; round++)
        {
            await new EditionRepository().ClearTextNodesAsync(editionId);
            await db.InsertLinesAsync(editionId, ("1.1", "arma uirumque cano"), ("1.2", "Troiae qui primus"));
            await IndexAsync(db, editionId);
        }

        Assert.Equal(2, await IndexCountAsync(db));
        Assert.Equal(0, await StrandedCountAsync(db));
    }

    /// <summary>
    /// Removing an edition from the library took its passages and left its
    /// whole word index behind.
    /// </summary>
    [Fact]
    public async Task DeletingAnEditionAlsoDeletesItsIndexEntries()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "arma uirumque cano"));
        await IndexAsync(db, editionId);

        await new EditionRepository().DeleteEditionAsync(editionId);

        Assert.Equal(0, await IndexCountAsync(db));
    }

    /// <summary>
    /// One edition's cleanup does not touch another's.
    /// </summary>
    [Fact]
    public async Task ClearingOneEditionLeavesTheOthersIndexAlone()
    {
        using var db = await TempDatabase.CreateAsync();
        var keep = await db.SeedEditionAsync("keep");
        var drop = await db.SeedEditionAsync("drop");
        await db.InsertLinesAsync(keep, ("1.1", "arma uirumque cano"));
        await db.InsertLinesAsync(drop, ("1.1", "arma uirumque cano"));
        await IndexAsync(db, keep);
        await IndexAsync(db, drop);
        Assert.Equal(2, await IndexCountAsync(db));

        await new EditionRepository().ClearTextNodesAsync(drop);

        Assert.Equal(1, await IndexCountAsync(db));
        Assert.Equal(0, await StrandedCountAsync(db));
    }
}
