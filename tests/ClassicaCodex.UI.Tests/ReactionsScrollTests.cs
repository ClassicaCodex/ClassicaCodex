using System.Text.RegularExpressions;
using ClassicaCodex.Core.Reactions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The debate has to still have words in it after you scroll.
///
/// <b>What went wrong.</b> The canvas applied its scroll offset with
/// Graphics.TranslateTransform. Every shape it draws is GDI+ - rounded
/// rectangles, the avatar, the chip - and moved correctly. Every word it draws
/// goes through TextRenderer, which is GDI, and GDI does not know the GDI+
/// world transform exists: all of it kept being drawn at the unscrolled
/// coordinates, which for anything below the first screen means off the
/// control entirely.
///
/// So a reader scrolling the Medea debate saw the bubbles slide up empty, a
/// speaker's name cut in half across the bubble above, and "read 230" floating
/// in space outside its chip. It shipped, and it shipped past a careful
/// author, four rounds of looking at screenshots and a run of every window in
/// the application - because a screenshot of a scrolling list is always taken
/// at the top, and at the top the scroll offset is zero and the two coordinate
/// systems agree exactly.
///
/// Both tests below fail against the old code. The first is the real one: it
/// paints the control at the bottom of a long debate and counts ink. The
/// second is a lint, because this is a mistake anybody would make again.
/// </summary>
public class ReactionsScrollTests
{
    /// <summary>
    /// Enough turns that the content is several times the height of the
    /// control, so scrolling to the end puts every block that was visible at
    /// the top well outside it.
    /// </summary>
    private static IReadOnlyList<TurnView> LongDebate()
    {
        var critic = new AncientCritic(
            "speaker", "Demeas", CriticKind.Composite, "Classical Athens",
            -458, -412, "Acharnae", "charcoal-burner",
            "A farmer from the deme worst hit by the raids.", "Plain speaking.",
            "skin:tan;hair:grey;beard:full;cloth:#7b5230");

        var turns = new List<TurnView>();

        for (var i = 1; i <= 12; i++)
        {
            turns.Add(new TurnView(
                new DebateTurn(i, critic.Id,
                    "Third place is too kind, and I say that as a man who laughed. "
                    + "But he is right about the talkers in the agora, and he is right "
                    + "in the way that costs money. They teach for a fee, and what they "
                    + "teach is how to argue your way out of paying it."),
                critic,
                ShowSpeaker: true,
                DividerBefore: null,
                PassageLabel: null,
                PassageResolved: false,
                SourceLabel: null,
                SourceResolved: false));
        }

        return turns;
    }

    /// <summary>
    /// The property that defines a correctly scrolling canvas: scrolling by N
    /// pixels moves everything on it up by exactly N pixels.
    ///
    /// Counts the pixels where the scrolled image disagrees with the
    /// unscrolled one shifted by N, over a region clear of the scrollbar and
    /// the edges. Everything drawn here is at integer coordinates, so a
    /// correct canvas matches almost exactly; the few disagreements come from
    /// content entering at the bottom edge.
    ///
    /// This replaced an earlier test that counted dark pixels before and
    /// after scrolling and asserted there were still plenty. That test passed
    /// against the bug, because the text of the first three turns is drawn at
    /// coordinates that stay on screen at every scroll position - it is simply
    /// drawn on top of the wrong bubbles. Counting ink cannot tell "the words
    /// are in the right place" from "the words are in the same place"; this
    /// can.
    /// </summary>
    private static (int Mismatched, int Compared) DisagreementAfterShift(
        Bitmap unscrolled, Bitmap scrolled, int shift, Rectangle region)
    {
        int mismatched = 0, compared = 0;

        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++)
        {
            if (y + shift >= unscrolled.Height) continue;

            var before = unscrolled.GetPixel(x, y + shift);
            var after = scrolled.GetPixel(x, y);
            compared++;

            if (Math.Abs(before.R - after.R) > 24
                || Math.Abs(before.G - after.G) > 24
                || Math.Abs(before.B - after.B) > 24) mismatched++;
        }

