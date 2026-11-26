using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClassicaCodex.Ingestion.Sefaria;

namespace ClassicaCodex.Tools.RabbinicCatalogue;

/// <summary>
/// Rebuilds RabbinicTexts.tsv - which versions of the Mishnah, the Tosefta,
/// both Talmuds, the minor tractates and the Targumim the rabbinic step
/// installs.
///
/// <b>Licences decide, and only the file says.</b> Sefaria's index lists about
/// 20,000 versions and no licence for any of them; the licence is a field at
/// the head of each file. So every candidate is asked with a one-kilobyte
/// range request, and a version whose licence is non-commercial or "unknown"
/// is not taken, whatever else is true of it.
///
/// <b>Half a translation is taken, a few lines are not.</b> Sefaria's
/// community English reaches some tractates completely and others for a verse
/// or two, and a pane that is empty past the first page says the wrong thing
/// about what is there. A translation has to cover half of its text.
///
/// Run it when the versions should change:
///
///   dotnet run --project tools/RabbinicCatalogue
///
/// It writes src/ClassicaCodex.Core/Rabbinic/RabbinicTexts.tsv, an embedded
/// resource, and prints what it left out and why.
/// </summary>
public static class Program
{
    private const string IndexUrl = "https://raw.githubusercontent.com/Sefaria/Sefaria-Export/master/books.json";
    private const string OrderUrl = "https://storage.googleapis.com/sefaria-export/table_of_contents.json";
    private const string OutputPath = "src/ClassicaCodex.Core/Rabbinic/RabbinicTexts.tsv";

    private const double MinimumCoverage = 0.5;

    private const string Hebrew = "heb";
    private const string Aramaic = "arc";
    private const string English = "eng";
    private const string German = "deu";

    private const string CommunityVersion = "Sefaria Community Translation";

    private static readonly HashSet<string> OpenLicences = new(StringComparer.OrdinalIgnoreCase)
    {
        "Public Domain", "PD", "CC0", "CC-BY", "CC-BY-SA"
    };

    /// <summary>In the library already, by hand, with the demons in them.</summary>
    private static readonly HashSet<string> InstalledTractates = new(StringComparer.Ordinal)
    {
        "Berakhot", "Shabbat", "Pesachim", "Chagigah", "Gittin"
    };

    /// <summary>Not Targum in the sense these are: Saadia's Arabic, and a text with no open version.</summary>
    private static readonly HashSet<string> LeftOutTargums = new(StringComparer.Ordinal)
    {
        "Tafsir Rasag", "Targum Neofiti"
    };

    /// <summary>The Pentateuch in Sefaria's "Targum Jonathan" is Pseudo-Jonathan; the Prophets are Jonathan.</summary>
    private static readonly HashSet<string> Torah = new(StringComparer.Ordinal)
    {
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy"
    };

    private sealed record Book(string Title, string Folder, string Version, string[] Categories, string Url);

    private sealed record Head(string Licence, string Language, long Bytes);

    private sealed record Row(
        string Corpus, string Slug, string Title, string Version, string Suffix, bool IsTranslation,
        string Language, string? Translator, string? Orthography, string? Scheme, string Licence, long Bytes,
        string ExportPath);

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(16);
    private static readonly List<string> LeftOut = new();

    private static List<Book> _books = new();
    private static Dictionary<string, Head> _heads = new();
    private static string _cache = string.Empty;

