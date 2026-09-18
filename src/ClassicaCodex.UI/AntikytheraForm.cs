using System.Diagnostics;
using System.Reflection;

namespace ClassicaCodex.UI;

/// <summary>
/// The card that stands beside the Antikythera mechanism: what it is, how
/// the simulation was made to work, and where every number in it came
/// from. The mechanism itself is a self-contained web page carried inside
/// this executable and opened in the reader's own browser.
///
/// Why a page in a browser rather than a window here. The machine is drawn
/// on a canvas at sixty frames a second while the reader drags a crank,
/// and it already existed as one file that runs anywhere. Re-implementing
/// it in WinForms would have bought nothing but a second copy to keep in
/// step with the first. The page carries no scripts from anywhere else and
/// makes no network requests at all, so it works with the machine offline,
/// and it ships no font - this application redistributes none, and the
/// release script fails the build if font data turns up in the payload.
///
/// Why the explanation is here rather than in the page. Someone clicking a
/// toolbar button in a library application has not asked for a planetarium.
/// This says what they are about to open, and admits how much of it is
/// reconstruction, before it opens.
/// </summary>
public class AntikytheraForm : ScaledForm
{
    private const string PageResource = "ClassicaCodex.UI.Pages.Antikythera.html";

    private readonly List<Label> _mutedLabels = new();

    public AntikytheraForm()
    {
        Text = "The Antikythera Mechanism";
        AppIcons.ApplyWindowIcon(this, "Antikythera");

        // ClientSize, not Width/Height: those set the outer window bounds
        // and would lose the title bar and borders off the content.
        ClientSize = new Size(760, 700);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 420);

