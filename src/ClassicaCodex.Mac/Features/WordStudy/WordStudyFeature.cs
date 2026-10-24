using Avalonia.Media;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// Entry points for Word Study, called from the reader's line menu and from
/// the translation workbench.
/// </summary>
public static class WordStudyFeature
{
    /// <summary>Opens Word Study on a passage the reader right-clicked, with no word chosen yet.</summary>
    public static void Open(IReaderHost host, PassageContext passage) => Open(host, passage, selectedWord: null);

    /// <summary>
    /// Opens Word Study on a passage with one of its words already chosen -
    /// what the translation workbench does with the word selected in its own
    /// list.
    ///
    /// The language is the edition's, because which pane a line came from
    /// decides it and the word cannot: an English translation and a Latin
    /// original are both written in the Latin alphabet. Occurrences start
    /// scoped to the work being read, as they do from both places on Windows
    /// - the corpus-wide count for a common word stops at the result limit
    /// and says only that the word is common. Choose Texts widens it.
    /// </summary>
    public static void Open(IReaderHost host, PassageContext passage, string? selectedWord)
    {
        var workId = passage.Edition?.WorkId ?? host.OpenWork?.WorkId;

        var window = new WordStudyWindow(passage.Node, passage.Edition?.Language, workId, selectedWord, ReadingFontFor(passage))
        {
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    /// <summary>
    /// The face the pane drew the line in, then the palette's reading face for
    /// that side - the pane reports only its primary family, which may not be
    /// installed everywhere - and the script's own face in front for Hebrew,
    /// cuneiform and the rest.
    /// </summary>
    private static FontFamily ReadingFontFor(PassageContext passage)
    {
        var palette = ToolPane.Font(passage.FromOriginal ? "Cc.SourceFont" : "Cc.TranslationFont");
        var family = string.IsNullOrWhiteSpace(passage.FontFamily)
            ? palette
            : new FontFamily($"{passage.FontFamily}, {palette}");

        return ScriptFonts.Compose(family, ScriptFonts.FamilyFor(passage.Node.Text));
    }
}
