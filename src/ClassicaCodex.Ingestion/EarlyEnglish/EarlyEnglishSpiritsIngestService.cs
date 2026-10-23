using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.EarlyEnglish;

/// <summary>One book from the Text Creation Partnership's transcriptions of Early English Books Online.</summary>
/// <param name="TcpId">The TCP identifier, which is also the name of the book's GitHub repository.</param>
/// <param name="AuthorUrn">The author's identifier - an existing one where the library already has the man.</param>
public sealed record EarlyEnglishBook(
    string TcpId, string AuthorUrn, string AuthorName, string Title, string Imprint, int Phase);

/// <summary>
/// Twenty-one English books of 1572-1681 on devils, spirits, angels, witches and
/// ghosts, from the Text Creation Partnership's transcriptions of Early English
/// Books Online: James VI's Daemonologie, Scot's Discovery of Witchcraft with
/// its catalogue of devils, Heywood's Hierarchie of the Blessed Angells, both
/// texts of Marlowe's Doctor Faustus, Paradise Lost in its first and second
/// editions, Agrippa, the angel conversations of John Dee, Burton, Lavater,
/// Perkins, Glanvill, Henry More and Thomas Browne.
///
/// <b>Why these and not the Renaissance collection's way.</b> Perseus's
/// Renaissance texts come with their own split into redistributable and not,
/// and every Marlowe play fell on the wrong side of it - the README says so
/// under "What Marlowe isn't here". The TCP transcriptions have no such
/// split: Phase I has been public domain since 2015 and Phase II was released
/// under CC0 in 2020, both "even for commercial purposes, all without asking
/// permission", as every file's header says. The 1604 and 1616 Faustus are
/// both here.
///
/// <b>As printed, not modernised.</b> These are transcriptions of particular
/// printings, in their own spelling: long s, u for v, the 1667 Paradise Lost
/// in ten books. Each is filed under the title and date of that printing, so
/// the 1667 and 1674 Paradise Lost are two works - their books are divided
/// differently and a reference to one is not a reference to the other.
///
/// The TEI is light enough for the parser that reads Perseus, with two
/// things put right first - see <see cref="Prepare"/>.
/// </summary>
public class EarlyEnglishSpiritsIngestService
{
    public const string CollectionKey = "early-english-spirits";

    public const string Language = "eng";

    public static string UrlFor(string tcpId) =>
        $"https://raw.githubusercontent.com/textcreationpartnership/{tcpId}/master/{tcpId}.xml";

    private const string James = "urn:cts:engLit:james1";
    private const string Marlowe = "engLit:renaissance:christopher_marlowe";

