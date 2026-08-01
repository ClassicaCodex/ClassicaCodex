namespace ClassicaCodex.UI;

/// <summary>
/// Remembers which installed voice Read Aloud should use, across sessions -
/// the same plain-file-under-%LocalAppData% approach TranslationSettings
/// and DbConnectionFactory's remembered database path already use, for the
/// same reason: this is machine-local configuration, not corpus data.
///
/// Only a voice *name* is stored, not an index or any other identifier -
/// SpeechSynthesizer.GetInstalledVoices() is exactly the same list Windows'
/// own Settings > Time &amp; Language > Speech page manages, so voices can be
/// added or removed there independently of this app. Storing anything but
/// the name would risk pointing at the wrong voice, or a voice that no
/// longer exists, the next time the list changes.
/// </summary>
public static class SpeechSettings
{
    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "speech-settings.txt");

    /// <summary>Null when no voice has been explicitly chosen yet - callers fall back to whatever SpeechSynthesizer's own default is.</summary>
    public static string? PreferredVoiceName
    {
        get
        {
            try
            {
                return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null;
            }
            catch
            {
                // Unreadable preference file - behave as if unset rather
                // than throwing on startup over a corrupted settings file,
                // same fallback DbConnectionFactory's own preference file uses.
                return null;
            }
        }
    }

    public static void SetPreferredVoice(string voiceName)
    {
        var directory = Path.GetDirectoryName(SettingsFile)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsFile, voiceName.Trim());
    }
}
