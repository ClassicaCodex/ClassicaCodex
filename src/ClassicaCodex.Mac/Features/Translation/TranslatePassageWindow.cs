using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// Opened from a reader pane's right-click menu, one step before Word Study.
/// Works from either pane: right-click the original and it looks toward the
/// translation; right-click the translation and it looks toward the
/// original instead. The caller decides which counterpart edition and
/// language apply from which pane was clicked - this window just acts on
/// whatever direction it's given.
///
/// Three independent sections, deliberately kept visually distinct:
///
///  - Listen - reads the passage aloud with the voices macOS has
///    (SpeechService). Fully offline, no confirmation needed. Greek is
///    transliterated to a rough Latin spelling first, since a stock voice
///    reads raw Greek script by naming each character rather than attempting
///    to say it.
///  - Ingested Translation / Ingested Original (the heading names whichever
///    one is being looked for) - purely local. If the counterpart edition is
///    loaded in its pane, this looks up the same citation ref in it through
///    PassageAligner, the alignment bilingual export uses.
///  - AI Translation - the one part of Classica Codex that isn't offline, and
///    the only place two providers exist side by side, because they have
///    real, different tradeoffs: Claude costs money with no free tier but
///    doesn't train on API traffic; Gemini has a genuine free tier with no
///    card required, but its free tier may use what's sent to it to improve
///    Google's models. Either needs its own key, and unless that is turned
///    off, asks before every single send.
/// </summary>
public sealed class TranslatePassageWindow : FeatureWindow
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
    // direction-dependent label below. Passed in explicitly by the caller,
    // which knows for certain which pane was clicked, rather than guessed
    // from a language code - a non-English translation edition would make
    // that guess wrong.
    private readonly bool _counterpartIsTranslation;

    private readonly ComboBox _voices;
    private readonly Button _readAloud;
    private readonly Button _stopReading;
    private readonly TextBlock _listenStatus;
    private readonly DispatcherTimer _listenPoll;
    private readonly TextBlock _ingestedStatus;
    private readonly SelectableTextBlock _ingestedText;
    private readonly Button _claude;
    private readonly Button _gemini;
    private readonly TextBlock _aiStatus;
    private readonly SelectableTextBlock _aiResult;
    private readonly Button _copy;
    private readonly IconLabel _copyLabel;
    private readonly IconLabel _claudeLabel = new("Key", string.Empty);
    private readonly IconLabel _geminiLabel = new("Key", string.Empty);

    // One-shot, purely so Copy can say "Copied" for a moment and then go
    // back to normal. A clipboard write that succeeds silently is
    // indistinguishable from one that did nothing.
    private readonly DispatcherTimer _copyFeedback;

    public TranslatePassageWindow(
        TextNode node, string? sourceLanguage, string? targetLanguage, string authorName, string workTitle,
        int? counterpartEditionId, bool counterpartIsTranslation, FontFamily passageFont)
    {
        _node = node;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage;
        _authorName = authorName;
        _workTitle = workTitle;
        _counterpartEditionId = counterpartEditionId;
        _counterpartIsTranslation = counterpartIsTranslation;

        Title = "Translate";
        Width = 680;
        Height = 820;
        MinWidth = 560;
        MinHeight = 720;

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock { Text = $"{authorName}, {workTitle}", Classes = { "h2" }, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = $"[{PassageCitation.Display(node.CitationRef, node.Milestone)}]", Classes = { "caption" } });

        var passage = new SelectableTextBlock
        {
            Text = node.Text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = ScriptFonts.Compose(passageFont, ScriptFonts.FamilyFor(node.Text)),
            FlowDirection = ScriptFonts.IsRightToLeft(node.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            FontSize = 16,
            LineHeight = 23
        };
        var passageBox = Boxed(passage, 120);

        // Listen.
        _voices = new ComboBox
        {
            Width = 340,
            ItemTemplate = new FuncDataTemplate<InstalledVoiceToken>((voice, _) => new TextBlock { Text = VoiceLabel(voice) })
        };
        _voices.SelectionChanged += (_, _) =>
        {
            if (_voices.SelectedItem is InstalledVoiceToken voice) SpeechService.SetVoice(voice.Name);
        };

        var moreVoices = new Button { Content = "Get more voices…", Classes = { "link" }, VerticalAlignment = VerticalAlignment.Center };
        moreVoices.Click += (_, _) => OpenSpokenContentSettings();

        var voiceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        voiceRow.Children.Add(new TextBlock { Text = "Voice:", VerticalAlignment = VerticalAlignment.Center });
        voiceRow.Children.Add(_voices);
        voiceRow.Children.Add(moreVoices);

        _readAloud = new Button { Content = new IconLabel("ReadAloud", "Read Aloud"), MinWidth = 130 };
        _readAloud.Click += (_, _) => OnReadAloud();
        _stopReading = new Button { Content = new IconLabel("Stop", "Stop"), IsEnabled = false, MinWidth = 84 };
        _stopReading.Click += (_, _) =>
        {
            SpeechService.Stop();
            RefreshListenButtons();
        };
        _listenStatus = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };

        var listenRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        listenRow.Children.Add(_readAloud);
        listenRow.Children.Add(_stopReading);
        listenRow.Children.Add(_listenStatus);

        var listen = new StackPanel { Spacing = 8 };
        listen.Children.Add(Section("LISTEN"));
        listen.Children.Add(voiceRow);
        listen.Children.Add(listenRow);

        // There is no ancient Greek or Latin voice, and the reader should
        // know what they are hearing is an approximation of one.
        if (sourceLanguage is "grc" or "lat")
        {
            listen.Children.Add(new TextBlock
            {
                Text = sourceLanguage == "grc"
                    ? "No voice speaks ancient Greek: the passage is read from a Latin-letter transliteration, as a guide to its sound rather than a reconstruction."
                    : "No voice speaks classical Latin: the passage is read as it stands, in a modern accent.",
                TextWrapping = TextWrapping.Wrap,
                Classes = { "caption" }
            });
        }

        // Polls rather than waiting on the process: 300ms is frequent enough
        // that the buttons feel responsive without taxing anything.
        _listenPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _listenPoll.Tick += (_, _) => RefreshListenButtons();

        // The ingested counterpart.
        _ingestedStatus = new TextBlock { Text = "Checking...", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        _ingestedText = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)Application.Current!.FindResource(counterpartIsTranslation ? "Cc.TranslationFont" : "Cc.SourceFont")!,
            FontSize = 14.5,
            LineHeight = 21
        };

        var ingested = new StackPanel { Spacing = 8 };
        ingested.Children.Add(Section(counterpartIsTranslation ? "INGESTED TRANSLATION" : "INGESTED ORIGINAL"));
        ingested.Children.Add(_ingestedStatus);
        ingested.Children.Add(Boxed(_ingestedText, 110));

        // AI translation.
        var warning = new TextBlock
        {
            Text = "Sends this passage over the internet - the only part of Classica Codex that isn't " +
                   "offline. Nothing is sent unless you click one of the two buttons below.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "warning" }
        };

        _claude = new Button { Content = _claudeLabel, MinWidth = 240, HorizontalContentAlignment = HorizontalAlignment.Center };
        _claude.Click += async (_, _) => await OnAiButtonAsync(
            "Claude",
            () => TranslationSettings.AnthropicApiKey,
            (key, ct) => ClaudeTranslationService.TranslateAsync(
                _node.Text, _sourceLanguage, _targetLanguage, _authorName, _workTitle, _node.CitationRef, key, ct));

        _gemini = new Button { Content = _geminiLabel, MinWidth = 240, HorizontalContentAlignment = HorizontalAlignment.Center };
        _gemini.Click += async (_, _) => await OnAiButtonAsync(
            "Gemini",
            () => TranslationSettings.GeminiApiKey,
            (key, ct) => GeminiTranslationService.TranslateAsync(
                _node.Text, _sourceLanguage, _targetLanguage, _authorName, _workTitle, _node.CitationRef, key, ct));

        var aiButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        aiButtons.Children.Add(_claude);
        aiButtons.Children.Add(_gemini);

        _aiStatus = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        _aiResult = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)Application.Current!.FindResource("Cc.TranslationFont")!,
            FontSize = 14.5,
            LineHeight = 21
        };

        var aiTop = new StackPanel { Spacing = 8 };
        aiTop.Children.Add(Section("AI TRANSLATION"));
        aiTop.Children.Add(warning);
        aiTop.Children.Add(aiButtons);
        aiTop.Children.Add(_aiStatus);

        // The one box that takes the extra height: it holds generated prose,
        // which runs long, and it's the only text here that exists nowhere
        // else.
        var aiResultBox = Boxed(_aiResult, null);

        _copyLabel = new IconLabel("Copy", "Copy to Clipboard");
        _copy = new Button { Content = _copyLabel, IsEnabled = false, MinWidth = 160, HorizontalContentAlignment = HorizontalAlignment.Center };
        _copy.Click += async (_, _) => await CopyTranslationAsync();
        _copyFeedback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _copyFeedback.Tick += (_, _) =>
        {
            _copyFeedback.Stop();
            _copyLabel.Set("Copy", "Copy to Clipboard");
        };

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(new ContentControl { Content = _copy, HorizontalAlignment = HorizontalAlignment.Left });

        var top = new StackPanel { Spacing = 14 };
        top.Children.Add(header);
        top.Children.Add(passageBox);
        top.Children.Add(Rule());
        top.Children.Add(listen);
        top.Children.Add(Rule());
        top.Children.Add(ingested);
        top.Children.Add(Rule());
        top.Children.Add(aiTop);

        aiResultBox.Margin = new Thickness(0, 8, 0, 0);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(aiResultBox);
        Content = root;

        RefreshAiButtons();
        LoadVoices();
        Opened += async (_, _) => await LoadIngestedMatchAsync();

        // Speech shouldn't keep playing after the window that started it is
        // gone, and the timers have nothing left to watch.
        Closed += (_, _) =>
        {
            _listenPoll.Stop();
            _copyFeedback.Stop();
            SpeechService.Stop();
        };
    }

    internal string IngestedStatusText => _ingestedStatus.Text ?? string.Empty;

    internal string IngestedText => _ingestedText.Text ?? string.Empty;

    internal string ListenStatusText => _listenStatus.Text ?? string.Empty;

    internal Button ClaudeButton => _claude;

    internal Button GeminiButton => _gemini;

    private static TextBlock Section(string text) => new() { Text = text, Classes = { "section" } };

    /// <summary>A status line in the warning colour, or back in the quiet caption one.</summary>
    private static void Warn(TextBlock status, bool warning)
    {
        status.Classes.Set("warning", warning);
        status.Classes.Set("caption", !warning);
    }

    private static Border Rule() => new() { Classes = { "rule" } };

    private static Border Boxed(Control content, double? maxHeight)
    {
        var scroller = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        if (maxHeight != null) scroller.MaxHeight = maxHeight.Value;

        return new Border { Classes = { "panel" }, Padding = new Thickness(12, 8), Child = scroller, MinHeight = 44 };
    }

    private static string VoiceLabel(InstalledVoiceToken? voice) =>
        voice == null ? string.Empty
        : string.IsNullOrEmpty(voice.Gender) ? $"{voice.Name} ({voice.CultureDisplayName})"
        : $"{voice.Name} ({voice.CultureDisplayName}, {voice.Gender})";

    private void LoadVoices()
    {
        var voices = SpeechService.GetInstalledVoices();
        if (voices.Count == 0)
        {
            _voices.IsEnabled = false;
            _readAloud.IsEnabled = false;
            Warn(_listenStatus, true);
            _listenStatus.Text = "No speech voice found on this computer.";
            return;
        }

        _voices.ItemsSource = voices;

        // A missing or damaged settings file can't be allowed to throw out
        // of opening the window.
        string? preferred;
        try { preferred = UI.SpeechSettings.PreferredVoiceName; }
        catch (Exception) { preferred = null; }

        _voices.SelectedItem = (preferred != null ? voices.FirstOrDefault(v => v.Name == preferred) : null) ?? voices[0];
    }

    private void OnReadAloud()
    {
        SpeechService.Speak(_node.Text, _sourceLanguage);
        _listenPoll.Start();
        RefreshListenButtons();
    }

    private void RefreshListenButtons()
    {
        var speaking = SpeechService.IsSpeaking;
        _stopReading.IsEnabled = speaking;
        Warn(_listenStatus, false);
        _listenStatus.Text = speaking ? "Speaking..." : string.Empty;

        if (!speaking) _listenPoll.Stop();
    }

    /// <summary>
    /// System Settings › Accessibility › Spoken Content, where macOS keeps
    /// its voices. If this system doesn't know the address the link is
    /// simply inert - not worth an alert over.
    /// </summary>
    private static void OpenSpokenContentSettings() =>
        MacShell.Open("x-apple.systempreferences:com.apple.preference.universalaccess?SpeakableItems");

    internal async Task LoadIngestedMatchAsync()
    {
        var counterpartNoun = _counterpartIsTranslation ? "translation" : "original";

        if (_counterpartEditionId == null)
        {
            _ingestedStatus.Text = $"(no {counterpartNoun} edition currently loaded for this work)";
            return;
        }

        List<TextNode> counterpartNodes;
        try
        {
            counterpartNodes = await _textNodeRepo.GetByEditionAsync(_counterpartEditionId.Value);
        }
        catch (Exception ex)
        {
            _ingestedStatus.Text = $"Couldn't read the {counterpartNoun}: {ex.Message}";
            return;
        }

        var aligner = new PassageAligner(counterpartNodes);
        var match = aligner.ResolveMatch(_node.CitationRef);

        if (match == null)
        {
            _ingestedStatus.Text = $"(no matching passage in the loaded {counterpartNoun})";
            return;
        }

        var (matchedRef, matchedText, matchedCount) = match.Value;

        if (matchedCount > 1)
        {
            // Several counterpart passages walked down from a coarser query
            // and joined - every one of them genuinely sits under the
            // citation asked for, so a long combined result is exactly what
            // should happen, not a coincidence worth flagging. Saying so is
            // what makes it visible that the length comes from real structure.
            _ingestedStatus.Text = $"Found across {matchedCount} passages, starting at [{matchedRef}]:";
            SetIngested(matchedText);
            return;
        }

        // A single exact-or-coarser hit is the case where two editions could
        // be numbering genuinely different things the same way by
        // coincidence - worth suspicion if the lengths don't match, since
        // nothing in the citation refs themselves reveals that.
        var ratio = (double)Math.Max(matchedText.Length, 1) / Math.Max(_node.Text.Length, 1);
        var lengthLooksOff = ratio > 5.0 || ratio < 0.2;

        _ingestedStatus.Text = lengthLooksOff
            ? $"Found at [{matchedRef}] - but its length differs a lot from the passage above. That " +
              "can be normal (a translation grouping several original lines under one citation), or a " +
              "sign the two editions number citations differently here. Compare the refs before trusting this."
            : $"Found at [{matchedRef}]:";
        SetIngested(matchedText);
    }

    private void SetIngested(string text)
    {
        _ingestedText.Text = text;
        _ingestedText.FontFamily = ScriptFonts.Compose(_ingestedText.FontFamily, ScriptFonts.FamilyFor(text));
        _ingestedText.FlowDirection = ScriptFonts.IsRightToLeft(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        RefreshCopyButton();
    }

    private void RefreshCopyButton() =>
        _copy.IsEnabled = !string.IsNullOrWhiteSpace(_aiResult.Text) || !string.IsNullOrWhiteSpace(_ingestedText.Text);

    /// <summary>
    /// Copies whichever translation is on screen. The AI result wins when
    /// both are present: it's what was just generated and what the reader is
    /// looking at, and unlike the ingested passage it exists nowhere else -
    /// that one is already in the reader behind this window, with its own
    /// Copy on the line menu.
    /// </summary>
    private async Task CopyTranslationAsync()
    {
        var text = !string.IsNullOrWhiteSpace(_aiResult.Text) ? _aiResult.Text : _ingestedText.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Say which way it went: a silent failure would look like a success.
        var copied = await ClipboardText.SetAsync(this, text);
        _copyLabel.Set(copied ? "Check" : "Warning", copied ? "Copied" : "Clipboard busy");
        _copyFeedback.Stop();
        _copyFeedback.Start();
    }

    /// <summary>Each button's text depends on whether that provider's key is configured yet.</summary>
    private void RefreshAiButtons()
    {
        var hasAnthropicKey = !string.IsNullOrWhiteSpace(TranslationSettings.AnthropicApiKey);
        _claudeLabel.Set(hasAnthropicKey ? "Translate" : "Key", hasAnthropicKey ? "Translate with Claude" : "Configure Claude Key…");
        _claude.IsEnabled = true;

        var hasGeminiKey = !string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey);
        _geminiLabel.Set(hasGeminiKey ? "Translate" : "Key", hasGeminiKey ? "Translate with Gemini (free)" : "Configure Gemini Key…");
        _gemini.IsEnabled = true;
    }

    /// <summary>
    /// Shared by both buttons - no key yet opens the AI settings; otherwise
    /// confirms (unless turned off) and calls whichever provider was passed in.
    /// </summary>
    private async Task OnAiButtonAsync(string providerName, Func<string?> getKey, Func<string, CancellationToken, Task<string>> translate)
    {
        var key = getKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            await SettingsFeature.OpenAiSettingsAsync(this);
            RefreshAiButtons();
            return;
        }

        if (TranslationSettings.AlwaysConfirmBeforeSending)
        {
            var confirmed = await Alert.Confirm(this,
                $"Send to {providerName}'s API?",
                $"This will send the selected passage (about {_node.Text.Length} characters) to " +
                $"{providerName}'s API over the internet, so it can be translated. This is the one " +
                "thing in Classica Codex that isn't offline.\n\n" +
                "Continue? (You can turn this confirmation off in AI Translation Settings.)",
                "Send", "Cancel", AlertKind.Warning);

            if (!confirmed) return;
        }

        _claude.IsEnabled = false;
        _gemini.IsEnabled = false;
        Warn(_aiStatus, false);
        _aiStatus.Text = $"Translating with {providerName}...";
        _aiResult.Text = string.Empty;
        RefreshCopyButton();

        try
        {
            _aiResult.Text = await translate(key, CancellationToken.None);
            _aiStatus.Text = string.Empty;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, $"AI translation via {providerName}");
            Warn(_aiStatus, true);
            _aiStatus.Text = $"Couldn't translate with {providerName}: {ex.Message}";
        }
        finally
        {
            RefreshAiButtons();
            RefreshCopyButton();
        }
    }
}
