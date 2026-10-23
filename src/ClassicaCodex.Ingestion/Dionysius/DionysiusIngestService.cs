using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Pseudepigrapha;

namespace ClassicaCodex.Ingestion.Dionysius;

/// <summary>
/// The works of Pseudo-Dionysius the Areopagite in John Parker's English
/// (1897-1899): On the Heavenly Hierarchy, where the nine orders of angels
/// are set out in three triads for the first time, On the Ecclesiastical
/// Hierarchy, On Divine Names, the Mystic Theology and the letters.
///
/// The later lists of the angelic choirs - Gregory the Great's, Aquinas's in
/// Summa I q. 108, Dante's in Paradiso 28, Heywood's - go back to the
/// Heavenly Hierarchy, and the library had none of it.
///
/// <b>English only.</b> The Greek is not in First1KGreek or Perseus, and the
/// open OCR of Migne's Greek is too rough to read beside a translation and
/// is missing the Heavenly Hierarchy's first chapter. Parker believed the
/// author was Paul's convert at Athens, and his translation says so; the
/// library files the works under the name scholars use now.
///
/// From the Christian Classics Ethereal Library's ThML file, transcribed by
/// Roger Pearse in 2004 and marked public domain.
/// </summary>
public class DionysiusIngestService
{
    public const string CollectionKey = "dionysius";

    private const string Ccel = "https://www.ccel.org/ccel/d/dionysius/works.xml";

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

        progress.Report("Dionysius: Parker's translation from the Christian Classics Ethereal Library...");
        var path = Path.Combine(root, "dionysius-parker.xml");
        if (!File.Exists(path))
        {
            await _downloader.DownloadAsync(Ccel, path, new Progress<string>(_ => { }), cancellationToken);
        }

        IReadOnlyList<DionysiusWork> works;
        await using (var stream = File.OpenRead(path))
        {
            works = DionysiusLoader.Load(DionysiusLoader.Read(stream));
        }

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:dionysius",
            Name = "Pseudo-Dionysius the Areopagite",
            Namespace = "dionysius",
            Language = "grc"
        }, cancellationToken);

        for (var i = 0; i < works.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (_, slug, title, scheme, year) = DionysiusLoader.Works[i];
            var urn = $"urn:dionysius:{slug}";
            progress.Report($"Dionysius: {title}...");

            var workId = await _workRepo.UpsertAsync(new Work
            {
                AuthorId = authorId,
                CtsUrn = urn,
                Title = title,
                CitationScheme = scheme
            }, cancellationToken);

            var editionId = await _editionRepo.UpsertAsync(new Edition
            {
                WorkId = workId,
                CtsUrn = $"{urn}:parker",
                Kind = EditionKind.Translation,
                Language = "eng",
                Translator = $"John Parker ({year})",
                SourcePath = $"{urn}:parker",
                Collection = CollectionKey
            }, cancellationToken);

            await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);
            await _textNodeRepo.BulkInsertAsync(PseudepigraphaIngestService.Nodes(editionId, works[i].Passages), cancellationToken);
            await _headerRepo.SaveAsync(editionId, new EditionHeader
            {
                Title = $"{title}, translated by John Parker",
                Publisher = "James Parker and Co.",
                PublicationPlace = "London",
                PublicationDate = year,
                SourceDescription = year == "1897"
                    ? "The Works of Dionysius the Areopagite, Part I, translated by the Rev. John Parker (London: "
                        + "James Parker and Co., 1897)"
                    : "The Works of Dionysius the Areopagite, Part II: The Heavenly Hierarchy and the Ecclesiastical "
                        + "Hierarchy, now first translated into English from the original Greek by the Rev. John Parker "
                        + "(London: James Parker and Co., 1899)",
                Availability = "Public domain. Transcribed by Roger Pearse (2004) for the Christian Classics Ethereal "
                    + "Library, which marks the text public domain. Parker's footnotes are not included."
            }, cancellationToken);

            WorksInstalled++;
            PassagesInstalled += works[i].Passages.Count;
        }

        progress.Report($"Dionysius: {WorksInstalled} works, {PassagesInstalled:N0} passages.");
        return IngestOutcome.Clean;
    }
}