        return (mismatched, compared);
    }

    [Fact]
    public void TheWordsAreStillThereAfterScrolling()
    {
        int scrolledBy = 0, contentHeight = 0, mismatched = 0, compared = 0, moved = 0;

        StaHarness.Run(host =>
        {
            // Shown, not merely handle-created. A scrollable control in an
            // unshown parent never lays out its scrollbars, so
            // AutoScrollPosition silently refuses to move and the test passes
            // by measuring nothing - which is exactly what it did first time.
            host.ClientSize = new Size(760, 460);
            host.Show();

            using var canvas = new ReactionsCanvas
            {
                Left = 0,
                Top = 0,
                Width = 700,
                Height = 400
            };

            host.Controls.Add(canvas);
            canvas.CreateControl();
            _ = canvas.Handle;
            Application.DoEvents();

            canvas.SetTurns(LongDebate());
            Application.DoEvents();

            contentHeight = canvas.AutoScrollMinSize.Height;

            using var unscrolled = new Bitmap(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(unscrolled, new Rectangle(0, 0, canvas.Width, canvas.Height));

            // A modest scroll, well inside the content, so that almost all of
            // what was on screen is still on screen - just N pixels higher.
            canvas.AutoScrollPosition = new Point(0, 120);
            scrolledBy = -canvas.AutoScrollPosition.Y;
            canvas.Invalidate();
            Application.DoEvents();

            using var scrolled = new Bitmap(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(scrolled, new Rectangle(0, 0, canvas.Width, canvas.Height));

            // Clear of the scrollbar on the right and of the bottom edge,
            // where content legitimately arrives that was not there before.
            var region = new Rectangle(8, 8, canvas.Width - 40, canvas.Height - scrolledBy - 16);

            (mismatched, compared) = DisagreementAfterShift(unscrolled, scrolled, scrolledBy, region);

            // And a control: the two images must differ from each other, or
            // the canvas did not repaint and everything above is vacuous.
            (moved, _) = DisagreementAfterShift(unscrolled, scrolled, 0, region);

            return Task.CompletedTask;
        });

        Assert.True(contentHeight > 800,
            $"the debate laid out to {contentHeight}px, which is not taller than the 400px canvas, "
            + "so there would be nothing to scroll");

        Assert.True(scrolledBy > 100,
            $"the canvas only scrolled {scrolledBy}px of {contentHeight}px of content, "
            + "so this test is not testing scrolling");

        Assert.True(compared > 100_000, $"only {compared} pixels were compared");

        Assert.True(moved > compared / 20,
            $"the canvas looks identical before and after scrolling {scrolledBy}px "
            + $"({moved} of {compared} pixels differ), so it did not repaint and this test proves nothing");

        // The real assertion. Scrolling by N moves the whole picture up by N.
        // Against the transform bug this is roughly a third of the region:
        // every bubble moves and every word stays where it was.
        var wrong = 100.0 * mismatched / compared;

        Assert.True(wrong < 2.0,
            $"after scrolling {scrolledBy}px, {wrong:F1}% of the canvas is not what it was "
            + $"{scrolledBy}px lower ({mismatched} of {compared} pixels). Part of the window is "
            + "not moving with the scroll - see this test's summary for the transform that does it.");
    }

    /// <summary>
    /// A lint, in the manner of PassageRowLengthTests: the bug is one line of
    /// code away from returning, the returned version looks completely
    /// reasonable, and the test above only catches it if somebody remembers
    /// to keep the canvas scrollable.
    /// </summary>
    [Fact]
    public void TheCanvasNeverOffsetsItselfWithATransform()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "ReactionsCanvas.cs"));

        // Matched only where it is a call. The word appears in this file's own
        // explanation of why it is not used, and an explanation is the thing
        // worth keeping.
        var calls = Regex.Matches(source, @"\.(TranslateTransform|ScaleTransform|RotateTransform)\s*\(");

        Assert.True(calls.Count == 0,
            "ReactionsCanvas applies a transform to its Graphics. TextRenderer is GDI and ignores "
            + "the GDI+ world transform, so every word in this window will be drawn at unscrolled "
            + "coordinates and vanish the moment a reader scrolls. Offset the rectangles instead - "
            + "see At() - or draw the text with Graphics.DrawString, which respects it.");
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
