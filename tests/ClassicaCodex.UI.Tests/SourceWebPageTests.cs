using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Whether an edition's recorded source is a page a reader can be offered.
///
/// This is the security-relevant half of the "View on ..." menu item, which
/// is why it is pinned rather than reasoned about. SourcePath is INGESTED
/// DATA - it comes out of somebody else's corpus, through the database, and
/// on the other side of this method it is handed to the shell. Every
/// collection but one stores a path on disk there.
///
/// So the rule is a whitelist, not a blacklist: http and https are a link,
/// and everything else is not - not because the alternatives are known to be
/// dangerous, but because a source field should never have been a way to
/// launch anything, and only two schemes need it to be.
/// </summary>
public class SourceWebPageTests
{
    /// <summary>
    /// The cuneiform case, and the reason the item exists: Oracc's editions
    /// record the text's own page, where the published English translation
    /// that is not in the open data can be read.
    /// </summary>
    [Fact]
    public void AnOraccSourceIsAPage()
    {
        var page = MainForm.WebPageFromSourcePath("http://oracc.org/rinap/rinap1/Q003414");

        Assert.NotNull(page);
        Assert.Equal("oracc.org", page!.Host);
    }

    [Fact]
    public void HttpsIsAPageToo()
    {
        Assert.NotNull(MainForm.WebPageFromSourcePath("https://oracc.museum.upenn.edu/rinap/rinap1/Q003414"));
    }

    /// <summary>
    /// What every other collection stores. A Perseus edition records the file
    /// it was parsed from, and the menu must not offer to open it - the item
    /// simply is not there.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\reader\ClassicaCodex\greek-texts\data\tlg0012\tlg001\tlg0012.tlg001.perseus-grc2.xml")]
    [InlineData("/home/reader/corpus/csel/file.xml")]
    [InlineData(@"..\relative\path.xml")]
    [InlineData("data/first1k/x.xml")]
    public void AFileOnDiskIsNotAPage(string sourcePath)
    {
        Assert.Null(MainForm.WebPageFromSourcePath(sourcePath));
    }

    /// <summary>
    /// The whitelist doing its job. None of these is necessarily an attack -
    /// they are simply not web pages, and a source field is not a launcher.
    /// </summary>
    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData(@"\\server\share\payload.exe")]
    [InlineData("ms-settings:")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.org/x")]
    [InlineData("mailto:someone@example.org")]
    public void NothingElseIsAPage(string sourcePath)
    {
        Assert.Null(MainForm.WebPageFromSourcePath(sourcePath));
    }

    /// <summary>
    /// An edition with nothing recorded, which is normal for a translation
    /// written in the workbench rather than imported.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingRecordedIsNotAPage(string? sourcePath)
    {
        Assert.Null(MainForm.WebPageFromSourcePath(sourcePath));
    }
}
