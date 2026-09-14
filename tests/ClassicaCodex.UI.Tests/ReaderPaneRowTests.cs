using ClassicaCodex.Core.Models;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// A reader pane once a passage can occupy more than one row.
///
/// A list row cannot exceed 255 pixels, which is a Win32 limit rather than a
/// choice, and about seven per cent of this corpus wants more. Those passages
/// used to show as much of themselves as fitted and no more. They are now
/// divided across rows, and these tests are about what that must not break:
/// that the text is all still there, that a row still answers with the passage
/// it belongs to, and that the two panes still line up.
///
/// SyncListView is a ListBox subclass that persists nothing, so it can be
/// exercised directly - hosted on a bare form, with handles forced, because a
/// control that was never shown measures nothing and would make all of this
/// pass without testing anything.
/// </summary>
public class ReaderPaneRowTests
{
    private static SyncListView Pane(Form host, int width = 380)
    {
        var pane = new SyncListView
        {
            Font = new Font("Palatino Linotype", 13f),
            Left = 0,
            Top = 0,
            Width = width,
            Height = 380,
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

    /// <summary>A passage far too tall for one row at any sane reader width.</summary>
    private static string LongProse(int words) =>
        string.Join(" ", Enumerable.Range(0, words).Select(i => $"verbum{i:D4}"));

    [Fact]
    public void AShortPassageIsStillOneRow() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(1, "1.1", "μῆνιν ἄειδε θεά") });

