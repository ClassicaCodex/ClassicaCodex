using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ClassicaCodex.Mac.Common;

/// <summary>Open and save panels, returning plain paths for the shared libraries.</summary>
public static class Files
{
    public static readonly FilePickerFileType Csv = new("Comma-separated values") { Patterns = new[] { "*.csv" } };
    public static readonly FilePickerFileType Tsv = new("Tab-separated text") { Patterns = new[] { "*.txt", "*.tsv" } };
    public static readonly FilePickerFileType Excel = new("Excel workbook") { Patterns = new[] { "*.xlsx" } };
    public static readonly FilePickerFileType Word = new("Word document") { Patterns = new[] { "*.docx" } };
    public static readonly FilePickerFileType Pdf = new("PDF document") { Patterns = new[] { "*.pdf" } };
    public static readonly FilePickerFileType Text = new("Plain text") { Patterns = new[] { "*.txt" } };
    public static readonly FilePickerFileType Json = new("JSON") { Patterns = new[] { "*.json" } };
    public static readonly FilePickerFileType Library = new("Classica Codex library") { Patterns = new[] { "*.db", "*.sqlite", "*.sqlite3" } };

    /// <summary>Asks where to save; null if the reader cancelled.</summary>
    public static async Task<string?> SaveAsync(Window owner, string title, string suggestedName, params FilePickerFileType[] types)
    {
        var safe = string.Join("_", suggestedName.Split(Path.GetInvalidFileNameChars()));
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = safe,
            FileTypeChoices = types,
            ShowOverwritePrompt = true
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// Asks where a new library should go; null if the reader cancelled.
    ///
    /// The panel is told no file types. macOS has none of its own for .db,
    /// .sqlite or .sqlite3, only made-up ones that do not match each other,
    /// so a panel told the library's types did not see the .db already on
    /// "classicacodex.db" as one of them and added a second:
    /// "classicacodex.db.db". Told none, it keeps the name as it stands, and
    /// a name typed without a library extension is given .db here instead.
    /// </summary>
    public static async Task<string?> SaveLibraryAsync(
        Window owner, string title, string suggestedName, IStorageFolder? startIn, bool showOverwritePrompt)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = startIn,
            ShowOverwritePrompt = showOverwritePrompt
        });

        return file?.TryGetLocalPath() is { } path ? WithLibraryExtension(path) : null;
    }

    /// <summary>The path as it is if it ends in a library extension, else with .db after it.</summary>
    internal static string WithLibraryExtension(string path) =>
        Library.Patterns!.Any(pattern => path.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
            ? path
            : path + ".db";

    /// <summary>Asks for one file to open; null if the reader cancelled.</summary>
    public static async Task<string?> OpenAsync(Window owner, string title, params FilePickerFileType[] types)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <summary>Asks for a folder; null if the reader cancelled.</summary>
    public static async Task<string?> FolderAsync(Window owner, string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
