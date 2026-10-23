using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The Quran's two Arabic texts are both originals in the same language, so
/// the edition list can only tell them apart by their spelling.
/// </summary>
public class QuranLabelTests
{
    private static Edition Arabic(string orthography) => new()
    {
        CtsUrn = $"urn:quran:2:tanzil-{orthography}",
        Kind = EditionKind.Original,
        Language = "ara",
        Orthography = orthography
    };

    [Fact]
    public void TheTwoArabicTextsAreNamedByTheirSpelling()
    {
        Assert.Equal("Arabic (Uthmani script)", EditionLabels.Descriptor(Arabic("uthmani")));
        Assert.Equal("Arabic (modern spelling)", EditionLabels.Descriptor(Arabic("simple")));
    }
}
