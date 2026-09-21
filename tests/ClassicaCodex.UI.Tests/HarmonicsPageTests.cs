using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Greek harmonic science is a whole web page carried inside the executable
/// and opened in somebody else's browser - the same arrangement as the
/// mechanism and the planetarium beside it, rebuilt from its own sources in
/// another repository and copied in, so every one of these could regress
/// without a line of this project changing.
///
/// Four of these are the four that guard both siblings: that the page is in
/// the build, that it is a whole document rather than the wrapper-free copy
/// the same source also produces, that it asks the network for nothing, and
/// that it carries no font.
///
/// THE REST ARE THIS PAGE'S OWN, and they exist because this page can make a
/// noise in a reading application. One holds the claim that it ships no
/// recording. One holds the gate that keeps it silent until it is asked.
/// One holds the ratios, because a page whose numbers have quietly become
/// somebody else's numbers sounds exactly like one whose numbers are
/// Ptolemy's - that is the entire difficulty with this page, and the reason
/// its own repository checks the arithmetic 106 ways before it is built.
/// </summary>
public class HarmonicsPageTests
{
    private const string PageResource = "ClassicaCodex.UI.Pages.Harmonics.html";

    private static string PageText()
    {
        using var stream = typeof(HarmonicsForm).Assembly.GetManifestResourceStream(PageResource);
        Assert.True(stream != null,
            "The Harmonics page is not embedded in the build under " + PageResource);
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// It is in the build, and it is a whole document rather than the
    /// wrapper-free fragment the same source also produces for publishing
    /// elsewhere. The fragment would open in a browser as unstyled text.
    /// </summary>
    [Fact]
    public void ThePageIsAWholeDocument()
    {
        var page = PageText();

        Assert.Contains("<!doctype html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>Ptolemy's Harmony</title>", page);

        Assert.Contains("<style>", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<canvas", page, StringComparison.OrdinalIgnoreCase);

        // The built page is around 62 KB; anything under thirty would be a
        // stub or a truncated copy.
        Assert.True(page.Length > 30_000,
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
    /// IT CARRIES NO RECORDING, and that is a claim about the scholarship
    /// rather than about the file size.
    ///
    /// Every note on the page is synthesised from its exact whole-number
    /// ratio at the moment it sounds. A recording could not be: it would fix
    /// the tuning at whatever the person who made it believed, and the one
    /// thing this page asserts is that the intervals are the ones its
    /// authors wrote. An embedded clip would also be the easiest possible
    /// way for the page to start making the ratios approximate without
    /// anything looking wrong.
    /// </summary>
    [Fact]
    public void ThePageCarriesNoRecording()
    {
        var page = PageText();

        Assert.DoesNotContain("data:audio", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mp3", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".ogg", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".wav", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<audio", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE PAGE IS SILENT UNTIL IT IS ASKED.
    ///
    /// This one exists because of where the page opens. Someone pressing a
    /// toolbar button in a library application has not asked for a noise and
    /// may be somewhere they must not make one, so the page opens silent and
    /// stays silent behind a gate of its own - a second, separate press,
    /// after the card that already warned them.
    ///
    /// A text search cannot prove a page never plays unbidden; it can only
    /// check that the gate is still there and that nothing obvious bypasses
    /// it. That is the honest limit of this test, and it is worth having
    /// anyway: the way this would regress is somebody removing the gate to
    /// "fix" the first click, not somebody hiding an autoplay.
    /// </summary>
    [Fact]
    public void ThePageIsSilentUntilItIsAsked()
    {
        var page = PageText();

        Assert.Contains("id=\"gate\"", page);
        Assert.Contains("id=\"gateBtn\"", page);
        Assert.Contains("Turn the sound on", page);

        // Every sounding path is guarded by this, so it cannot fire before
        // the gate has run.
        Assert.Contains("audioReady", page);

        // And nothing that plays on its own.
        Assert.DoesNotContain("autoplay", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writing the page out produces the whole of it. The copy in the
    /// temporary folder is what the browser actually opens, so a short write
    /// would show half a page rather than an error.
    /// </summary>
    [Fact]
    public void ExtractingThePageWritesAllOfIt()
    {
        var expected = PageText();
        var path = HarmonicsForm.ExtractPage();

        Assert.True(File.Exists(path), "Nothing was written to " + path);
        var written = File.ReadAllText(path, Encoding.UTF8);
        Assert.Equal(expected.Length, written.Length);
    }

    /// <summary>
    /// The icon ships for both themes. An icon with no Light\ counterpart is
    /// treated by this application as an old glyph and brightened by a third
    /// in dark mode.
    /// </summary>
    [Fact]
    public void TheToolbarIconShipsForBothThemes()
    {
        var baseDir = AppContext.BaseDirectory;
        var dark = Path.Combine(baseDir, "Icons", "Harmonics.png");
        var light = Path.Combine(baseDir, "Icons", "Light", "Harmonics.png");

        Assert.True(File.Exists(dark), "Missing the dark-mode icon at " + dark);
        Assert.True(File.Exists(light), "Missing the light-mode icon at " + light);
    }

    /// <summary>
    /// And the toolbar can actually find it under the name the button asks
    /// for.
    ///
    /// The two files existing is not the same fact as the lookup resolving:
    /// icons are addressed by a bare filename string in MainForm's button
    /// table and again in the form's window icon, and a typo in either
    /// produces no error at all - just a blank button, which is exactly the
    /// kind of thing that ships. This calls the real resolver.
    ///
    /// It also pins the consequence of having a Light\ variant: an icon
    /// without one is treated as an older glyph and lifted 30% toward white
    /// in dark mode, which would wash the slip off the pot.
    /// </summary>
    [Fact]
    public void TheToolbarCanResolveTheIconByName()
    {
        using var image = AppIcons.Get("Harmonics", 40);

        Assert.True(image != null,
            "AppIcons could not resolve \"Harmonics\" - the toolbar button and the window "
            + "icon both address it by that bare string, and a miss is silent.");
        Assert.Equal(40, image!.Width);
        Assert.Equal(40, image.Height);

        // The name MainForm asks for, spelled the same way in both places.
        var mainForm = File.ReadAllText(Path.Combine(UiSourceDirectory(), "MainForm.cs"));
        Assert.Contains("\"Harmonics\")", mainForm);

        var card = File.ReadAllText(Path.Combine(UiSourceDirectory(), "HarmonicsForm.cs"));
        Assert.Contains("ApplyWindowIcon(this, \"Harmonics\")", card);
    }

    /// <summary>
    /// THE RATIOS ARE STILL THE ONES THEY WROTE.
    ///
    /// This is the test that matters, and it is the analogue of the
    /// planetarium's sexagesimal check. A page rebuilt from sources where
    /// somebody had "tidied" a ratio - simplified 46:45 to a quarter-tone,
    /// swapped Didymus's ordering for Ptolemy's, replaced the Pythagorean
    /// comma with a decimal - would still open, still play, and still sound
    /// like music, because almost any ratio near the right one does. Nobody
    /// can hear 46:45 against 45:44. Nothing else here would notice.
    ///
    /// So the load-bearing ones are asserted in the exact form the page's
    /// source constructs them in.
    /// </summary>
    [Fact]
    public void ThePageStillCarriesTheRatiosItsAuthorsWrote()
    {
        var page = PageText();

        // Ptolemy's enharmonic: the pair of quarter-tones that is the whole
        // reason the page is worth hearing rather than reading.
        Assert.Contains("F(46, 45)", page);
        Assert.Contains("F(24, 23)", page);

        // His tense chromatic, whose pyknon beats the top interval by four
        // cents - the closest call in the catalogue.
        Assert.Contains("F(22, 21)", page);
        Assert.Contains("F(12, 11)", page);

        // The commas, which are the page's small print and its arithmetic
        // backbone. 3^12 over 2^19, written out.
        Assert.Contains("new Frac(531441, 524288)", page);
        Assert.Contains("new Frac(81, 80)", page);
        Assert.Contains("new Frac(2187, 2048)", page);

        // The Pythagorean limma, which is the one ratio in Ptolemy's own
        // eight genera that is NOT superparticular - and the page makes a
        // point of that, so losing it would cost an argument as well as a
        // number.
        Assert.Contains("F(256, 243)", page);
    }

    /// <summary>
    /// The page still admits the one thing it cannot do.
    ///
    /// The Pythagorean enharmonic used the ditone over a pyknon of 256:243,
    /// but how that pyknon was divided is not recorded anywhere. It is in
    /// the table undivided and it cannot be played. Inventing a plausible
    /// split would make it the only false line on the page, and it would be
    /// completely invisible - which is exactly why it is worth a test.
    /// </summary>
    [Fact]
    public void ThePageStillAdmitsWhatIsNotKnown()
    {
        var page = PageText();

        Assert.Contains("incomplete: true", page);
        Assert.Contains("not known", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("undivided", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE CARD WARNS ABOUT THE SOUND BEFORE IT DESCRIBES THE PAGE.
    ///
    /// Order is the whole point of the warning, so order is what is
    /// asserted. A caution that has drifted below the description is a
    /// caution the reader meets after they have decided to press the button.
    /// </summary>
    [Fact]
    public void TheCardWarnsAboutTheSoundFirst()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "HarmonicsForm.cs"));

        var caution = source.IndexOf("This one makes sound.", StringComparison.Ordinal);
        var description = source.IndexOf("\"What it is\"", StringComparison.Ordinal);

        Assert.True(caution > 0, "The card no longer says that the page makes a sound.");
        Assert.True(description > 0, "The card no longer has its description heading.");
        Assert.True(caution < description,
            "The sound warning has moved below the description. It has to be the first thing "
            + "on the card: a reader who meets it after deciding to press the button has not "
            + "been warned.");

        // The reader is told they can use the page without sound at all,
        // which is the part that makes the warning useful rather than just
        // discouraging.
        Assert.Contains("without a sound", source);

        // And the button itself says what it will do, for the reader who
        // read nothing else.
        Assert.Contains("Open it (it can make sound)", source);
    }

    /// <summary>
    /// Every link on the card has been opened and confirmed to resolve to
    /// the work it claims to be. A card whose purpose is to say where the
    /// numbers came from is worse than useless if a citation points at a
    /// dead host - it looks like scholarship and is not.
    /// </summary>
    [Fact]
    public void EveryLinkOnTheCardHasBeenChecked()
    {
        var verified = new HashSet<string>(StringComparer.Ordinal)
        {
            // Checked 2026-09-20, fetched and read back. Larry Polansky's
            // page at Dartmouth hosting scans of the original Frog Peak
            // edition of Chalmers; the chapter list came back complete,
            // chapter 2 "Pythagoras, Ptolemy, and the arithmetic tradition"
            // and chapter 9 "The Catalog of tetrachords", which are the two
            // the card names. The page states the scans are unedited.
            "https://eamusic.dartmouth.edu/~larry/published_articles/divisions_of_the_tetrachord/"
        };

        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "HarmonicsForm.cs"));

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

        Assert.DoesNotContain(urls, u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The card still names its printed sources, and still says which of
    /// them the numbers did not actually travel through.
    ///
    /// That second half is the unusual one and the one most likely to be
    /// lost in a tidy-up, because it reads like a disclaimer. It is not.
    /// The ratios came from two modern reference tables that agree, checked
    /// against the arithmetic; Solomon, Barker and Chalmers are named as
    /// where to settle a disputed number. A card that dropped the
    /// distinction would be claiming a chain of authority the page does not
    /// have.
    /// </summary>
    [Fact]
    public void TheCardStillNamesItsPrintedSourcesAndItsLimits()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "HarmonicsForm.cs"));

        Assert.Contains("Solomon", source);     // the Brill translation
        Assert.Contains("Barker", source);      // Greek Musical Writings II
        Assert.Contains("Chalmers", source);    // the standard catalogue

        Assert.Contains("What was actually read, and what was not", source);
        Assert.Contains("could not be opened", source);
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
