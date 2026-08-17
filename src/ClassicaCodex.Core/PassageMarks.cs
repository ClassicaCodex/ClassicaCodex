namespace ClassicaCodex.Core;

/// <summary>
/// What has been recorded against a passage - an inquiry, a tag, a bookmark,
/// or any combination.
///
/// Flags rather than a single value because they genuinely combine: a line
/// worth bookmarking is often a line worth tagging, and the one you have opened
/// an inquiry on is usually both.
/// </summary>
[Flags]
public enum PassageMarks
{
    None = 0,
    Inquiry = 1,
    Tag = 2,
    Bookmark = 4
}

/// <summary>
/// The marks a reader sees at the end of a line.
///
/// Three plain glyphs rather than icons or colour. The reading panes are drawn
/// with a font the reader chooses, which may be a Greek or medieval face with
/// narrow coverage, so anything ornamental risks arriving as a missing-glyph
/// box in exactly the fonts this app exists to display. A question mark, a
/// hash and a star are in effectively every font ever made, and each already
/// means the thing it is standing for.
///
/// The order is fixed - inquiry, tag, bookmark - so a line carrying two marks
/// looks the same wherever it appears, and so the run never ends on the
/// question mark, which trailing a sentence would read as punctuation rather
/// than as a mark.
/// </summary>
public static class PassageMarkSymbols
{
    public const string Inquiry = "?";
    public const string Tag = "#";
    public const string Bookmark = "★";

    /// <summary>
    /// What to append to a line, including its leading gap, or an empty string
    /// for an unmarked passage.
    ///
    /// Appended for display only. The passage text itself is what gets copied,
    /// exported, searched and tokenised, and marks written into it would travel
    /// into all of those - the same reason an athetized line is shown in italic
    /// rather than by putting brackets in the string.
    /// </summary>
    public static string Suffix(PassageMarks marks)
    {
        if (marks == PassageMarks.None) return string.Empty;

        var symbols = new List<string>(3);
        if (marks.HasFlag(PassageMarks.Inquiry)) symbols.Add(Inquiry);
        if (marks.HasFlag(PassageMarks.Tag)) symbols.Add(Tag);
        if (marks.HasFlag(PassageMarks.Bookmark)) symbols.Add(Bookmark);

        return "   " + string.Join(" ", symbols);
    }
}