        var scrollHost = new Panel
        {
            Left = 0,
            Top = 0,
            Width = 760,
            Height = 640,
            AutoScroll = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        const int textWidth = 708;
        var y = 16;

        AddHeading(scrollHost, "The Antikythera Mechanism", ref y, 15F, FontStyle.Bold);
        AddParagraph(scrollHost,
            "A Greek astronomical calculator, recovered in 1901 from a wreck off the island of "
            + "Antikythera and built somewhere around 100 BC. Thirty or so bronze gears behind a "
            + "pair of lettered dials. Turn the crank and it tells you where the Sun and Moon "
            + "stand among the stars, what phase the Moon is in, which month of the Greek "
            + "calendar it is, when the Panhellenic games fall, and when to expect an eclipse.",
            ref y, textWidth);
        AddParagraph(scrollHost,
            "This is a working simulation of it. Every wheel turns at the ratio its tooth count "
            + "demands, and every dial reads what the real one read.",
            ref y, textWidth);

        var openButton = new Button
        {
            Text = "Open the mechanism",
            Left = 16,
            Top = y,
            Width = 200,
            Height = 34
        };
        openButton.Click += (_, _) => OpenPage();
        scrollHost.Controls.Add(openButton);
        y += openButton.Height + 6;

        AddMuted(scrollHost,
            "Opens in your usual browser. It carries everything it needs and asks the network "
            + "for nothing, so it works offline.",
            ref y, textWidth);
        y += 10;

        AddHeading(scrollHost, "What you can do with it", ref y, 11F, FontStyle.Bold);
        AddParagraph(scrollHost,
            "Drag the crank. One turn is about eleven weeks, because the input wheel has "
            + "forty-eight teeth and drives the main wheel's two hundred and twenty-three.",
            ref y, textWidth);
        AddBullet(scrollHost, "The front dial",
            "The Sun and Moon against the Egyptian calendar and the zodiac, with the Moon "
            + "speeding up and slowing down as the real one does. In the open centre, six rings "
            + "carrying a little sphere each: Mercury, Venus, the true Sun, Mars, Jupiter, "
            + "Saturn. A sphere with a ring round it is running backwards through the zodiac.",
            ref y, textWidth);
        AddBullet(scrollHost, "The back dials",
            "A spiral of two hundred and thirty-five months, which is nineteen years, and "
            + "another of two hundred and twenty-three, which is how long eclipses take to come "
            + "round again. Fifty-one cells of the lower spiral carry a glyph saying an eclipse "
            + "falls in that month, and at what hour.",
            ref y, textWidth);
        AddBullet(scrollHost, "The gearing",
            "The wheels themselves, turning, coloured by whether their teeth were counted on a "
            + "surviving fragment, reconstructed, or are hypothesis.",
            ref y, textWidth);
        AddBullet(scrollHost, "Align to the sky",
            "Set the pointers the way an owner would and watch two thousand years of "
            + "accumulated drift collapse to what the gearing itself gets wrong.",
            ref y, textWidth);
        y += 8;

        AddHeading(scrollHost, "How it was made to work", ref y, 11F, FontStyle.Bold);
        AddParagraph(scrollHost,
            "The whole machine has one input, and every pointer is that number multiplied by an "
            + "exact fraction taken from the tooth counts. So the arithmetic is done in exact "
            + "fractions rather than decimals, and each train is checked against the whole-number "
            + "astronomical fact it exists to embody: two hundred and fifty-four sidereal months "
            + "in nineteen years for the Moon, two hundred and thirty-five synodic months in the "
            + "same nineteen for the calendar spiral. The test page makes a hundred and five "
            + "assertions and allows the gear trains no tolerance at all. One wrong tooth fails "
            + "it rather than quietly moving a pointer.",
            ref y, textWidth);
        AddParagraph(scrollHost,
            "The Moon does not move at a steady rate, and neither does it here. A pin on one "
            + "wheel runs in a slot on another whose axis is about a millimetre away, and that "
            + "millimetre makes the Moon hurry and lag by six and a half degrees either side of "
            + "its mean place. That is not an approximation of the effect. It is the eccentric "
            + "circle of Greek lunar theory built out of bronze, and the simulation uses the "
            + "published closed form of it rather than a sine wave that would look the same.",
            ref y, textWidth);
        AddParagraph(scrollHost,
            "The planets work the same way. Each of the five devices, however it is built, "
            + "produces the direction of a sum of two turning vectors, one going round once a "
            + "year and the other at the planet's own rate, their lengths in the ratio of that "
            + "planet's distance from the Sun to the Earth's. Written out, that is the "
            + "heliocentric vector sum seen from the Earth. Whoever laid it out had a working "
            + "model of the solar system and no idea they had one.",
            ref y, textWidth);
        AddParagraph(scrollHost,
            "To say how good the machine is, the page needs something better to compare it "
            + "against. The Sun and Moon come from Meeus's series, the planets from the "
            + "long-span Keplerian elements NASA publishes for three thousand years either side "
            + "of now. Both are one to three orders of magnitude better than the bronze, which "
            + "is all the comparison needs. The difference between Terrestrial and Universal "
            + "Time is carried too: at 100 BC it is about three hours, and the Moon moves half a "
            + "degree an hour, so leaving it out would have meant grading the machine against a "
            + "broken ruler.",
            ref y, textWidth);
        AddParagraph(scrollHost,
            "Set fresh against the sky, the machine holds the Moon to a fifth of a degree and "
            + "the six bodies of its Cosmos to about seven. Left running since 205 BC it is a "
            + "hundred degrees out on the Moon - and most of the planets' error turns out not to "
            + "be the gearing's fault at all, but precession moving the whole zodiac out from "
            + "under a scale cut in metal.",
            ref y, textWidth);
        y += 8;

        AddHeading(scrollHost, "How much of it is actually there", ref y, 11F, FontStyle.Bold);
        AddParagraph(scrollHost,
            "Less than the pictures suggest, and the page is built to say so. Four of the twelve "
            + "zodiac names are on the bronze. Three of the twelve month names. Twenty of the "
            + "fifty-one eclipse glyphs. One gear of the entire front survives, and what it drove "
            + "is disputed. No crank survives either - only the keyway where the drive went in, "
            + "so the handle you turn is the part we are least sure about.",
            ref y, textWidth);
        AddParagraph(scrollHost,
            "So attested lettering is drawn brighter than lettering we supply, reconstructed "
            + "wheels are dimmer than counted ones, purely hypothetical wheels are outlines, and "
            + "the two wheels that belong to no train anyone has worked out sit at the bottom of "
            + "the gearing view connected to nothing. A reconstruction drawn with the same "
            + "confidence as the surviving bronze is a lie told in good faith.",
            ref y, textWidth);
        y += 8;

        AddHeading(scrollHost, "Where the numbers come from", ref y, 11F, FontStyle.Bold);
        AddMuted(scrollHost,
            "Nothing in the simulation is from memory. Every tooth count, inscription and "
            + "constant was taken from the published literature, and where the literature "
            + "disagrees - as it does, sharply, on several tooth counts - the disagreement is "
            + "recorded in the source rather than smoothed over.",
            ref y, textWidth);
        y += 6;

        AddSource(scrollHost,
            "The gearing, and the lunar anomaly",
            "Freeth, Bitsakis, Moussas, Seiradakis, Tselikas, Mangou, Zafeiropoulou, Hadland, "
            + "Bate, Ramsey, Allen, Crawley, Hockley, Malzbender, Gelb, Ambrisco & Edmunds, "
            + "“Decoding the ancient Greek astronomical calculator known as the Antikythera "
            + "Mechanism”, Nature 444 (2006), 587–591, with its Supplementary Notes.",
            null, ref y, textWidth);

        AddSource(scrollHost,
            "The Games dial and the eclipse prediction",
            "Freeth, Jones, Steele & Bitsakis, “Calendars with Olympiad display and eclipse "
            + "prediction on the Antikythera Mechanism”, Nature 454 (2008), 614–617.",
            null, ref y, textWidth);

        AddSource(scrollHost,
            "The complete gearing tables",
            "Freeth & Jones, “The Cosmos in the Antikythera Mechanism”, ISAW Papers 4 "
            + "(2012). Open access, and the single most useful source here.",
            "https://dlib.nyu.edu/awdl/isaw/isaw-papers/4/", ref y, textWidth);

        AddSource(scrollHost,
            "The eclipse scheme and its epoch",
            "Freeth, “Eclipse Prediction on the Ancient Greek Astronomical Calculating "
            + "Machine known as the Antikythera Mechanism”, PLOS ONE 9(7): e103275 (2014); "
            + "and Carman & Evans, “On the epoch of the Antikythera mechanism and its "
            + "eclipse predictor”, Archive for History of Exact Sciences 68 (2014).",
            "https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0103275",
            ref y, textWidth);

        AddSource(scrollHost,
            "The epoch dates, reconsidered",
            "Jones, “The Epoch Dates of the Antikythera Mechanism (with an Appendix on its "
            + "Authenticity)”, ISAW Papers 17 (2020).",
            "https://isaw.nyu.edu/publications/isaw-papers/17/", ref y, textWidth);

        AddSource(scrollHost,
            "Every inscription, in Greek",
            "The six papers of “Inscriptions of the Antikythera Mechanism”, Almagest "
            + "7.1 (2016), by Bitsakis, Jones, Anastasiou, Moussas, Tselikas, Zafeiropoulou and "
            + "Steele - the critical edition. The sixth festival on the Games dial is Iversen's "
            + "reading, and it is the reason Rhodes is in the argument at all.",
            null, ref y, textWidth);

        AddSource(scrollHost,
            "The planets, and the front display",
            "Freeth, Higgon, Dacanalis, MacDonald, Georgakopoulou & Wojcik, “A Model of the "
            + "Cosmos in the ancient Greek Antikythera Mechanism”, Scientific Reports 11: "
            + "5821 (2021). Open access. Almost nothing of this front display survives, and the "
            + "paper says so in its own main text.",
            "https://www.nature.com/articles/s41598-021-84310-w", ref y, textWidth);

        AddSource(scrollHost,
            "The sky to compare it against",
            "Meeus, Astronomical Algorithms, 2nd edition (Willmann-Bell, 1998), for the Sun, the "
            + "Moon and the Julian day; Standish's Keplerian elements, published by NASA's Jet "
            + "Propulsion Laboratory, for the planets; and the Espenak and Meeus polynomial "
            + "expressions for the difference between Terrestrial and Universal Time.",
            "https://ssd.jpl.nasa.gov/planets/approx_pos.html", ref y, textWidth);

        // AutoScroll takes its range from the last child control, so an
        // explicit bottom margin keeps the final line clear of the button
        // strip at non-default Windows text scaling.
        scrollHost.AutoScrollMinSize = new Size(0, y + 28);

        var closeButton = new Button
        {
            Text = "Close",
            Left = 654,
            Top = 652,
            Width = 90,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };

        Controls.Add(scrollHost);
        Controls.Add(closeButton);
        AcceptButton = closeButton;

        // The generic theme walk cannot tell which labels are meant to be
        // quieter than the rest, so those are set here and re-set whenever
        // the theme changes.
        ReadingTheme.AttachTo(this, () =>
        {
            foreach (var label in _mutedLabels) label.ForeColor = ReadingTheme.MutedText;
        });

        WindowShortcuts.CloseOnEscape(this);
    }

