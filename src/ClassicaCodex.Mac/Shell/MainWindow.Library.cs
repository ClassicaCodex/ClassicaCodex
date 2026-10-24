using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Setup;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// Choosing the library file: opening one already built - on this Mac, or
/// copied from the PC - or starting an empty one and running setup into it.
/// </summary>
public partial class MainWindow
{
    /// <summary>Asks for a library file and switches to it.</summary>
    public async Task OpenLibraryAsync()
    {
        var path = await Files.OpenAsync(this, "Open a Classica Codex Library", Files.Library);
        if (path == null) return;

        await SwitchLibraryAsync(path, isNew: false);
    }

    /// <summary>Asks where a new, empty library should go, creates it, and offers setup.</summary>
    public async Task CreateLibraryAsync()
    {
        var path = await Files.SaveAsync(this, "Create a New Library", "classicacodex.db", Files.Library);
        if (path == null) return;

        if (File.Exists(path))
        {
            await Alert.Warn(this, "That file already exists",
                "Choose Open Library to use a library that is already there - a new one is never written over an existing file.");
            return;
        }

        if (await SwitchLibraryAsync(path, isNew: true)) SetupFeature.Open(this);
    }

    /// <summary>
    /// Points the app at a library file, brings its schema up to date - the
    /// same migrations the Windows app runs, so a library copied from the PC
    /// opens here as it is - and reloads everything that shows it.
    /// </summary>
    public async Task<bool> SwitchLibraryAsync(string path, bool isNew)
    {
        var previous = DbConnectionFactory.DatabasePath;

        try
        {
            DbConnectionFactory.Configure(path);
            await SchemaInitializer.EnsureSchemaAsync();
        }
        catch (Exception ex)
        {
            if (previous != null) DbConnectionFactory.Configure(previous);
            await Alert.Error(this, isNew ? "Couldn't create the library" : "Couldn't open the library",
                $"{ex.Message}\n\nThe file was {path}.");
            return false;
        }

        _openWork = null;
        _history.Clear();
        _historyIndex = -1;
        RefreshHistoryButtons();
        UpdateRunningHead();

        await Sidebar.LoadAsync();
        await _welcome.RefreshAsync();
        ShowContent(_welcome);
        return true;
    }
}
