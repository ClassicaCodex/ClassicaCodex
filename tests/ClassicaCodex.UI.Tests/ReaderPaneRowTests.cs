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

        var rebuilt = string.Concat(Enumerable.Range(0, pane.Items.Count).Select(i => pane.Items[i]!.ToString()));
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

        Assert.Equal(text, string.Concat(Enumerable.Range(0, narrow.Items.Count).Select(i => narrow.Items[i]!.ToString())));
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
        Assert.Equal(text, string.Concat(Enumerable.Range(0, pane.Items.Count).Select(i => pane.Items[i]!.ToString())));
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
            Assert.Equal(node.Text, string.Concat(rows.Select(i => pane.Items[i]!.ToString())));
        }
    });
}
