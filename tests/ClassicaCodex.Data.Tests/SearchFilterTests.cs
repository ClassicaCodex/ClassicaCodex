using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The search window's filters, which are the newest and least-exercised
/// thing in the app and the one most people will touch daily.
///
/// A filter that quietly returns the wrong rows is close to undetectable by
/// eye: the results look like plausible results. That's the failure mode
/// worth pinning, and it's why every case below asserts on which passages
/// came back rather than just how many.
/// </summary>
[Collection("Database")]
public class SearchFilterTests
{
    /// <summary>
    /// A miniature library with the shapes that matter: a Greek original and
    /// its English translation of the same work, a Latin original, and an
    /// English original - so "English" and "translation" can be told apart.
    /// </summary>
    private static async Task<TempDatabase> SeedLibraryAsync()
    {
        var db = await TempDatabase.CreateAsync();

        var greek = await db.SeedFullEditionAsync("homer", "Homer", "greekLit", "Iliad", "Original", "grc");
        await db.InsertLinesAsync(greek, ("1.1", "μῆνιν ἄειδε θεά"), ("1.2", "ἄνδρα μοι ἔννεπε"));

        var homerEnglish = await db.SeedSiblingEditionAsync("homer", "homer-eng", "Translation", "eng", "Samuel Butler");
        await db.InsertLinesAsync(homerEnglish,
            ("1.1", "Sing, goddess, the wrath of Achilles"),
            ("1.2", "Tell me, Muse, of that ingenious hero"));

        var latin = await db.SeedFullEditionAsync("vergil", "Vergil", "latinLit", "Aeneid", "Original", "lat");
        await db.InsertLinesAsync(latin, ("1.1", "arma virumque cano"), ("1.2", "wrath of the gods in Latin"));

        var english = await db.SeedFullEditionAsync("shakespeare", "Shakespeare", "engLit", "Hamlet", "Original", "eng");
        await db.InsertLinesAsync(english, ("1.1", "the wrath of a prince"), ("1.2", "arms and the man"));

        return db;
    }

    private static SearchFilters Query(string text) => new() { Query = text };

    private static List<string> TextsOf(SearchHits hits) => hits.Rows.Select(r => r.Text).ToList();

    [Fact]
    public async Task NoFiltersSearchesTheWholeLibrary()
    {
        using var db = await SeedLibraryAsync();

        var hits = await new TextNodeRepository().SearchFilteredAsync(Query("wrath"));

        Assert.Equal(3, hits.Rows.Count);
    }

    [Fact]
    public async Task EmptyQueryReturnsNothingRatherThanEverything()
    {
        using var db = await SeedLibraryAsync();

        var hits = await new TextNodeRepository().SearchFilteredAsync(Query("   "));

        Assert.Empty(hits.Rows);
    }

