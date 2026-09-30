using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Catmus;

/// <summary>
/// Downloads the line photographs of one manuscript, so it can be looked at
/// with no internet connection at all.
///
/// <b>Why this is per-manuscript and opt-in.</b> The photographs are 99.9% of
/// CATMuS - 24.71 GiB against about 15 MB of transcription - so downloading
/// them all is a decision nobody should make by accident. One manuscript is
/// a median of 30.5 MiB; the smallest is 0.84 MiB and the largest, Ghent UL
/// 1374, is 1.15 GiB across four files. The window states the size before
/// anything starts.
///
/// The shard is streamed to a temporary file, the photographs are extracted
/// into a pack, and the shard is deleted - so the disk cost is the pictures
/// and not the pictures twice. The pack is a plain concatenation of the PNGs
/// with the offsets in the database, which makes showing one line a seek and
/// a 150 KB read instead of decompressing the 1.5 MB Parquet page it shares
/// with nine others.
/// </summary>
public class CatmusImageDownloadService
{
    private readonly CatmusRepository _repository;
    private readonly FileDownloadService _downloader;

    public CatmusImageDownloadService(CatmusRepository? repository = null, FileDownloadService? downloader = null)
    {
        _repository = repository ?? new CatmusRepository();
        _downloader = downloader ?? new FileDownloadService();
    }

    /// <summary>The folder inside the data root where packs live.</summary>
    public const string ImagesFolder = "catmus-images";

    /// <summary>
    /// A file name for a manuscript's pack, built from its shelfmark.
    ///
    /// Shelfmarks contain commas, full stops and slashes - "Paris, BnF, fr.
    /// 1593" - and a slash in a file name is a directory that does not exist.
    /// Every character that is not a letter, digit or dash becomes one dash,
    /// runs of dashes collapse, and the result is unique because shelfmarks
    /// are: two manuscripts differing only in punctuation would collide, and
    /// no two in this catalogue do.
    /// </summary>
    public static string PackFileName(string shelfmark)
    {
        var slug = new string(shelfmark
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-') + ".pack";
    }

    public static string PackPath(string dataRoot, string packFileName) =>
        Path.Combine(dataRoot, ImagesFolder, packFileName);

    /// <summary>
    /// Fetches every shard of one manuscript and writes its photographs into
    /// a single pack.
    /// </summary>
    /// <param name="dataRoot">The reader's chosen download folder.</param>
    public async Task<int> DownloadAsync(
        CatmusManuscript manuscript,
        string dataRoot,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var packFileName = PackFileName(manuscript.Shelfmark);
        var packPath = PackPath(dataRoot, packFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);

        var scratch = Path.Combine(Path.GetDirectoryName(packPath)!, packFileName + ".partial");
        var placements = new Dictionary<(string ShardFile, int RowIndex), (long Offset, int Length)>();
        var written = 0;

        try
        {
            await using (var pack = File.Create(scratch))
            {
                foreach (var shard in manuscript.Shards)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    written += await AppendShardAsync(
                        shard, pack, placements, dataRoot, progress, cancellationToken);
                }
            }

            // The pack only replaces a previous one once it is complete, and
            // the offsets are only recorded once the pack is at its real
            // path - so an interrupted download leaves the manuscript exactly
            // as it was rather than pointing at half a file.
            File.Move(scratch, packPath, overwrite: true);

            await _repository.SetImagePackAsync(
                manuscript.Shelfmark, packFileName, placements, cancellationToken);

            progress.Report(
                $"{written:N0} line photographs saved - {new FileInfo(packPath).Length / 1048576.0:F1} MB.");

            return written;
        }
        catch
        {
            if (File.Exists(scratch)) TryDelete(scratch);
            throw;
        }
    }

    private async Task<int> AppendShardAsync(
        CatmusShard shard,
        FileStream pack,
        Dictionary<(string, int), (long, int)> placements,
        string dataRoot,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        // Streamed to disk rather than read over ranges: this wants every
        // image column of every row group, which is the whole file, and one
        // sequential download beats several hundred range requests for the
        // same bytes.
        var temp = Path.Combine(dataRoot, ImagesFolder, shard.FileName);

        progress.Report($"   {shard.FileName} ({shard.Bytes / 1048576.0:F0} MB)");
        await _downloader.DownloadAsync(shard.DownloadUrl, temp, progress, cancellationToken);

        try
        {
            await using var reader = await CatmusShardReader.OpenFileAsync(temp, cancellationToken);

            var written = 0;
            await foreach (var (rowIndex, image) in reader.ReadImagesAsync(cancellationToken))
            {
                if (image == null || image.Length == 0) continue;

                var offset = pack.Position;
                await pack.WriteAsync(image, cancellationToken);
                placements[(shard.FileName, rowIndex)] = (offset, image.Length);
                written++;
            }

            return written;
        }
        finally
        {
            // The Parquet file is 99.9% the pictures now in the pack; keeping
            // it would double the disk cost of every manuscript to no end.
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // A file still held open by something else is left where it is.
            // It is in the data folder, which the reader can clear.
        }
    }

    /// <summary>
    /// Reads one line photograph back out of a pack.
    ///
    /// Returns null rather than throwing when the pack has gone - a data
    /// folder can be moved or cleared between the download and the viewing,
    /// and the right answer to that is a line with no picture, not a crash.
    /// </summary>
    public static async Task<byte[]?> ReadImageAsync(
        string dataRoot, CatmusLine line, CancellationToken cancellationToken = default)
    {
        if (!line.HasImage || string.IsNullOrEmpty(line.ImagePack)) return null;

        var path = PackPath(dataRoot, line.ImagePack);
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = File.OpenRead(path);
            if (line.ImageOffset!.Value + line.ImageLength!.Value > stream.Length) return null;

            stream.Seek(line.ImageOffset.Value, SeekOrigin.Begin);
            var buffer = new byte[line.ImageLength.Value];
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            return buffer;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
