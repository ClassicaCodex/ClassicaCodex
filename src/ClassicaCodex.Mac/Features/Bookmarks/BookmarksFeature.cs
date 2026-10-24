using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Tags;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Bookmarks;

/// <summary>
/// Entry points for bookmarks, called from the main window's menus, toolbar
/// and context menus: bookmark the line the reader right-clicked, and open
/// the list of them.
/// </summary>
public static class BookmarksFeature
{
    private static readonly BookmarkRepository BookmarkRepo = new();

    /// <summary>
    /// "Bookmark This Line…": asks for an optional note, saves the bookmark
    /// and says so - MainForm's BookmarkSelectedLineAsync.
    /// </summary>
    /// <returns>True when the line was bookmarked; the shell then adds the mark.</returns>
    public static async Task<bool> BookmarkPassageAsync(IReaderHost host, PassageContext passage)
    {
        var node = passage.Node;

        var (confirmed, note) = await BookmarkPrompt.AskAsync(host.Window, node.Text);
        if (!confirmed) return false;

        try
        {
            await BookmarkRepo.AddAsync(node.TextNodeId, note);
        }
        catch (Exception ex)
        {
            // The repository's own message is written for the reader: the line
            // went away between the pane drawing it and the bookmark being saved.
            await Alert.Error(host.Window, "Couldn't bookmark the line", ex.Message);
            return false;
        }

        // The mark goes on before the confirmation, as on Windows.
        await host.RefreshPassageMarksAsync();

        await Alert.Info(host.Window, "Bookmarked",
            $"Bookmarked [{PassageCitation.Display(node.CitationRef, node.Milestone)}].");
        return true;
    }

    /// <summary>
    /// The bookmarks window. One at a time; a second request brings the open
    /// one forward. When it closes the reader re-reads its marks, since a
    /// bookmark deleted in there should not leave its star behind.
    /// </summary>
    public static void Open(IReaderHost host)
    {
        if (TagsFeature.Find<BookmarksWindow>(host) is { } open)
        {
            open.Activate();
            return;
        }

        var window = new BookmarksWindow { OnNavigate = host.NavigateToPassageAsync };
        window.Closed += async (_, _) => await host.RefreshPassageMarksAsync();
        window.Show(host.Window);
    }
}
