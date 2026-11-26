using System.IO.Compression;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Fetches Menota's manuscripts and the entity table they are written against
/// into one folder, ready for <see cref="MenotaIngestService"/>.
///
/// Menota publishes one XML file per manuscript, and this step used to send
/// the reader to the catalogue to save them, and then to save
/// menota-entities.txt beside them. On an iPad that meant Safari, the Files
/// app and a zip file moved by hand. But the catalogue's own "Download all XML
/// files" button asks Menota's corpus server for the whole collection as one
/// archive, in an ordinary request with no sign-in and no session, and gets
/// menota.zip: 91 manuscripts under texts/, 23 MB, in about ten seconds. That
/// request is the one made here. It was found on 9 November 2026 in the
/// catalogue's script (downloadZipFile, at clarino.uib.no/menota). If Menota
/// changes it, this is the address to look for again.
///
/// Every manuscript in the archive states CC BY-SA 4.0, granted by its editor,
/// and each reader's copy comes from Menota rather than from this app.
/// </summary>
public sealed class MenotaDownloadService
{
    /// <summary>What the catalogue's Download all XML files button fetches.</summary>
    public const string ArchiveUrl = "https://clarino.uib.no/korpuskel-api/download-zip?corpus=menota";

    /// <summary>
    /// The MUFI entity table every manuscript assumes (see
    /// <see cref="MenotaXmlLoader.LoadEntities"/>), at both of the names
    /// menota.org answers to. Menota publishes it nowhere else.
    /// </summary>
    public static readonly IReadOnlyList<string> EntitiesUrls = new[]
    {
        "https://www.menota.org/menota-entities.txt",
        "https://menota.org/menota-entities.txt"
    };

    public const string EntitiesFileName = "menota-entities.txt";

    /// <summary>
    /// How often, and for how long each time, menota.org is asked for the
    /// entity table before the step gives up on it.
    ///
    /// menota.org does not always answer. On 9 November 2026, four requests in
    /// ten from a Mac hung for twenty seconds or had the connection reset, at
    /// both names, while clarino.uib.no, which serves the manuscripts, answered
    /// every time. The ones that worked took a second. On the iPad the first
    /// request hung until .NET gave up after 100 seconds, and Setup took that
    /// for a cancel and said only "Stopped." So each try is cut short, and the
    /// next goes to the other name. Five tries of twenty seconds is room for a
    /// slow connection: the file is 157 KB.
    /// </summary>
    internal int EntitiesAttempts { get; init; } = 5;

    internal TimeSpan EntitiesAttemptTimeout { get; init; } = TimeSpan.FromSeconds(20);