    public static async Task<int> Main()
    {
        if (!Directory.Exists("src/ClassicaCodex.Core"))
        {
            Console.Error.WriteLine("Run this from the repository root.");
            return 1;
        }

        _cache = Path.Combine(Path.GetTempPath(), "rabbinic-catalogue");
        Directory.CreateDirectory(_cache);

        _books = LoadBooks(await Http.GetStringAsync(IndexUrl));
        var order = LoadOrder(await Http.GetStringAsync(OrderUrl));

        var candidates = _books.Where(b => CorpusOf(b) != null).ToList();
        Console.WriteLine($"{_books.Count:N0} versions in the index, {candidates.Count:N0} candidates.");

        _heads = (await Task.WhenAll(candidates.Select(async b => (b.Url, Head: await ReadHeadAsync(b)))))
            .ToDictionary(x => x.Url, x => x.Head);

        var rows = new List<Row>();
        foreach (var title in Titles(candidates, "mishnah", order)) rows.AddRange(Mishnah(title));
        foreach (var title in Titles(candidates, "tosefta", order)) rows.AddRange(await ToseftaAsync(title));
        foreach (var title in Titles(candidates, "bavli", order)) rows.AddRange(await BavliAsync(title));
        foreach (var title in Titles(candidates, "yerushalmi", order)) rows.AddRange(Yerushalmi(title));
        foreach (var title in Titles(candidates, "minor", order)) rows.AddRange(await MinorAsync(title));
        foreach (var title in Titles(candidates, "targum", order)) rows.AddRange(await TargumAsync(title));

        var text = new StringBuilder();
        foreach (var r in rows)
        {
            text.Append(string.Join('\t', r.Corpus, r.Slug, r.Title, r.Version, r.Suffix, r.IsTranslation ? "T" : "O",
                r.Language, r.Translator ?? "", r.Orthography ?? "", r.Scheme ?? "", r.Licence, r.Bytes, r.ExportPath));
            text.Append('\n');
        }

        await File.WriteAllTextAsync(OutputPath, text.ToString(), new UTF8Encoding(false));

        foreach (var group in rows.GroupBy(r => r.Corpus))
        {
            Console.WriteLine($"{group.Key,-11} {group.Select(r => r.Slug).Distinct().Count(),3} works, "
                + $"{group.Count(),4} versions, {group.Sum(r => r.Bytes) / 1e6,6:F1} MB, "
                + string.Join(", ", group.GroupBy(r => r.Licence).Select(l => $"{l.Key} {l.Count()}")));
        }

        Console.WriteLine($"Total: {rows.Select(r => r.Slug).Distinct().Count()} works, {rows.Count} versions, "
            + $"{rows.Sum(r => r.Bytes) / 1e6:F1} MB.");

        Console.WriteLine();
        foreach (var line in LeftOut) Console.WriteLine(line);
        return 0;
    }

    private static IEnumerable<Row> Mishnah(string title)
    {
        var name = Tractate(title["Mishnah ".Length..]);
        if (title == "Pirkei Avot") name = "Avot";
        var slug = $"mishnah-{Slug(name)}";

        foreach (var row in new[]
                 {
                     Take("mishnah", slug, name, title, "Torat Emet 357", "torat-emet", false, Hebrew),
                     Take("mishnah", slug, name, title, "Mishnah based on the Kaufmann manuscript", "kaufmann", false,
                         Hebrew, orthography: "kaufmann"),
                     Take("mishnah", slug, name, title, "Mishnah Yomit by Dr. Joshua Kulp", "kulp", true, English,
                         "Joshua Kulp"),
                     Take("mishnah", slug, name, title, "Talmud Bavli. German. Lazarus Goldschmidt", "goldschmidt", true,
                         German, "Lazarus Goldschmidt (1929)")
                 })
        {
            if (row != null) yield return row;
        }
    }

    private static async Task<IEnumerable<Row>> ToseftaAsync(string title)
    {
        var name = Tractate(title["Tosefta ".Length..]);
        var slug = $"tosefta-{Slug(name)}";
        var rows = new List<Row?>();

        var vilna = LargestOpen(title, "Hebrew");
        rows.Add(vilna == null ? null : ToRow("tosefta", slug, name, vilna, "vilna", false, Hebrew));

        rows.Add(Take("tosefta", slug, name, $"{title.Replace("'", "")} (Lieberman)", "The Tosefta according to",
            "lieberman", false, Hebrew, orthography: "lieberman"));

        if (vilna != null) rows.Add(await EnglishAsync("tosefta", slug, name, title, vilna, talmud: false));

        return rows.OfType<Row>();
    }

    private static async Task<IEnumerable<Row>> BavliAsync(string title)
    {
        if (InstalledTractates.Contains(title)) return Array.Empty<Row>();

        var name = Tractate(title);
        var slug = Slug(title);
        var rows = new List<Row?>
        {
            Take("bavli", slug, name, title, "Wikisource Talmud Bavli", "vilna", false, Aramaic),
            Take("bavli", slug, name, title, "Talmud Bavli. German trans. by Lazarus Goldschmidt", "goldschmidt", true,
                German, "Lazarus Goldschmidt (1929)")
        };

        var text = Find(title, "Wikisource Talmud Bavli");
        if (text != null) rows.Add(await EnglishAsync("bavli", slug, name, title, text, talmud: true));

        return rows.OfType<Row>();
    }

