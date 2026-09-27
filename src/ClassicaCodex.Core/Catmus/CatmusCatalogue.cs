using System.Reflection;

namespace ClassicaCodex.Core.Catmus;

/// <summary>
/// One Parquet file of the CATMuS-Medieval dataset: a block of lines from a
/// single manuscript in a single hand.
/// </summary>
public sealed class CatmusShard
{
    /// <summary>train, dev or test - CATMuS's own division, kept because the file lives under it.</summary>
    public string Split { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    /// <summary>Size of the Parquet file, almost all of it line photographs.</summary>
    public long Bytes { get; init; }

    public int Lines { get; init; }

    public string Shelfmark { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public int? Century { get; init; }
    public string ScriptType { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;

    /// <summary>"verse" or "prose", as CATMuS classifies the block.</summary>
    public string Verse { get; init; } = string.Empty;

    /// <summary>
    /// The transcription project the lines came from. This is the field that
    /// decides the licence - see <see cref="CatmusLicence"/>.
    /// </summary>
    public string Project { get; init; } = string.Empty;

    public string Path => $"{Split}/{FileName}";

    /// <summary>Where the file itself is served from.</summary>
    public string DownloadUrl => CatmusCatalogue.FileBaseUrl + Path;
}

/// <summary>
/// A manuscript, which is one or more shards sharing a shelfmark.
///
/// Most manuscripts are one shard. Some are split by size (Ghent, UL, 1374
/// runs to four parts and 1.15 GiB) and a few by hand, where the same book
/// was written by two scribes and CATMuS separates them.
/// </summary>
public sealed class CatmusManuscript
{
    public string Shelfmark { get; init; } = string.Empty;
    public IReadOnlyList<CatmusShard> Shards { get; init; } = Array.Empty<CatmusShard>();

    public long Bytes => Shards.Sum(s => s.Bytes);
    public int Lines => Shards.Sum(s => s.Lines);

    public string Language => Join(Shards.Select(s => s.Language));
    public string ScriptType => Join(Shards.Select(s => s.ScriptType));
    public string Genre => Join(Shards.Select(s => s.Genre));
    public string Verse => Join(Shards.Select(s => s.Verse));
    public string Project => Join(Shards.Select(s => s.Project));

    /// <summary>The earliest century any of its hands is dated to.</summary>
    public int? Century => Shards.Select(s => s.Century).Where(c => c.HasValue).Min();

    /// <summary>
    /// The holding institution, as far as the shelfmark gives one: everything
    /// before the last comma. "Paris, BnF, fr. 1593" gives "Paris, BnF".
    ///
    /// Used for grouping in the browser, not for anything that has to be
    /// right - a shelfmark with no comma simply groups under itself.
    /// </summary>
    public string Institution
    {
        get
        {
            var lastComma = Shelfmark.LastIndexOf(',');
            return lastComma > 0 ? Shelfmark[..lastComma].Trim() : Shelfmark;
        }
    }

    private static string Join(IEnumerable<string> values)
    {
        var distinct = values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count switch
        {
            0 => string.Empty,
            1 => distinct[0],
            _ => string.Join(", ", distinct.OrderBy(v => v, StringComparer.Ordinal))
        };
    }
}

/// <summary>
/// What may be done with a manuscript's transcriptions, and what is known
/// about its photographs.
///
/// <b>CATMuS is labelled CC-BY-4.0 and that label is not the whole story.</b>
/// 3.97 GiB of it - 32 manuscripts, including the largest - comes from a
/// source published under CC-BY-NC-SA-4.0, whose own deposit says the licence
/// applies "except for images" and that the reproductions appear by
/// permission of the holding libraries. The photographs themselves sit under
/// four different regimes read from the libraries' own IIIF manifests: BnF
/// conditions of use, CC0, rightsstatements.org In Copyright, and
/// "Images Copyright Biblioteca Apostolica Vaticana". None of those is
/// CC-BY-4.0.
///
/// So this application downloads nothing on the reader's behalf without
/// saying which of the two it is, ships none of it inside the program, and
/// tells the reader the terms beside the manuscript rather than in a licence
/// file they would never open.
/// </summary>
public static class CatmusLicence
{
    /// <summary>
    /// The transcription project whose material carries the share-alike,
    /// non-commercial terms. Matched exactly: this is a value from the
    /// dataset, not a search.
    /// </summary>
    public const string NonCommercialProject = "Towards General Castilian HTR";

    public const string Attribution = "CATMuS-Medieval (Clerice, Pinche et al.)";

    public static bool IsNonCommercial(string? project) =>
        string.Equals(project, NonCommercialProject, StringComparison.Ordinal);

    public static string Describe(string? project) => IsNonCommercial(project)
        ? "Transcriptions: CC-BY-NC-SA 4.0 - share alike, non-commercial use only."
        : "Transcriptions: CC-BY 4.0 - free to reuse with attribution.";

    /// <summary>
    /// Said of the photographs regardless of project, because the dataset
    /// does not record which library a line came from and the four regimes
    /// cannot be told apart from anything in the file.
    /// </summary>
    public const string ImageTerms =
        "Line photographs remain under the terms of the library that holds the manuscript, " +
        "which vary by library and are not recorded in the dataset. They are reproduced here " +
        "for study; check with the holding library before republishing one.";
}

/// <summary>
/// The list of what CATMuS-Medieval contains, so the Palaeography window can
/// show 313 manuscripts across eight centuries before a single byte has been
/// downloaded.
///
/// Built by tools/CatmusCatalogue from the dataset itself and embedded, for
/// three reasons: the file listing is the only part of CATMuS that is small,
/// the shard names on their own are lossy (they flatten "Liege, Archives de
/// l'Etat, T51.12" to "Liege__Archives_de_l_Etat__T51_12"), and a reader who
/// has not downloaded anything yet should still be able to see what there is
/// and how big each piece would be.
///
/// The transcriptions themselves are deliberately NOT embedded. Part of the
/// corpus is share-alike and non-commercial - see <see cref="CatmusLicence"/> -
/// and 15 MB of someone else's text inside the executable is redistribution
/// whatever the intent. The reader fetches it, knowing the terms.
/// </summary>
public static class CatmusCatalogue
{
    public const string DatasetUrl = "https://huggingface.co/datasets/CATMuS/medieval";

    public const string FileBaseUrl = "https://huggingface.co/datasets/CATMuS/medieval/resolve/main/";

    private const string ResourceName = "ClassicaCodex.Core.Catmus.CatmusShards.tsv";

    private static readonly Lazy<IReadOnlyList<CatmusShard>> LazyShards = new(Load);

    private static readonly Lazy<IReadOnlyList<CatmusManuscript>> LazyManuscripts = new(() =>
        LazyShards.Value
            .GroupBy(s => s.Shelfmark, StringComparer.Ordinal)
            .Select(g => new CatmusManuscript
            {
                Shelfmark = g.Key,
                // Ordered so a multi-part manuscript reads Part-0, Part-1, ...
                // rather than in whatever order the file listing arrived in.
                Shards = g.OrderBy(s => s.FileName, StringComparer.Ordinal).ToList()
            })
            .OrderBy(m => m.Shelfmark, StringComparer.Ordinal)
            .ToList());

    public static IReadOnlyList<CatmusShard> Shards => LazyShards.Value;

    public static IReadOnlyList<CatmusManuscript> Manuscripts => LazyManuscripts.Value;

    public static long TotalBytes => Shards.Sum(s => s.Bytes);

    public static int TotalLines => Shards.Sum(s => s.Lines);

    /// <summary>
    /// Languages in the catalogue, commonest first. Used to build the
    /// browser's filters, so what is offered is always what is actually
    /// there - a filter for a language no manuscript has is worse than no
    /// filter.
    /// </summary>
    public static IReadOnlyList<string> Languages => Axis(s => s.Language);

    public static IReadOnlyList<string> ScriptTypes => Axis(s => s.ScriptType);

    public static IReadOnlyList<string> Genres => Axis(s => s.Genre);

    public static IReadOnlyList<int> Centuries =>
        Shards.Where(s => s.Century.HasValue).Select(s => s.Century!.Value).Distinct().OrderBy(c => c).ToList();

    private static IReadOnlyList<string> Axis(Func<CatmusShard, string> select) =>
        Shards.Select(select)
            .Where(v => !string.IsNullOrEmpty(v))
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();

    public static CatmusManuscript? Find(string shelfmark) =>
        Manuscripts.FirstOrDefault(m => string.Equals(m.Shelfmark, shelfmark, StringComparison.Ordinal));

    private static IReadOnlyList<CatmusShard> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return Array.Empty<CatmusShard>();

        using var reader = new StreamReader(stream);
        var shards = new List<CatmusShard>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');
            if (parts.Length < 11) continue;

            shards.Add(new CatmusShard
            {
                Split = parts[0],
                FileName = parts[1],
                Bytes = long.TryParse(parts[2], out var bytes) ? bytes : 0,
                Lines = int.TryParse(parts[3], out var lines) ? lines : 0,
                Shelfmark = parts[4],
                Language = parts[5],
                Century = int.TryParse(parts[6], out var century) ? century : null,
                ScriptType = parts[7],
                Genre = parts[8],
                Verse = parts[9],
                Project = parts[10]
            });
        }

        return shards;
    }
}
