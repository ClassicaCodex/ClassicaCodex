namespace ClassicaCodex.UI;

/// <summary>
/// A one-line "type a name" dialog.
///
/// WinForms has no equivalent of an input box, and the other prompts here -
/// BookmarkPromptForm and TagPromptForm - are each shaped around their own
/// job rather than being general. This is the plain version, used for naming
/// a hand-written translation.
/// </summary>
public class TextPromptForm : Form
{
    private readonly TextBox _input;

    public string Value => _input.Text.Trim();

    public TextPromptForm(string title, string prompt, string initial)
    {
        Text = title;
        AppIcons.ApplyWindowIcon(this, "Search");
        Width = 460;
        Height = 190;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label { Text = prompt, Left = 16, Top = 18, Width = 410 };

        _input = new TextBox { Left = 16, Top = 44, Width = 410, Text = initial };
        _input.SelectAll();

        var okButton = new Button
        {
            Text = "Save", Left = 246, Top = 88, Width = 88, Height = 30, DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Cancel", Left = 340, Top = 88, Width = 88, Height = 30, DialogResult = DialogResult.Cancel
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(label);
        Controls.Add(_input);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        // The text box rather than the default button, so typing starts
        // immediately - and with the suggestion selected, so replacing it is
        // one keystroke while keeping it is one click.
        Shown += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };

        ReadingTheme.AttachTo(this);
    }

    /// <summary>Null when cancelled or left blank.</summary>
    public static string? Ask(IWin32Window owner, string title, string prompt, string initial = "")
    {
        using var form = new TextPromptForm(title, prompt, initial);
        return form.ShowDialog(owner) == DialogResult.OK && form.Value.Length > 0 ? form.Value : null;
    }
}
