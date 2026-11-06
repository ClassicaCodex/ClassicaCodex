using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Arcade;
using ClassicaCodex.Mac.Features.Export;
using ClassicaCodex.Mac.Features.Help;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// What the front end does on Windows. The wording is checked on any machine;
/// what needs real Windows returns early elsewhere, and CC_EXPECT_WINDOWS,
/// set by the Windows workflow, makes that a failure there, so that a test
/// cannot pass by not having run.
/// </summary>
public class WindowsTests
{
    /// <summary>Words that are right on Linux, and read as a mistake on Windows.</summary>
    private static readonly string[] LinuxOnly =
    {
        "espeak", "secret-tool", "libsecret", "keyring", "Secret Service", "~/", "Linux", "file manager", "your desktop's", "Noto Serif Hebrew"
    };

    private static bool OnWindows()
    {
        if (OperatingSystem.IsWindows()) return true;

        Assert.NotEqual("1", Environment.GetEnvironmentVariable("CC_EXPECT_WINDOWS"));
        return false;
    }

    [Theory]
    [InlineData("⌘F", "Ctrl+F")]
    [InlineData("⇧⌘C", "Ctrl+Shift+C")]
    [InlineData("⌘Return to bookmark", "Ctrl+Enter to bookmark")]
    [InlineData("Show in Finder", "Show in File Explorer")]
    [InlineData("Show it in the Finder?", "Show it in File Explorer?")]
    [InlineData("Follow macOS", "Follow Windows")]
    [InlineData("Appearance: follows macOS", "Appearance: follows Windows")]
    [InlineData("More voices: System Settings → Accessibility → Spoken Content → System Voice → Manage Voices.", "More voices: Settings → Time & Language → Speech.")]
    public void Keys_and_names_read_the_Windows_way(string mac, string windows) =>
        Assert.Equal(windows, SystemWords.ForWindows(mac));

    [Fact]
    public void Nothing_of_Linux_or_the_Mac_is_said_on_Windows()
    {
        var left = new List<string>();

        foreach (var (mac, _, windows) in SystemWords.Phrases)
        {
            foreach (var word in LinuxOnly.Concat(LinuxTests.MacOnly))
            {
                if (windows.Contains(word, StringComparison.Ordinal)) left.Add($"{word}: {windows}");
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(windows, @"\bMacs?\b")) left.Add(windows);
            if (mac.Length == 0) left.Add("An empty row.");
        }

        foreach (var (title, body) in HelpTopics.All)
        {
            var text = SystemWords.ForWindows(body);

            foreach (var word in LinuxOnly)
            {
                if (text.Contains(word, StringComparison.Ordinal)) left.Add($"{title}: {word}");
            }
        }

        Assert.True(left.Count == 0, string.Join(Environment.NewLine, left));
    }

    [Fact]
    public void Explorer_is_given_the_path_in_quotes_after_the_comma() =>
        Assert.Equal("/select,\"C:\\My Library\\classicacodex.db\"", MacShell.ExplorerArguments("C:\\My Library\\classicacodex.db"));

    /// <summary>
    /// Against the Windows app's own class, compiled into these tests from
    /// where it lives, and in both directions: a reader's keys have to arrive
    /// here, and a key changed here has to reach that app.
    /// </summary>
    [Fact]
    public void Keys_are_shared_with_the_Windows_app_in_its_own_file()
    {
        if (!OnWindows()) return;

        var file = KeyFile();
        File.Delete(file);
        ForgetWhatTheWindowsAppRead();

        ClassicaCodex.UI.TranslationSettings.Save("sk-ant-from-classic", "AIza-from-classic", alwaysConfirm: false);
        Assert.Equal(("sk-ant-from-classic", "AIza-from-classic", false), WindowsKeyFile.Read());

        Assert.True(WindowsKeyFile.Write("sk-ant-from-here", null, alwaysConfirm: true));
        ForgetWhatTheWindowsAppRead();

        Assert.Equal("sk-ant-from-here", ClassicaCodex.UI.TranslationSettings.AnthropicApiKey);
        Assert.Null(ClassicaCodex.UI.TranslationSettings.GeminiApiKey);
        Assert.True(ClassicaCodex.UI.TranslationSettings.AlwaysConfirmBeforeSending);

        var lines = File.ReadAllLines(file);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("dpapi:", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Empty(lines[1]);
        Assert.Equal("confirm", lines[2]);
    }

    [Fact]
    public void A_key_this_account_cannot_read_is_no_key_and_an_old_plain_one_is_read()
    {
        if (!OnWindows()) return;

        File.WriteAllLines(KeyFile(), new[] { "dpapi:bm90IGEga2V5", "sk-ant-from-before-encryption", "noconfirm" });

        Assert.Equal((null, "sk-ant-from-before-encryption", false), WindowsKeyFile.Read());
    }

    [Fact]
    public void An_absent_key_file_asks_before_it_sends()
    {
        if (!OnWindows()) return;

        File.Delete(KeyFile());

        Assert.Equal((null, null, true), WindowsKeyFile.Read());
    }

    /// <summary>The Windows app reads this preference too, so nothing may be proposed over its answer.</summary>
    [Fact]
    public void Setup_writes_no_download_folder_the_Windows_app_did_not_choose()
    {
        if (!OnWindows()) return;

        Assert.Null(MacDataFolder.HomeOverride);
        Assert.Equal(DataFolderSettings.Root, MacDataFolder.Proposed());
    }

    [Fact]
    public void The_fonts_are_found_in_the_Windows_folders()
    {
        if (!OnWindows()) return;

        var catalog = FontCatalog.Scan(FontCatalog.SystemDirectories);

        Assert.True(catalog.Has("Palatino Linotype"));
        Assert.True(catalog.Has("Times New Roman"));

        var resolver = new MacFontResolver(catalog);
        Assert.Equal("Palatino Linotype", resolver.FamilyFor("Palatino, Palatino Linotype, Book Antiqua, DejaVu Serif, FreeSerif, serif"));
    }

    [Fact]
    public void Voices_come_from_SAPI()
    {
        if (!OnWindows()) return;

        Assert.True(SpeechService.IsAvailable);
        Assert.NotEmpty(SpeechService.GetInstalledVoices());

        SpeechService.Speak("arma virumque cano", "lat");
        Assert.True(SpeechService.IsSpeaking);
        SpeechService.Stop();
    }

    [Fact]
    public void The_sound_effects_play_without_a_process_and_leave_nothing_behind()
    {
        if (!OnWindows()) return;

        var sound = new BronzeArcadeSound();
        for (var effect = 0; effect < 4; effect++) sound.Play(effect);
        sound.Dispose();

        Assert.Empty(Directory.GetDirectories(Path.GetTempPath(), $"ClassicaCodex-BronzeAndThunder-{Environment.ProcessId}"));
    }

    /// <summary>Where the key file goes, in a folder that exists whichever test runs first.</summary>
    private static string KeyFile()
    {
        Directory.CreateDirectory(PreferencesFolder.Root);
        return Path.Combine(PreferencesFolder.Root, "translate-settings.txt");
    }

    private static void ForgetWhatTheWindowsAppRead() =>
        typeof(ClassicaCodex.UI.TranslationSettings)
            .GetField("_cached", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
}