    /// <summary>
    /// The case the namespace filter cannot answer, and the reason the collection
    /// filter exists: two collections in one language tradition. CSEL and the
    /// classical Latin texts are both "latinLit", as are First1KGreek and Perseus
    /// Greek both "greekLit" - so "search only CSEL" has no expression in terms of
    /// namespaces at all.
    /// </summary>
    [Fact]
    public async Task CollectionFilterSeparatesTwoCollectionsSharingANamespace()
    {
        using var db = await TempDatabase.CreateAsync();

        var classical = await db.SeedFullEditionAsync("vergil", "Vergil", "latinLit", "Aeneid", "Original", "lat");
        await db.InsertLinesAsync(classical, ("1.1", "arma virumque cano"));
        var fathers = await db.SeedFullEditionAsync("augustine", "Augustine", "latinLit", "Confessiones", "Original", "lat");
        await db.InsertLinesAsync(fathers, ("1.1", "arma spiritalia sumenda sunt"));

        // Stamped the way a setup step stamps them: by the folder it just
        // imported from, recording a key that does not mention the folder.
        var editions = new EditionRepository();
        await db.ExecuteAsync(
            $@"UPDATE Editions SET SourcePath = 'C:\Data\latin-texts\data\vergil\x.xml' WHERE EditionId = {classical};
               UPDATE Editions SET SourcePath = 'C:\Data\csel\data\augustine\y.xml' WHERE EditionId = {fathers};");
        await editions.StampCollectionAsync(@"C:\Data\latin-texts", "perseus-latin");
        await editions.StampCollectionAsync(@"C:\Data\csel", "csel");

        var repo = new TextNodeRepository();

        var byNamespace = new SearchFilters { Query = "arma" };
        byNamespace.Corpora.Add("latinLit");
        Assert.Equal(2, (await repo.SearchFilteredAsync(byNamespace)).Rows.Count);

        var cselOnly = new SearchFilters { Query = "arma" };
        cselOnly.Collections.Add("csel");
        Assert.Equal("arma spiritalia sumenda sunt",
            Assert.Single((await repo.SearchFilteredAsync(cselOnly)).Rows).Text);

        // Any number of them, which is what makes it a set rather than a dropdown.
        var both = new SearchFilters { Query = "arma" };
        both.Collections.Add("csel");
        both.Collections.Add("perseus-latin");
        Assert.Equal(2, (await repo.SearchFilteredAsync(both)).Rows.Count);

        // The library knows what it holds without being told where it came from.
        Assert.Equal(["csel", "perseus-latin"], await editions.GetCollectionsAsync());

        // And the whole point of storing a key rather than a path: the downloads
        // are gone, the paths are meaningless, and the filter still works.
        await db.ExecuteAsync("UPDATE Editions SET SourcePath = NULL;");
        var afterCleanup = new SearchFilters { Query = "arma" };
        afterCleanup.Collections.Add("csel");
        Assert.Equal("arma spiritalia sumenda sunt",
            Assert.Single((await repo.SearchFilteredAsync(afterCleanup)).Rows).Text);

        // And unset still means everything, so the default search is unchanged.
        Assert.Equal(2, (await repo.SearchFilteredAsync(Query("arma"))).Rows.Count);
    }

    /// <summary>
    /// What the library tree's collection filter is built on. An author belongs in the
    /// filtered tree exactly when one of their works does, so the question is asked
    /// about works and the authors follow - otherwise a filter could leave an author
    /// standing with every one of their works hidden.
    /// </summary>
    [Fact]
    public async Task WorkIdsForCollectionsCoverOnlyTheChosenOnes()
    {
        using var db = await TempDatabase.CreateAsync();
        var classical = await db.SeedFullEditionAsync("vergil", "Vergil", "latinLit", "Aeneid", "Original", "lat");
        var fathers = await db.SeedFullEditionAsync("augustine", "Augustine", "latinLit", "Confessiones", "Original", "lat");
        var editions = new EditionRepository();

        await db.ExecuteAsync(
            $@"UPDATE Editions SET SourcePath = 'C:\D\latin-texts\a.xml' WHERE EditionId = {classical};
               UPDATE Editions SET SourcePath = 'C:\D\csel\b.xml' WHERE EditionId = {fathers};");
        await editions.StampCollectionAsync(@"C:\D\latin-texts", "perseus-latin");
        await editions.StampCollectionAsync(@"C:\D\csel", "csel");

        var vergil = await db.WorkIdForAsync("vergil");
        var augustine = await db.WorkIdForAsync("augustine");

        var cselOnly = await editions.GetWorkIdsForCollectionsAsync(["csel"]);
        Assert.Equal([augustine], cselOnly);

        var both = await editions.GetWorkIdsForCollectionsAsync(["csel", "perseus-latin"]);
        Assert.Contains(vergil, both);
        Assert.Contains(augustine, both);

        // Nothing chosen is not the same as everything chosen: the caller treats an
        // empty selection as "no filter" and never asks, so this must not answer with
        // the whole library and quietly invert the filter.
        Assert.Empty(await editions.GetWorkIdsForCollectionsAsync([]));
    }

    // --- match modes ------------------------------------------------------

    [Fact]
    public async Task ContainsMatchesInsideWords()
    {
        using var db = await SeedLibraryAsync();

        var hits = await new TextNodeRepository().SearchFilteredAsync(Query("arm"));

        // "arma", "arms" - the substring behaviour that makes this the safe
        // default rather than a precise one.
        Assert.Equal(2, hits.Rows.Count);
    }

    /// <summary>
    /// The case the SQL-only implementation got wrong: a word at the very
    /// start of a line has no character before it to act as a boundary.
    /// </summary>
    [Fact]
    public async Task WholeWordMatchesAWordStartingTheLine()
    {
        using var db = await SeedLibraryAsync();
        await new WordIndexService().BuildAsync();

        var filters = Query("arma");
        filters.MatchMode = SearchMatchMode.WholeWord;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Contains("arma virumque cano", TextsOf(hits));
    }

    [Fact]
    public async Task WholeWordRejectsASubstringOfALongerWord()
    {
        using var db = await SeedLibraryAsync();
        await new WordIndexService().BuildAsync();

        var filters = Query("arm");
        filters.MatchMode = SearchMatchMode.WholeWord;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        // "arma" and "arms" both contain "arm" and neither is the word.
        Assert.Empty(hits.Rows);
    }

    /// <summary>
    /// Accent-insensitivity, and where it has to come from.
    ///
    /// The word index stores one normalized word per line, so an unaccented
    /// query finds an accented text. A LIKE prefilter cannot do this at any
    /// stage: the pattern is compared against the raw text, so "θεα" simply
    /// isn't in "θεά", and no amount of normalizing afterwards recovers a
    /// row the prefilter already threw away. That was a real defect here
    /// until this test caught it.
    /// </summary>
    [Fact]
    public async Task WholeWordIgnoresGreekAccentsWhenTheIndexIsBuilt()
    {
        using var db = await SeedLibraryAsync();
        await new WordIndexService().BuildAsync();

        var filters = Query("θεα");
        filters.MatchMode = SearchMatchMode.WholeWord;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Contains("μῆνιν ἄειδε θεά", TextsOf(hits));
    }

    /// <summary>
    /// Without the index there's no normalized form to match against, so
    /// whole-word falls back to a literal prefilter: it still rejects
    /// substrings, but only finds a word spelled as typed. Pinned so the
    /// difference stays a known, documented limitation rather than an
    /// intermittent mystery for anyone who hasn't run Build Word Index.
    /// </summary>
    [Fact]
    public async Task WholeWordWithoutTheIndexStillMatchesAnExactSpelling()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("arma");
        filters.MatchMode = SearchMatchMode.WholeWord;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Contains("arma virumque cano", TextsOf(hits));

        var substring = Query("arm");
        substring.MatchMode = SearchMatchMode.WholeWord;

        Assert.Empty((await new TextNodeRepository().SearchFilteredAsync(substring)).Rows);
    }

    [Fact]
    public async Task AllWordsRequiresEveryWordButNotAdjacency()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("wrath Achilles");
        filters.MatchMode = SearchMatchMode.AllWords;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        var hit = Assert.Single(hits.Rows);
        Assert.Equal("Sing, goddess, the wrath of Achilles", hit.Text);
    }

