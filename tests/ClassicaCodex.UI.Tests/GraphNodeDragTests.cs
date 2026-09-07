using System.Drawing;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Dragging a tag around the Myth Network canvas.
///
/// The node is held inside the canvas by clamping its centre between its own
/// radius and the far edge less that radius. That is right until the canvas
/// is narrower than the node is wide, at which point the low bound passes the
/// high one - and Math.Clamp throws rather than picking one. A heavily used
/// tag draws at 30px, so a canvas under about 60px was enough, and the Myth
/// Network window sets no minimum size. Narrow it far enough, drag a tag, and
/// the application put up a crash dialog.
/// </summary>
public class GraphNodeDragTests
{
    [Theory]
    [InlineData(50, 400)]    // narrower than one node
    [InlineData(400, 50)]    // shorter than one node
    [InlineData(30, 30)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]       // mid-resize, before a layout pass
    public void ACanvasTooSmallForTheNodeDoesNotThrow(int width, int height)
    {
        var placed = GraphCanvas.KeepInside(new PointF(120, 120), radius: 30f, width, height);

        Assert.True(float.IsFinite(placed.X));
        Assert.True(float.IsFinite(placed.Y));
    }

    [Fact]
    public void ANodeIsHeldInsideACanvasWithRoomForIt()
    {
        var pushedOffTheLeft = GraphCanvas.KeepInside(new PointF(-500, 200), 30f, 800, 600);
        var pushedOffTheRight = GraphCanvas.KeepInside(new PointF(5000, 200), 30f, 800, 600);

        Assert.Equal(30f, pushedOffTheLeft.X);
        Assert.Equal(770f, pushedOffTheRight.X);
    }

    [Fact]
    public void ANodeLeftWhereItIsWhenItAlreadyFits()
    {
        var placed = GraphCanvas.KeepInside(new PointF(400, 300), 30f, 800, 600);

        Assert.Equal(400f, placed.X);
        Assert.Equal(300f, placed.Y);
    }

    /// <summary>
    /// With no room to hold it properly, the node sits in the middle of what
    /// there is - visibly wrong, but the window is too small to see anyway,
    /// and it comes back to itself the moment the window is widened.
    /// </summary>
    [Fact]
    public void WithNoRoomTheNodeSitsInTheMiddle()
    {
        var placed = GraphCanvas.KeepInside(new PointF(999, 999), 30f, 50, 40);

        Assert.Equal(25f, placed.X);
        Assert.Equal(20f, placed.Y);
    }
}
