using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The marks shown at the end of a line - an inquiry, a tag, a bookmark.
///
/// Gathered per edition in one query because the reader needs them for every
/// line it draws and a work can run to tens of thousands of lines. Keyed on the
/// citation reference rather than the node id, which is what makes them survive
/// a re-ingest along with the annotations they stand for.
/// </summary>
[Collection("Database")]
public class PassageMarkTests
{
    [Fact]
    public void SuffixIsEmptyForAnUnmarkedPassage()
    {
        Assert.Equal(string.Empty, PassageMarkSymbols.Suffix(PassageMarks.None));
    }

    /// <summary>
    /// A fixed order, so a line carrying two marks looks the same wherever it
    /// appears - and so the run never ends on the question mark, which after a
    /// sentence would read as punctuation rather than as a mark.
    /// </summary>
    [Fact]
    public void MarksAppearInAFixedOrderWhateverOrderTheyWereSetIn()
    {
        var all = PassageMarks.Bookmark | PassageMarks.Inquiry | PassageMarks.Tag;

        Assert.Equal("   ? # ★", PassageMarkSymbols.Suffix(all));
        Assert.EndsWith(PassageMarkSymbols.Bookmark, PassageMarkSymbols.Suffix(all));

        Assert.Equal("   ? ★", PassageMarkSymbols.Suffix(PassageMarks.Inquiry | PassageMarks.Bookmark));
        Assert.Equal("   #", PassageMarkSymbols.Suffix(PassageMarks.Tag));
    }

    /// <summary>
    /// All three sources read in one pass, and combined rather than counted -
    /// a passage tagged three times is still one hash.
    /// </summary>
    [Fact]
    public async Task EveryKindOfMarkIsGatheredForTheEdition()
    {
        using var db = await TempDatabase.CreateAsync();

        const string editionUrn = "urn:e:test1";

        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"), ("1.2", "οὐλομένην"), ("1.3", "πολλὰς"));

        var tags = new TagRepository();
        var wrath = await tags.GetOrCreateAsync("wrath", null);
        var anger = await tags.GetOrCreateAsync("anger", null);

        // 1.1 gets everything; 1.2 two tags, which must still read as one mark;
        // 1.3 nothing at all.
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.1"), wrath);
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.2"), wrath);
        await tags.TagTextNodeAsync(await db.TextNodeIdAsync(editionId, "1.2"), anger);
        await new BookmarkRepository().AddAsync(await db.TextNodeIdAsync(editionId, "1.1"), "here");

        await db.ExecuteAsync($@"
            INSERT INTO PassageInquiries
                (WorkCtsUrn, EditionCtsUrn, CitationRef, AuthorName, WorkTitle, Excerpt,
                 AttentionNote, DraftQuestion, Direction, CreatedUtc, UpdatedUtc)
            VALUES ('w', '{editionUrn}', '1.1', 'Homer', 'Iliad', 'μῆνιν',
                    'the first word', 'why wrath', 'none', '2026-01-01', '2026-01-01');");

        var marks = await new PassageMarkRepository().GetForEditionAsync(editionId, editionUrn);

        Assert.Equal(PassageMarks.Inquiry | PassageMarks.Tag | PassageMarks.Bookmark, marks["1.1"]);
        Assert.Equal(PassageMarks.Tag, marks["1.2"]);
        Assert.DoesNotContain("1.3", marks.Keys);
    }

    /// <summary>
    /// One edition's marks must not appear on another's. The original and its
    /// translation share citation references line for line, so a query that
    /// forgot to scope by edition would mark both panes identically and look
    /// entirely plausible doing it.
    /// </summary>
    [Fact]
    public async Task MarksDoNotLeakBetweenEditionsSharingCitationReferences()
    {
        using var db = await TempDatabase.CreateAsync();

        const string greekUrn = "urn:e:grk";
        const string englishUrn = "urn:e:eng";

        var greek = await db.SeedFullEditionAsync("grk", "Homer", "greekLit", "Iliad", "Original", "grc");
        var english = await db.SeedFullEditionAsync("eng", "Homer", "greekLit", "Iliad", "Translation", "eng");

        await db.InsertLinesAsync(greek, ("1.1", "μῆνιν ἄειδε"));
        await db.InsertLinesAsync(english, ("1.1", "sing the wrath"));

        await new BookmarkRepository().AddAsync(await db.TextNodeIdAsync(greek, "1.1"), null);

        var repo = new PassageMarkRepository();

        Assert.Equal(PassageMarks.Bookmark, (await repo.GetForEditionAsync(greek, greekUrn))["1.1"]);
        Assert.Empty(await repo.GetForEditionAsync(english, englishUrn));
    }

    /// <summary>
    /// The durability property, and the reason all three tables key on the
    /// citation reference. A re-ingest renumbers every node id; the marks have
    /// to come back pointing at the same lines.
    /// </summary>
    [Fact]
    public async Task MarksSurviveTheNodeIdsBeingReassigned()
    {
        using var db = await TempDatabase.CreateAsync();

        const string editionUrn = "urn:e:test1";

        var editionId = await db.SeedEditionAsync();
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));
        await new BookmarkRepository().AddAsync(await db.TextNodeIdAsync(editionId, "1.1"), null);

        // What a re-ingest does to this edition: the lines go and come back
        // with new ids.
        await db.ExecuteAsync($"DELETE FROM TextNodes WHERE EditionId = {editionId};");
        await db.InsertLinesAsync(editionId, ("1.1", "μῆνιν ἄειδε"));

        var marks = await new PassageMarkRepository().GetForEditionAsync(editionId, editionUrn);

        Assert.Equal(PassageMarks.Bookmark, marks["1.1"]);
    }
}
