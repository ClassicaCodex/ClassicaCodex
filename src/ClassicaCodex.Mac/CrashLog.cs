namespace ClassicaCodex.Mac;

/// <summary>
/// Writes what went wrong to errors.log beside the other preferences - the
/// same file name and place the Windows CrashReporter uses, so a report from
/// either machine is found in the same folder.
/// </summary>
public static class CrashLog
{
    public static string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "errors.log");

    public static void Write(Exception ex, string context)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogFile);
            if (directory != null) Directory.CreateDirectory(directory);

            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] (Mac) while {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A log that cannot be written is not worth a second failure.
        }
    }
}
