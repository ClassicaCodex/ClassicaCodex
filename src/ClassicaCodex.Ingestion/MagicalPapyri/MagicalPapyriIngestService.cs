using System.Text.RegularExpressions;
using System.Xml;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Epigraphy;

namespace ClassicaCodex.Ingestion.MagicalPapyri;

/// <summary>
/// Puts the magical papyri into the library: the working handbooks, single
/// spells and amulets of Greek and Coptic Egypt.
///
/// Two sources. The Greek ones are papyri.info's - every papyrus that the
/// Digital Corpus of Literary Papyri or the HGV tags as magic and that has an
/// edited text, about 150 of them, with PGM I complete and most of the
/// Supplementum Magicum (see <see cref="MagicalPapyriCatalogue"/>). The Coptic
/// ones are the Kyprianos database's, about 350 spells from the Würzburg
/// Coptic Magical Papyri project, each with its English translation.
///
/// <b>What a work is.</b> A papyrus, or a Kyprianos text. The authors are the
/// collections a reader would look under: the Papyri Graecae Magicae, the
/// Supplementum Magicum, the other Greek papyri and amulets, and the Coptic
/// texts by what they were for - curses, love and favour, healing and
/// protection, and the rest.
///
/// <b>How a passage is cited.</b> Column and line where a papyrus has columns,
/// so 3.12 is line 12 of PGM I's third column, with "col. iii" in the margin;
/// line alone where it has one. A Kyprianos text the same way by page or side,
/// and its translation is cited with it, so the English sits beside the
/// Coptic it translates.
///
/// <b>What is not here.</b> The great handbooks - PGM IV in Paris, V, VII and
/// the Leiden XII and XIII - have no open digital text anywhere: papyri.info
/// holds their records with an empty edition, and they are left out because
/// there is nothing to read.
/// </summary>
public class MagicalPapyriIngestService
{
    public const string CollectionKey = "magical-papyri";
    public const string Namespace = "magic";

    public const string KyprianosUrl =
        "https://www.coptic-magic.phil.uni-wuerzburg.de/wp-content/uploads/2026/07/Kyprianos_Texts_exports_24.07.2026.csv";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly FileDownloadService _downloader;

    public MagicalPapyriIngestService(FileDownloadService? downloader = null) =>
        _downloader = downloader ?? new FileDownloadService();

    public int GreekTextsInstalled { get; private set; }
    public int CopticTextsInstalled { get; private set; }
    public int LinesInstalled { get; private set; }

    public int TextsInstalled => GreekTextsInstalled + CopticTextsInstalled;

    private static readonly IProgress<string> Silent = new Progress<string>(_ => { });

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        var failed = new List<(string, string)>();
        var installed = new HashSet<string>(StringComparer.Ordinal);
        var authors = new Dictionary<string, int>(StringComparer.Ordinal);

        // ---- Greek, from papyri.info
        var papyri = MagicalPapyriCatalogue.Papyri;
        for (var i = 0; i < papyri.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var papyrus = papyri[i];
            var path = Path.Combine(root, papyrus.Path.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    await _downloader.DownloadAsync(MagicalPapyriCatalogue.Url(papyrus), path, Silent, cancellationToken);

                var text = MagicalPapyriLoader.ReadGreek(papyrus, MagicalPapyriLoader.LoadXml(path));
                if (text != null)
                {
                    var (authorUrn, authorName) = GreekAuthor(papyrus, text.Language);
                    var authorId = await AuthorAsync(authors, authorUrn, authorName, text.Language, cancellationToken);
                    installed.UnionWith(await InstallGreekAsync(authorId, text, cancellationToken));
                    GreekTextsInstalled++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or XmlException or InvalidDataException)
            {
                failed.Add(($"{papyrus.Label} ({papyrus.Path})", ex.GetBaseException().Message));
            }

            if ((i + 1) % 25 == 0 || i == papyri.Count - 1)
                progress.Report($"Greek magical papyri: {i + 1} of {papyri.Count}...");
        }

        // ---- Coptic, from Kyprianos
        var csv = Path.Combine(root, "kyprianos", "Kyprianos_Texts_exports_24.07.2026.csv");
        var kyprianosAnswered = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(csv)!);
            if (!File.Exists(csv) || new FileInfo(csv).Length == 0)
            {
                progress.Report("Downloading the Kyprianos texts (2.4 MB)...");
                await _downloader.DownloadAsync(KyprianosUrl, csv, Silent, cancellationToken);
            }

