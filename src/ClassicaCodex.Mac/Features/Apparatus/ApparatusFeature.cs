using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Apparatus;

/// <summary>
/// Editor's Notes and the preface: the two windows that show what an edition
/// carries besides its text, both opened from the reader's line menu.
/// </summary>
public static class ApparatusFeature
{
    /// <summary>
    /// Opens the editor's notes for a passage.
    ///
    /// The edition comes from whichever page was clicked rather than from the
    /// original's page always: a translation carries its own notes, and
    /// Smyth's on Agamemnon are not Dindorf's.
    /// </summary>
    public static void Open(IReaderHost host, PassageContext passage)
    {
        var editionId = passage.Edition?.EditionId ?? passage.Node.EditionId;
        var label = string.IsNullOrWhiteSpace(passage.EditionLabel) ? "this edition" : passage.EditionLabel;

        var window = new EditorNotesWindow(editionId, passage.Node, label, passage.FontFamily)
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };
        window.Show(host.Window);
    }

    /// <summary>Shows a preface the reader held back from a page.</summary>
    public static void ShowPreface(IReaderHost host, string title, string text)
    {
        var window = new PrefaceWindow(title, text)
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };
        window.Show(host.Window);
    }
}
