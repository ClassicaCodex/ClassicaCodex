using System.Diagnostics;
using System.Reflection;

namespace ClassicaCodex.UI;

/// <summary>
/// The card that stands beside Ptolemy's planetarium: what the model is,
/// what it gets right, what it gets wrong, and where every number came
/// from. The planetarium itself is a self-contained web page carried
/// inside this executable and opened in the reader's own browser.
///
/// Why a page in a browser rather than a window here. The diagram is drawn
/// on a canvas at sixty frames a second while deferents and epicycles
/// turn, and it already existed as one file that runs anywhere. The page
/// makes no network requests at all, so it works with the machine offline,
/// and it ships no font - this application redistributes none, and the
/// release script fails the build if font data turns up in the payload.
///
/// Why the explanation is here rather than in the page. Someone clicking a
/// toolbar button in a library application has not asked for a
/// planetarium. This says what they are about to open, and admits what is
/// wrong with it, before it opens.
///
/// WHY THIS FORM USES A LAYOUT PANEL AND ITS SIBLING DOES NOT.
///
/// AboutForm and AntikytheraForm lay prose out by computing each label's
/// height up front with TextRenderer.MeasureText and then stacking the
/// results. That over-allocates as roughly the SQUARE of the display
/// scaling - measured at 1.64x at 125% and 2.54x at 150% - because the
/// wrap width is a design pixel while the font is already device-sized,
/// and then the autoscale walk multiplies the result again. It cannot
/// clip, so it is only ever loose spacing and extra scrolling, but it is
/// a known defect and the recorded fix is to stop computing the flow up
/// front. A FlowLayoutPanel measures at layout time, at the real font and
/// the real width, and is right at every DPI. So this window does that
/// rather than adding a third copy of the problem.
///
/// The client size is also deliberately smaller than its sibling's: at
/// 150% scaling a 760x700 design exceeds a 1080p work area, and
/// AntikytheraForm is Sizable so it can at least be dragged. This is too.
/// </summary>
public class AlmagestForm : ScaledForm
{
    private const string PageResource = "ClassicaCodex.UI.Pages.Almagest.html";

    private readonly List<Label> _mutedLabels = new();

