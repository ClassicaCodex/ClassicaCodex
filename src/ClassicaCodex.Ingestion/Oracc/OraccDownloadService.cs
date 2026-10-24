using System.IO.Compression;

namespace ClassicaCodex.Ingestion.Oracc;

/// <summary>One Oracc project offered by the setup step.</summary>
/// <param name="Id">
/// The download id, which is the project path with its slash turned into a
/// dash: rinap/rinap1 downloads as rinap-rinap1.zip.
/// </param>
/// <param name="Title">What the step calls it while it is downloading.</param>
/// <param name="ApproxMegabytes">
/// Rounded size, measured 2026-09-21. Used only to tell a reader what they
/// are about to spend; nothing checks it.
/// </param>
/// <param name="TranslationCredit">
/// Set only for a project whose website releases its English translations
/// under an open licence, checked on the project's own pages: then each
/// translated text's page is fetched and its English goes in beside the
/// signs, credited with this. Null leaves a project as the download has it -
/// signs, transliteration and word meanings, no prose.
/// </param>
public sealed record OraccProjectChoice(string Id, string Title, double ApproxMegabytes, string? TranslationCredit = null);

/// <summary>
/// Fetches Oracc project downloads and finds the project inside them.
/// </summary>
public class OraccDownloadService
{
    private const string BaseUrl = "https://oracc.museum.upenn.edu/json/";

    /// <summary>
    /// The projects the setup step installs.
    ///
    /// A curated handful rather than a list, because Oracc publishes 208 of
    /// them and a setup step that asks a reader to choose from 208 project
    /// abbreviations is a setup step nobody finishes. These were picked to be
    /// a library rather than a sample: royal inscriptions and letters for the
    /// bulk of readable Akkadian, commentaries and bilinguals for the
    /// scholarly layer, and amgg because it is a whole project about the gods
    /// and weighs 120KB.
    ///
    /// dcclt, the lexical corpus, is deliberately out: it is 74MB on its own -
    /// more than everything here together - and word lists are the least
    /// rewarding thing to meet first.
    ///
    /// NOT EVERY ORACC PROJECT HOLDS TEXTS, which is worth knowing before
    /// adding to this list. amgg - Ancient Mesopotamian Gods and Goddesses -
    /// looks like exactly what a reader chasing a god would want, and its
    /// download is four entries: metadata, a portal file, and an EMPTY
    /// corpusjson folder. It is an encyclopedia about the gods rather than a
    /// corpus of them, so it would install nothing and be listed as though it
    /// had. Check a candidate's corpusjson has files in it before adding it.
    ///
    /// The gods are in the texts below regardless: Oracc tags every divine
    /// name with the part of speech DN, so they are found by lemma rather
    /// than by having a project of their own.
    ///
    /// Anyone wanting more can drop another project's folder alongside these;
    /// the importer reads whatever it finds.
    /// </summary>
    public static IReadOnlyList<OraccProjectChoice> StarterProjects { get; } = new[]
    {
        new OraccProjectChoice("rinap-rinap1", "Tiglath-pileser III and Shalmaneser V", 3.2),
        new OraccProjectChoice("rinap-rinap3", "Sennacherib", 11.6),
        new OraccProjectChoice("rinap-rinap4", "Esarhaddon", 7.5),
        new OraccProjectChoice("saao-saa01", "State Archives of Assyria: letters to Sargon II", 4.8),
        new OraccProjectChoice("blms", "Bilinguals in Late Mesopotamian Scholarship", 10.5),
        new OraccProjectChoice("ccpo", "Cuneiform Commentaries", 9.5),
        new OraccProjectChoice("glass", "Glassmaking texts", 1.1),

        // MAGIC. The anti-witchcraft corpus and the scholars' libraries of
        // Sultantepe and Uruk, whose sites - unlike the RINAP and SAA volumes
        // above, which are published books - release their translations under
        // CC BY-SA 3.0: "CC BY-SA The CMAwRo Project, 2014-" on every
        // CMAwRo page, and each GKAB text's credits line. Maqlû is the
        // composite text of all eight tablets and the ritual tablet,
        // normalised rather than transliterated, as Abusch edits it. There is
        // no composite Šurpu on Oracc; its tablets from Sultantepe and Uruk are
        // in GKAB, with the Udug-hul, Namburbi and Muššu'u tablets beside them.
        new OraccProjectChoice("cmawro-maqlu", "Maqlû, the anti-witchcraft ceremony", 3.1,
            "Tzvi Abusch, The Magical Ceremony Maqlû (Brill 2015), from CMAwRo - CC BY-SA 3.0"),
        new OraccProjectChoice("cmawro-cmawr1", "Anti-witchcraft rituals, volume 1", 5.5,
            "Tzvi Abusch and Daniel Schwemer, CMAwR 1 (Brill 2011), from CMAwRo - CC BY-SA 3.0"),
        new OraccProjectChoice("cmawro-cmawr2", "Anti-witchcraft rituals, volume 2", 5.2,
            "Tzvi Abusch and Daniel Schwemer, CMAwR 2 (Brill 2016), from CMAwRo - CC BY-SA 3.0"),
        new OraccProjectChoice("cmawro-cmawr3", "Anti-witchcraft rituals, volume 3", 2.9,
            "Tzvi Abusch, Daniel Schwemer, Mikko Luukko and Greta Van Buylaere, CMAwR 3 (Brill 2020), from CMAwRo - CC BY-SA 3.0"),
        new OraccProjectChoice("cams-gkab", "Scholars' libraries of Sultantepe, Nimrud and Uruk (with Šurpu)", 29.3,
            "the GKAB Project (Philippe Clancier, Eleanor Robson and others) - CC BY-SA 3.0"),
    };

