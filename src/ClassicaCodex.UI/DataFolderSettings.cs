namespace ClassicaCodex.UI;

/// <summary>
/// Where the downloaded source data lives - the texts, dictionaries and lemma
/// data the setup steps fetch, as opposed to the library database they are
/// ingested into.
///
/// Selectable because of how much of it there is. A full set measures about
/// 8.9 GB, most of it the two lemma collections (3.6 GB Greek, 1.9 GB Latin),
/// and the one place it used to go was a fixed folder under My Documents -
/// which is on the system drive, which is exactly the drive someone with a
/// small SSD does not have nine spare gigabytes on. The database has been
/// movable since long before this; the far larger download was not.
///
/// Stored as a plain file under %LocalAppData% beside the other preferences,
/// and deliberately NOT inside the database: it says where to put things on
/// this machine, so it should not travel with a library file copied to
/// another one.
///
/// Unset means the old fixed location, so an existing install keeps working
/// without being asked anything and without moving a byte.
/// </summary>
public static class DataFolderSettings
{
    private static string PreferenceFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "data-path.txt");

    /// <summary>
    /// The folder every download goes into and every setup step reads back
    /// from, and the folder the map file is found under.
    ///
    /// One property rather than one per consumer, which is the invariant the
    /// map's own note used to hold by being a constant: the download location
    /// and the "is this already here?" check must not be able to disagree.
    /// Making it configurable does not weaken that - they still both read
    /// this - it only makes the one answer choosable.
    ///
    /// Never returns empty: a blank or unreadable preference falls back to
    /// <see cref="DefaultRoot"/> rather than to the current directory, which
    /// is what an empty path would otherwise mean to Path.Combine.
    /// </summary>
    /// <summary>
    /// The choice made this session, held separately from the file so that a
    /// preference which could not be written still governs this run.
    ///
    /// Without it, a failed write meant the setter swallowed the error and the
    /// very next read returned the old folder - after the wizard had already
    /// told the reader the new one was ready, and while the downloads went
    /// somewhere they had not chosen. Now a failure costs the choice at the
    /// next launch, which is what the comment in the setter always claimed.
    /// </summary>
    private static string? _chosenThisSession;

    public static string Root
    {
        get
        {
            if (_chosenThisSession != null) return _chosenThisSession;

            try
            {
                if (!File.Exists(PreferenceFile)) return DefaultRoot;

                var stored = File.ReadAllText(PreferenceFile).Trim();
                return stored.Length == 0 ? DefaultRoot : stored;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return DefaultRoot;
            }
        }
        set
        {
            var chosen = value?.Trim() ?? string.Empty;

            // An empty choice means the default rather than the current
            // directory, which is what an empty path would mean to
            // Path.Combine - see the note on the getter.
            _chosenThisSession = chosen.Length == 0 ? DefaultRoot : chosen;

            try
            {
                var directory = Path.GetDirectoryName(PreferenceFile);
                if (directory != null) Directory.CreateDirectory(directory);

                File.WriteAllText(PreferenceFile, chosen);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not worth interrupting setup over. The choice still applies
                // for this session - see _chosenThisSession - and it is only
                // the next launch that forgets it.
            }
        }
    }

    /// <summary>
    /// Where the data went before this was choosable, and where it still goes
    /// unless someone says otherwise. Existing installs are all here.
    /// </summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClassicaCodexData");

    /// <summary>Whether a folder has been chosen, as opposed to inherited.</summary>
    public static bool IsCustom =>
        !string.Equals(Root, DefaultRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether downloads can actually be written to this folder, creating it if
    /// it is not there yet.
    ///
    /// Here rather than in either setup window so that the two cannot come to
    /// different conclusions about the same folder - the same reasoning that
    /// keeps the download location and the "is this already here?" check in one
    /// place. Neither window should be deciding this for itself.
    ///
    /// Creating the folder is part of the question: someone typing a path, or
    /// using New Folder in the browser dialog, may be naming somewhere that
    /// does not exist, and discovering that at the start of an hour-long
    /// download is discovering it too late.
    /// </summary>
    public static bool TryPrepare(string? chosen, out string resolved, out string? error)
    {
        resolved = string.Empty;
        error = null;

        var candidate = chosen?.Trim() ?? string.Empty;
        if (candidate.Length == 0)
        {
            error = "Enter a folder, or click Browse.";
            return false;
        }

        try
        {
            resolved = Path.GetFullPath(candidate);
            Directory.CreateDirectory(resolved);

            // Written and deleted rather than inferred from attributes. A
            // folder can look writable and not be - a network share, a signed
            // out cloud folder, a drive mounted read-only - and every one of
            // those fails at the first download instead of here.
            var probe = Path.Combine(resolved, ".classicacodex-write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException
                                      or PathTooLongException or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Whether this folder could plausibly take downloads, without creating
    /// anything to find out.
    ///
    /// For the wizard's tick mark, which is drawn before anyone has chosen
    /// anything. A folder that does not exist yet is not a problem - the
    /// default never exists on a fresh install - so what matters is whether
    /// some ancestor of it does, which is what makes it creatable when the
    /// moment comes. <see cref="TryPrepare"/> is the real answer; this is the
    /// one that can be asked cheaply and without side effects.
    /// </summary>
    public static bool LooksUsable(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;

        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(folder));

            while (directory != null)
            {
                if (directory.Exists) return true;
                directory = directory.Parent;
            }

            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException or System.Security.SecurityException
                                      or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// How much room is left where the downloads are going, or null where that
    /// cannot be established - a network path, or a drive that will not answer.
    /// Not knowing is no reason to refuse a folder, so callers say less rather
    /// than nothing.
    /// </summary>
    public static double? FreeGigabytesAt(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(folder);
            if (string.IsNullOrEmpty(root)) return null;

            return new DriveInfo(root).AvailableFreeSpace / 1024d / 1024d / 1024d;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a full set of downloads comes to, measured on a complete install:
    /// 8.92 GB, of which the Greek and Latin word-form data are 5.5 GB between
    /// them. The threshold callers warn below is deliberately above this, since
    /// a git clone needs room for its working copy as well as its history.
    /// </summary>
    public const double FullSetGigabytes = 9;
    public const double ComfortableFreeGigabytes = 12;
}
