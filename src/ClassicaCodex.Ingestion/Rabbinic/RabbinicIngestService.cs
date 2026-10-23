using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Pseudepigrapha;
using ClassicaCodex.Ingestion.Sefaria;

namespace ClassicaCodex.Ingestion.Rabbinic;

/// <summary>
/// One version of a rabbinic text to install, with the licence its Sefaria
/// file must carry for it to be installed at all.
/// </summary>
public sealed record RabbinicVersion(
    string LanguageFolder, string VersionTitle, string Licence, EditionKind Kind, string Language,
    string? Translator, string UrnSuffix);

/// <summary>A work, where Sefaria files it, and which of its versions to take.</summary>
/// <param name="Node">For a text with named parts, the part this work is; null for the whole text.</param>
public sealed record RabbinicWork(
    string Slug, string Title, string CategoryPath, string SefariaTitle, string? Node, bool IsTalmud,
    IReadOnlyList<RabbinicVersion> Versions);

/// <summary>
/// The rabbinic texts where the demons and angels of the Hebrew Bible's
/// readers live: five tractates of the Babylonian Talmud, Pirkei de-Rabbi
/// Eliezer, and the tracts of Eisenstein's Otzar Midrashim that tell of Lilith,
/// Metatron, the palaces of heaven and Solomon's demons.
///
/// <b>The tractates are the ones the demons are in.</b> Berakhot 6a says no
/// creature could bear to see them. Shabbat 151b forbids sleeping alone in a
/// house, for Lilith. Pesachim 110-112 holds Agrat bat Mahlat and the rules of
/// pairs. Hagigah 16a says demons are like angels in three ways and like men
/// in three. Gittin 68 is Solomon and Ashmedai and the shamir.
///
/// <b>Licences decided which versions, not convenience.</b> Sefaria carries
/// the Koren/Steinsaltz English and Aramaic for every tractate, and both are
/// non-commercial. What is taken instead: the Vilna text as transcribed on
/// Hebrew Wikisource (CC BY-SA); Lazarus Goldschmidt's German translation of
/// 1929 (public domain) for every tractate; A. Cohen's English of Berakhot
/// (1921, public domain) and Sefaria's community translation of Hagigah (CC0).
/// There is no free English of Pesachim, Shabbat or Gittin to take. Each
/// file's licence is checked as it is read, and a version whose terms have
/// changed is skipped and reported.
/// </summary>
public class RabbinicIngestService
{
    public const string CollectionKey = "rabbinic";

    private const string Aramaic = "arc";
    private const string Hebrew = "heb";

    private static RabbinicVersion Vilna => new(
        "Hebrew", "Wikisource Talmud Bavli", "CC-BY-SA", EditionKind.Original, Aramaic, null, "vilna");

    private static RabbinicVersion Goldschmidt => new(
        "English", "Talmud Bavli. German trans. by Lazarus Goldschmidt, 1929 [de]", "Public Domain",
        EditionKind.Translation, "deu", "Lazarus Goldschmidt (1929)", "goldschmidt");

    public static readonly IReadOnlyList<RabbinicWork> Works = new[]
    {
        new RabbinicWork("berakhot", "Berakhot", "Talmud/Bavli/Seder Zeraim", "Berakhot", null, true, new[]
        {
            Vilna,
            new RabbinicVersion("English", "Tractate Berakot by A. Cohen, Cambridge University Press, 1921",
                "Public Domain", EditionKind.Translation, "eng", "A. Cohen (1921)", "cohen"),
            Goldschmidt
        }),
        new RabbinicWork("shabbat", "Shabbat", "Talmud/Bavli/Seder Moed", "Shabbat", null, true, new[] { Vilna, Goldschmidt }),
        new RabbinicWork("pesachim", "Pesachim", "Talmud/Bavli/Seder Moed", "Pesachim", null, true, new[] { Vilna, Goldschmidt }),
        new RabbinicWork("chagigah", "Hagigah", "Talmud/Bavli/Seder Moed", "Chagigah", null, true, new[]
        {
            Vilna,
            new RabbinicVersion("English", "Sefaria Community Translation", "CC0", EditionKind.Translation, "eng",
                "Sefaria Community Translation", "community"),
            Goldschmidt
        }),
        new RabbinicWork("gittin", "Gittin", "Talmud/Bavli/Seder Nashim", "Gittin", null, true, new[] { Vilna, Goldschmidt }),

        new RabbinicWork("pirkei-de-rabbi-eliezer", "Pirkei de-Rabbi Eliezer", "Midrash/Aggadah", "Pirkei DeRabbi Eliezer",
            null, false, new[]
            {
                new RabbinicVersion("Hebrew", "Pirke DeRabbi Eliezer, Sefaria Vocalized Edition", "Public Domain",
                    EditionKind.Original, Hebrew, null, "vocalized"),
                new RabbinicVersion("English", "Pirke de Rabbi Eliezer, trans. Rabbi Gerald Friedlander, London, 1916",
                    "Public Domain", EditionKind.Translation, "eng", "Gerald Friedlander (1916)", "friedlander")
            }),

        Otzar("alphabet-of-ben-sira", "The Alphabet of Ben Sira", "The Aleph Bet of ben Sira"),
        Otzar("hekhalot", "Hekhalot", "Hekhalot"),
        Otzar("metatron", "The Angel Metatron (Chanoch)", "The Angel Metatron (Chanoch)"),
        Otzar("seventy-names-of-metatron", "Seventy Names of Metatron", "Seventy Names of Metatron"),
        Otzar("book-of-chanoch", "The Book of Chanoch", "The Book of Chanoch"),
        Otzar("book-of-raziel", "Book of Raziel the Angel", "Book of Rezial the Angel"),
        Otzar("eden-and-gehinnom", "The Garden of Eden and Gehinnom", "The Garden of Eden; Gehinnom"),
        Otzar("king-solomon", "Midrashim on King Solomon", "Midrashim on King Solomon"),
    };

