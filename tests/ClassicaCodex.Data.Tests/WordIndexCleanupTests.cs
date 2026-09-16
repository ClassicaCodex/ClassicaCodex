using System.Diagnostics;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Removing an edition has to remove its word-index entries, and has to do it
/// by naming them.
///
/// <b>What went wrong.</b> WordIndex is WITHOUT ROWID keyed (NormalizedWord,
/// TextNodeId), so there is no access path by line: a WHERE on TextNodeId is
/// answered with a skip-scan that probes once per distinct word in the whole
/// index. Both of the repository's removal paths used one, and that was judged
/// acceptable because a whole-edition re-ingest "happens once".
///
/// It does not. A Menota manuscript is a single file holding many works, and
/// the importer clears and rewrites one edition per work - Holm-A-80.xml
/// carries 275 of them. Importing that one file paid 275 skip-scans of a
/// twenty-six-million-row index. Nothing hung; it was going to finish, in
/// hours, while reporting the same filename throughout, which is why it was
/// reported as the import stalling at 44 of 91.
///
/// The rows are now deleted by primary key. These tests check the part that
/// can go wrong when you do that: rows left behind because the deleting code
/// tokenized the text differently from the code that inserted it.
/// </summary>
[Collection("Database")]
public class WordIndexCleanupTests
{
    private const string SomeGreek = "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος";
    private const string SomeLatin = "Arma virumque cano, Troiae qui primus ab oris";

    /// <summary>
    /// A line with nothing indexable in it. WordIndexService records an
    /// empty-word marker row for one of these, and a deletion that does not
    /// know about the marker leaves it behind.
    /// </summary>
    private const string NoWords = "-- ,,, ---";

