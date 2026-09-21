using System.Text.RegularExpressions;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// A toolbar button is wired up in two separate places in MainForm, and
/// missing either one fails silently.
///
/// THIS EXISTS BECAUSE IT HAPPENED. Ptolemy's Harmony was added to the
/// toolbarButtons array - which gave it a size, a position, a tooltip, an
/// accessible name, its icon and its theme re-registration - and was never
/// added to Controls. Every one of those properties was set correctly on a
/// button that was not on the form. It compiled, the whole suite passed, the
/// icon resolved by name when asked directly, and the button simply was not
/// there. Nothing pointed at the cause; the only symptom was an absence.
///
/// A test that constructs MainForm would catch it directly, but opening
/// product windows against the real library is not something this suite
/// does. So this reads the source instead, which is the same approach
/// ReaderAreaLayoutTests takes to the no-literals rule in
/// RelayoutReaderArea, and it generalises: it will catch the NEXT button
/// too, without anybody remembering to add a test for it.
/// </summary>
public class ToolbarWiringTests
{
    /// <summary>
    /// Every button in the toolbarButtons table is also added to the form.
    /// </summary>
    [Fact]
    public void EveryToolbarButtonIsAddedToTheForm()
    {
        var source = MainFormSource();
        var names = ToolbarButtonNames(source);

        // If the table stops being findable this test would quietly pass on
        // an empty list, which is the failure mode it exists to prevent.
        Assert.True(names.Count >= 15,
            "Only found " + names.Count + " buttons in the toolbarButtons table - the parse "
            + "below has probably stopped matching the table's shape.");

        var missing = names
            .Where(n => !source.Contains("Controls.Add(" + n + ")", StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "These buttons are in the toolbarButtons table but never added to Controls, so "
            + "they are positioned, tooltipped and given an icon and then never appear on "
            + "screen: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Icons named in the table that no longer ship at all, which is the
    /// other invisible-button failure: a name with no file behind it gives
    /// a button with no picture, and an IconButton with no picture on a dark
    /// toolbar is not a broken-looking button, it is no button.
    /// </summary>
    [Fact]
    public void EveryToolbarIconShips()
    {
        var icons = ToolbarIconNames(MainFormSource());

        Assert.True(icons.Count >= 15,
            "Only found " + icons.Count + " icons in the toolbarButtons table.");

        var missing = icons
            .Where(i => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Icons", i + ".png")))
            .ToList();

        Assert.True(missing.Count == 0,
            "These toolbar icons are named in MainForm but do not ship: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// The illustrated icons ship both variants.
    ///
    /// Not all of them do, and that is deliberate rather than an oversight:
    /// AppIcons treats an icon WITHOUT a Light\ counterpart as an older
    /// glyph drawn in dark ink for a pale surface, and lifts it 30% toward
    /// white in dark mode so it does not disappear. An icon that has its own
    /// dark-mode artwork must not be lifted, or the artwork washes out.
    ///
    /// So the rule is not "everything ships two files", it is "an
    /// illustrated icon ships two files". The list below is the remaining
    /// glyph, and it is written out rather than computed so that adding a
    /// new illustrated icon without its Light\ variant fails here instead of
    /// quietly going pale.
    /// </summary>
    [Fact]
    public void TheIllustratedToolbarIconsShipBothVariants()
    {
        // Measured 2026-09-20: sixteen of the seventeen toolbar icons ship a
        // Light\ variant. Bookmarks is the last one that does not, and so is
        // the last still going through the brightening path.
        var knownGlyphs = new HashSet<string>(StringComparer.Ordinal) { "Bookmarks" };

        var icons = ToolbarIconNames(MainFormSource());
        var baseDir = AppContext.BaseDirectory;

        var missing = icons
            .Where(i => !knownGlyphs.Contains(i))
            .Where(i => !File.Exists(Path.Combine(baseDir, "Icons", "Light", i + ".png")))
            .ToList();

        Assert.True(missing.Count == 0,
            "These toolbar icons have no Icons\\Light\\ variant, so they will be brightened "
            + "30% toward white in dark mode as though they were old glyphs: "
            + string.Join(", ", missing)
            + ". Either ship the light artwork or add the name to knownGlyphs above.");

        // And the list does not rot: an entry that has since gained its
        // light variant should come off it.
        var nowIllustrated = knownGlyphs
            .Where(i => File.Exists(Path.Combine(baseDir, "Icons", "Light", i + ".png")))
            .ToList();

        Assert.True(nowIllustrated.Count == 0,
            "These are listed as glyphs but now ship a light variant - remove them from "
            + "knownGlyphs: " + string.Join(", ", nowIllustrated));
    }

    /// <summary>
    /// The three companion pages are all on the toolbar, in the order the
    /// Help text describes them - the mechanism, then the theory, then the
    /// harmony, with the Harmonics button last. The help section says "the
    /// last button on the main toolbar" and "third from the end", and those
    /// sentences go stale silently.
    /// </summary>
    [Fact]
    public void TheThreeCompanionPagesAreInTheOrderHelpDescribes()
    {
        var names = ToolbarButtonNames(MainFormSource());

        var antikythera = names.IndexOf("antikytheraButton");
        var almagest = names.IndexOf("almagestButton");
        var harmonics = names.IndexOf("harmonicsButton");

        Assert.True(antikythera >= 0 && almagest >= 0 && harmonics >= 0,
            "One of the three companion buttons is no longer in the toolbar table.");

        Assert.True(antikythera < almagest && almagest < harmonics,
            "The three companion pages are out of order on the toolbar.");

        Assert.Equal(names.Count - 1, harmonics);
        Assert.Equal(names.Count - 3, antikythera);
    }

    /// <summary>
    /// The toolbar still fits the window it opens in.
    ///
    /// The row is laid out left to right from fixed arithmetic while the
    /// right-hand group is pinned inward from the right edge, so the two
    /// grow toward each other and nothing stops them meeting. At the
    /// seventeenth button there is still room, but the margin is now a
    /// number worth knowing rather than an assumption - this form has had
    /// two layout collisions already, both of them things sliding under
    /// something else in the z-order.
    /// </summary>
    [Fact]
    public void TheToolbarRowStillFitsTheDefaultWindow()
    {
        var source = MainFormSource();
        var count = ToolbarButtonNames(source).Count;

        // From the placement loop: 46 wide, 4 apart, starting at 10, with an
        // extra 14 after Forward and another after Search.
        const int size = 46, gap = 4, start = 10, extraGaps = 28;
        var rowRight = start + count * (size + gap) - gap + extraGaps;

        // The right-hand group: seven buttons 36 wide, 8 apart, inside a
        // 20px margin, measured from the client width.
        const int rightButtons = 7, rightWidth = 36, rightGap = 8, margin = 20;
        var rightGroupWidth = rightButtons * rightWidth + (rightButtons - 1) * rightGap + margin;

        // Width = 1840 in the constructor; the client area is a little less.
        const int clientWidth = 1840 - 16;
        var rightGroupLeft = clientWidth - rightGroupWidth;

        Assert.True(rowRight < rightGroupLeft,
            "The toolbar row now reaches " + rowRight + " and the right-hand group starts at "
            + rightGroupLeft + ". They overlap, and because the right-hand group is added to "
            + "Controls after the toolbar buttons it will be drawn behind them - so the "
            + "symptom will be a button that is present, hit-testable and invisible.");
    }

    private static string MainFormSource() =>
        File.ReadAllText(Path.Combine(UiSourceDirectory(), "MainForm.cs"));

    /// <summary>
    /// The identifiers in the toolbarButtons table, in table order.
    /// </summary>
    private static List<string> ToolbarButtonNames(string source) =>
        ToolbarTable(source)
            .Select(m => m.Groups[1].Value)
            .ToList();

    private static List<string> ToolbarIconNames(string source) =>
        ToolbarTable(source)
            .Select(m => m.Groups[2].Value)
            .ToList();

    /// <summary>
    /// Entries look like:  (almagestButton, "Ptolemy's Cosmos", "Almagest"),
    /// </summary>
    private static List<Match> ToolbarTable(string source)
    {
        var start = source.IndexOf("var toolbarButtons = new", StringComparison.Ordinal);
        Assert.True(start >= 0, "The toolbarButtons table is no longer in MainForm.cs.");

        var open = source.IndexOf('{', start);
        var close = source.IndexOf("};", open, StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open, "Could not find the bounds of the toolbarButtons table.");

        var table = source.Substring(open, close - open);

        return Regex.Matches(table, @"\(\s*(\w+)\s*,\s*""[^""]*""\s*,\s*""([^""]+)""\s*\)")
            .Cast<Match>()
            .ToList();
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
