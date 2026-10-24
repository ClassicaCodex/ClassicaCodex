using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Help;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class HelpFeature
{
    public static void Open(IReaderHost host) =>
        ComingSoon.Show(host, "Help", "starting-points", "Help");
}
