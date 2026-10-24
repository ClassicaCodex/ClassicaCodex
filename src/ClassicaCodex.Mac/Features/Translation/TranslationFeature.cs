using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class TranslationFeature
{
    public static Task OpenWorkbenchAsync(IReaderHost host, Work work)
    {
        ComingSoon.Show(host, "Translate It Yourself", "workbench", "Workbench", "The translation workbench");
        return Task.CompletedTask;
    }

    public static Task CreateTranslationAsync(IReaderHost host, Work work)
    {
        ComingSoon.Show(host, "Create Translation", "ai-translate", "Sparkle");
        return Task.CompletedTask;
    }

    public static Task TranslatePassageAsync(IReaderHost host, PassageContext passage)
    {
        ComingSoon.Show(host, "Translate", "ai-translate", "Translate");
        return Task.CompletedTask;
    }

    public static Task StartingPointsAsync(IReaderHost host)
    {
        ComingSoon.Show(host, "Where Should I Start?", "starting-points", "StartingPoints");
        return Task.CompletedTask;
    }
}