    /// <summary>
    /// A tract of Otzar Midrashim: Eisenstein's Hebrew (New York, 1915, public
    /// domain) and, where it has reached the tract, Sefaria's community
    /// translation (CC0). The other English versions Sefaria holds for Otzar
    /// Midrashim are marked "unknown" and are not taken.
    /// </summary>
    private static RabbinicWork Otzar(string slug, string title, string node) =>
        new(slug, title, "Midrash/Aggadah", "Otzar Midrashim", node, false, new[]
        {
            new RabbinicVersion("Hebrew", "Otzar Midrashim, New York, 1915", "Public Domain",
                EditionKind.Original, Hebrew, null, "eisenstein"),
            new RabbinicVersion("English", "Sefaria Community Translation", "CC0", EditionKind.Translation, "eng",
                "Sefaria Community Translation", "community")
        });

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int WorksInstalled { get; private set; }
    public int PassagesInstalled { get; private set; }

    public List<(string Text, string Error)> Failed { get; } = new();

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        foreach (var work in Works)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var count = await InstallAsync(root, work, cancellationToken);
                if (count > 0) WorksInstalled++;
                PassagesInstalled += count;
                progress.Report($"{work.Title}: {count:N0} passages");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failed.Add((work.Title, ex.GetBaseException().Message));
            }
        }

        progress.Report($"{WorksInstalled} works, {PassagesInstalled:N0} passages.");
        return IngestOutcome.From(Failed, attempted: Works.Count);
    }

    private async Task<int> InstallAsync(string root, RabbinicWork work, CancellationToken ct)
    {
        var (authorUrn, authorName) = work.IsTalmud
            ? ("urn:rabbinic:babylonian-talmud", "Babylonian Talmud")
            : work.Node != null
                ? ("urn:rabbinic:otzar-midrashim", "Otzar Midrashim")
                : ($"urn:rabbinic:{work.Slug}", work.Title);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = authorUrn, Name = authorName, Namespace = "rabbinic", Language = Hebrew
        }, ct);

        var workUrn = $"urn:rabbinic:{work.Slug}";
        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = workUrn,
            Title = work.Title,
            CitationScheme = work.IsTalmud ? "Folio.Segment" : "Chapter.Paragraph"
        }, ct);

        var total = 0;
        foreach (var version in work.Versions)
        {
            var file = Path.Combine(root, work.SefariaTitle, $"{version.UrnSuffix}.json");
            if (!File.Exists(file))
            {
                await _downloader.DownloadAsync(
                    SefariaExportLoader.UrlFor(work.CategoryPath, work.SefariaTitle, version.LanguageFolder, version.VersionTitle),
                    file, new Progress<string>(_ => { }), ct);
            }

            using var export = SefariaExportLoader.Load(file);
            if (!string.Equals(export.License, version.Licence, StringComparison.OrdinalIgnoreCase))
            {
                Failed.Add(($"{work.Title} ({version.VersionTitle})",
                    $"Sefaria lists it as \"{export.License}\", not \"{version.Licence}\"; not installed"));
                continue;
            }

            var node = SefariaExportLoader.Node(export.Text, work.Node);
            if (node == null) continue;

            var passages = work.IsTalmud
                ? SefariaExportLoader.FlattenTalmud(node.Value)
                : SefariaExportLoader.Flatten(node.Value);

            // A tract the community translation has not reached is left out,
            // not installed as an empty pane.
            if (passages.All(p => p.IsHead)) continue;

            var editionUrn = $"{workUrn}:{version.UrnSuffix}";
            var editionId = await _editionRepo.UpsertAsync(new Edition
            {
                WorkId = workId,
                CtsUrn = editionUrn,
                Kind = version.Kind,
                Language = version.Language,
                Translator = version.Translator,
                SourcePath = editionUrn,
                Collection = CollectionKey
            }, ct);

            await _editionRepo.ClearTextNodesAsync(editionId, ct);
            await _textNodeRepo.BulkInsertAsync(
                PseudepigraphaIngestService.Nodes(editionId, passages.Select(p => (p.Citation, p.Text, p.IsHead)).ToList()), ct);

            await _headerRepo.SaveAsync(editionId, new EditionHeader
            {
                Title = $"{work.Title} ({export.VersionTitle})",
                SourceDescription = $"From Sefaria's export, version \"{export.VersionTitle}\""
                    + (export.VersionSource == null ? "." : $"; Sefaria's source: {export.VersionSource}."),
                Availability = export.License
            }, ct);

            total += passages.Count;
        }

        return total;
    }
}
