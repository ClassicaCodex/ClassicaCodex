using System.Drawing.Drawing2D;
using System.Reflection;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Reactions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The Fictional Ancient Reactions window in the states nobody photographed.
///
/// Every test here corresponds to a defect found by reviewing the window for
/// states it had never been put into, after the scroll bug proved that a
/// screenshot at the default size is not evidence about any other size. None
/// of them would have been caught by looking at the window as it opens.
/// </summary>
/// <summary>
/// Points the whole test class at a throwaway database before any window in
/// it is constructed.
///
/// <b>This is load-bearing, not tidiness.</b> ReactionsForm resolves its
/// passage links on Load. With no database configured, the repository would
/// fall back to the default location - which on a developer's machine is a
/// real library of several gigabytes - and a query against a database with no
/// schema throws, which this window catches and hands to CrashReporter, which
/// appends to the real installation's errors.log. So a layout test would be
/// reading somebody's library and writing to their error log.
///
/// remember:false is the part that makes it safe: the saved database-path
/// preference is never written, exactly as in tools/FreshInstallCheck.
/// </summary>
public sealed class EmptyLibraryFixture : IDisposable
{
    public EmptyLibraryFixture()
    {
        Folder = Path.Combine(Path.GetTempPath(), "ccx-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);

        ClassicaCodex.Data.DbConnectionFactory.Configure(
            Path.Combine(Folder, "test.db"), remember: false);

        ClassicaCodex.Data.SchemaInitializer.EnsureSchemaAsync().GetAwaiter().GetResult();
    }

    public string Folder { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not worth failing over.
        }
    }
}

public class ReactionsLayoutTests : IClassFixture<EmptyLibraryFixture>
{
    private static Work SampleWork() => new()
    {
        WorkId = 1,
        Title = "Clouds",
        CtsUrn = "urn:cts:greekLit:tlg0019.tlg003"
    };

    private static IReadOnlyList<ReactionDebate> SampleDebates() =>
        ReactionLibrary.DebatesFor("Aristophanes", "Clouds");

    /// <summary>
    /// The two footer buttons must be clickable at every width the window can
    /// be dragged to.
    ///
    /// The status label is added to Controls before them, and index 0 is the
    /// FRONT of the WinForms z-order - not the back, which is the way round
    /// most people remember it. With a fixed 560-pixel box anchored only on
    /// the left, the buttons travelled left into it as the window narrowed and
    /// the label took their clicks: "What am I reading?" died at about 750px
    /// wide and Close at about 620px, both while still looking completely
    /// normal and both well above the 560px minimum size.
    /// </summary>
    [Fact]
    public void TheFooterButtonsStayClickableAtEveryWidth()
    {
        var dead = new List<string>();

        StaHarness.Run(_ =>
        {
            using var form = new ReactionsForm(SampleWork(), "Aristophanes", SampleDebates());
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            var buttons = form.Controls.OfType<Button>().ToList();
            Assert.Equal(2, buttons.Count);

            // Every width from the design size down to the minimum, in steps
            // small enough not to step over the range where it broke.
            for (var width = 900; width >= form.MinimumSize.Width; width -= 10)
            {
                form.ClientSize = new Size(width, form.ClientSize.Height);
                Application.DoEvents();

                foreach (var button in buttons)
                {
                    if (button.Left < 0 || button.Right > form.ClientSize.Width) continue;

                    var middle = new Point(
                        button.Left + button.Width / 2,
                        button.Top + button.Height / 2);

                    if (form.GetChildAtPoint(middle) != button)
                        dead.Add($"{button.Text} at client width {width}");
                }
            }

            return Task.CompletedTask;
        });

        Assert.True(dead.Count == 0,
            "these buttons are covered by another control and will not respond to a click:\n  "
            + string.Join("\n  ", dead.Take(12)));
    }

