using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Catmus;

/// <summary>
/// Downloads CATMuS transcriptions - the 0.1% of the dataset that is text -
/// and puts them in the library.
///
/// <b>Nothing is stored on disk by this.</b> Every shard is read over HTTP
/// Range requests through <see cref="HttpRangeStream"/>, which fetches only
/// the column chunks the transcriptions live in and hands them straight to
/// the database. The whole corpus - 194,808 lines from 313 manuscripts -
/// costs around 15 MB and a few minutes, against 24.71 GiB to download the
/// files it reads them out of.
///
/// The photographs are a separate, per-manuscript decision, because they are
/// the other 99.9%. See <see cref="CatmusImageDownloadService"/>.
/// </summary>
public class CatmusIngestService
{
    private readonly CatmusRepository _repository;

    public CatmusIngestService(CatmusRepository? repository = null)
    {
        _repository = repository ?? new CatmusRepository();
    }

    /// <summary>Shards that could not be read, for the outcome report.</summary>
    public List<(string FilePath, string Error)> FailedShards { get; } = new();

    // Written from the parallel fetches, so Interlocked rather than ++.
    private long _bytesFetched;
    private int _requestCount;

    public long BytesFetched => Interlocked.Read(ref _bytesFetched);

    public int RequestCount => _requestCount;

    /// <summary>
    /// Fetches the transcriptions of every shard given, skipping any already
    /// in the library.
    /// </summary>
    /// <param name="shards">
    /// Which shards to fetch. The Guided Setup step passes the whole
    /// catalogue; the Palaeography window passes one manuscript's worth.
    /// </param>
    public async Task<int> IngestAsync(
        IReadOnlyList<CatmusShard> shards,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var already = await _repository.GetIngestedShardsAsync(cancellationToken);
        var pending = shards.Where(s => !already.Contains(s.FileName)).ToList();

        if (pending.Count < shards.Count)
        {
            progress.Report($"{shards.Count - pending.Count} of {shards.Count} already downloaded - skipping those.");
        }

        if (pending.Count == 0)
        {
            progress.Report("Nothing to download.");
            return 0;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);

        var linesLoaded = 0;
        var done = 0;

        foreach (var batch in pending.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Fetched together, written one at a time. The whole cost of this
            // step is round trips - 346 files at four requests each before a
            // single column is read - and they are spent waiting, not
            // working, so several at once finishes in a fraction of the time.
            // Measured over a stratified sample of eight shards: 29.3 seconds
            // in sequence, which extrapolates to about fourteen minutes for
            // the dataset, against about four in batches of four.
            //
            // Four rather than more because this is somebody else's server
            // and the gain flattens: the limit here is latency per request,
            // and four in flight already hides almost all of it.
            var fetched = await Task.WhenAll(batch.Select(shard => FetchAsync(http, shard, cancellationToken)));

            foreach (var result in fetched)
            {
                cancellationToken.ThrowIfCancellationRequested();
                done++;

                if (result.Error != null)
                {
                    // One unreadable shard should not cost the other 345. It
                    // is named here and counted in the outcome, so the step
                    // can say what is missing rather than reporting a clean
                    // run.
                    FailedShards.Add((result.Shard.FileName, result.Error));
                    progress.Report($"[{done}/{pending.Count}] {result.Shard.Shelfmark} - could not read it: {result.Error}");
                    continue;
                }

                progress.Report($"[{done}/{pending.Count}] {result.Shard.Shelfmark} ({result.Lines.Count:N0} lines)");
                linesLoaded += await StoreAsync(result, cancellationToken);
            }
        }

        progress.Report(
            $"{linesLoaded:N0} lines from {pending.Count - FailedShards.Count} files - " +
            $"{BytesFetched / 1048576.0:F1} MB over {RequestCount:N0} requests.");

        return linesLoaded;
    }

    /// <summary>How many shards are read at once. See the comment at the batch loop.</summary>
    private const int BatchSize = 4;

    private sealed record FetchedShard(
        CatmusShard Shard,
        List<CatmusLine> Lines,
        CatmusLineRow? First,
        string? Error);

    /// <summary>
    /// Reads one shard's transcriptions. Touches the network and nothing
    /// else - the database write is done by the caller, one shard at a time,
    /// because SQLite takes one writer and a batch of four racing for it
    /// would serialise anyway with lock contention on top.
    /// </summary>
    private async Task<FetchedShard> FetchAsync(
        HttpClient http, CatmusShard shard, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await HttpRangeStream.OpenAsync(http, shard.DownloadUrl, cancellationToken);
            await using var reader = await CatmusShardReader.OpenRemoteAsync(
                stream, rowGroupLimit: null, cancellationToken);

            var rows = await reader.ReadLinesAsync(cancellationToken);

            Interlocked.Add(ref _bytesFetched, stream.BytesFetched);
            Interlocked.Add(ref _requestCount, stream.RequestCount);

            // A row with no transcription is a line the project segmented and
            // did not transcribe. It has a photograph and nothing to say
            // about it, so it is not a specimen of anything.
            var lines = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Text))
                .Select(r => new CatmusLine
                {
                    ShardFile = shard.FileName,
                    RowIndex = r.RowIndex,
                    Text = r.Text.Trim(),
                    Region = r.Region,
                    LineType = r.LineType
                })
                .ToList();

            return new FetchedShard(shard, lines, rows.FirstOrDefault(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FetchedShard(shard, new List<CatmusLine>(), null, ex.Message);
        }
    }

    private async Task<int> StoreAsync(FetchedShard result, CancellationToken cancellationToken)
    {
        // The catalogue's values were read from the first row group of this
        // same file, so they agree with the rows below; taking them from the
        // rows anyway means a shard whose metadata changed upstream arrives
        // correct rather than as the catalogue last remembered it.
        var first = result.First;
        var shard = result.Shard;

        await _repository.ReplaceShardAsync(
            shelfmark: first?.Shelfmark ?? shard.Shelfmark,
            language: first?.Language ?? shard.Language,
            century: first?.Century ?? shard.Century,
            scriptType: first?.ScriptType ?? shard.ScriptType,
            genre: first?.Genre ?? shard.Genre,
            verse: first?.Verse ?? shard.Verse,
            project: first?.Project ?? shard.Project,
            shardFile: shard.FileName,
            lines: result.Lines,
            cancellationToken: cancellationToken);

        return result.Lines.Count;
    }
}
