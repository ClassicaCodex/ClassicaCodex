using System.Security.Cryptography;
using Avalonia.Platform.Storage;
using ClassicaCodex.Mac.Features.Setup;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// Files on an iPad. The shared code reads and writes plain paths, and iPadOS
/// only honours those inside the app's own Documents folder. Anything picked
/// elsewhere can be reached through the picked item alone, and only for a
/// moment. So an export goes straight to a folder the Files app can show, and
/// a picked file is copied in first. The simulator enforces none of this, so
/// a path that works there proves nothing.
/// </summary>
internal static class DeviceFiles
{
    private static string Documents => Path.Combine(MacDataFolder.Home, "Documents");

    internal static string Exports => Path.Combine(Documents, "Exports");

    internal static string Imported => Path.Combine(Documents, "Imported");

    /// <summary>A new file in Exports, never one already there.</summary>
    public static string NewExport(string name)
    {
        Directory.CreateDirectory(Exports);
        return Numbered(Exports, name).First(path => !File.Exists(path));
    }

    /// <summary>A path of the app's own for a picked file, which stays readable after the picker has gone.</summary>
    public static async Task<string> KeepAsync(IStorageFile file)
    {
        await using var source = await file.OpenReadAsync();
        return await KeepAsync(source, file.Name);
    }

    /// <summary>Copies into Imported. Picking the same file twice gives the same path, so it is not attached twice.</summary>
    internal static async Task<string> KeepAsync(Stream source, string name)
    {
        Directory.CreateDirectory(Imported);
        var incoming = Path.Combine(Imported, $".{Guid.NewGuid():N}.part");

        try
        {
            await using (var output = File.Create(incoming)) await source.CopyToAsync(output);

            foreach (var path in Numbered(Imported, Path.GetFileName(name)))
            {
                if (!File.Exists(path))
                {
                    File.Move(incoming, path);
                    return path;
                }

                if (SameBytes(path, incoming)) return path;
            }

            throw new InvalidOperationException("Numbered never ends.");
        }
        finally
        {
            if (File.Exists(incoming)) File.Delete(incoming);
        }
    }

    /// <summary>
    /// A path stored by an earlier install, as it is now. An update moves the
    /// app's container, so the part before Documents goes out of date.
    /// </summary>
    public static string Here(string path)
    {
        const string marker = "/Documents/";

        var at = path.IndexOf(marker, StringComparison.Ordinal);
        return MacDataFolder.OnDevice && at >= 0 ? Path.Combine(Documents, path[(at + marker.Length)..]) : path;
    }

    private static IEnumerable<string> Numbered(string folder, string name)
    {
        yield return Path.Combine(folder, name);

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var number = 2; ; number++) yield return Path.Combine(folder, $"{stem} {number}{extension}");
    }

    private static bool SameBytes(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;

        using var one = File.OpenRead(first);
        using var two = File.OpenRead(second);
        return SHA256.HashData(one).AsSpan().SequenceEqual(SHA256.HashData(two));
    }
}
