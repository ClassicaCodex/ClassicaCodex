using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class EchoFeature
{
    public static void FindEchoes(IReaderHost host, PassageContext passage) =>
        ComingSoon.Show(host, "Find Echoes", "echo-finder", "Echo");

    public static Task CrossLanguageAsync(IReaderHost host, PassageContext passage)
    {
        ComingSoon.Show(host, "Cross-Language Echo", "cross-language", "CrossLanguage");
        return Task.CompletedTask;
    }

    public static void ReceptionHistory(IReaderHost host, PassageContext passage) =>
        ComingSoon.Show(host, "Reception History", "reception", "Reception");
}
