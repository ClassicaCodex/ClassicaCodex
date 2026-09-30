using Parquet;
using Parquet.Schema;

namespace ClassicaCodex.Ingestion.Catmus;

/// <summary>
/// One line of a manuscript as CATMuS records it: what the scribe wrote, and
/// the manuscript-level facts that are repeated on every row of a shard.
///
/// There is deliberately no line number and no page. CATMuS does not have
/// them - see <see cref="CatmusShardReader"/> - and inventing one from the
/// row order would be inventing a reading order that does not exist.
/// </summary>
public sealed class CatmusLineRow
{
    /// <summary>Position in the shard. The address of the line photograph, not a line number.</summary>
    public int RowIndex { get; init; }

    /// <summary>
    /// The diplomatic transcription, abbreviations and all: "ꝯcessisse" is
    /// stored as the scribe abbreviated it, not expanded to "concessisse".
    /// </summary>
    public string Text { get; init; } = string.Empty;

    public string? Shelfmark { get; init; }
    public string? Language { get; init; }
    public int? Century { get; init; }
    public string? ScriptType { get; init; }
    public string? Genre { get; init; }
    public string? Verse { get; init; }
    public string? Project { get; init; }

    /// <summary>SegmOnto zone: MainZone, MarginTextZone, NumberingZone and so on.</summary>
    public string? Region { get; init; }

    /// <summary>SegmOnto line type: DefaultLine, HeadingLine, InterlinearLine.</summary>
    public string? LineType { get; init; }
}

/// <summary>
/// Reads one CATMuS shard - local or remote - as transcriptions plus, on
/// request, the photograph of a single line.
///
/// <b>What CATMuS actually is, because it is not what its name suggests.</b>
/// Each row is one line of one manuscript: a PNG strip of roughly 4,772x170
/// pixels cropped to that line, and a diplomatic transcription of it. The
/// rows are shuffled. That is not a guess: in the Liege charter shard the
/// true order of the five lines is 3, 2, 0, 1, 4, and row 2 ends "par-" while
/// row 0 begins "tẽ" - two halves of "partem" three rows apart. There is no
/// page column, no line column, and the image path column is empty on every
/// row of every shard checked. So the reading order is not recoverable, and
/// nothing in this application pretends otherwise: CATMuS is loaded as a
/// reference collection of scribal hands, not as readable text.
///
/// The schema, from the files themselves:
///
///   text         string   the transcription
///   im/bytes     binary   PNG of the line, ~120-230 KB
///   im/path      string   empty on every row seen
///   language     string   Latin, French, Castilian, Middle Dutch, ...
///   century      sbyte    7 to 16
///   region       string   SegmOnto zone
///   script_type  string   Gothic Textualis, Caroline, Semitextualis, ...
///   shelfmark    string   "Paris, BnF, fr. 1593" - properly punctuated,
///                         unlike the file name, which flattens it
///   verse        string   verse / prose
///   genre        string   Narratives, Documents of practice, ...
///   project      string   the HTR project the transcription came from,
///                         which is what decides the licence
///   line_type    string   SegmOnto line type
///   gen_split    string   train / dev / test, from CATMuS's own splitting
///
/// im/bytes and im/path are nested one level inside a struct, so they carry a
/// maximum definition level of 2 and have to be read with a definition-levels
/// buffer. Every other column is flat.
/// </summary>
public sealed class CatmusShardReader : IAsyncDisposable
{
    /// <summary>
    /// The columns that describe a line without carrying its photograph.
    /// Reading only these is what costs 0.4% of a shard instead of all of it.
    /// </summary>
    public static readonly string[] TextColumns =
    {
        "text", "language", "century", "region", "script_type",
        "shelfmark", "verse", "genre", "project", "line_type", "gen_split"
    };

    private const string ImageColumn = "im/bytes";

    private readonly ParquetReader _reader;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly Dictionary<string, DataField> _fields;

    /// <summary>
    /// How many row groups <see cref="ReadLinesAsync"/> will actually read.
    ///
    /// The catalogue builder wants one shard row describing the manuscript,
    /// not its 40,000 transcriptions, and over the network each row group is
    /// its own request - so reading them all to learn a shelfmark that is
    /// identical on every row would cost roughly 16,800 requests across the
    /// dataset to find out 346 things.
    /// </summary>
    private readonly int _rowGroupsToRead;

    private CatmusShardReader(ParquetReader reader, Stream stream, bool ownsStream, int? rowGroupLimit)
    {
        _reader = reader;
        _stream = stream;
        _ownsStream = ownsStream;
        _fields = reader.Schema.GetDataFields().ToDictionary(f => f.Path.ToString());
        _rowGroupsToRead = Math.Min(rowGroupLimit ?? reader.RowGroupCount, reader.RowGroupCount);
    }