    /// <summary>
    /// The banner is the sentence that says the speakers are invented. It may
    /// never be cut off, at any width the window can be dragged to.
    ///
    /// It was a fixed 52-pixel panel holding a 40-pixel label, which fits
    /// three lines at the design width and two at about 700px - so narrowing
    /// the window silently truncated the disclaimer mid-clause. Of everything
    /// in this window, that is the text that most has to survive.
    /// </summary>
    [Fact]
    public void TheDisclaimerIsNeverClipped()
    {
        var clipped = new List<string>();

        StaHarness.Run(_ =>
        {
            using var form = new ReactionsForm(SampleWork(), "Aristophanes", SampleDebates());
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            var banner = form.Controls.OfType<Panel>().First();
            var text = banner.Controls.OfType<Label>().First();

            for (var width = 900; width >= form.MinimumSize.Width; width -= 20)
            {
                form.ClientSize = new Size(width, form.ClientSize.Height);
                Application.DoEvents();

                var needed = TextRenderer.MeasureText(
                    text.Text, text.Font, new Size(text.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

                if (needed > text.Height)
                    clipped.Add($"at client width {width}: needs {needed}px, has {text.Height}px");

                if (text.Bottom > banner.Height)
                    clipped.Add($"at client width {width}: text runs {text.Bottom - banner.Height}px "
                                + "past the bottom of the banner");
            }

            return Task.CompletedTask;
        });

        Assert.True(clipped.Count == 0,
            "the \"these conversations were written for this window\" disclaimer is being cut off:\n  "
            + string.Join("\n  ", clipped.Take(10)));
    }

    /// <summary>
    /// Nothing in the header may overlap the transcript, and the transcript
    /// may not fall off the bottom of the window.
    /// </summary>
    [Fact]
    public void TheHeaderAndTheTranscriptNeverOverlap()
    {
        var overlaps = new List<string>();

        StaHarness.Run(_ =>
        {
            using var form = new ReactionsForm(SampleWork(), "Aristophanes", SampleDebates());
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            var canvas = form.Controls.OfType<ReactionsCanvas>().Single();
            var note = form.Controls.OfType<Label>().OrderBy(l => l.Top).Last(l => l.Top < canvas.Top + 400);

            for (var width = 900; width >= form.MinimumSize.Width; width -= 20)
            {
                form.ClientSize = new Size(width, 520);
                Application.DoEvents();

                if (canvas.Top < note.Bottom)
                    overlaps.Add($"at width {width} the transcript starts {note.Bottom - canvas.Top}px "
                                 + "above the end of the setting note");

                if (canvas.Height < 60)
                    overlaps.Add($"at width {width} the transcript is only {canvas.Height}px tall");

                if (canvas.Bottom > form.ClientSize.Height)
                    overlaps.Add($"at width {width} the transcript runs off the bottom");
            }

            return Task.CompletedTask;
        });

        Assert.True(overlaps.Count == 0, string.Join("\n  ", overlaps.Take(10)));
    }

    /// <summary>
    /// Blank space inside a bubble is not a link.
    ///
    /// A source line is laid out across the full text column, because that is
    /// the width it wraps at, but it paints only as far as its words go -
    /// "Source: Cicero, Orator 30 - open it" is about 170 pixels inside a
    /// 596-pixel rectangle. Hit testing used the layout rectangle, so the
    /// remaining 420 pixels of empty bubble showed a hand cursor and, on a
    /// click, closed this window and sent the reader to Cicero. There is no
    /// way back from that except to reopen the debate.
    /// </summary>
    [Fact]
    public void EmptySpaceInABubbleIsNotAClickableLink()
    {
        var cursorFarRight = Cursors.Default;
        var cursorOnTheText = Cursors.Default;

        StaHarness.Run(host =>
        {
            host.ClientSize = new Size(800, 500);
            host.Show();

            var critic = new AncientCritic(
                "cicero", "Cicero", CriticKind.Historical, "Late Roman Republic",
                -80, -43, "Rome", "advocate", "A sketch.", "Tastes.", "cloth:#8c3b3b");

            using var canvas = new ReactionsCanvas { Left = 0, Top = 0, Width = 760, Height = 460 };
            host.Controls.Add(canvas);
            canvas.CreateControl();
            _ = canvas.Handle;

            canvas.SetTurns(new[]
            {
                new TurnView(
                    new DebateTurn(1, "cicero", "A turn long enough to wrap onto two lines of the "
                                                + "bubble so that the source line below it is not the "
                                                + "widest thing in the block."),
                    critic, ShowSpeaker: true, DividerBefore: null,
                    PassageLabel: null, PassageResolved: false,
                    SourceLabel: "Source: Cicero, Orator 30  - open it", SourceResolved: true)
            });

            Application.DoEvents();

            var move = typeof(ReactionsCanvas).GetMethod(
                "OnMouseMove", BindingFlags.NonPublic | BindingFlags.Instance)!;

            // Find the source row rather than computing it: run up the left
            // edge of the text column from the bottom of the block until the
            // cursor says "link". The name row is a link too, but it is at the
            // top, so coming from below finds the source line first.
            var sourceRow = -1;

            for (var y = canvas.AutoScrollMinSize.Height - 2; y > 0 && sourceRow < 0; y--)
            {
                move.Invoke(canvas, new object[]
                {
                    new MouseEventArgs(MouseButtons.None, 0, 90, y, 0)
                });

                if (canvas.Cursor == Cursors.Hand) sourceRow = y;
            }

            if (sourceRow < 0) return Task.CompletedTask;

            move.Invoke(canvas, new object[]
            {
                new MouseEventArgs(MouseButtons.None, 0, 90, sourceRow, 0)
            });
            cursorOnTheText = canvas.Cursor;

            // The same row, far to the right of where the italic stops.
            move.Invoke(canvas, new object[]
            {
                new MouseEventArgs(MouseButtons.None, 0, 560, sourceRow, 0)
            });
            cursorFarRight = canvas.Cursor;

            return Task.CompletedTask;
        });

        // The control: the words themselves really are a link, or this test
        // would pass against a canvas with no links at all.
        Assert.True(cursorOnTheText == Cursors.Hand,
            "the source line itself did not report as a link, so this test is not measuring "
            + "the right row");

        Assert.True(cursorFarRight == Cursors.Default,
            "empty bubble space 400px to the right of the source text reports as a clickable "
            + "link. Clicking there closes the window and navigates away - see this test's summary.");
    }

    /// <summary>
    /// A pill is a pill at every size.
    ///
    /// Rounded() fell back to a square-cornered rectangle when the height was
    /// exactly twice the radius, and all three pill shapes - the passage chip,
    /// the century divider, the "real person" badge - pass their own half
    /// height as the radius. So whether they were drawn as lozenges or as
    /// boxes came down to the parity of a measured line of text: odd at 96
    /// DPI, which is every screenshot ever taken, and even at 192.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(38)]
    [InlineData(40)]
    [InlineData(44)]
    [InlineData(46)]
    public void APillIsRoundAtEveryHeight(int height)
    {
        var rounded = typeof(ReactionsCanvas).GetMethod(
            "Rounded", BindingFlags.NonPublic | BindingFlags.Static)!;

        using var path = (GraphicsPath)rounded.Invoke(
            null, new object[] { new Rectangle(0, 0, 160, height), height / 2 })!;

        // A rectangle is four points; a stadium built from four arcs is many.
        Assert.True(path.PointCount > 8,
            $"a {height}px pill came back as a {path.PointCount}-point path - that is a rectangle. "
            + "See Rounded(): the height comparison has to be strict, or an even height is sent "
            + "to the square-cornered fallback.");
    }

    /// <summary>
    /// Every distance inside the canvas has to be a scaled distance.
    ///
    /// ScaledForm scales the control's bounds and its font and stops there, so
    /// a constant written into a paint method stays a raw device pixel: at
    /// 150% the text grew by half and the portrait, the gutters and the
    /// widest a bubble may be did not. This is a lint because the symptom -
    /// a cramped, wrongly proportioned window - only appears on a machine set
    /// to something other than 100%, which is not the machine it was written
    /// on.
    /// </summary>
    [Fact]
    public void TheCanvasScalesItsOwnDistances()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "ReactionsCanvas.cs"));

        var body = source[source.IndexOf("private void Rebuild()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("protected override void OnHandleCreated", StringComparison.Ordinal)];

        var raw = new[] { "AvatarSize", "Gutter", "Inset", "BlockGap", "TuckedGap", "MaxBubbleWidth" }
            .Where(name => System.Text.RegularExpressions.Regex.IsMatch(
                body, $@"(?<!Scale\()\b{name}\b"))
            .ToList();

        Assert.True(raw.Count == 0,
            "these design constants are used raw inside Rebuild rather than through Scale(): "
            + string.Join(", ", raw)
            + ". At 150% they stay the size they were at 100% while the text around them grows.");
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