    public AlmagestForm()
    {
        Text = "Ptolemy's Cosmos";
        AppIcons.ApplyWindowIcon(this, "Almagest");

        // ClientSize, not Width/Height: those set the outer window bounds
        // and would lose the title bar and borders off the content.
        ClientSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(520, 400);
        FormBorderStyle = FormBorderStyle.Sizable;

        // NO VERTICAL PADDING ON THE SCROLLER, and that is not a style choice.
        //
        // A ScrollableControl measures its scroll extent from the top of its
        // padding box: the extent is the union of the children's bounds plus
        // each child's bottom margin, MINUS DisplayRectangle.Y - which is
        // Padding.Top. So top padding is reserved by layout, pushing the
        // content down, and then subtracted from the range you can scroll
        // through. The content ends up exactly Padding.Top pixels longer than
        // the scrollbar can reach, and the deficit comes off the BOTTOM: first
        // out of the last child's bottom margin, then out of its glyphs.
        //
        // Measured, with the formula holding exactly in every configuration
        // tried:  clipped pixels = Padding.Top - lastChild.Margin.Bottom.
        // At Padding(16,14,16,14) with a 10px bottom margin that is 4px - the
        // descender band of a 16px font, which reads exactly as "the last line
        // is cut off". It gets worse with scaling, not better: 4px at 100%,
        // 6px at 125% and 150%.
        //
        // Padding.Bottom does not help, because it never enters the extent at
        // all: 40px of it bought zero reachable pixels. AutoScrollMargin does
        // not help either - it left VerticalScroll.Maximum identical and only
        // moved the initial scroll position, which makes the focus bug below
        // slightly worse.
        //
        // So the vertical gaps travel with the content instead, as spacers.
        // Measured trailing space after the change: +24px at 100%, +30px at
        // 125%, +36px at 150%, holding through a resize round-trip.
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

        AddHeading(flow, "Ptolemy's Cosmos", 15F, FontStyle.Bold);

        AddParagraph(flow,
            "The model of the heavens set out in the Almagest at Alexandria around AD 150, "
            + "turning at the rates Ptolemy's own tables give. The Earth is at the centre; each "
            + "planet rides a small circle, the epicycle, whose centre rides a large one, the "
            + "deferent. Between them those two circles produce the thing that broke every "
            + "earlier model: retrograde motion, where a planet stops, turns back on itself for "
            + "weeks, and then goes on.");

        AddParagraph(flow,
            "Nothing here is exaggerated to make the loops bigger. The epicycle radii, the "
            + "eccentricities, the apogees, the mean motions to six sexagesimal places and the "
            + "epoch positions are all his, and the page prints each one in the notation he "
            + "wrote it in beside the decimal it became.");

        AddHeading(flow, "The equant, and why it was the scandal", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "The epicycle's centre rides the deferent, but it does not move uniformly about the "
            + "deferent's centre. It moves uniformly as seen from a third point, the equant, as "
            + "far beyond the centre as the Earth is on the near side. That single device is "
            + "what makes the model fit the sky as well as it does, and it is what astronomers "
            + "from the Maragha school to Copernicus objected to for a thousand years: uniform "
            + "circular motion about a point that is not the centre is not, in the old physics, "
            + "uniform circular motion at all.");

        AddHeading(flow, "How wrong is it", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "Against modern theory, in Ptolemy's own lifetime, the model places the planets "
            + "within about a degree or two - Mercury, which he found hardest, is the worst. "
            + "Today it is out by seven to twelve degrees, and most of that is not the geometry: "
            + "he has precession at one degree a century where the truth is nearer 1.38, so "
            + "after nineteen centuries his whole frame has slipped.");

        AddParagraph(flow,
            "A further 1.15 degrees is there before any model error at all. Ptolemy's equinox "
            + "observations run about 28 hours late, which displaces his entire tropical frame - "
            + "and because every planet in the system is tied to the Sun, that one observational "
            + "error reaches all of them. The page will subtract it for you, because the "
            + "interesting question is not how wrong Ptolemy was but how wrong his geometry was, "
            + "given the observations he had.");

        AddHeading(flow, "In this library", 11F, FontStyle.Bold);
        AddParagraph(flow,
            "Cicero draws exactly this distinction in De re publica 1.22 - between a solid "
            + "star-globe of the old kind, from Thales through Eudoxus, and a sphere with the "
            + "motions of the Sun, the Moon and the five wandering stars in it, which the solid "
            + "kind could not do. That is the difference between a star-globe and a "
            + "planetarium, drawn by a Roman in the first century BC. The Somnium Scipionis at "
            + "6.17 gives the nine spheres. Both passages are on these shelves.");

        // ---- the sources --------------------------------------------------
        //
        // Every number in the page came from one of these, and the page is
        // worth nothing if that cannot be checked. The primary text is first,
        // then the working sources the parameters were actually read in, then
        // the modern theory the model is graded against.
        AddHeading(flow, "Where every number came from", 11F, FontStyle.Bold);

        AddSource(flow,
            "The text",
            "Claudius Ptolemy, Mathematike Syntaxis (the Almagest), Alexandria, c. AD 150. "
            + "Quoted throughout from G. J. Toomer's translation, Ptolemy's Almagest "
            + "(Duckworth 1984; Princeton 1998), with Heiberg's Greek text underlying it. "
            + "The parameters used here are from Books III (the Sun), IV-V (the Moon) and "
            + "IX-XI (the five planets).",
            null);

        AddSource(flow,
            "The standard commentaries",
            "O. Neugebauer, A History of Ancient Mathematical Astronomy (Springer 1975); "
            + "Olaf Pedersen, A Survey of the Almagest (Odense 1974; rev. Jones, Springer 2011); "
            + "James Evans, The History and Practice of Ancient Astronomy (OUP 1998).",
            null);

        AddSource(flow,
            "The parameter set, cross-checked line by line",
            "R. H. van Gent, Almagest Ephemeris Calculator (Universiteit Utrecht) - and its "
            + "JavaScript source, which was read directly rather than summarised. Every "
            + "eccentricity, epicycle radius, apogee, mean motion and epoch value in this page "
            + "was confirmed against it.",
            "https://webspace.science.uu.nl/~gent0113/astro/almagestephemeris_main.htm");

        AddSource(flow,
            "The equant, and Mercury's crank",
            "Dennis W. Duke (Florida State University), An Interesting Property of the Equant; "
            + "Ptolemy's Treatment of the Outer Planets; Almagest Planetary Model Animations. "
            + "The closed form used here for the equation of centre was derived independently "
            + "and then checked against Duke's animation source and against Fitzpatrick.",
            "https://people.sc.fsu.edu/~dduke/models.htm");

        AddSource(flow,
            "An independent derivation",
            "Richard Fitzpatrick, A Modern Almagest: An Updated Version of Ptolemy's Model of "
            + "the Solar System (University of Texas at Austin), sections 4.2-4.4.",
            "https://farside.ph.utexas.edu/Books/Syntaxis/Almagest.pdf");

        AddSource(flow,
            "The acceptance test",
            "Toomer's Appendix A, Example 14 - Mars at Nabonassar 886, Epiphi 15/16, 9 pm - "
            + "worked through against the printed tables by Jon Voisey, Following Kepler. "
            + "Ptolemy's tables give the true longitude as 241;35 degrees and his own "
            + "observation as 241;36. This engine returns 241;34,31, agreeing with his tables "
            + "to 0.8 seconds of arc at every intermediate step.",
            "https://jonvoisey.net/blog/2024/09/almagest-book-xi-calculating-planetary-longitude/");

        AddSource(flow,
            "The real sky, for grading",
            "Jet Propulsion Laboratory, Approximate Positions of the Planets - Table 2a, the "
            + "fit for 3000 BC to 3000 AD, with the Table 2b terms for Jupiter and Saturn. "
            + "Stated accuracy over that span is 20 arcseconds for Mercury and about 600 for "
            + "Saturn, against Ptolemaic errors measured in degrees - so the ruler is two "
            + "orders of magnitude finer than the thing being measured.",
            "https://ssd.jpl.nasa.gov/planets/approx_pos.html");

        AddSource(flow,
            "Delta T",
            "Fred Espenak and Jean Meeus, Polynomial Expressions for Delta T, after Morrison & "
            + "Stephenson (2004), published by NASA. Terrestrial time ran about 2 hours 34 "
            + "minutes ahead of universal time at AD 137; ignoring that would displace the Moon "
            + "by more than Ptolemy's own lunar error and make his good longitudes look bad.",
            "https://eclipse.gsfc.nasa.gov/SEcat5/deltatpoly.html");

        AddSource(flow,
            "Cicero",
            "De re publica 1.21-22 and 6.17, from the Latin Library text. Section numbering of "
            + "the Somnium Scipionis varies between editions; some cite 6.17 as 6.16.",
            "https://thelatinlibrary.com/cicero/repub1.shtml");

        AddMuted(flow,
            "The full research behind the page - eight documents, every figure carrying a "
            + "provenance tag and a source - lives with its own repository rather than here. "
            + "Where sources disagree, the page follows Toomer and says so.");

        flow.Controls.Add(Spacer(gap));

        Controls.Add(flow);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52 };

