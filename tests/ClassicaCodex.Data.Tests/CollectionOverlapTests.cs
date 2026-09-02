using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What happens where two collections carry the same work.
///
/// That overlap is the point rather than a problem - 147 works in a full
/// library hold editions from more than one collection, and a reader picking
/// between Perseus's Ajax and First1KGreek's is the feature. But two of those
/// collections can also ship a file under the SAME CTS identifier and mean
/// different books by it, and that is not the same thing at all.
///
/// CSEL and the Patrologia Latina do it three times: Paulinus of Nola's
/// Carmina, Pseudo-Cyprian's Ad Vigilium, Boethius' second commentary on
/// Porphyry. CSEL has the critical edition; Migne has his nineteenth-century
/// reprint. Ingested in the wizard's order, Migne came second and the upsert
/// overwrote - so the critical text of the Carmina, 9,380 lines of it, was
/// replaced by the reprint, silently, leaving CSEL's work row behind with
/// nothing under it. The reader saw two rows for one work, one of them empty,
/// and the better text was simply gone.
/// </summary>
[Collection("Database")]
public class CollectionOverlapTests
{
    private static Edition Edition(int workId, string urn, string? collection, string source) => new()
    {
        WorkId = workId,
        CtsUrn = urn,
        Kind = EditionKind.Original,
        Language = "lat",
        SourcePath = source,
        Collection = collection
    };

    private static async Task<int> WorkAsync(string urn, string title)
    {
        var authorId = await new AuthorRepository().UpsertAsync(new Author
        {
            CtsUrn = "urn:cts:latinLit:stoa0223", Name = "Paulinus of Nola", Namespace = "latinLit"
        });

        return await new WorkRepository().UpsertAsync(new Work
        {
            AuthorId = authorId, CtsUrn = urn, Title = title
        });
    }

    /// <summary>
    /// The edition that got there first keeps the identifier, and the second
    /// is refused rather than allowed to overwrite it.
    /// </summary>
    [Fact]
    public async Task AnEditionHeldByAnotherCollectionIsNotOverwritten()
    {
        using var db = await TempDatabase.CreateAsync();
        var workId = await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "Carmina");
        var editions = new EditionRepository();

        var first = await editions.UpsertAsync(
            Edition(workId, "stoa0223.stoa001.opp-lat1", "csel", @"C:\corpus\csel\carmina.xml"));
        Assert.True(first > 0);

        var second = await editions.UpsertAsync(
            Edition(workId, "stoa0223.stoa001.opp-lat1", "patrologia-latina", @"C:\corpus\pl\carmina.xml"));

        // Zero is the refusal, which the ingest turns into a reported skip.
        Assert.Equal(0, second);

        var kept = Assert.Single(await editions.GetByWorkAsync(workId));
        Assert.Equal("csel", kept.Collection);
        Assert.EndsWith(@"csel\carmina.xml", kept.SourcePath);
    }

    /// <summary>
    /// Re-ingesting the SAME collection still updates in place, which is what
    /// makes a corpus refresh work at all.
    /// </summary>
    [Fact]
    public async Task ReingestingTheSameCollectionStillUpdates()
    {
        using var db = await TempDatabase.CreateAsync();
        var workId = await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "Carmina");
        var editions = new EditionRepository();

        var first = await editions.UpsertAsync(
            Edition(workId, "stoa0223.stoa001.opp-lat1", "csel", @"C:\old\carmina.xml"));
        var again = await editions.UpsertAsync(
            Edition(workId, "stoa0223.stoa001.opp-lat1", "csel", @"C:\new\carmina.xml"));

        Assert.Equal(first, again);
        Assert.EndsWith(@"new\carmina.xml", Assert.Single(await editions.GetByWorkAsync(workId)).SourcePath);
    }

    /// <summary>
    /// An edition with no collection - the manual Ingest Corpus dialog writes
    /// these - keeps the old unconditional behaviour, so nothing that worked
    /// before stops working.
    /// </summary>
    [Fact]
    public async Task AnUnstampedEditionIsStillOverwritten()
    {
        using var db = await TempDatabase.CreateAsync();
        var workId = await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "Carmina");
        var editions = new EditionRepository();

        var first = await editions.UpsertAsync(
            Edition(workId, "stoa0223.stoa001.opp-lat1", null, @"C:\old\carmina.xml"));
        var again = await editions.UpsertAsync(
            Edition(workId, "stoa0223.stoa001.opp-lat1", "csel", @"C:\new\carmina.xml"));

        Assert.Equal(first, again);
    }

    // ------------------------------------------------------------ the title

    /// <summary>
    /// The first collection to carry a work names it. Migne calls Paulinus'
    /// Carmina "Carmen Adversos Gentes"; CSEL, fetched first, calls it
    /// Carmina, and adding Migne's editions afterwards must not rename it.
    /// </summary>
    [Fact]
    public async Task TheFirstCatalogueToNameAWorkKeepsTheName()
    {
        using var db = await TempDatabase.CreateAsync();

        var id = await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "Carmina");
        var again = await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "Carmen Adversos Gentes");

        Assert.Equal(id, again);
        Assert.Equal("Carmina", (await TitleOfAsync(id)));
    }

    /// <summary>
    /// A work that arrived with no name at all takes the first real one it is
    /// offered - the hold is on overwriting a title, not on acquiring one.
    /// </summary>
    [Fact]
    public async Task AWorkWithNoNameTakesTheFirstRealOne()
    {
        using var db = await TempDatabase.CreateAsync();

        var id = await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "   ");
        await WorkAsync("urn:cts:latinLit:stoa0223.stoa001", "Carmina");

        Assert.Equal("Carmina", (await TitleOfAsync(id)));
    }

    /// <summary>The stored title of one work, by id.</summary>
    private static async Task<string> TitleOfAsync(int workId)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Title FROM Works WHERE WorkId = @Id;";
        cmd.Parameters.AddWithValue("@Id", workId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
}
