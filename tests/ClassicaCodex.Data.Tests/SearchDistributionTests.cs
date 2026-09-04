using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// How many lines match, and which works they are in.
///
/// This exists because the search window used to answer both questions from
/// the rows it had room for. A filtered search stops at MaxResults having
/// ordered by author name, so the rows it stops with are not a sample of the
/// matches - they are the front of the alphabet. Grouping them produced a
/// table that looked exactly like a distribution and was not one.
///
/// Measured on a full library before the change, searching Latin "vel":
/// 44,457 lines across 1,623 works, of which the capped rows saw 5,000 lines
/// across 168 works. 1,455 works - 90% of those containing the word - showed
/// nothing. The last author reached was Augustine, who also has more matches
/// than anyone (1,056), and the grouped view credited him with 184 because
/// the cap landed in the middle of him. For Greek λόγος the real top of the
/// distribution is the Homeric scholia; the grouped view led with Aesop,
/// who was there because of the A.
///
/// So: every assertion here is about the answer being about the library
/// rather than about the page.
/// </summary>
[Collection("Database")]
public class SearchDistributionTests
{
    /// <summary>
    /// Authors deliberately named so that alphabetical order and match order
    /// disagree - Zosimus has the most matches and sorts last, which is the
    /// shape that made the old grouping wrong.
    /// </summary>
    private static async Task<TempDatabase> SeedAsync()
    {
        var db = await TempDatabase.CreateAsync();

        // Abbo has enough matches to use up a small cap on his own, so that a
        // capped search cannot reach Zosimus at all - which is the shape the
        // real bug had, where "vel" ran out of room inside Augustine.
        var abbo = await db.SeedFullEditionAsync("abbo", "Abbo", "latinLit", "Sermones", "Original", "lat");
        await AddLinesAsync(db, abbo,
            ("1.1", "virtus una"), ("1.2", "virtus bina"), ("1.3", "virtus terna"),
            ("1.4", "nihil hic"));

        var zosimus = await db.SeedFullEditionAsync("zos", "Zosimus", "latinLit", "Historia", "Original", "lat");
        await AddLinesAsync(db, zosimus,
            ("1.1", "virtus prima"), ("1.2", "virtus altera"),
            ("1.3", "virtus tertia"), ("1.4", "virtus quarta"));

        var greek = await db.SeedFullEditionAsync("plut", "Plutarch", "greekLit", "Moralia", "Original", "grc");
        await AddLinesAsync(db, greek, ("1.1", "ἀρετή τις"));

        return db;
    }

    /// <summary>
    /// Inserts lines and indexes them the way the real word-index build does -
    /// one normalized word per line - so the whole-word path these tests are
    /// about has an index to use.
    /// </summary>
    private static async Task AddLinesAsync(
        TempDatabase db, int editionId, params (string CitationRef, string Text)[] lines)
    {
        await db.InsertLinesAsync(editionId, lines);

        var rows = new List<string>();
        foreach (var (citationRef, text) in lines)
        {
            var id = await db.TextNodeIdAsync(editionId, citationRef);
            foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                         .Select(WordNormalizer.Normalize)
                         .Where(w => w.Length > 0)
                         .Distinct(StringComparer.Ordinal))
            {
                rows.Add($"('{word}', {id})");
            }
        }

