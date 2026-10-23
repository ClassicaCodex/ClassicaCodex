using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Sefaria;

namespace ClassicaCodex.Ingestion.Pseudepigrapha;

/// <summary>
/// The books outside the Bible that tell where angels and demons came from:
/// 1 Enoch in Charles's translation, the Testament of Solomon in Greek and
/// English, the Testaments of the Twelve Patriarchs in Greek and English,
/// Jubilees, the Greek Apocalypse of Moses and 2 Enoch.
///
/// These are the texts that name the Watchers and their two hundred on Mount
/// Hermon, the giants whose spirits became the demons, Mastema and the tenth
/// of the spirits left free, Beliar, Ornias and Asmodeus bound by Solomon's
/// ring, and Satanail's fall. The library had the Greek fragments of 1 Enoch
/// and nothing else of them.
///
/// <b>Four sources, chosen for their terms.</b> Charles's 1 Enoch (1917) and
/// Conybeare's Testament of Solomon (1898) are proofread on English Wikisource
/// and in the public domain. Charles's Jubilees (1917) and Twelve Patriarchs
/// (1908) come from Sefaria, which marks both public domain. The Greek texts
/// - McCown's Testament of Solomon (1922), the Greek of the Twelve Patriarchs
/// that Charles printed, Tischendorf's Apocalypse of Moses (1866) - come from
/// the Online Critical Pseudepigrapha under CC BY 4.0. Its other texts rest on
/// modern editions it could not license, and its English versions are recent
/// and unchecked; neither is taken.
///
/// <b>1 Enoch joins the Greek already here.</b> First1KGreek's Apocalypsis
/// Enochi is the same work, cited by chapter and verse, so Charles's English
/// is filed as its translation rather than as a book of its own: chapters
/// 1-32, where the Greek survives, read side by side, and the rest - the
/// Parables, the Astronomical Book, the Dream Visions - are in English only,
/// because only the Ethiopic has them.
/// </summary>
public class PseudepigraphaIngestService
{
    public const string CollectionKey = "pseudepigrapha";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    private readonly HashSet<int> _works = new();

    public int WorksInstalled { get; private set; }
    public int PassagesInstalled { get; private set; }

    public List<(string Text, string Error)> Failed { get; } = new();

    private const int Attempted = 6;

    private static readonly string[] Patriarchs =
    {
        "Reuben", "Simeon", "Levi", "Judah", "Issachar", "Zebulun",
        "Dan", "Naphtali", "Gad", "Asher", "Joseph", "Benjamin"
    };

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        await Attempt("1 Enoch", () => InstallEnochAsync(root, progress, cancellationToken));
        await Attempt("Testament of Solomon", () => InstallSolomonAsync(root, progress, cancellationToken));
        await Attempt("Testaments of the Twelve Patriarchs", () => InstallPatriarchsAsync(root, progress, cancellationToken));
        await Attempt("Jubilees", () => InstallJubileesAsync(root, progress, cancellationToken));
        await Attempt("Apocalypse of Moses", () => InstallMosesAsync(root, progress, cancellationToken));
        await Attempt("2 Enoch", () => InstallSecretsOfEnochAsync(root, progress, cancellationToken));

        progress.Report($"{WorksInstalled} works, {PassagesInstalled:N0} passages.");
        return IngestOutcome.From(Failed, attempted: Attempted);

