using System.Reflection;

namespace ClassicaCodex.Core.Rabbinic;

/// <summary>One version of a rabbinic text: the file to fetch and what it is filed as.</summary>
public sealed class RabbinicTextEntry
{
    /// <summary>"mishnah", "tosefta", "bavli", "yerushalmi", "minor" or "targum".</summary>
    public string Corpus { get; init; } = string.Empty;

    /// <summary>The work this version belongs to, as the last part of its URN.</summary>
    public string Slug { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>The version as Sefaria names it, for the edition's header.</summary>
    public string VersionTitle { get; init; } = string.Empty;

    /// <summary>The last part of the edition's URN, and its file name in the download folder.</summary>
    public string Suffix { get; init; } = string.Empty;

    public bool IsTranslation { get; init; }

    public string Language { get; init; } = string.Empty;

    public string? Translator { get; init; }

    /// <summary>What tells this edition from another original in the same language.</summary>
    public string? Orthography { get; init; }

    /// <summary>Where the corpus's own citation scheme does not fit this work.</summary>
    public string? CitationScheme { get; init; }

    /// <summary>
    /// The licence exactly as the file stated it when the catalogue was built.
    /// Sefaria writes public domain both as "Public Domain" and as "PD", so it
    /// is copied rather than normalised, and compared at install time.
    /// </summary>
    public string Licence { get; init; } = string.Empty;

    public long Bytes { get; init; }

    /// <summary>
    /// The file's path below the export's json folder, percent-encoded as
    /// Sefaria's own index gives it. Kept whole because a version title does
    /// not always survive as a file name.
    /// </summary>
    public string ExportPath { get; init; } = string.Empty;
}

/// <summary>
/// The rabbinic texts beyond the hand-picked demonology: the Mishnah, the
/// Tosefta, both Talmuds, the minor tractates and the Targumim.
///
/// Built by tools/RabbinicCatalogue from Sefaria's index and embedded. Each of
/// about 210 works has two to four versions, chosen by licence, and neither
/// the version titles nor the licence labels are regular enough to derive at
/// install time: the Tosefta alone has a differently named Hebrew file for
/// nearly every tractate. Fixing the list also means a version Sefaria
/// relicenses is refused by the licence check at install, not silently
/// swapped for whatever is there now.
///
/// The texts themselves are not embedded, only what to fetch and under what
/// terms.
/// </summary>
public static class RabbinicCatalogue
{
    private const string ResourceName = "ClassicaCodex.Core.Rabbinic.RabbinicTexts.tsv";

    private static readonly Lazy<IReadOnlyList<RabbinicTextEntry>> LazyTexts = new(Load);

    public static IReadOnlyList<RabbinicTextEntry> Texts => LazyTexts.Value;

    public static long TotalBytes => Texts.Sum(t => t.Bytes);

    private static IReadOnlyList<RabbinicTextEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return Array.Empty<RabbinicTextEntry>();

        using var reader = new StreamReader(stream);
        var texts = new List<RabbinicTextEntry>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');
            if (parts.Length < 13) continue;

            texts.Add(new RabbinicTextEntry
            {
                Corpus = parts[0],
                Slug = parts[1],
                Title = parts[2],
                VersionTitle = parts[3],
                Suffix = parts[4],
                IsTranslation = parts[5] == "T",
                Language = parts[6],
                Translator = Empty(parts[7]),
                Orthography = Empty(parts[8]),
                CitationScheme = Empty(parts[9]),
                Licence = parts[10],
                Bytes = long.TryParse(parts[11], out var bytes) ? bytes : 0,
                ExportPath = parts[12]
            });
        }

        return texts;
    }

    private static string? Empty(string value) => value.Length == 0 ? null : value;
}
