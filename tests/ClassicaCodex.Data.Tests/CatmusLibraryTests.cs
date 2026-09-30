using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Catmus;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Storing, searching and picturing CATMuS lines.
///
/// Nothing here touches the network: the dataset is 24.7 GiB behind somebody
/// else's server, and what is worth pinning is what this application does
/// with a line once it has one.
/// </summary>
[Collection("Database")]
public class CatmusLibraryTests
{
    private static CatmusLine Line(string shard, int row, string text, string? region = "MainZone") =>
        new() { ShardFile = shard, RowIndex = row, Text = text, Region = region, LineType = "DefaultLine" };

    private static async Task<CatmusRepository> SeedAsync(params CatmusLine[] lines)
    {
        var repo = new CatmusRepository();
        await repo.ReplaceShardAsync(
            "Liege, Archives de l'État, T51.12", "Latin", 12, "Gothic Documentary Script",
            "Documents of practice", "prose", "HTRogène", "shard-a.parquet", lines);
        return repo;
    }

    [Fact]
    public async Task AShardArrivesAsAManuscriptWithItsLines()
    {
        using var db = await TempDatabase.CreateAsync();

        var repo = await SeedAsync(
            Line("shard-a.parquet", 0, "tẽ dñicalis t̾re nr̃e apud uos ꝯcessisse."),
            Line("shard-a.parquet", 1, "nr̃a ei auctoritate ꝯcedimꝰ."));

        var holdings = await repo.GetHoldingsAsync();
        var held = Assert.Single(holdings);

        Assert.Equal("Liege, Archives de l'État, T51.12", held.Shelfmark);
        Assert.Equal("Gothic Documentary Script", held.ScriptType);
        Assert.Equal(12, held.Century);
        Assert.Equal(2, held.LineCount);
        Assert.False(held.HasImages);

        var lines = await repo.GetLinesAsync(held.ManuscriptId);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Liege, Archives de l'État, T51.12", lines[0].Shelfmark);
    }

    /// <summary>
    /// Running the step twice must not double the library.
    ///
    /// The download is 346 files and takes minutes, so it will be
    /// interrupted and re-run - and a shard re-read has to replace its own
    /// rows rather than add a second copy of them.
    /// </summary>
    [Fact]
    public async Task ReReadingAShardReplacesItsLinesRatherThanRepeatingThem()
    {
        using var db = await TempDatabase.CreateAsync();

        var repo = await SeedAsync(
            Line("shard-a.parquet", 0, "first"),
            Line("shard-a.parquet", 1, "second"));

        await repo.ReplaceShardAsync(
            "Liege, Archives de l'État, T51.12", "Latin", 12, "Gothic Documentary Script",
            "Documents of practice", "prose", "HTRogène", "shard-a.parquet",
            new[] { Line("shard-a.parquet", 0, "first"), Line("shard-a.parquet", 1, "second") });

        Assert.Equal(2, await repo.CountLinesAsync());
        Assert.Equal(2, (await repo.GetHoldingsAsync()).Single().LineCount);
    }

    /// <summary>
    /// A second shard of the same manuscript joins it rather than creating a
    /// second manuscript - Ghent UL 1374 arrives in four files and is one
    /// book.
    /// </summary>
    [Fact]
    public async Task ASecondShardOfTheSameManuscriptJoinsIt()
    {
        using var db = await TempDatabase.CreateAsync();

        var repo = await SeedAsync(Line("shard-a.parquet", 0, "part one"));
        await repo.ReplaceShardAsync(
            "Liege, Archives de l'État, T51.12", "Latin", 12, "Gothic Documentary Script",
            "Documents of practice", "prose", "HTRogène", "shard-b.parquet",
            new[] { Line("shard-b.parquet", 0, "part two") });

        var held = Assert.Single(await repo.GetHoldingsAsync());
        Assert.Equal(2, held.LineCount);

        var shards = await repo.GetIngestedShardsAsync();
        Assert.Equal(new[] { "shard-a.parquet", "shard-b.parquet" }, shards.OrderBy(s => s));
    }

