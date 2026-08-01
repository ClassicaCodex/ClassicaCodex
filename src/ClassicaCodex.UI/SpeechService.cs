using System.Speech.Synthesis;

namespace ClassicaCodex.UI;

/// <summary>
/// Reads a passage aloud using whatever speech voice is installed on the
/// Windows machine - the free, fully offline side of Read Aloud. Unlike
/// Translate or Cross-Language Echo, this never touches the network and
/// never needs a confirmation dialog: nothing leaves the machine.
///
/// Greek text is run through GreekPhoneticTransliterator first - tested on
/// a real machine, a stock English voice reads raw polytonic Greek by
/// naming each Unicode character ("alpha, lambda, omicron...") rather than
/// attempting to pronounce it at all, so there's nothing to lose and a real
/// (if rough) chance of sounding like an attempted word instead. Latin and
/// English are sent through unchanged - both were tested and sounded fine
/// as-is.
///
/// Voice selection reads from whatever SpeechSynthesizer.GetInstalledVoices()
/// returns, which is exactly Windows' own Settings > Time &amp; Language >
/// Speech list - this app has no way to add voices Windows doesn't already
/// have (an "Irish female" voice, for instance, needs to be installed there
/// first; see TranslateForm's Listen section for exactly where to send
/// someone looking for one).
/// </summary>
public static class SpeechService
{
    // One shared instance for the process's lifetime, created on first use -
    // same reasoning as the AI translation services' shared HttpClient.
    // Lazy rather than eager because constructing a SpeechSynthesizer talks
    // to Windows' speech subsystem immediately, which is pointless cost to
    // pay on every app launch if Read Aloud is never opened.
    private static readonly Lazy<SpeechSynthesizer?> LazySynthesizer = new(CreateSynthesizer);

    private static SpeechSynthesizer? CreateSynthesizer()
    {
        try
        {
            var synth = new SpeechSynthesizer();

            var preferredName = SpeechSettings.PreferredVoiceName;
            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                // SelectVoice throws if the name isn't currently installed -
                // entirely possible if a voice was removed in Windows
                // Settings since it was last chosen here. Falls back to
                // whatever SAPI's own default is rather than failing the
                // whole service over one missing voice.
                try { synth.SelectVoice(preferredName); }
                catch (ArgumentException) { /* voice no longer installed - keep the default */ }
            }

            return synth;
        }
        catch
        {
            // No speech engine registered at all - rare on an ordinary
            // Windows 10/11 install, but not guaranteed on every SKU or a
            // locked-down machine. Everything here degrades to a clear
            // message rather than crashing; see IsAvailable.
            return null;
        }
    }

    /// <summary>False when no speech engine could be created at all - callers show a clear message instead of silently doing nothing.</summary>
    public static bool IsAvailable => LazySynthesizer.Value != null;

    /// <summary>True while a Speak call is still playing - TranslateForm's Listen section uses this to keep its Stop button enabled only when there's something to stop.</summary>
    public static bool IsSpeaking => LazySynthesizer.Value?.State == SynthesizerState.Speaking;

    /// <summary>Every voice Windows currently has installed - the same list Settings > Time &amp; Language > Speech manages.</summary>
    public static IReadOnlyList<InstalledVoiceToken> GetInstalledVoices()
    {
        var synth = LazySynthesizer.Value;
        if (synth == null) return Array.Empty<InstalledVoiceToken>();

        return synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => new InstalledVoiceToken(v.VoiceInfo.Name, v.VoiceInfo.Culture.DisplayName, v.VoiceInfo.Gender.ToString()))
            .ToList();
    }

    /// <summary>Selects a voice by name for this and future sessions. Ignored silently if the name isn't currently installed.</summary>
    public static void SetVoice(string voiceName)
    {
        var synth = LazySynthesizer.Value;
        if (synth == null) return;

        try
        {
            synth.SelectVoice(voiceName);
            SpeechSettings.SetPreferredVoice(voiceName);
        }
        catch (ArgumentException)
        {
            // Not a currently installed voice name - nothing to select.
        }
    }

    /// <summary>
    /// Speaks the given text asynchronously, transliterating first if the
    /// language is Ancient Greek. Always cancels whatever was already
    /// playing first, so starting a new passage interrupts the last one
    /// rather than queuing behind it or erroring.
    /// </summary>
    public static void Speak(string text, string? language)
    {
        var synth = LazySynthesizer.Value;
        if (synth == null || string.IsNullOrWhiteSpace(text)) return;

        var toSpeak = string.Equals(language, "grc", StringComparison.OrdinalIgnoreCase)
            ? GreekPhoneticTransliterator.ToPhoneticLatin(text)
            : text;

        synth.SpeakAsyncCancelAll();
        synth.SpeakAsync(toSpeak);
    }

    public static void Stop()
    {
        LazySynthesizer.Value?.SpeakAsyncCancelAll();
    }
}

/// <summary>
/// The bit of an installed SAPI voice TranslateForm's picker actually needs
/// to display and act on - deliberately not exposing System.Speech's own
/// VoiceInfo type outside this file, so a future change to how voices are
/// enumerated doesn't ripple into UI code.
/// </summary>
public record InstalledVoiceToken(string Name, string CultureDisplayName, string Gender);
