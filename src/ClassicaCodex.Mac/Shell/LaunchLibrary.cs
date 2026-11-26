using ClassicaCodex.Data;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// Opens the library the reader last used, at launch, and remembers what went
/// wrong if it could not, so the main window can say so once it is on screen.
///
/// The classic Windows app tells these two apart, and so does this one now.
/// A library that is simply not there - most often one kept on a drive that
/// isn't plugged in - is named, with Retry, rather than treated as a first
/// run: setting up a new library from the first-run page writes the new path
/// over the remembered one, and plugging the drive back in no longer helps.
/// A library that is there but will not open - locked, unreadable, damaged -
/// is reported rather than written to errors.log while the window opens as
/// though nothing had happened.
/// </summary>
internal static class LaunchLibrary
{
    /// <summary>The remembered library, when it was not at the place remembered.</summary>
    public static string? Missing { get; private set; }

    /// <summary>The remembered library and why it would not open, when it would not.</summary>
    public static (string Path, Exception Error)? Failed { get; private set; }

    public static async Task OpenRememberedAsync()
    {
        Missing = null;
        Failed = null;

        var remembered = DbConnectionFactory.PreferredDatabasePath;
        if (!File.Exists(remembered))
        {
            // The default place with nothing in it is a first run, not a
            // missing library.
            var isDefault = string.Equals(
                Path.GetFullPath(remembered), Path.GetFullPath(DbConnectionFactory.DefaultDatabasePath),
                OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (!isDefault) Missing = remembered;
            return;
        }

        try
        {
            // Not remembered again: it is the remembered one already.
            DbConnectionFactory.Configure(remembered, remember: false);

            // The same migrations the Windows app runs, so a library copied
            // from the PC opens here as it is.
            await SchemaInitializer.EnsureSchemaAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "opening the library at launch");
            DbConnectionFactory.Forget();
            Failed = (remembered, ex);
        }
    }

    /// <summary>Once the window has dealt with it, so a second window does not raise it again.</summary>
    public static void Clear()
    {
        Missing = null;
        Failed = null;
    }
}
