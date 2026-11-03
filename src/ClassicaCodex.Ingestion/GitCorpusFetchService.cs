using System.Text;
using LibGit2Sharp;

namespace ClassicaCodex.Ingestion;

public record FetchProgress(string Message, int FilesExtracted);

/// <summary>
/// Downloads a git repository's files without ever letting the OS checkout
/// step run.
///
/// Every one of the CTS-organized repos this app uses names its files with
/// colons (urn:cts:greekLit:tlg0012.tlg001...xml), which NTFS refuses to
/// create - that's exactly what broke a plain `git clone` on Windows
/// earlier. LibGit2Sharp's ordinary clone would hit the same wall, since it
/// still performs a real working-tree checkout under the hood.
///
/// The fix is to skip checkout entirely (CloneOptions.Checkout = false),
/// then walk the resulting repository's git tree object-by-object and write
/// each blob's content out under a filename with the illegal characters
/// replaced - reading from git's own object database rather than asking the
/// filesystem to materialize the original names at all.
/// </summary>
public class GitCorpusFetchService
{
    /// <summary>What a clone writes out unless told otherwise: the TEI every corpus here is made of.</summary>
    public static readonly IReadOnlyList<string> DefaultFileExtensions = new[] { ".xml" };

    /// <summary>
    /// The kinds of file written out of the clone, by extension; everything
    /// else in the tree is left in git's object store and thrown away with
    /// it. The AI translations are JSON, and a clone of them that kept only
    /// XML arrived as an empty folder.
    /// </summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = DefaultFileExtensions;

    /// <summary>
    /// Folders, relative to the output folder, made to hold what the tree
    /// holds and no more: once the tree is written out, any file there of
    /// one of FileExtensions that the tree no longer has is deleted. Empty
    /// unless a source asks for it.
    ///
    /// A clone otherwise writes over the folder and removes nothing, so a
    /// file deleted or renamed upstream stays in the reader's copy for good.
    /// For the TEI corpora that has never mattered, and they are left as they
    /// were. For the AI translations it does: every file in the folder is
    /// installed, and a leftover that no longer installs keeps the step from
    /// ever showing complete.
    ///
    /// Done after the new tree is written, not by emptying the folder first,
    /// so a clone that fails or is cancelled leaves the old files usable.
    /// </summary>
    public IReadOnlyList<string> MirroredFolders { get; init; } = Array.Empty<string>();

