using ClassicaCodex.Core.Catmus;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The embedded list of what CATMuS-Medieval contains.
///
/// It is built by tools/CatmusCatalogue from the dataset itself and then
/// shipped, so the failure mode is not a wrong value - it is an absent or
/// truncated file, which would leave the Palaeography window showing an empty
/// catalogue and no error anywhere.
/// </summary>
public class CatmusCatalogueTests
{
    /// <summary>
    /// Measured 2026-09-26 against the dataset: 346 files, 313 manuscripts,
    /// 194,808 lines, 24.71 GiB. The numbers are asserted as lower bounds
    /// because CATMuS can gain manuscripts and the catalogue can be rebuilt -
    /// what must never happen is the file arriving empty or half-read.
    /// </summary>
    [Fact]
    public void TheCatalogueShipsAndIsWholeEnoughToBrowse()
    {
        Assert.True(CatmusCatalogue.Shards.Count >= 346,
            $"Only {CatmusCatalogue.Shards.Count} shards in the embedded catalogue - it was 346 when " +
            "written. An empty or short one leaves the Palaeography window blank with nothing to say why.");

        Assert.True(CatmusCatalogue.Manuscripts.Count >= 313);
        Assert.True(CatmusCatalogue.TotalLines >= 194_808);
        Assert.True(CatmusCatalogue.TotalBytes > 20L * 1024 * 1024 * 1024);
    }

    /// <summary>
    /// Every row has the fields the window puts on screen.
    ///
    /// The shard NAME carries these too, but flattened and abbreviated -
    /// "Liege__Archives_de_l_Etat__T51_12" and "Got_Doc_Scr" - which is why
    /// the catalogue is harvested from the files rather than parsed from
    /// their names. A row that lost them would silently show blanks.
    /// </summary>
    [Fact]
    public void EveryShardCarriesTheFieldsTheWindowShows()
    {
        foreach (var shard in CatmusCatalogue.Shards)
        {
            Assert.False(string.IsNullOrWhiteSpace(shard.Shelfmark), shard.FileName);
            Assert.False(string.IsNullOrWhiteSpace(shard.Language), shard.FileName);
            Assert.False(string.IsNullOrWhiteSpace(shard.ScriptType), shard.FileName);
            Assert.False(string.IsNullOrWhiteSpace(shard.Project), shard.FileName);
            Assert.True(shard.Century is >= 5 and <= 17, $"{shard.FileName} is dated {shard.Century}");
            Assert.True(shard.Lines > 0, shard.FileName);
            Assert.True(shard.Bytes > 0, shard.FileName);
            Assert.Contains(shard.Split, new[] { "train", "dev", "test" });
        }
    }

    /// <summary>
    /// The shelfmarks came out of the files, not the file names, so they
    /// still have their punctuation and their accents. If this fails, the
    /// harvester has been replaced by something parsing the names.
    /// </summary>
    [Fact]
    public void ShelfmarksAreTheRealOnesRatherThanFileNames()
    {
        var mangled = CatmusCatalogue.Shards
            .Where(s => s.Shelfmark.Contains("__", StringComparison.Ordinal))
            .Select(s => s.Shelfmark)
            .ToList();

        Assert.True(mangled.Count == 0, "Shelfmarks that look like file names: " + string.Join(", ", mangled));

        Assert.Contains(CatmusCatalogue.Manuscripts, m => m.Shelfmark.Contains(", ", StringComparison.Ordinal));
        Assert.Contains(CatmusCatalogue.Manuscripts, m => m.Shelfmark.Any(c => c > 127));
    }

    /// <summary>
    /// A manuscript is its shards, and a multi-part one keeps them in order.
    /// Ghent UL 1374 is the four-part case and the largest thing in the
    /// dataset; the window quotes its size before downloading, so the parts
    /// have to add up rather than report only the first.
    /// </summary>
    [Fact]
    public void AMultiPartManuscriptIsOneEntryWhoseSizeIsAllOfIt()
    {
        var biggest = CatmusCatalogue.Manuscripts.OrderByDescending(m => m.Bytes).First();

        Assert.True(biggest.Shards.Count > 1, "The largest manuscript should be a multi-part one.");
        Assert.Equal(biggest.Shards.Sum(s => s.Bytes), biggest.Bytes);
        Assert.Equal(biggest.Shards.Sum(s => s.Lines), biggest.Lines);

        var names = biggest.Shards.Select(s => s.FileName).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
    }

    /// <summary>
    /// The filter axes are built from the catalogue, so they can only ever
    /// offer values something actually has.
    /// </summary>
    [Fact]
    public void TheFilterAxesAreDrawnFromWhatIsThere()
    {
        Assert.Contains("Latin", CatmusCatalogue.Languages);
        Assert.Contains("Textualis", CatmusCatalogue.ScriptTypes);
        Assert.All(CatmusCatalogue.Languages, l => Assert.Contains(CatmusCatalogue.Shards, s => s.Language == l));
        Assert.All(CatmusCatalogue.ScriptTypes, t => Assert.Contains(CatmusCatalogue.Shards, s => s.ScriptType == t));
        Assert.All(CatmusCatalogue.Centuries, c => Assert.Contains(CatmusCatalogue.Shards, s => s.Century == c));

        // Commonest first, so the filter opens on the useful end.
        Assert.Equal("Latin", CatmusCatalogue.Languages[0]);
    }

    /// <summary>
    /// <b>The licence split is load-bearing, not decoration.</b> CATMuS is
    /// labelled CC-BY-4.0 and part of it is not: the Castilian HTR material
    /// descends from a CC-BY-NC-SA-4.0 source. A reader told the wrong one is
    /// being told they may do something with somebody else's work that they
    /// may not, so this pins both the rule and that it still matches
    /// manuscripts in the catalogue.
    /// </summary>
    [Fact]
    public void TheNonCommercialManuscriptsAreNamedAsSuch()
    {
        var restricted = CatmusCatalogue.Manuscripts
            .Where(m => CatmusLicence.IsNonCommercial(m.Project))
            .ToList();

        Assert.True(restricted.Count > 0,
            "No manuscript matches the non-commercial project name any more - either the dataset renamed " +
            "it, in which case CatmusLicence.NonCommercialProject is now silently telling every reader " +
            "the material is CC-BY, or the catalogue lost its Project column.");

        Assert.All(restricted, m => Assert.Contains("NC-SA", CatmusLicence.Describe(m.Project)));

        var free = CatmusCatalogue.Manuscripts.First(m => !CatmusLicence.IsNonCommercial(m.Project));
        Assert.DoesNotContain("NC-SA", CatmusLicence.Describe(free.Project));
    }

    /// <summary>
    /// Download URLs point at the main branch.
    ///
    /// HuggingFace also serves these files from refs/convert/parquet, where
    /// every one of them is renamed to 0000.parquet - which destroys the
    /// language, century, script and shelfmark the names encode and makes the
    /// 346 files indistinguishable.
    /// </summary>
    [Fact]
    public void DownloadUrlsUseTheMainBranchLayout()
    {
        foreach (var shard in CatmusCatalogue.Shards.Take(20))
        {
            Assert.StartsWith("https://huggingface.co/datasets/CATMuS/medieval/resolve/main/", shard.DownloadUrl);
            Assert.EndsWith(shard.FileName, shard.DownloadUrl);
            Assert.DoesNotContain("refs%2Fconvert", shard.DownloadUrl);
        }
    }
}
