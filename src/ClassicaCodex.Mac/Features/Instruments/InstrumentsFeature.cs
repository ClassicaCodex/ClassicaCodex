using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Instruments;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class InstrumentsFeature
{
    public static void OpenAntikythera(IReaderHost host) =>
        ComingSoon.Show(host, "The Antikythera Mechanism", "antikythera", "Antikythera");

    public static void OpenAlmagest(IReaderHost host) =>
        ComingSoon.Show(host, "Ptolemy's Cosmos", "almagest", "Almagest");

    public static void OpenHarmonics(IReaderHost host) =>
        ComingSoon.Show(host, "Ptolemy's Harmony", "harmonics", "Harmonics");
}
