using System.Text;
using System.Text.Json;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.Tools.BfmCatalogue;

/// <summary>
/// Rebuilds BfmTexts.tsv - the list of what the Base de Français Médiéval
/// contains, which the setup step downloads from.
///
/// <b>Why a shipped list.</b> Nakala publishes each text as its own deposit
/// with its own DOI, so finding out what there is means walking two
/// collections a page at a time - 500 deposits over 20 requests before a
/// single byte of text is fetched. Doing that at install time would put a
/// minute of someone else's API in front of the download and make the step
/// fail entirely if Nakala were down. Resolved once and shipped, the download
/// is 500 straight file requests whose sizes and checksums are known in
/// advance.
///
/// Run it when the corpus gains texts:
///
///   dotnet run --project tools/BfmCatalogue
///
/// It writes src/ClassicaCodex.Core/Bfm/BfmTexts.tsv, an embedded resource.
/// </summary>
public static class Program
{
    /// <summary>
    /// The two collections that between them are the whole corpus.
    ///
    /// BFM2022 contains BFM2019 - 219 texts of which 170 are the earlier
    /// release, verified by comparing their DOIs - so taking BFM2019 as well
    /// would import 170 texts twice. The fabliaux are a separate deposit and
    /// overlap neither.
    /// </summary>
    private static readonly (string Id, string Name)[] Collections =
    {
        ("10.34847/nkl.93ee3ts1", "BFM2022"),
        ("10.34847/nkl.1d1b28jr", "Fabliaux")
    };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var output = args.Length > 0
            ? args[0]
            : Path.Combine(FindRepositoryRoot(), "src", "ClassicaCodex.Core", "Bfm", "BfmTexts.tsv");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var rows = new List<string>();

        foreach (var (id, name) in Collections)
        {
            var texts = await ReadCollectionAsync(http, id, name);
            Console.WriteLine($"{name}: {texts.Count} texts");
            rows.AddRange(texts);
        }

        rows.Sort(StringComparer.Ordinal);

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllLinesAsync(output, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine();
        Console.WriteLine($"{rows.Count} texts written to {output}");
        return 0;
    }

    private static async Task<List<string>> ReadCollectionAsync(HttpClient http, string id, string collection)
    {
        var rows = new List<string>();
        var page = 1;
        var lastPage = 1;

        while (page <= lastPage)
        {
            var json = await http.GetStringAsync(
                $"https://api.nakala.fr/collections/{id}/datas?page={page}&limit=50");

            using var document = JsonDocument.Parse(json);
            lastPage = document.RootElement.GetProperty("lastPage").GetInt32();

            foreach (var deposit in document.RootElement.GetProperty("data").EnumerateArray())
            {
                var row = Describe(deposit, collection);
                if (row != null) rows.Add(row);
            }

            Console.Write($"\r  {collection} page {page}/{lastPage}, {rows.Count} texts   ");
            page++;
        }

        Console.WriteLine();
        return rows;
    }

    private static string? Describe(JsonElement deposit, string collection)
    {
        var doi = deposit.GetProperty("identifier").GetString();
        if (doi == null) return null;

        // Each deposit is one TEI file. A deposit with none, or with
        // something that is not TEI, is not a text and is skipped rather than
        // written down as one with no file to fetch.
        var file = deposit.GetProperty("files").EnumerateArray()
            .FirstOrDefault(f => (f.GetProperty("name").GetString() ?? "")
                .EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        if (file.ValueKind != JsonValueKind.Object) return null;

        var name = file.GetProperty("name").GetString() ?? "";
        var sha1 = file.GetProperty("sha1").GetString() ?? "";
        var size = file.GetProperty("size").GetString() ?? "0";

        return string.Join('\t',
            collection,
            doi,
            sha1,
            name,
            size,
            Clean(Meta(deposit, "#title")),
            Clean(Meta(deposit, "creator")),
            Clean(Meta(deposit, "created")),
            Clean(Meta(deposit, "#license")),
            Clean(Meta(deposit, "description")));
    }

    /// <summary>
    /// One metadata value by the tail of its property URI - "#title" for
    /// Nakala's own terms, "creator" and "description" for Dublin Core.
    /// </summary>
    private static string? Meta(JsonElement deposit, string propertySuffix)
    {
        if (!deposit.TryGetProperty("metas", out var metas)) return null;

        foreach (var meta in metas.EnumerateArray())
        {
            var property = meta.GetProperty("propertyUri").GetString();
            if (property == null || !property.EndsWith(propertySuffix, StringComparison.Ordinal)) continue;

            if (meta.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// A tab or a newline inside a field would shift every later column, and
    /// the descriptions are free text that may contain either.
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
