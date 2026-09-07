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
        try
        {
            var directory = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsFile, voiceName.Trim());
        }
        catch
        {
            // Same reasoning as the getter above, and as every other
            // preference writer in the application: a voice choice that does
            // not survive the session is a small loss, and not one worth
            // throwing over.
            //
            // This one mattered more than most because of where it runs from.
            // Opening the Translate dialog fills the voice list and selects
            // one, which raises SelectedIndexChanged, which lands here - so
            // the write happens while the constructor is still running, and
            // an unwritable settings folder took the whole dialog down rather
            // than merely forgetting the voice. The getter beside it already
            // carried a comment about not throwing out of the constructor
            // path; the setter on that same path did not.
        }
    }
}
