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

            // SuggestedRoot rather than DefaultRoot for the fallbacks, so that
            // the box, the status line, the tick and the downloads themselves
            // cannot disagree about where a machine with no preference is
            // going to put nine gigabytes. It answers DefaultRoot in every case
            // except one: a default folder that is inside a synced folder and
            // does not exist yet. See SuggestedRoot.
            try
            {
                if (!File.Exists(PreferenceFile)) return SuggestedRoot;

                var stored = File.ReadAllText(PreferenceFile).Trim();
                return stored.Length == 0 ? SuggestedRoot : stored;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return SuggestedRoot;
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

    /// <summary>
    /// What to put in the box on a machine that has never chosen - which is not
    /// always <see cref="DefaultRoot"/>.
    ///
    /// <b>MyDocuments is not always in Documents.</b> On a Windows 11 install
    /// signed into a Microsoft account, OneDrive folder backup is on by default
    /// and the known folder is redirected, so MyDocuments resolves to
    /// %USERPROFILE%\OneDrive\Documents. The default download folder is then
    /// inside a synced folder, and the corpus steps put about nine gigabytes of
    /// small XML files into it - tens of thousands of them - which OneDrive
    /// then starts uploading to an account whose free tier is five.
    ///
    /// What that costs someone is quota warnings, their other backed-up files
    /// silently ceasing to sync, and on a metered connection an actual bill,
    /// ten minutes after installing a reading application. The free-space check
    /// cannot see it coming either: it asks DriveInfo about the local disk and
    /// reports "250 GB free" quite correctly.
    ///
    /// None of this is worth syncing in any case. These folders hold downloaded
    /// source files that can be fetched again at any time; the library built
    /// from them is in the database, which lives under LocalApplicationData and
    /// is never redirected.
    ///
    /// <b>Only when there is nothing there yet.</b> If the default folder
    /// already exists this returns it unchanged, because an install that has
    /// been downloading into a synced Documents for months should not be
    /// silently pointed somewhere empty. <see cref="DefaultRoot"/> itself is
    /// untouched for the same reason - it is what Root falls back to when no
    /// preference was ever written, so moving it would move existing installs.
    /// </summary>
    public static string SuggestedRoot
    {
        get
        {
            var standard = DefaultRoot;

            if (CloudSyncedBy(standard) == null) return standard;

            try
            {
                if (Directory.Exists(standard)) return standard;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return standard;
            }

            // The profile root is visible, writable without elevation, and the
            // one place a known-folder redirection cannot follow.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ClassicaCodexData");
        }
    }

    /// <summary>
    /// The name of the service syncing this folder to the cloud, or null if
    /// nothing appears to be.
    ///
    /// OneDrive is asked about by environment variable, which is what it sets
    /// for its own configured roots and is reliable whether or not the folder
    /// is called OneDrive. The others are recognised by their folder name,
    /// which is weaker but only ever produces a note - nothing is refused on
    /// the strength of it, so a false positive costs a sentence and a false
    /// negative costs what it cost before this existed.
    /// </summary>
    public static string? CloudSyncedBy(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }

        foreach (var variable in new[] { "OneDriveConsumer", "OneDriveCommercial", "OneDrive" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root) && IsUnder(full, root)) return "OneDrive";
        }

        foreach (var segment in full.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            // Equals, or the business naming - "OneDrive - Contoso". Not a
            // bare StartsWith, which claims OneDriveBackup as well and would
            // tell someone their ordinary folder is being uploaded.
            if (segment.Equals("OneDrive", StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase)) return "OneDrive";
            if (segment.Equals("Dropbox", StringComparison.OrdinalIgnoreCase)) return "Dropbox";
            if (segment.Equals("Google Drive", StringComparison.OrdinalIgnoreCase)) return "Google Drive";
            if (segment.Equals("iCloudDrive", StringComparison.OrdinalIgnoreCase)) return "iCloud Drive";
        }

        return null;
    }

    /// <summary>
    /// Whether one path sits inside another. Compared segment-wise via a
    /// trailing separator, so C:\Users\x\OneDriveBackup is not read as being
    /// inside C:\Users\x\OneDrive.
    /// </summary>
    private static bool IsUnder(string path, string candidateParent)
    {
        try
        {
            var parent = Path.GetFullPath(candidateParent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

            var child = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;

            return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                      or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }

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