    /// <summary>Total lines in the shard, read from the footer without touching the data.</summary>
    public long LineCount => _reader.RowGroups.Sum(g => g.RowCount);

    public int RowGroupCount => _reader.RowGroupCount;

    public static async Task<CatmusShardReader> OpenFileAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var stream = File.OpenRead(path);
        try
        {
            var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);
            return new CatmusShardReader(reader, stream, ownsStream: true, rowGroupLimit: null);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Opens a shard over the network and prefetches every byte range the
    /// transcription columns occupy, so the reads that follow touch no
    /// network at all.
    /// </summary>
    /// <param name="rowGroupLimit">
    /// Read only this many row groups. Null reads the whole shard, which is
    /// what an ingest wants; the catalogue builder passes 1.
    /// </param>
    public static async Task<CatmusShardReader> OpenRemoteAsync(
        HttpRangeStream stream, int? rowGroupLimit = null, CancellationToken cancellationToken = default)
    {
        var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);
        var shard = new CatmusShardReader(reader, stream, ownsStream: false, rowGroupLimit);

        var ranges = new List<(long Start, long End)>();
        for (var group = 0; group < shard._rowGroupsToRead; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            foreach (var column in TextColumns)
            {
                if (!shard._fields.TryGetValue(column, out var field)) continue;

                var meta = rowGroup.GetMetadata(field)?.MetaData;

                // No metadata means no way to know where the column sits, so
                // nothing is prefetched for it and the read below falls back
                // to fetching it a page at a time. Slow, but correct.
                if (meta == null) continue;

                // A dictionary-encoded column stores its dictionary page
                // before its data pages; starting at the data page offset
                // would read half a column and decode nothing.
                var start = meta.DictionaryPageOffset is > 0
                    ? meta.DictionaryPageOffset.Value
                    : meta.DataPageOffset;
                ranges.Add((start, start + meta.TotalCompressedSize - 1));
            }
        }

