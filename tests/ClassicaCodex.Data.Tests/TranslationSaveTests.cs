using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Saving a translation in progress, which Create Translation does after
/// every batch.
///
/// It used to clear the edition's lines and insert the new set. That is the
/// obvious way to write "make the stored lines match these lines", and it
/// cost two things that are invisible until the library is large.
///
/// Every line got a new TextNodeId on every save. The word index is keyed on
/// those ids, so all of this edition's index rows were orphaned each time -
/// and the only way to clear an orphan is to find rows by line id, which the
/// index has no access path for. WordIndex is WITHOUT ROWID keyed
/// (NormalizedWord, TextNodeId), so a delete by id makes SQLite skip-scan
/// every distinct word: on a 70.8-million-row index, 2.2 million probes per
/// line. Measured there, an edition of 8,088 lines had not finished clearing
/// after fifteen minutes. Create Translation paid that after every batch, and
/// the bill grew as the translation got longer, holding SQLite's single write
/// lock throughout - which is enough to lock the reader out of its own
/// library while a translation runs.
///
/// So these tests are about identity, not just content: that a line keeps its
/// id, that the index does not accumulate rows pointing at lines that no
/// longer exist, and that what the index holds after two saves is exactly
/// what it would hold after one.
/// </summary>
[Collection("Database")]
public class TranslationSaveTests
{
    [Fact]
    public async Task ALineThatDidNotChangeKeepsItsIdentity()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SyncEditionAsync(1, Lines(("1.1", "the wrath of Peleus' son"), ("1.2", "sing, goddess")));
        var first = await repo.GetByEditionAsync(1);

        await repo.SyncEditionAsync(1, Lines(("1.1", "the wrath of Peleus' son"), ("1.2", "sing, goddess")));
        var second = await repo.GetByEditionAsync(1);