    // --- narrowing --------------------------------------------------------

    [Fact]
    public async Task LanguageFilterRestrictsToThatLanguage()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("wrath");
        filters.Languages.Add("lat");

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        var hit = Assert.Single(hits.Rows);
        Assert.Equal("wrath of the gods in Latin", hit.Text);
    }

    /// <summary>
    /// Language and kind are separate axes on purpose. Both of these lines
    /// are English; only one of them is a translation.
    /// </summary>
    [Fact]
    public async Task EnglishOriginalsAndEnglishTranslationsAreDistinguishable()
    {
        using var db = await SeedLibraryAsync();
        var repo = new TextNodeRepository();

        var translations = Query("wrath");
        translations.Languages.Add("eng");
        translations.OriginalsOnly = false;

        var originals = Query("wrath");
        originals.Languages.Add("eng");
        originals.OriginalsOnly = true;

        Assert.Equal("Sing, goddess, the wrath of Achilles",
            Assert.Single((await repo.SearchFilteredAsync(translations)).Rows).Text);
        Assert.Equal("the wrath of a prince",
            Assert.Single((await repo.SearchFilteredAsync(originals)).Rows).Text);
    }

    [Fact]
    public async Task AuthorFilterRestrictsToThatAuthor()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("wrath");
        filters.AuthorId = await db.AuthorIdForAsync("vergil");

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Equal("Vergil", Assert.Single(hits.Rows).AuthorName);
    }

    [Fact]
    public async Task CorpusFilterRestrictsToThatCollection()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("wrath");
        filters.Corpora.Add("engLit");

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Equal("the wrath of a prince", Assert.Single(hits.Rows).Text);
    }

    [Fact]
    public async Task TagFilterRestrictsToTaggedPassages()
    {
        using var db = await SeedLibraryAsync();

        var tags = new TagRepository();
        var tagId = await tags.GetOrCreateAsync("Achilles", "person");
        var nodeId = await db.ScalarAsync<long>(
            "SELECT TextNodeId FROM TextNodes WHERE Text LIKE '%wrath of Achilles%';");
        await tags.TagTextNodeAsync(nodeId, tagId);

        var filters = Query("wrath");
        filters.TagName = "Achilles";

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Equal("Sing, goddess, the wrath of Achilles", Assert.Single(hits.Rows).Text);
    }

    [Fact]
    public async Task BookmarkedFilterRestrictsToBookmarkedPassages()
    {
        using var db = await SeedLibraryAsync();

        var nodeId = await db.ScalarAsync<long>(
            "SELECT TextNodeId FROM TextNodes WHERE Text = 'the wrath of a prince';");
        await new BookmarkRepository().AddAsync(nodeId, "look at this");

        var filters = Query("wrath");
        filters.BookmarkedOnly = true;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Equal("the wrath of a prince", Assert.Single(hits.Rows).Text);
    }

    /// <summary>
    /// An era that matched no authors means no passages can qualify. That is
    /// a real answer and must not be mistaken for "no era filter set", which
    /// would silently widen the search to the whole library.
    /// </summary>
    [Fact]
    public async Task EraMatchingNoAuthorsReturnsNothing()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("wrath");
        filters.EraAuthorIds = new List<int>();

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Empty(hits.Rows);
    }

    [Fact]
    public async Task FiltersCombineAsAnd()
    {
        using var db = await SeedLibraryAsync();

        var filters = Query("wrath");
        filters.Languages.Add("eng");
        filters.AuthorId = await db.AuthorIdForAsync("shakespeare");

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Equal("the wrath of a prince", Assert.Single(hits.Rows).Text);
    }

    /// <summary>
    /// Narrowing has to happen before the limit, not after. Filtering a
    /// capped result set would give "the first N matches anywhere, of which
    /// a few happen to be Vergil" rather than "the first N in Vergil".
    /// </summary>
    [Fact]
    public async Task NarrowingIsAppliedBeforeTheResultLimit()
    {
        using var db = await SeedLibraryAsync();

        var noisy = await db.SeedFullEditionAsync("noise", "Anon", "greekLit", "Filler", "Original", "grc");
        await db.InsertLinesAsync(noisy,
            Enumerable.Range(0, 50).Select(i => ($"9.{i}", "wrath filler line")).ToArray());

        var filters = Query("wrath");
        filters.AuthorId = await db.AuthorIdForAsync("vergil");
        filters.MaxResults = 5;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        // Without SQL-side narrowing the 50 filler lines would consume the
        // budget and Vergil's single line would never appear.
        Assert.Equal("wrath of the gods in Latin", Assert.Single(hits.Rows).Text);
        Assert.False(hits.Truncated);
    }

    [Fact]
    public async Task TruncationIsReportedWhenTheLimitIsHit()
    {
        using var db = await SeedLibraryAsync();

        var noisy = await db.SeedFullEditionAsync("noise", "Anon", "greekLit", "Filler", "Original", "grc");
        await db.InsertLinesAsync(noisy,
            Enumerable.Range(0, 20).Select(i => ($"9.{i}", "wrath filler line")).ToArray());

        var filters = Query("wrath");
        filters.MaxResults = 5;

        var hits = await new TextNodeRepository().SearchFilteredAsync(filters);

        Assert.Equal(5, hits.Rows.Count);
        Assert.True(hits.Truncated);
        Assert.EndsWith("+", hits.DisplayCount);
    }

    /// <summary>
    /// A percent sign is a LIKE wildcard. Typed into the search box it has to
    /// mean itself, or "100%" quietly matches every line in the library.
    /// </summary>
    [Fact]
    public async Task WildcardCharactersInTheQueryAreLiteral()
    {
        using var db = await TempDatabase.CreateAsync();
        var edition = await db.SeedEditionAsync();
        await db.InsertLinesAsync(edition, ("1.1", "a plain line"), ("1.2", "100% certain"));

        var hits = await new TextNodeRepository().SearchFilteredAsync(Query("100%"));

        Assert.Equal("100% certain", Assert.Single(hits.Rows).Text);
    }
}
