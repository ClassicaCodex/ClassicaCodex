namespace ClassicaCodex.UI;

/// <summary>
/// Shows a translator's preface (or similar front matter) that
/// PopulateReaderAsync held back from the reader pane, since it has nothing
/// on the other side to sync against and used to just sit at the top of the
/// list looking like the first line of the actual text. Opened from
/// "View Preface..." on the reader panes' right-click menu, only when one
/// is actually available for whatever edition is currently loaded.
///
/// Resizable rather than a fixed dialog, and otherwise built like HelpForm -
/// this is the same kind of content (a block of prose someone is reading,
/// not a small form to fill in), so it gets the same treatment.
/// </summary>
public class PrefaceForm : Form
{
    public PrefaceForm(string title, string prefaceText)
    {
        Text = title;
        AppIcons.ApplyWindowIcon(this, "Preface");
        Width = 760;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(480, 320);

        var textBox = new TextBox
        {
            Left = 12,
            Top = 12,
            Width = 720,
            Height = 560,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Georgia", 10.5F),
            WordWrap = true,
            Text = prefaceText.ReplaceLineEndings("\r\n")
        };

        var closeButton = new Button
        {
            Text = "Close",
            Left = 656,
            Top = 580,
            Width = 76,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        AcceptButton = closeButton;

        Controls.Add(textBox);
        Controls.Add(closeButton);

        ReadingTheme.AttachTo(this);

        WindowShortcuts.CloseOnEscape(this);
    }
}