    /// <summary>Roughly what the whole step downloads, in megabytes.</summary>
    public static int ApproxTotalMegabytes =>
        (int)Math.Round(StarterProjects.Sum(p => p.ApproxMegabytes));

    private readonly FileDownloadService _downloads = new();

    /// <summary>
    /// Downloads one project and extracts it, returning the folder that holds
    /// its metadata.json - which is what <see cref="OraccProjectReader"/>
    /// wants and is not the folder the archive extracted to.
    /// </summary>
    public async Task<string> FetchAsync(
        OraccProjectChoice project,
        string destinationRoot,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var archive = Path.Combine(destinationRoot, project.Id + ".zip");
        var extractTo = Path.Combine(destinationRoot, project.Id);

        progress.Report($"Downloading {project.Title} ({project.ApproxMegabytes:0.#} MB)...");
        await _downloads.DownloadAsync(BaseUrl + project.Id + ".zip", archive, progress, cancellationToken);

        // Extracted fresh each time. A half-extracted folder left by a
        // cancelled run would otherwise be indistinguishable from a complete
        // one, and the importer would report a short project as a whole one.
        if (Directory.Exists(extractTo)) Directory.Delete(extractTo, recursive: true);
        Directory.CreateDirectory(extractTo);

        progress.Report($"Extracting {project.Title}...");
        ZipFile.ExtractToDirectory(archive, extractTo, overwriteFiles: true);

        // The archive is kept rather than deleted: re-running the step should
        // not re-download 48MB to fix one bad extraction, and a reader who
        // wants the disk back can empty the folder.
        var projectDirectory = FindProjectDirectory(extractTo)
            ?? throw new InvalidOperationException(
                $"No metadata.json anywhere inside {project.Id}.zip. Either the download is truncated " +
                "or Oracc has changed the archive layout.");

        return projectDirectory;
    }

    /// <summary>
    /// The folder holding metadata.json, searched for rather than assumed.
    ///
    /// THE DEPTH IS NOT CONSTANT. A top-level project extracts one folder
    /// deep - blms.zip gives blms/ - while a subproject extracts two -
    /// rinap-rinap1.zip gives rinap/rinap1/. Hardcoding either works for
    /// exactly half the corpus, and the half it fails on fails by finding no
    /// texts rather than by erroring, which is the worst way for it to go
    /// wrong: the step would report success over an empty import.
    ///
    /// Searched shallowest-first so that a project which somehow contains a
    /// nested project resolves to the outer one, and ordered so the answer
    /// does not depend on the filesystem's enumeration order.
    /// </summary>
    internal static string? FindProjectDirectory(string extractedRoot)
    {
        if (File.Exists(Path.Combine(extractedRoot, "metadata.json"))) return extractedRoot;

        var frontier = new List<string> { extractedRoot };

        // Four levels is far beyond anything Oracc publishes, and a bound
        // means a symlink loop or a pathological archive cannot hang setup.
        for (var depth = 0; depth < 4 && frontier.Count > 0; depth++)
        {
            var next = new List<string>();

            foreach (var directory in frontier.OrderBy(d => d, StringComparer.Ordinal))
            {
                foreach (var child in Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.Ordinal))
                {
                    if (File.Exists(Path.Combine(child, "metadata.json"))) return child;
                    next.Add(child);
                }
            }

            frontier = next;
        }

        return null;
    }
}
