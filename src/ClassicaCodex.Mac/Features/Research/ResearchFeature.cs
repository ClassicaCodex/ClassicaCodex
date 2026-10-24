using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class ResearchFeature
{
    public static void OpenBench(IReaderHost host, Work work, string authorName, long? projectId = null) =>
        ComingSoon.Show(host, "Research Bench", "research", "Research");

    public static Task<bool> StartInquiryAsync(IReaderHost host, PassageContext passage)
    {
        ComingSoon.Show(host, "Passage Inquiry", "research", "Inquiry");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Saves a set of echo candidates to a research project for human review -
    /// the Windows "Save echo investigation" dialog. Opened from Find Echoes,
    /// Cross-Language Echo and the Corpus Investigator.
    /// </summary>
    /// <returns>True if the reader saved them.</returns>
    public static Task<bool> CaptureEchoAsync(Avalonia.Controls.Window owner, ClassicaCodex.Core.EchoCaptureRequest request, long? defaultProjectId = null)
    {
        if (MainWindow.Current is { } host) ComingSoon.Show(host, "Save Echo Investigation", "research", "Research");
        return Task.FromResult(false);
    }
}
