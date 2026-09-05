using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What the default search mode returns, once there is a word index for it
/// to use.
///
/// Two things are pinned here, and both only became visible when whole-word
/// stopped being a mode people chose and became the one they land in.
///
/// The first is that several typed words mean all of them. The clause was one
/// IN over every word at once, which is an OR - identical to an AND while
/// queries were one word long, and badly wrong the moment they were not.
/// Searching a full library for "gallia est omnis divisa" returned 5,000+
/// lines led by an argumentum to a letter of Cyprian, because nearly every
/// line in the corpus contains "est"; the reader wanted the one line that
/// opens the Gallic War, and it was in there somewhere.
///
/// The second is that one word means every way an editor might spell it.
/// See SpellingVariants for the u/v and i/j measurements.
/// </summary>
[Collection("Database")]
public class WholeWordSearchTests
{
    /// <summary>
    /// A small Latin library carrying both orthographies, plus the word
    /// index the whole-word path needs. The index is filled the way the real
    /// build does it - one normalized word per line - rather than by calling
    /// the builder, so these tests stay about searching.
    /// </summary>
    private static async Task<TempDatabase> SeedAsync()
    {
        var db = await TempDatabase.CreateAsync();

        var modern = await db.SeedFullEditionAsync("caesar", "Julius Caesar", "latinLit", "Gallic War", "Original", "lat");
        await db.InsertLinesAsync(modern,
            ("1.1", "Gallia est omnis divisa in partes tres"),
            ("1.2", "iustitia et pax osculatae sunt"),
            ("1.3", "omnis divisa sine gallia"));

        var older = await db.SeedFullEditionAsync("migne", "Augustine", "latinLit", "Sermones", "Original", "lat");
        await db.InsertLinesAsync(older,
            ("2.1", "de justitia et misericordia"),
            ("2.2", "vt vita breuis est"));

        await IndexAsync(db);
        return db;
    }

    private static async Task IndexAsync(TempDatabase db)
    {
        var rows = new List<string>();
        foreach (var (id, text) in await LinesAsync(db))
        {
            foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => WordNormalizer.Normalize(w))
                         .Where(w => w.Length > 0)
                         .Distinct(StringComparer.Ordinal))
            {
                rows.Add($"('{word}', {id})");
            }
        }

        await db.ExecuteAsync(
            $"INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) VALUES {string.Join(",", rows)};");
    }

    private static async Task<List<(long Id, string Text)>> LinesAsync(TempDatabase db)
    {
        var hits = await new TextNodeRepository().SearchFilteredAsync(
            new SearchFilters { Query = "e", MatchMode = SearchMatchMode.Contains });
        return hits.Rows.Select(r => (r.TextNodeId, r.Text)).ToList();
    }

    private static SearchFilters WholeWord(string query) =>
        new() { Query = query, MatchMode = SearchMatchMode.WholeWord };

    private static async Task<List<string>> FindAsync(string query) =>
        (await new TextNodeRepository().SearchFilteredAsync(WholeWord(query)))
        .Rows.Select(r => r.Text).ToList();

    // ---- several words mean all of them ---------------------------------

    [Fact]
    public async Task EveryTypedWordHasToBePresent()
    {
        using var db = await SeedAsync();

        var found = await FindAsync("gallia est omnis divisa");

        Assert.Equal(new[] { "Gallia est omnis divisa in partes tres" }, found);
    }

    /// <summary>
    /// The line that would have drowned it: "est" alone is in most of the
    /// library, and an OR would have returned all of it.
    /// </summary>
    [Fact]
    public async Task ACommonWordInTheQueryDoesNotDragInEveryLineContainingIt()
    {
        using var db = await SeedAsync();

        var found = await FindAsync("gallia est omnis divisa");

        Assert.DoesNotContain("vt vita breuis est", found);
        Assert.DoesNotContain("de justitia et misericordia", found);
    }

    [Fact]
    public async Task WordOrderDoesNotMatter()
    {
        using var db = await SeedAsync();

        Assert.Equal(
            (await FindAsync("gallia omnis divisa")).OrderBy(x => x),
            (await FindAsync("divisa gallia omnis")).OrderBy(x => x));
    }

    [Fact]
    public async Task ASingleWordStillFindsEveryLineWithIt()
    {
        using var db = await SeedAsync();

        var found = await FindAsync("omnis");

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task RepeatingAWordDoesNotChangeTheAnswer()
    {
        using var db = await SeedAsync();

        Assert.Equal(await FindAsync("omnis divisa"), await FindAsync("omnis divisa omnis"));
    }

    // ---- one word means every spelling of it -----------------------------

    /// <summary>
    /// The spelling a reader is taught, against an edition that prints the
    /// other one.
    /// </summary>
    [Fact]
    public async Task TheClassicalSpellingFindsTheEditionPrintingTheOtherOne()
    {
        using var db = await SeedAsync();

        var found = await FindAsync("iustitia");

        Assert.Contains("iustitia et pax osculatae sunt", found);
        Assert.Contains("de justitia et misericordia", found);
    }

    [Fact]
    public async Task AndTheOtherWayRound()
    {
        using var db = await SeedAsync();

        Assert.Equal(
            (await FindAsync("iustitia")).OrderBy(x => x),
            (await FindAsync("justitia")).OrderBy(x => x));
    }

    [Fact]
    public async Task SpellingFoldingAppliesToEachWordOfAPhraseSeparately()
    {
        using var db = await SeedAsync();

        // "ut uita" typed classically; the line prints "vt vita".
        var found = await FindAsync("ut uita");

        Assert.Equal(new[] { "vt vita breuis est" }, found);
    }

    /// <summary>
    /// Folding must not turn one word into two requirements: "iustitia" and
    /// "justitia" are one word, and a line has only ever one of them.
    /// </summary>
    [Fact]
    public async Task TheSpellingsOfOneWordAreNotAllRequiredAtOnce()
    {
        using var db = await SeedAsync();

        Assert.NotEmpty(await FindAsync("iustitia"));
    }

    [Fact]
    public async Task GreekAndPunctuationOnlyQueriesAreUnaffected()
    {
        using var db = await SeedAsync();

        Assert.Empty(await FindAsync("μῆνιν"));
        Assert.Empty(await FindAsync("  ,  "));
    }
}