    private static IEnumerable<Row> Yerushalmi(string title)
    {
        var name = Tractate(title["Jerusalem Talmud ".Length..]);
        var slug = $"yerushalmi-{Slug(name)}";

        foreach (var row in new[]
                 {
                     Take("yerushalmi", slug, name, title, "The Jerusalem Talmud, edition by Heinrich W. Guggenheimer",
                         "guggenheimer", false, Aramaic),
                     Take("yerushalmi", slug, name, title, "The Jerusalem Talmud, translation and commentary by Heinrich",
                         "guggenheimer-en", true, English, "Heinrich W. Guggenheimer")
                 })
        {
            if (row != null) yield return row;
        }
    }

    private static async Task<IEnumerable<Row>> MinorAsync(string title)
    {
        var name = title.StartsWith("Tractate ", StringComparison.Ordinal) ? title["Tractate ".Length..] : title;
        var slug = Slug(name);

        var text = LargestOpen(title, "Hebrew");
        var rows = new List<Row?>
        {
            text == null ? null : ToRow("minor", slug, name, text, "vilna", false, Hebrew),
            Take("minor", slug, name, title, "The Minor Tractates of the Talmud, trans. A. Cohen", "cohen", true, English,
                "A. Cohen (1965)")
        };

        if (text != null && rows[1] == null) rows.Add(await EnglishAsync("minor", slug, name, title, text, talmud: false));

        return rows.OfType<Row>();
    }

    private static async Task<IEnumerable<Row>> TargumAsync(string title)
    {
        var (slug, name) = TargumName(title);

        // Onkelos comes in a public-domain text and a CC BY-SA vocalisation of
        // the same words. The one with no conditions is the one to take.
        var text = LargestOpen(title, "Hebrew", publicDomainFirst: true);
        if (text == null)
        {
            LeftOut.Add($"{title}: no open text.");
            return Array.Empty<Row>();
        }

        var depth = Depth(JsonDocument.Parse(await ReadFileAsync(text)).RootElement.GetProperty("text"));
        var scheme = depth == 3 ? "Book.Chapter.Verse" : null;

        var rows = new List<Row?> { ToRow("targum", slug, name, text, "text", false, Aramaic) with { Scheme = scheme } };
        var english = await EnglishAsync("targum", slug, name, title, text, talmud: false);
        rows.Add(english == null ? null : english with { Scheme = scheme });

        return rows.OfType<Row>();
    }

    private static (string Slug, string Name) TargumName(string title)
    {
        if (title.StartsWith("Onkelos ", StringComparison.Ordinal))
        {
            var book = title["Onkelos ".Length..];
            return ($"onkelos-{Slug(book)}", $"Onkelos: {book}");
        }

        if (title.StartsWith("Targum Jonathan on ", StringComparison.Ordinal))
        {
            var book = title["Targum Jonathan on ".Length..];
            return Torah.Contains(book)
                ? ($"pseudo-jonathan-{Slug(book)}", $"Pseudo-Jonathan: {book}")
                : ($"jonathan-{Slug(book)}", $"Jonathan: {book}");
        }

        if (title.StartsWith("Aramaic Targum to ", StringComparison.Ordinal))
        {
            var book = title["Aramaic Targum to ".Length..];
            return ($"targum-{Slug(book)}", $"Targum to {book}");
        }

        return title switch
        {
            "Targum of I Chronicles" => ("targum-1-chronicles", "Targum to 1 Chronicles"),
            "Targum of II Chronicles" => ("targum-2-chronicles", "Targum to 2 Chronicles"),
            "Targum Sheni on Esther" => ("targum-sheni-esther", "Targum Sheni to Esther"),
            "Targum Jerusalem" => ("targum-jerusalem", "Jerusalem Targum"),
            _ => throw new InvalidOperationException($"No name for the Targum \"{title}\".")
        };
    }

