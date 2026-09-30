using System.Reflection;

namespace ClassicaCodex.Core.Catmus;

/// <summary>
/// Which CATMuS manuscripts can be looked at as actual pages, and where.
///
/// <b>Why a shipped list rather than a live lookup.</b> Only some libraries
/// can be asked a direct question. The Vatican and e-codices build a manifest
/// URL out of the shelfmark; the BnF, which holds half of CATMuS, has no
/// shelfmark index that answers, and its full-text search is unreliable enough
/// that asked for "Espagnol 480" it offers a nineteenth-century bibliography
/// of books about hunting. Resolving that live would put a wrong manuscript in
/// front of a reader. So tools/CatmusManifests does it once, confirms every
/// answer against the record's own shelfmark, and what ships is only what
/// verified.
///
/// Measured 2026-09-28: 121 of 313 manuscripts. The rest are either not
/// digitised or held by a library that answers a shelfmark only through a
/// search page - Munich, Oxford, the British Library, KBR, the Escorial and
/// Vienna all publish IIIF and none of them is resolvable this way yet. The
/// window says "not available" rather than pretending.
/// </summary>
public static class CatmusManifests
{
    private const string ResourceName = "ClassicaCodex.Core.Catmus.CatmusManifests.tsv";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Lookup = new(Load);

    /// <summary>Manuscripts whose pages can be looked at, by shelfmark.</summary>
    public static IReadOnlyDictionary<string, string> ByShelfmark => Lookup.Value;

    public static int Count => Lookup.Value.Count;

    /// <summary>
    /// The IIIF manifest for a manuscript, or null when its library has not
    /// published one this application can find.
    /// </summary>
    public static string? For(string shelfmark) =>
        Lookup.Value.TryGetValue(shelfmark, out var manifest) ? manifest : null;

    public static bool Has(string shelfmark) => Lookup.Value.ContainsKey(shelfmark);

    private static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);

        using var reader = new StreamReader(stream);
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');
            if (parts.Length < 2) continue;

            lookup[parts[0]] = parts[1];
        }

        return lookup;
    }
}
