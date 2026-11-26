using Avalonia;
using Avalonia.Media.TextFormatting;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Lexicon;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// A citation as Word Study draws it: where it sits in the definition's text,
/// what it prints, and the passage it opens, or null where the library has
/// none - a citation shown as plain text.
/// </summary>
/// <param name="Start">Where the citation starts in the definition block's text.</param>
/// <param name="End">Where it ends, exclusive.</param>
internal sealed record CitationSpan(int Start, int End, EntryCitation Citation, CitedPassage? Passage)
{
    public bool Opens => Passage != null;

    /// <summary>The passage as a reader names it: "Homer, Iliad 1.441".</summary>
    public string? Destination => Passage is { } p ? DictionaryCitations.Name(p) : null;

    /// <summary>What hovering shows: the passage's name and its line.</summary>
    public string? Tip => Passage is { } p ? $"{DictionaryCitations.Name(p)}\n{PassageText.RowText(p.Text, 360)}" : null;
}

/// <summary>
/// What Word Study and the reader share about the dictionaries' citations:
/// the edition a citation opens, how a passage is named, and finding the
/// citation under the pointer.
/// </summary>
internal static class DictionaryCitations
{
    /// <summary>
    /// The originals of a work as the reader offers them, the one it opens by
    /// default first - so a citation opens the edition the reader would.
    /// </summary>
    public static IReadOnlyList<Edition> Arrange(IReadOnlyList<Edition> originals) =>
        ReaderEditionOrder.DefaultFirst(originals, PreferredCollectionSettings.Preferred);

    public static string Name(CitedPassage passage) =>
        $"{passage.AuthorName}, {passage.WorkTitle} {PassageCitation.Display(passage.CitationRef, passage.Milestone)}".Trim();

    /// <summary>
    /// The citation under a point, if any. Asked of each citation's own
    /// rectangles near the character the layout reports, rather than of the
    /// layout's point test alone, which on a wrapped line set taller than its
    /// font reports a point on the second line as outside the text - the
    /// reader's passage rows found the same.
    /// </summary>
    public static CitationSpan? At(TextLayout layout, Point point, IReadOnlyList<CitationSpan> spans)
    {
        if (spans.Count == 0) return null;

        var near = layout.HitTestPoint(point).TextPosition;
        foreach (var span in spans)
        {
            if (span.End < near - 400 || span.Start > near + 400) continue;
            foreach (var rect in layout.HitTestTextRange(span.Start, span.End - span.Start))
                if (rect.Inflate(new Thickness(0, 2)).Contains(point)) return span;
        }

        return null;
    }
}
