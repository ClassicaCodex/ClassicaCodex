using System.Text;
using ClassicaCodex.Ingestion;
using LibGit2Sharp;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A re-run of a setup step's clone leaving a mirrored folder as the
/// repository now has it.
///
/// <b>What happened.</b> A fetch extracts the new tree on top of the folder
/// the last one filled, and deleted nothing. The AI translations step promises
/// that running it again brings in corrections, and a translation withdrawn or
/// renamed upstream stayed in the folder, to be installed again on every run -
/// or, where it was the file being fixed, to fail on every run and keep the
/// step's tick from ever being earned.
///
/// The repository here is a bare one made in the test, its commits written
/// straight into the object database so that a rename differing only in case
/// is a real one whatever the disk; it is cloned by path, so nothing is
/// downloaded. libgit2's local transport may refuse the shallow clone, and the
/// full-clone fallback then fetches the same tree.
/// </summary>
public sealed class GitCorpusFetchMirrorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccx-git-mirror-tests", Guid.NewGuid().ToString("N"));

    private string Remote => Path.Combine(_root, "remote.git");
    private string Destination => Path.Combine(_root, "destination");

    /// <summary>Makes the repository hold exactly these files, as its next commit.</summary>
    private void Commit(params (string Path, string Content)[] files)
    {
        if (!Directory.Exists(Remote)) Repository.Init(Remote, isBare: true);
        using var repo = new Repository(Remote);

        var tree = new TreeDefinition();
        foreach (var (path, content) in files)
        {
            var blob = repo.ObjectDatabase.CreateBlob(new MemoryStream(Encoding.UTF8.GetBytes(content)));
            tree.Add(path, blob, Mode.NonExecutableFile);
        }

        var who = new Signature("Test", "test@example.com", DateTimeOffset.UnixEpoch);
        var parents = repo.Head.Tip == null ? Array.Empty<Commit>() : new[] { repo.Head.Tip };
        var commit = repo.ObjectDatabase.CreateCommit(
            who, who, "next", repo.ObjectDatabase.CreateTree(tree), parents, prettifyMessage: false);

        // HEAD names its branch before the branch exists.
        repo.Refs.Add(repo.Refs.Head.TargetIdentifier, commit.Id, allowOverwrite: true);
    }

    private Task FetchAsync(params string[] mirroredFolders) => new GitCorpusFetchService
    {
        FileExtensions = new[] { ".json" },
        MirroredFolders = mirroredFolders
    }.FetchAsync(Remote, Destination);

    /// <summary>Every file under the destination, by its path there.</summary>
    private string[] Held() =>
        Directory.GetFiles(Destination, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Destination, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    private string Read(string path) => File.ReadAllText(Path.Combine(Destination, path));

    /// <summary>
    /// A file deleted upstream goes, a renamed one is there under its new name
    /// only, and a corrected one has its correction. Nothing outside the
    /// mirrored folder is touched, and nothing in it the clone did not write:
    /// the reviews folder keeps its withdrawn file, and a reader's own note
    /// stays where they put it.
    /// </summary>
    [Fact]
    public async Task ARerunTakesOutOfAMirroredFolderWhatTheRepositoryNoLongerHolds()
    {
        Commit(
            ("translations/iliad.json", "first"),
            ("translations/menota_anonymous_untitled-section-1.json", "broken"),
            ("translations/voluspa.json", "withdrawn"),
            ("method/reviews/wave-01.json", "review"),
            ("README.md", "not a kind the clone writes"));
        await FetchAsync("translations");
        File.WriteAllText(Path.Combine(Destination, "translations", "notes.txt"), "the reader's own");

        Commit(
            ("translations/iliad.json", "corrected"),
            ("translations/menota_anonymous_untitled-section-1a.json", "fixed"));
        await FetchAsync("translations");

        Assert.Equal(
            new[]
            {
                "method/reviews/wave-01.json",
                "translations/iliad.json",
                "translations/menota_anonymous_untitled-section-1a.json",
                "translations/notes.txt"
            },
            Held());
        Assert.Equal("corrected", Read("translations/iliad.json"));
        Assert.Equal("fixed", Read("translations/menota_anonymous_untitled-section-1a.json"));
    }

    /// <summary>
    /// Every source but the AI translations mirrors nothing, and for them a
    /// re-run still only adds and overwrites, as it always has.
    /// </summary>
    [Fact]
    public async Task WithNoMirroredFolderARerunDeletesNothing()
    {
        Commit(("translations/iliad.json", "first"), ("translations/voluspa.json", "withdrawn"));
        await FetchAsync();

        Commit(("translations/iliad.json", "corrected"));
        await FetchAsync();

        Assert.Equal(new[] { "translations/iliad.json", "translations/voluspa.json" }, Held());
        Assert.Equal("corrected", Read("translations/iliad.json"));
    }

    /// <summary>
    /// A rename that changes only case. On NTFS, and on APFS as macOS formats
    /// it, the new file is written over the old one, which keeps its old name,
    /// so an exact comparison finds that name missing from the tree and
    /// deletes the file it has just written. The text has to survive under
    /// the name the repository now uses.
    /// </summary>
    [Fact]
    public async Task AFileRenamedOnlyInCaseKeepsItsNewText()
    {
        Commit(("translations/Iliad.json", "first"));
        await FetchAsync("translations");

        Commit(("translations/iliad.json", "corrected"));
        await FetchAsync("translations");

        Assert.Equal("corrected", Read("translations/iliad.json"));
    }

    /// <summary>
    /// Nothing is deleted until the new tree is written out in full, so a
    /// fetch that never gets that far leaves the folder as it was, and the
    /// translations already there can still be read.
    /// </summary>
    [Fact]
    public async Task AFetchThatFailsDeletesNothing()
    {
        Directory.CreateDirectory(Path.Combine(Destination, "translations"));
        File.WriteAllText(Path.Combine(Destination, "translations", "iliad.json"), "first");

        // No Commit: there is no repository at Remote to clone.
        await Assert.ThrowsAnyAsync<LibGit2SharpException>(() => FetchAsync("translations"));

        Assert.Equal(new[] { "translations/iliad.json" }, Held());
    }

    /// <summary>
    /// A mirrored folder that is the destination itself, or anywhere outside
    /// it, is refused before anything is downloaded. Advanced Setup lets a
    /// step be pointed at any folder, and tidying a folder of a reader's own
    /// files to match a repository would delete them.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("translations/..")]
    [InlineData("..")]
    [InlineData("../elsewhere")]
    [InlineData("translations/../../elsewhere")]
    public async Task AMirroredFolderOutsideTheDestinationIsRefused(string folder)
    {
        Commit(("translations/iliad.json", "first"));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => FetchAsync(folder));

        Assert.Equal(nameof(GitCorpusFetchService.MirroredFolders), error.ParamName);
        Assert.False(Directory.Exists(Destination));
    }

    [Fact]
    public async Task AnAbsoluteMirroredFolderIsRefused()
    {
        Commit(("translations/iliad.json", "first"));

        await Assert.ThrowsAsync<ArgumentException>(() => FetchAsync(Path.Combine(Destination, "translations")));

        Assert.False(Directory.Exists(Destination));
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(_root)) return;

            // Git's object files are written read-only; clear that first or the delete fails.
            foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder fails no test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
