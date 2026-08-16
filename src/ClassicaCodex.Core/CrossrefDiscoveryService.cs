using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

/// <summary>Small, read-only Crossref metadata lookup. A hit is a reading lead, never evidence of what a paper argues.</summary>
public static partial class CrossrefDiscoveryService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45) };

    public static async Task<IReadOnlyList<ScholarlyReadingLead>> SearchAsync(
        string query, int rows = 15, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        rows = Math.Clamp(rows, 1, 25);
        var url = "https://api.crossref.org/works?query.bibliographic=" + Uri.EscapeDataString(query.Trim()) +
                  $"&rows={rows}&select=DOI,title,author,published,container-title,publisher,URL,abstract";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ClassicaCodex/3.1 (research-metadata-discovery)");
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var items = doc.RootElement.GetProperty("message").GetProperty("items");
        var result = new List<ScholarlyReadingLead>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            var doi = String(item, "DOI"); var title = First(item, "title");
            if (string.IsNullOrWhiteSpace(doi) || string.IsNullOrWhiteSpace(title) || !seen.Add(doi)) continue;
            var authors = new List<string>();
            if (item.TryGetProperty("author", out var authorList) && authorList.ValueKind == JsonValueKind.Array)
                foreach (var author in authorList.EnumerateArray())
                {
                    var family = String(author, "family"); var given = String(author, "given");
                    var name = string.Join(", ", new[] { family, given }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (name.Length > 0) authors.Add(name);
                }
            string? year = null;
            if (item.TryGetProperty("published", out var published) && published.TryGetProperty("date-parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0
                && parts[0].ValueKind == JsonValueKind.Array && parts[0].GetArrayLength() > 0)
                year = parts[0][0].ToString();
            var abstractText = String(item, "abstract");
            if (abstractText != null) abstractText = WebUtility.HtmlDecode(Tags().Replace(abstractText, " ")).Trim();
            if (abstractText?.Length > 3_000) abstractText = abstractText[..3_000] + "…";
            result.Add(new ScholarlyReadingLead($"R{result.Count + 1:000}", title, authors, year,
                First(item, "container-title"), String(item, "publisher"), doi, String(item, "URL"), abstractText));
        }
        return result;
    }

    private static string? String(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    private static string? First(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0 ? value[0].GetString()?.Trim() : null;
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
}
