using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Export;

/// <summary>
/// Entry points for exporting, called from the reader's line menu, File →
/// Export Passage (⌘E), and any tool that has gathered a set of passages.
/// </summary>
public static class ExportFeature
{
    /// <summary>
    /// A run of lines from the right-clicked passage, as text, Word, PDF or a
    /// reference - MainForm's ExportSelectedLineAsync. The other pane's
    /// edition, when there is one, is what makes a bilingual export possible;
    /// when the work has no counterpart loaded the window simply disables that
    /// option.
    /// </summary>
    public static async Task ExportPassageAsync(IReaderHost host, PassageContext passage)
    {
        if (passage.Edition == null)
        {
            await Alert.Error(host.Window, "Error", "Couldn't determine which edition this line belongs to.");
            return;
        }

        var sourceInfo = await new TextNodeRepository().GetTextNodeSourceInfoAsync(passage.Node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        // The pane's face first, then the rest of its reading font's list, so
        // the export resolves to the same face the reader sees and falls back
        // the same way where that face is not installed.
        var paneFonts = $"{passage.FontFamily}, {ReadingFontList(passage.FromOriginal ? "Cc.SourceFont" : "Cc.TranslationFont")}";
        var originalFonts = passage.FromOriginal ? paneFonts : ReadingFontList("Cc.SourceFont");

        var window = new PassageExportWindow(
            passage.Node, passage.Edition.EditionId, authorName, workTitle, paneFonts,
            passage.Counterpart?.EditionId, counterpartIsOriginal: !passage.FromOriginal, originalFonts,
            passage.Counterpart == null ? null : EditionLabels.Descriptor(passage.Counterpart))
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };

        window.Show(host.Window);
    }

    /// <summary>
    /// A set of passages gathered from across the library - a tag, a
    /// concordance, echo results - with attribution on every line. The
    /// Windows PassageSetExportForm; <paramref name="detailLabel"/> names
    /// whatever extra line the caller attached to each passage.
    /// </summary>
    public static void ExportPassageSet(
        IReaderHost host, string collectionTitle, IReadOnlyList<ExportPassage> passages, string? detailLabel = null) =>
        ExportPassagesFrom(host.Window, collectionTitle, passages, detailLabel);

    /// <summary>
    /// The same, from a tool window: the dialog opens beside the window the
    /// passages came from, and still jumps the main reader. Every list that
    /// gathers passages - search, concordance, tags, bookmarks, echoes, the
    /// map, Word Study, Morphology - comes here, as on Windows they all come to
    /// PassageSetExportForm, so there is one export dialog and not one each.
    /// </summary>
    public static void ExportPassagesFrom(
        Window owner, string collectionTitle, IReadOnlyList<ExportPassage> passages, string? detailLabel = null)
    {
        var window = new PassageSetExportWindow(collectionTitle, passages, detailLabel)
        {
            OnNavigate = MainWindow.Current is { } main ? main.NavigateToPassageAsync : null,
            OnOpenWork = MainWindow.Current is { } host ? host.OpenWorkAsync : null
        };

        window.Show(owner);
    }

    /// <summary>A reading font's whole fallback list, as the theme declares it.</summary>
    internal static string ReadingFontList(string resourceKey) =>
        Application.Current?.FindResource(resourceKey) is FontFamily family
            ? string.Join(", ", family.FamilyNames)
            : "Palatino, Georgia, serif";
}
