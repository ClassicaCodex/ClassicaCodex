using System.Security.Cryptography;

namespace ClassicaCodex.Core;

public sealed record SourceFileFingerprint(string FullPath, string FileName, long FileSize,
    DateTime ModifiedUtc, string Sha256);

public static class SourceFileFingerprinter
{
    public static async Task<SourceFileFingerprint> CreateAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists) throw new FileNotFoundException("The source file does not exist.", info.FullName);
        await using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new SourceFileFingerprint(info.FullName, info.Name, info.Length,
            info.LastWriteTimeUtc, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
