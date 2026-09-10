using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// One row of a reader pane, which is a whole passage or a piece of one.
///
/// The reader used to put a <see cref="TextNode"/> straight into the list and
/// rely on one row meaning one passage. That could not survive the control's
/// 255-pixel ceiling on row height - see <see cref="ReaderRowHeight"/> - which
/// left about seven per cent of this corpus showing only as much of itself as
/// would fit. A passage too tall for a row is now several rows, and this is
/// what they are.
///
/// <see cref="Text"/> is the piece's own text and nothing else. In particular
/// the tag and bookmark marks are NOT folded into it, as they were when a row
/// was a whole passage: those are added and removed while a pane is on screen,
/// without repopulating it, and text frozen at the moment of splitting could
/// not carry them. They are drawn after the last piece instead.
/// </summary>
internal sealed class ReaderRow
{
    internal ReaderRow(TextNode node, string text, int segmentIndex, int segmentCount)
    {
        Node = node;
        Text = text;
        SegmentIndex = segmentIndex;
        SegmentCount = segmentCount;
    }

    /// <summary>The passage this row shows, whole or in part.</summary>
    internal TextNode Node { get; }

    /// <summary>
    /// This row's own text, exactly as the edition has it. Concatenating the
    /// text of every row of a passage gives that passage back character for
    /// character - see <see cref="ReaderRowSplitter"/>, which holds that
    /// invariant, and the tests that pin it.
    /// </summary>
    internal string Text { get; }

    internal int SegmentIndex { get; }

    internal int SegmentCount { get; }

    /// <summary>
    /// Whether this row begins a passage. The citation mark goes here and only
    /// here: a passage divided over four rows is still one passage, and
    /// printing its reference against each row would tell the reader it was
    /// four.
    /// </summary>
    internal bool IsFirst => SegmentIndex == 0;

    /// <summary>
    /// Whether this row ends a passage - where the tag and bookmark marks
    /// belong, for the same reason the citation goes on the first.
    /// </summary>
    internal bool IsLast => SegmentIndex == SegmentCount - 1;

    /// <summary>Whether the passage was divided at all.</summary>
    internal bool IsSplit => SegmentCount > 1;

    public override string ToString() => Text;
}
