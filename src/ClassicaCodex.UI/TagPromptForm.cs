namespace ClassicaCodex.UI;

public class TagPromptForm : ScaledForm
{
    private readonly TextBox _nameBox;
    private readonly TextBox _categoryBox;

    public string TagName => _nameBox.Text.Trim();
    public string? Category => string.IsNullOrWhiteSpace(_categoryBox.Text) ? null : _categoryBox.Text.Trim();

    public TagPromptForm(string previewText)
    {
        Text = "Tag This Line";
        AppIcons.ApplyWindowIcon(this, "AutoTag");
        Width = 520;
        Height = 300;
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

        var nameLabel = new Label { Left = 16, Top = 76, Width = 300, Text = "Tag name (e.g. \"Prometheus\"):" };
        _nameBox = new TextBox { Left = 16, Top = 100, Width = 480 };

        var categoryLabel = new Label
        {
            Left = 16,
            Top = 136,
            Width = 480,
            Text = "Category (optional - e.g. \"character\", \"theme\"):"
        };
        _categoryBox = new TextBox { Left = 16, Top = 160, Width = 480 };

        var okButton = new Button { Text = "Tag It", Left = 316, Top = 210, Width = 90, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 412, Top = 210, Width = 84, DialogResult = DialogResult.Cancel };
        AppIcons.Apply(okButton, "AutoTag", 16);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.Add(previewLabel);
        Controls.Add(nameLabel);
        Controls.Add(_nameBox);
        Controls.Add(categoryLabel);
        Controls.Add(_categoryBox);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        ReadingTheme.AttachTo(this);
    }
}