            var texts = MagicalPapyriLoader.ReadKyprianos(csv);
            kyprianosAnswered = true;
            for (var i = 0; i < texts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = texts[i];
                var (authorUrn, authorName) = CopticAuthor(text);
                var authorId = await AuthorAsync(authors, authorUrn, authorName, text.Language, cancellationToken);
                installed.UnionWith(await InstallKyprianosAsync(authorId, text, cancellationToken));
                CopticTextsInstalled++;

                if ((i + 1) % 50 == 0 || i == texts.Count - 1)
                    progress.Report($"Coptic magical texts: {i + 1} of {texts.Count}...");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            failed.Add(("Kyprianos texts", ex.GetBaseException().Message));
        }

        // Editions an earlier run made and this one did not - a papyrus that
        // has since lost its text, a renamed group - come out, but only when
        // every source answered, so a dropped connection deletes nothing.
        if (failed.Count == 0 && kyprianosAnswered)
        {
            foreach (var workId in await _editionRepo.GetWorkIdsForCollectionsAsync(new[] { CollectionKey }, cancellationToken))
            {
                foreach (var edition in await _editionRepo.GetByWorkAsync(workId, cancellationToken))
                {
                    if (edition.Collection == CollectionKey && !installed.Contains(edition.CtsUrn))
                        await _editionRepo.DeleteEditionAsync(edition.EditionId, cancellationToken);
                }
            }
        }

