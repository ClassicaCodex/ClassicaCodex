using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class WordStudyFeature
{
    public static void Open(IReaderHost host, PassageContext passage) =>
        ComingSoon.Show(host, "Word Study", "word-study", "WordStudy");
}
