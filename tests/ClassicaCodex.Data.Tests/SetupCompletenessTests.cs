using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// How the setup wizard decides a collection is already installed.
///
/// Getting this wrong in one direction is a nuisance and in the other
/// direction hides a corpus. A collection wrongly reported as missing costs a
/// re-run that changes nothing. A collection wrongly reported as installed
/// makes the wizard skip a step, and the texts never arrive - and nothing
/// says so, because the library is full of other texts and every screen
/// works.
///
/// That is not hypothetical. Until this was changed, the wizard asked whether
/// any latinLit author existed, which was the same question as "has the
/// classical Latin been downloaded" only while classical Latin was the only
/// Latin in the app. 3.2.0 added CSEL and the Patrologia Latina, both of them
/// latinLit, so from then on installing either one vouched for Caesar,
/// Cicero and Virgil - and the library that prompted this test had 335
/// latinLit authors, not one of them from canonical-latinLit. The Greek rows
/// carried the identical trap, with First1KGreek standing in for
/// canonical-greekLit.
/// </summary>
[Collection("Database")]
public class SetupCompletenessTests
{
    private const string PerseusLatin = "perseus-latin";
    private const string Csel = "csel";

    /// <summary>
    /// The exact shape of the bug: a library holding CSEL and nothing else
    /// Latin. The namespace says yes, and it is the wrong question.
    /// </summary>
    [Fact]
    public async Task CselDoesNotVouchForTheClassicalLatinCorpus()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionRepo = new EditionRepository();
        var authorRepo = new AuthorRepository();

        await db.SeedFullEditionAsync("cselwork", "Augustine", "latinLit",
            "De ciuitate dei", "Original", "lat");
        await db.ExecuteAsync(
            $"UPDATE Editions SET Collection = '{Csel}' WHERE CtsUrn = 'urn:e:cselwork';");

        // The signal the wizard used to trust, and what it would have said.
        Assert.True(await authorRepo.CountByNamespaceAsync("latinLit") > 0);

        // The signal it uses now.
        Assert.Equal(0, await editionRepo.CountByCollectionAsync(PerseusLatin));
        Assert.Equal(1, await editionRepo.CountByCollectionAsync(Csel));
    }

    /// <summary>
    /// And the same corpus, once it really is there, does read as installed -
    /// the check has to be capable of saying yes, or a passing test above
    /// would only prove it always says no.
    /// </summary>
    [Fact]
    public async Task TheClassicalLatinCorpusVouchesForItself()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionRepo = new EditionRepository();

        await db.SeedFullEditionAsync("virgil", "Virgil", "latinLit",
            "Aeneid", "Original", "lat");
        await db.ExecuteAsync(
            $"UPDATE Editions SET Collection = '{PerseusLatin}' WHERE CtsUrn = 'urn:e:virgil';");

        Assert.Equal(1, await editionRepo.CountByCollectionAsync(PerseusLatin));
    }

    /// <summary>
    /// An edition no step has stamped - a library predating the collection
    /// column whose backfill could not recognise a custom folder - counts for
    /// nobody. That is the safe answer: the step reads as not run, running it
    /// again stamps it properly, and nothing is lost but the time.
    /// </summary>
    [Fact]
    public async Task AnUnstampedEditionVouchesForNoCollection()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionRepo = new EditionRepository();

        await db.SeedFullEditionAsync("orphan", "Anonymous", "latinLit",
            "Incerta", "Original", "lat");

        Assert.Equal(0, await editionRepo.CountByCollectionAsync(PerseusLatin));
        Assert.Equal(0, await editionRepo.CountByCollectionAsync(Csel));
    }
}