    /// <summary>
    /// The fullest open English of a text, if it reaches half of it. Citations
    /// are compared rather than sizes, because a translation is routinely
    /// longer than its original.
    /// </summary>
    private static async Task<Row?> EnglishAsync(
        string corpus, string slug, string name, string title, Book original, bool talmud)
    {
        var versions = _books
            .Where(b => b.Title == title && b.Folder == "English" && b.Version != "merged"
                        && _heads.TryGetValue(b.Url, out var h) && h.Language is "en" or ""
                        && OpenLicences.Contains(_heads[b.Url].Licence))
            .OrderByDescending(b => _heads[b.Url].Bytes)
            .ToList();

        if (versions.Count == 0) return null;

        var text = Citations(await ReadFileAsync(original), talmud);
        foreach (var version in versions)
        {
            var english = Citations(await ReadFileAsync(version), talmud);
            var coverage = text.Count == 0 ? 0 : (double)english.Count(text.Contains) / text.Count;

            if (coverage >= MinimumCoverage)
            {
                var suffix = version.Version == CommunityVersion ? "community" : "english";
                var translator = version.Version == CommunityVersion ? CommunityVersion : TranslatorOf(version.Version);
                if (corpus == "bavli" || translator == null)
                    LeftOutNote($"{name}: taking \"{version.Version}\" at {coverage:P0}.");

                return ToRow(corpus, slug, name, version, suffix, true, English, translator ?? version.Version);
            }

            LeftOutNote($"{name}: \"{version.Version}\" covers {coverage:P0}, left out.");
        }

        return null;
    }

    private static string? TranslatorOf(string version)
    {
        if (version.Contains("Etheridge", StringComparison.OrdinalIgnoreCase)) return "J. W. Etheridge (1862)";
        if (version.Contains("Ginsburg", StringComparison.OrdinalIgnoreCase)) return "C. D. Ginsburg (1861)";
        if (version.Contains("Chaldee Paraphrase", StringComparison.OrdinalIgnoreCase)) return "C. W. H. Pauli (1871)";
        return null;
    }

    private static void LeftOutNote(string line)
    {
        lock (LeftOut) LeftOut.Add(line);
    }

    private static HashSet<string> Citations(string json, bool talmud)
    {
        using var export = SefariaExportLoader.Parse(json);
        var passages = talmud ? SefariaExportLoader.FlattenTalmud(export.Text) : SefariaExportLoader.Flatten(export.Text);
        return passages.Where(p => !p.IsHead).Select(p => p.Citation).ToHashSet(StringComparer.Ordinal);
    }

    private static int Depth(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Array => 1 + (node.GetArrayLength() == 0 ? 0 : node.EnumerateArray().Max(Depth)),
        JsonValueKind.Object => 1 + node.EnumerateObject().Select(p => Depth(p.Value)).DefaultIfEmpty(0).Max(),
        _ => 0
    };

    private static Row? Take(
        string corpus, string slug, string name, string title, string versionPrefix, string suffix, bool isTranslation,
        string language, string? translator = null, string? orthography = null)
    {
        var book = Find(title, versionPrefix);
        if (book == null)
        {
            LeftOutNote($"{name}: no \"{versionPrefix}\" under \"{title}\".");
            return null;
        }

        if (!OpenLicences.Contains(_heads[book.Url].Licence))
        {
            LeftOutNote($"{name}: \"{book.Version}\" is \"{_heads[book.Url].Licence}\", left out.");
            return null;
        }

        return ToRow(corpus, slug, name, book, suffix, isTranslation, language, translator, orthography);
    }

    private static Row ToRow(
        string corpus, string slug, string name, Book book, string suffix, bool isTranslation, string language,
        string? translator = null, string? orthography = null)
    {
        var head = _heads[book.Url];
        var path = book.Url[(book.Url.IndexOf("/json/", StringComparison.Ordinal) + "/json/".Length)..];
        return new Row(corpus, slug, name, book.Version, suffix, isTranslation, language, translator, orthography,
            null, head.Licence, head.Bytes, path);
    }

    private static Book? Find(string title, string versionPrefix) =>
        _books.FirstOrDefault(b => b.Title == title && b.Version.StartsWith(versionPrefix, StringComparison.Ordinal)
                                                    && _heads.ContainsKey(b.Url));

