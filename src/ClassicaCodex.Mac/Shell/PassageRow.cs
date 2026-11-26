using System.ComponentModel;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Quotations;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// One passage in a reader pane.
///
/// One row per passage, always. The Windows pane has to cut a long passage
/// into several rows because a WinForms list cannot draw a row taller than
/// 255 pixels, and much of its bookkeeping - passage ordinals as distinct from
/// row numbers, landing on a passage's first row - exists to undo that cut.
/// An Avalonia list measures each item at whatever height it needs, so here
/// the row number and the passage ordinal are the same number and the
/// bookkeeping has nothing to do.
/// </summary>
public sealed class PassageRow : INotifyPropertyChanged
{
    public PassageRow(TextNode node, int ordinal, string? marginText)
    {
        Node = node;
        Ordinal = ordinal;
        MarginText = marginText;
    }

    public TextNode Node { get; }

    /// <summary>Position in the pane, which is also the passage's position: see the class remarks.</summary>
    public int Ordinal { get; }

    /// <summary>
    /// The citation to print in the margin beside this row, or null where the
    /// margin stays quiet - which is most rows, since a printed edition marks
    /// where a section starts and says nothing until the next one.
    /// </summary>
    public string? MarginText { get; }

    private PassageMarks _marks;

    /// <summary>The ?, # and ★ after the passage: an inquiry, a tag, a bookmark.</summary>
    public PassageMarks Marks
    {
        get => _marks;
        set
        {
            if (_marks == value) return;
            _marks = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Marks)));
        }
    }

    public string Kind => string.IsNullOrWhiteSpace(Node.NodeKind) ? TextNodeKinds.Line : Node.NodeKind;

    /// <summary>
    /// The words in this passage that quote an earlier one, each with where it
    /// leads: drawn underlined, and a click on them goes to the line quoted.
    /// Empty for nearly every passage.
    /// </summary>
    public IReadOnlyList<QuotedSpan> Quotations { get; init; } = Array.Empty<QuotedSpan>();

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Quoted words in a passage: where they are in its text, and the line they
/// quote.
/// </summary>
/// <param name="Start">Where the quoted words start in the passage's text.</param>
/// <param name="End">Where they end, exclusive.</param>
/// <param name="Label">The quoted line as a reader names it: "Homer, Iliad 1.423".</param>
/// <param name="Link">The machine-found link this comes from.</param>
public sealed record QuotedSpan(int Start, int End, string Label, QuotationLink Link);
