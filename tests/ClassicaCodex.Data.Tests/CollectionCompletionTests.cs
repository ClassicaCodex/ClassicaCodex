using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Whether a setup step FINISHED, which is not the question the wizard was
/// asking.
///
/// It asked whether the collection had any editions at all, and one edition
/// out of thousands satisfies that. So an ingest cancelled twenty minutes
/// into ninety - a closed laptop lid, a dropped connection, a reader who
/// needed the machine back - came up the next day reporting "Already
/// loaded.", with a green tick beside it. The reader then had a fraction of a
/// corpus and nothing anywhere to say so, until they searched for a passage
/// they knew was in Perseus and did not find it. That is the accuracy failure
/// that sinks tools like this one, arriving through the setup wizard rather
/// than through a mis-tagged word.
///
/// A completion row is written at the end of an ingest that returned, and
/// nowhere else, so the two states are distinguishable.
/// </summary>
[Collection("Database")]
public class CollectionCompletionTests
{
    private const string Folder = @"C:\data\greek-texts";
    private const string Key = "perseus-greek";

    private static Edition Edition(int workId, string urn, string sourcePath) => new()
    {
        WorkId = workId,
        CtsUrn = urn,
        Kind = EditionKind.Original,
        Language = "grc",
        SourcePath = sourcePath
    };

    /// <summary>What an interrupted run leaves: rows in the library, no completion.</summary>
    private static async Task<int> SeedPartialIngestAsync()
    {
        var authorId = await new AuthorRepository().UpsertAsync(new Author
        {
            CtsUrn = "urn:cts:greekLit:tlg0012", Name = "Homer", Namespace = "greekLit"
        });

        var workId = await new WorkRepository().UpsertAsync(new Work
        {
            AuthorId = authorId, CtsUrn = "urn:cts:greekLit:tlg0012.tlg001", Title = "Iliad"
        });

        await new EditionRepository().UpsertAsync(Edition(workId, "urn:e1", Folder + @"\tlg0012.tlg001.xml"));
        return workId;
    }

    /// <summary>
    /// The case the wizard got wrong. Editions are present, so the old
    /// row-count question answered yes; the step never finished, so this one
    /// answers no.
    /// </summary>
    [Fact]
    public async Task AnInterruptedIngestIsNotComplete()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedPartialIngestAsync();

        var editions = new EditionRepository();

        // The edition is in the library - which is what the old question
        // asked, and why it answered "Already loaded." Both halves are
        // asserted, because the whole point is that they now differ.
        await db.ExecuteAsync($"UPDATE Editions SET Collection = '{Key}';");

        Assert.True(await editions.CountByCollectionAsync(Key) > 0);
        Assert.False(await editions.IsCollectionCompleteAsync(Key));
    }

    /// <summary>
    /// And the case it got right, which must keep working: stamping is what
    /// the end of a successful ingest does.
    /// </summary>
    [Fact]
    public async Task AFinishedIngestIsComplete()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedPartialIngestAsync();

        var editions = new EditionRepository();
        await editions.StampCollectionAsync(Folder, Key);

        Assert.True(await editions.IsCollectionCompleteAsync(Key));
        Assert.True(await editions.CountByCollectionAsync(Key) > 0);
    }

    /// <summary>
    /// Re-running a finished step must not break it - the wizard offers
    /// "Re-download &amp; Re-install" on a completed step, and ingestion is
    /// idempotent, so the completion row has to be too.
    /// </summary>
    [Fact]
    public async Task StampingTwiceLeavesItComplete()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedPartialIngestAsync();

        var editions = new EditionRepository();
        await editions.StampCollectionAsync(Folder, Key);
        await editions.StampCollectionAsync(Folder, Key);

        Assert.True(await editions.IsCollectionCompleteAsync(Key));
    }

    /// <summary>
    /// One step finishing says nothing about another. This is the shape of the
    /// bug that made the wizard skip a corpus that had never been fetched.
    /// </summary>
    [Fact]
    public async Task OneCollectionFinishingDoesNotCompleteAnother()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedPartialIngestAsync();

        var editions = new EditionRepository();
        await editions.StampCollectionAsync(Folder, Key);

        Assert.False(await editions.IsCollectionCompleteAsync("perseus-latin"));
    }

    /// <summary>
    /// An existing library - one someone has been reading for months - must
    /// not be reported as unfinished the first time this version opens it.
    /// Migration 39 back-fills a completion row for every collection already
    /// carrying editions, because telling that reader to re-run a ninety
    /// minute download would be a worse lie than the one being fixed.
    /// </summary>
    [Fact]
    public async Task AnExistingLibraryIsTreatedAsFinished()
    {
        using var db = await TempDatabase.CreateAsync();

        // A library as it stood before completion rows existed: editions
        // carrying a collection, and nothing in CollectionCompletions.
        await SeedPartialIngestAsync();
        await db.ExecuteAsync($"UPDATE Editions SET Collection = '{Key}'; DELETE FROM CollectionCompletions;");

        var editions = new EditionRepository();

        Assert.False(await editions.IsCollectionCompleteAsync(Key));

        // What the migration does.
        await db.ExecuteAsync(@"
            INSERT OR IGNORE INTO CollectionCompletions (Collection, CompletedUtc)
                SELECT DISTINCT Collection, strftime('%Y-%m-%dT%H:%M:%SZ', 'now')
                FROM Editions
                WHERE Collection IS NOT NULL AND TRIM(Collection) <> '';");

        Assert.True(await editions.IsCollectionCompleteAsync(Key));
    }
}
