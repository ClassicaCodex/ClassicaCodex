using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Reactions;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class ReactionsFeature
{
    public static void Open(IReaderHost host, Work work, string authorName) =>
        ComingSoon.Show(host, "Fictional Ancient Reactions", "reactions", "Reactions");

    /// <summary>Whether a work has a staged debate - most do not, and the menu item hides for them.</summary>
    public static bool HasDebate(string authorName, Work work) =>
        ClassicaCodex.Core.Reactions.ReactionLibrary.HasDebateFor(authorName, work.Title);
}