    internal TimeSpan RetryPause { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Stands in for FileDownloadService in the tests.</summary>
    internal Func<string, string, IProgress<string>, CancellationToken, Task> Download { get; init; }

    public MenotaDownloadService()
    {
        var files = new FileDownloadService();
        Download = files.DownloadAsync;
    }

    /// <summary>
    /// Downloads the entity table and the archive, and puts every manuscript
    /// in the archive into <paramref name="folder"/>. Returns how many there
    /// were.
    /// </summary>
    public async Task<int> FetchAsync(string folder, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);

        var downloading = new OncePerSecond(progress);

        progress.Report("Downloading the table of medieval letters (menota-entities.txt)...");
        await DownloadEntitiesAsync(folder, progress, downloading, cancellationToken);

        // Saved under its own name and deleted once read, so a folder that
        // already holds a menota.zip someone downloaded is left alone.
        var archive = Path.Combine(folder, "menota-download.zip");

        try
        {
            progress.Report("Downloading the manuscripts (about 23 MB)...");

            // clarino.uib.no answered every request on the day the tests above
            // failed four in ten, so one more try is enough. A timeout is made
            // an error here, because Setup reads a bare cancel as the reader's
            // own and says only "Stopped."
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await Download(ArchiveUrl, archive, downloading, cancellationToken);
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && attempt < 2)
                {
                    progress.Report($"clarino.uib.no: {ex.Message}. Trying again...");
                    await Task.Delay(RetryPause, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new HttpRequestException(
                        "Menota's catalogue server, clarino.uib.no, did not answer in time, so the manuscripts " +
                        "were not downloaded. Try again in a few minutes.");
                }
            }

            progress.Report("Unpacking the manuscripts...");
            var count = Extract(archive, folder, cancellationToken);

            if (count == 0)
            {
                throw new InvalidDataException(
                    "Menota's download had no manuscripts in it. The catalogue may have moved its download; " +
                    "the manuscripts can still be saved from the catalogue into this step's folder by hand.");
            }

            return count;
        }
        finally
        {
            try { File.Delete(archive); } catch (IOException) { /* re-downloaded next time anyway */ }
        }
    }

    /// <summary>
    /// Downloads menota-entities.txt, trying again when menota.org does not
    /// answer, and keeps it only if it defines characters. A page of HTML or an
    /// error message saved under that name would otherwise replace a good copy,
    /// and every manuscript would import with its thorns and eths as boxes.
    ///
    /// If menota.org never answers, a good copy already in the folder is used,
    /// since the table changes once a year or so. Without one the step stops
    /// with an error that says so, rather than as if it had been cancelled.
    /// </summary>
    internal async Task DownloadEntitiesAsync(
        string folder, IProgress<string> progress, IProgress<string> downloading, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(folder, "entities-download");
        var staged = Path.Combine(staging, EntitiesFileName);
        string? problem = null;

        try
        {
            for (var attempt = 1; attempt <= EntitiesAttempts; attempt++)
            {
                var url = EntitiesUrls[(attempt - 1) % EntitiesUrls.Count];
                var host = new Uri(url).Host;
                Directory.CreateDirectory(staging);

                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(EntitiesAttemptTimeout);

                try
                {
                    await Download(url, staged, downloading, limit.Token);

                    if (MenotaXmlLoader.LoadEntities(staging).Count > 0)
                    {
                        File.Move(staged, Path.Combine(folder, EntitiesFileName), overwrite: true);
                        return;
                    }

                    problem = $"{host} sent a file with no characters defined in it";
                }
                // Anything but the reader's own Cancel is worth another try:
                // no answer in time, a reset connection, an error page.
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    problem = ex is OperationCanceledException
                        ? $"{host} did not answer within {EntitiesAttemptTimeout.TotalSeconds:0} seconds"
                        : $"{host}: {ex.Message}";
                }

                if (attempt < EntitiesAttempts)
                {
                    progress.Report($"{problem}. Trying again ({attempt + 1} of {EntitiesAttempts})...");
                    await Task.Delay(RetryPause, cancellationToken);
                }
            }
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { /* empty or gone */ }
        }

        if (MenotaXmlLoader.LoadEntities(folder).Count > 0)
        {
            progress.Report($"{problem}. Using the menota-entities.txt already in the folder.");
            return;
        }

        throw new HttpRequestException(
            $"menota.org did not send the table of medieval letters (menota-entities.txt) after " +
            $"{EntitiesAttempts} tries - {problem}. It is published only there, and the manuscripts cannot be " +
            "read without it, so nothing was downloaded. menota.org is sometimes slow to answer; try again " +
            "in a few minutes.");
    }

    /// <summary>
    /// Passes on at most one line a second.
    ///
    /// FileDownloadService reports after every read, which for 23 MB on a fast
    /// connection is thousands of lines. This step's window writes every line
    /// into the log under its buttons, and on an iPad drawing them kept the
    /// screen frozen for minutes after a download that took seconds.
    /// </summary>
    private sealed class OncePerSecond(IProgress<string> inner) : IProgress<string>
    {
        private long _last = long.MinValue / 2;

        public void Report(string value)
        {
            var now = Environment.TickCount64;
            if (now - _last < 1000) return;

            _last = now;
            inner.Report(value);
        }
    }

    /// <summary>
    /// Writes each manuscript in the archive into the folder, and returns how
    /// many there were.
    ///
    /// A manuscript the folder already holds is replaced where it is, wherever
    /// that is, rather than written again under texts/. Its plan sits beside
    /// it and is found by its path, so moving the file would separate the two,
    /// and a reader who saved manuscripts by hand before this step fetched them
    /// would get every one twice. Anything else in the folder is left alone,
    /// including manuscripts Menota no longer offers.
    ///
    /// Each file is written under a temporary name and renamed once complete,
    /// so a cancelled run cannot leave half a manuscript looking like a whole
    /// one.
    /// </summary>
    internal static int Extract(string archivePath, string folder, CancellationToken cancellationToken = default)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var root = full + Path.DirectorySeparatorChar;

        var existing = Directory
            .EnumerateFiles(full, "*.xml", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key!, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var count = 0;
        using var zip = ZipFile.OpenRead(archivePath);

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Name.Length == 0 || !entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

            var destination = existing.TryGetValue(entry.Name, out var already)
                ? already
                : Path.GetFullPath(Path.Combine(root, entry.FullName));

            // An entry naming a path outside the folder is not a manuscript.
            if (!destination.StartsWith(root, StringComparison.Ordinal)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            var partial = destination + ".partial";
            try
            {
                entry.ExtractToFile(partial, overwrite: true);
                File.Move(partial, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(partial)) File.Delete(partial);
            }

            count++;
        }

        return count;
    }
}
