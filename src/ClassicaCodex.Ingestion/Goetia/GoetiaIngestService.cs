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
/// From English Wikisource, where the text is in the public domain. The
/// transcription there has not been proofread against the 1904 scan, which
/// the help and the edition header say; a quotation for print should be
/// checked against the scan.
/// </summary>
public class GoetiaIngestService
{
    public const string CollectionKey = "goetia";

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

        progress.Report("The Goetia: Mathers and Crowley's edition from Wikisource...");
        var path = Path.Combine(root, "lesser-key-of-solomon.wiki");
        if (!File.Exists(path))
        {
            await _downloader.DownloadAsync(WikisourceTextLoader.RawUrl("The Lesser Key of Solomon"), path,
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
                CitationScheme = work.Slug == "spirits" ? "Spirit" : work.Slug == "initiated-interpretation" ? "Paragraph" : "Section.Paragraph"
            }, cancellationToken);

            // Mathers called his Goetia a translation, and the label says so;
            // Crowley's essay and his English of the conjurations are his own.
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
                Publisher = "Society for the Propagation of Religious Truth",
                PublicationPlace = "Boleskine, Foyers, Inverness",
                PublicationDate = "1904",
                SourceDescription = "As transcribed on English Wikisource (\"The Lesser Key of Solomon\"). The "
                    + "transcription is an unproofread copy of an earlier web text, marked there for standardisation, "
                    + "and Wikisource names L. W. de Laurence's reprint of 1916 as its edition; check a quotation "
                    + "against the 1904 scan. The seals and figures are not included. Footnotes follow the passage "
                    + "their marker is in, as Note.",
                Availability = "Public domain. Published in 1904; Mathers died in 1918, Crowley in 1947, de Laurence "
                    + "in 1936."
            }, cancellationToken);

            WorksInstalled++;
            PassagesInstalled += work.Passages.Count;
        }

        progress.Report($"The Goetia: {WorksInstalled} works, {PassagesInstalled:N0} passages.");
        return IngestOutcome.Clean;
    }
}
