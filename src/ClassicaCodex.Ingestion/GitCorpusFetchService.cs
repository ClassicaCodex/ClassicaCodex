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
    public async Task FetchAsync(
        string repoUrl,
        string outputFolder,
        IProgress<FetchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var tempClonePath = Path.Combine(Path.GetTempPath(), $"classicacodex-clone-{Guid.NewGuid():N}");

            try
            {
                progress?.Report(new FetchProgress($"Connecting to {repoUrl}...", 0));

                var cloneOptions = new CloneOptions
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
                        }
                    }
                };

                Repository.Clone(repoUrl, tempClonePath, cloneOptions);
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(outputFolder);

                progress?.Report(new FetchProgress("Extracting files...", 0));

                using var repo = new Repository(tempClonePath);
                var tree = repo.Head.Tip.Tree;

                var extracted = 0;
                ExtractTree(tree, string.Empty, outputFolder, ref extracted, progress, cancellationToken);

                progress?.Report(new FetchProgress($"Done - {extracted} file(s) extracted.", extracted));
            }
            finally
            {
                TryDeleteDirectory(tempClonePath);
            }
        }, cancellationToken);
    }

    private static void ExtractTree(
        Tree tree, string relativePath, string outputRoot, ref int extracted,
        IProgress<FetchProgress>? progress, CancellationToken cancellationToken)
    {
        foreach (var entry in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sanitizedName = SanitizeFileName(entry.Name);
            var childRelative = relativePath.Length == 0 ? sanitizedName : $"{relativePath}/{sanitizedName}";

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                ExtractTree((Tree)entry.Target, childRelative, outputRoot, ref extracted, progress, cancellationToken);
                continue;
            }

            if (entry.TargetType != TreeEntryTargetType.Blob) continue;
            if (!entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue; // nothing else here is needed

            var destPath = Path.Combine(outputRoot, childRelative.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            var blob = (Blob)entry.Target;
            using (var contentStream = blob.GetContentStream())
            using (var fileStream = File.Create(destPath))
            {
                contentStream.CopyTo(fileStream);
            }

            extracted++;
            if (extracted % 250 == 0)
            {
                progress?.Report(new FetchProgress($"Extracted {extracted} file(s)...", extracted));
            }
        }
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
