using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// How an edition is named wherever its text is shown or written out.
///
/// This exists as one shared method rather than a string built at each call
/// site because export used to build its own. It emitted a bare "trans." with
/// no translator, which meant a published translation, a Gemini rendering and
/// the reader's own workbench translation all left the application looking
/// identical. Inside the app an AI translation is labelled everywhere - the
/// edition combo, Compare Translations, Work Details, the workbench panel -
/// and export was the one place that dropped it, which is also the one place
/// the text stops being something you are reading and becomes something you
/// are pasting into an essay.
///
/// Anything that renders an edition's provenance should call this, so that a
/// future label change reaches export too rather than only the screen.
/// </summary>
public static class EditionLabels
{
    /// <summary>
    /// The edition-specific part of a label - what distinguishes one edition
    /// of a work from another. "trans. Gemini (AI-generated)", "trans. Samuel
    /// Butler", "Greek (original)".
    ///
    /// Falls back to the trailing segment of the CTS URN when an edition
    /// carries no translator, which is the case for a good deal of the corpus.
    /// That is a weaker label but it is a true one; naming a translator that
    /// isn't in the data would be worse than being vague.
    /// </summary>
    public static string Descriptor(Edition edition)
    {
        if (edition.Kind == EditionKind.Original)
        {
            return edition.Language?.ToUpperInvariant() switch
            {
                "GRC" => "Greek (original)",
                "LAT" => "Latin (original)",
                not null => $"{edition.Language} (original)",
                null => "Original"
            };
        }

        if (edition.Kind == EditionKind.Translation && !string.IsNullOrWhiteSpace(edition.Translator))
        {
            return $"trans. {edition.Translator}";
        }

        // An edition ingest could not classify is named by its CTS version
        // identifier and called nothing more. Reaching the Original branch on
        // the strength of a null language used to label the notes volume
        // published with a text "Original" - in the translations dropdown,
        // which is where it appears - and calling it a translation instead
        // would only be a different wrong answer. The identifier is the one
        // thing here that is certainly true.
        var noun = edition.Kind == EditionKind.Translation ? "Translation" : "Other edition";
        var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(suffix) ? noun : $"{noun} ({suffix})";
    }
}
