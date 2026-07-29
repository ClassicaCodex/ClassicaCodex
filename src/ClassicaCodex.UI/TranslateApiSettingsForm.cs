using System.Diagnostics;

namespace ClassicaCodex.UI;

/// <summary>
/// Configures both providers Translate's AI option can use, and whether to
/// keep asking for confirmation before every send. Opened either from
/// TranslateForm the first time (no key yet for whichever provider was
/// clicked) or on demand afterward.
/// </summary>
public class TranslateApiSettingsForm : Form
{
    private readonly TextBox _anthropicKeyBox;
    private readonly TextBox _geminiKeyBox;
    private readonly CheckBox _alwaysConfirmCheckbox;

    public TranslateApiSettingsForm()
    {
        Text = "AI Translation Settings";
        AppIcons.ApplyWindowIcon(this, "Settings");
        Width = 560;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var explainLabel = new Label
        {
            Left = 16,
            Top = 14,
            Width = 520,
            Height = 34,
            ForeColor = Color.DimGray,
            Text = "The rest of Classica Codex works entirely offline. Translate's AI option is the " +
                   "one exception, and it can use either of two providers below - set up whichever suits."
        };

        // --- Anthropic (Claude) ---
        var anthropicHeader = new Label
        {
            Left = 16, Top = 54, Width = 520,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Anthropic (Claude)"
        };
        var anthropicExplain = new Label
        {
            Left = 16, Top = 74, Width = 520, Height = 50,
            ForeColor = Color.DimGray,
            Text = "A separate developer account from claude.ai or a Claude Pro/Max subscription - " +
                   "even if you already pay for one of those, this needs its own setup. Requires a " +
                   "payment method; no ongoing free tier, though a single passage costs a small " +
                   "fraction of a cent."
        };
        var anthropicLink = new LinkLabel { Left = 16, Top = 126, Width = 300, Text = "Get an API key \u2192" };
        anthropicLink.LinkClicked += (_, _) => OpenUrl("https://console.anthropic.com");
        var anthropicPricingLink = new LinkLabel
        {
            Left = 16, Top = 146, Width = 300, Text = "See current pricing \u2192"
        };
        anthropicPricingLink.LinkClicked += (_, _) => OpenUrl("https://www.anthropic.com/pricing");

        var anthropicKeyLabel = new Label { Left = 16, Top = 176, Width = 300, Text = "Anthropic API key:" };
        _anthropicKeyBox = new TextBox
        {
            Left = 16, Top = 198, Width = 520,
            UseSystemPasswordChar = true,
            Text = TranslationSettings.AnthropicApiKey ?? string.Empty
        };
        var showAnthropicKeyCheckbox = new CheckBox { Left = 16, Top = 224, Width = 200, Text = "Show key" };
        showAnthropicKeyCheckbox.CheckedChanged += (_, _) =>
            _anthropicKeyBox.UseSystemPasswordChar = !showAnthropicKeyCheckbox.Checked;
        var clearAnthropicButton = new Button { Text = "Remove Key", Left = 226, Top = 220, Width = 110 };
        clearAnthropicButton.Click += (_, _) => _anthropicKeyBox.Text = string.Empty;

        // --- Google (Gemini) ---
        var geminiHeader = new Label
        {
            Left = 16, Top = 262, Width = 520,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Google (Gemini) - free, no card required"
        };
        var geminiExplain = new Label
        {
            Left = 16, Top = 282, Width = 520, Height = 50,
            ForeColor = Color.DimGray,
            Text = "A genuinely free tier through Google AI Studio - no payment method, no expiration. " +
                   "The tradeoff: Google's free tier may use what you send it to improve their models, " +
                   "so \"free\" isn't the same guarantee as \"private\" here."
        };
        var geminiLink = new LinkLabel { Left = 16, Top = 334, Width = 300, Text = "Get a free API key \u2192" };
        geminiLink.LinkClicked += (_, _) => OpenUrl("https://aistudio.google.com/app/apikey");

        var geminiKeyLabel = new Label { Left = 16, Top = 360, Width = 300, Text = "Google AI Studio API key:" };
        _geminiKeyBox = new TextBox
        {
            Left = 16, Top = 382, Width = 520,
            UseSystemPasswordChar = true,
            Text = TranslationSettings.GeminiApiKey ?? string.Empty
        };
        var showGeminiKeyCheckbox = new CheckBox { Left = 16, Top = 408, Width = 200, Text = "Show key" };
        showGeminiKeyCheckbox.CheckedChanged += (_, _) =>
            _geminiKeyBox.UseSystemPasswordChar = !showGeminiKeyCheckbox.Checked;
        var clearGeminiButton = new Button { Text = "Remove Key", Left = 226, Top = 404, Width = 110 };
        clearGeminiButton.Click += (_, _) => _geminiKeyBox.Text = string.Empty;

        _alwaysConfirmCheckbox = new CheckBox
        {
            Left = 16, Top = 444, Width = 520,
            Checked = TranslationSettings.AlwaysConfirmBeforeSending,
            Text = "Ask for confirmation every time before sending a passage (either provider)"
        };

        var saveButton = new Button
        {
            Text = "Save", Left = 336, Top = 480, Width = 90, DialogResult = DialogResult.OK
        };
        saveButton.Click += (_, _) => TranslationSettings.Save(
            _anthropicKeyBox.Text, _geminiKeyBox.Text, _alwaysConfirmCheckbox.Checked);
        var cancelButton = new Button
        {
            Text = "Cancel", Left = 432, Top = 480, Width = 90, DialogResult = DialogResult.Cancel
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.Add(explainLabel);
        Controls.Add(anthropicHeader);
        Controls.Add(anthropicExplain);
        Controls.Add(anthropicLink);
        Controls.Add(anthropicPricingLink);
        Controls.Add(anthropicKeyLabel);
        Controls.Add(_anthropicKeyBox);
        Controls.Add(showAnthropicKeyCheckbox);
        Controls.Add(clearAnthropicButton);
        Controls.Add(geminiHeader);
        Controls.Add(geminiExplain);
        Controls.Add(geminiLink);
        Controls.Add(geminiKeyLabel);
        Controls.Add(_geminiKeyBox);
        Controls.Add(showGeminiKeyCheckbox);
        Controls.Add(clearGeminiButton);
        Controls.Add(_alwaysConfirmCheckbox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        ReadingTheme.AttachTo(this);
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* if the shell can't open it, there's nothing more useful to do here */ }
    }
}
