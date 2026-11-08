using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.About;
using ClassicaCodex.Mac.Features.Help;
using ClassicaCodex.Mac.Features.Research.Bench;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The two places the shared code hands over to the iPad app. Neither the
/// process it replaces nor the system it reaches exists on the other's machine,
/// so these check the handing over, and the fakes keep the real keychain out of it.
/// </summary>
public class DeviceTests
{
    [Fact]
    public void Links_and_folders_go_to_the_device_when_no_process_can_be_started()
    {
        var opened = new List<string>();
        MacShell.OpenInstead = target =>
        {
            opened.Add(target);
            return true;
        };

        try
        {
            Assert.True(MacShell.Open("https://console.anthropic.com"));

            // A device cannot select a file in a folder, so it shows the folder.
            var file = Path.Combine(Path.GetTempPath(), "library", "classicacodex.db");
            Assert.True(MacShell.Reveal(file));

            Assert.Equal(new[] { "https://console.anthropic.com", Path.GetDirectoryName(file)! }, opened);
        }
        finally
        {
            MacShell.OpenInstead = null;
        }
    }

    [Fact]
    public void A_refused_link_is_reported_as_refused()
    {
        MacShell.OpenInstead = _ => false;

        try
        {
            Assert.False(MacShell.Open("x-apple.systempreferences:com.apple.preference.universalaccess"));
        }
        finally
        {
            MacShell.OpenInstead = null;
        }
    }

    [Fact]
    public void Keys_go_to_the_device_keychain_under_the_usual_service_name()
    {
        var store = new FakeStore();
        Keychain.StoreInstead = store;

        try
        {
            Assert.True(Keychain.IsAvailable);
            Assert.True(Platform.TranslationSettings.CanStoreKeys);

            Assert.Null(Keychain.Read("test-account"));
            Assert.True(Keychain.Write("test-account", "the key"));
            Assert.Equal("the key", Keychain.Read("test-account"));
            Assert.Equal(Keychain.Service, store.LastService);

            Assert.True(Keychain.Write("test-account", null));
            Assert.Null(Keychain.Read("test-account"));
        }
        finally
        {
            Keychain.StoreInstead = null;
        }
    }

    [Fact]
    public void Every_step_that_clones_can_be_fetched_without_git()
    {
        var clones = SetupDataSourceCatalog
            .Build(new AuthorRepository(), new LemmaRepository(), new DefinitionRepository(), new ArtifactRepository(), new EditionRepository())
            .Where(source => source.FetchMode == SetupFetchMode.GitClone)
            .ToList();

        Assert.NotEmpty(clones);
        Assert.All(clones, source => Assert.StartsWith(
            "https://codeload.github.com/", GitHubArchiveFetchService.ArchiveUrl(source.RepoUrl)));
    }

    [Theory]
    [InlineData("Show in Finder", "Show in Files")]
    [InlineData("Saved. The keys are in your login keychain, under “Classica Codex” in Keychain Access.", "Saved. The keys are in this iPad's keychain.")]
    [InlineData("Follow macOS", "Follow System")]
    [InlineData("usually about 9 gigabytes, and two to three hours on a decent connection with everything selected - mostly unattended, and the Greek word-form data is about an hour of that on its own.",
        "usually about 9 gigabytes, and about half an hour for the main Greek and Latin collections on a good connection. Keep Classica Codex on screen while a step runs: iPadOS pauses an app that is switched away from, and the step would then have to start again.")]
    public void An_iPad_says_what_is_true_of_an_iPad(string mac, string device) =>
        Assert.Equal(device, SystemWords.ForDevice(mac));

    [Theory]
    [InlineData("PASSAGES · DOUBLE-CLICK TO JUMP", "PASSAGES · DOUBLE-TAP TO JUMP")]
    [InlineData("Type a word and press Return. Double-click a result to open it in the reader.", "Type a word and press Return. Double-tap a result to open it in the reader.")]
    [InlineData("No tags yet - right-click a line in the reader to add one.", "No tags yet - touch and hold a line in the reader to add one.")]
    [InlineData("Click a place to see its passages here.", "Tap a place to see its passages here.")]
    [InlineData("Merge, split, or set an author on a selection. ⇧-click or ⌘-click to select several rows.", "Merge, split, or set an author on a selection.")]
    public void A_finger_is_told_what_a_finger_can_do(string mouse, string finger) =>
        Assert.Equal(finger, SystemWords.ForTouch(mouse));

