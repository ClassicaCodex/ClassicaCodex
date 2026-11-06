using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ClassicaCodex.Mac.Features.Help;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// What the Mac front end does on Linux. The wording and the parsing are
/// checked on any machine; what needs a real Linux desktop returns early
/// elsewhere, and is run by the Linux workflow.
/// </summary>
public class LinuxTests
{
    /// <summary>Every word that means something on a Mac and nothing, or something wrong, elsewhere.</summary>
    internal static readonly string[] MacOnly =
    {
        "⌘", "⇧", "⌥", "⌃", "Finder", "macOS", "Keychain Access", "keychain", "System Settings", "Safari",
        "Time Machine", "Font Book", "AirDrop", "Go to Folder", "CoreText", "Geeza", "Option-", "Option key"
    };

    [Theory]
    [InlineData("Linux")]
    [InlineData("Windows")]
    public void Help_reads_with_nothing_of_the_Mac_left_in_it(string system)
    {
        var left = new List<string>();

        foreach (var (title, body) in HelpTopics.All)
        {
            var expanded = HelpOutline.Expand(body);
            var text = system == "Linux" ? SystemWords.ForLinux(expanded) : SystemWords.ForWindows(expanded);

            foreach (var word in MacOnly)
            {
                if (text.Contains(word, StringComparison.Ordinal)) left.Add($"{title}: {word}");
            }

            foreach (System.Text.RegularExpressions.Match mac in System.Text.RegularExpressions.Regex.Matches(text, @".{0,30}\bMacs?\b.{0,30}"))
            {
                left.Add($"{title}: {mac.Value}");
            }
        }

        Assert.True(left.Count == 0, string.Join(Environment.NewLine, left));
    }

    [Theory]
    [InlineData("⌘F", "Ctrl+F")]
    [InlineData("⇧⌘C", "Ctrl+Shift+C")]
    [InlineData("⌥⌘S", "Ctrl+Alt+S")]
    [InlineData("⌃⇧Return", "Ctrl+Shift+Return")]
    [InlineData("⌘Return to bookmark", "Ctrl+Enter to bookmark")]
    [InlineData("⇧-click or ⌘-click", "Shift-click or Ctrl-click")]
    [InlineData("Show in Finder", "Show in Folder")]
    [InlineData("Follow macOS", "Follow System")]
    public void Keys_and_names_read_the_Linux_way(string mac, string linux) =>
        Assert.Equal(linux, SystemWords.ForLinux(mac));

    [Fact]
    public void The_tests_keep_the_Mac_wording_on_any_machine() =>
        Assert.Equal("Show in Finder", SystemWords.Say("Show in Finder"));

    [Fact]
    public void Espeak_voices_are_read_from_its_list()
    {
        const string list = """
            Pty Language       Age/Gender VoiceName          File                 Other Languages
             5  af              --/M      Afrikaans          gmw/af
             5  en-gb           --/M      English_(Great_Britain) gmw/en
             2  el              --/M      Greek              grk/el
            """;

        var voices = SpeechService.ParseEspeakVoices(list);

        Assert.Equal(new[] { "af", "en-gb", "el" }, voices.Select(v => v.Name));
        Assert.Equal("English (Great Britain)", voices[1].CultureDisplayName);
    }

    /// <summary>
    /// Runs where a keyring answers. CC_EXPECT_KEYRING is set by the Linux
    /// workflow, so that a keyring failing to start fails there instead of
    /// quietly passing.
    /// </summary>
    [Fact]
    public void A_key_goes_into_the_keyring_and_comes_out_unchanged()
    {
        var expected = Environment.GetEnvironmentVariable("CC_EXPECT_KEYRING") == "1";
        if (!SecretService.IsAvailable)
        {
            Assert.False(expected, "secret-tool was not found.");
            return;
        }

        const string service = "Classica Codex tests";
        const string account = "A test key";

        if (!SecretService.Write(service, account, "sk-test"))
        {
            Assert.False(expected, "The keyring refused the key.");
            return;
        }

        Assert.Equal("sk-test", SecretService.Read(service, account));

        Assert.True(SecretService.Write(service, account, "sk-other"));
        Assert.Equal("sk-other", SecretService.Read(service, account));

        Assert.True(SecretService.Write(service, account, null));
        Assert.Null(SecretService.Read(service, account));
    }

    /// <summary>Needs the Noto fonts, which the Linux workflow installs and says so.</summary>
    [AvaloniaFact]
    public void The_ancient_scripts_are_given_the_Noto_fonts()
    {
        if (Environment.GetEnvironmentVariable("CC_EXPECT_NOTO") != "1") return;

        Assert.Equal("Noto Sans Cuneiform", ScriptFonts.FamilyFor("\U00012000"));
        Assert.Equal("Noto Sans Egyptian Hieroglyphs", ScriptFonts.FamilyFor("\U00013000"));
        Assert.Equal("Noto Serif Hebrew", ScriptFonts.FamilyFor("בְּרֵאשִׁית"));
        Assert.Equal("Noto Naskh Arabic", ScriptFonts.FamilyFor("بسم الله"));
    }

    [AvaloniaFact]
    public void Off_a_Mac_the_window_answers_the_keys_its_menu_shows()
    {
        var main = new MainWindow();
        var keys = main.KeyBindings.Select(k => k.Gesture.ToString()).ToList();

        if (OperatingSystem.IsMacOS())
        {
            Assert.Empty(keys);
            return;
        }

        foreach (var chord in new[] { "Ctrl+O", "Ctrl+E", "Ctrl+W", "Ctrl+F", "Ctrl+L", "Ctrl+Shift+C", "Ctrl+Alt+S", "Ctrl+Shift+T", "Ctrl+OemComma", "Ctrl+OemOpenBrackets" })
        {
            Assert.Contains(KeyGesture.Parse(chord).ToString(), keys);
        }

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
