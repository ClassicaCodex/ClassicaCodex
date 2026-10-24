using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Pseudepigrapha;

namespace ClassicaCodex.Ingestion.Goetia;

/// <summary>
/// The Goetia, the first book of the Lesser Key of Solomon, in the edition of
/// S. L. MacGregor Mathers and Aleister Crowley (1904): the seventy-two
/// spirits Solomon bound in a vessel of brass, each with its rank, its shape,
/// its office and its legions, and the art of calling them up.
///
/// The catalogue descends from Johann Weyer's Pseudomonarchia Daemonum
/// (1577), which Reginald Scot translated in the Discovery of Witchcraft -
/// already in the library in the early English books - so the two can be
/// read against each other spirit by spirit.
///
/// From Project Gutenberg's ebook #72679 (2024), L. W. de Laurence's reprint
/// of 1916, which the Distributed Proofreaders checked against page images.
/// It replaces the English Wikisource page the library used before, an
/// unproofread copy of the same reprint that appeared to descend from a
/// transcription released for non-commercial use only. The works keep their
/// URNs, so a reference to a spirit still finds it.
/// </summary>
public class GoetiaIngestService
{
    public const string CollectionKey = "goetia";

    /// <summary>The Gutenberg ebook: "The Lesser Key of Solomon: Goetia, the Book of Evil Spirits".</summary>
    public const int Ebook = 72679;

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int WorksInstalled { get; private set; }
    public int PassagesInstalled { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        progress.Report("The Goetia: Mathers and Crowley's edition from Project Gutenberg...");
        var path = Path.Combine(root, $"{Ebook}-h.htm");
        if (!File.Exists(path))
        {
            await _downloader.DownloadAsync(GutenbergHtml.MirrorUrl(Ebook), path,
                new Progress<string>(_ => { }), cancellationToken);
        }

        var works = GoetiaLoader.Parse(await File.ReadAllTextAsync(path, cancellationToken));

        var lemegeton = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:goetia",
            Name = "The Lesser Key of Solomon",
            Namespace = "goetia",
            Language = "eng"
        }, cancellationToken);

        var crowley = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:crowley",
            Name = "Aleister Crowley",
            Namespace = "goetia",
            Language = "eng"
        }, cancellationToken);

        foreach (var work in works)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var urn = work.ByCrowley ? $"urn:crowley:goetia:{work.Slug}" : $"urn:goetia:{work.Slug}";
            var workId = await _workRepo.UpsertAsync(new Work
            {
                AuthorId = work.ByCrowley ? crowley : lemegeton,
                CtsUrn = urn,
                Title = work.Title,
                CitationScheme = work.Slug switch { "spirits" => "Spirit", "initiated-interpretation" => "Paragraph", _ => "Section.Paragraph" }
            }, cancellationToken);

            // Mathers called his Goetia a translation, and the label says so;
            // Crowley's preface, essay and English of the conjurations are his own.
            var editionId = await _editionRepo.UpsertAsync(new Edition
            {
                WorkId = workId,
                CtsUrn = $"{urn}:1904",
                Kind = work.ByCrowley ? EditionKind.Original : EditionKind.Translation,
                Language = "eng",
                Translator = work.ByCrowley ? null : "S. L. MacGregor Mathers, edited by Aleister Crowley (1904)",
                SourcePath = $"{urn}:1904",
                Collection = CollectionKey
            }, cancellationToken);

            await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);
            await _textNodeRepo.BulkInsertAsync(PseudepigraphaIngestService.Nodes(editionId, work.Passages), cancellationToken);
            await _headerRepo.SaveAsync(editionId, new EditionHeader
            {
                Title = $"{work.Title}, from The Book of the Goetia of Solomon the King, translated by S. L. MacGregor "
                    + "Mathers, edited by Aleister Crowley",
                Publisher = "Society for the Propagation of Religious Truth (1904); reprinted by de Laurence, Scott & Co. (1916)",
                PublicationPlace = "Boleskine, Foyers, Inverness; reprinted Chicago",
                PublicationDate = "1904; reprinted 1916",
                SourceDescription = $"Project Gutenberg ebook #{Ebook} (2024), L. W. de Laurence's 1916 reprint of the 1904 "
                    + "edition, proofread by the Distributed Proofreaders against page images. The seals and figures "
                    + "are not included. Footnotes follow the passage their marker is in, as Note. Small capitals are "
                    + "typed as ordinary words.",
                Availability = "Public domain in the United States, where it was published before 1931, and in the "
                    + "United Kingdom: Mathers died in 1918, de Laurence in 1936 and Crowley in 1947."
            }, cancellationToken);

            WorksInstalled++;
            PassagesInstalled += work.Passages.Count;
        }

        progress.Report($"The Goetia: {WorksInstalled} works, {PassagesInstalled:N0} passages.");
        return IngestOutcome.Clean;
    }
}
