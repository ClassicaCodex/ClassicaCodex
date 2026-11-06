using System.Formats.Tar;
using System.IO.Compression;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Downloads a GitHub repository's files as one archive, for a device that
/// cannot run git.
///
/// <see cref="GitCorpusFetchService"/> needs libgit2, a native library with no
/// build for iOS, so every clone step on an iPad stopped at DllNotFoundException.
/// GitHub will hand over a repository's current files as a tar.gz, and that is
/// all a clone is used for here: it is shallow, and only its tip is read.
///
/// The files come out by the same rules because they are the same methods: the
/// extension filter, the sanitised names, and a mirrored folder tidied only once
/// everything has been written. So the two cannot come to disagree about what
/// a step's folder should hold.
///
/// Read as it arrives rather than saved first, so a download needs room for the
/// files it keeps and nothing else. That matters more on a device, where nothing
/// else can be cleared out of the way, and a repository of several gigabytes
/// would otherwise have to be on the disk twice.
/// </summary>
public class GitHubArchiveFetchService
{
    /// <summary>See <see cref="GitCorpusFetchService.FileExtensions"/>.</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = GitCorpusFetchService.DefaultFileExtensions;

    /// <summary>See <see cref="GitCorpusFetchService.MirroredFolders"/>.</summary>
    public IReadOnlyList<string> MirroredFolders { get; init; } = Array.Empty<string>();

    /// <summary>Answers the request instead of the network. Only the tests set it.</summary>
    internal HttpMessageHandler? Handler { get; init; }

    public async Task FetchAsync(
        string repoUrl,
        string outputFolder,
        IProgress<FetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var address = ArchiveUrl(repoUrl);

        // Checked before anything is downloaded, as a clone does.
        var mirrored = MirroredFolders
            .Select(folder => GitCorpusFetchService.MirroredFolderPath(outputFolder, folder))
            .ToList();

        await Task.Run(async () =>
        {
            progress?.Report(new FetchProgress($"Connecting to {repoUrl}...", 0));

            using var http = Handler == null ? new HttpClient() : new HttpClient(Handler, disposeHandler: false);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(FileDownloadService.UserAgent);

            using var response = await http.GetAsync(
                address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var download = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var unzipped = new GZipStream(download, CompressionMode.Decompress);
            await using var archive = new TarReader(unzipped);

            Directory.CreateDirectory(outputFolder);

            var extracted = 0;
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (await archive.GetNextEntryAsync(cancellationToken: cancellationToken) is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                if (RelativePath(entry.Name) is not { } relative) continue;
                if (!FileExtensions.Any(e => relative.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;

                var destPath = Path.Combine(outputFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                await using (var file = File.Create(destPath))
                {
                    if (entry.DataStream != null) await entry.DataStream.CopyToAsync(file, cancellationToken);
                }

                written.Add(Path.GetFullPath(destPath));
                extracted++;
                if (extracted % 250 == 0)
                {
                    progress?.Report(new FetchProgress($"Extracted {extracted} file(s)...", extracted));
                }
            }

            // Only once the whole archive is written out, so a fetch that fails
            // or is cancelled part of the way deletes nothing.
            var removed = GitCorpusFetchService.RemoveFilesNotInTree(mirrored, FileExtensions, written);

            progress?.Report(new FetchProgress(
                removed == 0
                    ? $"Done - {extracted} file(s) extracted."
                    : $"Done - {extracted} file(s) extracted, {removed} no longer in the repository removed.",
                extracted));
        }, cancellationToken);
    }

    /// <summary>
    /// Where GitHub serves a repository's current files. HEAD, because a clone
    /// follows the default branch and these repositories do not all name it alike.
    /// </summary>
    public static string ArchiveUrl(string repoUrl)
    {
        if (Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri)
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Trim('/').Split('/') is [var owner, var repository])
        {
            var name = repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^4] : repository;
            return $"https://codeload.github.com/{owner}/{name}/tar.gz/HEAD";
        }

        throw new ArgumentException(
            $"Only a repository on GitHub can be downloaded on this device, and {repoUrl} is not one.", nameof(repoUrl));
    }

    /// <summary>
    /// An entry's path inside the repository, as a clone would name it, or null
    /// for one that is not a file in there. GitHub puts everything in one folder
    /// named for the repository and the commit, which a clone would not have.
    ///
    /// A path that climbs is refused here, because unlike a git tree an archive
    /// can say anything, and the destination is built from what it says.
    /// </summary>
    internal static string? RelativePath(string entryName)
    {
        var segments = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments.Any(segment => segment is "." or "..")) return null;

        return string.Join('/', segments.Skip(1).Select(GitCorpusFetchService.SanitizeFileName));
    }
}