        var openButton = new Button
        {
            Text = "Open the planetarium",
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
            Left = 200,
            Top = 12,
            DialogResult = DialogResult.Cancel
        };
        closeButton.Click += (_, _) => Close();
        footer.Controls.Add(closeButton);

        Controls.Add(footer);
        AcceptButton = openButton;
        CancelButton = closeButton;

        // THE CARD MUST OPEN AT ITS TOP, and getting that takes one line in
        // the right place rather than the obvious ones.
        //
        // When the form activates - between Load and Shown, before the first
        // paint - WinForms gives focus to the first SELECTABLE control in tab
        // order, and ScrollableControl then scrolls that control into view. A
        // Label is not selectable but a LinkLabel is, so the first citation
        // link, two thirds of the way down, decided where this card opened.
        // Measured: the scroll position goes from 0 at Load to -316 at
        // Activated, and the first paint is already there.
        //
        // It is not a LinkLabel quirk - a plain Button in the panel does the
        // same - and AcceptButton has nothing to do with it.
        //
        // Setting ActiveControl to a control OUTSIDE the scrolling panel means
        // the scroll never happens in the first place. Three other fixes were
        // tried and measured, and each was worse: TabStop=false on the links
        // does stop it but collapses the tab order so the links cannot be
        // reached by keyboard at all; focusing a footer button in Shown moves
        // focus but does not undo the scroll; and ScrollControlIntoView on the
        // first child scrolls the leading gap away.
        //
        // IN Load, NOT IN THE CONSTRUCTOR, and that part is specific to this
        // application. Assigning ActiveControl forces the handle to be
        // created, and ScaledForm suspends layout in its own constructor and
        // resumes it in OnHandleCreated precisely so that every control the
        // derived constructor adds is in place before the scale is applied to
        // all of them together. Forcing the handle from here would fire that
        // resume in the middle of this constructor and scale half a form.
        // Load runs after the handle exists and before the activation that
        // assigns focus, which is exactly the window this needs.
        Load += (_, _) => ActiveControl = closeButton;

