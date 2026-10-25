using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Reactions;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Reactions;

/// <summary>
/// Fictional Ancient Reactions: the staged argument a work's first audiences
/// might have had, opened from the library's work menu, the reader's line
/// menu and the Research menu.
/// </summary>
public static class ReactionsFeature
{
    /// <summary>
    /// Opens the debates about a work beside the reader.
    ///
    /// Synchronous, because nothing has to be fetched first: the debates are
    /// in the executable and the window resolves its own passage links once
    /// it is up. The menu items only show when there is something to show, so
    /// the empty case is a guard rather than a message.
    /// </summary>
    public static void Open(IReaderHost host, Work work, string authorName)
    {
        var debates = ReactionLibrary.DebatesFor(authorName, work.Title);
        if (debates.Count == 0) return;

        try
        {
            var window = new ReactionsWindow(work, authorName, debates)
            {
                OnNavigate = host.NavigateToPassageAsync,
                OnOpenWork = host.OpenWorkAsync
            };
            window.Show(host.Window);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "opening Fictional Ancient Reactions");
            _ = Alert.Error(host.Window, "Fictional Ancient Reactions", $"Couldn't open the reactions window: {ex.Message}");
        }
    }

    /// <summary>Whether a work has a staged debate - most do not, and the menu item hides for them.</summary>
    public static bool HasDebate(string authorName, Work work) =>
        ClassicaCodex.Core.Reactions.ReactionLibrary.HasDebateFor(authorName, work.Title);
}
