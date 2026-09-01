using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Whether SQLite has been told what this library contains.
///
/// Without statistics the query planner works from built-in guesses, and on a
/// full library the guesses are wrong expensively: a dictionary lookup read
/// all 423,551 definitions instead of seeking the index (115ms), and finding a
/// form's headwords scanned 585,225 Latin lemmas through the wrong index of
/// the two available (349ms). Both are hundredths of a millisecond once the
/// planner has counts. The gathering itself takes about ten milliseconds,
/// because analysis_limit caps how much of each index is sampled.
///
/// The trap this guards is the empty database. Statistics saying every table
/// holds nothing are worse than no statistics at all, and a library is created
/// empty and filled afterwards - so gathering them at schema creation would
/// bake in exactly the wrong answer and keep it.
/// </summary>
[Collection("Database")]
public class QueryStatisticsTests
{
    private static async Task<bool> HasStatistics(TempDatabase db) =>
        await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='sqlite_stat1';") > 0;

    /// <summary>
    /// A new library is empty, so there is nothing true to record about it yet.
    /// </summary>
    [Fact]
    public async Task AFreshDatabaseIsNotAnalysed()
    {
        using var db = await TempDatabase.CreateAsync();

        Assert.False(await HasStatistics(db));
    }

    /// <summary>
    /// After the corpus is in - the word index build calls this - the planner
    /// gets counts.
    /// </summary>
    [Fact]
    public async Task UpdatingStatisticsRecordsThem()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "arma uirumque cano"));

        await SchemaInitializer.UpdateQueryStatisticsAsync();

        Assert.True(await HasStatistics(db));
    }

    /// <summary>
    /// An existing library that has never been analysed gets it on the next
    /// launch, without waiting for a re-ingest that may never happen.
    /// </summary>
    [Fact]
    public async Task AnExistingLibraryWithContentIsAnalysedOnOpen()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "arma uirumque cano"));
        Assert.False(await HasStatistics(db));

        // What the next launch does.
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.True(await HasStatistics(db));
    }

    /// <summary>
    /// And re-opening an empty one still does not, however many times it is
    /// opened.
    /// </summary>
    [Fact]
    public async Task ReopeningAnEmptyLibraryStillDoesNotAnalyse()
    {
        using var db = await TempDatabase.CreateAsync();

        await SchemaInitializer.EnsureSchemaAsync();
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.False(await HasStatistics(db));
    }

    /// <summary>
    /// Statistics are gathered once and not re-gathered on every open - the
    /// check is for their presence, so an already-analysed library opens
    /// without paying for it again.
    /// </summary>
    [Fact]
    public async Task AnAlreadyAnalysedLibraryIsNotReanalysedOnOpen()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "arma uirumque cano"));
        await SchemaInitializer.UpdateQueryStatisticsAsync();

        // Something the second gathering would overwrite if it ran.
        await db.ExecuteAsync("UPDATE sqlite_stat1 SET stat = '999999' WHERE rowid = 1;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(1, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_stat1 WHERE stat = '999999';"));
    }
}

/// <summary>
/// A dictionary lookup answers in the language it was asked about.
///
/// 2,999 normalized headwords in the full library belong to more than one
/// language, because the English WordNet and the Latin Lewis & Short both
/// carry words like "batrachomyomachia" and "metempsychosis". The query had
/// the language in hand - it normalizes with it, and Latin normalizing folds v
/// to u and j to i - and then did not filter on it, so a Latin lookup returned
/// its Lewis & Short entries with WordNet's English gloss underneath.
///
/// Language is also the leading column of IX_Definitions_Normalized, so the
/// same omission meant the lookup could not seek the index and read all
/// 423,551 rows instead.
/// </summary>
[Collection("Database")]
public class DefinitionLanguageScopeTests
{
    private static async Task SeedAsync(TempDatabase db)
    {
        // The same headword in two languages, which is the case that was wrong.
        await db.ExecuteAsync(@"
            INSERT INTO Definitions (Language, Headword, NormalizedHeadword, Entry, Source)
            VALUES ('lat', 'Batrachomyomachia', 'batrachomyomachia', 'the battle of frogs and mice', 'Lewis & Short'),
                   ('eng', 'batrachomyomachia', 'batrachomyomachia', 'a petty squabble', 'WordNet'),
                   ('grc', 'λόγος', 'λογοσ', 'word, reason', 'LSJ');");
    }

    [Fact]
    public async Task ALatinLookupReturnsOnlyLatin()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db);

        var entries = await new DefinitionRepository()
            .GetByHeadwordAsync("Batrachomyomachia", "lat");

        Assert.Equal(new[] { "Lewis & Short" }, entries.Select(e => e.Source));
    }

    [Fact]
    public async Task AnEnglishLookupReturnsOnlyEnglish()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db);

        var entries = await new DefinitionRepository()
            .GetByHeadwordAsync("batrachomyomachia", "eng");

        Assert.Equal(new[] { "WordNet" }, entries.Select(e => e.Source));
    }

    /// <summary>
    /// A language the library has no dictionary for gets nothing, rather than
    /// another language's entry that happens to normalize the same way. Menota
    /// editions are Old Norse and there is no Old Norse dictionary here.
    /// </summary>
    [Fact]
    public async Task ALanguageWithNoDictionaryGetsNothing()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db);

        Assert.Empty(await new DefinitionRepository()
            .GetByHeadwordAsync("Batrachomyomachia", "non"));
    }

    /// <summary>
    /// The Latin folding still applies - it is what lets "iuuenis" find a
    /// headword stored as "juvenis" - and now applies only to Latin.
    /// </summary>
    [Fact]
    public async Task LatinFoldingStillFindsTheHeadword()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Definitions (Language, Headword, NormalizedHeadword, Entry, Source)
            VALUES ('lat', 'juvenis', 'iuuenis', 'young', 'Lewis & Short');");

        var entries = await new DefinitionRepository().GetByHeadwordAsync("juvenis", "lat");

        Assert.Single(entries);
    }
}
