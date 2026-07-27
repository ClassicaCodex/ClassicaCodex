using Microsoft.Win32;
using PdfSharp.Fonts;

/// <summary>
/// PdfSharp 6.x is built to run on Windows, Linux, and Mac alike, so unlike
/// the old .NET Framework-only PdfSharp, it deliberately doesn't reach into
/// the OS to find fonts by name anymore - it needs an IFontResolver that
/// hands back actual font file bytes. Since this app only ever runs on
/// Windows, the natural source for that is the registry key Windows itself
/// uses to map a font's display name to its file - the same lookup Windows
/// does internally, rather than guessing filenames (which vary across
/// Windows versions and locales).
///
/// Falls back to Arial if a requested family isn't found on the machine, so
/// a missing font degrades to a substitution rather than crashing the export.
/// </summary>
public class WindowsFontResolver : IFontResolver
{
    private const string FallbackFamily = "Arial";
    private static readonly Dictionary<string, string> RegistryFontMap = LoadFontRegistry();
    private static readonly Dictionary<string, byte[]> Cache = new();

    public byte[]? GetFont(string faceName)
    {
        if (Cache.TryGetValue(faceName, out var cached)) return cached;

        if (!RegistryFontMap.TryGetValue(faceName, out var fileName)) return null;

        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var path = Path.Combine(fontsDir, fileName);
        if (!File.Exists(path)) return null;

        var bytes = File.ReadAllBytes(path);
        Cache[faceName] = bytes;
        return bytes;
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var faceName = FindBestMatch(familyName, isBold, isItalic)
            ?? FindBestMatch(FallbackFamily, isBold, isItalic)
            ?? RegistryFontMap.Keys.FirstOrDefault()
            ?? FallbackFamily;

        return new FontResolverInfo(faceName);
    }

    /// <summary>
    /// Tries, in order of preference: the exact style requested, then
    /// progressively simpler styles, then plain regular - so a family that
    /// only has a Regular face (no separate Bold/Italic files) still
    /// resolves to something rather than failing outright.
    /// </summary>
    private static string? FindBestMatch(string familyName, bool isBold, bool isItalic)
    {
        var candidates = new List<string>();
        if (isBold && isItalic) candidates.Add($"{familyName} Bold Italic");
        if (isBold) candidates.Add($"{familyName} Bold");
        if (isItalic) candidates.Add($"{familyName} Italic");
        candidates.Add(familyName);

        foreach (var candidate in candidates)
        {
            var match = RegistryFontMap.Keys.FirstOrDefault(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return null;
    }

    /// <summary>
    /// Reads HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts - the
    /// same registry key Windows itself uses to map a font's display name
    /// ("Georgia (TrueType)") to its file (georgia.ttf).
    /// </summary>
    private static Dictionary<string, string> LoadFontRegistry()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key == null) return map;

            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not string fileName) continue;

                // Registry names look like "Georgia Bold (TrueType)" - strip
                // the trailing format annotation to get the plain face name.
                var cleanName = valueName;
                var parenIndex = cleanName.IndexOf(" (", StringComparison.Ordinal);
                if (parenIndex > 0) cleanName = cleanName[..parenIndex];

                if (!map.ContainsKey(cleanName)) map[cleanName] = fileName;
            }
        }
        catch
        {
            // If the registry can't be read for any reason, GetFont/ResolveTypeface
            // simply won't find matches and fall back to Arial via FindBestMatch's
            // fallback chain - no need to fail construction over this.
        }

        return map;
    }
}
