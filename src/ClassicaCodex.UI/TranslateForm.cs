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
/// Three independent sections, deliberately kept visually distinct:
///
///  - "Listen" - reads the passage aloud with whatever voice is installed
///    on this Windows machine (SpeechService). Fully offline, no
///    confirmation needed - nothing here ever touches the network. Greek
///    text is transliterated to a rough Latin spelling first
///    (GreekPhoneticTransliterator), since a stock voice reads raw Greek
///    script by naming each Unicode character rather than attempting to say
///    it - confirmed on a real machine, not a guess.
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
    private readonly ComboBox _voiceComboBox;
    private readonly Button _readAloudButton;
    private readonly Button _stopReadingButton;
    private readonly Label _listenStatusLabel;
    private readonly System.Windows.Forms.Timer _listenPollTimer;
    private readonly Label _ingestedHeader;
    private readonly Label _ingestedStatusLabel;
    private readonly TextBox _ingestedTranslationBox;
    private readonly Label _aiWarningLabel;
    private readonly Button _claudeButton;
    private readonly Button _geminiButton;
    private readonly Label _aiStatusLabel;
    private readonly TextBox _aiResultBox;
    private readonly Button _copyButton;

    // One-shot, purely so the Copy button can say "Copied" for a moment and
    // then go back to normal. A clipboard write that succeeds silently is
    // indistinguishable from one that did nothing.
    private readonly System.Windows.Forms.Timer _copyFeedbackTimer;

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
        Height = 712;

        // The layout below is absolute, so this is the size everything was
        // positioned against - shrinking past it makes controls overlap
        // rather than reflow. Growing past it is handled by the anchors.
        MinimumSize = new Size(640, 712);
        StartPosition = FormStartPosition.CenterParent;

        var citationLabel = new Label
        {
            Left = 16,
            Top = 12,
            Width = 590,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = $"{authorName}, {workTitle} \u2014 [{node.CitationRef}]"
        };

        var originalLabel = new Label { Left = 16, Top = 38, Width = 300, Text = "Selected passage:" };
        _originalBox = new TextBox
        {
            Left = 16,
            Top = 58,
            Width = 590,
            Height = 70,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = node.Text
        };

        var listenHeader = new Label
        {
            Left = 16,
            Top = 138,
            Width = 590,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Listen"
        };
        var voiceLabel = new Label { Left = 16, Top = 163, Width = 50, Text = "Voice:" };
        _voiceComboBox = new ComboBox
        {
            Left = 68,
            Top = 159,
            Width = 340,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _voiceComboBox.SelectedIndexChanged += (_, _) => OnVoiceSelectionChanged();

        var moreVoicesLink = new LinkLabel
        {
            Left = 418,
            Top = 162,
            Width = 188,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = "Get more voices..."
        };
        moreVoicesLink.LinkClicked += (_, _) => OpenWindowsSpeechSettings();

        _readAloudButton = new Button { Left = 16, Top = 190, Width = 180, Height = 28, Text = "\u25B6 Read Aloud" };
        AppIcons.Apply(_readAloudButton, "Pronunciation", 16);
        _readAloudButton.Click += (_, _) => OnReadAloudClicked();

        _stopReadingButton = new Button
        {
            Left = 204, Top = 190, Width = 100, Height = 28, Text = "\u25A0 Stop", Enabled = false
        };
        _stopReadingButton.Click += (_, _) => SpeechService.Stop();

        _listenStatusLabel = new Label
        {
            Left = 312,
            Top = 195,
            Width = 294,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };

        // Polls rather than subscribing to SpeechSynthesizer's own
        // SpeakCompleted event - that event fires on a background thread,
        // and marshaling it back to the UI thread correctly is more
        // plumbing than a simple poll needs for something this low-stakes.
        // 300ms is frequent enough that the buttons feel responsive without
        // meaningfully taxing anything.
        _listenPollTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _listenPollTimer.Tick += (_, _) => RefreshListenButtonStates();

        _ingestedHeader = new Label
        {
            Left = 16,
            Top = 230,
            Width = 590,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font, FontStyle.Bold),
            Text = _counterpartIsTranslation ? "Ingested Translation" : "Ingested Original"
        };
        _ingestedStatusLabel = new Label
        {
            Left = 16,
            Top = 252,
            Width = 590,
            Height = 48,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray,
            Text = "Checking..."
        };
        _ingestedTranslationBox = new TextBox
        {
            Left = 16,
            Top = 304,
            Width = 590,
            Height = 62,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        var aiHeader = new Label
        {
            Left = 16,
            Top = 380,
            Width = 590,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font, FontStyle.Bold),
            Text = "AI Translation"
        };
        _aiWarningLabel = new Label
        {
            Left = 16,
            Top = 402,
            Width = 590,
            Height = 34,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DarkRed,
            Text = "Sends this passage over the internet - the only part of Classica Codex that isn't " +
                   "offline. Nothing is sent unless you click one of the two buttons below."
        };

        _claudeButton = new Button { Left = 16, Top = 440, Width = 280, Height = 28 };
        _claudeButton.Click += async (_, _) => await OnAiButtonClickAsync(
            "Claude",
            () => TranslationSettings.AnthropicApiKey,
            (key, ct) => ClaudeTranslationService.TranslateAsync(
                _node.Text, _sourceLanguage, _targetLanguage, _authorName, _workTitle, _node.CitationRef, key, ct));

        _geminiButton = new Button { Left = 306, Top = 440, Width = 280, Height = 28 };
        _geminiButton.Click += async (_, _) => await OnAiButtonClickAsync(
            "Gemini",
            () => TranslationSettings.GeminiApiKey,
            (key, ct) => GeminiTranslationService.TranslateAsync(
                _node.Text, _sourceLanguage, _targetLanguage, _authorName, _workTitle, _node.CitationRef, key, ct));

        _aiStatusLabel = new Label
        {
            Left = 16,
            Top = 472,
            Width = 590,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };

        // The one control that takes the extra height. Anchoring a second
        // box vertically as well would just make the two overlap - this
        // layout is absolute, so nothing below a growing control moves out
        // of its way. This is the box worth growing: it holds generated
        // prose, which runs long, and it's the only text here that exists
        // nowhere else.
        _aiResultBox = new TextBox
        {
            Left = 16,
            Top = 496,
            Width = 590,
            Height = 110,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        _copyButton = new Button
        {
            Text = "Copy to Clipboard",
            Left = 16,
            Top = 620,
            Width = 160,
            Height = 23,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false
        };
        _copyButton.Click += (_, _) => CopyTranslationToClipboard();

        _copyFeedbackTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _copyFeedbackTimer.Tick += (_, _) =>
        {
            _copyFeedbackTimer.Stop();
            _copyButton.Text = "Copy to Clipboard";
        };

        // Driven by the boxes themselves rather than by each place that
        // writes to them - LoadIngestedMatchAsync alone has four separate
        // exit paths, and a fifth added later would silently miss this.
        _ingestedTranslationBox.TextChanged += (_, _) => RefreshCopyButtonState();
        _aiResultBox.TextChanged += (_, _) => RefreshCopyButtonState();

        var closeButton = new Button
        {
            Text = "Close",
            Left = 526,
            Top = 620,
            Width = 80,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        CancelButton = closeButton;

        Controls.Add(citationLabel);
        Controls.Add(originalLabel);
        Controls.Add(_originalBox);
        Controls.Add(listenHeader);
        Controls.Add(voiceLabel);
        Controls.Add(_voiceComboBox);
        Controls.Add(moreVoicesLink);
        Controls.Add(_readAloudButton);
        Controls.Add(_stopReadingButton);
        Controls.Add(_listenStatusLabel);
        Controls.Add(_ingestedHeader);
        Controls.Add(_ingestedStatusLabel);
        Controls.Add(_ingestedTranslationBox);
        Controls.Add(aiHeader);
        Controls.Add(_aiWarningLabel);
        Controls.Add(_claudeButton);
        Controls.Add(_geminiButton);
        Controls.Add(_aiStatusLabel);
        Controls.Add(_aiResultBox);
        Controls.Add(_copyButton);
        Controls.Add(closeButton);

        RefreshAiButtonStates();
        LoadVoiceList();
        Load += async (_, _) => await LoadIngestedMatchAsync();

        // Speech shouldn't keep playing after the dialog that started it is
        // gone, and the poll timer has nothing left to watch once the form
        // is closed either way.
        FormClosed += (_, _) =>
        {
            _listenPollTimer.Stop();
            _listenPollTimer.Dispose();
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Dispose();
            SpeechService.Stop();
        };

        ReadingTheme.AttachTo(this);
    }

    private void LoadVoiceList()
    {
        var voices = SpeechService.GetInstalledVoices();
        if (voices.Count == 0)
        {
            _voiceComboBox.Enabled = false;
            _readAloudButton.Enabled = false;
            _listenStatusLabel.ForeColor = Color.DarkRed;
            _listenStatusLabel.Text = "No speech voice found on this computer.";
            return;
        }

        var preferredName = TranslationSettingsSafeGetPreferredVoice();
        foreach (var voice in voices)
        {
            _voiceComboBox.Items.Add(new VoiceOption(voice));
        }

        var selected = preferredName != null
            ? _voiceComboBox.Items.Cast<VoiceOption>().FirstOrDefault(v => v.Voice.Name == preferredName)
            : null;
        _voiceComboBox.SelectedItem = selected ?? _voiceComboBox.Items[0];
    }

    // Small indirection so a missing/corrupt settings file can't throw out
    // of the constructor path.
    private static string? TranslationSettingsSafeGetPreferredVoice()
    {
        try { return SpeechSettings.PreferredVoiceName; }
        catch { return null; }
    }

    private void OnVoiceSelectionChanged()
    {
        if (_voiceComboBox.SelectedItem is VoiceOption option)
        {
            SpeechService.SetVoice(option.Voice.Name);
        }
    }

    private void OnReadAloudClicked()
    {
        SpeechService.Speak(_node.Text, _sourceLanguage);
        _listenPollTimer.Start();
        RefreshListenButtonStates();
    }

    private void RefreshListenButtonStates()
    {
        var speaking = SpeechService.IsSpeaking;
        _stopReadingButton.Enabled = speaking;
        _listenStatusLabel.ForeColor = Color.DimGray;
        _listenStatusLabel.Text = speaking ? "Speaking..." : string.Empty;

        if (!speaking) _listenPollTimer.Stop();
    }

    private void OpenWindowsSpeechSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("ms-settings:speech") { UseShellExecute = true });
        }
        catch
        {
            // If the specific settings page URI isn't recognized on this
            // Windows build, there's nothing more useful to do than leave
            // the link inert - not worth a dialog over.
        }
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

    private void RefreshCopyButtonState() =>
        _copyButton.Enabled = !string.IsNullOrWhiteSpace(_aiResultBox.Text)
                              || !string.IsNullOrWhiteSpace(_ingestedTranslationBox.Text);

    /// <summary>
    /// Copies whichever translation is on screen. The AI result wins when
    /// both are present: it's what was just generated and what the reader is
    /// looking at, and unlike the ingested passage it exists nowhere else -
    /// that one is already sitting in the reader pane behind this dialog,
    /// with its own copy option on the right-click menu.
    /// </summary>
    private void CopyTranslationToClipboard()
    {
        var text = !string.IsNullOrWhiteSpace(_aiResultBox.Text)
            ? _aiResultBox.Text
            : _ingestedTranslationBox.Text;

        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Clipboard.SetText(text);

            _copyButton.Text = "Copied";
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The Windows clipboard is a shared, singly-locked resource -
            // another process holding it open makes this fail for a moment
            // through no fault of anything here. Say so rather than letting
            // a silent no-op look like a successful copy.
            _copyButton.Text = "Clipboard busy";
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Start();
        }
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

    private class VoiceOption
    {
        public InstalledVoiceToken Voice { get; }
        public VoiceOption(InstalledVoiceToken voice) => Voice = voice;
        public override string ToString() => $"{Voice.Name} ({Voice.CultureDisplayName}, {Voice.Gender})";
    }
}
