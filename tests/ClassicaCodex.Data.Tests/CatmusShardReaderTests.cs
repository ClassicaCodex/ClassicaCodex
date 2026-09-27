using ClassicaCodex.Ingestion.Catmus;
using Parquet.Serialization;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading a CATMuS shard.
///
/// <b>The thing worth pinning is null compaction.</b> Parquet does not store
/// a placeholder for a null: the values array holds only the rows that have
/// one, packed together, and a separate array of definition levels says which
/// rows those were. Reading the two positionally - which is what the obvious
/// code does - shifts every value after the first null onto the wrong row, so
/// a shard with one untranscribed line puts every later transcription against
/// the wrong photograph. Nothing about that looks like a bug from the
/// outside; the lines are all real and all present.
///
/// The photograph column makes it worse, because it is nested one level
/// inside a struct and so has a maximum definition level of 2 rather than 1 -
/// a reader that assumes "present means 1" finds no images at all.
///
/// So this builds a small Parquet file of the same shape as a real shard,
/// with holes in it, and reads it back.
/// </summary>
public class CatmusShardReaderTests
{
    /// <summary>
    /// The CATMuS row shape. The image is a nested object, which is how
    /// HuggingFace writes an image column and why im/bytes sits at definition
    /// level 2.
    /// </summary>
    private sealed class Row
    {
        public string? text { get; set; }
        public ImageValue? im { get; set; }
        public string? language { get; set; }
        public int? century { get; set; }
        public string? region { get; set; }
        public string? script_type { get; set; }
        public string? shelfmark { get; set; }
        public string? verse { get; set; }
        public string? genre { get; set; }
        public string? project { get; set; }
        public string? line_type { get; set; }
        public string? gen_split { get; set; }
    }

    private sealed class ImageValue
    {
        public byte[]? bytes { get; set; }
        public string? path { get; set; }
    }

    private static Row Make(string? text, byte[]? image) => new()
    {
        text = text,
        im = image == null ? null : new ImageValue { bytes = image, path = null },
        language = "Latin",
        century = 12,
        region = "MainZone",
        script_type = "Gothic Documentary Script",
        shelfmark = "Liege, Archives de l'État, T51.12",
        verse = "prose",
        genre = "Documents of practice",
        project = "HTRogène",
        line_type = "DefaultLine",
        gen_split = "train"
    };

    private static async Task<string> WriteShardAsync(IEnumerable<Row> rows)
    {
        var path = Path.Combine(Path.GetTempPath(), "ccx-catmus-" + Guid.NewGuid().ToString("N") + ".parquet");
        await using var file = File.Create(path);
        await ParquetSerializer.SerializeAsync(rows, file);
        return path;
    }

    /// <summary>
    /// A hole in the middle of the transcriptions must not shift the ones
    /// after it.
    /// </summary>
    [Fact]
    public async Task ARowWithNoTranscriptionDoesNotShiftTheRowsAfterIt()
    {
        var path = await WriteShardAsync(new[]
        {
            Make("first", new byte[] { 1 }),
            Make(null, new byte[] { 2 }),
            Make("third", new byte[] { 3 }),
            Make("fourth", new byte[] { 4 })
        });

        try
        {
            await using var reader = await CatmusShardReader.OpenFileAsync(path);
            var lines = await reader.ReadLinesAsync();

            Assert.Equal(4, lines.Count);
            Assert.Equal("first", lines[0].Text);
            Assert.Equal(string.Empty, lines[1].Text);
            Assert.Equal("third", lines[2].Text);
            Assert.Equal("fourth", lines[3].Text);

            Assert.Equal(new[] { 0, 1, 2, 3 }, lines.Select(l => l.RowIndex));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The metadata columns are read the same way and land on the same rows.
    /// </summary>
    [Fact]
    public async Task TheManuscriptFactsComeBackOffEveryRow()
    {
        var path = await WriteShardAsync(new[] { Make("a line", new byte[] { 1 }) });

        try
        {
            await using var reader = await CatmusShardReader.OpenFileAsync(path);
            var line = Assert.Single(await reader.ReadLinesAsync());

            Assert.Equal("Liege, Archives de l'État, T51.12", line.Shelfmark);
            Assert.Equal("Latin", line.Language);
            Assert.Equal(12, line.Century);
            Assert.Equal("Gothic Documentary Script", line.ScriptType);
            Assert.Equal("Documents of practice", line.Genre);
            Assert.Equal("prose", line.Verse);
            Assert.Equal("HTRogène", line.Project);
            Assert.Equal("MainZone", line.Region);
            Assert.Equal("DefaultLine", line.LineType);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The photograph column is nested, so "present" is definition level 2.
    /// A reader that expects 1 comes back with no photographs and no error.
    /// </summary>
    [Fact]
    public async Task ThePhotographsComeBackOnTheRowsThatHaveThem()
    {
        var path = await WriteShardAsync(new[]
        {
            Make("first", new byte[] { 10, 11 }),
            Make("second", null),
            Make("third", new byte[] { 30, 31, 32 })
        });

        try
        {
            await using var reader = await CatmusShardReader.OpenFileAsync(path);

            var images = new List<(int RowIndex, byte[]? Image)>();
            await foreach (var image in reader.ReadImagesAsync()) images.Add(image);

            Assert.Equal(3, images.Count);
            Assert.Equal(new byte[] { 10, 11 }, images[0].Image);
            Assert.Null(images[1].Image);
            Assert.Equal(new byte[] { 30, 31, 32 }, images[2].Image);

            // And one at a time, which is the path the viewer uses.
            Assert.Equal(new byte[] { 30, 31, 32 }, await reader.ReadImageAsync(2));
            Assert.Null(await reader.ReadImageAsync(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The dataset writes the century as an 8-bit integer, and the rows above
    /// write it as a 32-bit one. Both have to read.
    ///
    /// Parquet.Net refuses a read whose array type does not match the column,
    /// so a reader written against one width throws on the other - and that
    /// throw is per shard, which means the day CATMuS rebuilds with a wider
    /// integer the whole download stops. The rest of this file uses int; this
    /// one pins the width the dataset actually uses.
    /// </summary>
    [Fact]
    public async Task TheCenturyReadsAtTheWidthTheDatasetWritesIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccx-catmus-" + Guid.NewGuid().ToString("N") + ".parquet");
        await using (var file = File.Create(path))
        {
            await ParquetSerializer.SerializeAsync(
                new[] { new NarrowCenturyRow { text = "a line", century = 12 } }, file);
        }

        try
        {
            await using var reader = await CatmusShardReader.OpenFileAsync(path);
            Assert.Equal(12, Assert.Single(await reader.ReadLinesAsync()).Century);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class NarrowCenturyRow
    {
        public string? text { get; set; }
        public sbyte? century { get; set; }
    }

    /// <summary>
    /// The line count comes from the footer, so it is known before any data
    /// is read - which is what lets the catalogue builder learn a shard's
    /// size without downloading it.
    /// </summary>
    [Fact]
    public async Task TheLineCountIsReadableWithoutReadingTheLines()
    {
        var path = await WriteShardAsync(Enumerable.Range(0, 7).Select(i => Make("line " + i, new byte[] { 1 })));

        try
        {
            await using var reader = await CatmusShardReader.OpenFileAsync(path);
            Assert.Equal(7, reader.LineCount);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
