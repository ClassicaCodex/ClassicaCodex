using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class ArcadeFeature
{
    public static void Open(IReaderHost host) =>
        ComingSoon.Show(host, "Bronze and Thunder", "reactions", "Gamepad", "The hidden arcade");

    /// <summary>Whether the arcade window is open, for the ⌃⇧Return chord.</summary>
    public static bool IsOpen => false;

    /// <summary>Hands the reader's selected passage to the open arcade (⌃⇧Return).</summary>
    public static void SubmitReaderSelection()
    {
    }
}
