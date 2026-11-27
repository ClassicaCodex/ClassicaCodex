using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Artifacts;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.GoToCitation;

/// <summary>
/// Go to Citation: open the passage a citation names.
///
/// The Atlas of Ancient Myth and the reports built from this library copy a
/// citation when a reference is clicked - the edition's identifier and the
/// passage's reference, "tlg0020.tlg001.perseus-grc2 457" - so that it can be
/// looked up here. Books cite the other way, "Thuc. 2.40.1; Pl. Resp. 514a",
/// and a footnote pasted whole is read for every reference in it. This is
/// Go ▸ Go to Citation, with what is on the clipboard already filled in when
/// it cites anything, and Return to open it. A museum object the Atlas cites
/// ("artifact:London E 163") opens in the Art and Archaeology browser.
/// </summary>
public static class GoToCitationFeature
{
    /// <summary>Longer than this, the clipboard holds a passage of text, not a citation copied from a page.</summary>
    private const int LongestCitation = 300;

    /// <summary>Longer than this, the clipboard holds more than a footnote, and is not offered.</summary>
    private const int LongestFootnote = 3000;

    public static async Task OpenAsync(IReaderHost host)
    {
        var clipboard = await ClipboardText.GetAsync(host.Window);
        var initial = clipboard is { Length: <= LongestCitation } && CopiedCitation.Parse(clipboard) is { } copied && !copied.Identifier.EndsWith('.')
            ? copied.ToString()
            : string.Empty;
        var offered = initial.Length == 0 && clipboard is { Length: <= LongestFootnote } ? clipboard : null;

        var window = new GoToCitationWindow(
            new CopiedCitationResolver(WordStudy.DictionaryCitations.Arrange),
            result => OpenResultAsync(host, result),
            initial,
            offered);
        window.Show(host.Window);
    }

    private static async Task OpenResultAsync(IReaderHost host, object result)
    {
        switch (result)
        {
            case CitationLookupResult { Edition: { } edition, TextNodeId: { } passage }:
                await host.NavigateToPassageAsync(edition.WorkId, passage);
                break;

            case CitedArtifact artifact:
                ArtifactsFeature.Open(host.Window, artifact.Name, artifact.Name);
                break;
        }
    }
}
