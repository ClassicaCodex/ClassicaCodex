using System.Text.Json;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Rabbinic;
using ClassicaCodex.Ingestion.Rabbinic;
using ClassicaCodex.Ingestion.Sefaria;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The embedded list of which rabbinic versions the setup step installs, and
/// how it joins the works listed by hand.
///
/// Built by tools/RabbinicCatalogue and shipped, so the failures to guard
/// against are an empty or half-read file, a version that should never have
/// been taken, and a work that lands on a URN something else already holds.
/// </summary>
public class RabbinicCatalogueTests
{
    private static readonly HashSet<string> OpenLicences = new(StringComparer.OrdinalIgnoreCase)
    {
        "Public Domain", "PD", "CC0", "CC-BY", "CC-BY-SA"
    };

    private static IEnumerable<string> Slugs(string corpus) =>
        RabbinicCatalogue.Texts.Where(t => t.Corpus == corpus).Select(t => t.Slug).Distinct();

    /// <summary>
    /// Measured 2026-11-05: 252 works in 600 versions, 117 MB. Lower bounds,
    /// because the list can be rebuilt; what must not happen is the file
    /// arriving empty or cut short.
    /// </summary>
    [Fact]
    public void TheCatalogueShipsAndIsWhole()
    {
        Assert.True(RabbinicCatalogue.Texts.Count >= 590, $"Only {RabbinicCatalogue.Texts.Count} versions.");
        Assert.True(RabbinicCatalogue.TotalBytes > 100_000_000);
    }

    [Theory]
    [InlineData("mishnah", 63)]
    [InlineData("tosefta", 61)]
    [InlineData("yerushalmi", 39)]
    [InlineData("bavli", 32)]
    [InlineData("minor", 15)]
    public void EachCorpusIsComplete(string corpus, int works) => Assert.Equal(works, Slugs(corpus).Count());

    /// <summary>The licence is the reason each version is here, so nothing else may get in.</summary>
    [Fact]
    public void NothingNonCommercialOrUnknownIsListed() =>
        Assert.All(RabbinicCatalogue.Texts, t => Assert.Contains(t.Licence, OpenLicences));

    [Fact]
    public void EveryWorkHasAnOriginal() =>
        Assert.All(
            RabbinicCatalogue.Texts.GroupBy(t => t.Slug),
            work => Assert.Contains(work, t => !t.IsTranslation));

    /// <summary>
    /// Two originals in one language read identically in the edition dropdown
    /// unless something tells them apart.
    /// </summary>
    [Fact]
    public void TwoOriginalsInOneLanguageCanBeToldApart()
    {
        var pairs = RabbinicCatalogue.Texts
            .Where(t => !t.IsTranslation)
            .GroupBy(t => (t.Slug, t.Language))
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.NotEmpty(pairs);
        Assert.All(pairs, pair => Assert.Equal(pair.Count(), pair.Select(t => t.Orthography).Distinct().Count()));
    }

    /// <summary>
    /// The whole Babylonian Talmud, once: the five demon tractates by hand and
    /// the rest from the catalogue, under one author.
    /// </summary>
    [Fact]
    public void TheBabylonianTalmudIsThirtySevenTractatesWithNoneTwice()
    {
        var tractates = RabbinicIngestService.Works.Where(w => w.IsTalmud).ToList();

        Assert.Equal(37, tractates.Count);
        Assert.Equal(37, tractates.Select(w => w.Title).Distinct().Count());
    }

    /// <summary>
    /// URNs and download folders are both keys, and a download folder on
    /// Windows is not case-sensitive. The Otzar tracts share one file on
    /// purpose; nothing else may.
    /// </summary>
    [Fact]
    public void NoTwoWorksShareAUrnOrAFolder()
    {
        var works = RabbinicIngestService.Works;

        Assert.Equal(works.Count, works.Select(w => w.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var whole = works.Where(w => w.Node == null).ToList();
        Assert.Equal(whole.Count, whole.Select(w => w.SefariaTitle).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The AI translations are filed line for line against these. Moving one
    /// would orphan them with no error anywhere.
    /// </summary>
    [Theory]
    [InlineData("berakhot", "vilna")]
    [InlineData("shabbat", "goldschmidt")]
    [InlineData("chagigah", "community")]
    [InlineData("pirkei-de-rabbi-eliezer", "friedlander")]
    [InlineData("hekhalot", "eisenstein")]
    public void TheWorksListedByHandKeepTheirUrns(string slug, string suffix)
    {
        var work = Assert.Single(RabbinicIngestService.Works, w => w.Slug == slug);
        Assert.Contains(work.Versions, v => v.UrnSuffix == suffix);
    }

    [Fact]
    public void AnExportPathIsAlreadyAUrlPath() =>
        Assert.All(RabbinicCatalogue.Texts, t =>
        {
            Assert.EndsWith(".json", t.ExportPath);
            Assert.DoesNotContain(" ", t.ExportPath);
            Assert.False(t.ExportPath.StartsWith('/'));
        });

    /// <summary>
    /// A library that finished the step at 33 downloads must not read as
    /// finished now, and a real install that loses a few versions still must.
    /// </summary>
    [Fact]
    public void AnOldInstallIsNotComplete()
    {
        Assert.True(RabbinicIngestService.EditionsWhenComplete > 500);
        Assert.True(RabbinicIngestService.EditionsWhenComplete < RabbinicIngestService.Works.Sum(w => w.Versions.Count));
    }

    /// <summary>
    /// Be'eri's numbers sit inside the Kaufmann text as small letters. They go,
    /// and only there: elsewhere a small letter may be the scribe's.
    /// </summary>
    [Fact]
    public void KaufmannNumbersComeOutOfTheTextAndOnlyThere()
    {
        const string json = "[[\"<small>א</small> מֵאֵמָּתַי קוֹרִין <small>ב</small> אֶת שְׁמַע\"]]";
        using var document = JsonDocument.Parse(json);

        RabbinicVersion Version(string? orthography) =>
            new(string.Empty, "v", "PD", EditionKind.Original, "heb", null, "s") { Orthography = orthography };

        var kaufmann = SefariaExportLoader.Flatten(
            document.RootElement, RabbinicIngestService.PrepareFor(Version("kaufmann")));
        Assert.Equal("מֵאֵמָּתַי קוֹרִין אֶת שְׁמַע", Assert.Single(kaufmann).Text);

        var printed = SefariaExportLoader.Flatten(
            document.RootElement, RabbinicIngestService.PrepareFor(Version(null)));
        Assert.StartsWith("א ", Assert.Single(printed).Text);
    }
}
