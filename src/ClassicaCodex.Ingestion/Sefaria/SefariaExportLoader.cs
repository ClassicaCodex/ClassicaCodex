using System.Text.Json;

namespace ClassicaCodex.Ingestion.Sefaria;

/// <summary>A passage from a Sefaria text: its citation, its text, and whether it is a section title.</summary>
public sealed record SefariaPassage(string Citation, string Text, bool IsHead = false);

/// <summary>One version of a text from Sefaria's bulk export, with its licence as the file states it.</summary>
public sealed record SefariaExport(
    string VersionTitle, string License, string? VersionSource, JsonElement Text) : IDisposable
{
    internal JsonDocument? Document { get; init; }

    public void Dispose() => Document?.Dispose();
}

/// <summary>
/// Reads Sefaria's bulk export: one JSON file per version of a text, served
/// from Google Cloud Storage since September 2026 at
/// storage.googleapis.com/sefaria-export/json/{category}/{title}/{language}/{version}.json.
///
/// <b>Three shapes of text.</b> A simple text is a jagged array, chapters of
/// verses. A text with named parts - the twelve Testaments, the two hundred
/// tracts of Otzar Midrashim - is an object of named nodes, each its own
/// jagged array or object. And a tractate of the Talmud is an array of pages
/// in which page 2a is the third entry, because Sefaria numbers the sides of
/// each folio from the folio that does not exist. The caller picks the node
/// it wants; <see cref="Flatten"/> and <see cref="FlattenTalmud"/> turn what is
/// under it into citations.
///
/// The licence is the file's own "license" field, which is the only place
/// Sefaria states it - the repository says each text is licensed separately,
/// and some are "unknown". The caller checks it.
/// </summary>
public static class SefariaExportLoader
{
    public const string Root = "https://storage.googleapis.com/sefaria-export/json/";

    /// <summary>The export file for a version, from its category path, title, language folder and version title.</summary>
    public static string UrlFor(string categoryPath, string title, string languageFolder, string versionTitle) =>
        Root + string.Join('/', categoryPath.Split('/').Select(Uri.EscapeDataString))
        + "/" + Uri.EscapeDataString(title) + "/" + languageFolder + "/" + Uri.EscapeDataString(versionTitle) + ".json";

    public static SefariaExport Load(string path) => Parse(File.ReadAllText(path));

    public static SefariaExport Parse(string json)
    {
        var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string Field(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        return new SefariaExport(
            Field("versionTitle"),
            Field("license"),
            Field("versionSource") is { Length: > 0 } source ? source : null,
            root.TryGetProperty("text", out var text) ? text : default)
        {
            Document = document
        };
    }

    /// <summary>
    /// A named node of a text with named parts, or the whole text when it has
    /// none. Null when the node is not there.
    /// </summary>
    public static JsonElement? Node(JsonElement text, string? name)
    {
        if (name == null) return text;
        if (text.ValueKind != JsonValueKind.Object) return null;
        return text.TryGetProperty(name, out var node) ? node : null;
    }

    /// <summary>
    /// Every passage under a node, in reading order. Arrays are numbered from 1
    /// the way Sefaria numbers chapters and verses, and an empty entry is a
    /// passage this version lacks - skipped without renumbering what follows,
    /// so a citation means the same verse in every version.
    ///
    /// Named sub-parts are numbered by their position, and the name is kept as
    /// a section title at the head of the part. Every version of a text shares
    /// one schema, so the positions agree between the Hebrew and the English
    /// even where one of them leaves a part empty.
    /// </summary>
    public static List<SefariaPassage> Flatten(JsonElement node)
    {
        var passages = new List<SefariaPassage>();
        Walk(node, null, passages);
        return passages;
    }

    private static void Walk(JsonElement node, string? prefix, List<SefariaPassage> passages)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var i = 0;
                foreach (var child in node.EnumerateArray())
                {
                    i++;
                    Walk(child, Join(prefix, i), passages);
                }

                break;
            }

            case JsonValueKind.Object:
            {
                var i = 0;
                foreach (var part in node.EnumerateObject())
                {
                    i++;
                    var citation = Join(prefix, i);
                    var before = passages.Count;
                    Walk(part.Value, citation, passages);

                    // A title only for a part that has text in this version,
                    // so an empty part leaves no orphaned heading behind.
                    if (passages.Count > before && part.Name.Length > 0)
                        passages.Insert(before, new SefariaPassage($"{citation}.head", part.Name, IsHead: true));
                }

                break;
            }

            case JsonValueKind.String:
            {
                var text = SefariaTextLoader.Clean(node.GetString() ?? string.Empty);
                if (text.Length > 0 && prefix != null) passages.Add(new SefariaPassage(prefix, text));
                break;
            }
        }
    }

    private static string Join(string? prefix, int i) => prefix == null ? i.ToString() : $"{prefix}.{i}";

    /// <summary>
    /// A tractate of the Babylonian Talmud, cited by folio, side and segment the
    /// way the Talmud is cited: Berakhot 6a.12.
    ///
    /// Sefaria's array has an entry for every side of every folio from 1a,
    /// though the tractates begin on 2a, so entry i is folio i/2 + 1 and side a
    /// when i is even. The first two entries are always empty.
    /// </summary>
    public static List<SefariaPassage> FlattenTalmud(JsonElement text)
    {
        var passages = new List<SefariaPassage>();
        if (text.ValueKind != JsonValueKind.Array) return passages;

        var i = 0;
        foreach (var side in text.EnumerateArray())
        {
            var folio = $"{i / 2 + 1}{(i % 2 == 0 ? 'a' : 'b')}";
            i++;
            if (side.ValueKind != JsonValueKind.Array) continue;

            var j = 0;
            foreach (var segment in side.EnumerateArray())
            {
                j++;
                if (segment.ValueKind != JsonValueKind.String) continue;
                var cleaned = SefariaTextLoader.Clean(segment.GetString() ?? string.Empty);
                if (cleaned.Length > 0) passages.Add(new SefariaPassage($"{folio}.{j}", cleaned));
            }
        }

        return passages;
    }
}