    /// <summary>
    /// The font a control actually draws in. SystemFonts.DefaultFont is
    /// Microsoft Sans Serif 8.25pt and is not it; measuring against that
    /// loses the last line of a wrapped paragraph at 125% text scaling.
    /// </summary>
    private static Font PageFont => Control.DefaultFont;

    private static void AddHeading(Control parent, string text, ref int y, float size, FontStyle style)
    {
        var font = new Font("Segoe UI", size, style);
        var label = new Label
        {
            Text = text,
            UseMnemonic = false,
            Left = 16,
            Top = y,
            Width = 708,
            AutoSize = false,
            Height = font.Height + 6,
            Font = font
        };
        parent.Controls.Add(label);
        y += label.Height + 6;
    }

    private static Label MakeParagraph(string text, int top, int width)
    {
        return new Label
        {
            Text = text,
            UseMnemonic = false,
            Left = 16,
            Top = top,
            Width = width,
            AutoSize = false,
            // Measured against a slightly narrower line than the label's
            // own width, because the renderer breaks a shade earlier than
            // the measurement does and the last line would be clipped.
            Height = TextRenderer.MeasureText(text, PageFont,
                new Size(width - 10, int.MaxValue), TextFormatFlags.WordBreak).Height + 10
        };
    }

    private static void AddParagraph(Control parent, string text, ref int y, int width)
    {
        var label = MakeParagraph(text, y, width);
        parent.Controls.Add(label);
        y += label.Height + 6;
    }