    /// <summary>
    /// The books, by importance to the subject rather than by date. James I and
    /// Marlowe carry the identifiers the Renaissance collection gave them, so
    /// their new books join the man already in the library instead of a
    /// namesake.
    /// </summary>
    public static readonly IReadOnlyList<EarlyEnglishBook> Books = new[]
    {
        new EarlyEnglishBook("A04243", James, "James I", "Daemonologie", "Edinburgh, 1597", 1),
        new EarlyEnglishBook("A62397", "urn:eebo:author:scot-reginald", "Reginald Scot",
            "The Discovery of Witchcraft (third edition)", "London, 1665", 1),
        new EarlyEnglishBook("A03207", "urn:eebo:author:heywood-thomas", "Thomas Heywood",
            "The Hierarchie of the Blessed Angells", "London, 1635", 1),
        new EarlyEnglishBook("A50919", "urn:eebo:author:milton-john", "John Milton",
            "Paradise Lost (1667, ten books)", "London, 1667", 1),
        new EarlyEnglishBook("A50924", "urn:eebo:author:milton-john", "John Milton",
            "Paradise Lost (1674, twelve books)", "London, 1674", 2),
        new EarlyEnglishBook("A50931", "urn:eebo:author:milton-john", "John Milton",
            "Paradise Regain'd, with Samson Agonistes", "London, 1671", 1),
        new EarlyEnglishBook("A07009", Marlowe, "Christopher Marlowe",
            "Doctor Faustus (1604, the A-text)", "London, 1604", 1),
        new EarlyEnglishBook("A07013", Marlowe, "Christopher Marlowe",
            "Doctor Faustus (1616, the B-text)", "London, 1616", 2),
        new EarlyEnglishBook("A26565", "urn:eebo:author:agrippa", "Henry Cornelius Agrippa",
            "Three Books of Occult Philosophy", "London, 1651", 1),
        new EarlyEnglishBook("A26564", "urn:eebo:author:agrippa", "Henry Cornelius Agrippa",
            "The Fourth Book of Occult Philosophy, with the Heptameron and the Arbatel", "London, 1665", 1),
        new EarlyEnglishBook("A37412", "urn:eebo:author:dee-john", "John Dee",
            "A True and Faithful Relation of what passed between Dr. John Dee and some Spirits", "London, 1659", 1),
        new EarlyEnglishBook("A17310", "urn:eebo:author:burton-robert", "Robert Burton",
            "The Anatomy of Melancholy", "Oxford, 1621", 1),
        new EarlyEnglishBook("A05186", "urn:eebo:author:lavater-ludwig", "Ludwig Lavater",
            "Of Ghostes and Spirites Walking by Nyght", "London, 1572", 1),
        new EarlyEnglishBook("A01716", "urn:eebo:author:gifford-george", "George Gifford",
            "A Dialogue concerning Witches and Witchcraftes", "London, 1593", 1),
        new EarlyEnglishBook("A01718", "urn:eebo:author:gifford-george", "George Gifford",
            "A Discourse of the Subtill Practises of Devilles by Witches and Sorcerers", "London, 1587", 1),
        new EarlyEnglishBook("A09402", "urn:eebo:author:perkins-william", "William Perkins",
            "A Discourse of the Damned Art of Witchcraft", "Cambridge, 1610", 1),
        new EarlyEnglishBook("A42824", "urn:eebo:author:glanvill-joseph", "Joseph Glanvill",
            "Saducismus Triumphatus", "London, 1681", 1),
        new EarlyEnglishBook("A42820", "urn:eebo:author:glanvill-joseph", "Joseph Glanvill",
            "A Philosophical Endeavour towards the Defence of the Being of Witches and Apparitions", "London, 1666", 1),
        new EarlyEnglishBook("A51284", "urn:eebo:author:more-henry", "Henry More",
            "An Antidote against Atheisme", "London, 1653", 1),
        new EarlyEnglishBook("A51304", "urn:eebo:author:more-henry", "Henry More",
            "The Immortality of the Soul", "London, 1659", 1),
        new EarlyEnglishBook("A29868", "urn:eebo:author:browne-thomas", "Thomas Browne",
            "Religio Medici", "London, 1642", 1),
    };

    private readonly TeiParser _teiParser = new();
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int BooksInstalled { get; private set; }
    public int PassagesInstalled { get; private set; }

    public List<(string Book, string Error)> Failed { get; } = new();

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        for (var i = 0; i < Books.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var book = Books[i];
            var path = Path.Combine(root, book.TcpId + ".xml");

            try
            {
                if (!File.Exists(path))
                {
                    await _downloader.DownloadAsync(
                        UrlFor(book.TcpId), path, new Progress<string>(_ => { }), cancellationToken);
                }

                var count = await InstallAsync(book, File.ReadAllText(path), cancellationToken);
                BooksInstalled++;
                PassagesInstalled += count;
                progress.Report($"[{i + 1}/{Books.Count}] {book.Title}: {count:N0} passages");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failed.Add((book.Title, ex.GetBaseException().Message));
            }
        }

