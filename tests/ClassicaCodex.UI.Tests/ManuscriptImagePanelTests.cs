using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// How a line of a manuscript is sized on screen.
///
/// <b>This exists because the obvious thing is the wrong thing.</b> A CATMuS
/// photograph is one line cropped from a page: about 4,772 pixels wide and
/// 170 tall, a strip 28 times wider than it is tall. Every other image in
/// this application is sensibly shaped and scaled to fit its panel, and doing
/// that here produces a 25-pixel smudge in which no letter is visible - which
/// looks like a working feature and is useless.
///
/// So the default fits the HEIGHT and scrolls sideways, and the numbers below
/// are what says so.
/// </summary>

public class ManuscriptImagePanelTests
{
    /// <summary>The real proportions, from the Liege charter shard.</summary>
    private const int LineWidth = 4772;
    private const int LineHeight = 170;

    private static void WithPanel(Action<ManuscriptImagePanel, Image> body) =>
        StaHarness.Run(host =>
        {
            var panel = new ManuscriptImagePanel { Bounds = new Rectangle(0, 0, 700, 200) };
            host.Controls.Add(panel);
            _ = panel.Handle;

            using var image = new Bitmap(LineWidth, LineHeight);
            panel.Show((Image)image.Clone(), null);

            body(panel, image);
            return Task.CompletedTask;
        });

    /// <summary>
    /// Fitting the height fills the panel and leaves the line scrollable -
    /// which is how a line is actually read.
    /// </summary>
    [Fact]
    public void FittingTheHeightMakesTheLineReadableAndScrollable()
    {
        WithPanel((panel, _) =>
        {
            panel.Zoom = ManuscriptImageZoom.FitHeight;

            Assert.Equal(panel.ClientSize.Height, panel.AutoScrollMinSize.Height);

            Assert.True(panel.AutoScrollMinSize.Width > panel.ClientSize.Width * 3,
                $"At the panel's height the line is {panel.AutoScrollMinSize.Width} wide against a " +
                $"{panel.ClientSize.Width}-wide panel. If that stops being several screens the aspect " +
                "ratio assumption behind this whole panel has changed.");
        });
    }

    /// <summary>
    /// Fitting the width is the other question - how long is this line - and
    /// it is deliberately not the default, because at a panel's width the
    /// writing is a few pixels tall.
    /// </summary>
    [Fact]
    public void FittingTheWidthShowsTheWholeLineAndIsTiny()
    {
        WithPanel((panel, _) =>
        {
            panel.Zoom = ManuscriptImageZoom.FitWidth;

            Assert.Equal(panel.ClientSize.Width, panel.AutoScrollMinSize.Width);
            Assert.True(panel.AutoScrollMinSize.Height < 40,
                "Fitting the width is expected to be unreadably small - that is why it is not the default.");
        });
    }

    /// <summary>
    /// Fitting the whole image is what a leaf wants - a page from the holding
    /// library is 2,857 by 4,096, taller than it is wide, and the point of
    /// looking at one is seeing the page entire.
    /// </summary>
    [Fact]
    public void FittingTheWholeImageFitsAPageShapedLeaf()
    {
        StaHarness.Run(host =>
        {
            var panel = new ManuscriptImagePanel { Bounds = new Rectangle(0, 0, 700, 500) };
            host.Controls.Add(panel);
            _ = panel.Handle;

            using var leaf = new Bitmap(2857, 4096);
            panel.Show((Image)leaf.Clone(), null);
            panel.Zoom = ManuscriptImageZoom.FitAll;

            // Inside the panel both ways, and touching one edge - anything
            // smaller is not fitting, anything larger needs a scrollbar.
            Assert.True(panel.AutoScrollMinSize.Width <= panel.ClientSize.Width);
            Assert.True(panel.AutoScrollMinSize.Height <= panel.ClientSize.Height);
            Assert.Equal(panel.ClientSize.Height, panel.AutoScrollMinSize.Height);

            // Proportions kept: a squashed leaf is worse than a small one.
            var ratio = (double)panel.AutoScrollMinSize.Width / panel.AutoScrollMinSize.Height;
            Assert.Equal(2857.0 / 4096.0, ratio, 2);

            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(ManuscriptImageZoom.Half, LineWidth / 2)]
    [InlineData(ManuscriptImageZoom.Actual, LineWidth)]
    [InlineData(ManuscriptImageZoom.Double, LineWidth * 2)]
    public void TheFixedZoomsAreTheirOwnMultiples(ManuscriptImageZoom zoom, int expectedWidth)
    {
        WithPanel((panel, _) =>
        {
            panel.Zoom = zoom;
            Assert.Equal(expectedWidth, panel.AutoScrollMinSize.Width);
        });
    }

    /// <summary>
    /// Resizing has to recompute the fitted size, or the scrollable area
    /// stays at the size the panel used to be and the line is cropped or
    /// floats in space.
    /// </summary>
    [Fact]
    public void ResizingRecomputesAFittedLine()
    {
        WithPanel((panel, _) =>
        {
            panel.Zoom = ManuscriptImageZoom.FitHeight;
            var before = panel.AutoScrollMinSize.Width;

            panel.Height *= 2;

            Assert.True(panel.AutoScrollMinSize.Width > before,
                "A taller panel shows the line larger, so its scrollable width has to grow with it.");
        });
    }

    /// <summary>
    /// With no image there is nothing to scroll, and a message in its place -
    /// which is the normal state, since most manuscripts will have their
    /// transcriptions and not their photographs.
    /// </summary>
    [Fact]
    public void WithNoImageThereIsNothingToScroll()
    {
        StaHarness.Run(host =>
        {
            var panel = new ManuscriptImagePanel { Bounds = new Rectangle(0, 0, 700, 200) };
            host.Controls.Add(panel);
            _ = panel.Handle;

            panel.Show(null, "The photograph of this line has not been downloaded.");

            Assert.Equal(Size.Empty, panel.AutoScrollMinSize);
            return Task.CompletedTask;
        });
    }
}
