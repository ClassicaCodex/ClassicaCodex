using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// The reader's three ways of asking where else a passage is heard - its line
/// menu's Find Echoes, Find Cross-Language Echo and Reception History - each
/// opening its window beside the reader, so a jump to a candidate is visible
/// at once and the next one is still a click away.
/// </summary>
public static class EchoFeature
{
    /// <summary>Other passages ranked by the rare words they share with this one.</summary>
    public static void FindEchoes(IReaderHost host, PassageContext passage)
    {
        var window = new EchoResultsWindow(
            passage.Node, EchoUi.ReadingFontFor(passage.Edition, passage.FromOriginal), Attribution(host, passage))
        {
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    /// <summary>
    /// One chosen work, in any language, searched by Gemini for an image or an
    /// idea this passage shares with it. The edition the passage was read in
    /// is the one left out of the picker, since comparing a work against its
    /// own text is not the question.
    /// </summary>
    public static async Task CrossLanguageAsync(IReaderHost host, PassageContext passage)
    {
        var sourceInfo = await new TextNodeRepository().GetTextNodeSourceInfoAsync(passage.Node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        var window = new CrossLanguageEchoWindow(
            passage.Node, passage.Edition?.Language, authorName, workTitle, passage.Edition?.EditionId ?? -1,
            EchoUi.ReadingFontFor(passage.Edition, passage.FromOriginal))
        {
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    /// <summary>The Find Echoes candidates split by whether their author came before or after this one.</summary>
    public static void ReceptionHistory(IReaderHost host, PassageContext passage)
    {
        var window = new ReceptionTrackerWindow(
            passage.Node, EchoUi.ReadingFontFor(passage.Edition, passage.FromOriginal), Attribution(host, passage))
        {
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    /// <summary>
    /// "Author, Work" for the header above the candidates - the reader's open
    /// work, which is where a right-clicked passage comes from. Null if the
    /// passage turns out to belong to some other work, rather than a wrong name.
    /// </summary>
    private static string? Attribution(IReaderHost host, PassageContext passage) =>
        host.OpenWork is { } work && passage.Edition?.WorkId == work.WorkId
            ? $"{host.AuthorNameFor(work)}, {work.Title}"
            : null;
}
