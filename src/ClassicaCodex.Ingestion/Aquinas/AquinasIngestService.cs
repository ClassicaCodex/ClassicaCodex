using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Pseudepigrapha;

namespace ClassicaCodex.Ingestion.Aquinas;

/// <summary>
/// The First Part of Thomas Aquinas's Summa Theologiae in the English
/// Dominican translation: all 119 questions, with the treatise on the angels
/// (questions 50-64), the fall of the demons (63-64) and the angels' and
/// demons' action on the world (106-114).
///
/// <b>English only, and why.</b> Aquinas's Latin is in the public domain, but
/// the digital Latin texts are not: Corpus Thomisticum reserves all rights in
/// its edition, the Latin Wikisource Summa is a copy of it, and the one
/// openly licensed transcription is unvetted OCR of a 1931 printing. So the
/// work has no original-language pane, as Jubilees has none, and the
/// translation is filed under it.
///
/// The translation is the one most English readers have quoted: the Fathers
/// of the English Dominican Province (1911-1925, revised 1920-1922), in the
/// Benziger printing of 1947, from Project Gutenberg. Its principal
/// translator, Laurence Shapcote, died in 1947.
/// </summary>
public class AquinasIngestService
{
    public const string CollectionKey = "aquinas";

    private const string Gutenberg = "https://gutenberg.pglaf.org/1/7/6/1/17611/17611-0.txt";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int PassagesInstalled { get; private set; }
    public int ArticlesInstalled { get; private set; }

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        progress.Report("Aquinas: the First Part of the Summa from Project Gutenberg...");
        var path = Path.Combine(root, "summa-prima-pars-17611.txt");
        if (!File.Exists(path))
        {
            await _downloader.DownloadAsync(Gutenberg, path, new Progress<string>(_ => { }), cancellationToken);
        }

        var passages = SummaLoader.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (passages.Count == 0) throw new InvalidDataException("No passages were found in the Summa");

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:aquinas",
            Name = "Thomas Aquinas",
            Namespace = "aquinas",
            Language = "lat"
        }, cancellationToken);

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = "urn:aquinas:summa-theologiae:1",
            Title = "Summa Theologiae, First Part",
            CitationScheme = "Question.Article.Part"
        }, cancellationToken);

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = "urn:aquinas:summa-theologiae:1:dominicans",
            Kind = EditionKind.Translation,
            Language = "eng",
            Translator = "Fathers of the English Dominican Province",
            SourcePath = "urn:aquinas:summa-theologiae:1:dominicans",
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);
        await _textNodeRepo.BulkInsertAsync(PseudepigraphaIngestService.Nodes(editionId, passages), cancellationToken);
        await _headerRepo.SaveAsync(editionId, new EditionHeader
        {
            Title = "The Summa Theologica of St. Thomas Aquinas, First Part, literally translated by Fathers of the "
                + "English Dominican Province",
            Publisher = "Benziger Brothers",
            PublicationPlace = "New York",
            PublicationDate = "1947",
            SourceDescription = "The second and revised edition of the English Dominican translation (London: Burns "
                + "Oates and Washbourne, 1920-1922), in the Complete American Edition (New York: Benziger, 1947); "
                + "Project Gutenberg #17611 (2006), produced by Sandra K. Perry with corrections by David McClamrock, "
                + "who restored the question and article numbers and marked his few corrections from the Latin in "
                + "square brackets. Italics are not kept. Cited by question, article and part: 50.1.ob1 is the first "
                + "objection of question 50, article 1; sc is On the contrary, co I answer that, ad1 the reply to "
                + "the first objection.",
            Availability = "Public domain. First published 1911-1925; Laurence Shapcote, its principal translator, "
                + "died in 1947."
        }, cancellationToken);

        PassagesInstalled = passages.Count;
        ArticlesInstalled = passages
            .Select(p => p.Citation.Split('.'))
            .Where(parts => parts.Length >= 3 && int.TryParse(parts[1], out _))
            .Select(parts => (parts[0], parts[1]))
            .Distinct()
            .Count();
        progress.Report($"Aquinas: {ArticlesInstalled:N0} articles, {PassagesInstalled:N0} passages.");
        return IngestOutcome.Clean;
    }
}
