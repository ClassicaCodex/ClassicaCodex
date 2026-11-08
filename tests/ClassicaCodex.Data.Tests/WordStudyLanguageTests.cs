using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Word Study's occurrences, in a work that has a translation.
///
/// A lemma's paradigm is a list of spellings, and a spelling can also be a word
/// in another language. The Latin lemma data gives "can" as a form of cano, and
/// the Aeneid is one work with its Latin and its English translation together,
/// so Word Study on "Arma virumque cano" listed "Can gods such anger feel?" and
/// "Can Fate oppose?" among Vergil's own uses of the verb. The search now takes
/// the word's language and keeps to editions in it.
///
/// Both search paths are covered: the word index, and the LIKE scan a library
/// uses before its index is built.
/// </summary>
[Collection("Database")]
public class WordStudyLanguageTests
{
    private static readonly string[] CanoForms = { "cano", "canit", "can" };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OccurrencesKeepToTheWordsLanguage(bool buildIndex)
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAeneidAsync(db, buildIndex);

        var hits = await new TextNodeRepository().SearchByFormsAsync(CanoForms, 50, language: "lat");

        Assert.Equal(2, hits.Count);
        Assert.All(hits.Rows, row => Assert.DoesNotContain("Can ", row.Text, StringComparison.Ordinal));
    }

    /// <summary>
    /// The codes come from source files that do not agree on case.
    /// </summary>
    [Fact]
    public async Task TheLanguageIsComparedWithoutCase()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAeneidAsync(db, buildIndex: true);

        var hits = await new TextNodeRepository().SearchByFormsAsync(CanoForms, 50, language: "LAT");

        Assert.Equal(2, hits.Count);
    }

    /// <summary>
    /// Callers that name no language - auto-tagging, the places map - still
    /// get every edition, as before.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoLanguageStillSearchesEveryEdition(bool buildIndex)
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAeneidAsync(db, buildIndex);

        var hits = await new TextNodeRepository().SearchByFormsAsync(CanoForms, 50);

        Assert.Equal(3, hits.Count);
    }

    private static async Task SeedAeneidAsync(TempDatabase db, bool buildIndex)
    {
        await db.ExecuteAsync(
            @"INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace)
                VALUES (1, 'urn:cts:latinLit:phi0690', 'P. Vergilius Maro (Virgil)', 'latinLit');
              INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title)
                VALUES (1, 1, 'urn:cts:latinLit:phi0690.phi003', 'Aeneid');
              INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language)
                VALUES (1, 1, 'phi0690.phi003.perseus-lat2', 'Original', 'lat'),
                       (2, 1, 'phi0690.phi003.perseus-eng2', 'Translation', 'eng');");

        var lines = new (int Edition, string Text)[]
        {
            (1, "Arma virumque cano, Troiae qui primus ab oris"),
            (1, "Hic canit errantem lunam solisque labores;"),
            (2, "was serving Heaven? Can gods such anger feel?"),
        };

        for (var i = 0; i < lines.Length; i++)
        {
            var nodeId = i + 1;
            await db.ExecuteAsync(
                "INSERT INTO TextNodes (TextNodeId, EditionId, CitationRef, SortOrder, Text) " +
                $"VALUES ({nodeId}, {lines[i].Edition}, '1.{nodeId}', {i}, {Quoted(lines[i].Text)});");

            if (!buildIndex) continue;

            foreach (var word in lines[i].Text
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                         .Select(WordNormalizer.Normalize)
                         .Where(w => w.Length > 0)
                         .Distinct(StringComparer.Ordinal))
            {
                await db.ExecuteAsync(
                    "INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) " +
                    $"VALUES ({Quoted(word)}, {nodeId});");
            }
        }
    }

    private static string Quoted(string value) => "'" + value.Replace("'", "''") + "'";
}
