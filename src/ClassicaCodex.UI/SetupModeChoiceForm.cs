namespace ClassicaCodex.UI;

/// <summary>
/// Shown when Setup Wizard is opened from the main toolbar, so a first-time
/// or non-technical user lands in the guided flow by default without the
/// choice being hidden from someone who already knows they want the
/// all-at-once view.
///
/// Returns DialogResult.Yes for Guided, DialogResult.No for Advanced, and
/// DialogResult.Cancel if closed without choosing either.
/// </summary>
public class SetupModeChoiceForm : ScaledForm
{
    public SetupModeChoiceForm()
    {
        Text = "Setup Wizard";
        AppIcons.ApplyWindowIcon(this, "Settings");
        ClientSize = new Size(460, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var intro = new Label
        {
            Text = "How would you like to set things up?",
            Left = 16,
            Top = 14,
            Width = 428,
            Height = 24,
            Font = new Font(Font, FontStyle.Bold)
        };

        var guidedIcon = new PictureBox { Left = 16, Top = 50, Width = 28, Height = 28, SizeMode = PictureBoxSizeMode.Zoom };
        guidedIcon.Image = AppIcons.Get("Help", 28);
        var guidedTitle = new Label
        {
            Text = "Guided Setup (recommended)",
            Left = 56,
            Top = 50,
            Width = 388,
            Height = 22,
            Font = new Font(Font, FontStyle.Bold)
        };
        var guidedDescription = new Label
        {
            Text = "One step at a time, plain language, no file paths or technical detail. The right " +
                   "choice for a first-time setup, or anyone who'd rather not think about where things go.",
            Left = 56,
            Top = 74,
            Width = 388,
            Height = 54
        };
        var guidedButton = new Button
        {
            Text = "Use Guided Setup",
            Left = 56,
            Top = 132,
            Width = 200,
            Height = 32,
            DialogResult = DialogResult.Yes
        };

        var advancedIcon = new PictureBox { Left = 16, Top = 190, Width = 28, Height = 28, SizeMode = PictureBoxSizeMode.Zoom };
        advancedIcon.Image = AppIcons.Get("Settings", 28);
        var advancedTitle = new Label
        {
            Text = "Advanced Setup",
            Left = 56,
            Top = 190,
            Width = 388,
            Height = 22,
            Font = new Font(Font, FontStyle.Bold)
        };
        var advancedDescription = new Label
        {
            Text = "Everything on one screen - repository URLs, destination folders, manual ingest. Better " +
                   "if you already have the files downloaded, or want more control over where they go.",
            Left = 56,
            Top = 214,
            Width = 388,
            Height = 54
        };
        var advancedButton = new Button
        {
            Text = "Use Advanced Setup",
            Left = 56,
            Top = 272,
            Width = 200,
            Height = 32,
            DialogResult = DialogResult.No
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 372,
            Top = 272,
            Width = 72,
            Height = 32,
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(intro);
        Controls.Add(guidedIcon);
        Controls.Add(guidedTitle);
        Controls.Add(guidedDescription);
        Controls.Add(guidedButton);
        Controls.Add(advancedIcon);
        Controls.Add(advancedTitle);
        Controls.Add(advancedDescription);
        Controls.Add(advancedButton);
        Controls.Add(cancelButton);

        AcceptButton = guidedButton;
        CancelButton = cancelButton;

        ReadingTheme.AttachTo(this);
    }
}
