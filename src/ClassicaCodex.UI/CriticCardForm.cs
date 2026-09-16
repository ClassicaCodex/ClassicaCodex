using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.UI;

/// <summary>
/// Who a speaker is, and - the part that matters - whether they existed.
///
/// Opened by clicking a face or a name in the debate. The first line under the
/// portrait is the answer to the question a reader should be asking, in as
/// many words as it takes to be unambiguous: either this person is invented,
/// or they are real and every line they speak here carries the ancient
/// reference it came from.
/// </summary>
internal sealed class CriticCardForm : ScaledForm
{
    private const int PortraitSize = 96;

    public CriticCardForm(AncientCritic critic)
    {
        Text = critic.Name;
        AppIcons.ApplyWindowIcon(this, "Help");
        ClientSize = new Size(460, 430);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var portrait = new PictureBox
        {
            Left = 18,
            Top = 18,
            Width = PortraitSize,
            Height = PortraitSize,
            SizeMode = PictureBoxSizeMode.StretchImage,
            // Twice the displayed size: the portrait is drawn, not resampled,
            // so asking for more pixels costs nothing and a 200% display gets
            // a sharp one.
            Image = AncientAvatars.Portrait(critic, PortraitSize * 2)
        };

        var name = new Label
        {
            Text = critic.Name,
            Left = 130,
            Top = 20,
            Width = 310,
            Height = 30,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold)
        };

        var kind = new Label
        {
            Text = critic.Kind == CriticKind.Historical
                ? "A real person. Everything they say here is a paraphrase of something "
                  + "they actually wrote, and each of their turns carries the ancient "
                  + "reference so you can check it."
                : "Invented for this debate. Nobody of this name said any of this. They are "
                  + "here to voice the concerns of people who really did watch, read and "
                  + "argue about this work, and who left nothing in writing.",
            Left = 130,
            Top = 54,
            Width = 312,
            Height = 62,
            ForeColor = critic.Kind == CriticKind.Historical
                ? ReadingTheme.Text
                : ReadingTheme.WarningText
        };

        var facts = new Label
        {
            Text = string.Join("\r\n", new[]
                {
                    Line("Era", critic.Era),
                    Line("Active", critic.FloruitLabel),
                    Line("Place", critic.Place),
                    Line("Trade", critic.Role)
                }
                .Where(l => l != null)),
            Left = 18,
            Top = 132,
            Width = 424,
            Height = 82
        };

        var sketch = new Label
        {
            Text = critic.Sketch ?? string.Empty,
            Left = 18,
            Top = 222,
            Width = 424,
            Height = 96
        };

        var tastes = new Label
        {
            Text = string.IsNullOrWhiteSpace(critic.Tastes) ? string.Empty : "Cares about: " + critic.Tastes,
            Left = 18,
            Top = 320,
            Width = 424,
            Height = 58,
            ForeColor = ReadingTheme.MutedText
        };

        var close = new Button
        {
            Text = "Close",
            Left = 356,
            Top = 388,
            Width = 86,
            Height = 28,
            DialogResult = DialogResult.OK
        };

        Controls.AddRange(new Control[] { portrait, name, kind, facts, sketch, tastes, close });
        AcceptButton = close;
        CancelButton = close;

        ReadingTheme.AttachTo(this, () =>
        {
            kind.ForeColor = critic.Kind == CriticKind.Historical
                ? ReadingTheme.Text
                : ReadingTheme.WarningText;
            tastes.ForeColor = ReadingTheme.MutedText;
        });
    }

    private static string? Line(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{label}:  {value}";
}
