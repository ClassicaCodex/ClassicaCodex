namespace ClassicaCodex.UI;

/// <summary>
/// Whether the two reader panes scroll and select together.
///
/// Linked panes are right for verse, where a translation keeps roughly the
/// original's line structure and line 40 really does sit opposite line 40.
/// They are wrong for prose, where one Greek sentence can become three
/// English ones and the two sides drift steadily apart - at which point the
/// mirroring is actively fighting the reader, dragging one pane away from
/// the passage they were reading every time they scroll the other.
///
/// That drift is inherent to matching by line index and not something to be
/// fixed by a cleverer rule, so the honest answer is a switch.
///
/// Stored as a plain file under %LocalAppData%, the same approach
/// ReadingPosition and SpeechSettings use, and read the same way: absent
/// means on, because a preference file that has never been written should
/// mean the default rather than "off".
/// </summary>
public static class PaneSyncSettings
{
    private static string EnabledFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "sync-reader-panes.txt");

    public static bool Enabled
    {
        get
        {
            try
            {
                return !File.Exists(EnabledFile)
                       || !File.ReadAllText(EnabledFile).Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }
        set
        {
            try
            {
                var directory = Path.GetDirectoryName(EnabledFile);
                if (directory != null) Directory.CreateDirectory(directory);

                File.WriteAllText(EnabledFile, value ? "on" : "off");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Failing to persist is not worth interrupting reading over -
                // the setting still applies for this session.
            }
        }
    }
}
