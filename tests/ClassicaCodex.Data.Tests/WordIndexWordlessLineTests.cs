using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A line with no indexable words - a lacuna marker, a bare citation number,
/// a run of punctuation - contributes nothing to WordIndex once tokenized.
/// Without a placeholder row for it, its TextNodeId never appears in
/// GetIndexedTextNodeCountAsync, and the gap between that and the true line
/// count looks exactly like staleness to anything comparing the two -
/// forever, on every corpus that has even one such line, including a build
/// that just finished and has nothing wrong with it. These tests build a
/// real WordIndex over real wordless lines and check that the count comes
/// out whole.
/// </summary>
[Collection("Database")]
public class WordIndexWordlessLineTests
{
    [Fact]
    public async Task BuildAsync_CountsAWordlessLine_SameAsAnyOtherLine()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId,
            ("1.1", "μῆνιν ἄειδε"),
            ("1.2", "..."),           // lacuna marker: no letters at all
            ("1.3", "12"));           // bare citation number: no letters

        await new WordIndexService().BuildAsync();

        var repo = new WordIndexRepository();
        var total = await repo.GetTextNodeCountAsync();
        var indexed = await repo.GetIndexedTextNodeCountAsync();

        Assert.Equal(3, total);
        Assert.Equal(total, indexed);   // the exact comparison SetupWizardForm makes
    }

    /// <summary>
    /// The reported scenario at corpus scale: two ordinary lines and one
    /// wordless one. Before the fix, indexed would be 2 of 3 forever.
    /// </summary>
    [Fact]
    public async Task AFreshBuild_ReportsUpToDate_EvenWithWordlessLinesPresent()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId,
            ("1.1", "arma virumque cano"),
            ("1.2", "—"),
            ("1.3", "Troiae qui primus"));

        await new WordIndexService().BuildAsync();

        var repo = new WordIndexRepository();
        var upToDate = await repo.GetIndexedTextNodeCountAsync() >= await repo.GetTextNodeCountAsync();

        Assert.True(upToDate, "A completed build should never report itself as stale.");
    }

    [Fact]
    public async Task ReindexEditionAsync_AlsoCountsAWordlessLine()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "..."));

        await new WordIndexService().ReindexEditionAsync(editionId);

        var repo = new WordIndexRepository();
        Assert.Equal(1, await repo.GetIndexedTextNodeCountAsync());
    }

    /// <summary>
    /// The placeholder must never leak into a real result. Every search path
    /// filters normalized forms to Length > 0 before querying, but this
    /// confirms the placeholder itself can't be found even if asked for
    /// directly by its literal value.
    /// </summary>
    [Fact]
    public async Task WordlessPlaceholder_NeverMatchesASearch()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "real word"), ("1.2", "..."));

        await new WordIndexService().BuildAsync();

        var hits = await new TextNodeRepository().SearchByFormsAsync(new[] { "" });

        Assert.Empty(hits.Rows);
    }

    [Fact]
    public async Task ALineWithRealWords_IsNotDoubleCountedByThePlaceholder()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "one real word"));

        await new WordIndexService().BuildAsync();

        Assert.Equal(1, await new WordIndexRepository().GetIndexedTextNodeCountAsync());
    }
}
