using System.Text.Json;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Downloads and ingests the Perseus Art &amp; Archaeology collection - six
/// categories of object (vases, coins, gems, sculptures, sites, buildings),
/// plus the shared image-caption table and each category's artifact-to-image
/// index.
///
/// Deliberately does its own downloading here rather than going through the
/// wizard's usual single-file DirectDownload step: this needs thirteen
/// separate files (six category records, six image indexes, one shared
/// caption table), not one, and SetupFetchMode.SelfManaged exists
/// specifically so a source like this can handle its own fetching from
/// inside RunIngest.
///
/// Two categories (gems, sites) have never actually had their JSON records
/// looked at directly - only their sibling categories' attribute lists were
/// confirmed, which showed a clear and consistent pattern (a handful of
/// core fields shared by everything, plus category-specific extras). The
/// description-field fallback chain below is written defensively for
/// exactly that reason: it tries several plausible field names in order
/// and takes whichever is actually present, rather than assuming gems and
/// sites match the confirmed categories exactly.
/// </summary>
public class ArtifactIngestService
{
    private const string BaseUrl = "https://raw.githubusercontent.com/perseus-aa/json/refs/heads/main";

    // FileBase (plural) names the record file - vases.json, coins.json.
    // MapFileBase (singular) names its map file - vase_map.json, not
    // vases_map.json. Confirmed directly for coins (coin_map.json is the
    // one map file ever actually fetched successfully); the other five
    // follow the same singular pattern based on a sidebar file listing.
    private static readonly (string FileBase, string TypeName, string MapFileBase)[] Categories =
    {
        ("vases", "Vase", "vase"),
        ("coins", "Coin", "coin"),
        ("gems", "Gem", "gem"),
        ("sculptures", "Sculpture", "sculpture"),
        ("sites", "Site", "site"),
        ("buildings", "Building", "building")
    };

    public async Task IngestAsync(string destinationRoot, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        var downloadService = new FileDownloadService();
        var recordsDir = Path.Combine(destinationRoot, "full_records");
        var mapsDir = Path.Combine(destinationRoot, "maps");

        // Download every file first - thirteen total, six category records,
        // six image indexes, one shared caption table.
        foreach (var (fileBase, typeName, mapFileBase) in Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"Downloading {typeName} records...");
            await DownloadAsync(
                downloadService,
                $"{BaseUrl}/artifacts/full_records/{fileBase}.json",
                Path.Combine(recordsDir, $"{fileBase}.json"),
                $"{typeName} records", progress, cancellationToken);

            progress.Report($"Downloading {typeName} image index...");
            // Only coin_map.json's location under images/indexes/ was ever
            // actually confirmed by fetching it; the other five category
            // names were inferred from a file listing, not a working
            // fetch. If that inference is wrong for a given category, this
            // falls back to the shallower images/{name}_map.json path
            // rather than failing the whole step over one bad guess.
            await DownloadWithFallbackAsync(
                downloadService,
                primaryUrl: $"{BaseUrl}/images/indexes/{mapFileBase}_map.json",
                fallbackUrl: $"{BaseUrl}/images/{mapFileBase}_map.json",
                localPath: Path.Combine(mapsDir, $"{mapFileBase}_map.json"),
                description: $"{typeName} image index",
                progress, cancellationToken);
        }

        progress.Report("Downloading image captions...");
        var imagesJsonPath = Path.Combine(destinationRoot, "images.json");
        await DownloadAsync(downloadService, $"{BaseUrl}/images/images.json", imagesJsonPath,
            "image captions", progress, cancellationToken);

        // Shared caption/credits lookup, built once and used for every category.
        progress.Report("Reading image captions...");
        var captionsById = new Dictionary<string, (string? Caption, string? Credits)>();
        using (var imagesDoc = JsonDocument.Parse(await File.ReadAllTextAsync(imagesJsonPath, cancellationToken)))
        {
            foreach (var entry in imagesDoc.RootElement.EnumerateArray())
            {
                var imageId = GetString(entry, "id");
                if (imageId == null) continue;
                captionsById[imageId] = (GetString(entry, "caption"), GetString(entry, "credits"));
            }
        }

        var allArtifacts = new List<Artifact>();
        var allImages = new List<ArtifactImage>();
        var knownPlaces = PlaceData.All();

        foreach (var (fileBase, typeName, mapFileBase) in Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"Reading {typeName} records...");

            var recordsPath = Path.Combine(recordsDir, $"{fileBase}.json");
            var mapPath = Path.Combine(mapsDir, $"{mapFileBase}_map.json");

            // Artifact id -> list of image ids, from this category's map file.
            var imageIdsByArtifact = new Dictionary<string, List<string>>();
            if (File.Exists(mapPath))
            {
                using var mapDoc = JsonDocument.Parse(await File.ReadAllTextAsync(mapPath, cancellationToken));
                foreach (var prop in mapDoc.RootElement.EnumerateObject())
                {
                    var ids = new List<string>();
                    foreach (var imageIdEl in prop.Value.EnumerateArray())
                    {
                        var imageId = imageIdEl.GetString();
                        if (imageId != null) ids.Add(imageId);
                    }
                    imageIdsByArtifact[prop.Name] = ids;
                }
            }

