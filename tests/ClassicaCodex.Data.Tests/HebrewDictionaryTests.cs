using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Looking Hebrew words up in Strong's dictionary.
///
/// Normalising takes the vowel points off, so words that differ only in their
/// pointing share one key - the noun "adversary" and the verb "to accuse" are
/// both שטנ. Both are offered, because a reader may want either; these tests
/// pin which comes first, and that loading the Hebrew dictionary leaves the
/// others alone.
/// </summary>
[Collection("Database")]
public class HebrewDictionaryTests
{
    private static Definition Entry(string headword, string entry) => new()
    {
        Headword = headword,
        NormalizedHeadword = WordNormalizer.NormalizeHeadword(headword, "hbo"),
        Language = "hbo",
        Entry = entry,
        Source = "Strong's"
    };

    [Fact]
    public async Task TheHeadwordAskedForComesBeforeItsHomographs()
    {
        using var db = await TempDatabase.CreateAsync();
        await new DefinitionRepository().BulkInsertAsync(new[]
        {
            Entry("שָׂטַן", "H7853 sâṭan (v). to attack, accuse"),
            Entry("שָׂטָן", "H7854 sâṭân (n-m). an opponent; especially Satan")
        });

        var entries = await new DefinitionRepository().GetByHeadwordAsync("שָׂטָן", "hbo");

        Assert.Equal(2, entries.Count);
        Assert.StartsWith("H7854", entries[0].Entry);
    }

    [Fact]
    public async Task ReloadingTheHebrewDictionaryLeavesTheOthersAlone()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new DefinitionRepository();
        await repo.BulkInsertAsync(new[]
        {
            Entry("מַלְאָךְ", "H4397 a messenger"),
            new Definition
            {
                Headword = "angelus", NormalizedHeadword = "angelus", Language = "lat",
                Entry = "a messenger", Source = "Lewis & Short"
            }
        });

        Assert.Equal(1, await repo.ClearByLanguageAsync("hbo"));

        Assert.Empty(await repo.GetByHeadwordAsync("מַלְאָךְ", "hbo"));
        Assert.Single(await repo.GetByHeadwordAsync("angelus", "lat"));
    }
}
