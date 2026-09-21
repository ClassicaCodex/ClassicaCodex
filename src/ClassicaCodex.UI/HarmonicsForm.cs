using System.Diagnostics;
using System.Reflection;

namespace ClassicaCodex.UI;

/// <summary>
/// The card that stands in front of Greek harmonic science: what the page
/// is, how it is kept honest, and - before anything else - that it makes
/// a noise. The page itself is a self-contained web page carried inside
/// this executable and opened in the reader's own browser.
///
/// WHY THE WARNING IS THE FIRST THING ON THE CARD. Its two siblings open
/// a diagram; this one opens an instrument. Someone clicking a toolbar
/// button in a library application has not asked for a sound, and may be
/// somewhere they must not make one. So the caution is above the
/// description rather than below it, the button says what it will do,
/// and the page behind it still plays nothing until a second, separate
/// press on the page itself. Two gates, deliberately: the reader can
/// open the page in a quiet room, read all of it, and never make a
/// sound.
///
/// It also happens to be what browsers require - an AudioContext will
/// not open without a gesture - so the honest design and the technical
/// one agree.
///
/// WHY A PAGE IN A BROWSER rather than a window here. Same reasons as
/// the other two: it already existed as one file that runs anywhere, it
/// makes no network requests, and it ships no font. It ships no audio
/// either - every note is synthesised from its exact whole-number ratio
/// at the moment it sounds, which is the only way the page can claim the
/// intervals are the ones Ptolemy wrote. A recording would fix the
/// tuning at whatever the recorder believed.
///
/// This form follows AlmagestForm rather than AntikytheraForm: a
/// FlowLayoutPanel that measures at layout time, not a stack of heights
/// computed up front with MeasureText, which over-allocates as roughly
/// the square of the display scaling. See the long note in AlmagestForm
/// for the measurements.
/// </summary>
public class HarmonicsForm : ScaledForm
{
    private const string PageResource = "ClassicaCodex.UI.Pages.Harmonics.html";

    private readonly List<Label> _mutedLabels = new();
    private readonly List<Label> _cautionLabels = new();

    public HarmonicsForm()
    {
        Text = "Ptolemy's Harmony";
        AppIcons.ApplyWindowIcon(this, "Harmonics");

        // ClientSize, not Width/Height: those set the outer window bounds
        // and would lose the title bar and borders off the content.
        ClientSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 400);
        FormBorderStyle = FormBorderStyle.Sizable;

