using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Two ways a colour escapes the theme, both found after 3.6.7 shipped.
///
/// The first is a property the theme never read. A LinkLabel paints its link
/// text with LinkColor; ReadingTheme.Apply set ForeColor, which LinkLabel does
/// not use for that. Because LinkLabel derives from Label, the Label case in
/// Apply's switch matched it and the code looked entirely correct. Seven links
/// across four themed windows kept the WinForms default blue - 1.94:1 on the
/// dark surface, worse than the DarkRed that 3.6.5 called unreadable - and
/// they included the two a new reader needs in order to set up AI translation.
///
/// The second is a colour the theme actively discarded. The owner-draw painter
/// filled every list row with the list's own BackColor, so a row that set its
/// own was painted over. Stylometry marks its most interesting rows that way,
/// and not one of those marks had ever been drawn, in either theme.
///
/// These tests do not touch the persisted theme. ReadingTheme.SetMode and
/// Toggle write to the user's theme.txt, so the assertions are written to hold
/// in whichever mode the machine running them happens to be in.
/// </summary>
public class ThemedControlColourTests
{
    /// <summary>WCAG 2.x relative luminance.</summary>
    private static double Luminance(Color c)
    {
        static double Channel(double v)
        {
            v /= 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void ApplyGivesALinkLabelTheThemesLinkColour()
    {
        using var host = new Panel();
        using var link = new LinkLabel { Text = "Get an API key" };
        host.Controls.Add(link);

        ReadingTheme.Apply(host);

        Assert.Equal(ReadingTheme.LinkText, link.LinkColor);
    }

    /// <summary>
    /// The specific failure. Blue is what a LinkLabel arrives with, and what
    /// it kept through every theme pass until 3.6.8.
    /// </summary>
    [Fact]
    public void AThemedLinkIsNotLeftAtTheDefaultBlue()
    {
        using var host = new Panel();
        using var link = new LinkLabel { Text = "Get a free API key" };
        host.Controls.Add(link);

        Assert.Equal(Color.FromArgb(0, 0, 255), link.LinkColor);

        ReadingTheme.Apply(host);

        Assert.NotEqual(Color.FromArgb(0, 0, 255), link.LinkColor);
    }

    /// <summary>
    /// Visited defaults to purple, which is 1.77:1 on the dark surface - worse
    /// than the blue this replaced, and reachable simply by having clicked the
    /// link once before.
    /// </summary>
    [Fact]
    public void AVisitedLinkIsAlsoReadable()
    {
        using var host = new Panel();
        using var link = new LinkLabel { Text = "See current pricing" };
        host.Controls.Add(link);

        ReadingTheme.Apply(host);

        Assert.True(Contrast(link.VisitedLinkColor, ReadingTheme.Background) >= 4.5,
            $"a visited link is {Contrast(link.VisitedLinkColor, ReadingTheme.Background):0.00}:1 " +
            "against the current background");
    }

    [Theory]
    [InlineData("link")]
    [InlineData("muted")]
    [InlineData("warning")]
    public void TheThemesTextColoursAreLegibleOnItsOwnSurfaces(string which)
    {
        var colour = which switch
        {
            "link" => ReadingTheme.LinkText,
            "muted" => ReadingTheme.MutedText,
            _ => ReadingTheme.WarningText
        };

        foreach (var surface in new[] { ReadingTheme.Background, ReadingTheme.Surface })
        {
            Assert.True(Contrast(colour, surface) >= 4.5,
                $"{which} is {Contrast(colour, surface):0.00}:1 on rgb({surface.R},{surface.G},{surface.B}), " +
                "under the 4.5:1 minimum for body text");
        }
    }

    /// <summary>
    /// A LinkLabel is a Label, so the LinkLabel case has to be matched first.
    /// Reordering the switch would silently restore the bug.
    /// </summary>
    [Fact]
    public void ALinkLabelIsStillALabelAndMustBeMatchedFirst()
    {
        using var link = new LinkLabel();
        Assert.IsAssignableFrom<Label>(link);
    }

    [Fact]
    public void ARowThatSetItsOwnColourKeepsIt()
    {
        using var list = new ListView { BackColor = Color.FromArgb(24, 24, 26) };
        var highlighted = new ListViewItem("residual") { BackColor = Color.FromArgb(42, 62, 46) };
        list.Items.Add(highlighted);

        Assert.Equal(
            Color.FromArgb(42, 62, 46),
            ReadingTheme.RowBackground(highlighted, list, selected: false));
    }

    [Fact]
    public void ARowThatSetNoColourTakesTheListsOwn()
    {
        using var list = new ListView { BackColor = Color.FromArgb(24, 24, 26) };
        var plain = new ListViewItem("ordinary");
        list.Items.Add(plain);

        Assert.Equal(
            Color.FromArgb(24, 24, 26),
            ReadingTheme.RowBackground(plain, list, selected: false));
    }

    /// <summary>
    /// Selection still wins, or a highlighted row would stop showing that it
    /// is the one being looked at.
    /// </summary>
    [Fact]
    public void SelectionOutranksARowsOwnColour()
    {
        using var list = new ListView { BackColor = Color.FromArgb(24, 24, 26) };
        var highlighted = new ListViewItem("residual") { BackColor = Color.FromArgb(42, 62, 46) };
        list.Items.Add(highlighted);

        Assert.Equal(
            ReadingTheme.SelectionBackground,
            ReadingTheme.RowBackground(highlighted, list, selected: true));
    }
}
