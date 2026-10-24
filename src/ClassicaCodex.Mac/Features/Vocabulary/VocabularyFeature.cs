using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Vocabulary;

/// <summary>
/// Entry points for Core Vocabulary, called from the library's work menu,
/// the Tools menu, the sidebar and the gallery.
/// </summary>
public static class VocabularyFeature
{
    /// <summary>
    /// Opens the vocabulary list for a work's original-language text.
    ///
    /// The original, never a translation: the list ranks headwords from the
    /// Greek and Latin lemma data, so a work with only translations loaded
    /// has nothing it can count, and says so rather than opening an empty
    /// table.
    /// </summary>
    public static async Task OpenAsync(IReaderHost host, Work work)
    {
        try
        {
            var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
            var original = editions.FirstOrDefault(e => e.Kind == EditionKind.Original);

            if (original == null)
            {
                await Alert.Info(host.Window, "Core Vocabulary",
                    "This work has no original-language text loaded, so there is no Greek or Latin "
                    + "vocabulary to count.");
                return;
            }

            var window = new VocabularyWindow(work, host.AuthorNameFor(work), original.EditionId, original.Language);
            window.Show(host.Window);
        }
        catch (Exception ex)
        {
            await Alert.Error(host.Window, "Core Vocabulary", $"Couldn't open the vocabulary list: {ex.Message}");
        }
    }
}