        await stream.PrefetchAsync(ranges, cancellationToken: cancellationToken);
        return shard;
    }

    /// <summary>
    /// Every line in the shard, transcription and metadata only. The
    /// photographs are not touched, which is the point.
    /// </summary>
    public async Task<List<CatmusLineRow>> ReadLinesAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<CatmusLineRow>((int)LineCount);
        var rowIndex = 0;

        for (var group = 0; group < _rowGroupsToRead; group++)
        {
            using var rowGroup = _reader.OpenRowGroupReader(group);
            var count = (int)rowGroup.RowCount;

            var text = await ReadStringsAsync(rowGroup, "text", count, cancellationToken);
            var language = await ReadStringsAsync(rowGroup, "language", count, cancellationToken);
            var region = await ReadStringsAsync(rowGroup, "region", count, cancellationToken);
            var script = await ReadStringsAsync(rowGroup, "script_type", count, cancellationToken);
            var shelfmark = await ReadStringsAsync(rowGroup, "shelfmark", count, cancellationToken);
            var verse = await ReadStringsAsync(rowGroup, "verse", count, cancellationToken);
            var genre = await ReadStringsAsync(rowGroup, "genre", count, cancellationToken);
            var project = await ReadStringsAsync(rowGroup, "project", count, cancellationToken);
            var lineType = await ReadStringsAsync(rowGroup, "line_type", count, cancellationToken);

            var century = await ReadCenturyAsync(rowGroup, count, cancellationToken);

            for (var i = 0; i < count; i++)
            {
                rows.Add(new CatmusLineRow
                {
                    RowIndex = rowIndex++,
                    Text = text[i] ?? string.Empty,
                    Language = language[i],
                    Century = century[i],
                    Region = region[i],
                    ScriptType = script[i],
                    Shelfmark = shelfmark[i],
                    Verse = verse[i],
                    Genre = genre[i],
                    Project = project[i],
                    LineType = lineType[i]
                });
            }
        }

        return rows;
    }

    /// <summary>
    /// The PNG of one line, by its position in the shard.
    ///
    /// Only worth calling on a local file. Over the network this reads the
    /// whole row group the line sits in - about 1.5 MB for one 150 KB
    /// photograph - because a Parquet data page is the smallest unit that can
    /// be decoded, and the images are large enough that a page holds only a
    /// handful of them.
    /// </summary>
    public async Task<byte[]?> ReadImageAsync(int rowIndex, CancellationToken cancellationToken = default)
    {
        if (!_fields.TryGetValue(ImageColumn, out var field)) return null;

        var seen = 0;
        for (var group = 0; group < _reader.RowGroupCount; group++)
        {
            using var rowGroup = _reader.OpenRowGroupReader(group);
            var count = (int)rowGroup.RowCount;

            if (rowIndex >= seen + count)
            {
                seen += count;
                continue;
            }

            var values = new ReadOnlyMemory<byte>[count];
            var definitions = new int[count];
            await rowGroup.ReadRawAsync(field, values.AsMemory(), definitions.AsMemory(), null, cancellationToken);

            var local = rowIndex - seen;
            var source = 0;
            for (var i = 0; i < count; i++)
            {
                if (definitions[i] != field.MaxDefinitionLevel) continue;
                if (i == local) return values[source].ToArray();
                source++;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Every line photograph in the shard, in row order, with nulls where a
    /// row has none. Used by the image download, which wants all of them and
    /// should pay for each row group exactly once.
    /// </summary>
    public async IAsyncEnumerable<(int RowIndex, byte[]? Image)> ReadImagesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_fields.TryGetValue(ImageColumn, out var field)) yield break;

        var rowIndex = 0;
        for (var group = 0; group < _reader.RowGroupCount; group++)
        {
            using var rowGroup = _reader.OpenRowGroupReader(group);
            var count = (int)rowGroup.RowCount;

            var values = new ReadOnlyMemory<byte>[count];
            var definitions = new int[count];
            await rowGroup.ReadRawAsync(field, values.AsMemory(), definitions.AsMemory(), null, cancellationToken);

            var source = 0;
            for (var i = 0; i < count; i++)
            {
                var present = definitions[i] == field.MaxDefinitionLevel;
                yield return (rowIndex++, present ? values[source++].ToArray() : null);
            }
        }
    }

    /// <summary>
    /// Reads the century column, whatever width it was written at.
    ///
    /// CATMuS writes it as an 8-bit integer today - the values are 7 to 16,
    /// so one byte is plenty - and Parquet.Net refuses a read whose array
    /// type does not match the column exactly, throwing rather than
    /// converting. Hard-coding sbyte therefore means that the day the dataset
    /// is rebuilt with a wider integer, every shard fails to read and the
    /// whole download stops, over a field nothing depends on.
    ///
    /// Four widths cover anything a century could plausibly be written as.
    /// An unrecognised one leaves the centuries null rather than throwing,
    /// because a manuscript with no date is still a manuscript, and the
    /// catalogue has the date anyway.
    /// </summary>
    private async Task<int?[]> ReadCenturyAsync(
        ParquetRowGroupReader rowGroup, int count, CancellationToken cancellationToken)
    {
        var centuries = new int?[count];
        if (!_fields.TryGetValue("century", out var field)) return centuries;

        switch (Type.GetTypeCode(field.ClrType))
        {
            case TypeCode.SByte:
                var bytes = new sbyte?[count];
                await rowGroup.ReadAsync(field, bytes.AsMemory(), null, cancellationToken);
                for (var i = 0; i < count; i++) centuries[i] = bytes[i];
                break;

            case TypeCode.Int16:
                var shorts = new short?[count];
                await rowGroup.ReadAsync(field, shorts.AsMemory(), null, cancellationToken);
                for (var i = 0; i < count; i++) centuries[i] = shorts[i];
                break;

            case TypeCode.Int32:
                await rowGroup.ReadAsync(field, centuries.AsMemory(), null, cancellationToken);
                break;

            case TypeCode.Int64:
                var longs = new long?[count];
                await rowGroup.ReadAsync(field, longs.AsMemory(), null, cancellationToken);
                for (var i = 0; i < count; i++) centuries[i] = (int?)longs[i];
                break;
        }

        return centuries;
    }

    /// <summary>
    /// Reads one string column, undoing Parquet's null compaction.
    ///
    /// The values array holds only the rows that are not null, packed
    /// together; the definition levels say which rows those were. Reading the
    /// two in step is the only way to get a value back onto the row it came
    /// from - lining them up positionally shifts every value after the first
    /// null.
    /// </summary>
    private async Task<string?[]> ReadStringsAsync(
        ParquetRowGroupReader rowGroup, string column, int count, CancellationToken cancellationToken)
    {
        var values = new string?[count];
        if (!_fields.TryGetValue(column, out var field)) return values;

        var raw = new ReadOnlyMemory<char>[count];
        var definitions = new int[count];
        await rowGroup.ReadRawAsync(field, raw.AsMemory(), definitions.AsMemory(), null, cancellationToken);

        var source = 0;
        for (var i = 0; i < count; i++)
        {
            if (definitions[i] != field.MaxDefinitionLevel) continue;
            var value = new string(raw[source++].Span);
            values[i] = value.Length == 0 ? null : value;
        }

        return values;
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync();
        if (_ownsStream) await _stream.DisposeAsync();
    }
}
