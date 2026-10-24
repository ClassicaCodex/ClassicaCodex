using System.Diagnostics;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// Hands things to macOS: a page to the default browser, a file to the app
/// that opens it, a folder to the Finder.
///
/// Through <c>open</c>, which is what the Finder itself does on a double-
/// click, so whatever the reader has chosen as their browser or PDF viewer
/// is what appears. Elsewhere (a Linux machine building the Mac front end)
/// xdg-open does the same job.
/// </summary>
public static class MacShell
{
    public static bool Open(string pathOrUrl)
    {
        try
        {
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

    /// <summary>Shows a file selected in a Finder window.</summary>
    public static bool Reveal(string path)
    {
        if (!OperatingSystem.IsMacOS()) return Open(Path.GetDirectoryName(path) ?? path);

        try
        {
            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
