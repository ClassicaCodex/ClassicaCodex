namespace ClassicaCodex.UI;

/// <summary>
/// Keyboard shortcuts shared across windows.
///
/// This application had none at all - 68 hand-built forms and not one
/// accelerator between them - which is a speed problem for anyone who works
/// in it daily and an accessibility problem for anyone who cannot easily use
/// a mouse. The conventions here are the ones every Windows application
/// already uses, so nothing has to be learned: Escape closes, F1 helps,
/// Ctrl+F finds.
/// </summary>
public static class WindowShortcuts
{
    /// <summary>
    /// Closes a window on Escape.
    ///
    /// Wired through a KeyDown handler with KeyPreview rather than by setting
    /// CancelButton, because most of these windows have no Cancel to point
    /// at - they are tool windows with a single Close, or none at all. The
    /// effect is the same and it does not require inventing a button.
    ///
    /// Deliberately NOT applied to windows that are doing something: ingest,
    /// lemma loading, and the two setup wizards. Escape is pressed absently,
    /// and on those it would abandon work halfway through rather than
    /// dismiss a view of something.
    /// </summary>
    public static void CloseOnEscape(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;

            // A combining key means something else is intended - Escape
            // alone is the close, and Shift+Escape is not a synonym for it.
            if (e.Modifiers != Keys.None) return;

            e.Handled = true;
            form.Close();
        };
    }
}
