using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// What a reader pane must still be true of while two things are happening to
/// it at once.
///
/// A fill is asynchronous - the passages are cut into rows on a worker thread,
/// which takes seconds on a long prose work - and so is the re-cut that a
/// change of width or font queues behind it. Both end by replacing every row
/// in the control. Which one lands last therefore decides what the reader is
/// looking at, and nothing in the pane arbitrates between them.
///
/// EVERY TEST IN THIS FILE FAILS AS OF 3.7.0. They are written against what
/// the pane claims rather than what it does; see the remarks on each.
/// </summary>
public class ReaderPaneLifecycleTests
{
    private static SyncListView Pane(Form host, int width = 380, int height = 380)
    {
        var pane = new SyncListView
        {
            Font = new Font("Palatino Linotype", 13f),
            Left = 0,
            Top = 0,
            Width = width,
            Height = height,
            ShowCitationMargin = false
        };

        host.Controls.Add(pane);
        _ = pane.Handle;
        return pane;
    }

    private static TextNode Node(long id, string citation, string text) => new()
    {
        TextNodeId = id,
        CitationRef = citation,
        Text = text,
        SortOrder = (int)id,
        NodeKind = TextNodeKinds.Line,
        EditionId = 1
    };

    private static string LongProse(int words) =>
        string.Join(" ", Enumerable.Range(0, words).Select(i => $"verbum{i:D4}"));

    /// <summary>A work long enough that cutting it takes a visible moment.</summary>
    private static IReadOnlyList<TextNode> LongWork(int passages = 600) =>
        Enumerable.Range(1, passages).Select(i => Node(i, $"1.{i}", LongProse(200))).ToList();

    /// <summary>
    /// A re-cut already in flight must not put the previous work back into a
    /// pane that has since been cleared.
    ///
    /// Deterministic: Relayout runs synchronously as far as its first await, so
    /// the re-cut is certainly in flight by the time ShowMessage is called. The
    /// margin toggle is the shortest route to one, but see the test below for
    /// the route a reader takes without touching anything.
    /// </summary>
    [Fact]
    public void ARelayoutInFlightDoesNotResurrectAClearedWork() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);
        await pane.SetPassagesAsync(LongWork());

        pane.ShowCitationMargin = true;
        pane.Relayout();

        pane.ShowMessage("(no translation ingested)");
        await Task.Delay(6000);

        Assert.Single(pane.Items);
        Assert.IsType<string>(pane.Items[0]);
        Assert.Null(pane.NodeAt(0));
    }, timeoutSeconds: 180);

    /// <summary>
    /// The same thing, by the route that needs no menu and no drag.
    ///
    /// Filling a long work makes the vertical scrollbar appear, which takes
    /// seventeen pixels off the client area - so the rows were cut for a width
    /// the pane no longer has, and SetPassagesAsync queues a re-cut of the
    /// whole work 150ms later. Opening a second work inside that window is an
    /// ordinary thing to do.
    /// </summary>
    [Fact]
    public void OpeningAWorkAndThenClearingItLeavesThePaneCleared() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);
        await pane.SetPassagesAsync(LongWork());

        // past the 150ms debounce; the queued re-cut is now running
        await Task.Delay(250);

        pane.ShowMessage("(no translation ingested)");
        await Task.Delay(6000);

        Assert.Single(pane.Items);
        Assert.IsType<string>(pane.Items[0]);
    }, timeoutSeconds: 180);

    /// <summary>
    /// And with a second work rather than a message: the work asked for last
    /// is the work shown, whatever else was being cut at the time.
    /// </summary>
    [Fact]
    public void TheWorkAskedForLastIsTheWorkShown() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);
        await pane.SetPassagesAsync(LongWork());
        await Task.Delay(250);

        await pane.SetPassagesAsync(new[] { Node(9001, "2.1", "μῆνιν ἄειδε θεά") }, () => true);
        await Task.Delay(6000);

        Assert.Single(pane.Items);
        Assert.Equal(9001, pane.NodeAt(0)!.TextNodeId);
    }, timeoutSeconds: 180);

    /// <summary>
    /// Whatever is in the control, the pane's own record of the work must
    /// agree with it - otherwise nothing can put the pane right again, since
    /// both OnResize and RelayoutAsync give up on an empty _passages.
    /// </summary>
    [Fact]
    public void AClearedPaneStaysClearedThroughAResize() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);
        await pane.SetPassagesAsync(LongWork());
        await Task.Delay(250);

        pane.ClearPassages();
        await Task.Delay(6000);

        Assert.Empty(pane.Items);

        pane.Width = 240;
        await Task.Delay(3000);

        Assert.Empty(pane.Items);
    }, timeoutSeconds: 180);

    /// <summary>
    /// Opening a work cuts it up once, not twice.
    ///
    /// The scrollbar that appears during the fill narrows the client area, the
    /// end of SetPassagesAsync notices the width has moved, and the whole work
    /// is cut again. Measured on this machine at 600 passages: a first pass of
    /// 458-500ms followed by a second of 366-417ms, three runs. It is the same
    /// proportion whatever the work costs, so a prose edition that opens in
    /// twenty seconds opens in something closer to thirty-six.
    /// </summary>
    [Fact]
    public void OpeningAWorkLaysItOutOnlyOnce() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);
        await pane.SetPassagesAsync(LongWork());

        var asFilled = pane.RowAt(0);

        for (var i = 0; i < 200; i++)
        {
            await Task.Delay(25);
            Assert.True(ReferenceEquals(asFilled, pane.RowAt(0)),
                $"the work was cut a second time {i * 25}ms after it was filled");
        }
    }, timeoutSeconds: 180);

    /// <summary>
    /// A pane showing one of the explanatory messages must still be showing all
    /// of it after the reader drags the splitter.
    ///
    /// Nothing re-measures it: both OnResize and RelayoutAsync return early
    /// when the pane holds no passages, and a message is not a passage. The row
    /// keeps the height it was given at the width it was shown at, so narrowing
    /// the pane cuts the sentence off - and these messages are precisely the
    /// ones a confused reader needs to be able to read.
    /// </summary>
    [Fact]
    public void APlaceholderMessageSurvivesThePaneBeingNarrowed() => StaHarness.Run(async host =>
    {
        const string message =
            "(everything in this edition is hidden - right-click and use Show to bring it back)";

        var pane = Pane(host, width: 600);
        pane.ShowMessage(message);

        pane.Width = 170;
        await Task.Delay(1000);

        var needed = TextRenderer.MeasureText(message, pane.Font,
            new Size(pane.ClientSize.Width - 8, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

        Assert.True(pane.GetItemHeight(0) >= needed,
            $"the message is given {pane.GetItemHeight(0)}px and needs {needed}px, so it is cut off");
    }, timeoutSeconds: 120);
}
