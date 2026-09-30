using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.Tools.CatmusManifests;

/// <summary>
/// Rebuilds CatmusManifests.tsv - which of the 313 CATMuS manuscripts can be
/// looked at as actual pages, and where.
///
/// <b>Why this is resolved once and shipped rather than looked up live.</b>
/// Only one of the three libraries below can be asked a direct question. The
/// Vatican and e-codices build a manifest URL straight out of the shelfmark,
/// but the BnF - which holds half of CATMuS - has no shelfmark index that
/// answers, so the only route is its full-text search, and that search is
/// unreliable: asked for "Espagnol 480" it offers a nineteenth-century
/// bibliography of hunting books. Doing that at run time would put a wrong
/// manuscript in front of a reader, or a spinner in front of a reader, on
/// somebody else's uptime.
///
/// <b>What makes the answers trustworthy anyway.</b> Every Gallica record
/// carries dc:source with its own shelfmark, so a hit is confirmed against the
/// shelfmark that was asked for and discarded otherwise. Nothing is accepted
/// on the search engine's word. Measured 2026-09-28: 116 of 153 BnF items
/// resolve and verify, and the 37 that do not are simply not in Gallica under
/// that shelfmark.
///
/// Run it when CATMuS gains manuscripts, or when a library digitises more:
///
///   dotnet run --project tools/CatmusManifests
///
/// It is deliberately slow - one request at a time with a pause between - both
/// because Gallica throttles (a first run at four at a time silently lost four
/// manuscripts that resolve fine one at a time) and because it is somebody
/// else's search engine.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var output = args.Length > 0
            ? args[0]
            : Path.Combine(FindRepositoryRoot(), "src", "ClassicaCodex.Core", "Catmus", "CatmusManifests.tsv");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);

        // Answers accumulate rather than being replaced. Gallica does not
        // answer reliably over a long run of queries - a first pass found 90
        // where a shorter one had found 116, with no error, just silence - so
        // a run that overwrote its predecessor would throw away confirmed
        // manuscripts every time the search had an off day. Everything here
        // is verified against the record's own shelfmark before it is written,
        // so an answer already in the file is as good as a new one.
        var resolved = LoadExisting(output);
        Console.WriteLine($"{resolved.Count} already resolved; looking for the rest.");

        var manuscripts = CatmusCatalogue.Manuscripts;

        // Three passes over whatever is still missing. A miss is usually the
        // search being unhelpful rather than the manuscript being absent, and
        // the only way to tell is to ask again.
        for (var pass = 1; pass <= 3; pass++)
        {
            var missing = manuscripts.Where(m => !resolved.ContainsKey(m.Shelfmark)).ToList();
            if (missing.Count == 0) break;

            Console.WriteLine();
            Console.WriteLine($"--- pass {pass}: {missing.Count} to try ---");

            var found = 0;
            for (var i = 0; i < missing.Count; i++)
            {
                var shelfmark = missing[i].Shelfmark;
                Console.Write($"[{i + 1,3}/{missing.Count}] {shelfmark,-46} ");
                Console.Out.Flush();

                string? manifest = null;
                try
                {
                    manifest = await ResolveAsync(http, shelfmark);
                }
                catch (Exception ex)
                {
                    Console.Write($"error: {ex.GetBaseException().Message} ");
                }

                if (manifest == null)
                {
                    Console.WriteLine("-");
                    continue;
                }

                resolved[shelfmark] = manifest;
                found++;
                Console.WriteLine(manifest);

                // Written as it goes, so an interrupted run keeps what it found.
                await SaveAsync(output, resolved);
            }

            Console.WriteLine($"--- pass {pass} found {found} ---");
            if (found == 0) break;
        }

        await SaveAsync(output, resolved);

        Console.WriteLine();
        Console.WriteLine($"{resolved.Count} of {manuscripts.Count} resolved ({100.0 * resolved.Count / manuscripts.Count:F0}%)");
        Console.WriteLine($"not digitised, or not findable by shelfmark: {manuscripts.Count - resolved.Count}");
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    private static Dictionary<string, string> LoadExisting(string path)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return resolved;

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 && parts[0].Length > 0) resolved[parts[0]] = parts[1];
        }

        return resolved;
    }

    private static async Task SaveAsync(string path, Dictionary<string, string> resolved)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var rows = resolved
            .OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => $"{r.Key}\t{r.Value}");

        await File.WriteAllLinesAsync(path, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static async Task<string?> ResolveAsync(HttpClient http, string shelfmark)
    {
        if (shelfmark.StartsWith("Paris, BnF", StringComparison.Ordinal))
            return await ResolveGallicaAsync(http, shelfmark);

        if (shelfmark.StartsWith("Vatican, Biblioteca Apostolica Vaticana,", StringComparison.Ordinal))
            return await ResolveVaticanAsync(http, shelfmark);

        if (shelfmark.StartsWith("St. Gallen, Stiftsbibliothek,", StringComparison.Ordinal))
            return await ResolveStGallenAsync(http, shelfmark);

        // Munich, Oxford, the British Library, KBR, the Escorial and Vienna all
        // hold CATMuS manuscripts and all publish IIIF, but none of them
        // answers a shelfmark without going through a search page that returns
        // HTML. They are left out rather than guessed at; adding one is a
        // matter of writing another method here.
        return null;
    }

    /// <summary>
    /// The Vatican builds its manifest URL out of the shelfmark with the
    /// spaces removed: "Reg.lat. 1616" is MSS_Reg.lat.1616. Verified by
    /// fetching it, so a wrong guess is a miss rather than a dead link in the
    /// window.
    /// </summary>
    private static async Task<string?> ResolveVaticanAsync(HttpClient http, string shelfmark)
    {
        var tail = shelfmark["Vatican, Biblioteca Apostolica Vaticana,".Length..].Trim();
        var url = $"https://digi.vatlib.it/iiif/MSS_{tail.Replace(" ", "")}/manifest.json";
        return await ExistsAsync(http, url) ? url : null;
    }

    /// <summary>
    /// e-codices numbers St Gall by a four-digit code: "Cod. Sang. 18" is
    /// csg-0018.
    /// </summary>
    private static async Task<string?> ResolveStGallenAsync(HttpClient http, string shelfmark)
    {
        var match = Regex.Match(shelfmark, @"Cod\.\s*Sang\.\s*(?<number>\d+)");
        if (!match.Success) return null;

        var url = $"https://www.e-codices.unifr.ch/metadata/iiif/csg-{int.Parse(match.Groups["number"].Value):0000}/manifest.json";
        return await ExistsAsync(http, url) ? url : null;
    }

    /// <summary>
    /// Whether a guessed manifest URL is really there.
    ///
    /// A GET rather than a HEAD, and that is not fussiness: digi.vatlib.it
    /// serves every one of its manifests to a GET and refuses a HEAD, so
    /// checking the cheap way reported all three Vatican manuscripts as
    /// undigitised when all three are online.
    /// </summary>
    private static async Task<bool> ExistsAsync(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        return response.IsSuccessStatusCode;
    }

    private static async Task<string?> ResolveGallicaAsync(HttpClient http, string shelfmark)
    {
        foreach (var source in BnfSourceForms(shelfmark))
        {
            var ark = await SearchGallicaAsync(http, source);

            // One at a time with a pause. Four concurrent requests made
            // Gallica answer four of them with nothing at all, and the
            // manuscripts looked undigitised when they are not.
            await Task.Delay(1200);

            if (ark != null) return $"https://gallica.bnf.fr/iiif/ark:/12148/{ark}/manifest.json";
        }

        return null;
    }

    /// <summary>
    /// "Paris, BnF, fr. 1593" is how CATMuS writes it; Gallica writes
    /// "Bibliothèque nationale de France. Département des Manuscrits.
    /// Français 1593". The abbreviations are the whole translation.
    /// </summary>
    private static IEnumerable<string> BnfSourceForms(string shelfmark)
    {
        var tail = shelfmark["Paris, BnF, ".Length..].Trim();

        var match = Regex.Match(tail, @"^(?<prefix>[^0-9]+?)[\s,]+(?<number>[0-9].*)$");
        if (!match.Success)
        {
            yield return tail;
            yield break;
        }

        var prefix = match.Groups["prefix"].Value.TrimEnd('.', ',', '-', ' ').Trim();
        var number = match.Groups["number"].Value.Trim();
        const string manuscripts = "Département des Manuscrits.";

        switch (prefix.ToLowerInvariant())
        {
            case "fr": yield return $"{manuscripts} Français {number}"; break;
            case "lat": yield return $"{manuscripts} Latin {number}"; break;
            case "esp": yield return $"{manuscripts} Espagnol {number}"; break;
            case "ita": yield return $"{manuscripts} Italien {number}"; break;
            case "naf": yield return $"{manuscripts} NAF {number}"; break;
            case "nal": yield return $"{manuscripts} NAL {number}"; break;
            case "smith-lesouëf":
            case "smith-lesouef": yield return $"{manuscripts} Smith-Lesouëf {number}"; break;
            case "arsenal": yield return $"Bibliothèque de l'Arsenal. Ms-{number}"; break;
            case "velins": yield return $"Réserve des livres rares. VELINS-{number}"; break;
            default:
                // Rés. Y2-82 and its kind are early printed books in the
                // Réserve, which is why CATMuS has a "Print" script type.
                yield return $"Réserve des livres rares. {prefix.ToUpperInvariant()}-{number}";
                yield return $"Réserve des livres rares. {prefix.ToUpperInvariant()}{number}";
                break;
        }
    }

    private static async Task<string?> SearchGallicaAsync(HttpClient http, string source)
    {
        var query = Uri.EscapeDataString($"gallica all \"{source}\"");
        var url = $"https://gallica.bnf.fr/SRU?operation=searchRetrieve&version=1.2&maximumRecords=8&query={query}";

        var xml = XDocument.Parse(await http.GetStringAsync(url));
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        foreach (var record in xml.Descendants().Where(e => e.Name.LocalName == "recordData"))
        {
            var sources = record.Descendants(dc + "source").Select(s => s.Value).ToList();

            // THE VERIFICATION, and the reason this tool can be trusted. Asked
            // for "Espagnol 480", Gallica's top hit is a bibliography of books
            // about hunting - so a hit counts only when the record's own
            // shelfmark is the one that was asked for.
            if (!sources.Any(s => Normalise(s).Contains(Normalise(source), StringComparison.Ordinal))) continue;

            var ark = record.Descendants(dc + "identifier")
                .Select(i => Regex.Match(i.Value, @"ark:/12148/(?<ark>[A-Za-z0-9]+)"))
                .FirstOrDefault(m => m.Success)?.Groups["ark"].Value;

            if (ark != null) return ark;
        }

        return null;
    }

    /// <summary>
    /// Accents and punctuation differ between the two catalogues for the same
    /// shelfmark, and neither spelling is wrong - so the comparison is made on
    /// letters and digits alone.
    /// </summary>
    private static string Normalise(string value)
    {
        var stripped = new string(value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return Regex.Replace(stripped.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    }

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
