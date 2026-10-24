using System.Diagnostics;
using System.Text.RegularExpressions;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// Read Aloud, through the voices macOS already has.
///
/// Windows hands a passage to SAPI; the Mac equivalent is the system speech
/// engine behind the <c>say</c> command, which every Mac has and which uses
/// whatever voices are installed in System Settings → Accessibility → Spoken
/// Content. The same caveat applies as on Windows, and the window says so:
/// there is no ancient Greek or Latin voice, so Greek is transliterated into
/// Latin letters first (with the same transliterator Windows uses) and Latin
/// is read as it stands, in a modern accent.
///
/// The text goes to <c>say</c> on standard input rather than as an argument,
/// so a long passage is not limited by the command line and nothing in it
/// can be read as an option.
/// </summary>
public static class SpeechService
{
    private static Process? _current;
    private static IReadOnlyList<InstalledVoiceToken>? _voices;

    public static bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/say");

    public static bool IsSpeaking => _current is { HasExited: false };

    /// <summary>
    /// The installed voices, as <c>say -v ?</c> lists them: a name, a locale,
    /// and a sample sentence after a '#'.
    /// </summary>
    public static IReadOnlyList<InstalledVoiceToken> GetInstalledVoices()
    {
        if (_voices != null) return _voices;
        if (!IsAvailable) return _voices = Array.Empty<InstalledVoiceToken>();

        try
        {
            var start = new ProcessStartInfo("/usr/bin/say", "-v ?")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(start);
            if (process == null) return _voices = Array.Empty<InstalledVoiceToken>();

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return _voices = ParseVoices(output);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return _voices = Array.Empty<InstalledVoiceToken>();
        }
    }

    private static readonly Regex VoiceLine = new(@"^(?<name>.+?)\s+(?<locale>[a-z]{2,3}[_-][A-Za-z0-9]+)\s+#", RegexOptions.Compiled);

    internal static IReadOnlyList<InstalledVoiceToken> ParseVoices(string output)
    {
        var voices = new List<InstalledVoiceToken>();

        foreach (var line in output.Split('\n'))
        {
            var match = VoiceLine.Match(line);
            if (!match.Success) continue;

            var locale = match.Groups["locale"].Value.Replace('_', '-');
            string display;
            try { display = System.Globalization.CultureInfo.GetCultureInfo(locale).DisplayName; }
            catch (System.Globalization.CultureNotFoundException) { display = locale; }

            voices.Add(new InstalledVoiceToken(match.Groups["name"].Value.Trim(), display, string.Empty));
        }

        return voices;
    }

    public static void SetVoice(string voiceName) => SpeechSettings.SetPreferredVoice(voiceName);

    public static void Speak(string text, string? language)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text)) return;

        Stop();

        var toSpeak = string.Equals(language, "grc", StringComparison.OrdinalIgnoreCase)
            ? GreekPhoneticTransliterator.ToPhoneticLatin(text)
            : text;

        var start = new ProcessStartInfo("/usr/bin/say")
        {
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        var voice = SpeechSettings.PreferredVoiceName;
        if (!string.IsNullOrWhiteSpace(voice) && GetInstalledVoices().Any(v => v.Name == voice))
        {
            start.ArgumentList.Add("-v");
            start.ArgumentList.Add(voice);
        }

        try
        {
            _current = Process.Start(start);
            if (_current == null) return;

            _current.StandardInput.Write(toSpeak);
            _current.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _current = null;
        }
    }

    public static void Stop()
    {
        try
        {
            if (_current is { HasExited: false }) _current.Kill();
        }
        catch (InvalidOperationException)
        {
            // Finished on its own between the check and the kill.
        }

        _current = null;
    }
}

public record InstalledVoiceToken(string Name, string CultureDisplayName, string Gender);