    [Theory]
    [InlineData("Google (Gemini) - free, no card required", "Google (Gemini)")]
    [InlineData("Get a free API key →", "Get an API key →")]
    [InlineData("Translate with Gemini (free)", "Translate with Gemini")]
    [InlineData("Find Echoes with Gemini (free)", "Find Echoes with Gemini")]
    public void An_iPad_does_not_sell_a_developer_account_as_free(string mac, string device) =>
        Assert.Equal(device, SystemWords.ForDevice(mac));

    [Fact]
    public void The_Help_on_an_iPad_gives_Googles_limits_instead_of_the_free_tier()
    {
        var mac = HelpTopics.All.Single(topic => topic.Title == "Translate").Body;
        var device = SystemWords.ForDevice(mac);

        Assert.Contains("Genuinely free", mac);
        Assert.DoesNotContain("Genuinely free", device);
        Assert.Contains("18 or older", device);
        Assert.Contains("the EEA, Switzerland and the UK", device);
        Assert.DoesNotContain("Anthropic", device);
        Assert.DoesNotContain("Claude Pro", device);
    }

    [Fact]
    public void The_Help_on_an_iPad_describes_a_touch_screen_and_not_a_desktop()
    {
        var left = HelpTopics.All
            .Where(topic => topic.Title != "Ptolemy's Harmony")
            .SelectMany(topic => SystemWords.ForDevice(HelpOutline.Expand(topic.Body)).Split('\n')
                .Where(line => DesktopWords.Any(word => line.Contains(word, StringComparison.Ordinal)))
                .Select(line => $"{topic.Title}: {line.Trim()}"))
            .ToList();

        Assert.Empty(left);
    }

    private static readonly string[] DesktopWords =
    {
        "Ctrl+", "Alt+", "Option-", "⌘", "⇧", "⌥", "File >", "Go >", "menu bar", "Tools menu", "Go menu", "View menu",
        "Help menu", "Instruments menu", "Hover", "hover", "Control-click", " click", "Click ", "clicking", "It works offline",
        "Finder", "file manager", "Time Machine", "View >", "Research menu", "Font Book", "your computer", "your desktop", "your Mac",
        "Windows", "macOS", "either machine", "touch and holding", "touch and hold menu", "Zotero's local", "iOS ", "The README", "Advanced Setup", "Guided Setup", "this machine", "another machine",
        "%DATABASE%", "%DOWNLOADS%", "%PREFERENCES%", "%ERRORS_LOG%", "%SKIPPED_LOG%", "/var/", "~/",
    };

    [Theory]
    [InlineData("Where your data lives", "classicacodex.db")]
    [InlineData("When something looks wrong", "errors.log, in the ClassicaCodex folder")]
    [InlineData("Getting started", "Quick Start (recommended)")]
    [InlineData("The Antikythera Mechanism", "It needs a connection")]
    [InlineData("Getting started", "A tool opens as a full page, with Back or Close at its top left.")]
    [InlineData("Appearance and settings", "Settings\n   In the sidebar's Tools list")]
    [InlineData("Reading texts", "Marks added from a line's menu appear at once.")]
    [InlineData("Where your data lives", "press Remove Key in the AI tab of Settings first")]
    [InlineData("Tags and the Myth Network", "a folder called Figures in this app's folder in the Files app")]
    [InlineData("Research Bench", "choose Research Bench in the sidebar's Tools list")]
    [InlineData("Exporting passages", "Each goes to the Exports folder, in this app's folder in the Files app")]
    [InlineData("Where your data lives", "Exports and Imported")]
    [InlineData("Research Bench", "The app keeps its own copy in the Imported folder")]
    [InlineData("Translate", "keep Classica Codex on screen while it runs")]
    [InlineData("Medieval Hands", "Once fetched they are on this iPad")]
    [InlineData("Getting started", "It leaves out Greek Lemma Data and Latin Lemma Data")]
    [InlineData("Word Study and dictionaries", "Until that is in, the menu item says so.")]
    public void The_Help_on_an_iPad_says_where_things_are_there(string topic, string said) =>
        Assert.Contains(said, SystemWords.ForDevice(HelpOutline.Expand(HelpTopics.All.Single(t => t.Title == topic).Body)));

