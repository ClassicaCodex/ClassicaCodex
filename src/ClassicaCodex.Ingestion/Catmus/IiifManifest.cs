using System.Text.Json;

namespace ClassicaCodex.Ingestion.Catmus;

/// <summary>One leaf of a digitised manuscript.</summary>
public sealed class IiifPage
{
    /// <summary>What the library calls it: "f. 60", "page de garde recto", "1r".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The IIIF Image API service for this leaf, without a trailing slash -
    /// the base that <see cref="ImageUrl"/> builds a request on.
    /// </summary>
    public string ImageService { get; init; } = string.Empty;

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>
    /// This leaf at a given width, in pixels, with the height following the
    /// page's own proportions.
    ///
    /// Asking for a width rather than the whole image is the point: a leaf of
    /// Arsenal 3516 is 2857 by 4096, which is four megabytes to look at on a
    /// screen that can show a thousand pixels of it.
    ///
    /// <b>"default", not "native".</b> They are the same thing under different
    /// names - version 1 of the IIIF Image API called the unaltered image
    /// native and version 2 renamed it default. Gallica runs version 1 and
    /// answers to both; e-codices runs version 2 and answers 404 to native,
    /// so the version 1 spelling would have left every St Gall page blank.
    /// </summary>
    public string ImageUrl(int width) => $"{ImageService}/full/,{width}/0/default.jpg";
}

/// <summary>
/// A digitised manuscript as its holding library publishes it: the leaves,
/// and the terms they come with.
///
/// <b>Nothing here is downloaded or cached.</b> The images are fetched from
/// the library's own server when a page is looked at and kept only as long as
/// it is on screen - the same rule the Art &amp; Archaeology browser follows
/// for Perseus, and for the same reason: these are photographs somebody else
/// owns, published under their terms and not ours. <see cref="Attribution"/>
/// and <see cref="LicenceUrl"/> are read out of the manifest rather than
/// assumed, because they differ by library and, for the four libraries behind
/// CATMuS, by manuscript.
/// </summary>
public sealed class IiifManifest
{
    public string Label { get; init; } = string.Empty;

    /// <summary>Who to credit, in the library's own words. "Bibliothèque nationale de France".</summary>
    public string? Attribution { get; init; }

    /// <summary>Where the library states its conditions of use.</summary>
    public string? LicenceUrl { get; init; }

    /// <summary>The library's own page for this manuscript, for a reader who wants the catalogue entry.</summary>
    public string? RelatedUrl { get; init; }

    public IReadOnlyList<IiifPage> Pages { get; init; } = Array.Empty<IiifPage>();

    /// <summary>
    /// Fetches and reads a IIIF Presentation manifest.
    ///
    /// Version 2 only, which is what all three libraries behind CATMuS serve.
    /// A version 3 manifest has items rather than sequences and would come
    /// back with no pages rather than wrong ones - the window says so instead
    /// of showing an empty frame.
    /// </summary>
    public static async Task<IiifManifest> LoadAsync(
        HttpClient http, string manifestUrl, CancellationToken cancellationToken = default)
    {
        await using var stream = await http.GetStreamAsync(manifestUrl, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        return new IiifManifest
        {
            Label = Text(root, "label") ?? string.Empty,
            Attribution = Text(root, "attribution"),
            LicenceUrl = Text(root, "license"),
            RelatedUrl = Text(root, "related"),
            Pages = ReadPages(root)
        };
    }

    private static List<IiifPage> ReadPages(JsonElement root)
    {
        var pages = new List<IiifPage>();

        if (!root.TryGetProperty("sequences", out var sequences) || sequences.ValueKind != JsonValueKind.Array)
        {
            return pages;
        }

        foreach (var sequence in sequences.EnumerateArray())
        {
            if (!sequence.TryGetProperty("canvases", out var canvases) || canvases.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var canvas in canvases.EnumerateArray())
            {
                var service = ImageServiceOf(canvas);
                if (service == null) continue;

                pages.Add(new IiifPage
                {
                    Label = Text(canvas, "label") ?? $"[{pages.Count + 1}]",
                    ImageService = service.TrimEnd('/'),
                    Width = Number(canvas, "width"),
                    Height = Number(canvas, "height")
                });
            }
        }

        return pages;
    }

    /// <summary>
    /// The image service id buried at canvas → images[0] → resource → service.
    ///
    /// The resource also carries a direct @id for the full-size JPEG, but that
    /// one is the whole leaf at full resolution and nothing else; the service
    /// is what allows asking for a width.
    /// </summary>
    private static string? ImageServiceOf(JsonElement canvas)
    {
        if (!canvas.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var image in images.EnumerateArray())
        {
            if (!image.TryGetProperty("resource", out var resource)) continue;
            if (!resource.TryGetProperty("service", out var service)) continue;

            var id = Text(service, "@id");
            if (!string.IsNullOrEmpty(id)) return id;
        }

        return null;
    }

    /// <summary>
    /// A IIIF string property, which may be a plain string or a language map.
    /// Version 2 usually writes a string; some publishers write
    /// { "@value": ..., "@language": ... } for the same field.
    /// </summary>
    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object => value.TryGetProperty("@value", out var inner) ? inner.GetString() : null,
            JsonValueKind.Array => value.EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : v.TryGetProperty("@value", out var inner) ? inner.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v)),
            _ => null
        };
    }

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
