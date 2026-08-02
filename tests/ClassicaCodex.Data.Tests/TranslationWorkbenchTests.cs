using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The translation workbench's write path - saving a passage at a time into
/// a hand-written translation edition.
///
/// This is the only place in the app where someone's own composition goes
/// into the database. A bug here costs work that cannot be re-downloaded,
/// unlike every ingested text, so the behaviours worth pinning are the
/// destructive ones: revising a passage must replace it rather than leave
/// two, and clearing one must actually remove it rather than store a blank
/// that counts as progress.
/// </summary>
[Collection("Database")]
public class TranslationWorkbenchTests
{
    private static async Task<(TempDatabase Db, int Source, int Mine, int WorkId)> SeedWorkAsync()
    {
        var db = await TempDatabase.CreateAsync();

        var source = await db.SeedFullEditionAsync("iliad", "Homer", "greekLit", "Iliad", "Original", "grc");
        await db.InsertLinesAsync(source,
            ("1.1", "μῆνιν ἄειδε θεά"), ("1.2", "οὐλομένην"), ("1.3", "ἣ μυρί' Ἀχαιοῖς"));

        var mine = await db.SeedSiblingEditionAsync("iliad", "iliad-mine", "Translation", "eng", "My translation");

        return (db, source, mine, await db.WorkIdForAsync("iliad"));
    }

    [Fact]
    public async Task SavingAPassageStoresItAgainstItsCitationRef()
    {
        var (db, _, mine, _) = await SeedWorkAsync();
        using var _db = db;

        await new TextNodeRepository().SaveTranslatedLineAsync(mine, "1.1", 0, "Sing, goddess, the wrath");

        var line = Assert.Single(await new TextNodeRepository().GetByEditionAsync(mine));

        Assert.Equal("1.1", line.CitationRef);
        Assert.Equal("Sing, goddess, the wrath", line.Text);
    }

    /// <summary>
    /// Going back to an earlier passage and changing your mind is the normal
    /// case, not the exception - a second row for the same reference would
    /// show as a duplicate line in the reader and inflate the progress count.
    /// </summary>
    [Fact]
    public async Task RevisingAPassageReplacesItRatherThanAddingASecond()
    {
        var (db, _, mine, _) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "first attempt");
        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "second attempt");

        var line = Assert.Single(await repo.GetByEditionAsync(mine));

        Assert.Equal("second attempt", line.Text);
    }

    /// <summary>
    /// Clearing the box means "I haven't done this one", so the line goes
    /// rather than being stored blank. A blank would count toward the
    /// progress figure and stop the workbench resuming at that passage.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ClearingAPassageRemovesTheLine(string? cleared)
    {
        var (db, _, mine, _) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "something");
        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, cleared);

        Assert.Empty(await repo.GetByEditionAsync(mine));
    }

    [Fact]
    public async Task PassagesAreIndependentOfEachOther()
    {
        var (db, _, mine, _) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "one");
        await repo.SaveTranslatedLineAsync(mine, "1.2", 1, "two");
        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, null);

        var line = Assert.Single(await repo.GetByEditionAsync(mine));

        Assert.Equal("1.2", line.CitationRef);
    }

    [Fact]
    public async Task SavedTextIsTrimmed()
    {
        var (db, _, mine, _) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "  padded  ");

        Assert.Equal("padded", Assert.Single(await repo.GetByEditionAsync(mine)).Text);
    }

    /// <summary>
    /// Work done in one sitting has to be there in the next. The workbench
    /// reloads by reading the edition back and keying it on citation
    /// reference, which is what lets it resume at the first passage with
    /// nothing written.
    /// </summary>
    [Fact]
    public async Task WorkSurvivesBeingReadBackAsTheWorkbenchDoes()
    {
        var (db, _, mine, _) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "first");
        await repo.SaveTranslatedLineAsync(mine, "1.3", 2, "third");

        var existing = (await repo.GetByEditionAsync(mine))
            .GroupBy(n => n.CitationRef)
            .ToDictionary(g => g.Key, g => g.First().Text);

        Assert.Equal(2, existing.Count);
        Assert.Equal("first", existing["1.1"]);
        Assert.Equal("third", existing["1.3"]);

        // 1.2 is the first with nothing against it - where the workbench
        // should reopen.
        Assert.False(existing.ContainsKey("1.2"));
    }

    // --- inferring which original a translation belongs to ----------------

    /// <summary>
    /// Which edition a translation was made from isn't recorded, so on
    /// resume it's inferred from shared citation references. Getting this
    /// wrong is destructive rather than cosmetic: the passages already
    /// written would stop lining up with the text on screen.
    /// </summary>
    [Fact]
    public async Task TheSourceEditionIsInferredFromSharedCitationRefs()
    {
        var (db, source, mine, workId) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        // A second original of the same work, lineated completely differently.
        var other = await db.SeedSiblingEditionAsync("iliad", "iliad-alt", "Original", "grc");
        await db.InsertLinesAsync(other, ("A", "x"), ("B", "y"), ("C", "z"));

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "one");
        await repo.SaveTranslatedLineAsync(mine, "1.2", 1, "two");

        var inferred = await repo.FindClosestEditionAsync(mine, workId, EditionKind.Original);

        Assert.Equal(source, inferred);
        Assert.NotEqual(other, inferred);
    }

    [Fact]
    public async Task NoInferenceWhenNothingHasBeenWrittenYet()
    {
        var (db, _, mine, workId) = await SeedWorkAsync();
        using var _db = db;

        // An empty translation shares no references with anything, so there
        // is no evidence to infer from - the caller asks instead of guessing.
        Assert.Null(await new TextNodeRepository()
            .FindClosestEditionAsync(mine, workId, EditionKind.Original));
    }

    [Fact]
    public async Task InferenceIgnoresEditionsOfOtherWorks()
    {
        var (db, source, mine, workId) = await SeedWorkAsync();
        using var _db = db;
        var repo = new TextNodeRepository();

        // Another work using the very same citation scheme - the commonest
        // reference in the corpus is "1.1", so matching on it alone across
        // works would pair a translation with a completely different text.
        var elsewhere = await db.SeedFullEditionAsync(
            "odyssey", "Homer", "greekLit", "Odyssey", "Original", "grc");
        await db.InsertLinesAsync(elsewhere, ("1.1", "ἄνδρα μοι ἔννεπε"));

        await repo.SaveTranslatedLineAsync(mine, "1.1", 0, "one");

        Assert.Equal(source, await repo.FindClosestEditionAsync(mine, workId, EditionKind.Original));
    }
}
