using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// How securely a work is attributed to the author it is filed under.
///
/// Perseus and First1KGreek file the spuria under the author without comment -
/// correctly, since their job is to transmit what the manuscripts say rather
/// than to adjudicate - so the corpus carries no signal at all and the library
/// was presenting Definitiones as flatly Platonic. A built-in catalog seeds the
/// well-known cases; a person can overrule it; and the second must survive the
/// first, or the feature is worse than not having it.
/// </summary>
[Collection("Database")]
public class AttributionTests
{
    private static async Task<(int PlatoId, int WorkId)> SeedAsync(string title)
    {
        var authors = new AuthorRepository();
        var works = new WorkRepository();

        var authorId = await authors.UpsertAsync(new Author
        {
            Name = "Plato",
            CtsUrn = "urn:cts:greekLit:tlg0059"
        });

        var workId = await works.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = "urn:cts:greekLit:tlg0059.tlg" + Math.Abs(title.GetHashCode() % 900 + 100),
            Title = title
        });

        return (authorId, workId);
    }

    /// <summary>
    /// Everything is accepted until something says otherwise. The overwhelming
    /// majority of a library is exactly that, and a default of anything else
    /// would be a claim nobody made.
    /// </summary>
    [Fact]
    public async Task AWorkIsAcceptedByDefault()
    {
        using var db = await TempDatabase.CreateAsync();
        var (authorId, workId) = await SeedAsync("Republic");

        var work = (await new WorkRepository().GetByAuthorAsync(authorId))
            .Single(w => w.WorkId == workId);

        Assert.Equal(AttributionStatus.Accepted, work.AttributionStatus);
        Assert.False(work.IsDoubted);
        Assert.False(work.AttributionSetByUser);
    }

    /// <summary>
    /// The catalog seeds the well-known cases, and distinguishes the two kinds
    /// of doubt: Definitiones is nobody's idea of Plato, while Alcibiades I is
    /// defended and rejected by serious editors alike.
    /// </summary>
    [Theory]
    [InlineData("Definitiones", AttributionStatus.Spurious)]
    [InlineData("Hipparchus", AttributionStatus.Spurious)]
    [InlineData("Alcibiades 1", AttributionStatus.Disputed)]
    [InlineData("Hippias Major", AttributionStatus.Disputed)]
    [InlineData("Republic", AttributionStatus.Accepted)]
    [InlineData("Symposium", AttributionStatus.Accepted)]
    public async Task TheCatalogSeedsTheKnownCases(string title, AttributionStatus expected)
    {
        using var db = await TempDatabase.CreateAsync();
        var (authorId, workId) = await SeedAsync(title);
        var works = new WorkRepository();

        await works.ApplyCatalogDefaultsAsync();

        var work = (await works.GetByAuthorAsync(authorId)).Single(w => w.WorkId == workId);

        Assert.Equal(expected, work.AttributionStatus);
        Assert.False(work.AttributionSetByUser);
    }

    /// <summary>
    /// THE PROPERTY THE WHOLE DESIGN EXISTS FOR. A judgement made by a person
    /// survives the catalog running again - which it will, after every ingest,
    /// and every time the catalog grows.
    ///
    /// Without this, a decision could never be made to stick: somebody who
    /// concluded that Alcibiades I is genuine would find it marked disputed
    /// again the next time they imported a corpus, with nothing to tell them it
    /// had happened.
    /// </summary>
    [Fact]
    public async Task AJudgementSurvivesTheCatalogRunningAgain()
    {
        using var db = await TempDatabase.CreateAsync();
        var (authorId, workId) = await SeedAsync("Alcibiades 1");
        var works = new WorkRepository();

        await works.ApplyCatalogDefaultsAsync();
        await works.SetAttributionAsync(workId, AttributionStatus.Accepted, "I am convinced by Denyer.");

        await works.ApplyCatalogDefaultsAsync();
        await works.ApplyCatalogDefaultsAsync();

        var work = (await works.GetByAuthorAsync(authorId)).Single(w => w.WorkId == workId);

        Assert.Equal(AttributionStatus.Accepted, work.AttributionStatus);
        Assert.Equal("I am convinced by Denyer.", work.AttributionNote);
        Assert.True(work.AttributionSetByUser);
    }

    /// <summary>
    /// And it survives the work being re-ingested, which happens whenever a
    /// corpus is refreshed. The upsert updates title and citation scheme and
    /// leaves the attribution columns alone.
    /// </summary>
    [Fact]
    public async Task AJudgementSurvivesReIngestion()
    {
        using var db = await TempDatabase.CreateAsync();
        var (authorId, workId) = await SeedAsync("Hipparchus");
        var works = new WorkRepository();

        await works.SetAttributionAsync(workId, AttributionStatus.Accepted, "Under review.");

        var existing = (await works.GetByAuthorAsync(authorId)).Single(w => w.WorkId == workId);
        await works.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = existing.CtsUrn,
            Title = "Hipparchus",
            CitationScheme = "Stephanus"
        });

        var after = (await works.GetByAuthorAsync(authorId)).Single(w => w.WorkId == workId);

        Assert.Equal(AttributionStatus.Accepted, after.AttributionStatus);
        Assert.True(after.AttributionSetByUser);
        Assert.Equal("Stephanus", after.CitationScheme);
    }

    /// <summary>
    /// Handing a work back to the catalog forgets the judgement and restores
    /// the default, so a change of mind does not require remembering what the
    /// catalog originally said.
    /// </summary>
    [Fact]
    public async Task ClearingAnOverrideRestoresTheCatalogDefault()
    {
        using var db = await TempDatabase.CreateAsync();
        var (authorId, workId) = await SeedAsync("Definitiones");
        var works = new WorkRepository();

        await works.SetAttributionAsync(workId, AttributionStatus.Accepted, "Testing.");
        await works.ClearAttributionOverrideAsync(workId);

        var work = (await works.GetByAuthorAsync(authorId)).Single(w => w.WorkId == workId);

        Assert.Equal(AttributionStatus.Spurious, work.AttributionStatus);
        Assert.False(work.AttributionSetByUser);
        Assert.NotNull(work.AttributionNote);
    }

    /// <summary>
    /// Running the catalog is idempotent: the first pass changes what it needs
    /// to and the second changes nothing. The count it returns is meant to be
    /// shown after an ingest, and a number that stayed high on every run would
    /// be meaningless.
    /// </summary>
    [Fact]
    public async Task RunningTheCatalogTwiceChangesNothingTheSecondTime()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync("Definitiones");
        await SeedAsync("Theages");
        await SeedAsync("Republic");
        var works = new WorkRepository();

        var first = await works.ApplyCatalogDefaultsAsync();
        var second = await works.ApplyCatalogDefaultsAsync();

        Assert.Equal(2, first);
        Assert.Equal(0, second);
    }

    /// <summary>
    /// Titles arrive spelled differently from different corpora, so the match
    /// is a case-insensitive substring in either direction.
    /// </summary>
    [Theory]
    [InlineData("Plato", "Cleitophon")]
    [InlineData("Plato", "Clitophon")]           // First1KGreek's spelling
    [InlineData("Plato", "Kleitophon")]          // strict transliteration
    [InlineData("plato", "CLEITOPHON")]
    [InlineData("Plato of Athens", "Cleitophon, or The Exhortation")]
    [InlineData("Plato", "Alcibiades I")]        // editors number these three ways
    [InlineData("Plato", "First Alcibiades")]
    [InlineData("Xenophon", "Old Oligarch")]     // a nickname the manuscripts never use
    [InlineData("Homer", "Battle of Frogs and Mice")]
    public void TitlesAreMatchedAcrossSpellingsAndNumbering(string author, string title)
    {
        Assert.NotNull(DisputedWorkData.Lookup(author, title));
    }

    /// <summary>
    /// THE BUG THIS MATCHER WAS REWRITTEN FOR. Plato's *Ion* is genuine, and the
    /// first version marked it spurious: matching was substring-based in either
    /// direction, and "ion" sits inside "Definitiones" - and inside
    /// "Constitution of the Athenians" too.
    ///
    /// A genuine dialogue quietly reclassified, and dropped from any pool
    /// filtered on attribution. Matching whole words in one direction only
    /// fixes it, at the cost of no longer recognising a work called just
    /// "Alcibiades" - which is the right trade, since it could equally be
    /// either one and this table should not guess.
    /// </summary>
    [Theory]
    [InlineData("Plato", "Ion")]
    [InlineData("Plato", "Meno")]
    [InlineData("Plato", "Crito")]
    [InlineData("Plato", "Laws")]
    [InlineData("Plato", "Hippias Minor")]
    [InlineData("Plato", "Symposium")]
    [InlineData("Plato", "Republic")]
    [InlineData("Euripides", "Medea")]
    [InlineData("Sophocles", "Antigone")]
    public void GenuineWorksAreNeverMatched(string author, string title)
    {
        Assert.Null(DisputedWorkData.Lookup(author, title));
    }

    /// <summary>
    /// Every entry carries a note. A work marked doubted without a reason is an
    /// assertion the reader cannot evaluate or look up.
    /// </summary>
    [Fact]
    public void EveryCatalogEntryExplainsItself()
    {
        Assert.All(DisputedWorkData.All(), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Note));
            Assert.NotEqual(AttributionStatus.Accepted, e.Status);
        });
    }
}
