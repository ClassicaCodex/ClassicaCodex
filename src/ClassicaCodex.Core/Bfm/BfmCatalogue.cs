using System.Reflection;

namespace ClassicaCodex.Core.Bfm;

/// <summary>
/// One text of the Base de Français Médiéval, as Nakala publishes it: its own
/// deposit, with its own DOI and its own licence.
/// </summary>
public sealed class BfmTextEntry
{
    /// <summary>BFM2022 or Fabliaux - the two deposits that are the whole corpus.</summary>
    public string Collection { get; init; } = string.Empty;

    public string Doi { get; init; } = string.Empty;

    /// <summary>The file's checksum, which is also half of its download address.</summary>
    public string Sha1 { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;
    public long Bytes { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Author { get; init; }

    /// <summary>Date of composition as the corpus gives it, yyyy-MM-dd, often approximate.</summary>
    public string? Date { get; init; }

    /// <summary>
    /// The licence Nakala records for this deposit, verbatim: etalab-2.0,
    /// CC-BY-NC-SA-4.0 or CC-BY-NC-SA-3.0. See <see cref="BfmLicence"/>.
    /// </summary>
    public string Licence { get; init; } = string.Empty;

    /// <summary>The corpus's own one-line description, carrying genre and form.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The author where there is one, and null where the corpus says
    /// "anonyme".
    ///
    /// <b>Not the raw field.</b> 292 of the 500 texts record their author as
    /// the word "anonyme", and taking that at face value files more than half
    /// the corpus under a French adjective as though it were a person - which
    /// is what the first run of the ingest did, giving a library tree with a
    /// writer called "anonyme" more prolific than Chrétien de Troyes.
    /// </summary>
    public string? NamedAuthor =>
        string.IsNullOrWhiteSpace(Author) ||
        Author.Equals("anonyme", StringComparison.OrdinalIgnoreCase) ||
        Author.Equals("anonymous", StringComparison.OrdinalIgnoreCase)
            ? null
            : Author.Trim();

    /// <summary>Where the file itself is fetched from.</summary>
    public string DownloadUrl => $"https://api.nakala.fr/data/{Doi}/{Sha1}";

    /// <summary>The deposit's landing page, for a reader who wants the record.</summary>
    public string RecordUrl => $"https://doi.org/{Doi}";

    /// <summary>
    /// The year, where the date can be read as one. Used for ordering a
    /// library that spans the ninth century to the fifteenth.
    /// </summary>
    public int? Year =>
        Date != null && Date.Length >= 4 && int.TryParse(Date[..4], out var year) ? year : null;
}

/// <summary>
/// What may be done with these texts.
///
/// <b>The corpus is not under one licence, and the difference is not small.</b>
/// 211 texts are Licence Ouverte 2.0, which asks only for attribution. The 281
/// fabliaux and 8 others - 289 of the 500 - are CC-BY-NC-SA, which is
/// noncommercial and share-alike. Among those 8 are the Serments de Strasbourg
/// and the Séquence de sainte Eulalie, which is to say the two oldest texts in
/// the French language.
///
/// This application is already noncommercial as distributed, because the Greek
/// lemma data is, so nothing here changes what may be done with it as a whole.
/// It is still worth saying which text is which rather than averaging them.
/// </summary>
public static class BfmLicence
{
    public static bool IsNonCommercial(string? licence) =>
        licence != null && licence.StartsWith("CC-BY-NC", StringComparison.OrdinalIgnoreCase);

    public static string Describe(string? licence) => licence switch
    {
        "etalab-2.0" => "Licence Ouverte 2.0 - free to reuse with attribution.",
        "CC-BY-NC-SA-4.0" => "CC BY-NC-SA 4.0 - share alike, non-commercial use only.",
        "CC-BY-NC-SA-3.0" => "CC BY-NC-SA 3.0 - share alike, non-commercial use only.",
        null or "" => "Licence not recorded.",
        _ => licence
    };

    public const string Attribution =
        "Base de Français Médiéval, ENS de Lyon / UMR 5317 IHRIM (http://bfm.ens-lyon.fr)";
}

/// <summary>
/// What the Base de Français Médiéval contains - 500 texts of Old and Middle
/// French, the Chanson de Roland and Chrétien's romances among them.
///
/// Built by tools/BfmCatalogue from Nakala and embedded, because Nakala
/// publishes each text as its own deposit and finding out what there is means
/// walking two collections a page at a time. Doing that at install time would
/// put twenty requests of someone else's API in front of the download, and
/// make the step fail entirely when Nakala is down. Shipped, the download is
/// 500 straight file requests whose sizes and checksums are known in advance.
///
/// The texts themselves are not embedded - 222 MB, and most of them share-alike.
/// </summary>
public static class BfmCatalogue
{
    public const string HomeUrl = "http://bfm.ens-lyon.fr";

    private const string ResourceName = "ClassicaCodex.Core.Bfm.BfmTexts.tsv";

    private static readonly Lazy<IReadOnlyList<BfmTextEntry>> LazyTexts = new(Load);

    public static IReadOnlyList<BfmTextEntry> Texts => LazyTexts.Value;

    public static long TotalBytes => Texts.Sum(t => t.Bytes);

    public static int Count => Texts.Count;

    private static IReadOnlyList<BfmTextEntry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null) return Array.Empty<BfmTextEntry>();

        using var reader = new StreamReader(stream);
        var texts = new List<BfmTextEntry>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            var parts = line.Split('\t');
            if (parts.Length < 10) continue;

            texts.Add(new BfmTextEntry
            {
                Collection = parts[0],
                Doi = parts[1],
                Sha1 = parts[2],
                FileName = parts[3],
                Bytes = long.TryParse(parts[4], out var bytes) ? bytes : 0,
                Title = parts[5],
                Author = Empty(parts[6]),
                Date = Empty(parts[7]),
                Licence = parts[8],
                Description = Empty(parts[9])
            });
        }

        return texts;
    }

    private static string? Empty(string value) => value.Length == 0 ? null : value;
}
