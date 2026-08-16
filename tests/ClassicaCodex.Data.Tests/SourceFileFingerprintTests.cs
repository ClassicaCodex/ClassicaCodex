using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class SourceFileFingerprintTests
{
    [Fact]
    public async Task FingerprintUsesAbsolutePathMetadataAndSha256()
    {
        var directory = Path.Combine(Path.GetTempPath(), "classicacodex-fingerprints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "article.pdf");
        try
        {
            await File.WriteAllTextAsync(path, "abc");

            var result = await SourceFileFingerprinter.CreateAsync(path);

            Assert.Equal(Path.GetFullPath(path), result.FullPath);
            Assert.Equal("article.pdf", result.FileName);
            Assert.Equal(3, result.FileSize);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", result.Sha256);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FingerprintChangesWhenTheSourceChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "classicacodex-fingerprints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "article.pdf");
        try
        {
            await File.WriteAllTextAsync(path, "first version");
            var first = await SourceFileFingerprinter.CreateAsync(path);
            await File.WriteAllTextAsync(path, "replacement version");
            var replacement = await SourceFileFingerprinter.CreateAsync(path);

            Assert.NotEqual(first.Sha256, replacement.Sha256);
            Assert.NotEqual(first.FileSize, replacement.FileSize);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
