using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Alphabet;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class AlphabetFeature
{
    public static void Open(IReaderHost host, string? language) =>
        ComingSoon.Show(host, "Alphabet Reference", "alphabet", "Alphabet");
}
