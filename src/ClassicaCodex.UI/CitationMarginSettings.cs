namespace ClassicaCodex.UI;

/// <summary>
/// Whether the reader prints references in the margin.
///
/// On by default, because a reader who cannot see where they are has to hover
/// over a line to find out, and the reference is the thing the rest of this
/// application exists to let them do something with.
///
/// A switch rather than a certainty because the margin costs width, and a
/// narrow window reading verse in a large font has little to spare. Anyone
/// reading rather than citing can have it back.
///
/// Stored as a plain file under %LocalAppData%, the same way
/// <see cref="PaneSyncSettings"/> and ReadingPosition do, and read the same
/// way: absent means on, because a preference never written should mean the
/// default rather than "off".
/// </summary>
public static class CitationMarginSettings
{
    private static string EnabledFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "citation-margin.txt");

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
