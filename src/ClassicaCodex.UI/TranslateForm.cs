using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Opened from a reader pane's right-click menu, one step before Word Study.
/// Works from either pane: right-click the original and it looks toward the
/// translation; right-click the translation and it looks toward the
/// original instead. MainForm decides which counterpart edition and
/// language apply based on which pane was clicked - this form just acts on
/// whatever direction it's given.
///
/// Two independent sections, deliberately kept visually distinct:
///
///  - "Ingested Translation"/"Ingested Original" (the header names whichever
///    one is actually being looked for) - purely local. If the counterpart
///    edition is loaded in its pane, this looks up the same citation ref in
///    it via PassageAligner (the same alignment logic bilingual Export
///    already uses) and shows whatever it finds. No network involved; works
///    exactly like every other feature here.
///  - "AI Translation" - the one part of Classica Codex that isn't offline,
///    and the only place two providers exist side by side rather than one
///    replacing the other, because they have real, different tradeoffs:
///    Claude costs money with no free tier but doesn't train on API
///    traffic; Gemini has a genuine free tier with no card required, but
///    its free tier may use what's sent to it to improve Google's models.
///    Either needs its own API key (configured once, stored locally - see
///    TranslationSettings) and, unless turned off, asks for confirmation
///    before every single send. Translates in whichever direction the
///    counterpart language implies - into English from the original pane,
///    or into the work's original language from the translation pane.
/// </summary>
public class TranslateForm : Form
{
    private readonly TextNode _node;
    private readonly string? _sourceLanguage;
    private readonly string? _targetLanguage;
    private readonly string _authorName;
    private readonly string _workTitle;
    private readonly int? _counterpartEditionId;
    private readonly TextNodeRepository _textNodeRepo = new();

    // "Translation" when the counterpart is the translation pane's edition
    // (started from the original pane), "original" when it's the original
    // pane's edition (started from the translation pane) - drives every
    // direction-dependent label below. Passed in explicitly by MainForm
    // (which knows for certain which pane was clicked) rather than guessed
    // from a language code - a non-English translation edition would make
    // that guess wrong, even if none exists in this app yet.
    private readonly bool _counterpartIsTranslation;

    private readonly TextBox _originalBox;
    private readonly Label _ingestedHeader;
    private readonly Label _ingestedStatusLabel;
    private readonly TextBox _ingestedTranslationBox;
    private readonly Label _aiWarningLabel;
    private readonly Button _claudeButton;
    private readonly Button _geminiButton;
    private readonly Label _aiStatusLabel;
    private readonly TextBox _aiResultBox;