        Assert.Equal(
            first.Select(n => n.TextNodeId).OrderBy(i => i),
            second.Select(n => n.TextNodeId).OrderBy(i => i));
    }

    [Fact]
    public async Task AnUnchangedLineIsNotReportedAsChanged()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess")));
        var changes = await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess")));

        Assert.False(changes.Any);
    }

    /// <summary>
    /// The old text is the part that matters: without it the index cannot
    /// know which rows to withdraw.
    /// </summary>
    [Fact]
    public async Task ARewrittenLineReportsWhatItUsedToSay()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess")));
        var changes = await repo.SyncEditionAsync(1, Lines(("1.1", "sing, O goddess")));

        var (id, oldText, newText) = Assert.Single(changes.Rewritten);
        Assert.Equal("sing, goddess", oldText);
        Assert.Equal("sing, O goddess", newText);
        Assert.True(id > 0);
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public async Task ANewLineIsAddedAndReported()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess")));
        var changes = await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess"), ("1.2", "of the wrath")));

        Assert.Single(changes.Added);
        Assert.Empty(changes.Rewritten);
        Assert.Equal(2, (await repo.GetByEditionAsync(1)).Count);
    }

    [Fact]
    public async Task ALineNoLongerWantedIsRemovedAndReported()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess"), ("1.2", "of the wrath")));
        var changes = await repo.SyncEditionAsync(1, Lines(("1.1", "sing, goddess")));

        var removed = Assert.Single(changes.Removed);
        Assert.Equal("of the wrath", removed.Text);
        Assert.Single(await repo.GetByEditionAsync(1));
    }

    /// <summary>
    /// The regression this whole change exists for. Two saves must leave the
    /// index holding exactly what one save would - no rows pointing at lines
    /// that no longer exist.
    /// </summary>
    [Fact]
    public async Task SavingTwiceLeavesNoIndexRowsPointingAtNothing()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        var first = await repo.SyncEditionAsync(1, Lines(("1.1", "sing goddess"), ("1.2", "of the wrath")));
        await index.ApplyChangesAsync(first);

        var second = await repo.SyncEditionAsync(1, Lines(("1.1", "sing muse"), ("1.2", "of the wrath")));
        await index.ApplyChangesAsync(second);

        Assert.Equal(0, await OrphanedIndexRowsAsync(db));
    }

    /// <summary>
    /// And the index has to be right, not merely tidy: the withdrawn word
    /// must be gone and the new one present.
    /// </summary>
    [Fact]
    public async Task RewritingALineWithdrawsItsOldWordsAndIndexesTheNew()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        await index.ApplyChangesAsync(await repo.SyncEditionAsync(1, Lines(("1.1", "sing goddess"))));
        Assert.Equal(1, await RowsForWordAsync(db, "goddess"));

        await index.ApplyChangesAsync(await repo.SyncEditionAsync(1, Lines(("1.1", "sing muse"))));

        Assert.Equal(0, await RowsForWordAsync(db, "goddess"));
        Assert.Equal(1, await RowsForWordAsync(db, "muse"));
        Assert.Equal(1, await RowsForWordAsync(db, "sing"));
    }

    /// <summary>
    /// A word kept across a rewrite must survive it. The withdrawal and the
    /// re-indexing overlap on that word, so doing them in the wrong order
    /// would delete the row that had just been written.
    /// </summary>
    [Fact]
    public async Task AWordCommonToBothVersionsSurvivesTheRewrite()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        await index.ApplyChangesAsync(await repo.SyncEditionAsync(1, Lines(("1.1", "sing goddess"))));
        await index.ApplyChangesAsync(await repo.SyncEditionAsync(1, Lines(("1.1", "sing muse"))));

        Assert.Equal(1, await RowsForWordAsync(db, "sing"));
    }

    /// <summary>
    /// The hand-written workbench, which had the same fault in a quieter
    /// form: it saved a line by deleting and re-inserting it, so every edit
    /// gave the line a new id.
    /// </summary>
    [Fact]
    public async Task EditingALineInTheWorkbenchKeepsItsIdentity()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SaveTranslatedLineAsync(1, "1.1", 0, "first attempt");
        var before = (await repo.GetByEditionAsync(1)).Single().TextNodeId;

        await repo.SaveTranslatedLineAsync(1, "1.1", 0, "second attempt");
        var after = (await repo.GetByEditionAsync(1)).Single().TextNodeId;

        Assert.Equal(before, after);
    }

    /// <summary>
    /// And the part that was missing altogether: nothing on the workbench's
    /// save path ever touched the word index, so a translation you wrote
    /// yourself could be read but not found.
    /// </summary>
    [Fact]
    public async Task AHandWrittenLineReachesTheIndexAndStaysCorrectWhenEdited()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        await index.ApplyChangesAsync(await repo.SaveTranslatedLineAsync(1, "1.1", 0, "sing goddess"));
        Assert.Equal(1, await RowsForWordAsync(db, "goddess"));

        await index.ApplyChangesAsync(await repo.SaveTranslatedLineAsync(1, "1.1", 0, "sing muse"));

        Assert.Equal(0, await RowsForWordAsync(db, "goddess"));
        Assert.Equal(1, await RowsForWordAsync(db, "muse"));
        Assert.Equal(0, await OrphanedIndexRowsAsync(db));
    }

    /// <summary>
    /// Clearing a line still deletes it - that behaviour is deliberate and
    /// documented - but its index rows have to go with it.
    /// </summary>
    [Fact]
    public async Task ClearingALineWithdrawsItFromTheIndexToo()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        await index.ApplyChangesAsync(await repo.SaveTranslatedLineAsync(1, "1.1", 0, "sing goddess"));
        await index.ApplyChangesAsync(await repo.SaveTranslatedLineAsync(1, "1.1", 0, ""));

        Assert.Empty(await repo.GetByEditionAsync(1));
        Assert.Equal(0, await RowsForWordAsync(db, "goddess"));
        Assert.Equal(0, await OrphanedIndexRowsAsync(db));
    }

    /// <summary>
    /// A citation reference can repeat. The index on (EditionId, CitationRef)
    /// is deliberately not unique, and this corpus has three editions where
    /// one does - the eddic poems carry 1.45.10 and 1.45.11 twice, with
    /// different text.
    ///
    /// The first version of SyncEditionAsync kept one row per reference and
    /// deleted the rest as lines that should no longer exist. That turned a
    /// duplicate the reader already had into a deletion on the next save: an
    /// existing 519-line AI translation would have become 517 lines, silently.
    /// </summary>
    [Fact]
    public async Task TwoLinesSharingACitationReferenceBothSurvive()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        // Shaped like the eddic poems: the source carries the reference twice,
        // so the form hands over two lines for it.
        var wanted = Lines(("1.45.10", "but the giant breaks free"), ("1.45.10", "but the giant breaks free"));
        await repo.SyncEditionAsync(1, wanted);
        Assert.Equal(2, (await repo.GetByEditionAsync(1)).Count);

        // The save that used to delete one of them.
        var changes = await repo.SyncEditionAsync(1, wanted);

        Assert.Equal(2, (await repo.GetByEditionAsync(1)).Count);
        Assert.Empty(changes.Removed);
        Assert.False(changes.Any);
    }

    /// <summary>
    /// And the identities have to hold too, or the index is orphaned even
    /// though the count looks right.
    /// </summary>
    [Fact]
    public async Task DuplicateLinesKeepTheirIdentitiesAcrossSaves()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        var wanted = Lines(("1.45.11", "shakes"), ("1.45.11", "shakes"));
        await repo.SyncEditionAsync(1, wanted);
        var first = (await repo.GetByEditionAsync(1)).Select(n => n.TextNodeId).OrderBy(i => i).ToList();

        await repo.SyncEditionAsync(1, wanted);
        var second = (await repo.GetByEditionAsync(1)).Select(n => n.TextNodeId).OrderBy(i => i).ToList();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Asking for fewer lines than are stored for a reference does still
    /// remove the extras - the caller has said how many it wants - but it is
    /// reported, so the index follows.
    /// </summary>
    [Fact]
    public async Task AskingForFewerLinesThanAreStoredRemovesAndReportsTheExtras()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();

        await repo.SyncEditionAsync(1, Lines(("1.1", "one"), ("1.1", "two")));
        var changes = await repo.SyncEditionAsync(1, Lines(("1.1", "one")));

        Assert.Single(await repo.GetByEditionAsync(1));
        Assert.Single(changes.Removed);
    }

    /// <summary>
    /// The workbench's clear. The delete has always taken every row for the
    /// reference; reporting only the first left the others' index rows
    /// pointing at a passage that no longer existed - and SQLite reuses
    /// rowids, so those words would eventually surface in an unrelated
    /// passage's search results.
    /// </summary>
    [Fact]
    public async Task ClearingAReferenceWithTwoLinesWithdrawsBothFromTheIndex()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        await index.ApplyChangesAsync(await repo.SyncEditionAsync(1,
            Lines(("1.1", "alpha unique"), ("1.1", "beta distinct"))));
        Assert.Equal(1, await RowsForWordAsync(db, "beta"));

        await index.ApplyChangesAsync(await repo.SaveTranslatedLineAsync(1, "1.1", 0, ""));

        Assert.Empty(await repo.GetByEditionAsync(1));
        Assert.Equal(0, await RowsForWordAsync(db, "alpha"));
        Assert.Equal(0, await RowsForWordAsync(db, "beta"));
        Assert.Equal(0, await OrphanedIndexRowsAsync(db));
    }

    /// <summary>
    /// Writing a translation for a reference that has two rows must not
    /// destroy one of them.
    /// </summary>
    [Fact]
    public async Task SavingOverADuplicatedReferenceLosesNothing()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var repo = new TextNodeRepository();
        var index = new WordIndexService();

        await index.ApplyChangesAsync(await repo.SyncEditionAsync(1,
            Lines(("1.1", "first wording"), ("1.1", "second wording"))));

        await index.ApplyChangesAsync(await repo.SaveTranslatedLineAsync(1, "1.1", 0, "agreed wording"));

        var rows = await repo.GetByEditionAsync(1);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("agreed wording", r.Text));
        Assert.Equal(0, await RowsForWordAsync(db, "first"));
        Assert.Equal(0, await RowsForWordAsync(db, "second"));
        Assert.Equal(2, await RowsForWordAsync(db, "agreed"));
        Assert.Equal(0, await OrphanedIndexRowsAsync(db));
    }

    /// <summary>
    /// The last line of defence on the one method here that deletes passages.
    ///
    /// A harness in this project once handed it a source edition and deleted
    /// three passages of a real library. Nothing reachable through the
    /// interface can do that - the workbench is only given the reader's own
    /// translation - but the only thing enforcing it was a substring of a URN
    /// chosen by the caller.
    /// </summary>
    [Fact]
    public async Task WritingToAnOriginalEditionIsRefused()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(
            @"INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace)
                VALUES (1, 'urn:cts:greekLit:tlg0016', 'Herodotus', 'greekLit');
              INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title)
                VALUES (1, 1, 'urn:cts:greekLit:tlg0016.tlg001', 'Histories');
              INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language)
                VALUES (7, 1, 'tlg0016.tlg001.perseus-grc1', 'Original', 'grc');
              INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text)
                VALUES (7, '1.1', 0, 'the enquiries of Herodotus of Halicarnassus');");

        var repo = new TextNodeRepository();

        // The exact shape of the accident: an empty box, which means delete.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.SaveTranslatedLineAsync(7, "1.1", 0, ""));

        Assert.Single(await repo.GetByEditionAsync(7));
    }

    [Fact]
    public async Task DeletingByPairRemovesOnlyThatPair()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedEditionAsync(db);
        var index = new WordIndexRepository();

        await index.BulkInsertAsync(new[] { ("alpha", 1L), ("beta", 1L), ("alpha", 2L) });
        await index.DeleteExactAsync(new[] { ("alpha", 1L) });

        Assert.Equal(0, await PairExistsAsync(db, "alpha", 1));
        Assert.Equal(1, await PairExistsAsync(db, "beta", 1));
        Assert.Equal(1, await PairExistsAsync(db, "alpha", 2));
    }

    private static List<TextNode> Lines(params (string Citation, string Text)[] lines) =>
        lines.Select((l, i) => new TextNode
        {
            EditionId = 1,
            CitationRef = l.Citation,
            SortOrder = i,
            Text = l.Text,
            NodeKind = "line"
        }).ToList();

    private static Task SeedEditionAsync(TempDatabase db) => db.ExecuteAsync(
        @"INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace)
            VALUES (1, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit');
          INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title)
            VALUES (1, 1, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
          INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language)
            VALUES (1, 1, 'tlg0012.tlg001.mine-1', 'Translation', 'eng');");

    private static async Task<long> OrphanedIndexRowsAsync(TempDatabase db) =>
        await db.ScalarAsync<long>(
            @"SELECT COUNT(*) FROM WordIndex wi
              LEFT JOIN TextNodes t ON t.TextNodeId = wi.TextNodeId
              WHERE t.TextNodeId IS NULL");

    private static async Task<long> RowsForWordAsync(TempDatabase db, string word) =>
        await db.ScalarAsync<long>($"SELECT COUNT(*) FROM WordIndex WHERE NormalizedWord = '{word}'");

    private static async Task<long> PairExistsAsync(TempDatabase db, string word, long id) =>
        await db.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM WordIndex WHERE NormalizedWord = '{word}' AND TextNodeId = {id}");
}
