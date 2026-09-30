using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Ingestion.Catmus;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading a library's IIIF manifest, and the list of which manuscripts have
/// one.
///
/// The manifest fixture below is the real shape Gallica serves, trimmed to two
/// leaves - the nesting is the part worth pinning, because the image service
/// that a page is actually fetched from sits four levels down at
/// canvas → images → resource → service, and a reader that stops one level
/// short finds the full-resolution JPEG instead and downloads four megabytes
/// to show a thousand pixels.
/// </summary>
public class IiifManifestTests
{
    private const string GallicaManifest = """
    {
      "@id": "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/manifest.json",
      "label": "BnF. Bibliothèque de l'Arsenal. Ms-3516",
      "attribution": "Bibliothèque nationale de France",
      "license": "https://gallica.bnf.fr/html/und/conditions-dutilisation-des-contenus-de-gallica",
      "related": "https://gallica.bnf.fr/ark:/12148/btv1b55000507q",
      "sequences": [ {
        "canvases": [ {
          "@id": "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/canvas/f1",
          "label": "page de garde recto",
          "height": 4096,
          "width": 2857,
          "images": [ {
            "resource": {
              "service": { "@id": "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/f1" },
              "@id": "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/f1/full/full/0/native.jpg"
            }
          } ]
        }, {
          "@id": "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/canvas/f60",
          "label": "f. 60",
          "height": 4096,
          "width": 2857,
          "images": [ {
            "resource": {
              "service": { "@id": "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/f60" }
            }
          } ]
        } ]
      } ]
    }
    """;

    private static async Task<IiifManifest> ReadAsync(string json)
    {
        var handler = new StubHandler(json);
        using var http = new HttpClient(handler);
        return await IiifManifest.LoadAsync(http, "https://example.invalid/manifest.json");
    }

    [Fact]
    public async Task TheLeavesAndTheirImageServicesAreRead()
    {
        var manifest = await ReadAsync(GallicaManifest);

        Assert.Equal("BnF. Bibliothèque de l'Arsenal. Ms-3516", manifest.Label);
        Assert.Equal(2, manifest.Pages.Count);
        Assert.Equal("page de garde recto", manifest.Pages[0].Label);
        Assert.Equal("f. 60", manifest.Pages[1].Label);
        Assert.Equal(2857, manifest.Pages[0].Width);
        Assert.Equal(4096, manifest.Pages[0].Height);

        // The service, not the resource's own full-resolution @id.
        Assert.Equal("https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/f60",
            manifest.Pages[1].ImageService);
    }

    /// <summary>
    /// Asking for a width is what keeps a page turn to a few hundred
    /// kilobytes instead of the four megabytes a 2857x4096 leaf weighs.
    /// </summary>
    [Fact]
    public async Task APageIsRequestedAtAChosenWidth()
    {
        var manifest = await ReadAsync(GallicaManifest);

        Assert.Equal(
            "https://gallica.bnf.fr/iiif/ark:/12148/btv1b55000507q/f60/full/,1200/0/default.jpg",
            manifest.Pages[1].ImageUrl(1200));
    }

    /// <summary>
    /// <b>The terms come out of the manifest, never from an assumption.</b>
    /// Four different regimes cover the photographs behind CATMuS, so a window
    /// that credited them all the same way would be wrong for most of them.
    /// </summary>
    [Fact]
    public async Task TheCreditAndTheConditionsComeFromTheLibrary()
    {
        var manifest = await ReadAsync(GallicaManifest);

        Assert.Equal("Bibliothèque nationale de France", manifest.Attribution);
        Assert.Equal("https://gallica.bnf.fr/html/und/conditions-dutilisation-des-contenus-de-gallica",
            manifest.LicenceUrl);
        Assert.Equal("https://gallica.bnf.fr/ark:/12148/btv1b55000507q", manifest.RelatedUrl);
    }

    /// <summary>
    /// Some publishers write a IIIF string as a language map rather than a
    /// plain string. Both have to read, or a manifest arrives with a blank
    /// credit on it.
    /// </summary>
    [Fact]
    public async Task ALanguageMapLabelIsReadAsText()
    {
        var manifest = await ReadAsync("""
        {
          "label": { "@value": "Cod. Sang. 18", "@language": "de" },
          "attribution": [ { "@value": "Stiftsbibliothek St. Gallen", "@language": "de" } ],
          "sequences": []
        }
        """);

        Assert.Equal("Cod. Sang. 18", manifest.Label);
        Assert.Equal("Stiftsbibliothek St. Gallen", manifest.Attribution);
    }

    /// <summary>
    /// A version 3 manifest has items where version 2 has sequences. It comes
    /// back with no pages, which the window reports - rather than with pages
    /// read out of the wrong place.
    /// </summary>
    [Fact]
    public async Task AVersionThreeManifestYieldsNoPagesRatherThanWrongOnes()
    {
        var manifest = await ReadAsync("""
        {
          "@context": "http://iiif.io/api/presentation/3/context.json",
          "label": { "en": [ "A version 3 manifest" ] },
          "items": [ { "type": "Canvas", "height": 100, "width": 100 } ]
        }
        """);

        Assert.Empty(manifest.Pages);
    }

    /// <summary>
    /// A canvas with no image service is skipped rather than producing a page
    /// whose picture cannot be fetched.
    /// </summary>
    [Fact]
    public async Task ACanvasWithNoImageServiceIsSkipped()
    {
        var manifest = await ReadAsync("""
        {
          "label": "Partly digitised",
          "sequences": [ { "canvases": [
            { "label": "f. 1", "images": [ { "resource": { } } ] },
            { "label": "f. 2", "images": [ { "resource": { "service": { "@id": "https://example.invalid/f2" } } } ] }
          ] } ]
        }
        """);

        var page = Assert.Single(manifest.Pages);
        Assert.Equal("f. 2", page.Label);
    }

    /// <summary>
    /// The shipped list of which manuscripts can be looked at.
    ///
    /// Every entry has to name a manuscript that is actually in the catalogue:
    /// a shelfmark that drifted - because CATMuS renamed one, or the resolver
    /// wrote a normalised spelling - is a "See the pages" button that never
    /// lights up, with nothing anywhere to say why.
    /// </summary>
    [Fact]
    public void EveryResolvedManifestNamesAManuscriptInTheCatalogue()
    {
        Assert.True(CatmusManifests.Count > 0,
            "No manuscript has a manifest - the embedded list is empty or was not rebuilt.");

        var known = CatmusCatalogue.Manuscripts.Select(m => m.Shelfmark).ToHashSet(StringComparer.Ordinal);
        var orphans = CatmusManifests.ByShelfmark.Keys.Where(s => !known.Contains(s)).ToList();

        Assert.True(orphans.Count == 0,
            "These manifests name a manuscript not in the catalogue: " + string.Join(", ", orphans));
    }

    [Fact]
    public void EveryManifestUrlIsAnHttpsManifest()
    {
        foreach (var (shelfmark, url) in CatmusManifests.ByShelfmark)
        {
            Assert.StartsWith("https://", url);
            Assert.Contains("manifest", url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(url, CatmusManifests.For(shelfmark));
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
