using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// What the shared <see cref="DataFolderSettings"/> cannot know about a Mac:
/// where nine gigabytes of downloads should go when nobody has said, which
/// folders iCloud is quietly uploading, and how much room a volume really has.
///
/// <b>The default is in Documents, and Documents is often in iCloud.</b>
/// DataFolderSettings.DefaultRoot is ~/Documents/ClassicaCodexData on a Mac,
/// and on a great many Macs "Desktop &amp; Documents Folders" is switched on
/// in iCloud Drive - Apple offers it during the Mac's own setup. The corpus
/// steps would then put tens of thousands of small XML files into a folder
/// that is being uploaded to an account whose free tier is five gigabytes,
/// which is exactly the OneDrive case DataFolderSettings.SuggestedRoot exists
/// for on Windows. Its CloudSyncedBy recognises OneDrive, Dropbox and Google
/// Drive by name and cannot see iCloud's Desktop &amp; Documents, which keeps
/// the ordinary folder names, so the shared code would let it happen.
///
/// So the Mac proposes ~/ClassicaCodexData - the home folder itself, which
/// iCloud never syncs, as the profile root is the place a Windows known-
/// folder redirection cannot follow. Not ~/Library/Application Support,
/// which would also be safe: the Menota step asks the reader to save files
/// into this folder by hand, and Library is hidden in the Finder.
///
/// None of this edits the shared file. The proposal is applied by setting
/// DataFolderSettings.Root, which is what every step reads.
/// </summary>
internal static class MacDataFolder
{
    public static string Home => HomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// A home folder to look in instead of the real one. Only the tests set
    /// it, to a made-up home with or without iCloud's folders in it - the
    /// detection is about what a home folder contains, and the machine the
    /// tests run on is not a Mac with iCloud signed in.
    /// </summary>
    internal static string? HomeOverride { get; set; }

    /// <summary>The Mac's proposal: a folder in the home folder, never synced.</summary>
    public static string Unsynced(string? home = null) => Path.Combine(home ?? Home, "ClassicaCodexData");

    /// <summary>
    /// The folder setup should use for someone who has not chosen one.
    ///
    /// A folder chosen before, here or on an earlier run, is kept - even a
    /// synced one, which the step then warns about rather than overriding. So
    /// is the shared default when it already holds downloads: an install that
    /// has been filling ~/Documents/ClassicaCodexData should not be pointed
    /// somewhere empty without being asked. Only a machine with nothing there
    /// yet gets the unsynced folder - the same rule SuggestedRoot follows.
    /// </summary>
    public static string Proposed(string? home = null)
    {
        if (DataFolderSettings.IsCustom) return DataFolderSettings.Root;

        var shared = DataFolderSettings.DefaultRoot;
        return HoldsAnything(shared) ? shared : Unsynced(home);
    }

    /// <summary>
    /// Makes <see cref="Proposed"/> the folder in force, when it differs.
    ///
    /// Done as setup opens rather than only when the reader confirms the
    /// Download Folder step, because that step can be skipped - Next on a step
    /// is optional, Advanced Setup never shows it - and a download started from
    /// a later step reads DataFolderSettings.Root as it stands. Leaving the
    /// synced default in force until the reader happened to visit one step is
    /// how nine gigabytes would go to iCloud. On Windows SuggestedRoot does the
    /// same by answering Root's getter; here the answer has to be written.
    /// </summary>
    public static void AdoptProposalIfUnchosen()
    {
        var proposed = Proposed();
        if (!SamePath(proposed, DataFolderSettings.Root)) DataFolderSettings.Root = proposed;
    }

