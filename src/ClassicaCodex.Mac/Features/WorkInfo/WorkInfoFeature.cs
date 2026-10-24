using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.WorkInfo;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class WorkInfoFeature
{
    public static void ShowDetails(IReaderHost host, Work work) =>
        ComingSoon.Show(host, "Work Details", "reader", "Work");

    public static Task<bool> EditAttributionAsync(IReaderHost host, Work work)
    {
        ComingSoon.Show(host, "Attribution", "stylometry", "Show");
        return Task.FromResult(false);
    }
}
