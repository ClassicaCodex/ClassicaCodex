using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.WorkInfo;

/// <summary>
/// Entry points for what the library knows and thinks about a work, called
/// from the library's right-click menu: its details, and its attribution.
/// </summary>
public static class WorkInfoFeature
{
    private static readonly WorkRepository WorkRepo = new();

    /// <summary>Opens the details for a work beside the reader.</summary>
    public static void ShowDetails(IReaderHost host, Work work)
    {
        var window = new WorkDetailsWindow(work) { OnNavigate = host.NavigateToPassageAsync, OnOpenWork = host.OpenWorkAsync };
        window.Show(host.Window);
    }

    /// <summary>
    /// Opens the attribution sheet for a work and applies whatever the reader
    /// decided - MainForm's EditAttributionForSelectedWorkAsync.
    ///
    /// The stored value is read back afterwards and put on the Work rather
    /// than the choice being copied across: the library's menu, the reader's
    /// running head and the panes all carry the value, and the database is
    /// the one copy that cannot be stale.
    /// </summary>
    /// <returns>True when the attribution changed, so the shell can reload the work if it is the one open.</returns>
    public static async Task<bool> EditAttributionAsync(IReaderHost host, Work work)
    {
        try
        {
            var authorName = host.AuthorNameFor(work);
            var current = await WorkRepo.GetAttributionAsync(work.WorkId);

            var sheet = new AttributionWindow(authorName, work.Title, current.Status, current.Note, current.SetByUser);
            var decision = await sheet.AskAsync(host.Window);
            if (decision == null) return false;

            if (decision.ClearOverride)
            {
                await WorkRepo.ClearAttributionOverrideAsync(work.WorkId);
            }
            else if (decision.Chosen is { } chosen)
            {
                await WorkRepo.SetAttributionAsync(work.WorkId, chosen.Status, chosen.Note);
            }
            else
            {
                return false;
            }

            var updated = await WorkRepo.GetAttributionAsync(work.WorkId);
            work.AttributionStatus = updated.Status;
            work.AttributionNote = updated.Note;
            work.AttributionSetByUser = updated.SetByUser;
            return true;
        }
        catch (Exception ex)
        {
            await Alert.Error(host.Window, "Couldn't save the attribution", ex.Message);
            return false;
        }
    }
}
