using Avalonia.Controls;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// What a tool window may ask of the main window. The Windows forms are
/// handed a delegate or two by MainForm - OnNavigate, OnOpenWork - and this is
/// the same idea gathered in one place, so a feature can be opened from the
/// toolbar, the menu bar or a context menu and always find the reader the
/// same way.
/// </summary>
public interface IReaderHost
{
    /// <summary>The main window, to own dialogs and tool windows.</summary>
    Window Window { get; }

    /// <summary>Opens the work and selects the passage, in whichever pane holds it.</summary>
    Task NavigateToPassageAsync(int workId, long textNodeId);

    /// <summary>Opens a work without choosing a passage.</summary>
    Task<bool> OpenWorkAsync(int workId);

    /// <summary>The work in the reader, if any.</summary>
    Work? OpenWork { get; }

    /// <summary>The work selected in the library sidebar, if a work row is selected.</summary>
    Work? SelectedLibraryWork { get; }

    /// <summary>The author's plain name for a work, as the library knows it.</summary>
    string AuthorNameFor(Work work);

    /// <summary>Re-reads the tag, bookmark and inquiry marks without refilling the panes.</summary>
    Task RefreshPassageMarksAsync();

    /// <summary>Reloads the library tree - after setup has added to it.</summary>
    Task ReloadLibraryAsync();

    /// <summary>Reloads the open work's editions - after a translation was written or changed.</summary>
    Task ReloadOpenWorkAsync();
}

/// <summary>
/// A passage the reader right-clicked, with what the Windows menu handlers
/// work out from the pane: which edition it is in, which edition is
/// opposite it, and whether it came from the original's side.
/// </summary>
public sealed record PassageContext(
    TextNode Node,
    Edition? Edition,
    Edition? Counterpart,
    bool FromOriginal,
    string FontFamily,
    string? EditionLabel,
    string? CounterpartLabel);
