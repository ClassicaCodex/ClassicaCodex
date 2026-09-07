using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Clicking a pin on the places map, when the pin's name is more than one
/// word.
///
/// 3.6.5 replaced the map's substring search with a word-index search, because
/// a substring search for a short name matched the letters wherever they fell
/// - clicking Ur returned five thousand passages of which one mentioned Ur,
/// the rest being "during", "figure" and "purple".
///
/// That fix took every multi-word name to nothing at all. Normalization keeps
/// only letters, so the space vanished and "Euxine sea" reached the index as
/// the single token "euxinesea", which no line contains. Eight of the map's
/// 240 pins - the Euxine sea, the Arabian Gulf, Egyptian Thebes, lake Moeris,
/// Hippo Regius, Colonia Agrippina, Monte Cassino and Boeotian Thebes -
/// answered a click with an empty list where they had previously answered with
/// real mentions: 86, 57, 13, 12, 9, 3, 2 and 2 of them.
///
/// So there are two ways to be wrong here and a test for each: matching the
/// letters, and matching nothing.
/// </summary>
[Collection("Database")]
public class PlaceNameSearchTests
{
    /// <summary>The regression itself: this returned nothing in 3.6.5.</summary>
    [Fact]
    public async Task AMultiWordNameFindsThePassagesThatMentionIt()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true,
            "They sailed out into the Euxine sea and turned east",
            "The sea was calm that morning",
            "Euxine is the older name for the same water");

        var hits = await new TextNodeRepository().SearchPhraseAsync("Euxine sea", 50);

        Assert.Equal(1, hits.Count);
        Assert.Contains("Euxine sea", hits.Rows[0].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every name that actually lost results on this library, so a future
    /// change to normalization cannot quietly take them away again.
    /// </summary>
    [Theory]
    [InlineData("Euxine sea", "They sailed into the Euxine sea at dawn")]
    [InlineData("Arabian Gulf", "ships from the Arabian Gulf came laden")]
    [InlineData("Egyptian Thebes", "the priests of Egyptian Thebes recorded it")]
    [InlineData("lake Moeris", "the water of lake Moeris rises yearly")]
    [InlineData("Hippo Regius", "he was made bishop at Hippo Regius")]
    [InlineData("Colonia Agrippina", "the legions wintered at Colonia Agrippina")]
    [InlineData("Monte Cassino", "the house at Monte Cassino was founded then")]
    [InlineData("Boeotian Thebes", "an embassy came from Boeotian Thebes")]
    public async Task EveryNameThatWentSilentFindsItsLineAgain(string placeName, string line)
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true, line, "an unrelated line about ships and water");

        var hits = await new TextNodeRepository().SearchPhraseAsync(placeName, 50);

        Assert.Equal(1, hits.Count);
    }

    /// <summary>
    /// Why this method exists, kept as a test so the reason cannot be
    /// forgotten. The forms search is not broken - ORing alternative
    /// spellings of one word is exactly its contract - but a name is not a
    /// set of spellings, and handing it one collapses the space and produces
    /// a token no line can contain.
    ///
    /// Both halves matter: without the second assertion this test would pass
    /// against the very bug it describes.
    /// </summary>
    [Fact]
    public async Task TheFormsSearchThisReplacedFindsNothingForATwoWordName()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true,
            "They sailed out into the Euxine sea and turned east");

        var repo = new TextNodeRepository();

        Assert.Equal(0, (await repo.SearchByFormsAsync(new[] { "Euxine sea" }, 50)).Count);
        Assert.Equal(1, (await repo.SearchPhraseAsync("Euxine sea", 50)).Count);
    }

    /// <summary>
    /// The other way to be wrong. Requiring both words but not the phrase
    /// would call this a mention of Egyptian Thebes; it is not one.
    /// </summary>
    [Fact]
    public async Task BothWordsFarApartIsNotAMention()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true,
            "The Egyptian fleet was scattered, and long afterwards Thebes fell");

        var hits = await new TextNodeRepository().SearchPhraseAsync("Egyptian Thebes", 50);

        Assert.Equal(0, hits.Count);
    }

    /// <summary>
    /// What 3.6.5 was fixing must stay fixed: a one-word name matches whole
    /// words, not the letters inside longer ones.
    /// </summary>
    [Fact]
    public async Task AOneWordNameStillDoesNotMatchTheLettersInsideOtherWords()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true,
            "The city of Ur stood on the plain",
            "It happened during the night",
            "a purple figure came out of the dark");

        var hits = await new TextNodeRepository().SearchPhraseAsync("Ur", 50);

        Assert.Equal(1, hits.Count);
        Assert.Contains("city of Ur", hits.Rows[0].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every library is without a word index between its first ingest and its
    /// first index build, and the map is clickable throughout.
    /// </summary>
    [Fact]
    public async Task AMultiWordNameWorksBeforeTheIndexIsBuilt()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: false,
            "They sailed out into the Euxine sea and turned east",
            "The sea was calm that morning");

        Assert.False(await new WordIndexRepository().HasDataAsync());

        var hits = await new TextNodeRepository().SearchPhraseAsync("Euxine sea", 50);

        Assert.Equal(1, hits.Count);
    }

    /// <summary>
    /// The phrase goes into a LIKE, so a wildcard inside a name must be a
    /// literal character rather than "match anything".
    /// </summary>
    [Fact]
    public async Task AWildcardInTheNameMatchesItselfAndNotEverything()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true,
            "the a% n marker stands here",
            "the a and n marker stands here");

        var hits = await new TextNodeRepository().SearchPhraseAsync("a% n", 50);

        Assert.Equal(1, hits.Count);
        Assert.Contains("a% n", hits.Rows[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("- , -")]
    public async Task ANameWithNoWordsInItFindsNothingRatherThanEverything(string placeName)
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db, buildIndex: true, "They sailed out into the Euxine sea");

        var hits = await new TextNodeRepository().SearchPhraseAsync(placeName, 50);

        Assert.Equal(0, hits.Count);
    }

    /// <summary>
    /// Seeds lines, and optionally the word-index rows an index build would
    /// have written for them - tokenized exactly as WordIndexService does, so
    /// the test cannot pass against an index the application would not have
    /// produced.
    /// </summary>
    private static async Task SeedAsync(TempDatabase db, bool buildIndex, params string[] lines)
    {
        await db.ExecuteAsync(
            @"INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace)
                VALUES (1, 'urn:cts:greekLit:tlg0016', 'Herodotus', 'greekLit');
              INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title)
                VALUES (1, 1, 'urn:cts:greekLit:tlg0016.tlg001', 'Histories');
              INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language)
                VALUES (1, 1, 'tlg0016.tlg001.perseus-eng1', 'Translation', 'eng');");

        for (var i = 0; i < lines.Length; i++)
        {
            var nodeId = i + 1;
            await db.ExecuteAsync(
                "INSERT INTO TextNodes (TextNodeId, EditionId, CitationRef, SortOrder, Text) " +
                $"VALUES ({nodeId}, 1, '1.{nodeId}', {i}, {Quoted(lines[i])});");

            if (!buildIndex) continue;

            var words = lines[i]
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(WordNormalizer.Normalize)
                .Where(w => w.Length > 0 && w.Length <= 200)
                .Distinct(StringComparer.Ordinal);

            foreach (var word in words)
            {
                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) " +
                    $"VALUES ({Quoted(word)}, {nodeId});");
            }
        }
    }

    private static string Quoted(string value) => "'" + value.Replace("'", "''") + "'";
}
