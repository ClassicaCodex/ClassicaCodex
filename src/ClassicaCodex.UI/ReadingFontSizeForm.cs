namespace ClassicaCodex.UI;

/// <summary>
/// Chooses the sizes text is drawn at - Greek and Latin, and English
/// translations, linked by default.
///
/// Sliders with live samples rather than number boxes, because the question
/// being answered is "can I make out the breathing on that vowel", and the
/// only way to answer it is to look. The Greek sample carries a rough
/// breathing, an acute, a circumflex and an iota subscript - the four marks
/// that disappear first - so what is being judged is present in the preview
/// rather than implied by it.
///
/// Applies as the sliders move rather than on OK. Reading back and forth
/// between a slider and a sample is the whole task; making it modal on a
/// confirmation would mean guessing, confirming, and reopening.
/// </summary>
public class ReadingFontSizeForm : Form
{
    private readonly TrackBar _sourceSlider;
    private readonly TrackBar _translationSlider;
    private readonly CheckBox _linkCheckbox;
    private readonly Label _sourceSample;
    private readonly Label _translationSample;
    private readonly Label _sourceSizeLabel;
    private readonly Label _translationSizeLabel;

    private readonly float _sourceOnOpen;
    private readonly float _translationOnOpen;
    private readonly bool _linkedOnOpen;

    /// <summary>
    /// Guards against the feedback loop the link creates: moving the source
    /// slider changes the translation size, which updates the translation
    /// slider, whose ValueChanged would push the size back again.
    /// </summary>
    private bool _updating;

    public ReadingFontSizeForm()
    {
        _sourceOnOpen = ReadingFontSettings.SourceSize;
        _translationOnOpen = ReadingFontSettings.TranslationSize;
        _linkedOnOpen = ReadingFontSettings.Linked;

        Text = "Text size";
        AppIcons.ApplyWindowIcon(this, "FontSize");
        ClientSize = new Size(560, 470);
        MinimumSize = new Size(500, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var headerLabel = new Label
        {
            Text = "Reading text size",
            Left = 16, Top = 14, Width = 520, Height = 24,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold)
        };
        Controls.Add(headerLabel);

        _linkCheckbox = new CheckBox
        {
            Text = "Keep Greek, Latin and English the same size",
            Left = 16, Top = 42, Width = 520, Height = 24,
            Checked = ReadingFontSettings.Linked
        };
        _linkCheckbox.CheckedChanged += (_, _) =>
        {
            ReadingFontSettings.SetLinked(_linkCheckbox.Checked);
            SyncFromSettings();
        };
        Controls.Add(_linkCheckbox);

        var sourceLabel = new Label
        {
            Text = "Greek and Latin", Left = 16, Top = 76, Width = 300, Height = 20,
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
        };
        Controls.Add(sourceLabel);

        _sourceSample = new Label
        {
            // Rough breathing, acute, circumflex and iota subscript, in that
            // order - the marks that stop resolving first as size drops.
            Text = "ὥρᾳ μὲν ἦν τοῦ ἔτους",
            Left = 16, Top = 98, Width = 528, Height = 74,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_sourceSample);

        _sourceSizeLabel = new Label { Left = 16, Top = 178, Width = 120, Height = 20 };
        Controls.Add(_sourceSizeLabel);

        _sourceSlider = new TrackBar
        {
            Left = 14, Top = 198, Width = 532,
            Minimum = (int)ReadingFontSettings.MinimumSize,
            Maximum = (int)ReadingFontSettings.MaximumSize,
            TickFrequency = 2,
            Value = (int)Math.Round(ReadingFontSettings.SourceSize)
        };
        _sourceSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            ReadingFontSettings.SetSource(_sourceSlider.Value);
            SyncFromSettings();
        };
        Controls.Add(_sourceSlider);

        var translationLabel = new Label
        {
            Text = "English translations", Left = 16, Top = 250, Width = 300, Height = 20,
            Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold)
        };
        Controls.Add(translationLabel);

        _translationSample = new Label
        {
            Text = "May Zeus the suppliants' god look graciously upon our company",
            Left = 16, Top = 272, Width = 528, Height = 74,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_translationSample);

        _translationSizeLabel = new Label { Left = 16, Top = 352, Width = 120, Height = 20 };
        Controls.Add(_translationSizeLabel);

        _translationSlider = new TrackBar
        {
            Left = 14, Top = 372, Width = 532,
            Minimum = (int)ReadingFontSettings.MinimumSize,
            Maximum = (int)ReadingFontSettings.MaximumSize,
            TickFrequency = 2,
            Value = (int)Math.Round(ReadingFontSettings.TranslationSize)
        };
        _translationSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            ReadingFontSettings.SetTranslation(_translationSlider.Value);
            SyncFromSettings();
        };
        Controls.Add(_translationSlider);

        var okButton = new Button
        {
            Text = "OK", Left = 364, Top = 428, Width = 84, Height = 30,
            DialogResult = DialogResult.OK
        };
        Controls.Add(okButton);
        AcceptButton = okButton;

        // Cancel restores what was there on open, since the sizes have been
        // changing live the whole time the dialog was up.
        var cancelButton = new Button
        {
            Text = "Cancel", Left = 456, Top = 428, Width = 84, Height = 30,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Click += (_, _) =>
        {
            // Unlink first: with the link on, setting either size would drag
            // the other with it and the restore would not restore.
            ReadingFontSettings.SetLinked(false);
            ReadingFontSettings.SetSource(_sourceOnOpen);
            ReadingFontSettings.SetTranslation(_translationOnOpen);
            ReadingFontSettings.SetLinked(_linkedOnOpen);
        };
        Controls.Add(cancelButton);
        CancelButton = cancelButton;

        SyncFromSettings();

        ReadingTheme.AttachTo(this);
    }

    /// <summary>
    /// Pushes the stored sizes back onto both sliders and samples. Called
    /// after every change rather than updating only the control that was
    /// moved, because with the link on a change to one is a change to both.
    /// </summary>
    private void SyncFromSettings()
    {
        _updating = true;
        try
        {
            _sourceSlider.Value = (int)Math.Round(ReadingFontSettings.SourceSize);
            _translationSlider.Value = (int)Math.Round(ReadingFontSettings.TranslationSize);

            // Disabled rather than hidden when linked: the size is still
            // worth seeing, and a control that vanishes makes the checkbox
            // look like it removed a feature.
            _translationSlider.Enabled = !ReadingFontSettings.Linked;

            _sourceSample.Font = new Font("Palatino Linotype", ReadingFontSettings.SourceSize);
            _translationSample.Font = new Font("Georgia", ReadingFontSettings.TranslationSize);

            _sourceSizeLabel.Text = $"{ReadingFontSettings.SourceSize:0} point";
            _translationSizeLabel.Text = $"{ReadingFontSettings.TranslationSize:0} point";
        }
        finally
        {
            _updating = false;
        }
    }
}