    public async Task FetchAsync(
        string repoUrl,
        string outputFolder,
        IProgress<FetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Only ever a folder inside the clone's own: a rooted path or one that
        // climbs out would have the mirror delete files that are not its.
        // Checked before anything is downloaded.
        foreach (var folder in MirroredFolders)
        {
            if (Path.IsPathRooted(folder) || folder.Split('/', '\\').Contains(".."))
                throw new ArgumentException($"{folder} is not a folder inside the clone.", nameof(MirroredFolders));
        }

        await Task.Run(() =>
        {
            var tempClonePath = Path.Combine(Path.GetTempPath(), $"classicacodex-clone-{Guid.NewGuid():N}");

            try
            {
                progress?.Report(new FetchProgress($"Connecting to {repoUrl}...", 0));

                CloneOptions BuildCloneOptions(int? depth)
                {
                    var options = new CloneOptions
                    {
                        Checkout = false, // the whole point - see class remarks
                        FetchOptions =
                        {
                            OnTransferProgress = tp =>
                            {
                                progress?.Report(new FetchProgress(
                                    $"Downloading... {tp.ReceivedObjects}/{tp.TotalObjects} objects, " +
                                    $"{tp.ReceivedBytes / 1024 / 1024} MB",
                                    0));
                                return !cancellationToken.IsCancellationRequested;
                            },

                            // The transfer callback only fires once objects
                            // start arriving, and the server can spend a long
                            // time counting them first on a corpus this size.
                            // This one fires during that, so Cancel is noticed
                            // then rather than whenever the first packet lands.
                            OnProgress = _ => !cancellationToken.IsCancellationRequested
                        }
                    };

                    if (depth.HasValue) options.FetchOptions.Depth = depth.Value;
                    return options;
                }

                // A clone that reports a cancellation as a cancellation.
                //
                // THIS IS WHAT MADE CANCEL START A BIGGER DOWNLOAD INSTEAD OF
                // STOPPING. A progress callback returning false is how libgit2
                // is told to abort, and LibGit2Sharp surfaces that as
                // UserCancelledException - which derives from
                // LibGit2SharpException, the very type the shallow-clone
                // fallback below catches. So pressing Cancel aborted the
                // shallow clone, was read as "this server cannot do shallow",
                // and was answered by deleting the partial download and
                // starting a FULL clone of the same repository. The status line
                // said "fetching in full instead" and the machine went back to
                // work on something several times larger.
                //
                // It could not have been found by reading either piece alone.
                // The token was threaded through correctly and the fallback was
                // right about every failure that had ever happened to it;
                // nothing had ever cancelled, because until 3.10.1 no button
                // called Cancel.
                void CloneWith(int? depth)
                {
                    try
                    {
                        Repository.Clone(repoUrl, tempClonePath, BuildCloneOptions(depth));
                    }
                    catch (LibGit2SharpException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                }

                // Depth 1 - only the tip commit, none of the history behind
                // it. Nothing downstream reads history: the extraction below
                // walks repo.Head.Tip.Tree and nothing else, so every earlier
                // commit fetched was pure waste. On these corpora that's the
                // difference between a couple of gigabytes and a few hundred
                // megabytes, because the same files have been revised many
                // times over the repository's life and a full clone brings
                // down every past version of each.
                //
                // FetchOptions.Depth needs LibGit2Sharp newer than 0.30.0 -
                // the property doesn't exist there. Shallow clone is also
                // relatively new in libgit2 (1.7+) and needs server
                // cooperation, so a failure falls back to the full clone that
                // always worked rather than failing setup outright.
                try
                {
                    CloneWith(depth: 1);
                }
                catch (LibGit2SharpException)
                {
                    // Not reached on a cancellation: CloneWith converts that to
                    // OperationCanceledException, which is not a
                    // LibGit2SharpException and so passes straight through here
                    // rather than being mistaken for a server that cannot do
                    // shallow clones. See CloneWith.
                    progress?.Report(new FetchProgress(
                        "Shallow download unavailable for this repository - fetching in full instead...", 0));

                    // A failed clone can leave a partial directory behind,
                    // and Repository.Clone refuses a non-empty target.
                    TryDeleteDirectory(tempClonePath);
                    CloneWith(depth: null);
                }

                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(outputFolder);

                progress?.Report(new FetchProgress("Extracting files...", 0));

                using var repo = new Repository(tempClonePath);
                var tree = repo.Head.Tip.Tree;

                var extracted = 0;
                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ExtractTree(tree, string.Empty, outputFolder, FileExtensions, written, ref extracted, progress, cancellationToken);

                var removed = RemoveFilesGoneUpstream(outputFolder, MirroredFolders, FileExtensions, written, cancellationToken);

                progress?.Report(new FetchProgress(
                    removed == 0
                        ? $"Done - {extracted} file(s) extracted."
                        : $"Done - {extracted} file(s) extracted, {removed} no longer in the repository removed.",
                    extracted));
            }
            finally
            {
                TryDeleteDirectory(tempClonePath);
            }
        }, cancellationToken);
    }

    private static void ExtractTree(
        Tree tree, string relativePath, string outputRoot, IReadOnlyList<string> extensions,
        HashSet<string> written, ref int extracted,
        IProgress<FetchProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (var entry in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sanitizedName = SanitizeFileName(entry.Name);
            var childRelative = relativePath.Length == 0 ? sanitizedName : $"{relativePath}/{sanitizedName}";

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                ExtractTree((Tree)entry.Target, childRelative, outputRoot, extensions, written, ref extracted, progress, cancellationToken);
                continue;
            }

            if (entry.TargetType != TreeEntryTargetType.Blob) continue;
            if (!extensions.Any(e => entry.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue; // nothing else here is needed

            var destPath = Path.Combine(outputRoot, childRelative.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            var blob = (Blob)entry.Target;
            using (var contentStream = blob.GetContentStream())
            using (var fileStream = File.Create(destPath))
            {
                contentStream.CopyTo(fileStream);
            }

            written.Add(Path.GetFullPath(destPath));
            extracted++;
            if (extracted % 250 == 0)
            {
                progress?.Report(new FetchProgress($"Extracted {extracted} file(s)...", extracted));
            }
        }
    }

    /// <summary>
    /// Deletes each file in the mirrored folders, of the extensions the clone
    /// writes, that this clone did not write. See MirroredFolders.
    ///
    /// Compared without regard to case, because the folders Windows and macOS
    /// give a reader are case-insensitive: a file renamed upstream only in its
    /// case is written over the old one, which keeps the old name's case on
    /// disk, and an ordinal comparison would take the file just written for a
    /// leftover and delete it.
    /// </summary>
    private static int RemoveFilesGoneUpstream(
        string outputRoot, IReadOnlyList<string> folders, IReadOnlyList<string> extensions,
        HashSet<string> written, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var folder in folders)
        {
            var path = Path.Combine(outputRoot, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!extensions.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                if (written.Contains(Path.GetFullPath(file))) continue;

                File.Delete(file);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Replaces every character NTFS won't allow in a filename with an underscore.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;

            // Git's object files are written read-only; clear that first or the delete fails.
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort - a leftover temp clone isn't worth failing the whole operation over.
        }
    }
}
