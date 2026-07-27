namespace ClassicaCodex.UI;

public class BookmarkPromptForm : Form
{
    private readonly TextBox _noteBox;

    public string? Note => string.IsNullOrWhiteSpace(_noteBox.Text) ? null : _noteBox.Text.Trim();

    public BookmarkPromptForm(string previewText)
    {
        Text = "Bookmark This Line";
        AppIcons.ApplyWindowIcon(this, "Bookmarks");
        Width = 520;
        Height = 320;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var previewLabel = new Label
        {
            Left = 16,
            Top = 14,
            Width = 480,
            Height = 50,
            ForeColor = Color.DimGray,
            Text = previewText.Length > 160 ? previewText[..160] + "..." : previewText
        };

        var noteLabel = new Label { Left = 16, Top = 76, Width = 300, Text = "Note (optional):" };
        _noteBox = new TextBox
        {
            Left = 16,
            Top = 100,
            Width = 480,
            Height = 120,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true
        };

        var okButton = new Button { Text = "Bookmark It", Left = 296, Top = 234, Width = 100, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 402, Top = 234, Width = 94, DialogResult = DialogResult.Cancel };
        AppIcons.Apply(okButton, "Bookmarks", 16);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(previewLabel);
        Controls.Add(noteLabel);
        Controls.Add(_noteBox);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        ReadingTheme.AttachTo(this);
    }
}
