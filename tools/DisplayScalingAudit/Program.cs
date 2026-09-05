using System.Text;
using ClassicaCodex.Data;
using ClassicaCodex.UI;

namespace ClassicaCodex.Tools.DisplayScalingAudit;

/// <summary>
/// Opens every window this application can open without arguments and asks one
/// question of every caption in them: does the box it sits in have room for it.
///
/// <b>Why this exists.</b> Until 3.6.2 the DPI scaling did nothing at all - the
/// scale factor was always exactly 1, so at 125% the text grew a quarter and no
/// window grew with it. That survived six releases for one reason: at 100% a
/// broken scale factor and a correct one are the same number. Nothing in the
/// test suite could see it, and nothing could, because reproducing it needs a
/// real display scaling change; setting a larger font by hand does not do it,
/// as AutoScaleMode reads the scale when a form is built and lays out from
/// there.
///
/// So this is the only check that can catch a regression here, and a regression
/// would otherwise be invisible to anyone developing at 100%.
///
/// <b>How to run it.</b> Set the display scaling in Settings - System -
/// Display - Scale, then run this once at each setting you care about. The
/// report is named for the scaling it detects, so it is the same command every
/// time:
///
/// <code>
/// dotnet run --project tools/DisplayScalingAudit -c Release
/// </code>
///
/// <b>How to read it.</b> The count is not the signal. This measures more
/// strictly than WinForms draws - TextRenderer includes leading a Label does
/// not need - so it reports several captions at every scaling that render
/// perfectly. <b>The 100% report is the baseline and what matters is what is
/// new above it</b>, which is exactly the failure this is looking for: a
/// caption with room at 100% and nowhere else. Anything new is photographed
/// beside the report so it can be judged by eye rather than by arithmetic.
///
/// A healthy result is the same findings at every scaling, and form sizes that
/// scale exactly with it: 420x480 at 100%, 525x600 at 125%, 630x720 at 150%.
/// </summary>
internal static class Program
{
    private static readonly List<string> Report = new();
    private static string _outputDirectory = ".";
    private static string _prefix = "100";

