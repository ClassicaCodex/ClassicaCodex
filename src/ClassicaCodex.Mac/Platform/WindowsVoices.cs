using System.Runtime.Versioning;
using System.Speech.Synthesis;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// Read Aloud on Windows: SAPI, as the Windows app does it, and so the same
/// voices, the ones in Settings > Time &amp; Language > Speech.
///
/// One synthesizer for the life of the process, made on first use, since
/// making one talks to the speech subsystem. It can fail on a machine with no
/// engine registered, and then <see cref="IsAvailable"/> is false.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsVoices
{
    private static readonly Lazy<SpeechSynthesizer?> Synthesizer = new(Create);

    public static bool IsAvailable => Synthesizer.Value != null;

    public static bool IsSpeaking => Synthesizer.Value?.State == SynthesizerState.Speaking;

    private static SpeechSynthesizer? Create()
    {
        try
        {
            var synthesizer = new SpeechSynthesizer();
            Select(synthesizer, SpeechSettings.PreferredVoiceName);
            return synthesizer;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException or TypeInitializationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    public static IReadOnlyList<InstalledVoiceToken> Installed()
    {
        var synthesizer = Synthesizer.Value;
        if (synthesizer == null) return Array.Empty<InstalledVoiceToken>();

        return synthesizer.GetInstalledVoices()
            .Where(voice => voice.Enabled)
            .Select(voice => new InstalledVoiceToken(voice.VoiceInfo.Name, voice.VoiceInfo.Culture.DisplayName, voice.VoiceInfo.Gender.ToString()))
            .ToList();
    }

    public static void Use(string voiceName)
    {
        if (Synthesizer.Value is { } synthesizer) Select(synthesizer, voiceName);
    }

    public static void Speak(string text)
    {
        if (Synthesizer.Value is not { } synthesizer) return;

        // Starting a passage replaces the last one rather than queuing behind it.
        synthesizer.SpeakAsyncCancelAll();
        synthesizer.SpeakAsync(text);
    }

    public static void Stop() => Synthesizer.Value?.SpeakAsyncCancelAll();

    private static void Select(SpeechSynthesizer synthesizer, string? voiceName)
    {
        if (string.IsNullOrWhiteSpace(voiceName)) return;

        try
        {
            synthesizer.SelectVoice(voiceName);
        }
        catch (ArgumentException)
        {
            // Removed from Windows since it was chosen, so the default speaks.
        }
    }
}
