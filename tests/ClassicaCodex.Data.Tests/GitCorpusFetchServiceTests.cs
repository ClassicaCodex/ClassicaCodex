using System.Text;
using ClassicaCodex.Ingestion;
using LibGit2Sharp;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Fetching a repository over a folder that holds an earlier fetch of it.
///
/// The repository is built in the test, commit by commit, straight into git's
/// object store - no working tree, so a rename that differs only in case is
/// the same on every file system - and cloned from its path, so nothing is
/// downloaded.
/// </summary>
public class GitCorpusFetchServiceTests
{
    private static string Scratch() =>
        Path.Combine(Path.GetTempPath(), "ccx-git-fetch-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Makes the repository's tip hold these files and nothing else.</summary>
    private static void Commit(string repository, params (string Path, string Text)[] files)
    {
        if (!Directory.Exists(repository)) Repository.Init(repository, isBare: true);
        using var repo = new Repository(repository);

        var tree = new TreeDefinition();
        foreach (var (path, text) in files)
        {
            var blob = repo.ObjectDatabase.CreateBlob(new MemoryStream(Encoding.UTF8.GetBytes(text)));
            tree.Add(path, blob, Mode.NonExecutableFile);
        }

        var signature = new Signature("Test", "test@example.com", DateTimeOffset.UnixEpoch);
        var parents = repo.Head.Tip == null ? Array.Empty<LibGit2Sharp.Commit>() : new[] { repo.Head.Tip };
        var commit = repo.ObjectDatabase.CreateCommit(
            signature, signature, "test", repo.ObjectDatabase.CreateTree(tree), parents, prettifyMessage: false);

        repo.Refs.Add("refs/heads/main", commit.Id, allowOverwrite: true);
        repo.Refs.UpdateTarget(repo.Refs.Head, "refs/heads/main");
    }

    private static Task FetchAsync(string repository, string destination, params string[] mirrored) =>
        new GitCorpusFetchService { FileExtensions = new[] { ".json" }, MirroredFolders = mirrored }
            .FetchAsync(repository, destination);

    private static string[] Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// What ai-translations does when a file is fixed by renaming it: the old
    /// name goes from a mirrored folder, and stays everywhere else - in a
    /// folder that is not mirrored, and in every clone that asks for no
    /// mirroring, which is every TEI corpus.
    /// </summary>
    [Fact]
    public async Task AMirroredFolderLosesWhatTheRepositoryNoLongerHasAndNothingElseDoes()
    {
        var root = Scratch();
        var repository = Path.Combine(root, "repo.git");
        var mirrored = Path.Combine(root, "mirrored");
        var plain = Path.Combine(root, "plain");

        Commit(repository,
            ("README.md", "readme"),
            ("pending/held-back.json", "{}"),
            ("translations/kept.json", "{\"v\":1}"),
            ("translations/menota_anonymous_untitled-section-1.json", "{}"),
            ("translations/sub/moved.json", "{}"));

        await FetchAsync(repository, mirrored, "translations");
        await FetchAsync(repository, plain);

        // Put there by the reader, not the clone, and not a kind it writes.
        File.WriteAllText(Path.Combine(mirrored, "translations", "notes.txt"), "mine");

        Commit(repository,
            ("README.md", "readme"),
            ("translations/kept.json", "{\"v\":2}"),
            ("translations/menota_anonymous_untitled-section-105.json", "{}"),
            ("translations/sub/renamed.json", "{}"));

        await FetchAsync(repository, mirrored, "translations");
        await FetchAsync(repository, plain);

        Assert.Equal(
            new[]
            {
                "pending/held-back.json",
                "translations/kept.json",
                "translations/menota_anonymous_untitled-section-105.json",
                "translations/notes.txt",
                "translations/sub/renamed.json"
            },
            Files(mirrored));
        Assert.Equal("{\"v\":2}", File.ReadAllText(Path.Combine(mirrored, "translations", "kept.json")));

        Assert.Equal(
            new[]
            {
                "pending/held-back.json",
                "translations/kept.json",
                "translations/menota_anonymous_untitled-section-1.json",
                "translations/menota_anonymous_untitled-section-105.json",
                "translations/sub/moved.json",
                "translations/sub/renamed.json"
            },
            Files(plain));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// Renamed upstream only in its case, a file is written over the old one,
    /// which on Windows and macOS keeps the old name's case on disk. Compared
    /// by its exact name, the file just written would look like a leftover
    /// and be deleted.
    /// </summary>
    [Fact]
    public async Task AFileRenamedOnlyInCaseIsNotTakenForALeftover()
    {
        var root = Scratch();
        var repository = Path.Combine(root, "repo.git");
        var destination = Path.Combine(root, "clone");

        Commit(repository, ("translations/Seneca.json", "{\"v\":1}"));
        await FetchAsync(repository, destination, "translations");

        Commit(repository, ("translations/seneca.json", "{\"v\":2}"));
        await FetchAsync(repository, destination, "translations");

        Assert.Equal("{\"v\":2}", File.ReadAllText(Path.Combine(destination, "translations", "seneca.json")));

        Directory.Delete(root, recursive: true);
    }

    /// <summary>A folder outside the clone's own is refused before anything is fetched, not emptied.</summary>
    [Theory]
    [InlineData("..")]
    [InlineData("translations/../..")]
    public async Task AMirroredFolderOutsideTheCloneIsRefused(string folder)
    {
        var root = Scratch();
        var repository = Path.Combine(root, "repo.git");
        var destination = Path.Combine(root, "clone");
        Commit(repository, ("translations/a.json", "{}"));
        File.WriteAllText(Path.Combine(root, "beside.json"), "{}");

        await Assert.ThrowsAsync<ArgumentException>(() => FetchAsync(repository, destination, folder));
        Assert.True(File.Exists(Path.Combine(root, "beside.json")));
        Assert.False(Directory.Exists(destination));

        Directory.Delete(root, recursive: true);
    }
}
