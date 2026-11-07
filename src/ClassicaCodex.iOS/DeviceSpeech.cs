using AVFoundation;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.iOS;

/// <summary>
/// Read Aloud through the voices on the iPad. A Mac starts <c>say</c>, and iOS
/// does not let an app start a process. With no voice chosen this uses the
/// system's own, as a Mac does.
/// </summary>
internal sealed class DeviceSpeech : ISpeechEngine
{
    private readonly AVSpeechSynthesizer _synthesizer = new();

    public bool IsSpeaking => _synthesizer.Speaking;

    public IReadOnlyList<InstalledVoiceToken> Voices() =>
        AVSpeechSynthesisVoice.GetSpeechVoices()
            .Select(voice => new InstalledVoiceToken(voice.Name, DisplayName(voice.Language), string.Empty))
            .ToList();

    public void Speak(string text, string? voiceName)
    {
        var utterance = new AVSpeechUtterance(text);

        var voice = AVSpeechSynthesisVoice.GetSpeechVoices().FirstOrDefault(v => v.Name == voiceName);
        if (voice != null) utterance.Voice = voice;

        _synthesizer.SpeakUtterance(utterance);
    }

    public void Stop() => _synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);

    private static string DisplayName(string language)
    {
        try
        {
            return System.Globalization.CultureInfo.GetCultureInfo(language).DisplayName;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return language;
        }
    }
}
