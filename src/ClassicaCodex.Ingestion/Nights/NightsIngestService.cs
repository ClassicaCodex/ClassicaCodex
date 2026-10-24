using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Pseudepigrapha;

namespace ClassicaCodex.Ingestion.Nights;

/// <summary>
/// The Arabian Nights in Richard Burton's translation: the ten volumes of The
/// Book of the Thousand Nights and a Night (Benares, 1885), all 1,001 nights,
/// and the third volume of his Supplemental Nights (1887), with Aladdin and
/// Ali Baba, which are not in the Arabic manuscripts of the Nights at all but
/// in Galland's French of 1709-1717.
///
/// For a study of demons and angels this is where the jinn of the Quran went
/// next: the Ifrit who rises from the fisherman's brass jar under Solomon's
/// seal, the jinni who keeps a woman in a casket under the sea, and the
/// slaves of the ring and of the lamp. Burton's is the one complete English
/// translation on Project Gutenberg; Lane's and Payne's are there only in
/// part, and Lane's is bowdlerised.
///
/// From Project Gutenberg's HTML, fetched one volume at a time from its
/// mirror. See NightsLoader for what is kept and how it is cited.
/// </summary>
public class NightsIngestService
{
    public const string CollectionKey = "arabian-nights";

    /// <summary>The Gutenberg ebooks, in order: the ten volumes, then the two parts of Supplemental III.</summary>
    public static readonly int[] MainVolumes = { 51252, 51775, 52564, 53254, 54257, 54525, 54778, 55091, 55587, 58360 };
    public static readonly int[] SupplementalThree = { 60889, 61974 };

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int PassagesInstalled { get; private set; }

    /// <summary>The ebook's HTML on Gutenberg's mirror, which shelves books by the digits of their number.</summary>
    public static string MirrorUrl(int ebook)
    {
        var digits = ebook.ToString();
        var shelf = string.Join("/", digits[..^1].Select(c => c.ToString()));
        return $"https://gutenberg.pglaf.org/{shelf}/{ebook}/{ebook}-h/{ebook}-h.htm";
    }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        var main = new List<string>();
        for (var i = 0; i < MainVolumes.Length; i++)
        {
            progress.Report($"Arabian Nights: Burton's volume {i + 1} of 10 from Project Gutenberg...");
            main.Add(await FetchAsync(root, MainVolumes[i], cancellationToken));
        }

        var supplemental = new List<string>();
        foreach (var ebook in SupplementalThree)
        {
            progress.Report("Arabian Nights: the Supplemental Nights, volume III...");
            supplemental.Add(await FetchAsync(root, ebook, cancellationToken));
        }

        progress.Report("Arabian Nights: reading the nights...");
        var nights = NightsLoader.Main(main);
        var tales = NightsLoader.Supplemental(supplemental);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:nights",
            Name = "The Thousand and One Nights",
            Namespace = "nights",
            Language = "ara"
        }, cancellationToken);

        await InstallAsync(authorId, "urn:nights:burton", "The Book of the Thousand Nights and a Night", "Night.Paragraph",
            nights, new EditionHeader
            {
                Title = "The Book of the Thousand Nights and a Night, translated by Richard F. Burton",
                Publisher = "The Burton Club, for private subscribers (Shammar Edition)",
                PublicationDate = "1885",
                SourceDescription = "A Plain and Literal Translation of the Arabian Nights Entertainments, ten volumes, "
                    + "first printed for the Kamashastra Society at \"Benares\" (London) in 1885; Project Gutenberg's "
                    + "transcription (ebooks 51252-58360) is of the Burton Club's undated American reprint, the Shammar "
                    + "Edition. Cited by night and paragraph; the frame story before the first tale is night 0. "
                    + "Burton's footnotes, prefaces, indexes and Terminal Essay are not included.",
                Availability = "Public domain (Burton died in 1890)."
            }, cancellationToken);

        await InstallAsync(authorId, "urn:nights:burton-supplemental-3",
            "Supplemental Nights III: Aladdin, Ali Baba and the Galland tales", "Tale.Paragraph",
            tales, new EditionHeader
            {
                Title = "Supplemental Nights to the Book of the Thousand Nights and a Night, volume III, translated by "
                    + "Richard F. Burton",
                Publisher = "The Burton Club, for private subscribers (Shammar Edition)",
                PublicationDate = "1887",
                SourceDescription = "Supplemental Nights, vol. III (1887): Zayn al-Asnam, Aladdin, Khudadad, the "
                    + "Caliph's Night Adventure, Ali Baba, Ali Khwajah, Prince Ahmad and the Fairy Peri-Banu, and the Two "
                    + "Sisters, with Burton's English of Galland's Aladdin; first printed in 1887, and transcribed by Project "
                    + "Gutenberg (ebooks 60889 and 61974) from the Burton Club's reprint. Cited by tale, in the order the volume prints them, and paragraph. Clouston's "
                    + "appendix of variants and Burton's footnotes are not included.",
                Availability = "Public domain (Burton died in 1890)."
            }, cancellationToken);

        progress.Report($"Arabian Nights: 1,001 nights and 10 supplemental tales, {PassagesInstalled:N0} passages.");
        return IngestOutcome.Clean;
    }

    private async Task<string> FetchAsync(string root, int ebook, CancellationToken ct)
    {
        var path = Path.Combine(root, $"{ebook}-h.htm");
        if (!File.Exists(path))
        {
            await _downloader.DownloadAsync(MirrorUrl(ebook), path, new Progress<string>(_ => { }), ct);
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    private async Task InstallAsync(
        int authorId, string urn, string title, string scheme,
        List<(string Citation, string Text, bool IsHead)> passages, EditionHeader header, CancellationToken ct)
    {
        var workId = await _workRepo.UpsertAsync(new Work { AuthorId = authorId, CtsUrn = urn, Title = title, CitationScheme = scheme }, ct);

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"{urn}:eng",
            Kind = EditionKind.Translation,
            Language = "eng",
            Translator = "Richard F. Burton",
            SourcePath = $"{urn}:eng",
            Collection = CollectionKey
        }, ct);

        await _editionRepo.ClearTextNodesAsync(editionId, ct);
        await _textNodeRepo.BulkInsertAsync(PseudepigraphaIngestService.Nodes(editionId, passages), ct);
        await _headerRepo.SaveAsync(editionId, header, ct);
        PassagesInstalled += passages.Count;
    }
}