    /// <param name="args">
    /// Optionally where to write the report and photographs. Defaults to a
    /// "display-scaling" folder under the current directory.
    /// </param>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        _outputDirectory = args.Length > 0
            ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "display-scaling");

        // The library this application would open on its own. Most windows do
        // not need one, and the few that do are reported as unopenable rather
        // than stopping the run - a scaling audit is still worth having on a
        // machine that has never been set up.
        try { DbConnectionFactory.TryConfigureFromPreferred(); }
        catch (Exception ex) { Report.Add($"(no library configured: {ex.GetType().Name})"); }

        var host = new Form
        {
            Size = new Size(1, 1),
            Location = new Point(-4000, -4000),
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false
        };

        host.Shown += (_, _) =>
        {
            try { Run(host); }
            catch (Exception ex) { Report.Add($"audit error: {ex}"); }
            Write();
            host.Close();
        };

        Application.Run(host);
    }

    private static void Write()
    {
        Directory.CreateDirectory(_outputDirectory);
        var path = Path.Combine(_outputDirectory, $"report-{_prefix}.txt");
        File.WriteAllLines(path, Report, new UTF8Encoding(false));

        // The only thing a console-less WinForms process can say to a shell.
        Console.WriteLine(path);
    }

    private static void Run(Form host)
    {
        var dpi = host.DeviceDpi;
        _prefix = (dpi * 100 / 96).ToString();

        Report.Add($"device DPI          {dpi}  ({dpi * 100 / 96}% scaling)");
        Report.Add($"Control.DefaultFont {Control.DefaultFont.Name} {Control.DefaultFont.SizeInPoints:0.##}pt "
                   + $"(height {Control.DefaultFont.Height}px)");
        Report.Add(new string('-', 78));

        var forms = typeof(MainForm).Assembly.GetTypes()
            .Where(t => typeof(Form).IsAssignableFrom(t) && !t.IsAbstract
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        int opened = 0, failed = 0, problems = 0;
        var scaleReported = false;

        foreach (var type in forms)
        {
            Form? form = null;
            try
            {
                form = (Form)Activator.CreateInstance(type)!;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                opened++;

                // What WinForms itself believes the scale is, once. Declared
                // and current being equal after a window has been shown is
                // normal - the factor is recorded as applied - so the proof
                // that scaling happened is the form sizes, not these two.
                if (!scaleReported && form is ScaledForm)
                {
                    scaleReported = true;
                    Report.Add($"scaling             mode {form.AutoScaleMode}, "
                               + $"declared {form.AutoScaleDimensions}, current {form.CurrentAutoScaleDimensions}");
                    Report.Add(new string('-', 78));
                }

                problems += Inspect(form, type.Name);
            }
            catch (Exception ex)
            {
                failed++;
                Report.Add($"{type.Name}: could not open - {ex.GetType().Name}: {Short(ex.Message)}");
            }
            finally
            {
                try { form?.Close(); form?.Dispose(); }
                catch { /* the window is going away either way */ }
                Application.DoEvents();
            }
        }

        Report.Add(new string('-', 78));
        Report.Add($"{opened} windows opened, {failed} could not be, {problems} captions without room");
    }

    /// <summary>
    /// Reports every caption in one window that needs more room than it has,
    /// and photographs the window if any did.
    /// </summary>
    private static int Inspect(Form form, string name)
    {
        var problems = new List<string>();
        Walk(form, problems);

        if (problems.Count == 0) return 0;

        Report.Add($"{name}  ({form.ClientSize.Width}x{form.ClientSize.Height})");
        foreach (var problem in problems) Report.Add("    " + problem);

        // A measurement saying a caption is short is a claim, not a finding.
        // The picture is what says whether the words are actually cut off, and
        // several of these are always measurement noise.
        try
        {
            var shots = Path.Combine(_outputDirectory, "shots");
            Directory.CreateDirectory(shots);
            using var shot = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
            form.DrawToBitmap(shot, new Rectangle(0, 0, shot.Width, shot.Height));
            shot.Save(Path.Combine(shots, $"{_prefix}-{name}.png"));
        }
        catch (Exception ex)
        {
            Report.Add($"    (could not photograph: {ex.GetType().Name})");
        }

        return problems.Count;
    }

    private static void Walk(Control control, List<string> problems)
    {
        foreach (Control child in control.Controls)
        {
            Check(child, problems);
            Walk(child, problems);
        }
    }

    private static void Check(Control control, List<string> problems)
    {
        // Nothing to do with scaling, but this walk visits every caption in the
        // application and it is the only thing that does. A Label treats & as a
        // keyboard shortcut marker, so prose containing one loses it: "Data
        // Sources & Licensing" draws as "Data Sources  Licensing".
        if (control is Label or GroupBox && control.Text.Contains('&')
            && !control.Text.Contains("&&") && UsesMnemonic(control))
        {
            problems.Add($"{control.GetType().Name} \"{Short(control.Text)}\" loses its & to a mnemonic");
        }

        // Only things that draw a caption in a box of their own. A text box or
        // a list scrolls by design, and a container's whole job is to be a box.
        if (control is not (Label or Button or CheckBox or RadioButton or GroupBox)) return;
        if (string.IsNullOrWhiteSpace(control.Text)) return;

        // AutoSize grows the control to its text, so it cannot clip and
        // measuring it only reports the layout's own arithmetic back.
        if (control is Label { AutoSize: true } or Button { AutoSize: true }
            or CheckBox { AutoSize: true } or RadioButton { AutoSize: true }) return;
        if (control.Width <= 0 || control.Height <= 0) return;

        var wraps = control is Label or CheckBox or RadioButton;
        var flags = wraps ? TextFormatFlags.WordBreak : TextFormatFlags.Default;

        // A checkbox or radio spends part of its width on its glyph, and a
        // group box on its border - measured against what is left for words.
        var available = control.Width - control switch
        {
            CheckBox or RadioButton => 20,
            GroupBox => 12,
            Button => 8,
            _ => 2
        };
        if (available <= 10) return;

        var needed = TextRenderer.MeasureText(control.Text, control.Font,
            new Size(available, int.MaxValue), flags | TextFormatFlags.NoPadding);

        var room = control is GroupBox ? control.Height - 18 : control.Height;
        if (needed.Height <= room && (wraps || needed.Width <= available)) return;

        var what = needed.Height > room
            ? $"needs {needed.Height}px of height in {room}px"
            : $"needs {needed.Width}px of width in {available}px";

        problems.Add($"{control.GetType().Name} \"{Short(control.Text)}\" {what}"
                     + (control.Visible ? string.Empty : "  [currently hidden]"));
    }

    // LinkLabel derives from Label, so it has to be asked first.
    private static bool UsesMnemonic(Control control) => control switch
    {
        LinkLabel link => link.UseMnemonic,
        Label label => label.UseMnemonic,
        _ => true
    };

    private static string Short(string text)
    {
        var single = text.Replace(Environment.NewLine, " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= 54 ? single : single[..54] + "…";
    }
}
