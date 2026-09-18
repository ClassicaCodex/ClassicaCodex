using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The Antikythera page is the one piece of shipped content that nothing
/// else in this application resembles: a whole web page, carried inside the
/// executable, opened in somebody else's browser. That makes it easy to
/// break in ways the compiler cannot see.
///
/// Three things have to stay true of it, and none of them is obvious from
/// reading the C#. It has to actually be in the build. It has to ask the
/// network for nothing, because the whole point of this application is that
/// it works with a library on a disk and no connection. And it has to carry
/// no font, because this application redistributes none and the release
/// script fails the build if font data turns up in the payload - a web page
/// with a web font in it would be exactly the sort of thing that slips past
/// a check written to look for a .NET assembly.
///
/// The page is rebuilt from its own sources in another repository and
/// copied in, so all three could regress without a single line of this
/// project changing.
/// </summary>
public class AntikytheraPageTests
{
    private const string PageResource = "ClassicaCodex.UI.Pages.Antikythera.html";

    private static string PageText()
    {
        using var stream = typeof(AntikytheraForm).Assembly.GetManifestResourceStream(PageResource);
        Assert.True(stream != null,
            "The Antikythera page is not embedded in the build under " + PageResource);
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// It is in the build, and it is a whole document rather than the
    /// wrapper-free fragment that the same source also produces for
    /// publishing elsewhere. The fragment would open in a browser as a wall
    /// of unstyled text.
    /// </summary>
    [Fact]
    public void ThePageIsAWholeDocument()
    {
        var page = PageText();

        Assert.Contains("<!doctype html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>The Antikythera Mechanism</title>", page);

        // Its own stylesheet and script, inlined rather than linked.
        Assert.Contains("<style>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<canvas", page, StringComparison.OrdinalIgnoreCase);

        // Big enough to be the real thing. The built page is around 220 KB;
        // anything under fifty would be a stub or a truncated copy.
        Assert.True(page.Length > 50_000,
            "The page is only " + page.Length + " characters, which is too small to be the real one.");
    }

    /// <summary>
    /// It asks the network for nothing.
    ///
    /// A stylesheet link, a script tag or an @import pointing at a CDN would
    /// work perfectly on the machine it was built on and fail silently on a
    /// laptop with no connection - the dial would open unstyled or dead, and
    /// nothing would say why. That is the bug nobody would ever find, so it
    /// is checked here as well as in the page's own build script.
    /// </summary>
    [Fact]
    public void ThePageAsksTheNetworkForNothing()
    {
        var page = PageText();

        var reaches = new Regex(
            @"(<link[^>]+href\s*=\s*[""']https?:)"
            + @"|(<script[^>]+src\s*=\s*[""']https?:)"
            + @"|(@import[^;]*https?:)"
            + @"|(url\(\s*[""']?https?:)",
            RegexOptions.IgnoreCase);

        var match = reaches.Match(page);
        Assert.False(match.Success,
            "The page reaches out to the network: " + (match.Success ? match.Value : string.Empty));
    }

    /// <summary>
    /// It carries no font.
    ///
    /// This application redistributes no fonts at all, and publish-release.ps1
    /// enforces that by looking for font name-table markers in the executable.
    /// A web font embedded in a page as a data URI would sail past that check
    /// - it is not a .NET assembly and its markers are not the ones the script
    /// knows about - while being exactly the thing the policy exists to stop.
    /// </summary>
    [Fact]
    public void ThePageCarriesNoFont()
    {
        var page = PageText();

        Assert.DoesNotContain("@font-face", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:font/", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("application/font-woff", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writing the page out produces the whole of it.
    ///
    /// The copy in the temporary folder is what the browser actually opens,
    /// so a short write - a full disk, an interrupted run - would show a
    /// half-drawn machine rather than an error.
    /// </summary>
    [Fact]
    public void ExtractingThePageWritesAllOfIt()
    {
        var expected = PageText().Length;

        var path = AntikytheraForm.ExtractPage();
        Assert.True(File.Exists(path), "ExtractPage returned a path that is not there: " + path);

        var written = File.ReadAllText(path, Encoding.UTF8);
        Assert.Equal(expected, written.Length);
        Assert.Contains("</html>", written, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The toolbar icon exists for both themes.
    ///
    /// AppIcons resolves icons by filename and returns null when a file is
    /// missing, which leaves a button that is simply blank rather than
    /// throwing. Worse, an icon that ships WITHOUT its Light variant is
    /// treated as an old-style glyph and brightened by thirty per cent in
    /// dark mode, which washes the bronze out. Both files or neither.
    /// </summary>
    [Fact]
    public void TheToolbarIconShipsForBothThemes()
    {
        var icons = Path.Combine(UiSourceDirectory(), "Icons");

        Assert.True(File.Exists(Path.Combine(icons, "Antikythera.png")),
            "The dark-theme icon is missing from Icons.");
        Assert.True(File.Exists(Path.Combine(icons, "Light", "Antikythera.png")),
            "The light-theme icon is missing from Icons\\Light, so dark mode would brighten the other one.");
    }

    private static string UiSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClassicaCodex.sln")))
            directory = directory.Parent;

        Assert.True(directory != null, "Could not find ClassicaCodex.sln above " + AppContext.BaseDirectory);
        return Path.Combine(directory!.FullName, "src", "ClassicaCodex.UI");
    }
}
