namespace ClassicaCodex.Data;

/// <summary>
/// The folder every preference, log and remembered setting is kept in -
/// ClassicaCodex under %LocalAppData% on Windows, under Application Support
/// on a Mac - and where the library database goes if nobody picks anywhere
/// else.
///
/// One answer for both front ends and every settings class, rather than each
/// working it out for itself, so that there is one place to move it from.
/// </summary>
public static class PreferencesFolder
{
    private static string? _instead;

    public static string Root => _instead ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex");

    /// <summary>
    /// Points <see cref="Root"/> at another folder for the rest of this process.
    ///
    /// Only the tests call this. They run the real windows on a reader's own
    /// machine, and on Windows nothing outside the process can redirect
    /// LocalApplicationData the way XDG_DATA_HOME does on Linux: one run of
    /// the Mac tests on Windows left a mac-settings.json behind, added its
    /// test exceptions to the reader's errors.log and rewrote their
    /// sync-reader-panes.txt.
    /// </summary>
    internal static void UseInstead(string folder) => _instead = folder;
}
