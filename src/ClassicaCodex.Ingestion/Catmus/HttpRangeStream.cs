using System.Net;
using System.Net.Http.Headers;

namespace ClassicaCodex.Ingestion.Catmus;

/// <summary>
/// A read-only seekable <see cref="Stream"/> over an HTTPS resource, served
/// by Range requests, with an explicit prefetch so a caller that knows which
/// byte ranges it wants can fetch them in a handful of requests instead of
/// thousands.
///
/// <b>This is what makes CATMuS usable at all.</b> The dataset is 24.71 GiB,
/// of which the transcriptions - the part a reader of this application wants -
/// are about 0.1%; the rest is line photographs. Its own row-streaming API is
/// broken (every /rows call answers HTTP 500 from a stale presigned URL that
/// asks for bytes past the end of the object it points at), so the only route
/// to the text is the Parquet files themselves. Handing Parquet.Net one of
/// these instead of a FileStream turns "download 7 MB to read 11 KB of
/// transcription" into exactly the ten requests below.
///
/// Measured against one real shard (Paris, BnF, fr. 1593 - 405 lines,
/// 7,351,212 bytes):
///
///   plain range stream, no prefetch     30,040 bytes   2,465 requests
///   with PrefetchAsync                  30,040 bytes      10 requests
///
/// Same bytes either way - Parquet.Net already reads only the column chunks it
/// is asked for - but it reads them a few hundred bytes at a time, and a round
/// trip per few hundred bytes is the difference between four seconds and four
/// minutes.
/// </summary>
public sealed class HttpRangeStream : Stream
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly long _length;
    private long _position;

    /// <summary>
    /// Prefetched blocks, in the order they were asked for. A list rather than
    /// anything cleverer because there are never more than a few dozen per
    /// file and each read scans it once; an interval tree here would be more
    /// code than the thing it optimises.
    /// </summary>
    private readonly List<(long Start, long End, byte[] Data)> _blocks = new();

    /// <summary>Total bytes actually pulled over the wire, for progress reporting.</summary>
    public long BytesFetched { get; private set; }

    /// <summary>How many HTTP requests that took.</summary>
    public int RequestCount { get; private set; }

    private HttpRangeStream(HttpClient http, string url, long length)
    {
        _http = http;
        _url = url;
        _length = length;
    }

    /// <summary>
    /// Asks the server how big the file is and whether it serves ranges at
    /// all. Throws if it does not: silently falling back to reading the whole
    /// file would turn a 30 KB operation into a 7 MB one without saying so.
    /// </summary>
    public static async Task<HttpRangeStream> OpenAsync(
        HttpClient http, string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength
            ?? throw new InvalidOperationException($"{url} did not report a content length.");

        // HuggingFace answers "bytes"; a host that answers "none", or omits
        // the header entirely, cannot serve this reader.
        if (!response.Headers.AcceptRanges.Contains("bytes"))
        {
            throw new InvalidOperationException($"{url} does not serve byte ranges.");
        }

        return new HttpRangeStream(http, url, length);
    }

    /// <summary>
    /// Pulls one byte range into memory, where subsequent reads will find it.
    /// Ranges may be asked for in any order and may overlap; each is stored as
    /// given.
    /// </summary>
    public async Task PrefetchAsync(long start, long end, CancellationToken cancellationToken = default)
    {
        if (start < 0 || end >= _length || end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start), $"[{start}, {end}] is not inside a {_length}-byte file.");
        }

        var buffer = new byte[end - start + 1];
        await FetchAsync(start, end, buffer, 0, cancellationToken);
        _blocks.Add((start, end, buffer));
    }

    /// <summary>
    /// Prefetches several ranges, merging any that sit within
    /// <paramref name="mergeGap"/> bytes of each other into one request.
    ///
    /// The merge is the whole trick. Parquet lays a row group out as
    /// contiguous column chunks in schema order, so the text column of one row
    /// group and the small metadata columns of the row group before it are
    /// neighbours in the file even though they belong to different row groups.
    /// On fr. 1593 that turned 55 separate column chunks into 6 requests, and
    /// the merged ranges covered 15,678 bytes - every byte those 55 chunks
    /// occupy and not one byte more.
    /// </summary>
    public async Task PrefetchAsync(
        IEnumerable<(long Start, long End)> ranges, int mergeGap = 16384,
        CancellationToken cancellationToken = default)
    {
        var merged = new List<(long Start, long End)>();

        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start - merged[^1].End <= mergeGap)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        foreach (var range in merged)
        {
            await PrefetchAsync(range.Start, range.End, cancellationToken);
        }
    }

    private async Task<int> FetchAsync(
        long start, long end, byte[] buffer, int offset, CancellationToken cancellationToken)
    {
        var wanted = (int)(end - start + 1);

        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.Range = new RangeHeaderValue(start, end);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // A server that ignores the Range header answers 200 with the whole
        // file, which would overrun the buffer and hand back the wrong bytes
        // without any error. Insist on 206.
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new InvalidOperationException(
                $"Expected 206 Partial Content for bytes {start}-{end} of {_url}, " +
                $"got {(int)response.StatusCode}.");
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        await content.ReadExactlyAsync(buffer.AsMemory(offset, wanted), cancellationToken);

        BytesFetched += wanted;
        RequestCount++;
        return wanted;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_position >= _length || count == 0) return 0;

        var wanted = (int)Math.Min(count, _length - _position);

        foreach (var block in _blocks)
        {
            if (_position < block.Start || _position + wanted - 1 > block.End) continue;
            Array.Copy(block.Data, _position - block.Start, buffer, offset, wanted);
            _position += wanted;
            return wanted;
        }

        var read = await FetchAsync(_position, _position + wanted - 1, buffer, offset, cancellationToken);
        _position += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // Parquet.Net reads through the Memory overload. Copying via a plain
        // array keeps one real implementation above rather than two.
        var scratch = new byte[buffer.Length];
        var read = await ReadAsync(scratch, 0, scratch.Length, cancellationToken);
        scratch.AsMemory(0, read).CopyTo(buffer);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset
        };
        return _position;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