        progress.Report($"{BooksInstalled} books, {PassagesInstalled:N0} passages.");
        return IngestOutcome.From(Failed, attempted: Books.Count);
    }

    private async Task<int> InstallAsync(EarlyEnglishBook book, string raw, CancellationToken cancellationToken)
    {
        var prepared = Prepare(raw);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = book.AuthorUrn,
            Name = book.AuthorName,
            Namespace = "engLit",
            Language = Language
        }, cancellationToken);

        var workUrn = $"urn:eebo:{book.TcpId.ToLowerInvariant()}";
        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = workUrn,
            Title = book.Title
        }, cancellationToken);

        var editionUrn = $"{workUrn}:tcp";
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = editionUrn,
            Kind = EditionKind.Original,
            Language = Language,
            SourcePath = editionUrn,
            Collection = CollectionKey
        }, cancellationToken);

        var parsed = _teiParser.ParseXml(prepared);
        var nodes = _teiParser.ToTextNodes(editionId, parsed);
        if (nodes.Count == 0) throw new InvalidDataException("the transcription produced no passages");

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);
        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);

        var header = TeiHeaderReader.Read(XDocument.Parse(prepared)) ?? new EditionHeader();
        header.Title ??= book.Title;
        header.PublicationPlace ??= book.Imprint;
        header.SourceDescription =
            $"Early English Books Online, Text Creation Partnership {book.TcpId} (Phase {(book.Phase == 1 ? "I" : "II")}), "
            + $"a transcription of the {book.Imprint} printing.";
        header.Availability = book.Phase == 1
            ? "EEBO-TCP Phase I: public domain since 1 January 2015, released under CC0 1.0."
            : "EEBO-TCP Phase II: released under CC0 1.0, \"even for commercial purposes, all without asking permission\".";
        await _headerRepo.SaveAsync(editionId, header, cancellationToken);

        return nodes.Count;
    }

    private static readonly Regex LineEndHyphen = new(
        @"<g ref=""char:EOL(?:un)?hyphen""\s*/>\s*", RegexOptions.Compiled);

    /// <summary>
    /// The two things the TCP markup does that the Perseus parser would get
    /// wrong.
    ///
    /// <b>Words broken at the end of a printed line.</b> TCP keeps the break
    /// and marks it, 47,214 times across these books: "disobedi&lt;g
    /// ref="char:EOLhyphen"/&gt;ence". Left in, the parser writes "disobedi
    /// ence", which is two words to search and to read. The marker and the
    /// line break after it are taken out, so the word closes up - with or
    /// without the printed hyphen, which is what "EOLunhyphen" records.
    ///
    /// <b>Gaps.</b> Where the microfilm was illegible TCP writes a gap with a
    /// description of what is missing - a bullet for each letter, "〈◊〉" for a
    /// word, "〈 in non-Latin alphabet 〉" for Greek or Hebrew type. Those
    /// descriptions are the transcribers' and stay in the text, the way TCP's
    /// own reading copies print them, because a sentence that silently loses a
    /// word reads as if it never had one.
    ///
    /// <b>Several books in one volume.</b> Where a printing bound several
    /// texts together, TCP writes a &lt;group&gt; of &lt;text&gt; elements, and
    /// the parser reads the first body it finds. Saducismus Triumphatus is five
    /// texts so bound, and came out as its prefatory letter alone - 76,000
    /// characters of a million. The texts are brought into one body, a part
    /// apiece, numbered in the order they are printed.
    /// </summary>
    public static string Prepare(string raw)
    {
        var joined = LineEndHyphen.Replace(raw, string.Empty);
        if (!joined.Contains("<group", StringComparison.Ordinal)) return joined;

        var document = XDocument.Parse(joined, LoadOptions.PreserveWhitespace);
        var group = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "group");
        if (group == null) return joined;

        var ns = group.Name.Namespace;
        var body = new XElement(ns + "body");
        var n = 0;

        foreach (var text in group.Elements().Where(e => e.Name.LocalName == "text").ToList())
        {
            n++;
            body.Add(new XElement(ns + "div",
                new XAttribute("type", "part"),
                new XAttribute("n", n),
                text.Elements().SelectMany(section => section.Nodes())));
        }

        group.ReplaceWith(body);
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
