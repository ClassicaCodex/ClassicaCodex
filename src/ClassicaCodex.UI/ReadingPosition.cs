namespace ClassicaCodex.UI;

/// <summary>
/// Where you were last reading, so the next launch opens there instead of
/// nothing.
///
/// Kept in a plain file under %LocalAppData% rather than in the library,
/// alongside the remembered database path, the chosen voice, and the theme.
/// This is machine-local session state, not something about the texts: two
/// people sharing a database file have different places in it, and nothing
/// in the app ever queries against it.
///
/// Stored as a work's CTS URN and a citation reference, never as database
/// ids. Ids are assigned locally and renumber completely when a corpus is
/// re-ingested into a fresh file - the position would then point confidently
/// at an entirely different passage, which is worse than having no position
/// at all. A URN and a citation ref either resolve to the same passage they
/// always did or resolve to nothing.
/// </summary>
public static class ReadingPosition
{
    private static string EnabledFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "reopen-last-passage.txt");

    /// <summary>
    /// Whether the last passage is reopened on launch.
    ///
    /// On unless it has been turned off. Reopening a long work costs a
    /// moment at startup, and someone who reads mostly short texts will
    /// never notice while someone who opens the Iliad every morning might -
    /// so it's worth being able to decline rather than something to endure.
    ///
    /// Stored as its own tiny file rather than folded into the position
    /// file, so that turning the feature off doesn't discard where you were:
    /// switch it back on and the position is still there.
    /// </summary>
    public static bool ReopenOnLaunch
    {
        get
        {
            try
            {
                // Absent means on. A preference file that has never been
                // written should mean the default, not "off".
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
            }
        }
    }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "reading-position.txt");

    /// <summary>Null when nothing has been read yet, or the file is unreadable.</summary>
    public static (string WorkCtsUrn, string CitationRef)? Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return null;

            var lines = File.ReadAllLines(SettingsFile);
            if (lines.Length < 2) return null;

            var urn = lines[0].Trim();
            var citation = lines[1].Trim();

            return urn.Length == 0 || citation.Length == 0 ? null : (urn, citation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Behave as if unset rather than failing a launch over a
            // preference file, the same fallback the other settings use.
            return null;
        }
    }

    public static void Save(string workCtsUrn, string citationRef)
    {
        if (string.IsNullOrWhiteSpace(workCtsUrn) || string.IsNullOrWhiteSpace(citationRef)) return;

        try
        {
            var directory = Path.GetDirectoryName(SettingsFile);
            if (directory != null) Directory.CreateDirectory(directory);

            File.WriteAllLines(SettingsFile, new[] { workCtsUrn.Trim(), citationRef.Trim() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a reading position is not worth interrupting anyone
            // over; it just means the next launch opens where it would have
            // before this existed.
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SettingsFile)) File.Delete(SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
