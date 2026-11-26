using ClassicaCodex.Core.Quotations;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.QuotedBy;

/// <summary>
/// The indirect tradition, line by line: which later authors quote a passage,
/// and in a quoting passage, which line it quotes.
///
/// The links come from two machine searches run over the library -
/// Testimonia and Afterlives - and ship with the application as references
/// and positions only (see QuotationIndex). Nothing here is an editor's
/// apparatus of testimonia: every link is shown with the search that found
/// it, how strong the match was, and how often that search was right when a
/// sample of its results was read.
/// </summary>
public static class QuotedByFeature
{
    /// <summary>
    /// How right the two searches were, measured, as the reader is told it.
    /// The README says the same in more words; keep the two in step.
    /// </summary>
    internal const string Accuracy =
        "Found by machine and checked by no one. Testimonia looks for an author named beside a word of citing " +
        "(“ut ait Vergilius”, ὡς Πλάτων φησίν), then for the line that shares at least two four-word runs with the " +
        "words around the name; in each of two development samples of 40 of its citations, 37 were genuine. " +
        "Afterlives matches Latin quotations of the classics by shared rare words and runs of words, whether or not " +
        "the source is named; a second AI read 40 of its high-rated quotations and judged 35 genuine. " +
        "Expect roughly one link in ten to be wrong.";

    /// <summary>Opens the later passages that quote this one, or says there are none.</summary>
    public static async Task OpenAsync(IReaderHost host, PassageContext passage)
    {
        var links = passage.Edition == null
            ? Array.Empty<QuotationLink>()
            : QuotationIndex.Current.QuotersOf(passage.Edition.CtsUrn, passage.Node.CitationRef);

        if (links.Count == 0)
        {
            await Alert.Info(host.Window, "Quoted By",
                "No later passage in the shipped quotation links quotes this line.\n\n" +
                "The links come from two machine searches, Testimonia and Afterlives, and they find a fraction of what " +
                "was quoted: Testimonia only where the author is named, Afterlives only for Latin. Lines marked “ at the " +
                "end have quoters.");
            return;
        }

        var window = new QuotedByWindow(
            passage.Node, passage.Edition!, links, EchoUi.ReadingFontFor(passage.Edition, passage.FromOriginal),
            host.OpenWork is { } work && passage.Edition!.WorkId == work.WorkId ? $"{host.AuthorNameFor(work)}, {work.Title}" : null)
        {
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    /// <summary>
    /// Goes to the line some quoted words come from - the link back from a
    /// quoting passage. The reader keeps Back to return to the quotation.
    /// </summary>
    public static async Task GoToSourceAsync(IReaderHost host, QuotedSpan span)
    {
        var passage = await new QuotationRepository().GetPassageAsync(span.Link.Source, span.Link.SourceFingerprint);
        if (passage == null)
        {
            await Alert.Info(host.Window, "Quoted passage not found",
                $"{span.Label} is not in this library, so there is nowhere to go. It may have been removed since the reader opened this text.");
            return;
        }

        await host.NavigateToPassageAsync(passage.Edition.WorkId, passage.TextNodeId);
    }
}
