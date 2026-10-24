using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Morphology;

/// <summary>
/// Entry points for the morphology search, called from the Tools menu, the
/// sidebar and the gallery.
/// </summary>
public static class MorphologyFeature
{
    /// <summary>Opens the search beside the reader; a passage it finds opens there.</summary>
    public static void Open(IReaderHost host)
    {
        var window = new MorphologyWindow { OnNavigate = host.NavigateToPassageAsync };
        window.Show(host.Window);
    }
}
