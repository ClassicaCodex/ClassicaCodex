using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.DictionaryCitations;

/// <summary>
/// The dictionaries from the passage's side: which LSJ and Lewis and Short
/// entries cite a line, under which sense, and what they say there - a
/// lexicographer's commentary on the line, read off the dictionaries' own
/// citations.
/// </summary>
public static class DictionaryCitationsFeature
{
    /// <summary>
    /// How far the link from a citation to a line can be trusted, measured, as
    /// the reader is told it. The README says the same in more words; keep the
    /// two in step.
    /// </summary>
    internal const string Accuracy =
        "The citations are the dictionaries' own, as the Perseus Digital Library marked them up. Each was matched to " +
        "this line by its reference, which the dictionaries and the library's editions often number differently; " +
        "of 40 matched citations read against the passages they open, 37 opened the one the dictionary quotes, and " +
        "the other three a section or chapter away, where the edition divides the text differently. Where a " +
        "dictionary cites a chapter, the chapter's first line is the one marked.";

    /// <summary>Opens the entries that cite this line, or says why there are none.</summary>
    public static async Task OpenAsync(IReaderHost host, PassageContext passage, IReadOnlyList<long> citationIds)
    {
        if (citationIds.Count == 0)
        {
            var kept = await Task.Run(() => new LexiconCitationRepository().AnyAsync());
            await Alert.Info(host.Window, "Cited in the Dictionaries",
                kept
                    ? "Neither LSJ nor Lewis & Short cites this line, as far as their citations can be matched to this edition. " +
                      "Lines marked § at the end are cited."
                    : "The dictionaries in this library were loaded before their citations were kept. Run the Dictionaries " +
                      "step in Setup again, and the lines LSJ and Lewis & Short cite will be marked §.");
            return;
        }

        var window = new DictionaryCitationsWindow(
            passage.Node, passage.Edition, citationIds, EchoUi.ReadingFontFor(passage.Edition, passage.FromOriginal),
            host.OpenWork is { } work && passage.Edition?.WorkId == work.WorkId ? $"{host.AuthorNameFor(work)}, {work.Title}" : null);
        window.Show(host.Window);
    }
}
