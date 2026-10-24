using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Compare;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class CompareFeature
{
    public static void CompareTranslations(IReaderHost host) =>
        ComingSoon.Show(host, "Compare Translations", "compare", "CompareTranslations");

    public static void Collate(IReaderHost host) =>
        ComingSoon.Show(host, "Collate Editions", "collation", "Collate");
}
