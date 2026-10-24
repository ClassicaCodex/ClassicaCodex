using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Vocabulary;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class VocabularyFeature
{
    public static Task OpenAsync(IReaderHost host, Work work)
    {
        ComingSoon.Show(host, "Core Vocabulary", "core-vocabulary", "Vocabulary");
        return Task.CompletedTask;
    }
}
