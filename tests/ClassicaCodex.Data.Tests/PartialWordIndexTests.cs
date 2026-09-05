using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What a half-built word index does to a search, and why nothing may create
/// one by accident.
///
/// Every search path decides between the index and a text scan by asking
/// whether the index has any rows at all. That is the right question when the
/// only two states are "built" and "never built" - but a single edition's
/// worth of rows makes the answer yes, and then whole-word search, which is
/// the default, answers a two-million-line library out of whatever fraction
/// happens to be indexed. Silently: no error, and a plausible-looking short
/// list rather than an empty one.
///
/// The one thing in the application that could put the library into that state
/// was Create Translation, which reindexes the edition it just wrote after
/// every batch. On a library whose index had never been built, one AI
/// translation was enough. It now reindexes only when there is already an
/// index to keep current, and these tests are why.
/// </summary>
[Collection("Database")]
public class PartialWordIndexTests
{
    private static async Task<TempDatabase> SeedAsync()
    {
        var db = await TempDatabase.CreateAsync();

        var one = await db.SeedFullEditionAsync("a", "Abbo", "latinLit", "Sermones", "Original", "lat");
        await db.InsertLinesAsync(one, ("1.1", "virtus una"), ("1.2", "virtus bina"));

        var two = await db.SeedFullEditionAsync("z", "Zosimus", "latinLit", "Historia", "Original", "lat");
        await db.InsertLinesAsync(two, ("1.1", "virtus tertia"), ("1.2", "virtus quarta"));

        return db;
    }

    /// <summary>Indexes one edition, the way a per-edition reindex would.</summary>
    private static async Task IndexOneEditionAsync(TempDatabase db, string key)
    {
        var editionId = await db.ScalarAsync<int>($"SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:{key}';");

        for (var line = 1; line <= 2; line++)
        {
            var id = await db.TextNodeIdAsync(editionId, $"1.{line}");
            await db.ExecuteAsync(
                $"INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) VALUES ('virtus', {id});");
        }
    }

    private static SearchFilters WholeWord(string q) =>
        new() { Query = q, MatchMode = SearchMatchMode.WholeWord };

    [Fact]
    public async Task WithNoIndexAtAllTheSearchScansTheTextAndFindsEverything()
    {
        using var db = await SeedAsync();

        var hits = await new TextNodeRepository().SearchFilteredAsync(WholeWord("virtus"));

        Assert.Equal(4, hits.Count);
    }

    /// <summary>
    /// The hazard, characterised. One edition indexed is enough to make every
    /// search consult the index, and the other edition then does not exist as
    /// far as the reader can tell.
    /// </summary>
    [Fact]
    public async Task OneEditionIndexedMakesTheSearchAnswerFromThatEditionAlone()
    {
        using var db = await SeedAsync();
        await IndexOneEditionAsync(db, "a");

        var hits = await new TextNodeRepository().SearchFilteredAsync(WholeWord("virtus"));

        Assert.Equal(2, hits.Count);
        Assert.All(hits.Rows, r => Assert.Equal("Abbo", r.AuthorName));
    }

    /// <summary>
    /// And it is one row that flips it, not a threshold - which is what makes
    /// "only reindex when an index already exists" the fix rather than any
    /// amount of counting.
    /// </summary>
    [Fact]
    public async Task ASingleRowIsEnoughToCountAsAnIndex()
    {
        using var db = await SeedAsync();
        var editionId = await db.ScalarAsync<int>("SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:a';");
        var id = await db.TextNodeIdAsync(editionId, "1.1");
        await db.ExecuteAsync(
            $"INSERT INTO WordIndex (NormalizedWord, TextNodeId) VALUES ('virtus', {id});");

        Assert.True(await new WordIndexRepository().HasDataAsync());
        Assert.Equal(1, (await new TextNodeRepository().SearchFilteredAsync(WholeWord("virtus"))).Count);
    }

    // ---- the count, on the path where it cannot be exact -------------------

    /// <summary>
    /// Without an index the aggregate is a LIKE prefilter with no per-row
    /// confirmation, so it counts lines the search itself rejects. It says so,
    /// and the search window uses the returned rows for its document list
    /// whenever it does - see RenderResults.
    /// </summary>
    [Fact]
    public async Task WithoutAnIndexTheAggregateOvercountsAndAdmitsIt()
    {
        using var db = await TempDatabase.CreateAsync();

        var real = await db.SeedFullEditionAsync("r", "Abbo", "latinLit", "Real", "Original", "lat");
        await db.InsertLinesAsync(real, ("1.1", "arma virumque"));

        // "arm" is in "harm" as a substring and is not a whole word in it.
        var false_ = await db.SeedFullEditionAsync("f", "Zosimus", "latinLit", "False", "Original", "lat");
        await db.InsertLinesAsync(false_, ("1.1", "harm and charm"));

        var repo = new TextNodeRepository();
        var hits = await repo.SearchFilteredAsync(WholeWord("arma"));
        var distribution = await repo.CountMatchesByWorkAsync(WholeWord("arma"));

        Assert.Single(hits.Rows);
        Assert.Equal("Abbo", hits.Rows[0].AuthorName);

        // The aggregate cannot run the confirmation, so it says it is not exact
        // rather than quietly reporting a larger number as though it were.
        Assert.False(distribution.ExactlyMatchesTheSearch);
    }

    [Fact]
    public async Task WithAnIndexOverEverythingTheCountAndTheRowsAgree()
    {
        using var db = await SeedAsync();
        await IndexOneEditionAsync(db, "a");
        await IndexOneEditionAsync(db, "z");

        var repo = new TextNodeRepository();
        var hits = await repo.SearchFilteredAsync(WholeWord("virtus"));
        var distribution = await repo.CountMatchesByWorkAsync(WholeWord("virtus"));

        Assert.True(distribution.ExactlyMatchesTheSearch);
        Assert.Equal(hits.Count, distribution.TotalMatches);
        Assert.Equal(2, distribution.WorkCount);
    }
}
