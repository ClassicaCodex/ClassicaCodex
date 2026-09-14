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
    internal ReaderRow(TextNode node, string text, int segmentIndex, int segmentCount, int height = 0)
    {
        Node = node;
        Text = text;
        SegmentIndex = segmentIndex;
        SegmentCount = segmentCount;
        Height = height;
    }

    /// <summary>
    /// How tall this row was measured to be, or zero when it has not been
    /// measured yet.
    ///
    /// Carried on the row rather than looked up by its text, which is what the
    /// reader used to do and what made filling a long work slow. Two things
    /// were wrong with the lookup. It hashed and compared the row's whole text
    /// on every row, which for paragraphs is not free; and worse, it was kept
    /// per pane width, while the width is read afresh when the control asks -
    /// so the moment the vertical scrollbar appeared during a fill and took
    /// seventeen pixels off the client area, every remaining row looked in a
    /// bucket that did not exist and was measured again from scratch. A work
    /// whose layout had just been read back from disk was re-measured in full
    /// anyway.
    ///
    /// A row knows its own height. There is nothing to look up and nothing to
    /// disagree with.
    /// </summary>
    internal int Height { get; }

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

    /// <summary>
    /// How much of a row's text the list control is given to keep.
    ///
    /// It keeps a copy of whatever a row's ToString says, and it charges more
    /// than linearly for the length: measured on a bare owner-draw list of
    /// 8,000 rows, 10 characters a row costs 42 ms per thousand, 300 costs
    /// 112, 600 costs 349 and 1,200 costs 1,300 - while the same rows showing
    /// nothing at all cost 34. Rows of divided prose run to several hundred
    /// characters, so handing over the whole of every one was most of the time
    /// it took to open a long work.
    ///
    /// Nothing the reader sees comes from that copy. The panes are
    /// owner-drawn: every row is painted from <see cref="Text"/>. What the
    /// copy is for is the control's own type-ahead, which matches from the
    /// beginning of a row and so is unaffected by a limit this far in.
    /// </summary>
    private const int ShownToTheControl = 120;

    /// <summary>
    /// What the list control keeps, which is not what the reader is shown -
    /// see <see cref="ShownToTheControl"/>. Use <see cref="Text"/> for the
    /// row's actual text.
    /// </summary>
    public override string ToString() =>
        Text.Length <= ShownToTheControl ? Text : Text[..ShownToTheControl];
}