    /// <summary>
    /// <b>The folded search is the one that makes this collection reachable.</b>
    ///
    /// A diplomatic transcription writes what the scribe wrote, which is full
    /// of marks over and through the letters: "nr̃e" for nostre, "t̾re" for
    /// terre, "ſtet" with a long s. Nobody types those. Folding the stored
    /// text and the query the same way - the same fold the Middle High German
    /// and Menota collections depend on - is what lets an ordinary spelling
    /// find them.
    /// </summary>
    [Theory]
    [InlineData("nre")]      // the mark is over the r
    [InlineData("nr̃e")]      // and the marked form still finds itself
    [InlineData("NRE")]      // and case does not matter
    public async Task TheFoldedSearchReachesAMarkedSpelling(string query)
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(Line("shard-a.parquet", 0, "tẽ dñicalis t̾re nr̃e apud uos ꝯcessisse."));

        var hits = await repo.SearchAsync(query, matchRawCharacters: false);
        Assert.Single(hits);
    }

    /// <summary>
    /// The long s, which is the same letter in a different shape and the
    /// commonest thing in this material that an ordinary keyboard cannot
    /// produce.
    /// </summary>
    [Fact]
    public async Task TheFoldedSearchReachesALongS()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(Line("shard-a.parquet", 0, "ſtet in uia"));

        Assert.Single(await repo.SearchAsync("stet", matchRawCharacters: false));
    }

    /// <summary>
    /// And the raw search is the other question: where did a scribe write
    /// this sign. The Tironian et is punctuation, so the fold drops it
    /// entirely and only the raw search can find it.
    /// </summary>
    [Fact]
    public async Task OnlyTheRawSearchFindsASignThatIsNotALetter()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(
            Line("shard-a.parquet", 0, "⁊met si haut et si lenfiche"),
            Line("shard-a.parquet", 1, "sanz nule chose"));

        Assert.Single(await repo.SearchAsync("⁊", matchRawCharacters: true));
        Assert.Empty(await repo.SearchAsync("⁊", matchRawCharacters: false));
    }

    /// <summary>
    /// LIKE reads _ and % as wildcards. A reader searching for a literal
    /// underscore would otherwise match every line with any character in that
    /// position, which is every line.
    /// </summary>
    [Fact]
    public async Task WildcardCharactersInAQueryAreLiteral()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(
            Line("shard-a.parquet", 0, "a_b"),
            Line("shard-a.parquet", 1, "axb"));

        var hits = await repo.SearchAsync("a_b", matchRawCharacters: true);
        Assert.Equal("a_b", Assert.Single(hits).Text);
    }

    [Fact]
    public async Task SearchNarrowsByScriptAndCentury()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(Line("shard-a.parquet", 0, "notum esse uolumus"));

        await repo.ReplaceShardAsync(
            "Paris, BnF, fr. 1593", "French", 13, "Textualis",
            "Narratives", "verse", "Biblissima+ Fabliaux", "shard-c.parquet",
            new[] { Line("shard-c.parquet", 0, "notum esse uolumus") });

        Assert.Equal(2, (await repo.SearchAsync("notum", false)).Count);
        Assert.Single(await repo.SearchAsync("notum", false, scriptType: "Textualis"));
        Assert.Single(await repo.SearchAsync("notum", false, century: 12));
        Assert.Empty(await repo.SearchAsync("notum", false, century: 15));
    }

    /// <summary>
    /// The photographs live in a pack beside the library and the lines hold
    /// their addresses in it. This is the whole read path: offsets go in, the
    /// right bytes come back out.
    /// </summary>
    [Fact]
    public async Task ALinePicksItsOwnPhotographOutOfThePack()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(
            Line("shard-a.parquet", 0, "first line"),
            Line("shard-a.parquet", 1, "second line"));

        var root = Path.Combine(Path.GetTempPath(), "ccx-catmus-pack-" + Guid.NewGuid().ToString("N"));
        var pack = CatmusImageDownloadService.PackFileName("Liege, Archives de l'État, T51.12");

        var first = new byte[] { 1, 2, 3, 4 };
        var second = new byte[] { 9, 8, 7 };

        Directory.CreateDirectory(Path.Combine(root, CatmusImageDownloadService.ImagesFolder));
        await File.WriteAllBytesAsync(
            CatmusImageDownloadService.PackPath(root, pack), first.Concat(second).ToArray());

        try
        {
            await repo.SetImagePackAsync("Liege, Archives de l'État, T51.12", pack,
                new Dictionary<(string, int), (long, int)>
                {
                    [("shard-a.parquet", 0)] = (0, first.Length),
                    [("shard-a.parquet", 1)] = (first.Length, second.Length)
                });

            var held = Assert.Single(await repo.GetHoldingsAsync());
            Assert.True(held.HasImages);

            var lines = await repo.GetLinesAsync(held.ManuscriptId);
            Assert.All(lines, l => Assert.True(l.HasImage));

            Assert.Equal(first, await CatmusImageDownloadService.ReadImageAsync(root, lines[0]));
            Assert.Equal(second, await CatmusImageDownloadService.ReadImageAsync(root, lines[1]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A data folder that has moved, or been cleared, gives a line with no
    /// picture rather than an exception in the middle of the viewer.
    /// </summary>
    [Fact]
    public async Task AMissingPackIsNoPictureRatherThanAFailure()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(Line("shard-a.parquet", 0, "a line"));

        await repo.SetImagePackAsync("Liege, Archives de l'État, T51.12", "gone.pack",
            new Dictionary<(string, int), (long, int)> { [("shard-a.parquet", 0)] = (0, 10) });

        var held = Assert.Single(await repo.GetHoldingsAsync());
        var line = (await repo.GetLinesAsync(held.ManuscriptId))[0];

        Assert.Null(await CatmusImageDownloadService.ReadImageAsync(
            Path.Combine(Path.GetTempPath(), "ccx-catmus-not-here"), line));
    }

    /// <summary>
    /// Re-reading a manuscript's transcriptions must not lose the
    /// photographs somebody already spent a gigabyte fetching.
    /// </summary>
    [Fact]
    public async Task ReReadingTheTextKeepsThePhotographs()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = await SeedAsync(Line("shard-a.parquet", 0, "a line"));

        await repo.SetImagePackAsync("Liege, Archives de l'État, T51.12", "some.pack",
            new Dictionary<(string, int), (long, int)> { [("shard-a.parquet", 0)] = (0, 10) });

        await repo.ReplaceShardAsync(
            "Liege, Archives de l'État, T51.12", "Latin", 12, "Gothic Documentary Script",
            "Documents of practice", "prose", "HTRogène", "shard-b.parquet",
            new[] { Line("shard-b.parquet", 0, "another line") });

        Assert.True(Assert.Single(await repo.GetHoldingsAsync()).HasImages);
    }

    /// <summary>
    /// Shelfmarks carry commas, full stops and slashes; a pack name cannot.
    /// Two manuscripts must not collapse onto one file, because the second
    /// download would then overwrite the first and both would point at it.
    /// </summary>
    [Fact]
    public void PackNamesAreFileNamesAndStayDistinct()
    {
        var names = CatmusCatalogue.Manuscripts
            .Select(m => CatmusImageDownloadService.PackFileName(m.Shelfmark))
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var invalid = Path.GetInvalidFileNameChars();
        Assert.All(names, n => Assert.False(n.Any(invalid.Contains), n));

        Assert.Equal("paris-bnf-fr-1593.pack", CatmusImageDownloadService.PackFileName("Paris, BnF, fr. 1593"));
    }
}
