using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class TagsFeature
{
    public static Task<bool> TagPassageAsync(IReaderHost host, PassageContext passage)
    {
        ComingSoon.Show(host, "Tag This Line", "tags", "Tags");
        return Task.FromResult(false);
    }

    public static void OpenBrowser(IReaderHost host) =>
        ComingSoon.Show(host, "Tags", "tags", "Tags");
}