        // Belt and braces, and cheap: if anything ever puts focus inside the
        // panel before the first paint again, this still opens at the top.
        // Measured to land before paint, so it does not flicker.
        Shown += (_, _) => flow.AutoScrollPosition = new Point(0, 0);

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
    /// A vertical gap that is part of the content rather than part of the
    /// container, so that it lands inside the scrollable extent.
    ///
    /// A Panel and not a Label: Panel is not selectable, so a spacer can never
    /// become the focused control and drag the view to itself - which is the
    /// other bug this file had.
    /// </summary>
    private static Panel Spacer(int height) => new()
    {
        Width = 1,
        Height = 0,
        TabStop = false,
        Margin = new Padding(0, 0, 0, height)
    };

    /// <summary>
    /// A wrapped paragraph that measures itself.
    ///
    /// AutoSize with a MaximumSize whose width is set and whose height is
    /// zero is the WinForms idiom for "wrap at this width, grow to fit".
    /// The width is re-set on every layout of the parent, so it follows a
    /// resized window and a scaled display alike without any arithmetic
    /// here - which is the whole point of not computing the flow up front.
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
    /// One citation: a bold heading, the reference itself in the quieter
    /// colour, and where it can be reached if it is online.
    ///
    /// The URL is its own clickable line rather than being buried in the
    /// prose, because a reader who wants to check a number wants to find
    /// the link without reading the sentence - and because a LinkLabel
    /// inside a wrapped paragraph would have to be positioned by hand,
    /// which is the arithmetic this form exists to avoid.
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
            CrashReporter.LogHandled(ex, "opening a source link from Ptolemy's cosmos");
            MessageBox.Show(this,
                "Couldn't open that link: " + ex.Message,
                "Ptolemy's Cosmos",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        var path = Path.Combine(folder, "Almagest.html");

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PageResource)
            ?? throw new InvalidOperationException(
                "The Almagest page is missing from this build.");

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
            CrashReporter.LogHandled(ex, "opening Ptolemy's cosmos");
            MessageBox.Show(this,
                "Couldn't open the planetarium: " + ex.Message,
                "Ptolemy's Cosmos",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
