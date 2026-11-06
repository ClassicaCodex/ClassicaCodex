using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What an iPad fetches in place of a clone. The archive here is built in the
/// test to the shape GitHub gives it - one top folder named for the repository
/// and the commit, after a global header - and handed over by a fake handler, so
/// nothing is downloaded.
/// </summary>
public sealed class GitHubArchiveFetchTests : IDisposable
{
    private const string Repository = "https://github.com/PerseusDL/lexica";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccx-archive-tests", Guid.NewGuid().ToString("N"));

    private string Destination => Path.Combine(_root, "destination");

    private static byte[] Archive(params (string Path, string Content)[] files)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(
                new Dictionary<string, string> { ["comment"] = "0123456789abcdef" }));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "lexica-0123456/"));

            foreach (var (path, content) in files)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, path)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
                });
            }
        }

        return bytes.ToArray();
    }

    private Task FetchAsync(byte[] archive, HttpStatusCode status = HttpStatusCode.OK, params string[] mirroredFolders) =>
        new GitHubArchiveFetchService
        {
            FileExtensions = new[] { ".xml" },
            MirroredFolders = mirroredFolders,
            Handler = new Answering(archive, status)
        }.FetchAsync(Repository, Destination);

    private string Written(params string[] path) => Path.Combine(new[] { Destination }.Concat(path).ToArray());

    [Fact]
    public async Task Files_come_out_where_a_clone_would_put_them()
    {
        await FetchAsync(Archive(
            ("lexica-0123456/CTS_XML_TEI/perseus/lsj.xml", "<lsj/>"),
            ("lexica-0123456/top.XML", "<top/>"),
            ("lexica-0123456/README.md", "not a text")));

        Assert.Equal("<lsj/>", await File.ReadAllTextAsync(Written("CTS_XML_TEI", "perseus", "lsj.xml")));
        Assert.Equal("<top/>", await File.ReadAllTextAsync(Written("top.XML")));
        Assert.False(File.Exists(Written("README.md")));
        Assert.False(Directory.Exists(Written("lexica-0123456")));
    }

    [Fact]
    public async Task A_mirrored_folder_is_left_holding_what_the_repository_holds()
    {
        Directory.CreateDirectory(Written("texts"));
        await File.WriteAllTextAsync(Written("texts", "withdrawn.xml"), "<gone/>");
        await File.WriteAllTextAsync(Written("texts", "notes.txt"), "a reader's own");
        await File.WriteAllTextAsync(Written("outside.xml"), "<kept/>");

        await FetchAsync(Archive(("lexica-0123456/texts/current.xml", "<new/>")), mirroredFolders: "texts");

        Assert.True(File.Exists(Written("texts", "current.xml")));
        Assert.False(File.Exists(Written("texts", "withdrawn.xml")));
        Assert.True(File.Exists(Written("texts", "notes.txt")));
        Assert.True(File.Exists(Written("outside.xml")));
    }

    [Fact]
    public async Task A_download_cut_short_deletes_nothing()
    {
        Directory.CreateDirectory(Written("texts"));
        await File.WriteAllTextAsync(Written("texts", "withdrawn.xml"), "<still here/>");

        var whole = Archive(("lexica-0123456/texts/current.xml", new string('x', 200_000)));
        var cut = whole[..(whole.Length / 2)];

        await Assert.ThrowsAnyAsync<Exception>(() => FetchAsync(cut, mirroredFolders: "texts"));

        Assert.True(File.Exists(Written("texts", "withdrawn.xml")));
    }

    [Fact]
    public async Task A_refused_download_is_an_error_rather_than_an_empty_folder()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(Array.Empty<byte>(), HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task An_archive_cannot_write_outside_the_folder()
    {
        await FetchAsync(Archive(
            ("lexica-0123456/../../climbed.xml", "<out/>"),
            ("../climbed.xml", "<out/>"),
            ("lexica-0123456/fine.xml", "<in/>")));

        Assert.True(File.Exists(Written("fine.xml")));
        Assert.Empty(Directory.GetFiles(_root, "climbed.xml", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_link_in_the_archive_is_not_followed_or_written()
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "lexica-0123456/link.xml") { LinkName = "/etc/hosts" });
        }

        await FetchAsync(bytes.ToArray());

        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Theory]
    [InlineData("https://github.com/PerseusDL/lexica", "https://codeload.github.com/PerseusDL/lexica/tar.gz/HEAD")]
    [InlineData("https://github.com/PerseusDL/lexica/", "https://codeload.github.com/PerseusDL/lexica/tar.gz/HEAD")]
    [InlineData("https://github.com/PerseusDL/lexica.git", "https://codeload.github.com/PerseusDL/lexica/tar.gz/HEAD")]
    public void A_repository_address_becomes_its_archive(string repository, string archive) =>
        Assert.Equal(archive, GitHubArchiveFetchService.ArchiveUrl(repository));

    [Theory]
    [InlineData("https://gitlab.com/someone/something")]
    [InlineData("https://github.com/PerseusDL")]
    [InlineData("/a/local/repository.git")]
    public void Anything_but_a_GitHub_repository_is_refused(string repository) =>
        Assert.Throws<ArgumentException>(() => GitHubArchiveFetchService.ArchiveUrl(repository));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Answering(byte[] body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
