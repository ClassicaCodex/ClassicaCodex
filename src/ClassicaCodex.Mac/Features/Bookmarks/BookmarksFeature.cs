using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Bookmarks;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class BookmarksFeature
{
    public static Task<bool> BookmarkPassageAsync(IReaderHost host, PassageContext passage)
    {
        ComingSoon.Show(host, "Bookmark This Line", "bookmarks", "Bookmarks");
        return Task.FromResult(false);
    }

    public static void Open(IReaderHost host) =>
        ComingSoon.Show(host, "Bookmarks", "bookmarks", "Bookmarks");
}
