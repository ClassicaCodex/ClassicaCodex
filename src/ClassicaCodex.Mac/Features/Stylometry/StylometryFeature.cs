using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// Entry points for stylometry, called from the main window's menus, toolbar
/// and tool gallery, and from the research bench.
///
/// Compare Saved Runs is a window of its own rather than a tab of the
/// Stylometry window, as on Windows: that one produces a run, this one
/// compares many, and they are used at different moments - the compare view
/// would sit idle behind a tab during the batch that fills it. The validation
/// bench and its grid and perturbation series open from the Stylometry
/// window, on the question it raises: should a Delta at these settings be
/// believed at all.
/// </summary>
public static class StylometryFeature
{
    /// <summary>
    /// Opens the Stylometry window, with the work in the reader selected if it
    /// has an original - what someone opening it from a text they are reading
    /// most likely means to analyse.
    /// </summary>
    public static void Open(IReaderHost host)
    {
        var window = new StylometryWindow(host.OpenWork?.WorkId)
        {
            OnOpenWork = host.OpenWorkAsync,
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    /// <summary>Opens Compare Saved Runs on the first settings profile with saved runs.</summary>
    public static void OpenCompareRuns(IReaderHost host) => ShowCompareRuns(host, null);

    /// <summary>
    /// Opens Compare Saved Runs focused on one saved run - its profile chosen,
    /// its author filtered to and its row selected - which is what the research
    /// bench does with a run attached to a project as evidence. A run deleted
    /// since is said to be gone, and the rest are shown.
    /// </summary>
    public static void OpenCompareRuns(IReaderHost host, long focusRunId) => ShowCompareRuns(host, focusRunId);

    private static void ShowCompareRuns(IReaderHost host, long? focusRunId)
    {
        var window = new StylometryAnalysisWindow(focusRunId)
        {
            OnOpenWork = host.OpenWorkAsync,
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }
}
