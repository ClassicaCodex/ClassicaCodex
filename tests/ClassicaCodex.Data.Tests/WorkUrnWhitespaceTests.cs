using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A trailing space in a work's URN does not make an untidy work. It makes a
/// second one.
///
/// The URN is the row's identity - everything upserts on it - so
/// "urn:...stoa001 " and "urn:...stoa001" are two different works holding the
/// same text. Four Patrologia Latina catalogue files carry that space inside
/// the urn attribute, upstream, and no other file in any collection does.
/// Three of the four collided with CSEL's entry for the same text, which is
/// how the library came to list an empty "Carmina" under Paulinus of Nola and
/// an empty "Ad Vigilium" under Pseudo-Cyprian, each beside a full copy of
/// itself under a slightly different name.
///
/// The catalogue readers trim, so imports are clean. The trouble is a library
/// that already has the duplicates: the bad row keeps its edition, so nothing
/// revisits it and it stays. Hence both a guard at the upsert and a repair for
/// files that already exist.
/// </summary>
[Collection("Database")]
public class WorkUrnWhitespaceTests
{
    private const string Urn = "urn:cts:latinLit:stoa0223.stoa001";

    private static async Task<int> AuthorAsync() =>
        await new AuthorRepository().UpsertAsync(new Author
        {
            CtsUrn = "urn:cts:latinLit:stoa0223", Name = "Paulinus of Nola", Namespace = "latinLit"
        });

    // ------------------------------------------------ the guard at the write

    [Fact]
    public async Task AUrnWithATrailingSpaceIsStoredTrimmed()
    {
        using var db = await TempDatabase.CreateAsync();
        var authorId = await AuthorAsync();

        await new WorkRepository().UpsertAsync(new Work
        { AuthorId = authorId, CtsUrn = Urn + " ", Title = "Carmen Adversos Gentes" });

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM Works WHERE CtsUrn <> TRIM(CtsUrn);"));
    }

    /// <summary>
    /// The point of trimming: the spaced and unspaced forms are the same work,
    /// so the second import updates the first rather than adding a twin.
    /// </summary>
    [Fact]
    public async Task TheSpacedAndUnspacedFormsAreOneWork()
    {
        using var db = await TempDatabase.CreateAsync();
        var authorId = await AuthorAsync();
        var works = new WorkRepository();

        var first = await works.UpsertAsync(new Work { AuthorId = authorId, CtsUrn = Urn, Title = "Carmina" });
        var second = await works.UpsertAsync(new Work { AuthorId = authorId, CtsUrn = Urn + " ", Title = "Carmina" });

        Assert.Equal(first, second);
        Assert.Equal(1, await db.CountAsync("Works"));
    }

    [Fact]
    public async Task EditionAndAuthorUrnsAreTrimmedToo()
    {
        using var db = await TempDatabase.CreateAsync();
        var authorId = await new AuthorRepository().UpsertAsync(new Author
        { CtsUrn = "urn:cts:latinLit:stoa0223 ", Name = "Paulinus of Nola", Namespace = "latinLit" });
        var workId = await new WorkRepository().UpsertAsync(new Work
        { AuthorId = authorId, CtsUrn = Urn, Title = "Carmina" });
        await new EditionRepository().UpsertAsync(new Edition
        { WorkId = workId, CtsUrn = Urn + ".opp-lat1 ", Kind = EditionKind.Original, Language = "lat" });

        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM Authors  WHERE CtsUrn <> TRIM(CtsUrn);"));
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM Editions WHERE CtsUrn <> TRIM(CtsUrn);"));
    }

    // ------------------------------------------------ the repair for old files

    /// <summary>
    /// Plants the split the way a real library holds it - the spaced row with
    /// the edition, the unspaced one empty beside it - then upgrades. Inserted
    /// directly, because the repository now refuses to create it.
    /// </summary>
    private static async Task PlantSplitAsync(TempDatabase db, bool withTwin)
    {
        await db.ExecuteAsync(@"
            INSERT INTO Authors (CtsUrn, Name, Namespace) VALUES ('urn:cts:latinLit:stoa0223', 'Paulinus of Nola', 'latinLit');");

        if (withTwin)
        {
            await db.ExecuteAsync($@"
                INSERT INTO Works (AuthorId, CtsUrn, Title)
                VALUES ((SELECT AuthorId FROM Authors), '{Urn}', 'Carmina');");
        }

        await db.ExecuteAsync($@"
            INSERT INTO Works (AuthorId, CtsUrn, Title)
            VALUES ((SELECT AuthorId FROM Authors), '{Urn} ', 'Carmen Adversos Gentes');");

        await db.ExecuteAsync($@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language)
            VALUES ((SELECT WorkId FROM Works WHERE CtsUrn = '{Urn} '), '{Urn}.opp-lat1', 'Original', 'lat');");

        await db.RewindSchemaAsync(36);
    }

    [Fact]
    public async Task UpgradingMergesTheDuplicateOntoTheGoodRow()
    {
        using var db = await TempDatabase.CreateAsync();
        await PlantSplitAsync(db, withTwin: true);
        Assert.Equal(2, await db.CountAsync("Works"));

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.CountAsync("Works"));
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM Works WHERE CtsUrn <> TRIM(CtsUrn);"));

        // The edition follows the text, onto the row that survives.
        Assert.Equal("Carmina", await db.ScalarStringAsync("SELECT Title FROM Works;"));
        Assert.Equal(1, await db.ScalarAsync<long>(
            @"SELECT COUNT(*) FROM Editions e JOIN Works w ON w.WorkId = e.WorkId WHERE w.CtsUrn = '" + Urn + "';"));
    }

    /// <summary>
    /// The fourth of the four had no twin - nothing to merge onto, so it is
    /// trimmed where it stands and keeps its edition.
    /// </summary>
    [Fact]
    public async Task UpgradingTrimsALoneDuplicateInPlace()
    {
        using var db = await TempDatabase.CreateAsync();
        await PlantSplitAsync(db, withTwin: false);

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.CountAsync("Works"));
        Assert.Equal(0, await db.ScalarAsync<long>("SELECT COUNT(*) FROM Works WHERE CtsUrn <> TRIM(CtsUrn);"));
        Assert.Equal(1, await db.ScalarAsync<long>(
            @"SELECT COUNT(*) FROM Editions e JOIN Works w ON w.WorkId = e.WorkId WHERE w.CtsUrn = '" + Urn + "';"));
    }

    /// <summary>
    /// No edition may be lost or left pointing at a work that is gone - which
    /// is the way a repair like this fails.
    /// </summary>
    [Fact]
    public async Task NoEditionIsStrandedByTheRepair()
    {
        using var db = await TempDatabase.CreateAsync();
        await PlantSplitAsync(db, withTwin: true);

        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.CountAsync("Editions"));
        Assert.Equal(0, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM Editions e LEFT JOIN Works w ON w.WorkId = e.WorkId WHERE w.WorkId IS NULL;"));
    }
}
