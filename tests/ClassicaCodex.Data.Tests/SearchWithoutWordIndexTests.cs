using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Searching a library that has texts but no word index yet.
///
/// This is not an exotic state. Ingesting a corpus does not build the word
/// index - no ingest service touches WordIndex at all. The index is built by
/// the Word Index window, or by a setup wizard step whose own text invites
/// you to skip it. So every library is in this state between its first
/// ingest and its first index build, and some stay there.
///
/// In that state the search repository falls back from the index to a plain
/// LIKE, and that fallback carried a one-character bug for as long as it has
/// existed: the clause was built in an interpolated string as
/// <c>ESCAPE '\'</c>, where <c>\'</c> is an escaped apostrophe, so what
/// reached SQLite was <c>ESCAPE ''</c>. SQLite rejects an empty escape
/// expression while preparing the statement, so the query threw before it
/// could return a row.
///
/// It surfaced as Auto-Tag's "Search Corpus" doing nothing at all - status
/// stuck on "Searching...", no results, an error dialog - and the same
/// fallback is shared by Word Study's occurrence search.
///
/// These tests exercise the fallback specifically, by leaving WordIndex
/// empty. Nothing else in the suite does, which is why a bug on the line was
/// invisible to 1,050 passing tests.
/// </summary>
[Collection("Database")]
public class SearchWithoutWordIndexTests
{
    [Fact]
    public async Task SearchByForms_WorksWhenTheIndexHasNotBeenBuilt()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedOneLineAsync(db, "Then Achilles answered him and said");

        // Nothing has written to WordIndex, exactly as after a plain ingest.
        Assert.False(await new WordIndexRepository().HasDataAsync());

        var hits = await new TextNodeRepository().SearchByFormsAsync(new[] { "Achilles" }, 50);

        Assert.Equal(1, hits.Count);
        Assert.Contains("Achilles", hits.Rows[0].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the escape character is actually for. The LIKE here is only a
    /// prefilter - the real matching is a normalized pass in C# afterwards -
    /// so the escape does not decide whether a literal "%" is findable. What
    /// it decides is whether a "%" in the term turns the prefilter into
    /// "match everything", which is the wall of unrelated results the helper
    /// above it warns about.
    /// </summary>
    [Fact]
    public async Task SearchByForms_DoesNotLetAWildcardInTheTermMatchEverything()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedOneLineAsync(db, "Sing, goddess, the wrath of Peleus' son");

        // Unescaped, "a%n" is "a, anything, n" and matches the line above.
        // Escaped, it is three literal characters and matches nothing.
        var hits = await new TextNodeRepository().SearchByFormsAsync(new[] { "a%n" }, 50);

        Assert.Equal(0, hits.Count);
    }

    [Fact]
    public async Task SearchByForms_FindsNothingWithoutThrowingWhenThereIsNoMatch()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedOneLineAsync(db, "Sing, goddess, the wrath of Peleus' son");

        var hits = await new TextNodeRepository().SearchByFormsAsync(new[] { "Hannibal" }, 50);

        Assert.Equal(0, hits.Count);
    }

    private static async Task SeedOneLineAsync(TempDatabase db, string text)
    {
        await db.ExecuteAsync(
            @"INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace)
                VALUES (1, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit');
              INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title)
                VALUES (1, 1, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
              INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language)
                VALUES (1, 1, 'tlg0012.tlg001.perseus-eng1', 'Translation', 'eng');");

        await db.ExecuteAsync(
            "INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES (1, '1.1', 0, $text);"
                .Replace("$text", "'" + text.Replace("'", "''") + "'"));
    }
}