    private static bool HoldsAnything(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable is not empty: keep the folder rather than strand it.
            return Directory.Exists(folder);
        }
    }

    /// <summary>A sync service's name, and - when it is not obvious - why the folder is synced.</summary>
    public sealed record CloudSync(string Service, string? Reason);

    /// <summary>
    /// What is uploading this folder to the cloud, or null if nothing appears
    /// to be. Only ever produces a warning - nothing is refused on the
    /// strength of it - so a false positive costs a sentence.
    /// </summary>
    public static CloudSync? SyncedBy(string? folder, string? home = null)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        home ??= Home;
        var mobileDocuments = Path.Combine(home, "Library", "Mobile Documents");

        // iCloud Drive itself, and every app's iCloud container beside it.
        if (IsUnder(full, mobileDocuments)) return new CloudSync("iCloud Drive", null);

        // Desktop & Documents Folders. With it on, ~/Documents and ~/Desktop
        // keep their paths and their names - nothing about the folder says it
        // is synced - but iCloud Drive grows a Documents and a Desktop of its
        // own. That is the tell, and the cheapest one there is: two directory
        // checks, no process to start and no defaults database to read.
        var cloudDocs = Path.Combine(mobileDocuments, "com~apple~CloudDocs");
        foreach (var name in new[] { "Documents", "Desktop" })
        {
            if (IsUnder(full, Path.Combine(home, name)) && Directory.Exists(Path.Combine(cloudDocs, name)))
            {
                return new CloudSync("iCloud Drive",
                    "\"Desktop & Documents Folders\" is turned on in iCloud Drive's settings, so everything in "
                    + $"your {name} folder is uploaded");
            }
        }

        // Dropbox, Google Drive, OneDrive and Box on current macOS all live
        // under ~/Library/CloudStorage, in folders named for the provider and
        // the account - "GoogleDrive-someone@example.com", "OneDrive-Personal".
        var cloudStorage = Path.Combine(home, "Library", "CloudStorage");
        if (IsUnder(full, cloudStorage))
        {
            var provider = full[(cloudStorage.Length + 1)..].Split(Path.DirectorySeparatorChar)[0];
            return new CloudSync(ProviderName(provider), null);
        }

        // Older Dropbox and Google Drive installs, under their own names.
        return DataFolderSettings.CloudSyncedBy(full) is { } service ? new CloudSync(service, null) : null;
    }

    private static string ProviderName(string folderName)
    {
        var stem = folderName.Split('-')[0];
        return stem.ToLowerInvariant() switch
        {
            "googledrive" => "Google Drive",
            "onedrive" => "OneDrive",
            "dropbox" => "Dropbox",
            "box" => "Box",
            _ => stem.Length > 0 ? stem : "a cloud service"
        };
    }

    /// <summary>
    /// The mount point holding this folder: "/" for anything on the startup
    /// disk, /Volumes/Something for an external one.
    ///
    /// The shared FreeGigabytesAt asks Path.GetPathRoot, which is "/" for every
    /// path on a Mac - so a roomy external disk was reported with the startup
    /// disk's free space, and the reader who moved the downloads to get away
    /// from a full internal disk was told it was full.
    /// </summary>
    public static string? VolumeOf(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            string? best = null;

            foreach (var drive in DriveInfo.GetDrives())
            {
                string root;
                try
                {
                    root = drive.RootDirectory.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((root == "/" || IsUnder(full, root)) && (best == null || root.Length > best.Length)) best = root;
            }

            return best ?? Path.GetPathRoot(full);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// How much room is left on the volume holding this folder, or null where
    /// that cannot be established. Not knowing is no reason to refuse a folder,
    /// so callers say less rather than nothing.
    /// </summary>
    public static double? FreeGigabytesAt(string folder)
    {
        var volume = VolumeOf(folder);
        if (string.IsNullOrEmpty(volume)) return null;

        try
        {
            return new DriveInfo(volume).AvailableFreeSpace / 1024d / 1024d / 1024d;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Names the startup disk as well, when the downloads are going to another
    /// volume and the startup disk is short of room.
    ///
    /// The big collections arrive as a git clone into the system temporary
    /// folder and are copied out from there, so the download needs room on
    /// two volumes, not one. Choosing a roomy external disk and being told
    /// "500 GB free" is exactly the reassurance that precedes running out of
    /// space on the internal one an hour later - and the person most likely
    /// to move the folder is the person whose internal disk is already full.
    /// </summary>
    public static string TemporaryVolumeNote(string root)
    {
        var tempVolume = VolumeOf(Path.GetTempPath());
        var chosenVolume = VolumeOf(root);

        if (string.IsNullOrEmpty(tempVolume) || string.IsNullOrEmpty(chosenVolume)) return string.Empty;
        if (SamePath(tempVolume, chosenVolume)) return string.Empty;

        var tempFree = FreeGigabytesAt(tempVolume);
        if (tempFree == null || tempFree >= DataFolderSettings.ComfortableFreeGigabytes) return string.Empty;

        var where = tempVolume == "/" ? "your Mac's startup disk" : tempVolume;
        return $" - note the largest downloads also unpack through {where} first, which has {tempFree:N1} GB free.";
    }

    /// <summary>
    /// Whether one path sits inside another, compared segment-wise through a
    /// trailing separator so ~/DocumentsOld is not read as inside ~/Documents.
    /// Case-insensitive, as a Mac's own disk is.
    /// </summary>
    public static bool IsUnder(string path, string candidateParent)
    {
        var parent = candidateParent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var child = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(
            a.Trim().TrimEnd(Path.DirectorySeparatorChar),
            b.Trim().TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
