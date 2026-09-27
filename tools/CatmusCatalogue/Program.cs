using System.Text;
using System.Text.Json;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Ingestion.Catmus;

namespace ClassicaCodex.Tools.CatmusCatalogue;

/// <summary>
/// Rebuilds CatmusShards.tsv - the list of manuscripts the Palaeography
/// window offers before anything has been downloaded.
///
/// The file has to be built rather than written by hand because the shard
/// names only half-describe their contents: the name flattens
/// "Liege, Archives de l'Etat, T51.12" to "Liege__Archives_de_l_Etat__T51_12"
/// and abbreviates "Gothic Documentary Script" to "Got_Doc_Scr", losing the
/// punctuation, the accents and the full words for good. So this reads the
/// real values out of each shard, which costs about six range requests and
/// twenty kilobytes per file rather than downloading 24.71 GiB.
///
/// Run it when CATMuS publishes new manuscripts:
///
///   dotnet run --project tools/CatmusCatalogue
///
/// It writes src/ClassicaCodex.Core/Catmus/CatmusShards.tsv, which is an
/// embedded resource, so the change is picked up by a rebuild.
/// </summary>
public static class Program
{
    private const string TreeApi = "https://huggingface.co/api/datasets/CATMuS/medieval/tree/main/";

    private static readonly string[] Splits = { "train", "dev", "test" };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var output = args.Length > 0
            ? args[0]
            : Path.Combine(FindRepositoryRoot(), "src", "ClassicaCodex.Core", "Catmus", "CatmusShards.tsv");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);

        var shards = new List<ShardListing>();
        foreach (var split in Splits)
        {
            var listing = await ListSplitAsync(http, split);
            Console.WriteLine($"{split}: {listing.Count} shards");
            shards.AddRange(listing);
        }

        Console.WriteLine($"{shards.Count} shards, {shards.Sum(s => s.Bytes) / 1073741824.0:F2} GiB total");
        Console.WriteLine();

        var rows = new List<string>();
        var failures = 0;

        for (var i = 0; i < shards.Count; i++)
        {
            var shard = shards[i];
            Console.Write($"[{i + 1,3}/{shards.Count}] {shard.Path} ... ");

            try
            {
                var described = await DescribeAsync(http, shard);
                rows.Add(described);
                Console.WriteLine("ok");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAILED: {ex.Message}");
            }
        }

        if (failures > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{failures} shards could not be read. The catalogue was NOT written - " +
                              "a partial one would silently hide manuscripts.");
            return 1;
        }

        rows.Sort(StringComparer.Ordinal);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllLinesAsync(output, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine();
        Console.WriteLine($"Wrote {rows.Count} rows to {output}");
        return 0;
    }

    private sealed record ShardListing(string Split, string FileName, long Bytes)
    {
        public string Path => $"{Split}/{FileName}";
    }

    private static async Task<List<ShardListing>> ListSplitAsync(HttpClient http, string split)
    {
        var json = await http.GetStringAsync(TreeApi + split);
        using var document = JsonDocument.Parse(json);

        var listing = new List<ShardListing>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.GetProperty("type").GetString() != "file") continue;

            var path = entry.GetProperty("path").GetString();
            if (path == null || !path.EndsWith(".parquet", StringComparison.Ordinal)) continue;

            // The size at the top level is the real object size; the one
            // nested under "lfs" is the same number, and "pointerSize" is the
            // size of the git pointer, which is not what anyone downloads.
            listing.Add(new ShardListing(split, System.IO.Path.GetFileName(path), entry.GetProperty("size").GetInt64()));
        }

        return listing;
    }

    /// <summary>
    /// Reads one shard's metadata over the network. Only the first row group
    /// is read: every column below is constant across a shard by
    /// construction - the shard name encodes language, century and script,
    /// and CATMuS builds one shard per (manuscript, hand) - so a second row
    /// group would cost requests to confirm what the file name already says.
    /// The ingest, which reads every row anyway, is where a shard that turned
    /// out not to be uniform would be noticed.
    /// </summary>
    private static async Task<string> DescribeAsync(HttpClient http, ShardListing shard)
    {
        var url = "https://huggingface.co/datasets/CATMuS/medieval/resolve/main/" + shard.Path;

        await using var stream = await HttpRangeStream.OpenAsync(http, url);
        await using var reader = await CatmusShardReader.OpenRemoteAsync(stream, rowGroupLimit: 1);

        var lines = await reader.ReadLinesAsync();
        var first = lines.FirstOrDefault()
            ?? throw new InvalidOperationException("no rows");

        Console.Write($"{stream.BytesFetched / 1024.0:F0} KiB / {stream.RequestCount} req ... ");

        return string.Join('\t',
            shard.Split,
            shard.FileName,
            shard.Bytes.ToString(),
            reader.LineCount.ToString(),
            Clean(first.Shelfmark),
            Clean(first.Language),
            first.Century?.ToString() ?? "",
            Clean(first.ScriptType),
            Clean(first.Genre),
            Clean(first.Verse),
            Clean(first.Project));
    }

    /// <summary>
    /// A tab or a newline inside a field would split the row into a different
    /// number of columns and quietly shift every later value one place left,
    /// so they are removed rather than escaped - none of these fields has any
    /// business containing either.
    /// </summary>
    private static string Clean(string? value) =>
        value == null ? "" : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClassicaCodex.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