        if (rows.Count > 0)
        {
            await db.ExecuteAsync(
                $"INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) VALUES {string.Join(",", rows)};");
        }
    }

    private static SearchFilters Query(string q, int maxResults = 5000) =>
        new() { Query = q, MatchMode = SearchMatchMode.WholeWord, MaxResults = maxResults };

    // ---- the count is of the library, not of the page --------------------

    [Fact]
    public async Task TheTotalCountsEveryMatchNotJustTheReturnedPage()
    {
        using var db = await SeedAsync();
        var repo = new TextNodeRepository();

        var hits = await repo.SearchFilteredAsync(Query("virtus", maxResults: 2));
        var distribution = await repo.CountMatchesByWorkAsync(Query("virtus", maxResults: 2));

        Assert.True(hits.Truncated);
        Assert.Equal(2, hits.Count);
        Assert.Equal(7, distribution.TotalMatches);
    }

    /// <summary>
    /// The case the whole thing is for. Capped at two rows, the search can
    /// only reach Abbo; Zosimus has four of the seven matches and must still
    /// appear, at the top.
    /// </summary>
    [Fact]
    public async Task AWorkPastTheCapStillAppearsAndInTheRightPlace()
    {
        using var db = await SeedAsync();
        var repo = new TextNodeRepository();

        var hits = await repo.SearchFilteredAsync(Query("virtus", maxResults: 2));
        var distribution = await repo.CountMatchesByWorkAsync(Query("virtus", maxResults: 2));

        Assert.DoesNotContain(hits.Rows, r => r.AuthorName == "Zosimus");

        Assert.Equal(2, distribution.WorkCount);
        Assert.Equal("Zosimus", distribution.Works[0].AuthorName);
        Assert.Equal(4, distribution.Works[0].Matches);
        Assert.Equal("Abbo", distribution.Works[1].AuthorName);
        Assert.Equal(3, distribution.Works[1].Matches);
    }

    [Fact]
    public async Task TheCountAgreesWithTheRowsWhenNothingWasCapped()
    {
        using var db = await SeedAsync();
        var repo = new TextNodeRepository();

        var hits = await repo.SearchFilteredAsync(Query("virtus"));
        var distribution = await repo.CountMatchesByWorkAsync(Query("virtus"));

        Assert.False(hits.Truncated);
        Assert.Equal(hits.Count, distribution.TotalMatches);
    }

    [Fact]
    public async Task WorksAreOrderedByHowManyMatchesTheyHave()
    {
        using var db = await SeedAsync();

        var distribution = await new TextNodeRepository().CountMatchesByWorkAsync(Query("virtus"));

        Assert.Equal(
            distribution.Works.Select(w => w.Matches).OrderByDescending(m => m),
            distribution.Works.Select(w => w.Matches));
    }

    [Fact]
    public async Task AuthorsAreCountedAsWellAsWorks()
    {
        using var db = await SeedAsync();

        var distribution = await new TextNodeRepository().CountMatchesByWorkAsync(Query("virtus"));

        Assert.Equal(2, distribution.WorkCount);
        Assert.Equal(2, distribution.AuthorCount);
    }

    // ---- the filters have to reach it ------------------------------------

    [Fact]
    public async Task ALanguageFilterNarrowsTheCountToo()
    {
        using var db = await SeedAsync();
        var repo = new TextNodeRepository();

        var greekOnly = Query("ἀρετή");
        greekOnly.Languages.Add("grc");
        Assert.Equal(1, (await repo.CountMatchesByWorkAsync(greekOnly)).TotalMatches);

        var latinOnly = Query("ἀρετή");
        latinOnly.Languages.Add("lat");
        Assert.Equal(0, (await repo.CountMatchesByWorkAsync(latinOnly)).TotalMatches);
    }

    [Fact]
    public async Task AWorkFilterNarrowsTheCountToo()
    {
        using var db = await SeedAsync();
        var repo = new TextNodeRepository();

        var scoped = Query("virtus");
        scoped.WorkId = await db.WorkIdForAsync("zos");

        var distribution = await repo.CountMatchesByWorkAsync(scoped);

        Assert.Equal(4, distribution.TotalMatches);
        Assert.Single(distribution.Works);
    }

    /// <summary>
    /// An era that matched no authors is a real answer - no passages qualify -
    /// and must not read as "no era filter", the same way the row query
    /// treats it.
    /// </summary>
    [Fact]
    public async Task AnEraThatMatchedNoAuthorsCountsNothing()
    {
        using var db = await SeedAsync();

        var filters = Query("virtus");
        filters.EraAuthorIds = Array.Empty<int>();

        Assert.Equal(0, (await new TextNodeRepository().CountMatchesByWorkAsync(filters)).TotalMatches);
    }

    // ---- what it counts --------------------------------------------------

    /// <summary>
    /// A work this library holds twice - CSEL and Migne both ship Augustine -
    /// is one row rather than two with the same title, and its matching lines
    /// from both editions are counted. That is a decision rather than an
    /// accident: what this reports is matching lines in the library, which is
    /// the same quantity the row list has always shown.
    /// </summary>
    [Fact]
    public async Task TwoEditionsOfOneWorkAreOneRowCarryingBoth()
    {
        using var db = await SeedAsync();

        var second = await db.SeedSiblingEditionAsync("abbo", "abbo-migne", "Original", "lat");
        await AddLinesAsync(db, second, ("1.1", "virtus una"));

        var distribution = await new TextNodeRepository().CountMatchesByWorkAsync(Query("virtus"));

        var abbo = distribution.Works.Single(w => w.AuthorName == "Abbo");
        Assert.Equal(4, abbo.Matches);
        Assert.Equal(2, distribution.WorkCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzzqqqq")]
    public async Task NothingToCountIsAnEmptyDistribution(string query)
    {
        using var db = await SeedAsync();

        var distribution = await new TextNodeRepository().CountMatchesByWorkAsync(Query(query));

        Assert.Equal(0, distribution.TotalMatches);
        Assert.Empty(distribution.Works);
        Assert.Equal(0, distribution.AuthorCount);
    }

    /// <summary>
    /// Without a word index the whole-word search is a LIKE prefilter plus a
    /// per-row confirmation, and a confirmation cannot run inside an
    /// aggregate. The count would be of the prefilter, so it says it is not
    /// exact and the window keeps showing the row-based figure.
    /// </summary>
    [Fact]
    public async Task WithoutAWordIndexTheCountSaysItIsNotExact()
    {
        using var db = await TempDatabase.CreateAsync();
        var edition = await db.SeedFullEditionAsync("x", "Abbo", "latinLit", "Sermones", "Original", "lat");
        await db.InsertLinesAsync(edition, ("1.1", "virtus una"));

        var distribution = await new TextNodeRepository().CountMatchesByWorkAsync(Query("virtus"));

        Assert.False(distribution.ExactlyMatchesTheSearch);
    }

    [Fact]
    public async Task WithAWordIndexTheCountSaysItIsExact()
    {
        using var db = await SeedAsync();

        var distribution = await new TextNodeRepository().CountMatchesByWorkAsync(Query("virtus"));

        Assert.True(distribution.ExactlyMatchesTheSearch);
    }
}
