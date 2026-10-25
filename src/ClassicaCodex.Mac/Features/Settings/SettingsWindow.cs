using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Settings;

/// <summary>
/// Settings (⌘,), laid out the way a Mac's settings windows are: a row of
/// labelled icons across the top, one pane beneath.
///
/// On Windows these choices are scattered - a toolbar toggle for the theme, a
/// dialog for text size, another for the AI keys reached from Translate, the
/// database location reached from the setup wizard. A Mac user looks for all
/// of them in one place, under the application menu, so they are gathered
/// here; each still writes the same preference the Windows app reads.
///
/// Changes take effect at once, as Mac settings do - apart from the API keys,
/// which are saved by a button, because writing to the keychain on every
/// keystroke of a pasted key would be both slow and strange.
/// </summary>
public sealed class SettingsWindow : FeatureWindow
{
    public const string General = "general";
    public const string Reading = "reading";
    public const string Ai = "ai";
    public const string Voice = "voice";
    public const string Library = "library";

    private readonly IReaderHost _host;
    private readonly ContentControl _pane = new();
    private readonly List<(string Key, ToggleButton Tab)> _tabs = new();

    public SettingsWindow(IReaderHost host, string initialPane = General)
    {
        _host = host;
        Title = "Settings";
        Width = 620;
        Height = 600;
        MinWidth = 560;
        MinHeight = 480;

        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4, Margin = new Thickness(0, 10, 0, 8) };
        foreach (var (key, label, glyph) in new[]
                 {
                     (General, "General", "Settings"),
                     (Reading, "Reading", "TextSize"),
                     (Ai, "AI", "Sparkle"),
                     (Voice, "Voice", "ReadAloud"),
                     (Library, "Library", "Database")
                 })
        {
            var content = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new PathIcon { Data = Glyphs.Get(glyph), Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });

