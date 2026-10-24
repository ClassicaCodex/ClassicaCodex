using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.About;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class AboutFeature
{
    public static void Open(IReaderHost host) =>
        ComingSoon.Show(host, "About Classica Codex", "authors", "About");
}
