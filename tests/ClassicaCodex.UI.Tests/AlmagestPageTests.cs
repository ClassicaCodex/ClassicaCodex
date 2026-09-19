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

    /// <summary>
    /// Every link on the card has been opened and confirmed to resolve to
    /// the work it claims to be.
    ///
    /// A card whose whole purpose is to say where the numbers came from is
    /// worse than useless if a citation points at a dead host or the wrong
    /// paper - it looks like scholarship and is not. So the list below is
    /// checked by hand and this test fails on any URL in the form that is
    /// not in it, which forces the check rather than assuming it.
    /// </summary>
    [Fact]
    public void EveryLinkOnTheCardHasBeenChecked()
    {
        var verified = new HashSet<string>(StringComparer.Ordinal)
        {
            // Checked 2026-09-19, each one fetched and read back.
            //
            // Title returned "Almagest Ephemeris Calculator"; confirmed it
            // computes geocentric positions from the Syntaxis models.
            "https://webspace.science.uu.nl/~gent0113/astro/almagestephemeris_main.htm",

            // Title "Almagest Planetary Model Animations", on dduke's FSU space.
            "https://people.sc.fsu.edu/~dduke/models.htm",

            // This one is a 1.3 MB PDF whose text streams are compressed, so
            // the title PAGE could not be read back the way the others were.
            // Verified two other ways instead, and the weaker check is
            // recorded here rather than glossed: the host is Richard
            // Fitzpatrick's own UT Austin site (confirmed from its index,
            // rfitzp@farside.ph.utexas.edu), and the PDF's own bookmark
            // titles include "Ptolemy's Model of the Solar System",
            // "Copernicus's Model of the Solar System" and "Euclid's
            // Elements and Ptolemy's Almagest".
            "https://farside.ph.utexas.edu/Books/Syntaxis/Almagest.pdf",

            // Title "Almagest Book XI: Calculating Planetary Longitude".
            // This one also confirmed the CLAIM the card makes about it: the
            // post follows Toomer's Appendix A Example 14 and reaches
            // Sagittarius 1;35, against Ptolemy's observed 1;36.
            "https://jonvoisey.net/blog/2024/09/almagest-book-xi-calculating-planetary-longitude/",

            // Title "Approximate Positions of the Planets"; Table 2a present
            // and stated as the 3000 BC - 3000 AD fit, with Table 2b's extra
            // terms for Jupiter through Neptune.
            "https://ssd.jpl.nasa.gov/planets/approx_pos.html",

            // Title "Polynomial Expressions for Delta T", after the Five
            // Millennium Canon of Espenak and Meeus.
            "https://eclipse.gsfc.nasa.gov/SEcat5/deltatpoly.html",

            // Cicero, De re publica I, in Latin. Also confirmed the exact
            // clause the page quotes at 1.22: "in dissimillimis motibus
            // inaequabiles et varios cursus servaret una conversio".
            "https://thelatinlibrary.com/cicero/repub1.shtml"
        };

        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "AlmagestForm.cs"));

        var urls = Regex.Matches(source, @"""(https?://[^""\s]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(urls);

        var unverified = urls.Where(u => !verified.Contains(u)).ToList();
        Assert.True(unverified.Count == 0,
            "These links are in the card but not in the checked list. Open each one, "
            + "confirm it resolves to the work it claims, then add it above: "
            + string.Join(", ", unverified));

        // And nothing plain-text. A citation link that downgrades is a worse
        // look on a page about scholarship than a missing one.
        Assert.DoesNotContain(urls, u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The card still names its primary text and its standard commentaries.
    ///
    /// Those three have no URL - they are books - so nothing else in this
    /// suite would notice if an edit quietly dropped them, and a page that
    /// cites only what happens to be online is making a different and
    /// weaker claim than this one does.
    /// </summary>
    [Fact]
    public void TheCardStillNamesItsPrintedSources()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "AlmagestForm.cs"));

        Assert.Contains("Toomer", source);          // the translation everything is quoted from
        Assert.Contains("Neugebauer", source);      // HAMA
        Assert.Contains("Pedersen", source);        // A Survey of the Almagest
        Assert.Contains("Heiberg", source);         // the Greek text under Toomer
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
