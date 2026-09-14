using ClassicaCodex.Core.Models;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Remembering where a big work's rows fell, so reopening it does not cost
/// twenty seconds of measuring text that was measured last time.
///
/// The risk in any cache of this kind is that it answers with something stale,
/// and here that would mean showing a reader text the edition no longer has -
/// which would be far worse than being slow. So the file holds lengths rather
/// than text, every row is sliced out of the passage as it stands now, and an
/// entry is used only if each passage's pieces account for it exactly. Most of
/// what follows is about that: the ways an entry can be wrong, and that each
/// of them ends in a miss rather than in a lie.
///
/// Nothing here writes to the reader's own settings folder - the cache is
/// pointed at a temporary directory for the duration.
/// </summary>
public class ReaderLayoutCacheTests : IDisposable
{
    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(), "ccx-layout-cache-tests", Guid.NewGuid().ToString("N"));

    private readonly string _realDirectory = ReaderLayoutCache.Directory;

    public ReaderLayoutCacheTests()
    {
        Directory.CreateDirectory(_scratch);
        ReaderLayoutCache.Directory = _scratch;
    }

    public void Dispose()
    {
        ReaderLayoutCache.Directory = _realDirectory;

        try { Directory.Delete(_scratch, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        GC.SuppressFinalize(this);
    }

    private static readonly ReaderLayoutCache.Key Key = new(42, 737, "Palatino Linotype", 13f, 22);

    private static TextNode Node(long id, string text) => new()
    {
        TextNodeId = id,
        CitationRef = $"1.{id}",
        Text = text,
        SortOrder = (int)id,
        NodeKind = TextNodeKinds.Line,
        EditionId = 42
    };

    /// <summary>Rows as the pane would have produced them, cut where asked.</summary>
    private static List<ReaderRow> RowsFor(TextNode node, params int[] lengths)
    {
        var rows = new List<ReaderRow>();
        var start = 0;

        for (var i = 0; i < lengths.Length; i++)
        {
            rows.Add(new ReaderRow(node, node.Text.Substring(start, lengths[i]), i, lengths.Length));
            start += lengths[i];
        }

        return rows;
    }

    private static void Save(IReadOnlyList<ReaderRow> rows, int took = 5000) =>
        ReaderLayoutCache.Save(Key, rows, _ => 100, took);

    [Fact]
    public void ALayoutComesBackWithTheSameCutsAndHeights()
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 10, 10, 10));

        var loaded = ReaderLayoutCache.TryLoad(Key, new[] { node });

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Value.Rows.Count);
        Assert.Equal(node.Text, string.Concat(loaded.Value.Rows.Select(r => r.Text)));
        Assert.All(loaded.Value.Rows, r => Assert.Equal(100, loaded.Value.Heights[r.Text]));
    }

    /// <summary>
    /// The whole safety argument in one test. If the passage has been edited or
    /// re-ingested, its pieces no longer add up, and the entry is refused
    /// rather than used to slice text that is not there any more.
    /// </summary>
    [Fact]
    public void AnEntryForDifferentTextIsRefused()
    {
        var before = Node(1, new string('a', 30));
        Save(RowsFor(before, 10, 10, 10));

        var after = Node(1, new string('a', 26));   // the passage changed

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { after }));
    }

    /// <summary>
    /// The case the length check cannot see, and the one that matters most.
    ///
    /// A passage can be replaced by the same number of different characters -
    /// a re-ingest refresh and a saved translation both rewrite Text in place
    /// and keep the TextNodeId on purpose - and the pieces would still account
    /// for it exactly. The cuts would then fall mid-sentence and, worse, each
    /// row would be given a height measured for text it no longer holds, so a
    /// line would be drawn outside its row and clipped away with no marker.
    /// </summary>
    [Fact]
    public void AnEntryForTextOfTheSameLengthButDifferentCharactersIsRefused()
    {
        var before = Node(1, new string('a', 30));
        Save(RowsFor(before, 10, 10, 10));

        var after = Node(1, new string('a', 29) + 'b');

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { after }));
    }

    /// <summary>
    /// Every number in the file is in pixels and the font size in the key is
    /// in points, so the display scaling stands between them. An entry written
    /// at 100% must not be accepted at 125%, where the same point size is a
    /// taller line.
    /// </summary>
    [Fact]
    public void AnEntryMeasuredAtAnotherDisplayScalingIsNotUsed()
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 10, 10, 10));

        var scaled = new ReaderLayoutCache.Key(
            Key.EditionId, Key.Width, Key.FontFamily, Key.FontSize, Key.LineHeight + 5);

        Assert.Null(ReaderLayoutCache.TryLoad(scaled, new[] { node }));
    }

    [Fact]
    public void AnEntryForADifferentSetOfPassagesIsRefused()
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 15, 15));

        // Same edition, but the passages are not the same passages.
        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { Node(2, new string('a', 30)) }));
    }

    [Fact]
    public void AnEntryWithTheWrongNumberOfPassagesIsRefused()
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 30));

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { node, Node(2, "another") }));
    }

    /// <summary>
    /// Width and font decide where the cuts fall, so an entry made at one is
    /// no use at another. Asking with a different key must miss, not adapt.
    /// </summary>
    [Theory]
    [InlineData(500, "Palatino Linotype", 13f)]
    [InlineData(737, "Georgia", 13f)]
    [InlineData(737, "Palatino Linotype", 15f)]
    public void AnEntryMadeForAnotherLayoutIsNotUsed(int width, string family, float size)
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 10, 10, 10));

        Assert.Null(ReaderLayoutCache.TryLoad(new ReaderLayoutCache.Key(42, width, family, size, 22), new[] { node }));
    }

    [Fact]
    public void AskingForAWorkThatWasNeverKeptIsSimplyAMiss()
    {
        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { Node(1, "never seen") }));
    }

    /// <summary>
    /// The part that keeps this to the works it is for. A work that opened
    /// quickly is not written down at all, so most of the library never has a
    /// cache entry to be wrong about.
    /// </summary>
    [Fact]
    public void AWorkThatOpenedQuicklyIsNotKept()
    {
        var node = Node(1, new string('a', 30));

        Save(RowsFor(node, 10, 10, 10), took: ReaderLayoutCache.WorthKeepingMilliseconds - 1);

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { node }));
        Assert.Empty(Directory.GetFiles(_scratch, "*.layout"));
    }

    [Fact]
    public void AWorkThatTookLongEnoughIsKept()
    {
        var node = Node(1, new string('a', 30));

        Save(RowsFor(node, 10, 10, 10), took: ReaderLayoutCache.WorthKeepingMilliseconds);

        Assert.NotNull(ReaderLayoutCache.TryLoad(Key, new[] { node }));
    }

    /// <summary>
    /// A passage that was never divided is the ordinary case even in a large
    /// work, and has to survive the round trip as one row.
    /// </summary>
    [Fact]
    public void AnUndividedPassageRoundTripsAsOneRow()
    {
        var node = Node(1, "a whole passage that fits");
        Save(RowsFor(node, node.Text.Length));

        var loaded = ReaderLayoutCache.TryLoad(Key, new[] { node });

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Value.Rows);
        Assert.True(loaded.Value.Rows[0].IsFirst);
        Assert.True(loaded.Value.Rows[0].IsLast);
        Assert.Equal(node.Text, loaded.Value.Rows[0].Text);
    }

    [Fact]
    public void AWorkOfManyPassagesRoundTripsInOrder()
    {
        var nodes = Enumerable.Range(1, 50).Select(i => Node(i, new string((char)('a' + i % 26), 20 + i))).ToList();

        var rows = new List<ReaderRow>();
        foreach (var node in nodes) rows.AddRange(RowsFor(node, node.Text.Length / 2, node.Text.Length - node.Text.Length / 2));
        Save(rows);

        var loaded = ReaderLayoutCache.TryLoad(Key, nodes);

        Assert.NotNull(loaded);
        Assert.Equal(nodes.Count * 2, loaded!.Value.Rows.Count);

        foreach (var node in nodes)
        {
            var mine = loaded.Value.Rows.Where(r => r.Node.TextNodeId == node.TextNodeId).ToList();
            Assert.Equal(node.Text, string.Concat(mine.Select(r => r.Text)));
        }
    }

    /// <summary>
    /// A half-written or corrupted file must read as "nothing here", not throw
    /// - this runs while a reader is waiting for a work to open.
    /// </summary>
    [Fact]
    public void ATruncatedFileIsAMissRatherThanAFailure()
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 10, 10, 10));

        var file = Directory.GetFiles(_scratch, "*.layout").Single();
        var bytes = File.ReadAllBytes(file);
        File.WriteAllBytes(file, bytes.Take(bytes.Length / 2).ToArray());

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { node }));
    }

    [Fact]
    public void RubbishInTheFileIsAMissRatherThanAFailure()
    {
        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 10, 10, 10));

        File.WriteAllText(Directory.GetFiles(_scratch, "*.layout").Single(), "not a layout at all");

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { node }));
    }

    /// <summary>
    /// Entries from an older format are swept rather than left to age out.
    ///
    /// They are invisible dead weight: the key names a different filename now,
    /// so no load ever opens one and nothing would report them - while they go
    /// on counting against the forty entries the folder is allowed. A reader
    /// who has used this before the format changed has a folder half full of
    /// them.
    /// </summary>
    [Fact]
    public void EntriesFromAnOlderFormatAreSweptOnTheNextSave()
    {
        var stale = Path.Combine(_scratch, "e99-w737-PalatinoLinotype-13.layout");
        File.WriteAllBytes(stale, BitConverter.GetBytes(1));            // version 1

        var rubbish = Path.Combine(_scratch, "e98-w737-Georgia-13.layout");
        File.WriteAllText(rubbish, "not a layout at all");

        var node = Node(1, new string('a', 30));
        Save(RowsFor(node, 10, 10, 10));

        Assert.False(File.Exists(stale), "a version 1 entry survived, and nothing can ever read it");
        Assert.False(File.Exists(rubbish), "an unreadable file survived");
        Assert.NotNull(ReaderLayoutCache.TryLoad(Key, new[] { node }));
    }

    /// <summary>
    /// A missing folder - first run, or someone clearing their settings - is a
    /// miss, and the next save makes it again.
    /// </summary>
    [Fact]
    public void AMissingCacheFolderIsHarmless()
    {
        Directory.Delete(_scratch, recursive: true);

        Assert.Null(ReaderLayoutCache.TryLoad(Key, new[] { Node(1, "text") }));

        var node = Node(1, new string('a', 12));
        Save(RowsFor(node, 6, 6));

        Assert.NotNull(ReaderLayoutCache.TryLoad(Key, new[] { node }));
    }
}