    private static async Task<int> SeedEditionAsync(params string[] lines)
    {
        var authorId = await new AuthorRepository().UpsertAsync(new Author
        {
            CtsUrn = "urn:cts:greekLit:tlg0012", Name = "Homer", Namespace = "greekLit"
        });

        var workId = await new WorkRepository().UpsertAsync(new Work
        {
            AuthorId = authorId, CtsUrn = "urn:cts:greekLit:tlg0012.tlg001", Title = "Iliad"
        });

        var editionId = await new EditionRepository().UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = "urn:cts:greekLit:tlg0012.tlg001.test",
            Kind = EditionKind.Original,
            Language = "grc",
            SourcePath = @"C:\data\iliad.xml"
        });

        await new TextNodeRepository().BulkInsertAsync(lines.Select((text, i) => new TextNode
        {
            EditionId = editionId,
            CitationRef = $"1.{i + 1}",
            SortOrder = i,
            Text = text
        }).ToList());

        // Built by the real service, so the rows under test are the rows the
        // application actually writes - including the marker.
        await new WordIndexService().BuildAsync();

        return editionId;
    }

    private static async Task<long> IndexRowsForEditionAsync(TempDatabase db, int editionId) =>
        await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM WordIndex WHERE TextNodeId IN "
            + $"(SELECT TextNodeId FROM TextNodes WHERE EditionId = {editionId});");

    [Fact]
    public async Task DeletingAnEditionTakesItsWordIndexWithIt()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await SeedEditionAsync(SomeGreek, SomeLatin, NoWords);

        Assert.True(await IndexRowsForEditionAsync(db, editionId) > 0,
            "nothing was indexed, so this test would pass against any deletion at all");

        await new EditionRepository().DeleteEditionAsync(editionId);

        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM WordIndex;"));
    }

    /// <summary>
    /// The same for the other path, which is the one the Menota importer runs
    /// once per work - 275 times for a single manuscript.
    /// </summary>
    [Fact]
    public async Task ClearingAnEditionsTextTakesItsWordIndexWithIt()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await SeedEditionAsync(SomeGreek, SomeLatin, NoWords);

        Assert.True(await IndexRowsForEditionAsync(db, editionId) > 0);

        await new EditionRepository().ClearTextNodesAsync(editionId);

        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM WordIndex;"));
    }

    /// <summary>
    /// The line that contributes no words still gets its marker row removed.
    /// Asserted on its own because it is the one row a tokenizer-based
    /// deletion naturally misses - TokenizeLine returns nothing for it, so
    /// only code that knows the marker exists will delete it.
    /// </summary>
    [Fact]
    public async Task TheMarkerRowForAWordlessLineIsRemovedToo()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await SeedEditionAsync(NoWords);

        var before = await db.ScalarAsync<long>("SELECT COUNT(*) FROM WordIndex WHERE NormalizedWord = '';");
        Assert.True(before > 0, "no marker row was written, so this test is guarding nothing - "
                                + "has WordIndexService stopped recording wordless lines?");

        await new EditionRepository().DeleteEditionAsync(editionId);

        Assert.Equal(0L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM WordIndex WHERE NormalizedWord = '';"));
    }

    /// <summary>
    /// <b>The limit of deleting by key, written down rather than discovered.</b>
    ///
    /// Naming the rows means the cleanup can only remove rows the line's text
    /// can produce. A row whose word is not in that text survives - and this
    /// test asserts that it does, so the trade is recorded rather than
    /// implied.
    ///
    /// Why it is an acceptable trade. In every path the application takes, a
    /// line's index rows ARE the tokens of that line's text: the builder
    /// tokenizes the text to insert them, an edit re-syncs them through the
    /// old text read back out of the database, and a re-ingest clears the
    /// edition while the database still holds the text the index was built
    /// from. The state below is reachable only if the index has already
    /// drifted from the text - and the alternative was a skip-scan of the
    /// whole index per edition, measured at 12.6 seconds for an edition of
    /// zero lines on a full library, which the Menota importer paid 275 times
    /// for one manuscript.
    ///
    /// A row left this way is invisible to search, because search joins index
    /// entries back to passages and one pointing at a deleted passage drops
    /// out. Rebuild Word Index clears any that accumulate. If that ever stops
    /// being good enough, the fix is an index on WordIndex(TextNodeId), which
    /// buys exactness back for roughly 250 MB and a slower full build.
    /// </summary>
    [Fact]
    public async Task ARowTheTextCannotProduceIsLeftBehind()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await SeedEditionAsync(SomeLatin);

        var node = await db.ScalarAsync<long>(
            $"SELECT MIN(TextNodeId) FROM TextNodes WHERE EditionId = {editionId};");

        // A word that is not in the line. Nothing in the application writes
        // one; this is the drifted state, staged on purpose.
        await db.ExecuteAsync(
            $"INSERT INTO WordIndex (NormalizedWord, TextNodeId) VALUES ('notinthisline', {node});");

        await new EditionRepository().ClearTextNodesAsync(editionId);

        Assert.Equal(1L, await db.ScalarAsync<long>("SELECT COUNT(*) FROM WordIndex;"));
        Assert.Equal("notinthisline",
            await db.ScalarStringAsync("SELECT NormalizedWord FROM WordIndex LIMIT 1;"));
    }

    /// <summary>
    /// The deletion must not depend on how many OTHER editions are indexed.
    ///
    /// This is the shape of the original defect rather than its size: a
    /// skip-scan costs what the whole index costs, so clearing a three-line
    /// edition got slower as the library grew. Naming the rows costs what the
    /// edition costs and nothing else.
    ///
    /// Asserted as a ratio rather than a wall-clock budget, because a
    /// threshold in milliseconds is a test that fails on a busy machine. Even
    /// so it is generous: the original took minutes here, not double.
    /// </summary>
    [Fact]
    public async Task ClearingIsNotSlowedDownByTheRestOfTheLibrary()
    {
        using var db = await TempDatabase.CreateAsync();

        var small = await SeedEditionAsync(SomeGreek);
        var alone = Stopwatch.StartNew();
        await new EditionRepository().ClearTextNodesAsync(small);
        alone.Stop();

        // A few thousand more indexed lines from another edition, which a
        // skip-scan would have to walk and a key lookup never touches.
        var crowdWorkId = await new WorkRepository().UpsertAsync(new Work
        {
            AuthorId = (await new AuthorRepository().GetAllAsync()).First().AuthorId,
            CtsUrn = "urn:cts:greekLit:tlg0012.tlg002",
            Title = "Odyssey"
        });

        var crowdEdition = await new EditionRepository().UpsertAsync(new Edition
        {
            WorkId = crowdWorkId,
            CtsUrn = "urn:cts:greekLit:tlg0012.tlg002.test",
            Kind = EditionKind.Original,
            Language = "grc",
            SourcePath = @"C:\data\odyssey.xml"
        });

        await new TextNodeRepository().BulkInsertAsync(Enumerable.Range(0, 3000).Select(i => new TextNode
        {
            EditionId = crowdEdition,
            CitationRef = $"2.{i}",
            SortOrder = i,
            Text = $"ἄνδρα μοι ἔννεπε μοῦσα πολύτροπον ὃς μάλα πολλά word{i} filler{i % 97}"
        }).ToList());

        var second = await SeedEditionAsync(SomeGreek);

        var crowded = Stopwatch.StartNew();
        await new EditionRepository().ClearTextNodesAsync(second);
        crowded.Stop();

        Assert.True(
            crowded.ElapsedMilliseconds <= Math.Max(250, alone.ElapsedMilliseconds * 8),
            $"clearing a one-line edition took {alone.ElapsedMilliseconds}ms in an empty library and "
            + $"{crowded.ElapsedMilliseconds}ms once another edition was indexed. The cost is following "
            + "the size of the index rather than the size of the edition - see this test's summary.");
    }

    /// <summary>
    /// The two halves have to agree about what a line's words are, or a
    /// deletion silently orphans rows instead of removing them. They agree by
    /// being the same method; this asserts that they still are.
    /// </summary>
    [Fact]
    public async Task TheIndexBuilderAndTheIndexCleanerTokenizeIdentically()
    {
        using var db = await TempDatabase.CreateAsync();
        var editionId = await SeedEditionAsync(SomeGreek, SomeLatin, NoWords);

        // What the BUILDER actually put in the index, per line.
        var written = new List<(long Node, string Word)>();

        await using (var conn = await DbConnectionFactory.OpenConnectionAsync())
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT wi.TextNodeId, wi.NormalizedWord FROM WordIndex wi "
                + "JOIN TextNodes tn ON tn.TextNodeId = wi.TextNodeId "
                + $"WHERE tn.EditionId = {editionId};";

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) written.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        Assert.NotEmpty(written);

        // What the CLEANER would offer for deletion, per line, from the same
        // text. Every written row has to be in that set, or deleting an
        // edition leaves rows behind - which is the whole risk of deleting by
        // key instead of by line.
        var offered = new HashSet<(long, string)>();

        foreach (var line in await new TextNodeRepository().GetByEditionAsync(editionId))
        {
            foreach (var token in WordNormalizer.TokenizeLine(line.Text)) offered.Add((line.TextNodeId, token));
            offered.Add((line.TextNodeId, string.Empty));
        }

        var orphans = written.Where(w => !offered.Contains(w)).ToList();

        Assert.True(orphans.Count == 0,
            "these rows were written by the index builder and would NOT be offered for deletion, "
            + "so removing the edition would orphan them: "
            + string.Join(", ", orphans.Take(8).Select(o => $"({o.Node}, \"{o.Word}\")")));
    }
}
