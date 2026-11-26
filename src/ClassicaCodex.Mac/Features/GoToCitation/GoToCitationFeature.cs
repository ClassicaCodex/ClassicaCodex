using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Artifacts;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.GoToCitation;

/// <summary>
/// Go to Citation: open the passage a copied citation names.
///
/// The Atlas of Ancient Myth and the reports built from this library copy a
/// citation when a reference is clicked - the edition's identifier and the
/// passage's reference, "tlg0020.tlg001.perseus-grc2 457" - so that it can be
/// looked up here. This is the other half: Go ▸ Go to Citation, with what is
/// on the clipboard already filled in when it reads as a citation, and Return
/// to open it. A museum object the Atlas cites ("artifact:London E 163")
/// opens in the Art and Archaeology browser.
/// </summary>
public static class GoToCitationFeature
{
    /// <summary>Longer than this, the clipboard holds a passage of text, not a citation copied from a page.</summary>
    private const int LongestCitation = 300;

    public static async Task OpenAsync(IReaderHost host)
    {
        var clipboard = await ClipboardText.GetAsync(host.Window);
        var initial = clipboard is { Length: <= LongestCitation } && CopiedCitation.Parse(clipboard) is { } copied
            ? copied.ToString()
            : string.Empty;

        var window = new GoToCitationWindow(new CopiedCitationResolver(WordStudy.DictionaryCitations.Arrange), initial);
        switch (await window.ShowDialog<object?>(host.Window))
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
