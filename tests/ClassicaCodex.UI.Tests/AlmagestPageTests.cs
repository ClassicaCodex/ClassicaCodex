using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Ptolemy's planetarium is a whole web page carried inside the executable
/// and opened in somebody else's browser - the same unusual arrangement as
/// the Antikythera page beside it, and breakable in the same ways the
/// compiler cannot see. It is rebuilt from its own sources in another
/// repository and copied in, so every one of these could regress without a
/// line of this project changing.
///
/// Four of these tests are the same four that guard the mechanism: that the
/// page is in the build, that it is a whole document rather than the
/// wrapper-free copy the same source also produces, that it asks the
/// network for nothing, and that it carries no font.
///
/// The last two are this page's own. A planetarium whose numbers have
/// quietly become somebody else's numbers looks exactly like one whose
/// numbers are Ptolemy's, so the page is checked for the sexagesimal
/// parameters it claims to be built on - and for the identity that ties
/// them together.
/// </summary>
public class AlmagestPageTests
{
    private const string PageResource = "ClassicaCodex.UI.Pages.Almagest.html";

    private static string PageText()
    {
        using var stream = typeof(AlmagestForm).Assembly.GetManifestResourceStream(PageResource);
        Assert.True(stream != null,
            "The Almagest page is not embedded in the build under " + PageResource);
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// It is in the build, and it is a whole document rather than the
    /// wrapper-free fragment the same source also produces for publishing
    /// elsewhere. The fragment would open in a browser as a wall of
    /// unstyled text.
    /// </summary>
    [Fact]
    public void ThePageIsAWholeDocument()
    {
        var page = PageText();

        Assert.Contains("<!doctype html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>Ptolemy's Cosmos</title>", page);

        Assert.Contains("<style>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<canvas", page, StringComparison.OrdinalIgnoreCase);

        // The built page is around 130 KB; anything under fifty would be a
        // stub or a truncated copy.
        Assert.True(page.Length > 50_000,
            "The page is only " + page.Length + " characters, which is too small to be the real one.");
    }

    /// <summary>
    /// It asks the network for nothing. A stylesheet link, a script tag or
    /// an @import pointing at a CDN would work perfectly on the machine it
    /// was built on and fail silently on a laptop with no connection.
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
    /// It carries no font. This application redistributes none, and
    /// publish-release.ps1 enforces that by looking for font name-table
    /// markers in the executable - which a web font embedded in a page as a
    /// data URI would sail straight past.
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
    /// Writing the page out produces the whole of it. The copy in the
    /// temporary folder is what the browser actually opens, so a short
    /// write would show a half-drawn diagram rather than an error.
    /// </summary>
    [Fact]
    public void ExtractingThePageWritesAllOfIt()
    {
        var expected = PageText();
        var path = AlmagestForm.ExtractPage();

        Assert.True(File.Exists(path), "Nothing was written to " + path);
        var written = File.ReadAllText(path, Encoding.UTF8);
        Assert.Equal(expected.Length, written.Length);
    }

    /// <summary>
    /// The icon ships for both themes. An icon with no Light\ counterpart
    /// is treated by this application as an old glyph and brightened by a
    /// third in dark mode, which washes the brass out.
    /// </summary>
    [Fact]
    public void TheToolbarIconShipsForBothThemes()
    {
        var baseDir = AppContext.BaseDirectory;
        var dark = Path.Combine(baseDir, "Icons", "Almagest.png");
        var light = Path.Combine(baseDir, "Icons", "Light", "Almagest.png");

        Assert.True(File.Exists(dark), "Missing the dark-mode icon at " + dark);
        Assert.True(File.Exists(light), "Missing the light-mode icon at " + light);
    }

    /// <summary>
    /// THE NUMBERS ARE STILL PTOLEMY'S.
    ///
    /// This is the test that matters, and it is the one the mechanism's
    /// suite has no equivalent of. Everything the page claims rests on the
    /// parameters being his: the epicycle radii and eccentricities in parts
    /// of a deferent radius of sixty, and the mean motions to six
    /// sexagesimal places. Those are transcribed by hand from a translation.
    ///
    /// A page rebuilt from sources where somebody had "tidied" a parameter -
    /// rounded a mean motion, replaced an eccentricity with a modern
    /// equivalent, swapped a sexagesimal string for its decimal - would
    /// still open, still animate, and still look entirely correct. Nothing
    /// else in this repository would notice. So a handful of the load-bearing
    /// ones are asserted here, in the exact notation the page prints.
    /// </summary>
    [Fact]
    public void ThePageStillCarriesPtolemysOwnParameters()
    {
        var page = PageText();

        // The Sun's mean daily motion, six sexagesimal places (Alm. III.1).
        Assert.Contains("0;59,8,17,13,12,31", page);

        // Mars: the largest epicycle relative to its deferent in the whole
        // system, and the eccentricity that goes with it (Alm. X.7-8).
        Assert.Contains("39;30", page);
        Assert.Contains("6;0", page);

        // Venus's epicycle, which is why it never strays far from the Sun.
        Assert.Contains("43;10", page);

        // The Sun's eccentricity and its fixed apogee (Alm. III.4).
        Assert.Contains("2;30", page);
        Assert.Contains("65;30", page);

        // The epoch mean longitude that all three outer planets' epoch
        // positions sum to. If this string is gone, the identity that makes
        // the whole parameter table self-checking has gone with it.
        Assert.Contains("330;45", page);
    }

    /// <summary>
    /// The page still says what it is and what is wrong with it.
    ///
    /// The honesty is the point. A planetarium of a model that is out by
    /// degrees has to say so, and say why - and the reasons are specific
    /// enough that losing them in an edit would be easy and silent.
    /// </summary>
    [Fact]
    public void ThePageStillAdmitsItsErrors()
    {
        var page = PageText();

        // The equant, which is the whole reason the model works and the
        // whole reason it was attacked.
        Assert.Contains("equant", page, StringComparison.OrdinalIgnoreCase);

        // The equinox offset: Ptolemy's observations ran late, and because
        // every planet is tied to the Sun, that one error reaches all of
        // them. The page offers to subtract it.
        Assert.Contains("1.15", page);

        // Precession at one degree a century, which is where most of the
        // present-day error actually comes from.
        Assert.Contains("precession", page, StringComparison.OrdinalIgnoreCase);
    }
}