            var tab = new ToggleButton { Classes = { "toolbar" }, Content = content, Height = 52, MinWidth = 70, Padding = new Thickness(8, 4) };
            var captured = key;
            tab.Click += (_, _) => ShowPane(captured);
            _tabs.Add((key, tab));
            tabRow.Children.Add(tab);
        }

        var header = new Border { Child = tabRow, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Cc.Rule"));

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(new ScrollViewer { Content = _pane, Padding = new Thickness(32, 20, 32, 24) });
        Content = dock;

        ShowPane(initialPane);
    }

    public void ShowPane(string key)
    {
        foreach (var (tabKey, tab) in _tabs) tab.IsChecked = tabKey == key;

        _pane.Content = key switch
        {
            Reading => BuildReadingPane(),
            Ai => BuildAiPane(),
            Voice => BuildVoicePane(),
            Library => BuildLibraryPane(),
            _ => BuildGeneralPane()
        };
    }

    private static StackPanel Section(string title, string? explanation = null)
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 22) };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "h3" } });
        if (explanation != null)
            stack.Children.Add(new TextBlock { Text = explanation, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private Control BuildGeneralPane()
    {
        var root = new StackPanel();

        var appearance = Section("Appearance",
            "Parchment is the light theme the Windows app ships with; Lamplight is its dark counterpart. Following macOS switches between them with the system, at sunset if you have it set that way.");
        foreach (var (label, choice) in new[]
                 {
                     ("Follow macOS", AppearanceChoice.System),
                     ("Parchment (light)", AppearanceChoice.Light),
                     ("Lamplight (dark)", AppearanceChoice.Dark)
                 })
        {
            var radio = new RadioButton { Content = label, GroupName = "appearance", IsChecked = MacSettings.Current.Appearance == choice };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && MainWindow.Current is { } main) main.ApplyAppearance(choice);
            };
            appearance.Children.Add(radio);
        }

        root.Children.Add(appearance);

        var launch = Section("When Classica Codex opens");
        var reopen = new CheckBox { Content = "Reopen the passage I was last reading", IsChecked = ReadingPosition.ReopenOnLaunch };
        reopen.IsCheckedChanged += (_, _) => ReadingPosition.ReopenOnLaunch = reopen.IsChecked == true;
        launch.Children.Add(reopen);
        root.Children.Add(launch);

        var collection = Section("Default collection",
            "Collections overlap - Perseus and First1KGreek both carry the Agamemnon, CSEL and the Patrologia Latina share a good deal of Augustine. "
            + "Pick which one a work opens on. A preference, not a filter: the other editions stay in the dropdown.");
        var picker = new ComboBox { MinWidth = 320 };
        collection.Children.Add(picker);
        root.Children.Add(collection);
        _ = FillCollectionPickerAsync(picker);

        return root;
    }

    private static async Task FillCollectionPickerAsync(ComboBox picker)
    {
        var options = new List<(string? Key, string Title)> { (null, "No preference - the first edition") };

        try
        {
            if (DbConnectionFactory.IsConfigured)
            {
                foreach (var key in await new EditionRepository().GetCollectionsAsync())
                    options.Add((key, SetupDataSourceCatalog.DescribeCollection(key)));
            }
        }
        catch (Exception)
        {
            // An empty list of collections is still a usable control.
        }

        picker.ItemsSource = options.Select(o => o.Title).ToList();
        var current = PreferredCollectionSettings.Preferred;
        picker.SelectedIndex = Math.Max(0, options.FindIndex(o => o.Key == current));
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0) PreferredCollectionSettings.Preferred = options[picker.SelectedIndex].Key;
        };
    }

    private Control BuildReadingPane()
    {
        var root = new StackPanel();

        var size = Section("Text size",
            "Polytonic diacritics are what you need to see to look a word up, and they are a few pixels each at a small size.");
        var source = new Slider { Minimum = ReadingFontSettings.MinimumSize, Maximum = ReadingFontSettings.MaximumSize, Value = ReadingFontSettings.SourceSize, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var translation = new Slider { Minimum = ReadingFontSettings.MinimumSize, Maximum = ReadingFontSettings.MaximumSize, Value = ReadingFontSettings.TranslationSize, Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var sourceLabel = new TextBlock { Classes = { "caption" } };
        var translationLabel = new TextBlock { Classes = { "caption" } };
        var linked = new CheckBox { Content = "Keep the two the same size", IsChecked = ReadingFontSettings.Linked };

        void Labels()
        {
            sourceLabel.Text = $"Originals - Greek, Latin and the rest: {ReadingFontSettings.SourceSize:0.#} pt";
            translationLabel.Text = $"Translations: {ReadingFontSettings.TranslationSize:0.#} pt";
        }

        var updating = false;
        source.ValueChanged += (_, e) =>
        {
            if (updating) return;
            ReadingFontSettings.SetSource((float)Math.Round(e.NewValue * 2) / 2);
            updating = true;
            translation.Value = ReadingFontSettings.TranslationSize;
            updating = false;
            Labels();
        };
        translation.ValueChanged += (_, e) =>
        {
            if (updating) return;
            ReadingFontSettings.SetTranslation((float)Math.Round(e.NewValue * 2) / 2);
            updating = true;
            source.Value = ReadingFontSettings.SourceSize;
            updating = false;
            Labels();
        };
        linked.IsCheckedChanged += (_, _) => ReadingFontSettings.SetLinked(linked.IsChecked == true);
        Labels();

        size.Children.Add(sourceLabel);
        size.Children.Add(source);
        size.Children.Add(translationLabel);
        size.Children.Add(translation);
        size.Children.Add(linked);
        root.Children.Add(size);

        var panes = Section("The two pages");
        var link = new CheckBox { Content = "Link the original and the translation, so they scroll and select together", IsChecked = PaneSyncSettings.Enabled };
        link.IsCheckedChanged += (_, _) => MainWindow.Current?.SetPanesLinkedFromSettings(link.IsChecked == true);
        panes.Children.Add(link);
        panes.Children.Add(new TextBlock
        {
            Text = "Linked pages suit verse, where line 40 really does sit opposite line 40. They fight you in prose, where one Greek sentence can become three English ones - switch them off there.",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap
        });

        var margin = new CheckBox { Content = "Citations in the margin", IsChecked = CitationMarginSettings.Enabled, Margin = new Thickness(0, 6, 0, 0) };
        margin.IsCheckedChanged += (_, _) => MainWindow.Current?.SetCitationMarginFromSettings(margin.IsChecked == true);
        panes.Children.Add(margin);
        root.Children.Add(panes);

        var kinds = Section("Show beside the text",
            "Speakers, stage directions, headings and the rest are shown or held back as you choose. Word counts and stylometry always leave them out.");
        foreach (var kind in NodeKindVisibility.InMenuOrder(new[]
                 {
                     TextNodeKinds.Line, TextNodeKinds.Speaker, TextNodeKinds.Stage, TextNodeKinds.Head,
                     TextNodeKinds.Cast, TextNodeKinds.Paratext, TextNodeKinds.Attribution
                 }))
        {
            var box = new CheckBox { Content = NodeKindVisibility.Label(kind), IsChecked = NodeKindVisibility.IsVisible(kind) };
            var captured = kind;
            box.IsCheckedChanged += async (_, _) =>
            {
                NodeKindVisibility.SetVisible(captured, box.IsChecked == true);
                if (MainWindow.Current is { } main) await main.RefreshPanesFromSettingsAsync();
            };
            kinds.Children.Add(box);
        }

        root.Children.Add(kinds);
        return root;
    }

    /// <summary>
    /// The Windows "AI Translation Settings" form: two providers, what each
    /// costs and what each does with what you send, and the switch that asks
    /// before every send. The keys go to the macOS keychain.
    /// </summary>
    private Control BuildAiPane()
    {
        var root = new StackPanel();

        root.Children.Add(new TextBlock
        {
            Text = "The rest of Classica Codex works entirely offline. The AI features are the one exception, and they can use either of two providers below - set up whichever suits. Nothing is sent until you press a button that sends it.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            Margin = new Thickness(0, 0, 0, 18)
        });

        var anthropic = KeyBox(TranslationSettings.AnthropicApiKey);
        var gemini = KeyBox(TranslationSettings.GeminiApiKey);

        var claude = Section("Anthropic (Claude)",
            "A separate developer account from claude.ai or a Claude Pro or Max subscription - even if you already pay for one of those, this needs its own setup. "
            + "Requires a payment method; no ongoing free tier, though a single passage costs a small fraction of a cent.");
        claude.Children.Add(Links(("Get an API key →", "https://console.anthropic.com"), ("See current pricing →", "https://www.anthropic.com/pricing")));
        claude.Children.Add(KeyRow(anthropic));
        root.Children.Add(claude);

        var google = Section("Google (Gemini) - free, no card required",
            "A genuinely free tier through Google AI Studio - no payment method, no expiration. The trade-off: Google's free tier may use what you send it to improve their models, "
            + "so \"free\" isn't the same guarantee as \"private\" here.");
        google.Children.Add(Links(("Get a free API key →", "https://aistudio.google.com/app/apikey")));
        google.Children.Add(KeyRow(gemini));
        root.Children.Add(google);

        var confirm = new CheckBox
        {
            Content = "Ask for confirmation every time before sending a passage (either provider)",
            IsChecked = TranslationSettings.AlwaysConfirmBeforeSending
        };
        root.Children.Add(confirm);

        var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Content = "Save", Classes = { "accent" }, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        save.Click += (_, _) =>
        {
            var stored = TranslationSettings.Save(anthropic.Text, gemini.Text, confirm.IsChecked == true);
            status.Text = stored
                ? "Saved. The keys are in your login keychain, under “Classica Codex” in Keychain Access."
                : TranslationSettings.CanStoreKeys
                    ? "The keychain refused to store a key - check Keychain Access and try again."
                    : "This system has no keychain to keep the keys in, so they apply until Classica Codex quits.";
        };

        root.Children.Add(save);
        root.Children.Add(status);
        return root;
    }

    private static TextBox KeyBox(string? value) => new()
    {
        Text = value ?? string.Empty,
        PasswordChar = '•',
        MinWidth = 360,
        FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!
    };

    private static Control KeyRow(TextBox box)
    {
        var show = new CheckBox { Content = "Show key", VerticalAlignment = VerticalAlignment.Center };
        show.IsCheckedChanged += (_, _) => box.PasswordChar = show.IsChecked == true ? '\0' : '•';

        var remove = new Button { Content = "Remove Key" };
        remove.Click += (_, _) => box.Text = string.Empty;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(show);
        row.Children.Add(remove);

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(box);
        stack.Children.Add(row);
        return stack;
    }

    private static Control Links(params (string Text, string Url)[] links)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var (text, url) in links)
        {
            var link = new Button { Content = text, Classes = { "link" } };
            link.Click += (_, _) => MacShell.Open(url);
            row.Children.Add(link);
        }

        return row;
    }

    private Control BuildVoicePane()
    {
        var root = new StackPanel();

        var voice = Section("Read Aloud",
            "Hands a passage to the voices macOS already has. There is no ancient Greek or Latin voice to be had, so this is an approximation and says so: "
            + "Greek is transliterated into Latin letters first, and Latin goes through as it stands and comes back in a modern accent. "
            + "Useful for hearing the shape of a line, not for pronunciation. Entirely offline.");

        if (!SpeechService.IsAvailable)
        {
            voice.Children.Add(new TextBlock { Text = "Speech is not available on this system.", Classes = { "warning" } });
            root.Children.Add(voice);
            return root;
        }

        var voices = SpeechService.GetInstalledVoices();
        var picker = new ComboBox
        {
            MinWidth = 340,
            ItemsSource = voices.Select(v => $"{v.Name}  ({v.CultureDisplayName})").Prepend("The system voice").ToList()
        };
        var current = SpeechSettings.PreferredVoiceName;
        var index = voices.ToList().FindIndex(v => v.Name == current);
        picker.SelectedIndex = index < 0 ? 0 : index + 1;
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex > 0) SpeechService.SetVoice(voices[picker.SelectedIndex - 1].Name);
        };

        var sample = new TextBox { Text = "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος", FontFamily = (FontFamily)Application.Current!.FindResource("Cc.SourceFont")!, FontSize = 16 };
        var play = new Button { Content = "Read Sample", Classes = { "accent" } };
        play.Click += (_, _) => SpeechService.Speak(sample.Text ?? string.Empty, "grc");
        var stop = new Button { Content = "Stop" };
        stop.Click += (_, _) => SpeechService.Stop();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(play);
        buttons.Children.Add(stop);

        voice.Children.Add(picker);
        voice.Children.Add(sample);
        voice.Children.Add(buttons);
        voice.Children.Add(new TextBlock
        {
            Text = "More voices: System Settings → Accessibility → Spoken Content → System Voice → Manage Voices.",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(voice);
        return root;
    }

    private Control BuildLibraryPane()
    {
        var root = new StackPanel();

        var library = Section("This library",
            "Everything - the texts, the dictionaries, your tags, bookmarks and research - is in this one file. A library copied from the PC opens here as it is.");
        var path = DbConnectionFactory.DatabasePath ?? "No library is open.";
        library.Children.Add(new SelectableTextBlock { Text = path, Classes = { "mono" }, TextWrapping = TextWrapping.Wrap });

        var reveal = new Button { Content = "Show in Finder", IsEnabled = DbConnectionFactory.IsConfigured };
        reveal.Click += (_, _) => { if (DbConnectionFactory.DatabasePath is { } p) MacShell.Reveal(p); };
        var open = new Button { Content = "Open Another Library…" };
        open.Click += async (_, _) =>
        {
            if (MainWindow.Current is { } main)
            {
                await main.OpenLibraryAsync();
                ShowPane(Library);
            }
        };
        var create = new Button { Content = "Create a New Library…" };
        create.Click += async (_, _) =>
        {
            if (MainWindow.Current is { } main)
            {
                await main.CreateLibraryAsync();
                ShowPane(Library);
            }
        };

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var b in new[] { reveal, open, create })
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(b);
        }

        library.Children.Add(buttons);
        root.Children.Add(library);

        var downloads = Section("Downloads for setup",
            "Where the setup wizard keeps what it downloads before ingesting it - about 9 GB with everything selected. Setup itself is where this is changed, so it can check the space and the folder first.");
        downloads.Children.Add(new SelectableTextBlock { Text = DataFolderSettings.Root, Classes = { "mono" }, TextWrapping = TextWrapping.Wrap });
        var revealDownloads = new Button { Content = "Show in Finder" };
        revealDownloads.Click += (_, _) => MacShell.Open(Directory.Exists(DataFolderSettings.Root) ? DataFolderSettings.Root : Path.GetDirectoryName(DataFolderSettings.Root) ?? DataFolderSettings.Root);
        downloads.Children.Add(revealDownloads);
        root.Children.Add(downloads);

        var log = Section("If something goes wrong",
            "Errors are written to a log beside the preferences - the same file name the Windows app uses. Attaching it to an issue helps more than a description.");
        log.Children.Add(new SelectableTextBlock { Text = CrashLog.LogFile, Classes = { "mono" }, TextWrapping = TextWrapping.Wrap });
        var revealLog = new Button { Content = "Show in Finder", IsEnabled = File.Exists(CrashLog.LogFile) };
        revealLog.Click += (_, _) => MacShell.Reveal(CrashLog.LogFile);
        log.Children.Add(revealLog);
        root.Children.Add(log);

        return root;
    }
}
