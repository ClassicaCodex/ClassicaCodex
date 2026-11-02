using System.Text.Json;
using ClassicaCodex.Data;

namespace ClassicaCodex.Mac;

/// <summary>Which appearance the app draws in.</summary>
public enum AppearanceChoice
{
    /// <summary>Whatever macOS is set to, following it when it changes at sunset.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Preferences that only the Mac front end has: the appearance choice, the
/// sidebar, where the main window was.
///
/// Everything the two front ends share - the reading position, linked panes,
/// text size, the favourite collection - stays in the Windows app's own
/// settings files, which this app compiles and uses as they are. Those are
/// about the reading; these are about the window it happens in.
///
/// One JSON file beside the shared ones rather than a file per setting,
/// because these are read together at launch and written together at quit.
/// Read failures fall back to the defaults: a preference file is never worth
/// refusing to start over.
/// </summary>
public sealed class MacSettings
{
    public AppearanceChoice Appearance { get; set; } = AppearanceChoice.System;

    /// <summary>Width of the sidebar, in points.</summary>
    public double SidebarWidth { get; set; } = 270;

    public bool SidebarVisible { get; set; } = true;

    /// <summary>"library" or "tools": which of the sidebar's two lists is showing.</summary>
    public string SidebarMode { get; set; } = "library";

    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// The share of the reading area the original takes, so a reader who
    /// prefers a wide Greek column keeps it. 0.5 is an even split.
    /// </summary>
    public double OriginalShare { get; set; } = 0.5;

    private static MacSettings? _current;

    public static MacSettings Current => _current ??= Load();

    internal static string SettingsFile => Path.Combine(PreferencesFolder.Root, "mac-settings.json");

    private static MacSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                return JsonSerializer.Deserialize<MacSettings>(File.ReadAllText(SettingsFile))
                       ?? new MacSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall through to the defaults.
        }

        return new MacSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFile);
            if (directory != null) Directory.CreateDirectory(directory);

            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not persisting a window size is not worth interrupting anyone over.
        }
    }
}
