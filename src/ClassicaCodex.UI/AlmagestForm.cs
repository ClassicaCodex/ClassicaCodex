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

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16, 14, 16, 14)
        };

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

        AddHeading(flow, "Where it comes from", 11F, FontStyle.Bold);
        AddMuted(flow,
            "Parameters from the Almagest, Books III and IX to XI, in Toomer's translation, "
            + "cross-checked against Neugebauer, Pedersen, Duke and van Gent. The comparison "
            + "with the real sky uses JPL's approximate planetary elements on the long-interval "
            + "fit, with the Espenak-Meeus delta-T polynomials. The engine reproduces Ptolemy's "
            + "own worked example for Mars - Toomer's Appendix A, Example 14 - to within one "
            + "second of arc.");

        AddMuted(flow,
            "Cicero describes exactly this distinction in De re publica 1.22: between a solid "
            + "star-globe of the old kind and a sphere with the motions of the Sun, the Moon and "
            + "the five wandering stars in it. Both passages are in this library.");

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