    [Theory]
    [InlineData("Sends this passage over the internet - the only part of Classica Codex that isn't offline. Nothing is sent.",
        "Sends this passage over the internet. Nothing is sent.")]
    [InlineData("so it can be translated. This is the one thing in Classica Codex that isn't offline.\n\nContinue?",
        "so it can be translated.\n\nContinue?")]
    [InlineData("You can turn this confirmation off in AI Translation Settings.", "You can turn this confirmation off in the AI tab of Settings.")]
    public void An_iPad_does_not_claim_to_be_offline_apart_from_AI(string mac, string device) =>
        Assert.Equal(device, SystemWords.ForDevice(mac));

    [Fact]
    public async Task An_iPad_exports_into_its_own_folder_and_never_over_an_earlier_file()
    {
        await OnAnIpad(async folder =>
        {
            var first = await Files.SaveAsync(null!, "Export", "Homer: Iliad.txt", Files.Text);
            await File.WriteAllTextAsync(first!, "μῆνιν");
            var second = await Files.SaveAsync(null!, "Export", "Homer: Iliad.txt", Files.Text);

            Assert.Equal(Path.Combine(folder, "Documents", "Exports"), Path.GetDirectoryName(first));
            Assert.NotEqual(first, second);
            Assert.False(File.Exists(second));
        });
    }

