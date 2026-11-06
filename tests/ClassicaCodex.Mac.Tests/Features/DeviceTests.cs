using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The two places the shared code hands over to the iPad app. Neither the
/// process it replaces nor the system it reaches exists on the other's machine,
/// so these check the handing over, and the fakes keep the real keychain out of it.
/// </summary>
public class DeviceTests
{
    [Fact]
    public void Links_and_folders_go_to_the_device_when_no_process_can_be_started()
    {
        var opened = new List<string>();
        MacShell.OpenInstead = target =>
        {
            opened.Add(target);
            return true;
        };

        try
        {
            Assert.True(MacShell.Open("https://console.anthropic.com"));

            // A device cannot select a file in a folder, so it shows the folder.
            var file = Path.Combine(Path.GetTempPath(), "library", "classicacodex.db");
            Assert.True(MacShell.Reveal(file));

            Assert.Equal(new[] { "https://console.anthropic.com", Path.GetDirectoryName(file)! }, opened);
        }
        finally
        {
            MacShell.OpenInstead = null;
        }
    }

    [Fact]
    public void A_refused_link_is_reported_as_refused()
    {
        MacShell.OpenInstead = _ => false;

        try
        {
            Assert.False(MacShell.Open("x-apple.systempreferences:com.apple.preference.universalaccess"));
        }
        finally
        {
            MacShell.OpenInstead = null;
        }
    }

    [Fact]
    public void Keys_go_to_the_device_keychain_under_the_usual_service_name()
    {
        var store = new FakeStore();
        Keychain.StoreInstead = store;

        try
        {
            Assert.True(Keychain.IsAvailable);
            Assert.True(Platform.TranslationSettings.CanStoreKeys);

            Assert.Null(Keychain.Read("test-account"));
            Assert.True(Keychain.Write("test-account", "the key"));
            Assert.Equal("the key", Keychain.Read("test-account"));
            Assert.Equal(Keychain.Service, store.LastService);

            Assert.True(Keychain.Write("test-account", null));
            Assert.Null(Keychain.Read("test-account"));
        }
        finally
        {
            Keychain.StoreInstead = null;
        }
    }

    [Fact]
    public void Every_step_that_clones_can_be_fetched_without_git()
    {
        var clones = SetupDataSourceCatalog
            .Build(new AuthorRepository(), new LemmaRepository(), new DefinitionRepository(), new ArtifactRepository(), new EditionRepository())
            .Where(source => source.FetchMode == SetupFetchMode.GitClone)
            .ToList();

        Assert.NotEmpty(clones);
        Assert.All(clones, source => Assert.StartsWith(
            "https://codeload.github.com/", GitHubArchiveFetchService.ArchiveUrl(source.RepoUrl)));
    }

    [Theory]
    [InlineData("Show in Finder", "Show in Files")]
    [InlineData("Saved. The keys are in your login keychain, under “Classica Codex” in Keychain Access.", "Saved. The keys are in this iPad's keychain.")]
    [InlineData("Follow macOS", "Follow System")]
    [InlineData("usually about 9 gigabytes, and two to three hours on a decent connection with everything selected - mostly unattended, and the Greek word-form data is about an hour of that on its own.",
        "usually about 9 gigabytes, and about half an hour for the main Greek and Latin collections on a good connection. Keep Classica Codex on screen while a step runs: iOS pauses an app that is switched away from, and the step would then have to start again.")]
    public void An_iPad_says_what_is_true_of_an_iPad(string mac, string device) =>
        Assert.Equal(device, SystemWords.ForDevice(mac));

    private sealed class FakeStore : ISecretStore
    {
        private readonly Dictionary<string, string> _items = new();

        public string? LastService { get; private set; }

        public string? Read(string service, string account)
        {
            LastService = service;
            return _items.GetValueOrDefault(account);
        }

        public bool Write(string service, string account, string? secret)
        {
            LastService = service;
            if (string.IsNullOrEmpty(secret)) _items.Remove(account);
            else _items[account] = secret;
            return true;
        }
    }
}
