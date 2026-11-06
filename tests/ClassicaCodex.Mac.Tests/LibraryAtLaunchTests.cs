using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// A library that doesn't open at launch: named, with Retry, and the path that
/// both apps remember left alone until the reader has chosen another that opens.
/// </summary>
public sealed class LibraryAtLaunchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-launch-tests", Guid.NewGuid().ToString("N"));
    private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;
    private MainWindow? _window;

    private static string Preference => Path.Combine(PreferencesFolder.Root, "database-path.txt");

    /// <summary>A library on a drive that isn't plugged in: its folder isn't there either.</summary>
    private string OnTheDrive => Path.Combine(_folder, "drive", "library.db");

    public LibraryAtLaunchTests()
    {
        Directory.CreateDirectory(_folder);
        Directory.CreateDirectory(PreferencesFolder.Root);
        Screens.ForgetLibrary();
    }

    public void Dispose()
    {
        foreach (var owned in _window?.OwnedWindows.ToList() ?? new()) owned.Close();
        _window?.Close();

        SqliteConnection.ClearAllPools();
        File.Delete(Preference);
        if (_previousLibrary != null) DbConnectionFactory.Configure(_previousLibrary, remember: false);
        else Screens.ForgetLibrary();
        Directory.Delete(_folder, recursive: true);
    }

    private static void Remember(string path) => File.WriteAllText(Preference, path);

    private static async Task BuildLibraryAt(string path)
    {
        DbConnectionFactory.Configure(path, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();
        SqliteConnection.ClearAllPools();
        Screens.ForgetLibrary();
    }

    private string NotALibrary()
    {
        var path = Path.Combine(_folder, "not-a-library.db");
        File.WriteAllText(path, new string('x', 8192));
        return path;
    }

    private async Task<NoLibraryView> LaunchAsync()
    {
        _window = new MainWindow { Width = 1000, Height = 760, LibraryProblem = await LibraryFile.OpenRememberedAsync() };
        _window.Show();
        await Screens.Settle();
        return _window.GetVisualDescendants().OfType<NoLibraryView>().Single();
    }

    private static List<string?> Words(NoLibraryView page) =>
        page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();

    private static Button ButtonOn(NoLibraryView page, string text) =>
        page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));

    private static async Task RetryAsync(NoLibraryView page)
    {
        ButtonOn(page, "Retry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Screens.Settle();
    }

    [AvaloniaFact]
    public async Task A_library_that_is_not_there_is_named_and_Retry_opens_it_once_it_is()
    {
        Remember(OnTheDrive);
        var page = await LaunchAsync();

        Assert.False(DbConnectionFactory.IsConfigured);
        Assert.True(page.IsVisible);
        var words = Words(page);
        Assert.Contains("Your library isn't available", words);
        Assert.Contains(OnTheDrive, words);
        Assert.Contains(words, w => w!.Contains("this app will look for the one you choose from now on"));
        Assert.DoesNotContain("Choose your library", words);
        Assert.True(ButtonOn(page, "Retry").IsEffectivelyVisible);
        Screens.Save(_window!, "library-not-available");

        // Not yet: the page stays and says it tried, and nothing is written over.
        await RetryAsync(page);
        Assert.True(page.IsVisible);
        Assert.Contains("Tried again, with the same result.", Words(page));
        Assert.False(DbConnectionFactory.IsConfigured);
        Assert.Equal(OnTheDrive, File.ReadAllText(Preference));

        await BuildLibraryAt(OnTheDrive);
        await RetryAsync(page);
        Assert.False(page.IsVisible);
        Assert.Equal(OnTheDrive, DbConnectionFactory.DatabasePath);
        Assert.Equal(OnTheDrive, File.ReadAllText(Preference));
    }

    [AvaloniaFact]
    public async Task A_first_run_gets_the_general_page_and_nothing_to_retry()
    {
        var page = await LaunchAsync();

        Assert.Null(_window!.LibraryProblem);
        Assert.Contains("Choose your library", Words(page));
        Assert.False(ButtonOn(page, "Retry").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_default_library_having_gone_is_a_first_run_too()
    {
        Remember(DbConnectionFactory.DefaultDatabasePath.ToUpperInvariant());

        Assert.Null(await LibraryFile.OpenRememberedAsync());
    }

    [AvaloniaFact]
    public async Task A_library_that_will_not_open_is_reported_and_Retry_can_open_it_later()
    {
        var path = NotALibrary();
        Remember(path);
        var page = await LaunchAsync();

        Assert.True(page.IsVisible);
        var failure = Assert.IsType<string>(_window!.LibraryProblem?.Failure);
        var words = Words(page);
        Assert.Contains("Your library couldn't be opened", words);
        Assert.Contains(path, words);
        Assert.Contains(failure, words);
        Assert.Contains("opening the library at launch", await File.ReadAllTextAsync(CrashLog.LogFile));
        Screens.Save(_window, "library-will-not-open");

        await RetryAsync(page);
        Assert.True(page.IsVisible);
        Assert.Contains("Tried again, with the same result.", Words(page));

        SqliteConnection.ClearAllPools();
        File.Delete(path);
        await BuildLibraryAt(path);
        await RetryAsync(page);
        Assert.False(page.IsVisible);
    }

    [AvaloniaFact]
    public async Task Retry_that_finds_a_different_problem_says_that_one()
    {
        Remember(OnTheDrive);
        var page = await LaunchAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(OnTheDrive)!);
        File.WriteAllText(OnTheDrive, new string('x', 8192));
        await RetryAsync(page);

        var words = Words(page);
        Assert.Contains("Your library couldn't be opened", words);
        Assert.DoesNotContain("Tried again, with the same result.", words);
    }

    [AvaloniaFact]
    public async Task A_file_that_will_not_open_does_not_replace_the_remembered_library()
    {
        Remember(OnTheDrive);
        var page = await LaunchAsync();

        var switching = _window!.SwitchLibraryAsync(NotALibrary(), isNew: false);
        await Screens.Settle();
        Assert.Single(_window.OwnedWindows).Close();

        Assert.False(await switching);
        Assert.True(page.IsVisible);
        Assert.Equal(OnTheDrive, File.ReadAllText(Preference));
    }

    [AvaloniaFact]
    public async Task Another_library_that_opens_takes_over_from_the_page()
    {
        Remember(OnTheDrive);
        var page = await LaunchAsync();

        var another = Path.Combine(_folder, "another.db");
        Assert.True(await _window!.SwitchLibraryAsync(another, isNew: true));
        await Screens.Settle();

        Assert.False(page.IsVisible);
        Assert.Null(_window.LibraryProblem);
        Assert.Equal(another, File.ReadAllText(Preference));
    }
}
