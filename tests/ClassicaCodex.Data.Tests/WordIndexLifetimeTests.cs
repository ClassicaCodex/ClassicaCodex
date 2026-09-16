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
    /// <summary>
    /// Indexes an edition the way the application does: each line contributes
    /// the words that are in it.
    ///
    /// <b>This used to insert the word 'arma' against every line of the
    /// edition</b>, including "Troiae qui primus", which does not contain it.
    /// That was harmless while the cleanup deleted by line id - it removed
    /// whatever was there regardless - and it stopped being harmless when the
    /// cleanup started naming the rows it deletes, because a row the text
    /// cannot produce is a row the cleanup cannot name.
    ///
    /// The seed was changed rather than the cleanup, because the seed was the
    /// thing that did not describe the application: WordIndexService tokenizes
    /// each line and inserts that line's own words. What these tests are for -
    /// that clearing and deleting an edition take its index entries with them,
    /// that a re-ingest leaves nothing stranded, and that one edition's
    /// cleanup does not touch another's - is unchanged and still checked.
    ///
    /// The case this no longer covers is recorded on its own, deliberately, in
    /// WordIndexCleanupTests.ARowTheTextCannotProduceIsLeftBehind.
    /// </summary>
    private static async Task IndexAsync(TempDatabase db, int editionId)
    {
        var lines = await new TextNodeRepository().GetByEditionAsync(editionId);

        foreach (var line in lines)
        {
            foreach (var word in ClassicaCodex.Core.WordNormalizer.TokenizeLine(line.Text))
            {
                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) "
                    + $"VALUES ('{word.Replace("'", "''")}', {line.TextNodeId});");
            }
        }
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
        Assert.Equal(6, await IndexCountAsync(db));

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

        Assert.Equal(6, await IndexCountAsync(db));
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
        Assert.Equal(6, await IndexCountAsync(db));

        await new EditionRepository().ClearTextNodesAsync(drop);

        Assert.Equal(3, await IndexCountAsync(db));
        Assert.Equal(0, await StrandedCountAsync(db));
    }
}
