namespace ClassicaCodex.UI;

/// <summary>
/// The collection a work should open on when several of them carry it - see
/// PreferredEdition for what the preference means and why it is expressed per
/// collection rather than per work.
///
/// Stored as a plain file under %LocalAppData%, the same way PaneSyncSettings
/// and ReadingPosition are, and for the same reason: this is a preference
/// about how to read, not part of the library, so it belongs beside the
/// application rather than inside the database file people copy around as
/// their backup.
///
/// Absent or empty means no preference, which is both the default and what
/// anyone with a single collection should never have to think about.
/// </summary>
public static class PreferredCollectionSettings
{
    private static string PreferenceFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "preferred-collection.txt");

    /// <summary>
    /// The stored collection key, or null for no preference.
    ///
    /// The key is not checked against what is installed, here or on the way
    /// in. A preference naming a collection that is not currently loaded
    /// simply never matches, and survives being set aside and reinstalled -
    /// validating it would mean silently discarding a still-wanted preference
    /// the first time this is read against a partial library.
    /// </summary>
    public static string? Preferred
    {
        get
        {
            try
            {
                if (!File.Exists(PreferenceFile)) return null;

                var stored = File.ReadAllText(PreferenceFile).Trim();
                return stored.Length == 0 ? null : stored;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        set
        {
            try
            {
                var directory = Path.GetDirectoryName(PreferenceFile);
                if (directory != null) Directory.CreateDirectory(directory);

                File.WriteAllText(PreferenceFile, value?.Trim() ?? string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Failing to persist is not worth interrupting setup over -
                // the preference still applies for this session.
            }
        }
    }
}
