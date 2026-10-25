using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Choosing the library file from inside setup - the guided Library File step
/// and Advanced Setup's Library File button, which on Windows are the
/// Database step and the Database Location dialog.
///
/// Windows makes the same two calls either way: Configure to remember the
/// path, EnsureSchemaAsync to create the tables if they are not there yet (a
/// no-op on a library that already has them). The Mac makes them through the
/// main window when there is one, because the main window is what shows the
/// library: switching under it without telling it would leave the sidebar
/// listing the old library's works and the reading history pointing into a
/// file that is no longer open.
/// </summary>
internal static class LibraryFile
{
    /// <summary>The file the step offers: the one open now, else the one last used, else the default.</summary>
    public static string Current => DbConnectionFactory.DatabasePath ?? DbConnectionFactory.PreferredDatabasePath;

    /// <summary>
    /// Makes <paramref name="path"/> the library - opening it if it exists,
    /// creating it if not. Returns false if the main window could not open it,
    /// in which case it has already said why. Throws for anything else, for
    /// the caller to describe.
    /// </summary>
    public static async Task<bool> UseAsync(IReaderHost host, string path)
    {
        var full = Path.GetFullPath(path);

        // The same file again: "Reconfigure" on Windows, and a no-op there too
        // apart from bringing the schema up to date. Not a switch, which would
        // close the work in the reader and clear its history for nothing.
        if (DbConnectionFactory.DatabasePath is { } open && MacDataFolder.SamePath(Path.GetFullPath(open), full))
        {
            await SchemaInitializer.EnsureSchemaAsync();
            return true;
        }

        if (host.Window is MainWindow main) return await main.SwitchLibraryAsync(full, isNew: !File.Exists(full));

        DbConnectionFactory.Configure(full);
        await SchemaInitializer.EnsureSchemaAsync();
        await host.ReloadLibraryAsync();
        return true;
    }

    /// <summary>Asks for a library that already exists - one built before, or copied from the PC.</summary>
    public static Task<string?> ChooseExistingAsync(Window owner) =>
        Files.OpenAsync(owner, "Open a Classica Codex Library", Files.Library);

    /// <summary>
    /// Asks where a new library should go.
    ///
    /// Without the overwrite prompt Files.SaveAsync asks for: naming a file
    /// that is already there opens it as it is, as on Windows, and a sheet
    /// asking whether to "replace" it would describe something that is never
    /// going to happen.
    /// </summary>
    public static async Task<string?> ChooseNewAsync(Window owner, string suggested) =>
        await Files.SaveLibraryAsync(owner, "Choose Where the Library Goes", Path.GetFileName(suggested),
            await FolderOf(owner, suggested), showOverwritePrompt: false);

    private static async Task<IStorageFolder?> FolderOf(Window owner, string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;

        try
        {
            return await owner.StorageProvider.TryGetFolderFromPathAsync(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
