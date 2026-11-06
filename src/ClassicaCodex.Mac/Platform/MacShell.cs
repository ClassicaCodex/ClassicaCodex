using System.Diagnostics;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// Hands things to macOS: a page to the default browser, a file to the app
/// that opens it, a folder to the Finder.
///
/// Through <c>open</c>, which is what the Finder itself does on a double-
/// click, so whatever the reader has chosen as their browser or PDF viewer
/// is what appears. On Linux xdg-open does the same job, and on Windows the
/// shell itself, which is also what a double-click goes through. On an iPad
/// neither is possible, and <see cref="OpenInstead"/> does the job.
/// </summary>
public static class MacShell
{
    /// <summary>
    /// What to call in place of starting a process, which iOS does not allow.
    /// The iPad app sets it as it starts, because this file is also compiled
    /// for the Mac and cannot name UIKit itself.
    /// </summary>
    public static Func<string, bool>? OpenInstead { get; set; }

    public static bool Open(string pathOrUrl)
    {
        if (OpenInstead is { } open) return open(pathOrUrl);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true })?.Dispose();
                return true;
            }

            var start = OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("/usr/bin/open")
                : new ProcessStartInfo("xdg-open");
            start.ArgumentList.Add(pathOrUrl);
            start.UseShellExecute = false;
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Shows a file selected in a Finder or File Explorer window.</summary>
    public static bool Reveal(string path)
    {
        if (OpenInstead != null || (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()))
            return Open(Path.GetDirectoryName(path) ?? path);

        try
        {
            ProcessStartInfo start;
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo("explorer.exe", ExplorerArguments(path));
            }
            else
            {
                start = new ProcessStartInfo("/usr/bin/open");
                start.ArgumentList.Add("-R");
                start.ArgumentList.Add(path);
            }

            start.UseShellExecute = false;
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Written out rather than left to ArgumentList, which would put the
    /// quotes around the whole argument: Explorer wants them around the path.
    /// </summary>
    internal static string ExplorerArguments(string path) => $"/select,\"{path}\"";
}