    private void AddMuted(Control parent, string text, ref int y, int width)
    {
        var label = MakeParagraph(text, y, width);
        label.ForeColor = ReadingTheme.MutedText;
        _mutedLabels.Add(label);
        parent.Controls.Add(label);
        y += label.Height + 6;
    }

    private void AddBullet(Control parent, string title, string body, ref int y, int width)
    {
        var titleLabel = new Label
        {
            Text = "•  " + title,
            UseMnemonic = false,
            Left = 16,
            Top = y,
            Width = width,
            AutoSize = false,
            Height = PageFont.Height + 4,
            Font = new Font(PageFont, FontStyle.Bold)
        };
        parent.Controls.Add(titleLabel);
        y += titleLabel.Height + 2;

        var bodyLabel = MakeParagraph(body, y, width - 26);
        bodyLabel.Left = 42;
        bodyLabel.ForeColor = ReadingTheme.MutedText;
        _mutedLabels.Add(bodyLabel);
        parent.Controls.Add(bodyLabel);
        y += bodyLabel.Height + 8;
    }

    private void AddSource(Control parent, string title, string citation, string? url, ref int y, int width)
    {
        var titleLabel = new Label
        {
            Text = title,
            UseMnemonic = false,
            Left = 16,
            Top = y,
            Width = width,
            AutoSize = false,
            Height = PageFont.Height + 4,
            Font = new Font(PageFont, FontStyle.Bold)
        };
        parent.Controls.Add(titleLabel);
        y += titleLabel.Height + 2;

        var citationLabel = MakeParagraph(citation, y, width);
        citationLabel.ForeColor = ReadingTheme.MutedText;
        _mutedLabels.Add(citationLabel);
        parent.Controls.Add(citationLabel);
        y += citationLabel.Height + 2;

        if (url != null)
        {
            var link = new LinkLabel
            {
                Text = url,
                UseMnemonic = false,
                Left = 16,
                Top = y,
                Width = width,
                AutoSize = false,
                Height = PageFont.Height + 4
            };
            link.LinkClicked += (_, _) => OpenUrl(url);
            parent.Controls.Add(link);
            y += link.Height + 2;
        }

        y += 8;
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Couldn't open the link",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Writes the page out beside the other temporary files and hands it to
    /// the shell.
    ///
    /// It is rewritten on every open rather than cached, so a file left
    /// half-written by an interrupted earlier run is not opened forever
    /// afterwards. If the write fails because a browser still has the file
    /// from a previous open, the existing copy is used: it is the same
    /// bytes, and refusing to show the page over a sharing lock would be
    /// the wrong answer.
    /// </summary>
    internal static string ExtractPage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ClassicaCodex");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Antikythera.html");

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PageResource)
            ?? throw new InvalidOperationException(
                "The Antikythera page is missing from this build.");

        try
        {
            using var file = File.Create(path);
            resource.CopyTo(file);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Already open somewhere. The copy on disk is this same page.
        }

        return path;
    }

    private void OpenPage()
    {
        try
        {
            var path = ExtractPage();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashReporter.LogHandled(ex, "opening the Antikythera mechanism");
            MessageBox.Show(this,
                "Couldn't open the mechanism: " + ex.Message,
                "The Antikythera Mechanism",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