        Assert.Single(pane.Items);
        Assert.Equal(1, pane.NodeAt(0)!.TextNodeId);
    });

    [Fact]
    public void ALongPassageBecomesSeveralRows() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(1, "1.1", LongProse(600)) });

        Assert.True(pane.Items.Count > 1, $"expected several rows, got {pane.Items.Count}");
    });

    /// <summary>
    /// The point of the whole change. Every character the edition has must be
    /// on screen somewhere, in order.
    /// </summary>
    [Fact]
    public void EveryCharacterOfALongPassageIsOnScreenSomewhere() => StaHarness.Run(async host =>
    {
        var text = LongProse(600);
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(1, "1.1", text) });

        var rebuilt = string.Concat(Enumerable.Range(0, pane.Items.Count).Select(i => pane.RowAt(i)!.Text));
        Assert.Equal(text, rebuilt);
    });

    /// <summary>
    /// Every row of a divided passage answers with that passage, so that
    /// tagging, bookmarking or translating from the middle of a long paragraph
    /// acts on the paragraph.
    /// </summary>
    [Fact]
    public void EveryRowOfAPassageResolvesToThatPassage() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(7, "1.1", LongProse(600)) });

        Assert.True(pane.Items.Count > 1);
        for (var i = 0; i < pane.Items.Count; i++)
        {
            Assert.Equal(7, pane.NodeAt(i)!.TextNodeId);
        }
    });

    [Fact]
    public void JumpingToAPassageLandsOnItsFirstRow() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[]
        {
            Node(1, "1.1", "short one"),
            Node(2, "1.2", LongProse(600)),
            Node(3, "1.3", "short two")
        });

        var row = pane.RowOfNode(2);

        Assert.True(row >= 0);
        Assert.Equal(2, pane.NodeAt(row)!.TextNodeId);
        Assert.Equal(row, pane.FirstRowOfPassage(row + 1));
    });

    /// <summary>
    /// What the two panes are now kept in step by. Row number cannot do it:
    /// the editions divide their text differently, so they divide their rows
    /// differently again.
    /// </summary>
    [Fact]
    public void PassageOrdinalsCountPassagesRatherThanRows() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[]
        {
            Node(1, "1.1", "short one"),
            Node(2, "1.2", LongProse(600)),
            Node(3, "1.3", "short two")
        });

        Assert.Equal(0, pane.PassageOrdinalAt(0));
        Assert.Equal(2, pane.PassageOrdinalAt(pane.Items.Count - 1));

        // Every row of the middle passage is the second passage, however many
        // rows it took.
        var middleRows = Enumerable.Range(0, pane.Items.Count)
            .Where(i => pane.NodeAt(i)!.TextNodeId == 2)
            .ToList();

        Assert.True(middleRows.Count > 1);
        foreach (var row in middleRows) Assert.Equal(1, pane.PassageOrdinalAt(row));
    });

    /// <summary>
    /// The round trip the pane sync relies on: a passage ordinal in one pane
    /// resolves to that passage's first row in the other, whatever either pane
    /// had to do to fit it.
    /// </summary>
    [Fact]
    public void AnOrdinalResolvesBackToThatPassagesFirstRow() => StaHarness.Run(async host =>
    {
        var source = Pane(host);
        var target = Pane(host, width: 300);

        var passages = new[]
        {
            Node(1, "1.1", "short one"),
            Node(2, "1.2", LongProse(600)),
            Node(3, "1.3", "short two")
        };

        await source.SetPassagesAsync(passages);
        await target.SetPassagesAsync(passages);

        for (var i = 0; i < source.Items.Count; i++)
        {
            var ordinal = source.PassageOrdinalAt(i);
            var mirrored = target.RowOfPassageOrdinal(ordinal);

            Assert.Equal(source.NodeAt(i)!.TextNodeId, target.NodeAt(mirrored)!.TextNodeId);
        }
    });

    /// <summary>
    /// A pane with nothing in it must not move a pane that is being read.
    ///
    /// The two questions the sync is built out of - which passage is here, and
    /// where is that passage - both used to answer with an index rather than
    /// admitting they could not answer. "No passage at this row" came back as
    /// passage zero, and "this pane has no such passage" came back as the last
    /// row in it. Both are valid indices, so both callers acted on them.
    ///
    /// What that cost a reader: most works have no translation, so the right
    /// pane ordinarily holds "(no translation ingested)", and Windows sends
    /// the wheel to whatever the pointer is over. One notch over the right
    /// half of the window, or one click on it, and the work being read jumped
    /// back to its first line. The reverse case - a shorter counterpart, which
    /// is 667 of the 896 works here - pinned the other pane to its last row
    /// instead, and printed that passage's reference under it.
    ///
    /// Not a 3.7.0 regression: 3.6.19 copied TopIndex directly and did the
    /// same thing from the same two gestures. It survived the rewrite.
    /// </summary>
    [Fact]
    public void APaneShowingAMessageDoesNotMoveThePaneBeingRead() => StaHarness.Run(async host =>
    {
        var reading = Pane(host);
        var placeholder = Pane(host, width: 300);

        await reading.SetPassagesAsync(
            Enumerable.Range(1, 300).Select(i => Node(i, $"1.{i}", $"line {i}")).ToList());

        placeholder.ShowMessage("(no translation ingested)");

        Assert.Single(placeholder.Items);
        Assert.Equal(-1, placeholder.PassageOrdinalAt(0));
        Assert.Equal(-1, placeholder.RowOfPassageOrdinal(0));
    });

    /// <summary>
    /// And a pane that simply does not go that far must decline rather than
    /// offer its last row. The Iliad is 15,687 passages against 425 in its
    /// translation, so this is reached by scrolling, not by contriving.
    /// </summary>
    [Fact]
    public void AnOrdinalPastTheEndOfAShorterPaneIsRefusedRatherThanClamped() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(
            Enumerable.Range(1, 20).Select(i => Node(i, $"1.{i}", $"line {i}")).ToList());

        Assert.Equal(19, pane.RowOfPassageOrdinal(19));
        Assert.Equal(-1, pane.RowOfPassageOrdinal(20));
        Assert.Equal(-1, pane.RowOfPassageOrdinal(5000));
        Assert.Equal(-1, pane.RowOfPassageOrdinal(-1));
    });

    /// <summary>
    /// The one that matters most, and the one that was missing.
    ///
    /// Reported from an actual launch: both panes empty, and a dialog saying
    /// the passage was not on screen - which was true, because nothing was.
    /// A window still settling its layout changes width while a work is being
    /// cut, and the fill was being discarded for being a moment out of date;
    /// the pane was then empty, and the resize handler declined to queue
    /// anything for an empty pane, so nothing ever brought it back.
    ///
    /// Given passages, a pane must end up showing them. Whatever happened to
    /// the layout in between.
    /// </summary>
    [Fact]
    public void APaneGivenPassagesIsNeverLeftEmpty() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        var passages = Enumerable.Range(1, 40)
            .Select(i => Node(i, $"1.{i}", i % 7 == 0 ? LongProse(400) : $"line {i}"))
            .ToArray();

        // Moved mid-flight, as a window settling its layout does.
        var fill = pane.SetPassagesAsync(passages);
        pane.Width = 300;
        await fill;

        Assert.NotEmpty(pane.Items);
        Assert.Equal(1, pane.NodeAt(0)!.TextNodeId);
    });

    /// <summary>
    /// Reported from use: opening a work with no translation left the previous
    /// work's translation sitting in the right-hand pane.
    ///
    /// Clearing the rows is not enough, because the pane knows how to rebuild
    /// its rows from the passages it was given - so anything that prompted a
    /// re-cut put the old work straight back. A cleared pane has to forget the
    /// work as well as the rows.
    /// </summary>
    [Fact]
    public void AClearedPaneDoesNotBringTheOldWorkBack() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(1, "1.1", LongProse(400)) });
        Assert.NotEmpty(pane.Items);

        pane.ClearPassages();

        // Whatever would have prompted a re-cut: a splitter drag, a window
        // resize, the citation margin being switched on.
        pane.Width = 300;
        await Task.Delay(400);

        Assert.Empty(pane.Items);
    });

    /// <summary>
    /// The same for the pane that is showing an explanation rather than a
    /// text - "(no translation ingested)" must not turn back into the last
    /// translation.
    /// </summary>
    [Fact]
    public void APaneShowingAMessageKeepsShowingIt() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(1, "1.1", LongProse(400)) });
        pane.ShowMessage("(no translation ingested)");

        pane.Width = 300;
        await Task.Delay(400);

        Assert.Single(pane.Items);
        Assert.Equal("(no translation ingested)", pane.Items[0]!.ToString());
        Assert.Null(pane.NodeAt(0));
    });

    /// <summary>
    /// And the same for the font, which moves the cuts just as the width does.
    /// </summary>
    [Fact]
    public void APaneWhoseFontChangesMidFillIsStillFilled() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        var fill = pane.SetPassagesAsync(new[] { Node(1, "1.1", LongProse(400)) });
        pane.Font = new Font("Palatino Linotype", 15f);
        await fill;

        Assert.NotEmpty(pane.Items);
    });

    /// <summary>
    /// Cutting depends on the width, so a narrower pane needs more rows for
    /// the same passage - and still all of it.
    /// </summary>
    [Fact]
    public void ANarrowerPaneUsesMoreRowsAndStillShowsEverything() => StaHarness.Run(async host =>
    {
        var text = LongProse(600);

        var wide = Pane(host, width: 600);
        var narrow = Pane(host, width: 260);

        await wide.SetPassagesAsync(new[] { Node(1, "1.1", text) });
        await narrow.SetPassagesAsync(new[] { Node(1, "1.1", text) });

        Assert.True(narrow.Items.Count > wide.Items.Count,
            $"narrow {narrow.Items.Count} rows vs wide {wide.Items.Count}");

        Assert.Equal(text, string.Concat(Enumerable.Range(0, narrow.Items.Count).Select(i => narrow.RowAt(i)!.Text)));
    });

    /// <summary>
    /// A passage with no break opportunity cannot be divided, and must still
    /// come back as a row rather than as nothing or a hang.
    /// </summary>
    [Fact]
    public void AnUnbreakablePassageIsStillShown() => StaHarness.Run(async host =>
    {
        var text = new string('x', 4000);
        var pane = Pane(host);

        await pane.SetPassagesAsync(new[] { Node(1, "1.1", text) });

        Assert.True(pane.Items.Count >= 1);
        Assert.Equal(text, string.Concat(Enumerable.Range(0, pane.Items.Count).Select(i => pane.RowAt(i)!.Text)));
    });

    [Fact]
    public void AnEmptyEditionLeavesAnEmptyPane() => StaHarness.Run(async host =>
    {
        var pane = Pane(host);

        await pane.SetPassagesAsync(Array.Empty<TextNode>());

        Assert.Empty(pane.Items);
    });

    /// <summary>
    /// Mixed content - the shape of a real work, where most passages fit and a
    /// few do not.
    /// </summary>
    [Fact]
    public void AWorkOfMostlyShortPassagesKeepsThemOneRowEach() => StaHarness.Run(async host =>
    {
        var passages = Enumerable.Range(1, 30)
            .Select(i => Node(i, $"1.{i}", i == 15 ? LongProse(600) : $"line {i} of the poem"))
            .ToArray();

        var pane = Pane(host);
        await pane.SetPassagesAsync(passages);

        // 29 short passages plus however many the long one needed.
        Assert.True(pane.Items.Count > 30);

        foreach (var node in passages)
        {
            var rows = Enumerable.Range(0, pane.Items.Count)
                .Where(i => pane.NodeAt(i)!.TextNodeId == node.TextNodeId)
                .ToList();

            Assert.NotEmpty(rows);
            Assert.Equal(node.Text, string.Concat(rows.Select(i => pane.RowAt(i)!.Text)));
        }
    });
}
