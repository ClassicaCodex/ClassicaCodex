using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Instruments;

/// <summary>
/// The three instruments in the Instruments menu. Each opens a card rather
/// than the thing itself - the card says what the instrument is and how much
/// of it is reconstruction, and carries the button that opens its page in the
/// reader's browser. The third is the only one that can make a noise, and its
/// card says so before anything else.
/// </summary>
public static class InstrumentsFeature
{
    public static void OpenAntikythera(IReaderHost host) => new AntikytheraCard().Show(host.Window);

    public static void OpenAlmagest(IReaderHost host) => new AlmagestCard().Show(host.Window);

    public static void OpenHarmonics(IReaderHost host) => new HarmonicsCard().Show(host.Window);
}