    private static Book? LargestOpen(string title, string folder, bool publicDomainFirst = false)
    {
        var open = _books
            .Where(b => b.Title == title && b.Folder == folder && b.Version != "merged" && _heads.ContainsKey(b.Url)
                        && OpenLicences.Contains(_heads[b.Url].Licence))
            .ToList();

        return open
            .OrderByDescending(b => publicDomainFirst && _heads[b.Url].Licence is "Public Domain" or "PD")
            .ThenByDescending(b => _heads[b.Url].Bytes)
            .FirstOrDefault();
    }

    /// <summary>Which of the six corpora a version belongs to, or null.</summary>
    private static string? CorpusOf(Book b)
    {
        var c = b.Categories;
        if (b.Version == "merged" || c.Contains("Commentary")) return null;

        string At(int i) => c.Length > i ? c[i] : string.Empty;
        bool Seder(int i) => At(i).StartsWith("Seder ", StringComparison.Ordinal);

        if (c[0] == "Mishnah" && Seder(1)) return "mishnah";
        if (c[0] == "Tosefta" && At(1) == "Vilna Edition" && Seder(2)) return "tosefta";
        if (c[0] == "Tosefta" && At(1) == "Lieberman Edition" && Seder(2)) return "tosefta-lieberman";
        if (c[0] == "Talmud" && At(1) == "Bavli" && Seder(2)) return "bavli";
        if (c[0] == "Talmud" && At(1) == "Bavli" && At(2) == "Minor Tractates") return "minor";
        if (c[0] == "Talmud" && At(1) == "Yerushalmi" && Seder(2)) return "yerushalmi";
        if (c[0] == "Tanakh" && At(1) == "Targum" && !LeftOutTargums.Contains(b.Title)) return "targum";

        return null;
    }

    private static IEnumerable<string> Titles(IEnumerable<Book> candidates, string corpus, Dictionary<string, int> order) =>
        candidates.Where(b => CorpusOf(b) == corpus)
            .Select(b => b.Title)
            .Distinct()
            .OrderBy(t => order.TryGetValue(t, out var rank) ? rank : int.MaxValue)
            .ThenBy(t => t, StringComparer.Ordinal);

    /// <summary>
    /// One name for a tractate across the corpora. Sefaria spells Ta'anit two
    /// ways and Oktzin two ways, and the library has always said Hagigah.
    /// </summary>
    private static string Tractate(string name) => name.Replace("'", "") switch
    {
        "Chagigah" => "Hagigah",
        "Oktsin" => "Oktzin",
        var plain => plain
    };

    private static string Slug(string name) =>
        Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    private static List<Book> LoadBooks(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("books").EnumerateArray().Select(b => new Book(
            b.GetProperty("title").GetString() ?? "",
            b.GetProperty("language").GetString() ?? "",
            b.GetProperty("versionTitle").GetString() ?? "",
            b.GetProperty("categories").EnumerateArray().Select(c => c.GetString() ?? "").ToArray(),
            b.GetProperty("json_url").GetString() ?? "")).ToList();
    }

    /// <summary>Titles in the order of Sefaria's table of contents, which is the traditional order.</summary>
    private static Dictionary<string, int> LoadOrder(string json)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Walk(child);
                return;
            }

            if (node.ValueKind != JsonValueKind.Object) return;

            if (node.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } name)
                order.TryAdd(name, order.Count);

            if (node.TryGetProperty("contents", out var contents)) Walk(contents);
        }

        using var document = JsonDocument.Parse(json);
        Walk(document.RootElement);
        return order;
    }

    private static async Task<Head> ReadHeadAsync(Book book)
    {
        await Gate.WaitAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, book.Url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1100);

            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var head = await response.Content.ReadAsStringAsync();
            string Field(string name) =>
                Regex.Match(head, $"\"{name}\":\\s*\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : string.Empty;

            return new Head(Field("license"), Field("actualLanguage"), response.Content.Headers.ContentRange?.Length ?? 0);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> ReadFileAsync(Book book)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(book.Url)))[..24];
        var path = Path.Combine(_cache, $"{name}.json");
        if (File.Exists(path)) return await File.ReadAllTextAsync(path);

        // Sefaria's bucket drops the odd connection on a run this long.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var json = await Http.GetStringAsync(book.Url);
                await File.WriteAllTextAsync(path, json);
                return json;
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClassicaCodex-RabbinicCatalogue");
        return client;
    }
}