        // NO VERTICAL PADDING ON THE SCROLLER. A ScrollableControl measures
        // its extent from the top of its padding box and then subtracts
        // Padding.Top from it, so top padding is reserved by layout and then
        // taken off the range you can scroll through - the content ends up
        // exactly Padding.Top pixels longer than the scrollbar can reach and
        // the deficit comes off the last line. Measured in AlmagestForm,
        // where the formula and the numbers are written down. The vertical
        // gaps travel with the content here, as spacers.
        const int gutter = 16;
        const int gap = 14;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(gutter, 0, gutter, 0)
        };

        flow.Controls.Add(Spacer(gap));

        AddHeading(flow, "Ptolemy's Harmony", 15F, FontStyle.Bold);

        // ---- the caution, before the description --------------------------
        AddCaution(flow, "This one makes sound.");

        AddParagraph(flow,
            "It is a musical instrument, so hearing it is most of the point - but it will not "
            + "play anything by itself. The page opens silent and stays silent until you press "
            + "a button on it marked \"Turn the sound on\". If you are in a library, or on a "
            + "train, or anywhere a sudden note would be unwelcome, you can open it, read all "
            + "of it and work the monochord without a sound: the ratios, the table and the "
            + "diagram all run in silence, and only the notes are missing. Somewhere you can "
            + "listen, turn it on - a page about music that you cannot hear is half a page.");

        AddMuted(flow,
            "Headphones are worth it for the enharmonic. Its two smallest steps are about a "
            + "third of a semitone each, and a laptop speaker will blur them into one.");

        // ---- what it is ----------------------------------------------------
        AddHeading(flow, "What it is", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "Greek harmonic science on the instrument it was found on: a monochord, which is "
            + "one string with a bridge you can move. Drag the bridge and the pitch rises as "
            + "the sounding length falls - the octave at half the string, the fifth at two "
            + "thirds, the fourth at three quarters. That inverse is the whole of the "
            + "discovery, and it is why a musical interval is a ratio of two small whole "
            + "numbers rather than a matter of taste.");

        AddParagraph(flow,
            "The rest of the page is the argument that followed. A tetrachord spans a perfect "
            + "fourth, exactly 4:3; its outer notes are fixed and its two inner ones move, and "
            + "where you put them is what makes a genus. Twenty divisions of it are here, from "
            + "Archytas in the fourth century BC through Eratosthenes and Didymus to Ptolemy "
            + "at Alexandria around AD 150 - the Harmonics being the companion to the Almagest, "
            + "which is the page next door on this toolbar.");

        AddHeading(flow, "What is worth hearing", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "The enharmonic genus. Its lowest two steps are a pair of quarter-tones crowded "
            + "under a wide major third, and it sounds nothing like a scale - Greek writers "
            + "were already complaining in Ptolemy's day that singers could no longer manage "
            + "it. Hearing it settles in two seconds an argument that print cannot make at all.");

        AddParagraph(flow,
            "And the beating. Hold the open string, drag slowly through the fifth, and a little "
            + "either side of it the two notes wobble against each other at a rate you can "
            + "count. At exactly 3:2 the wobble stops and the pair locks. That is what a just "
            + "interval is, and it is why small whole numbers were trusted over ears.");

        AddParagraph(flow,
            "Then Didymus against Ptolemy. They divide the fourth with the identical three "
            + "intervals and differ only in the order of two of them, which moves the middle "
            + "note by a syntonic comma - a fifth of a semitone. The page plays one, then the "
            + "other, then both together over a held string, where the disagreement becomes a "
            + "slow beat rather than a fact you have to take on trust.");

        // ---- how it is kept honest ------------------------------------------
        AddHeading(flow, "How it is kept honest", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "A wrong ratio in a page like this does not break anything. It sounds - and it "
            + "sounds like music, because almost any ratio near the right one does. Nobody "
            + "alive can hear 46:45 against 45:44. So the arithmetic is checked instead of the "
            + "ear: every division is required to multiply out to exactly 4:3, which is what a "
            + "tetrachord is, and a mistyped ratio almost never still does. Every ratio is also "
            + "typed a second time, independently, in the test file and compared with the "
            + "first copy. 106 checks, and one deliberate skip.");

        AddParagraph(flow,
            "The skip is the Pythagorean enharmonic. Its large interval was the ditone 81:64 "
            + "over a pyknon of 256:243, but how that pyknon was divided is simply not "
            + "recorded. It is shown in the table undivided and it cannot be played, because "
            + "inventing a plausible split would have made it the only false line in the page.");

        AddParagraph(flow,
            "Two of the labels in the table are computed rather than asserted, so they cannot "
            + "drift from the numbers beside them. \"Pyknon\" means the two lower intervals "
            + "together are smaller than the top one, which is the real structural difference "
            + "between the enharmonic and chromatic genera and the diatonic. \"Epimoric\" means "
            + "every ratio in the division is superparticular, of the form (n+1):n - the form "
            + "Ptolemy insists on. Seven of his eight genera are epimoric throughout. The "
            + "eighth is the Pythagorean tuning he reports rather than endorses, and it is the "
            + "one that is not.");

        // ---- where the numbers came from --------------------------------------
        //
        // The page is worth nothing if its ratios cannot be checked, and it
        // would be worth less than nothing if this card implied a chain of
        // authority the numbers did not actually come through. They came from
        // two reference tables and an arithmetic identity. That is said here
        // plainly, with the standard editions named as what to consult rather
        // than as what was consulted.
        AddHeading(flow, "Where the numbers came from", 11F, FontStyle.Bold);

        AddSource(flow,
            "The text",
            "Claudius Ptolemy, Harmonics, Alexandria, c. AD 150 - the genera at I.15-16 and "
            + "II.14, which also preserve the divisions of Archytas, Eratosthenes and Didymus. "
            + "The standard translations are Jon Solomon, Ptolemy Harmonics: Translation and "
            + "Commentary (Brill 2000), and Andrew Barker, Greek Musical Writings II: Harmonic "
            + "and Acoustic Theory (Cambridge 1989).",
            null);

        AddSource(flow,
            "The standard catalogue",
            "John Chalmers, Divisions of the Tetrachord (Frog Peak, 1993) - chapter 2 for the "
            + "arithmetic tradition, chapter 9 for the catalog itself. Scans of the original "
            + "edition are hosted at Dartmouth.",
            "https://eamusic.dartmouth.edu/~larry/published_articles/divisions_of_the_tetrachord/");

        AddSource(flow,
            "What was actually read, and what was not",
            "The twenty divisions in this page were taken from two independent modern "
            + "reference tables which agree completely on every ratio, attribution and "
            + "ordering, and each one was then checked to span exactly 4:3. Chalmers and the "
            + "translations above are named as where to settle a disputed number, not as the "
            + "route these numbers travelled - the Chalmers PDF could not be opened when the "
            + "page was built. The research document in the page's own repository tags every "
            + "figure with the source it came through, and says the same thing at more length.",
            null);

        AddMuted(flow,
            "Ordering matters more than it looks, which is why it is recorded as a sourced "
            + "fact rather than assumed. Didymus's diatonic and Ptolemy's tense diatonic are "
            + "the same three ratios; the order of two of them is the only thing that "
            + "distinguishes the pair, and they are the most confusable in Greek harmonics.");

        AddHeading(flow, "The sound itself", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "No audio is shipped and none is downloaded. Every note is built from its ratio at "
            + "the moment it sounds, as a plucked string with its upper harmonics - a plain "
            + "sine tone would hide the beating, which is the thing worth hearing. The page "
            + "makes no network requests of any kind and carries no fonts, so it works with "
            + "the machine offline, exactly like the mechanism and the planetarium beside it.");

        flow.Controls.Add(Spacer(gap));

        Controls.Add(flow);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52 };

        // The button says what it will do. Someone who has read nothing else
        // on this card has read the button.
        var openButton = new Button
        {
            Text = "Open it (it can make sound)",
            UseMnemonic = false,
            AutoSize = true,
            Left = 16,
            Top = 12
        };
        openButton.Click += (_, _) => OpenPage();
        footer.Controls.Add(openButton);

        var closeButton = new Button
        {
            Text = "Close",
            UseMnemonic = false,
            AutoSize = true,
            Left = 240,
            Top = 12,
            DialogResult = DialogResult.Cancel
        };
        closeButton.Click += (_, _) => Close();
        footer.Controls.Add(closeButton);

        Controls.Add(footer);
        AcceptButton = openButton;
        CancelButton = closeButton;

        // THE CARD MUST OPEN AT ITS TOP - and on this card that is not
        // cosmetic, because the thing at the top is the warning.
        //
        // At activation, between Load and Shown, WinForms focuses the first
        // SELECTABLE control in tab order and ScrollableControl scrolls it
        // into view. A Label is not selectable but a LinkLabel is, so the
        // first citation link - two thirds of the way down, well past the
        // caution - would decide where this card opened. Setting
        // ActiveControl to a control OUTSIDE the panel means the scroll never
        // happens. Measured and written up in AlmagestForm, along with the
        // three alternatives that are worse.
        //
        // IN Load, NOT IN THE CONSTRUCTOR: assigning ActiveControl forces
        // handle creation, and ScaledForm suspends layout in its constructor
        // and resumes in OnHandleCreated so the DPI scale reaches every
        // control together. Forcing the handle from here would scale half a
        // form.
        Load += (_, _) => ActiveControl = closeButton;

        // Belt and braces, and cheap.
        Shown += (_, _) => flow.AutoScrollPosition = new Point(0, 0);

        // The generic theme walk cannot tell which labels are meant to be
        // quieter than the rest, or which is meant to be louder, so those are
        // set here and re-set whenever the theme changes.
        ReadingTheme.AttachTo(this, () =>
        {
            foreach (var label in _mutedLabels) label.ForeColor = ReadingTheme.MutedText;
            foreach (var label in _cautionLabels) label.ForeColor = ReadingTheme.ActiveLinkText;
        });

        WindowShortcuts.CloseOnEscape(this);
    }

    /// <summary>
    /// A vertical gap that is part of the content rather than the container,
    /// so that it lands inside the scrollable extent. A Panel and not a
    /// Label, because Panel is not selectable and so can never become the
    /// focused control and drag the view to itself.
    /// </summary>
    private static Panel Spacer(int height) => new()
    {
        Width = 1,
        Height = 0,
        TabStop = false,
        Margin = new Padding(0, 0, 0, height)
    };

    /// <summary>
    /// A wrapped paragraph that measures itself. AutoSize with a MaximumSize
    /// whose width is set and whose height is zero is the WinForms idiom for
    /// "wrap at this width, grow to fit"; the width is re-set on every layout
    /// of the parent, so it follows a resized window and a scaled display
    /// without any arithmetic here.
    /// </summary>
    private static Label AddWrapped(FlowLayoutPanel parent, string text, Font font)
    {
        var label = new Label
        {
            Text = text,
            UseMnemonic = false,
            AutoSize = true,
            Font = font,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(Math.Max(120, parent.ClientSize.Width - parent.Padding.Horizontal - 24), 0)
        };
        parent.Controls.Add(label);

        parent.ClientSizeChanged += (_, _) =>
        {
            var w = Math.Max(120, parent.ClientSize.Width - parent.Padding.Horizontal - 24);
            label.MaximumSize = new Size(w, 0);
        };

        return label;
    }

    private void AddHeading(FlowLayoutPanel parent, string text, float size, FontStyle style)
    {
        var label = AddWrapped(parent, text, new Font("Segoe UI", size, style));
        label.Margin = new Padding(0, 8, 0, 6);
    }

    private void AddParagraph(FlowLayoutPanel parent, string text)
    {
        AddWrapped(parent, text, Control.DefaultFont);
    }

    private void AddMuted(FlowLayoutPanel parent, string text)
    {
        var label = AddWrapped(parent, text, Control.DefaultFont);
        _mutedLabels.Add(label);
    }

    /// <summary>
    /// The one line on this card that has to be read before the button is
    /// pressed. Bold, a size up, and in the theme's attention colour rather
    /// than its warning red - nothing is wrong, and a red line would say
    /// something was.
    /// </summary>
    private void AddCaution(FlowLayoutPanel parent, string text)
    {
        var label = AddWrapped(parent, text, new Font("Segoe UI", 11F, FontStyle.Bold));
        label.Margin = new Padding(0, 2, 0, 6);
        _cautionLabels.Add(label);
    }

    /// <summary>
    /// One citation: a bold heading, the reference itself in the quieter
    /// colour, and where it can be reached if it is online. The URL is its
    /// own clickable line rather than buried in the prose, because a reader
    /// who wants to check a number wants to find the link without reading
    /// the sentence.
    /// </summary>
    private void AddSource(FlowLayoutPanel parent, string title, string citation, string? url)
    {
        var heading = AddWrapped(parent, title, new Font(Control.DefaultFont, FontStyle.Bold));
        heading.Margin = new Padding(0, 6, 0, 2);

        var body = AddWrapped(parent, citation, Control.DefaultFont);
        body.Margin = new Padding(0, 0, 0, url == null ? 8 : 2);
        _mutedLabels.Add(body);

        if (url == null) return;

        var link = new LinkLabel
        {
            Text = url,
            UseMnemonic = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            MaximumSize = new Size(Math.Max(120, parent.ClientSize.Width - parent.Padding.Horizontal - 24), 0)
        };
        link.LinkClicked += (_, _) => OpenUrl(url);
        parent.Controls.Add(link);
        parent.ClientSizeChanged += (_, _) =>
        {
            link.MaximumSize = new Size(
                Math.Max(120, parent.ClientSize.Width - parent.Padding.Horizontal - 24), 0);
        };
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashReporter.LogHandled(ex, "opening a source link from Ptolemy's harmony");
            MessageBox.Show(this,
                "Couldn't open that link: " + ex.Message,
                "Ptolemy's Harmony",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Writes the page out beside the other temporary files and hands it to
    /// the shell. Rewritten on every open rather than cached, so a file left
    /// half-written by an interrupted earlier run is not opened forever
    /// afterwards. If the write fails because a browser still holds the file
    /// from a previous open, the existing copy is used: it is the same bytes.
    /// </summary>
    internal static string ExtractPage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ClassicaCodex");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Harmonics.html");

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PageResource)
            ?? throw new InvalidOperationException(
                "The Harmonics page is missing from this build.");

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
            CrashReporter.LogHandled(ex, "opening Ptolemy's harmony");
            MessageBox.Show(this,
                "Couldn't open it: " + ex.Message,
                "Ptolemy's Harmony",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
