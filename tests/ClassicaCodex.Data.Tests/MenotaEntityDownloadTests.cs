using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Fetching menota-entities.txt from a menota.org that does not always answer.
///
/// On 9 November 2026 four requests in ten to menota.org hung or were reset,
/// and on the iPad the step's first request hung until .NET gave up after 100
/// seconds. Setup took that for a cancel, said "Stopped." and nothing else, and
/// the reader had no way to know the site was the problem.
/// </summary>
public class MenotaEntityDownloadTests : IDisposable
{
    private const string GoodTable = "<!ENTITY thorn \"þ\">\n<!ENTITY eth \"ð\">";

    private readonly string _folder;
    private readonly List<string> _asked = new();
    private readonly List<string> _said = new();

    public MenotaEntityDownloadTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "cc-menota-entities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    private sealed class Said(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    /// <summary>
    /// A downloader that behaves as each answer says, one call at a time:
    /// "hang" waits until the try is cut short, "reset" fails, "html" sends an
    /// error page, and anything else is written as the file.
    /// </summary>
    private MenotaDownloadService Answering(params string[] answers) => new()
    {
        EntitiesAttemptTimeout = TimeSpan.FromMilliseconds(200),
        RetryPause = TimeSpan.Zero,
        Download = async (url, destination, _, token) =>
        {
            var answer = answers[Math.Min(_asked.Count, answers.Length - 1)];
            _asked.Add(url);

            switch (answer)
            {
                case "hang":
                    await Task.Delay(Timeout.Infinite, token);
                    break;
                case "reset":
                    throw new HttpRequestException("Connection reset by peer");
                case "html":
                    await File.WriteAllTextAsync(destination, "<html><body>Service unavailable</body></html>", token);
                    break;
                default:
                    await File.WriteAllTextAsync(destination, answer, token);
                    break;
            }
        }
    };

    private string Saved => Path.Combine(_folder, MenotaDownloadService.EntitiesFileName);

    private Task FetchAsync(MenotaDownloadService service, CancellationToken token = default) =>
        service.DownloadEntitiesAsync(_folder, new Said(_said), new Said(_said), token);

    [Fact]
    public async Task A_request_that_hangs_is_cut_short_and_tried_at_menotas_other_name()
    {
        await FetchAsync(Answering("hang", "reset", "html", GoodTable));

        Assert.Equal(GoodTable, File.ReadAllText(Saved));
        Assert.Equal(4, _asked.Count);
        Assert.Equal(MenotaDownloadService.EntitiesUrls[0], _asked[0]);
        Assert.Equal(MenotaDownloadService.EntitiesUrls[1], _asked[1]);
        Assert.Contains(_said, line => line.Contains("did not answer within"));
        Assert.Contains(_said, line => line.Contains("Trying again (2 of 5)"));
        Assert.False(Directory.Exists(Path.Combine(_folder, "entities-download")));
    }

    [Fact]
    public async Task A_good_copy_already_in_the_folder_is_used_when_menota_never_answers()
    {
        File.WriteAllText(Saved, GoodTable);

        await FetchAsync(Answering("hang"));

        Assert.Equal(5, _asked.Count);
        Assert.Equal(GoodTable, File.ReadAllText(Saved));
        Assert.Contains("already in the folder", _said[^1]);
    }

    [Fact]
    public async Task Without_one_the_step_stops_with_an_error_that_names_menota()
    {
        File.WriteAllText(Saved, "<html>saved from a browser</html>");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => FetchAsync(Answering("reset")));

        Assert.Contains("menota.org", error.Message);
        Assert.Contains("try again", error.Message);

        // The page someone saved by hand is not replaced with another.
        Assert.Equal("<html>saved from a browser</html>", File.ReadAllText(Saved));
    }

    [Fact]
    public async Task The_readers_own_cancel_stops_at_once()
    {
        using var cancel = new CancellationTokenSource();
        var hanging = Answering("hang");
        var service = new MenotaDownloadService
        {
            EntitiesAttemptTimeout = TimeSpan.FromMinutes(1),
            RetryPause = TimeSpan.Zero,
            Download = async (url, destination, progress, token) =>
            {
                cancel.Cancel();
                await hanging.Download(url, destination, progress, token);
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FetchAsync(service, cancel.Token));

        Assert.Single(_asked);
    }
}
