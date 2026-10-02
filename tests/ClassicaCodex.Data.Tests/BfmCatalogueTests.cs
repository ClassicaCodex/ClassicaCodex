using ClassicaCodex.Core.Bfm;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The embedded list of what the Base de Français Médiéval contains.
///
/// Built by tools/BfmCatalogue from Nakala and shipped, so the failure mode is
/// not a wrong value but an absent or truncated file - which would leave the
/// setup step with nothing to download and no error anywhere.
/// </summary>
public class BfmCatalogueTests
{
    /// <summary>
    /// Measured 2026-10-02 against the two deposits: 219 texts in BFM2022 and
    /// 281 fabliaux, 222.5 MB. Asserted as lower bounds because the corpus can
    /// grow and the catalogue can be rebuilt; what must never happen is the
    /// file arriving empty or half-read.
    /// </summary>
    [Fact]
    public void TheCatalogueShipsAndIsWhole()
    {
        Assert.True(BfmCatalogue.Count >= 500,
            $"Only {BfmCatalogue.Count} texts in the embedded catalogue - it was 500 when written.");

        Assert.True(BfmCatalogue.TotalBytes > 200L * 1024 * 1024);
    }

    [Fact]
    public void EveryTextCanBeFetchedAndNamed()
    {
        foreach (var text in BfmCatalogue.Texts)
        {
            Assert.False(string.IsNullOrWhiteSpace(text.Doi), text.FileName);
            Assert.False(string.IsNullOrWhiteSpace(text.Sha1), text.FileName);
            Assert.False(string.IsNullOrWhiteSpace(text.Title), text.FileName);
            Assert.EndsWith(".xml", text.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.True(text.Bytes > 0, text.FileName);
            Assert.Contains(text.Collection, new[] { "BFM2022", "Fabliaux" });
        }
    }

    /// <summary>
    /// Each text is its own deposit, so the file name has to identify it on
    /// disk. Two texts sharing one would overwrite each other during the
    /// download and leave the second reading the first.
    /// </summary>
    [Fact]
    public void NoTwoTextsShareAFileName()
    {
        var names = BfmCatalogue.Texts.Select(t => t.FileName).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(names.Count, BfmCatalogue.Texts.Select(t => t.Doi).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The download address is built from the DOI and the checksum, which is
    /// how Nakala addresses a file inside a deposit.
    /// </summary>
    [Fact]
    public void TheDownloadUrlIsBuiltFromTheDepositAndTheChecksum()
    {
        var text = BfmCatalogue.Texts[0];

        Assert.Equal($"https://api.nakala.fr/data/{text.Doi}/{text.Sha1}", text.DownloadUrl);
        Assert.StartsWith("https://doi.org/10.34847/", text.RecordUrl);
    }

    /// <summary>
    /// <b>The corpus is not under one licence and the difference is not
    /// small.</b> Most is Licence Ouverte; the fabliaux and a few others are
    /// non-commercial share-alike - and among those few are the Serments de
    /// Strasbourg and the Séquence de sainte Eulalie, the two oldest texts in
    /// French. Saying "Licence Ouverte" of the whole corpus would be wrong
    /// about 289 of its 500 texts.
    /// </summary>
    [Fact]
    public void BothLicencesArePresentAndToldApart()
    {
        var open = BfmCatalogue.Texts.Where(t => !BfmLicence.IsNonCommercial(t.Licence)).ToList();
        var nonCommercial = BfmCatalogue.Texts.Where(t => BfmLicence.IsNonCommercial(t.Licence)).ToList();

        Assert.True(open.Count > 0, "No text is open-licensed any more - the licence field has changed shape.");
        Assert.True(nonCommercial.Count > 0,
            "No text is non-commercial any more, which would mean either the corpus was relicensed or " +
            "BfmLicence.IsNonCommercial has stopped matching - and the second would tell every reader " +
            "the fabliaux are freer than they are.");

        Assert.All(open, t => Assert.DoesNotContain("NC", BfmLicence.Describe(t.Licence)));
        Assert.All(nonCommercial, t => Assert.Contains("non-commercial", BfmLicence.Describe(t.Licence)));
    }

    [Fact]
    public void EveryLicenceInTheCatalogueIsOneWeHaveWordsFor()
    {
        var unknown = BfmCatalogue.Texts
            .Select(t => t.Licence)
            .Distinct(StringComparer.Ordinal)
            .Where(l => BfmLicence.Describe(l) == l)
            .ToList();

        Assert.True(unknown.Count == 0,
            "These licences are shown to the reader as a bare identifier: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// The corpus spans the ninth century to the fifteenth, and the dates are
    /// what let a library tree put it in order.
    /// </summary>
    [Fact]
    public void TheDatesSpanTheCorpus()
    {
        var years = BfmCatalogue.Texts.Select(t => t.Year).Where(y => y.HasValue).Select(y => y!.Value).ToList();

        Assert.True(years.Count > BfmCatalogue.Count / 2, "Most texts should carry a date.");
        Assert.True(years.Min() < 1000, $"The earliest text is dated {years.Min()}; the Serments are 842.");
        Assert.True(years.Max() >= 1400);
    }
}
