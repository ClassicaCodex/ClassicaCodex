using System.ComponentModel;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

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

    public event PropertyChangedEventHandler? PropertyChanged;
}
