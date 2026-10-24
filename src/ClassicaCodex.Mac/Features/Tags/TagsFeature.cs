using Avalonia.Controls;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// Entry points for tagging, called from the main window's menus, toolbar
/// and context menus: tag the line the reader right-clicked, and browse
/// every tag across every author.
/// </summary>
public static class TagsFeature
{
    private static readonly TagRepository TagRepo = new();

    /// <summary>
    /// "Tag This Line…": asks for a tag, applies it, and says so - MainForm's
    /// TagSelectedLineAsync.
    /// </summary>
    /// <returns>True when the line was tagged; the shell then adds the mark.</returns>
    public static async Task<bool> TagPassageAsync(IReaderHost host, PassageContext passage)
    {
        var node = passage.Node;

        var answer = await TagPrompt.AskAsync(host.Window, node.Text);
        if (answer is not { } chosen || string.IsNullOrWhiteSpace(chosen.Name)) return false;

        string? storedCategory;
        try
        {
            int tagId;
            (tagId, storedCategory) = await TagRepo.GetOrCreateAsync(chosen.Name, chosen.Category);
            await TagRepo.TagTextNodeAsync(node.TextNodeId, tagId);
        }
        catch (Exception ex)
        {
            await Alert.Error(host.Window, "Couldn't tag the line", ex.Message);
            return false;
        }

        // The mark goes on before the confirmation, as on Windows, so the
        // reader sees the line marked behind the message rather than after it.
        await host.RefreshPassageMarksAsync();

        // A category typed for a tag that already had another is not applied
        // (see TagRepository.GetOrCreateAsync); Auto-Tag says so, and so does
        // this, rather than leave the box looking as if it took.
        var note = AutoTagWindow.CategoryNote(chosen.Name, chosen.Category, storedCategory);

        await Alert.Info(host.Window, "Tagged",
            $"Tagged [{PassageCitation.Display(node.CitationRef, node.Milestone)}] with “{chosen.Name}”." + note);
        return true;
    }

    /// <summary>
    /// The tag browser. One at a time: a second request brings the open one
    /// forward rather than stacking another beside it.
    ///
    /// When it closes the reader re-reads its marks, because tags can be
    /// deleted in there as well as browsed, and a passage still showing its
    /// mark after the tag behind it is gone would be worse than not marking
    /// it at all.
    /// </summary>
    public static void OpenBrowser(IReaderHost host)
    {
        if (Find<TagBrowserWindow>(host) is { } open)
        {
            open.Activate();
            return;
        }

        var window = new TagBrowserWindow { OnNavigate = host.NavigateToPassageAsync };
        window.Closed += async (_, _) => await host.RefreshPassageMarksAsync();
        window.Show(host.Window);
    }

    internal static T? Find<T>(IReaderHost host) where T : Window =>
        host.Window.OwnedWindows.OfType<T>().FirstOrDefault(w => w.IsVisible);
}
