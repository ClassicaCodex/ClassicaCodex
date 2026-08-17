using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Finding the works this library holds twice, and reading them out in a form
/// the comparison can use.
///
/// The part worth pinning is the passage reference. The stored citation
/// reference carries the edition's own version identifier, which differs
/// between editions by design - so two printings of the same line never match
/// on it, and a collation keyed on the raw reference would report a work as
/// having nothing in common with itself.
/// </summary>
[Collection("Database")]
public class CollationRepositoryTests
{
    private static async Task<int> SeedAsync(
        TempDatabase db, string key, string collection, string kind = "Original", string language = "grc")
    {
        var editionId = await db.SeedFullEditionAsync(key, "Aeschylus", "greekLit", "Agamemnon", kind, language);
        await db.ExecuteAsync($"UPDATE Editions SET Collection = '{collection}' WHERE EditionId = {editionId};");
        return editionId;
    }

    /// <summary>
    /// Two collections carrying the same work is the whole precondition. They
    /// have to be found as one pair rather than two editions.
    /// </summary>
    [Fact]
    public async Task AWorkHeldByTwoCollectionsIsFoundAsOnePair()
    {
        using var db = await TempDatabase.CreateAsync();

        // One work, two editions - the shape a second collection creates.
        var perseus = await SeedAsync(db, "ag", "perseus-greek");
        await db.ExecuteAsync(@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Collection)
            VALUES ((SELECT WorkId FROM Editions WHERE EditionId = " + perseus + @"),
                    'urn:e:ag-1st1k', 'Original', 'grc', 'first1k-greek');");

        var pair = Assert.Single(await new CollationRepository().FindPairsAsync());

        Assert.Equal("Aeschylus", pair.AuthorName);
        Assert.Equal("Agamemnon", pair.WorkTitle);
        Assert.Equal("first1k-greek", pair.LeftCollection);
        Assert.Equal("perseus-greek", pair.RightCollection);
        Assert.Equal("grc", pair.Language);
    }

    /// <summary>
    /// A work's translations are different texts by different hands. Lining two
    /// of those up produces a difference at every line and says nothing about
    /// either, so only original-language editions pair.
    /// </summary>
    [Fact]
    public async Task TranslationsAreNotCollatedAgainstEachOther()
    {
        using var db = await TempDatabase.CreateAsync();

        var greek = await SeedAsync(db, "ag", "perseus-greek");
        await db.ExecuteAsync($@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Collection)
            VALUES ((SELECT WorkId FROM Editions WHERE EditionId = {greek}),
                    'urn:e:ag-eng1', 'Translation', 'eng', 'first1k-greek');
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Collection)
            VALUES ((SELECT WorkId FROM Editions WHERE EditionId = {greek}),
                    'urn:e:ag-eng2', 'Translation', 'eng', 'perseus-greek');");

        Assert.Empty(await new CollationRepository().FindPairsAsync());
    }

    /// <summary>
    /// Two editions of a work within one collection are not a cross-collection
    /// pair, and an edition with no collection recorded cannot be attributed to
    /// one - neither belongs in the list.
    /// </summary>
    [Fact]
    public async Task EditionsFromOneCollectionOrFromNoneDoNotPair()
    {
        using var db = await TempDatabase.CreateAsync();

        var first = await SeedAsync(db, "ag", "perseus-greek");
        await db.ExecuteAsync($@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Collection)
            VALUES ((SELECT WorkId FROM Editions WHERE EditionId = {first}),
                    'urn:e:ag-2', 'Original', 'grc', 'perseus-greek');
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Collection)
            VALUES ((SELECT WorkId FROM Editions WHERE EditionId = {first}),
                    'urn:e:ag-3', 'Original', 'grc', NULL);");

        Assert.Empty(await new CollationRepository().FindPairsAsync());
    }

    /// <summary>
    /// The reference the two editions actually share. Stored refs carry each
    /// edition's own version identifier - perseus-grc2 against 1st1K-grc1 - so
    /// comparing them whole matches nothing, however identical the lines.
    /// </summary>
    [Fact]
    public async Task PassagesAreKeyedByTheReferenceBothEditionsShare()
    {
        using var db = await TempDatabase.CreateAsync();

        var editionId = await SeedAsync(db, "ag", "perseus-greek");
        await db.InsertLinesAsync(editionId,
            ("urn:cts:greekLit:tlg0085.tlg005.perseus-grc2.1.1", "μῆνιν ἄειδε"),
            ("urn:cts:greekLit:tlg0085.tlg005.perseus-grc2.1.2", "οὐλομένην"));

        var passages = await new CollationRepository().GetPassagesAsync(editionId);

        Assert.Equal(["1.1", "1.2"], passages.Select(p => p.PassageRef));
        Assert.Equal("μῆνιν ἄειδε", passages[0].Text);
    }

    /// <summary>
    /// Empty lines are not readings. An edition carrying structural nodes with
    /// no text would otherwise contribute rows that differ from nothing.
    /// </summary>
    [Fact]
    public async Task PassagesWithNoTextAreLeftOut()
    {
        using var db = await TempDatabase.CreateAsync();

        var editionId = await SeedAsync(db, "ag", "perseus-greek");
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν"), ("1.2", "   "), ("1.3", "ἄειδε"));

        Assert.Equal(["1.1", "1.3"],
            (await new CollationRepository().GetPassagesAsync(editionId)).Select(p => p.PassageRef));
    }

    /// <summary>
    /// End to end: two editions differing in one word, read out of the database
    /// and compared, with the difference landing in the right bucket.
    /// </summary>
    [Fact]
    public async Task CollatingAPairReadsBothSidesAndGradesTheDifferences()
    {
        using var db = await TempDatabase.CreateAsync();

        var perseus = await SeedAsync(db, "ag", "perseus-greek");
        await db.ExecuteAsync($@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Collection)
            VALUES ((SELECT WorkId FROM Editions WHERE EditionId = {perseus}),
                    'urn:e:ag-1st1k', 'Original', 'grc', 'first1k-greek');");
        var first1k = await db.ScalarAsync<int>("SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:ag-1st1k';");

        await db.InsertLinesAsync(perseus,
            ("urn:cts:greekLit:tlg0085.tlg005.perseus-grc2.1", "μῆνιν ἄειδε θεά,"),
            ("urn:cts:greekLit:tlg0085.tlg005.perseus-grc2.2", "λήμασιν ἴσους"));
        await db.InsertLinesAsync(first1k,
            ("urn:cts:greekLit:tlg0085.tlg005.1st1K-grc1.1", "μῆνιν ἄειδε θεά"),
            ("urn:cts:greekLit:tlg0085.tlg005.1st1K-grc1.2", "λήμασι δισσοὺς"));

        var repo = new CollationRepository();
        var result = await repo.CollateAsync(Assert.Single(await repo.FindPairsAsync()));

        Assert.True(result.IsAlignable);
        Assert.Equal(2, result.Shared);
        Assert.Equal(1, result.PresentationDiffers);
        Assert.Equal(1, result.TextDiffers);
        Assert.Equal(0, result.OnlyInLeft);
        Assert.Equal(0, result.OnlyInRight);
    }
}