        async Task Attempt(string name, Func<Task> install)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await install();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failed.Add((name, ex.GetBaseException().Message));
            }
        }
    }

    // ---- 1 Enoch ------------------------------------------------------------

    private async Task InstallEnochAsync(string root, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("1 Enoch: 108 chapters from Wikisource...");
        var passages = new List<(string Citation, string Text, bool IsHead)>();

        for (var chapter = 1; chapter <= 108; chapter++)
        {
            var title = $"The Book of Enoch (Charles)/Chapter {chapter:00}";
            var path = await FetchAsync(root, Path.Combine("enoch-charles", $"{chapter:000}.wiki"),
                WikisourceTextLoader.RawUrl(title), ct, politely: true);
            passages.AddRange(WikisourceTextLoader.EnochChapter(chapter, await File.ReadAllTextAsync(path, ct)));
        }

        // Filed under the identifiers First1KGreek gives the work, so the
        // English lands beside the Greek; the names are only used if the
        // Greek has not been installed.
        var authorId = await AuthorAsync("urn:cts:greekLit:tlg1463", "Liber Enoch", "greekLit", "grc", ct);
        var workId = await WorkAsync(authorId, "urn:cts:greekLit:tlg1463.tlg001", "Apocalypsis Enochi", "Chapter.Verse", ct);

        await EditionAsync(workId, "urn:pseudepigrapha:1enoch:charles1917", EditionKind.Translation, "eng",
            "R. H. Charles (1917)", passages,
            new EditionHeader
            {
                Title = "The Book of Enoch, translated by R. H. Charles",
                Publisher = "SPCK",
                PublicationPlace = "London",
                PublicationDate = "1917",
                SourceDescription = "R. H. Charles, The Book of Enoch (London: SPCK, 1917), with an introduction by "
                    + "W. O. E. Oesterley; transcribed on English Wikisource. Translated from the Ethiopic.",
                Availability = "Public domain (Charles died in 1931)."
            }, ct);
    }

    // ---- Testament of Solomon ---------------------------------------------

    private async Task InstallSolomonAsync(string root, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Testament of Solomon: McCown's Greek and Conybeare's English...");
        var authorId = await AuthorAsync("urn:pseudepigrapha:testament-of-solomon", "Testament of Solomon", "pseudepigrapha", "grc", ct);
        var workId = await WorkAsync(authorId, "urn:pseudepigrapha:testament-of-solomon", "Testament of Solomon", "Chapter.Verse", ct);

        var greekPath = await FetchAsync(root, "TSol.xml", OcpLoader.Root + "TSol.xml", ct);
        var (greek, bibliography) = OcpLoader.Load(greekPath, "Greek", "McCown");

        await EditionAsync(workId, "urn:pseudepigrapha:testament-of-solomon:mccown", EditionKind.Original, "grc", null,
            greek.Select(p => (p.Citation, p.Text, false)).ToList(),
            new EditionHeader
            {
                Title = "Testament of Solomon (Greek)",
                SourceDescription = (bibliography ?? "C. C. McCown, The Testament of Solomon (Leipzig, 1922)")
                    + " As encoded by the Online Critical Pseudepigrapha, McCown's text, without the recensions he printed beside it.",
                Availability = "CC BY 4.0, the Online Critical Pseudepigrapha. McCown's edition (1922) is in the public "
                    + "domain in the United States; he died in 1958."
            }, ct);

        // Conybeare numbered his translation in 130 sections of his own, from a
        // single Paris manuscript, and they do not correspond to McCown's
        // chapters. Cited as §1 to §130 so that neither numbering pretends to
        // be the other and the panes do not claim an alignment that is not there.
        var englishPath = await FetchAsync(root, "tsol-conybeare.json",
            WikisourceTextLoader.ParsedUrl("The Testament of Solomon"), ct, politely: true);
        var text = WikisourceTextLoader.RenderedText(await File.ReadAllTextAsync(englishPath, ct));
        var opening = text.IndexOf("1. Testament of Solomon", StringComparison.Ordinal);
        if (opening < 0) throw new InvalidDataException("Conybeare's first section was not found");

        var sections = WikisourceTextLoader.NumberedRuns(text[opening..], 1)
            .Select(s => ($"§{s.Number}", s.Text, false))
            .ToList();

        await EditionAsync(workId, "urn:pseudepigrapha:testament-of-solomon:conybeare", EditionKind.Translation, "eng",
            "F. C. Conybeare (1898)", sections,
            new EditionHeader
            {
                Title = "The Testament of Solomon, translated by F. C. Conybeare",
                Publisher = "The Jewish Quarterly Review 11",
                PublicationDate = "1898",
                SourceDescription = "F. C. Conybeare, \"The Testament of Solomon\", Jewish Quarterly Review 11 (1898), "
                    + "pp. 1-45, from the Paris manuscript; transcribed and proofread on English Wikisource. "
                    + "Cited by Conybeare's own sections, which are not McCown's chapters.",
                Availability = "Public domain (Conybeare died in 1924)."
            }, ct);
    }

    // ---- Testaments of the Twelve Patriarchs -------------------------------

    private async Task InstallPatriarchsAsync(string root, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Testaments of the Twelve Patriarchs: Greek and Charles's English...");
        var greekPath = await FetchAsync(root, "T12P.xml", OcpLoader.Root + "T12P.xml", ct);
        var (greek, bibliography) = OcpLoader.Load(greekPath, "Greek");

        const string Version = "Testaments of the Twelve Patriarchs, R. H. Charles,1908";
        var englishPath = await FetchAsync(root, "t12p-charles.json", SefariaExportLoader.UrlFor(
            "Second Temple/Apocrypha", "The Testaments of the Twelve Patriarchs", "English", Version), ct);
        using var english = SefariaExportLoader.Load(englishPath);
        RequireLicence(english, "Public Domain");

        var authorId = await AuthorAsync("urn:pseudepigrapha:twelve-patriarchs", "Testaments of the Twelve Patriarchs",
            "pseudepigrapha", "grc", ct);

        for (var i = 0; i < Patriarchs.Length; i++)
        {
            var name = Patriarchs[i];
            var slug = name.ToLowerInvariant();
            var workId = await WorkAsync(authorId, $"urn:pseudepigrapha:twelve-patriarchs:{slug}",
                $"Testament of {name}", "Chapter.Verse", ct);

            // The Greek carries each testament's title as a division named
            // "Title"; it is the heading, not a verse.
            var greekPassages = greek
                .Where(p => p.Citation.StartsWith(name + ".", StringComparison.Ordinal))
                .Select(p =>
                {
                    var rest = p.Citation[(name.Length + 1)..];
                    return rest == "Title" ? ("head", p.Text, true) : (rest, p.Text, false);
                })
                .ToList();

            await EditionAsync(workId, $"urn:pseudepigrapha:twelve-patriarchs:{slug}:grc", EditionKind.Original, "grc",
                null, greekPassages,
                new EditionHeader
                {
                    Title = $"Testament of {name} (Greek)",
                    SourceDescription = (bibliography ?? "") + " As encoded by the Online Critical Pseudepigrapha.",
                    Availability = "CC BY 4.0, the Online Critical Pseudepigrapha; the Greek text is in the public domain."
                }, ct);

            var key = english.Text.ValueKind == System.Text.Json.JsonValueKind.Object
                ? english.Text.EnumerateObject().Select(p => p.Name)
                    .FirstOrDefault(k => k.StartsWith($"The Testament of {name}", StringComparison.OrdinalIgnoreCase))
                : null;
            var node = key == null ? null : SefariaExportLoader.Node(english.Text, key);
            if (node == null) continue;

            await EditionAsync(workId, $"urn:pseudepigrapha:twelve-patriarchs:{slug}:charles1908", EditionKind.Translation,
                "eng", "R. H. Charles (1908)",
                SefariaExportLoader.Flatten(node.Value).Select(p => (p.Citation, p.Text, p.IsHead)).ToList(),
                Header($"The Testament of {name}, translated by R. H. Charles", "1908", english,
                    "R. H. Charles, The Testaments of the Twelve Patriarchs (London, 1908), from Sefaria."), ct);
        }
    }

    // ---- Jubilees ------------------------------------------------------------

    private async Task InstallJubileesAsync(string root, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Jubilees: Charles's English and Rubin's Hebrew...");
        var authorId = await AuthorAsync("urn:pseudepigrapha:jubilees", "Jubilees", "pseudepigrapha", "eng", ct);
        var workId = await WorkAsync(authorId, "urn:pseudepigrapha:jubilees", "The Book of Jubilees", "Chapter.Verse", ct);

        const string English = "The Book of Jubilees, trans. R. H. Charles. London [1917]";
        var englishPath = await FetchAsync(root, "jubilees-charles.json", SefariaExportLoader.UrlFor(
            "Second Temple/Apocrypha", "Book of Jubilees", "English", English), ct);
        using (var english = SefariaExportLoader.Load(englishPath))
        {
            RequireLicence(english, "Public Domain");
            await EditionAsync(workId, "urn:pseudepigrapha:jubilees:charles1917", EditionKind.Translation, "eng",
                "R. H. Charles (1917)",
                SefariaExportLoader.Flatten(english.Text).Select(p => (p.Citation, p.Text, p.IsHead)).ToList(),
                Header("The Book of Jubilees, translated by R. H. Charles", "1917", english,
                    "R. H. Charles, The Book of Jubilees (London: SPCK, 1917), translated from the Ethiopic; from Sefaria."), ct);
        }

        // Not the original: Jubilees was written in Hebrew, but what survives
        // whole is Ethiopic. This is Rubin's retranslation into Hebrew (Vienna,
        // 1870), and is filed as a translation for that reason, which leaves
        // the original-language pane empty - honestly so.
        var hebrewPath = await FetchAsync(root, "jubilees-rubin.json", SefariaExportLoader.UrlFor(
            "Second Temple/Apocrypha", "Book of Jubilees", "Hebrew", "Wikisource 2019"), ct);
        using var hebrew = SefariaExportLoader.Load(hebrewPath);
        RequireLicence(hebrew, "CC-BY-SA");
        await EditionAsync(workId, "urn:pseudepigrapha:jubilees:rubin1870", EditionKind.Translation, "heb",
            "Salomo Rubin (1870), Hebrew retranslation",
            SefariaExportLoader.Flatten(hebrew.Text).Select(p => (p.Citation, p.Text, p.IsHead)).ToList(),
            Header("Sefer ha-Yovelim, Hebrew retranslation by Salomo Rubin", "1870", hebrew,
                "Salomo Rubin's Hebrew retranslation from the Ethiopic (Vienna, 1870), as transcribed on Hebrew "
                + "Wikisource; from Sefaria. Not an ancient text."), ct);
    }

    // ---- Apocalypse of Moses ------------------------------------------------

    private async Task InstallMosesAsync(string root, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Apocalypse of Moses: Tischendorf's Greek...");
        var path = await FetchAsync(root, "AdamEve.xml", OcpLoader.Root + "AdamEve.xml", ct);
        var (greek, bibliography) = OcpLoader.Load(path, "Greek");

        var authorId = await AuthorAsync("urn:pseudepigrapha:life-of-adam-and-eve", "Life of Adam and Eve",
            "pseudepigrapha", "grc", ct);
        var workId = await WorkAsync(authorId, "urn:pseudepigrapha:life-of-adam-and-eve:greek",
            "Apocalypse of Moses (Greek Life of Adam and Eve)", "Chapter.Verse", ct);

        await EditionAsync(workId, "urn:pseudepigrapha:life-of-adam-and-eve:greek:tischendorf", EditionKind.Original, "grc",
            null, greek.Select(p => (p.Citation, p.Text, false)).ToList(),
            new EditionHeader
            {
                Title = "Apocalypse of Moses (Greek)",
                SourceDescription = (bibliography ?? "C. Tischendorf, Apocalypses Apocryphae (Leipzig, 1866)")
                    + " As encoded by the Online Critical Pseudepigrapha.",
                Availability = "CC BY 4.0, the Online Critical Pseudepigrapha; Tischendorf's text (1866) is in the public domain."
            }, ct);
    }

    // ---- 2 Enoch -------------------------------------------------------------

    private async Task InstallSecretsOfEnochAsync(string root, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("2 Enoch: Morfill's English...");
        var path = await FetchAsync(root, "2enoch.wiki", WikisourceTextLoader.RawUrl(
            "The Forgotten Books of Eden/The Book of the Secrets of Enoch"), ct, politely: true);
        var passages = WikisourceTextLoader.ChapterParagraphs(await File.ReadAllTextAsync(path, ct));

        var authorId = await AuthorAsync("urn:pseudepigrapha:2enoch", "2 Enoch", "pseudepigrapha", "eng", ct);
        var workId = await WorkAsync(authorId, "urn:pseudepigrapha:2enoch", "The Book of the Secrets of Enoch", "Chapter.Verse", ct);

        await EditionAsync(workId, "urn:pseudepigrapha:2enoch:morfill", EditionKind.Translation, "eng",
            "W. R. Morfill (1896)", passages.Select(p => (p.Citation, p.Text, false)).ToList(),
            new EditionHeader
            {
                Title = "The Book of the Secrets of Enoch, translated by W. R. Morfill",
                SourceDescription = "W. R. Morfill's translation from the Slavonic (Oxford, 1896, edited by R. H. Charles), "
                    + "as reprinted in The Forgotten Books of Eden (1926) and transcribed on English Wikisource.",
                Availability = "Public domain in the United States (published 1926). Morfill died in 1909 and Charles in "
                    + "1931; the 1926 reprint's editor died in 1975, so outside the United States check a quotation "
                    + "against the 1896 edition."
            }, ct);
    }

    // ---- shared --------------------------------------------------------------

    /// <summary>
    /// Refuses a Sefaria version whose file does not carry the licence the step
    /// was written for. Sefaria labels each version separately, and the version
    /// title is not a promise about terms.
    /// </summary>
    internal static void RequireLicence(SefariaExport version, string licence)
    {
        if (!string.Equals(version.License, licence, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Sefaria lists \"{version.VersionTitle}\" as \"{version.License}\", not \"{licence}\"");
        }
    }

    private static EditionHeader Header(string title, string date, SefariaExport version, string source) => new()
    {
        Title = title,
        PublicationDate = date,
        SourceDescription = source + (version.VersionSource == null ? "" : $" Sefaria's source: {version.VersionSource}."),
        Availability = version.License
    };

    private async Task<string> FetchAsync(
        string root, string relative, string url, CancellationToken ct, bool politely = false)
    {
        var path = Path.Combine(root, relative);
        if (File.Exists(path)) return path;

        // Wikimedia asks anonymous clients to keep their request rate down.
        if (politely) await Task.Delay(250, ct);

        await _downloader.DownloadAsync(url, path, new Progress<string>(_ => { }), ct);
        return path;
    }

    private Task<int> AuthorAsync(string urn, string name, string ns, string language, CancellationToken ct) =>
        _authorRepo.UpsertAsync(new Author { CtsUrn = urn, Name = name, Namespace = ns, Language = language }, ct);

    private Task<int> WorkAsync(int authorId, string urn, string title, string scheme, CancellationToken ct) =>
        _workRepo.UpsertAsync(new Work { AuthorId = authorId, CtsUrn = urn, Title = title, CitationScheme = scheme }, ct);

    private async Task EditionAsync(
        int workId, string urn, EditionKind kind, string language, string? translator,
        List<(string Citation, string Text, bool IsHead)> passages, EditionHeader header, CancellationToken ct)
    {
        if (passages.Count == 0) throw new InvalidDataException($"{urn}: no passages");

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = urn,
            Kind = kind,
            Language = language,
            Translator = translator,
            SourcePath = urn,
            Collection = CollectionKey
        }, ct);

        await _editionRepo.ClearTextNodesAsync(editionId, ct);
        await _textNodeRepo.BulkInsertAsync(Nodes(editionId, passages), ct);
        await _headerRepo.SaveAsync(editionId, header, ct);

        _works.Add(workId);
        WorksInstalled = _works.Count;
        PassagesInstalled += passages.Count;
    }

    /// <summary>
    /// TextNodes in reading order. A heading keeps its own citation (1.head)
    /// so that it can never take a verse's place in a search result or a
    /// bookmark.
    /// </summary>
    public static List<TextNode> Nodes(int editionId, IReadOnlyList<(string Citation, string Text, bool IsHead)> passages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<TextNode>(passages.Count);

        foreach (var (citation, text, isHead) in passages)
        {
            // A citation used twice would make the second unreachable; it
            // happens only where a source repeats a heading.
            var unique = citation;
            for (var n = 2; !seen.Add(unique); n++) unique = $"{citation}{n}";

            nodes.Add(new TextNode
            {
                EditionId = editionId,
                CitationRef = unique,
                SortOrder = nodes.Count + 1,
                Text = text,
                NodeKind = isHead ? TextNodeKinds.Head : TextNodeKinds.Line
            });
        }

        return nodes;
    }
}
