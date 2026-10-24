using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Research.Bench;
using ClassicaCodex.Mac.Features.Research.Echoes;
using ClassicaCodex.Mac.Features.Research.Inquiry;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research;

/// <summary>
/// Entry points for the research area, called from the main window's menus,
/// toolbar and context menus, and from the echo tools that save what they
/// found to a project.
/// </summary>
public static class ResearchFeature
{
    /// <summary>
    /// Opens the Research Bench for a work, on <paramref name="projectId"/> if
    /// one is named. One Bench per work: asking again brings it forward, and
    /// puts it on the project if one is named.
    /// </summary>
    public static void OpenBench(IReaderHost host, Work work, string authorName, long? projectId = null)
    {
        var author = string.IsNullOrWhiteSpace(authorName) ? "Unknown author" : authorName;
        var (bench, created) = ResearchWindows.Show($"bench:{work.WorkId}", host.Window,
            () => new ResearchBenchWindow(host, work, author, projectId ?? 0));
        if (!created && projectId is { } id) _ = ShowProjectAsync(bench, id);
    }

    private static async Task ShowProjectAsync(ResearchBenchWindow bench, long projectId)
    {
        try
        {
            await bench.ShowProjectAsync(projectId);
        }
        catch (Exception ex)
        {
            await Alert.Error(bench, "Research Bench", ex.Message);
        }
    }

    /// <summary>
    /// Opens the passage inquiry for a right-clicked passage, and the Bench on the
    /// project it was turned into, if it was.
    /// </summary>
    /// <returns>True when the passage now carries an inquiry, so the reader marks it.</returns>
    public static async Task<bool> StartInquiryAsync(IReaderHost host, PassageContext passage)
    {
        var textNodes = new TextNodeRepository();
        var identity = await textNodes.GetPassageResearchIdentityAsync(passage.Node.TextNodeId);
        if (identity == null)
        {
            await Alert.Info(host.Window, "Inquiry", "That passage is no longer present in the local corpus.");
            return false;
        }

        var inquiry = new PassageInquiryWindow(identity);
        await inquiry.ShowDialog(host.Window);

        // Asked of the database rather than inferred from how the window closed. An
        // inquiry saved without being promoted - which is the ordinary case, and the
        // whole point of inquiries being a lighter thing than projects - closes the
        // same way as one abandoned; judging by that would mark only the promoted ones.
        var marked = await new PassageInquiryRepository().GetAsync(identity.EditionCtsUrn, identity.CitationRef) != null;

        if (inquiry.OpenProjectId is { } projectId)
        {
            var work = await FindWorkAsync(host, identity.WorkId);
            if (work == null)
            {
                // The project was created; only the library entry for its work is
                // missing. Saying nothing makes a promotion that succeeded look like a
                // button that did nothing.
                await Alert.Info(host.Window, "Research project created",
                    "The research project was created, but this work is not in the library list at the " +
                    "moment, so the Research Bench cannot be opened from here. Open the work from the " +
                    "library and choose Research… to find it.");
            }
            else
            {
                OpenBench(host, work, identity.AuthorName, projectId);
            }
        }

        return marked;
    }

    /// <summary>
    /// Saves a set of echo candidates to a research project for human review -
    /// the Windows "Save echo investigation" dialog. Opened from Find Echoes,
    /// Cross-Language Echo and the Corpus Investigator.
    /// </summary>
    /// <returns>True if the reader saved them.</returns>
    public static async Task<bool> CaptureEchoAsync(Avalonia.Controls.Window owner, ClassicaCodex.Core.EchoCaptureRequest request, long? defaultProjectId = null)
    {
        var capture = new EchoCaptureWindow(request, defaultProjectId);
        await capture.ShowDialog(owner);
        return capture.SavedInvestigationId != null;
    }

    /// <summary>The work as the library lists it: the one in the reader, or one from the library.</summary>
    private static async Task<Work?> FindWorkAsync(IReaderHost host, int workId)
    {
        if (host.OpenWork?.WorkId == workId) return host.OpenWork;
        if (host.SelectedLibraryWork?.WorkId == workId) return host.SelectedLibraryWork;

        var all = await new WorkRepository().GetAllGroupedByAuthorAsync();
        return all.Values.SelectMany(works => works).FirstOrDefault(w => w.WorkId == workId);
    }
}
