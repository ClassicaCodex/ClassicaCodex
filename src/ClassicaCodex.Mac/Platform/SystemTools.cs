namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// Finds the command-line tools that stand in on Linux for what a Mac has
/// built in. None of them is guaranteed: a desktop may have espeak-ng and no
/// secret-tool, or the reverse. So each feature asks here, and a tool that is
/// missing turns off that feature and nothing else.
/// </summary>
internal static class SystemTools
{
    /// <summary>The full path of the first of these on the PATH, or null if none is.</summary>
    public static string? Find(params string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        var folders = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var name in names)
        {
            foreach (var folder in folders)
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