    public TranslateForm(
        TextNode node, string? sourceLanguage, string? targetLanguage, string authorName, string workTitle,
        int? counterpartEditionId, bool counterpartIsTranslation)
    {
        _node = node;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;
        _authorName = authorName;
        _workTitle = workTitle;
        _counterpartEditionId = counterpartEditionId;
        _counterpartIsTranslation = counterpartIsTranslation;

        Text = "Translate";
        AppIcons.ApplyWindowIcon(this, "Translate");
        Width = 640;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        var citationLabel = new Label
        {
            Left = 16,
            Top = 12,
            Width = 590,
            Text = $"{authorName}, {workTitle} \u2014 [{node.CitationRef}]"
        };

        var originalLabel = new Label { Left = 16, Top = 38, Width = 300, Text = "Selected passage:" };
        _originalBox = new TextBox
        {
            Left = 16,
            Top = 58,
            Width = 590,
            Height = 70,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = node.Text
        };

        _ingestedHeader = new Label
        {
            Left = 16,
            Top = 138,
            Width = 590,
            Font = new Font(Font, FontStyle.Bold),
            Text = _counterpartIsTranslation ? "Ingested Translation" : "Ingested Original"
        };
        _ingestedStatusLabel = new Label
        {
            Left = 16,
            Top = 160,
            Width = 590,
            Height = 48,
            ForeColor = Color.DimGray,
            Text = "Checking..."
        };
        _ingestedTranslationBox = new TextBox
        {
            Left = 16,
            Top = 212,
            Width = 590,
            Height = 62,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        var aiHeader = new Label
        {
            Left = 16,
            Top = 288,
            Width = 590,
            Font = new Font(Font, FontStyle.Bold),
            Text = "AI Translation"
        };
        _aiWarningLabel = new Label
        {
            Left = 16,
            Top = 310,
            Width = 590,
            Height = 34,
            ForeColor = Color.DarkRed,
            Text = "Sends this passage over the internet - the only part of Classica Codex that isn't " +
                   "offline. Nothing is sent unless you click one of the two buttons below."
        };

        _claudeButton = new Button { Left = 16, Top = 348, Width = 280, Height = 28 };
        _claudeButton.Click += async (_, _) => await OnAiButtonClickAsync(
            "Claude",
            () => TranslationSettings.AnthropicApiKey,
            (key, ct) => ClaudeTranslationService.TranslateAsync(
                _node.Text, _sourceLanguage, _targetLanguage, _authorName, _workTitle, _node.CitationRef, key, ct));

        _geminiButton = new Button { Left = 306, Top = 348, Width = 280, Height = 28 };
        _geminiButton.Click += async (_, _) => await OnAiButtonClickAsync(
            "Gemini",
            () => TranslationSettings.GeminiApiKey,
            (key, ct) => GeminiTranslationService.TranslateAsync(
                _node.Text, _sourceLanguage, _targetLanguage, _authorName, _workTitle, _node.CitationRef, key, ct));

        _aiStatusLabel = new Label { Left = 16, Top = 380, Width = 590, ForeColor = Color.DimGray };

        _aiResultBox = new TextBox
        {
            Left = 16,
            Top = 404,
            Width = 590,
            Height = 110,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        var closeButton = new Button
        {
            Text = "Close", Left = 526, Top = 528, Width = 80, DialogResult = DialogResult.Cancel
        };
        CancelButton = closeButton;

        Controls.Add(citationLabel);
        Controls.Add(originalLabel);
        Controls.Add(_originalBox);
        Controls.Add(_ingestedHeader);
        Controls.Add(_ingestedStatusLabel);
        Controls.Add(_ingestedTranslationBox);
        Controls.Add(aiHeader);
        Controls.Add(_aiWarningLabel);
        Controls.Add(_claudeButton);
        Controls.Add(_geminiButton);
        Controls.Add(_aiStatusLabel);
        Controls.Add(_aiResultBox);
        Controls.Add(closeButton);

        RefreshAiButtonStates();
        Load += async (_, _) => await LoadIngestedMatchAsync();
        ReadingTheme.AttachTo(this);
    }

    private async Task LoadIngestedMatchAsync()
    {
        var counterpartNoun = _counterpartIsTranslation ? "translation" : "original";

        if (_counterpartEditionId == null)
        {
            _ingestedStatusLabel.Text = $"(no {counterpartNoun} edition currently loaded for this work)";
            return;
        }

        var counterpartNodes = await _textNodeRepo.GetByEditionAsync(_counterpartEditionId.Value);
        var aligner = new PassageAligner(counterpartNodes);
        var match = aligner.ResolveMatch(_node.CitationRef);

        if (match == null)
        {
            _ingestedStatusLabel.Text = $"(no matching passage in the loaded {counterpartNoun})";
            return;
        }

        var (matchedRef, matchedText, matchedCount) = match.Value;

        if (matchedCount > 1)
        {
            // Several counterpart passages walked *down* from a coarser
            // query and joined - every one of them genuinely sits under the
            // citation asked for, so a long combined result here is exactly
            // what should happen, not a coincidence worth flagging. Saying
            // so is the actual fix for "the Latin breaks into one big
            // translation": now it's visible that the length comes from real
            // structure (many original sentences under one citation), not a
            // mismatch.
            _ingestedStatusLabel.Text =
                $"Found across {matchedCount} passages, starting at [{matchedRef}]:";
            _ingestedTranslationBox.Text = matchedText;
            return;
        }

        // A single exact-or-coarser hit is the case where two editions
        // could be numbering genuinely different things the same way by
        // coincidence - worth treating with suspicion if the lengths don't
        // match, since nothing in the citation refs themselves reveals that.
        var ratio = (double)Math.Max(matchedText.Length, 1) / Math.Max(_node.Text.Length, 1);
        var lengthLooksOff = ratio > 5.0 || ratio < 0.2;

        _ingestedStatusLabel.Text = lengthLooksOff
            ? $"Found at [{matchedRef}] - but its length differs a lot from the passage above. That " +
              "can be normal (a translation grouping several original lines under one citation), or a " +
              "sign the two editions number citations differently here. Compare the refs before trusting this."
            : $"Found at [{matchedRef}]:";
        _ingestedTranslationBox.Text = matchedText;
    }

    /// <summary>Each button's text and enabled state depend on whether that provider's key is configured yet.</summary>
    private void RefreshAiButtonStates()
    {
        var hasAnthropicKey = !string.IsNullOrWhiteSpace(TranslationSettings.AnthropicApiKey);
        _claudeButton.Text = hasAnthropicKey ? "Translate with Claude" : "Configure Claude Key...";
        _claudeButton.Enabled = true;
        AppIcons.Apply(_claudeButton, "Translate", 16);

        var hasGeminiKey = !string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey);
        _geminiButton.Text = hasGeminiKey ? "Translate with Gemini (free)" : "Configure Gemini Key...";
        _geminiButton.Enabled = true;
        AppIcons.Apply(_geminiButton, "Translate", 16);
    }

    /// <summary>
    /// Shared by both buttons - "no key yet" opens settings, otherwise
    /// confirms (unless turned off) and calls whichever provider's
    /// TranslateAsync was passed in.
    /// </summary>
    private async Task OnAiButtonClickAsync(
        string providerName, Func<string?> getKey, Func<string, CancellationToken, Task<string>> translate)
    {
        var key = getKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            using var settingsForm = new TranslateApiSettingsForm();
            settingsForm.ShowDialog(this);
            RefreshAiButtonStates();
            return;
        }

        if (TranslationSettings.AlwaysConfirmBeforeSending)
        {
            var confirmed = MessageBox.Show(this,
                $"This will send the selected passage (about {_node.Text.Length} characters) to " +
                $"{providerName}'s API over the internet, so it can be translated. This is the one " +
                "thing in Classica Codex that isn't offline.\n\n" +
                "Continue? (You can turn this confirmation off in AI Translation Settings.)",
                $"Send to {providerName}'s API?",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

            if (!confirmed) return;
        }

        _claudeButton.Enabled = false;
        _geminiButton.Enabled = false;
        _aiStatusLabel.ForeColor = Color.DimGray;
        _aiStatusLabel.Text = $"Translating with {providerName}...";
        _aiResultBox.Text = string.Empty;

        try
        {
            var translated = await translate(key, CancellationToken.None);
            _aiResultBox.Text = translated;
            _aiStatusLabel.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _aiStatusLabel.ForeColor = Color.DarkRed;
            _aiStatusLabel.Text = $"Couldn't translate with {providerName}: {ex.Message}";
        }
        finally
        {
            RefreshAiButtonStates();
        }
    }
}
