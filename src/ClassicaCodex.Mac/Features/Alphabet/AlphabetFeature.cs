using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Alphabet;

/// <summary>
/// Entry points for the alphabet reference, called from the Tools menu and
/// from the translation workbench.
/// </summary>
public static class AlphabetFeature
{
    /// <summary>
    /// Opens the reference for <paramref name="language"/> - the edition's
    /// code, "grc" or "lat". Anything else, or nothing, opens on the note that
    /// there is no chart for it, with the switch to Greek or Latin above.
    /// </summary>
    public static void Open(IReaderHost host, string? language)
    {
        var window = new AlphabetWindow(language);
        window.Show(host.Window);
    }
}