    [Fact]
    public async Task A_picked_file_is_copied_in_and_picking_it_again_gives_the_same_copy()
    {
        await OnAnIpad(async folder =>
        {
            using var paper = new MemoryStream(new byte[] { 1, 2, 3 });
            using var again = new MemoryStream(new byte[] { 1, 2, 3 });
            using var other = new MemoryStream(new byte[] { 4, 5, 6 });

            var kept = await DeviceFiles.KeepAsync(paper, "paper.pdf");

            Assert.Equal(Path.Combine(folder, "Documents", "Imported", "paper.pdf"), kept);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(kept));
            Assert.Equal(kept, await DeviceFiles.KeepAsync(again, "paper.pdf"));
            Assert.EndsWith("paper 2.pdf", await DeviceFiles.KeepAsync(other, "paper.pdf"));
            Assert.Equal(2, Directory.GetFiles(DeviceFiles.Imported).Length);
        });
    }

    [Fact]
    public async Task An_attachment_is_found_again_after_an_update_moves_the_app()
    {
        await OnAnIpad(folder =>
        {
            Assert.Equal(
                Path.Combine(folder, "Documents", "Imported", "paper.pdf"),
                DeviceFiles.Here("/private/var/mobile/Containers/Data/Application/OLD/Documents/Imported/paper.pdf"));
            return Task.CompletedTask;
        });

        Assert.Equal("/Users/me/Documents/paper.pdf", DeviceFiles.Here("/Users/me/Documents/paper.pdf"));
    }

    private static async Task OnAnIpad(Func<string, Task> test)
    {
        var folder = Path.Combine(Path.GetTempPath(), "device-files-" + Guid.NewGuid().ToString("N"));
        MacDataFolder.OnDevice = true;
        MacDataFolder.HomeOverride = folder;
        try
        {
            await test(folder);
        }
        finally
        {
            MacDataFolder.OnDevice = false;
            MacDataFolder.HomeOverride = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("grc", true, "Greek Lemma Data")]
    [InlineData("lat", true, "Latin Lemma Data")]
    [InlineData("eng", true, null)]
    [InlineData("grc", false, null)]
    public void Word_Study_says_what_it_needs_on_an_iPad(string language, bool onDevice, string? step)
    {
        MacDataFolder.OnDevice = onDevice;
        try
        {
            Assert.Equal(step, MainWindow.LemmaDataFor(language));
        }
        finally
        {
            MacDataFolder.OnDevice = false;
        }
    }

    [Fact]
    public async Task The_open_source_notices_are_read_in_order_and_point_online_if_missing()
    {
        await OnAnIpad(async folder =>
        {
            Directory.CreateDirectory(folder);
            Assert.Contains("github.com/ClassicaCodex", NoticesWindow.Read(folder));

            await File.WriteAllTextAsync(Path.Combine(folder, NoticesWindow.FileNames[1]), "second");
            await File.WriteAllTextAsync(Path.Combine(folder, NoticesWindow.FileNames[0]), "first");
            Assert.Equal("first\n\nsecond", NoticesWindow.Read(folder));
        });
    }

    /// <summary>
    /// A desktop closes these from the title bar. An iPad has none, and each was
    /// a card with no way off it, until the page host gave them its bar.
    /// </summary>
    [AvaloniaFact]
    public void A_dialog_that_relies_on_its_title_bar_says_so()
    {
        var main = new MainWindow { Width = 1200, Height = 800 };

        Assert.True(new SettingsWindow(main).ClosesFromTitleBar);
        Assert.True(new GuidedSetupWindow(main).ClosesFromTitleBar);
        Assert.True(new AiProjectSuggestionWindow(main, new Work { Title = "Iliad" }, "Homer").ClosesFromTitleBar);

        // It has a Close button of its own, so it stays a card.
        Assert.False(new WordIndexWindow().ClosesFromTitleBar);
    }

    [AvaloniaFact]
    public void Apple_requires_the_privacy_policy_to_be_linked_inside_the_app()
    {
        var shown = new Func<Window, bool>(window => window.GetVisualDescendants()
            .Any(part => part is TextBlock { Text: ProjectLinks.PrivacyPolicy } or Button { Content: ProjectLinks.PrivacyPolicy }));

        var desktop = new AboutWindow();
        desktop.Show();
        Assert.False(shown(desktop));
        desktop.Close();

        MacDataFolder.OnDevice = true;
        try
        {
            var device = new AboutWindow();
            device.Show();
            Assert.True(shown(device));
            device.Close();
        }
        finally
        {
            MacDataFolder.OnDevice = false;
        }
    }

    [Theory]
    [InlineData("the click of the latch, and a mouse ran to the wheel")]
    [InlineData("He heard one click a moment before the door gave way.")]
    [InlineData("μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος")]
    public void A_text_being_read_is_never_reworded(string line) =>
        Assert.Equal(line, SystemWords.ForTouch(line));

    [Fact]
    public void Harmony_stays_off_the_lists_until_it_has_a_page_a_device_can_open()
    {
        Assert.Contains(ToolCatalog.Offered, tool => tool.Key == "harmonics");

        MacDataFolder.OnDevice = true;
        try
        {
            Assert.DoesNotContain(ToolCatalog.Offered, tool => tool.Key == "harmonics");
            Assert.Contains(ToolCatalog.Offered, tool => tool.Key == "antikythera");
        }
        finally
        {
            MacDataFolder.OnDevice = false;
        }
    }

    [AvaloniaFact]
    public void A_device_is_offered_Quick_Start_and_says_how_much_it_will_download()
    {
        MacDataFolder.OnDevice = true;
        try
        {
            var chooser = new SetupChoiceWindow(libraryIsEmpty: true);
            chooser.Show();

            var said = chooser.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
            Assert.Contains("Quick Start", said);
            Assert.Contains(said, text => text != null && text.Contains("about 2 GB of space"));
            Assert.DoesNotContain("Advanced Setup", said);
            chooser.Close();
        }
        finally
        {
            MacDataFolder.OnDevice = false;
        }
    }

    [Fact]
    public void Speech_goes_to_the_device_and_Greek_is_transliterated_first()
    {
        var engine = new FakeSpeech();
        SpeechService.EngineInstead = engine;

        try
        {
            Assert.True(SpeechService.IsAvailable);

            SpeechService.Speak("μῆνιν", "grc");
            var spoken = Assert.Single(engine.Spoken);
            Assert.DoesNotContain("μ", spoken);
            Assert.True(engine.Stopped > 0);
        }
        finally
        {
            SpeechService.EngineInstead = null;
        }
    }

    private sealed class FakeSpeech : ISpeechEngine
    {
        public List<string> Spoken { get; } = new();

        public int Stopped { get; private set; }

        public bool IsSpeaking => false;

        public IReadOnlyList<InstalledVoiceToken> Voices() => Array.Empty<InstalledVoiceToken>();

        public void Speak(string text, string? voiceName) => Spoken.Add(text);

        public void Stop() => Stopped++;
    }

    private sealed class FakeStore : ISecretStore
    {
        private readonly Dictionary<string, string> _items = new();

        public string? LastService { get; private set; }

        public string? Read(string service, string account)
        {
            LastService = service;
            return _items.GetValueOrDefault(account);
        }

        public bool Write(string service, string account, string? secret)
        {
            LastService = service;
            if (string.IsNullOrEmpty(secret)) _items.Remove(account);
            else _items[account] = secret;
            return true;
        }
    }
}