            using var recordsDoc = JsonDocument.Parse(await File.ReadAllTextAsync(recordsPath, cancellationToken));
            var recordCount = 0;
            foreach (var record in recordsDoc.RootElement.EnumerateArray())
            {
                var id = GetString(record, "id");
                if (id == null) continue;
                recordCount++;

                var context = GetString(record, "context");
                var matchedPlace = MatchPlace(context, knownPlaces);

                allArtifacts.Add(new Artifact
                {
                    ArtifactId = id,
                    Type = typeName,
                    Name = GetString(record, "name"),
                    Region = GetString(record, "region"),
                    Context = context,
                    MatchedPlaceName = matchedPlace,
                    Period = GetString(record, "period"),
                    StartDate = GetString(record, "start_date"),
                    EndDate = GetString(record, "end_date"),
                    Collection = GetString(record, "collection"),
                    Material = GetString(record, "material"),
                    Location = GetString(record, "location"),
                    Description = PickDescription(record, typeName),
                    PrimaryCitation = GetString(record, "primary_citation")
                });

                if (imageIdsByArtifact.TryGetValue(id, out var imageIds))
                {
                    foreach (var imageId in imageIds)
                    {
                        captionsById.TryGetValue(imageId, out var captionInfo);
                        allImages.Add(new ArtifactImage
                        {
                            ArtifactId = id,
                            ImageId = imageId,
                            Caption = captionInfo.Caption,
                            Credits = captionInfo.Credits
                        });
                    }
                }
            }

            progress.Report($"{typeName}: {recordCount:N0} records.");
        }

        progress.Report($"Saving {allArtifacts.Count:N0} artifacts and {allImages.Count:N0} images...");
        var repo = new ArtifactRepository();
        await repo.ReplaceAllAsync(allArtifacts, allImages, cancellationToken);

        progress.Report($"Done - {allArtifacts.Count:N0} artifacts, {allImages.Count:N0} images.");
    }

    /// <summary>
    /// Downloads one file, and if it fails, says exactly which file and URL
    /// failed - the raw exception alone doesn't say that, and "something
    /// failed" out of thirteen possible files isn't actually actionable
    /// for anyone trying to figure out what broke.
    /// </summary>
    private static async Task DownloadAsync(
        FileDownloadService downloadService, string url, string localPath,
        string description, IProgress<string> progress, CancellationToken cancellationToken)
    {
        try
        {
            await downloadService.DownloadAsync(url, localPath, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Couldn't download {description} from {url}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tries primaryUrl first; on a 404 specifically, tries fallbackUrl
    /// before giving up. Exists for exactly one reason: only one of the six
    /// map files' locations was ever confirmed by an actual successful
    /// fetch (coin_map.json, under images/indexes/) - the other five
    /// category names came from reading a file listing, and if that
    /// listing was misread or the pattern doesn't hold for every category,
    /// this recovers instead of failing the whole step over one bad guess.
    /// </summary>
    private static async Task DownloadWithFallbackAsync(
        FileDownloadService downloadService, string primaryUrl, string fallbackUrl, string localPath,
        string description, IProgress<string> progress, CancellationToken cancellationToken)
    {
        try
        {
            await downloadService.DownloadAsync(primaryUrl, localPath, progress, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            progress.Report($"{description} wasn't at the expected location - trying an alternate path...");
            try
            {
                await downloadService.DownloadAsync(fallbackUrl, localPath, progress, cancellationToken);
            }
            catch (Exception fallbackEx) when (fallbackEx is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Couldn't download {description}. Tried:\n  {primaryUrl}\n  {fallbackUrl}\n" +
                    $"Last error: {fallbackEx.Message}", fallbackEx);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Couldn't download {description} from {primaryUrl}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reuses PlaceData's existing fuzzy match rather than reimplementing
    /// it - context text is often a full sentence ("Athens, from the pyre
    /// of a grave near the Acharnian Gate"), and Contains-based matching
    /// already handles a known place name appearing inside a longer string.
    /// </summary>
    private static string? MatchPlace(
        string? context,
        IReadOnlyList<(string Name, double Lat, double Lon, PlaceKind Kind)> knownPlaces)
    {
        if (string.IsNullOrWhiteSpace(context)) return null;

        foreach (var place in knownPlaces)
        {
            if (context.Contains(place.Name, StringComparison.OrdinalIgnoreCase))
            {
                return place.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// The single richest description field for a category, tried in order
    /// of preference. Vases and coins are confirmed field lists; the
    /// fallback branch is what protects gems and sites, whose actual field
    /// names haven't been directly confirmed.
    /// </summary>
    private static string? PickDescription(JsonElement record, string typeName)
    {
        switch (typeName)
        {
            case "Vase":
                return FirstNonEmpty(
                    GetString(record, "decoration_description"),
                    GetString(record, "summary"),
                    GetString(record, "essay_text"));

            case "Coin":
                var obverse = GetString(record, "obverse_type");
                var reverse = GetString(record, "reverse_type");
                if (obverse == null && reverse == null)
                {
                    return FirstNonEmpty(GetString(record, "summary"));
                }
                var parts = new List<string>();
                if (obverse != null) parts.Add($"Obverse: {obverse}");
                if (reverse != null) parts.Add($"Reverse: {reverse}");
                return string.Join(" ", parts);

            case "Sculpture":
                return FirstNonEmpty(
                    GetString(record, "subject_description"),
                    GetString(record, "form_style_description"),
                    GetString(record, "summary"));

            case "Building":
                return FirstNonEmpty(
                    GetString(record, "history"),
                    GetString(record, "summary"));

            default: // Gem, Site, and anything else - unconfirmed field names, try the common candidates
                return FirstNonEmpty(
                    GetString(record, "summary"),
                    GetString(record, "description"),
                    GetString(record, "decoration_description"),
                    GetString(record, "subject_description"),
                    GetString(record, "history"));
        }
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