        progress.Report($"{GreekTextsInstalled} Greek and {CopticTextsInstalled} Coptic magical texts, {LinesInstalled:N0} lines.");
        return IngestOutcome.From(failed, attempted: papyri.Count + 1);
    }

    private async Task<int> AuthorAsync(
        Dictionary<string, int> authors, string urn, string name, string language, CancellationToken cancellationToken)
    {
        if (authors.TryGetValue(urn, out var id)) return id;

        id = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = urn,
            Name = name,
            Namespace = Namespace,
            Language = language
        }, cancellationToken);
        authors[urn] = id;
        return id;
    }

    /// <summary>The collection a Greek papyrus is filed under: PGM, the Supplementum Magicum, or neither.</summary>
    public static (string Urn, string Name) GreekAuthor(MagicalPapyrus papyrus, string language)
    {
        if (language == "cop") return ("urn:magic:cop:papyri", "Coptic magical papyri (papyri.info)");
        if (papyrus.Label.StartsWith("PGM ", StringComparison.Ordinal)) return ("urn:magic:pgm", "Papyri Graecae Magicae");
        if (papyrus.Label.StartsWith("Suppl. Mag. ", StringComparison.Ordinal)) return ("urn:magic:suppl-mag", "Supplementum Magicum");
        return ("urn:magic:grc:other", "Greek magical papyri and amulets");
    }

    /// <summary>
    /// The group a Coptic text is filed under, by what it was for. Kyprianos
    /// names the purpose first in its type ("Love spell (magical, formulary)",
    /// "Curse against virility"), and over a hundred distinct types fall into
    /// these four.
    /// </summary>
    public static (string Urn, string Name) CopticAuthor(KyprianosText text)
    {
        var type = text.Type.ToLowerInvariant();
        if (text.Language == "grc") return ("urn:magic:kyp:greek", "Greek texts in the Coptic magical papyri");
        if (type.Contains("curse")) return ("urn:magic:kyp:curses", "Coptic curses");
        if (Regex.IsMatch(type, @"^(love|separation|reconciliation|favour|sleep|erotic|attraction)"))
            return ("urn:magic:kyp:love", "Coptic love and favour spells");
        if (Regex.IsMatch(type, @"^(healing|protection|exorcism|amulet)"))
            return ("urn:magic:kyp:healing", "Coptic healing and protection spells");
        return ("urn:magic:kyp:other", "Other Coptic magical texts");
    }

    private async Task<List<string>> InstallGreekAsync(int authorId, GreekMagicalText text, CancellationToken cancellationToken)
    {
        var papyrus = text.Papyrus;
        var urn = $"urn:magic:{papyrus.Source}:{papyrus.Id}";
        var title = string.IsNullOrWhiteSpace(papyrus.Title) ? papyrus.Label : $"{papyrus.Label}: {papyrus.Title}";

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = urn,
            Title = Shorten(title),
            CitationScheme = "Column.Line"
        }, cancellationToken);

        // Parts are numbered only when there is more than one of them, and only
        // the parts that hold lines count: PGM I's recto wraps its five
        // columns, and the recto itself is not a column.
        var parts = text.Lines.Select(l => l.Part).Distinct().ToList();
        var byPart = parts.Count > 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<TextNode>();

        foreach (var line in text.Lines)
        {
            var part = parts.IndexOf(line.Part) + 1;
            var citation = Unique(byPart ? $"{part}.{line.Line}" : line.Line, seen);
            var margin = byPart && !string.IsNullOrWhiteSpace(line.PartLabel)
                ? $"{papyrus.Label}, {PartName(line.PartLabel!, line.PartType)}"
                : papyrus.Label;
            nodes.Add(new TextNode { CitationRef = citation, Text = line.Text, Milestone = margin });
        }

        var editionUrn = $"{urn}:{text.Language}";
        await InstallEditionAsync(workId, editionUrn, EditionKind.Original, text.Language, null, "papyrus", nodes, cancellationToken);
        return new List<string> { editionUrn };
    }

    private async Task<List<string>> InstallKyprianosAsync(int authorId, KyprianosText text, CancellationToken cancellationToken)
    {
        var urn = $"urn:magic:kyp:t{text.Id}";
        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = urn,
            Title = Shorten($"{text.Title} (KYP T{text.Id})"),
            CitationScheme = text.SectionCount > 1 ? "Page.Line" : "Line"
        }, cancellationToken);

        List<TextNode> Nodes(IReadOnlyList<KyprianosLine> lines)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new List<TextNode>();
            string previous = "0";
            foreach (var line in lines)
            {
                // A line without a number - a drawing's caption - takes the
                // number of the line before it, made unique, so it stays where
                // it stands on the page.
                var number = line.Line ?? previous;
                previous = number;
                var citation = Unique(text.SectionCount > 1 ? $"{line.Section}.{number}" : number, seen);
                var margin = line.SectionLabel != null ? $"{text.Sigla}, {line.SectionLabel}" : text.Sigla;
                nodes.Add(new TextNode { CitationRef = citation, Text = line.Text, Milestone = Shorten(margin) });
            }

            return nodes;
        }

        var urns = new List<string> { $"{urn}:{text.Language}" };
        await InstallEditionAsync(workId, urns[0], EditionKind.Original, text.Language, null, "diplomatic",
            Nodes(text.Lines), cancellationToken);

        if (text.Translation.Count > 0)
        {
            urns.Add($"{urn}:eng");
            await InstallEditionAsync(workId, urns[1], EditionKind.Translation, "eng",
                "the Coptic Magical Papyri project (Kyprianos)", null, Nodes(text.Translation), cancellationToken);
        }

        return urns;
    }

    private async Task InstallEditionAsync(
        int workId, string urn, EditionKind kind, string language, string? translator, string? orthography,
        List<TextNode> nodes, CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = urn,
            Kind = kind,
            Language = language,
            Translator = translator,
            SourcePath = urn,
            Collection = CollectionKey,

            // Kept out of stylometry, the way the curse tablets are: a spell
            // was copied from handbook to amulet by whoever was paid to write
            // it, and a papyrus is not an author. The originals are marked
            // "papyrus" or "diplomatic" - Kyprianos prints the Coptic as the
            // scribe wrote it, abbreviation strokes and all.
            Orthography = kind == EditionKind.Original ? orthography : null
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        for (var i = 0; i < nodes.Count; i++)
        {
            nodes[i].EditionId = editionId;
            nodes[i].SortOrder = i + 1;
        }

        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
        if (kind == EditionKind.Original) LinesInstalled += nodes.Count;
    }

    /// <summary>
    /// A part as a papyrologist names it. The edition's subtype decides first,
    /// because "v" is the verso of one papyrus and the fifth column of PGM I;
    /// without one, "r" and "v" are recto and verso, a numeral is a column, and
    /// anything else is printed as given.
    /// </summary>
    public static string PartName(string label, string? type = null) => label switch
    {
        _ when type == "column" => $"col. {label}",
        _ when type is { Length: > 0 } && type != "column" && !Regex.IsMatch(label, @"^[rv]$") => $"{type} {label}",
        "r" or "recto" => "recto",
        "v" or "verso" => "verso",
        _ when Regex.IsMatch(label, @"^[ivxlc]+$", RegexOptions.IgnoreCase) => $"col. {label}",
        _ when Regex.IsMatch(label, @"^\d+$") => $"col. {label}",
        _ => label
    };

    /// <summary>A citation made unique within its edition by numbering a repeat ("5", then "5-2").</summary>
    private static string Unique(string citation, HashSet<string> seen)
    {
        var candidate = citation;
        for (var n = 2; !seen.Add(candidate); n++) candidate = $"{citation}-{n}";
        return candidate;
    }

    /// <summary>Titles and margins cut at a word near 160 characters; a few catalogue descriptions run to a paragraph.</summary>
    private static string Shorten(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length <= 160) return s;
        var cut = s.LastIndexOf(' ', 157);
        return s[..(cut > 100 ? cut : 157)].TrimEnd(',', ';', ' ') + "…";
    }
}
