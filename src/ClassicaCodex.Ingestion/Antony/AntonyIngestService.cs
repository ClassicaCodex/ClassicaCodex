using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Dionysius;
using ClassicaCodex.Ingestion.Pseudepigrapha;

namespace ClassicaCodex.Ingestion.Antony;

/// <summary>
/// The Life of Antony, Athanasius's account of the first desert hermit
/// (written about 360), in the English of the Rev. H. Ellershaw from the
/// Nicene and Post-Nicene Fathers (1892).
///
/// It is the founding text of Christian demonology in practice: the devil
/// beating Antony senseless in a tomb, demons coming through the walls as
/// lions, bears, serpents and scorpions, the long sermon on how to tell a
/// demon from an angel, the giant whose head reached the clouds. Every later
/// account of a saint tormented by devils, painted or written, goes back to it.
///
/// <b>Filed under Athanasius, beside his Greek.</b> First1KGreek gives the
/// library six of his works under urn:cts:greekLit:tlg2035; the Life is
/// tlg2035.tlg047 in the same catalogue, so the English goes there, and the
/// Greek, if it is ever added, will read beside it.
/// </summary>
public class AntonyIngestService
{
    public const string CollectionKey = "life-of-antony";

    private const string Ccel = "https://www.ccel.org/ccel/schaff/npnf204.xml";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int PassagesInstalled { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        progress.Report("Life of Antony: the Athanasius volume of the Nicene and Post-Nicene Fathers from the CCEL...");
        var path = Path.Combine(root, "npnf204.xml");
        if (!File.Exists(path))
        {
            await _downloader.DownloadAsync(Ccel, path, new Progress<string>(_ => { }), cancellationToken);
        }

        List<(string Citation, string Text, bool IsHead)> passages;
        await using (var stream = File.OpenRead(path))
        {
            passages = AntonyLoader.Load(DionysiusLoader.Read(stream));
        }

        // The author row First1KGreek made, if it is there; the name it
        // already has is kept by the upsert.
        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:cts:greekLit:tlg2035",
            Name = "Athanasius of Alexandria",
            Namespace = "greekLit"
        }, cancellationToken);

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = "urn:cts:greekLit:tlg2035.tlg047",
            Title = "Vita Antonii (Life of Antony)",
            CitationScheme = "Section"
        }, cancellationToken);

        const string Urn = "tlg2035.tlg047.ellershaw-eng1";
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = Urn,
            Kind = EditionKind.Translation,
            Language = "eng",
            Translator = "H. Ellershaw (1892)",
            SourcePath = Urn,
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);
        await _textNodeRepo.BulkInsertAsync(PseudepigraphaIngestService.Nodes(editionId, passages), cancellationToken);
        await _headerRepo.SaveAsync(editionId, new EditionHeader
        {
            Title = "Life of Antony, translated by the Rev. H. Ellershaw",
            Publisher = "The Christian Literature Company",
            PublicationPlace = "New York",
            PublicationDate = "1892",
            SourceDescription = "Nicene and Post-Nicene Fathers, Second Series, vol. 4: Athanasius, Select Works and "
                + "Letters, ed. Archibald Robertson (New York, 1892), as transcribed by the Christian "
                + "Classics Ethereal Library (npnf204). Cited by the 94 sections; the letter to the monks abroad is "
                + "the preface. The editor's table of contents gives the headings. Footnotes are not included.",
            Availability = "Ellershaw's translation (1892) is in the public domain. The transcription is the CCEL's, "
                + "which asks to be contacted before its texts are republished or used commercially: for a book that "
                + "will be sold, ask the CCEL or quote from the 1892 printing."
        }, cancellationToken);

        PassagesInstalled = passages.Count;
        progress.Report($"Life of Antony: 94 sections, {PassagesInstalled:N0} passages.");
        return IngestOutcome.Clean;
    }
}
