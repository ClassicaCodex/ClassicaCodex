using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassicaCodex.Core.Quotations;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.GoToCitation;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Go to Citation, end to end: a citation copied from one of the pages is on
/// the clipboard, Go to Citation fills it in and shows the line it names, and
/// Open puts the reader on that line. On a library of its own - two lines of
/// the Theogony stored as Perseus stores them, with the edition's URN in
/// front of each number - so it runs everywhere.
/// </summary>
public class GoToCitationTests
{
    private const string Theogony = "tlg0020.tlg001.perseus-grc2";
    private const string Line457 = "Ζῆνά τε μητιόεντα, θεῶν πατέρʼ ἠδὲ καὶ ἀνδρῶν,";

    private sealed class Library : IAsyncDisposable
    {
        private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-citation-tests", Guid.NewGuid().ToString("N"));
        private readonly IDisposable _quotations = QuotationIndex.UseInstead(new QuotationIndex(Array.Empty<QuotationLink>()));

        public MainWindow Window { get; private set; } = null!;

        public async Task<MainWindow> OpenAsync()
        {
            Directory.CreateDirectory(_folder);
            DbConnectionFactory.Configure(Path.Combine(_folder, "library.db"), remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            await using (var conn = await DbConnectionFactory.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES (1, 'urn:cts:greekLit:tlg0020', 'Hesiod', 'greekLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES (1, 1, 'urn:cts:greekLit:tlg0020.tlg001', 'Theogony');
                    INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES (1, 1, '{Theogony}', 'Original', 'grc', 'perseus-greek');
                    INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text, IsVerse) VALUES
                        (1, 'urn:cts:greekLit:{Theogony}.456', 1, 'ἄλλους μὲν ...', 1),
                        (1, 'urn:cts:greekLit:{Theogony}.457', 2, '{Line457}', 1);";
                await cmd.ExecuteNonQueryAsync();
            }

            Window = new MainWindow { Width = 1280, Height = 800 };
            Window.Show();
            await Screens.Settle();
            return Window;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var owned in Window?.OwnedWindows.ToList() ?? new()) owned.Close();
            Window?.Close();
            _quotations.Dispose();
            SqliteConnection.ClearAllPools();
            if (_previousLibrary != null) DbConnectionFactory.Configure(_previousLibrary, remember: false);
            else Screens.ForgetLibrary();

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open is the temporary folder's to clear.
            }

            return ValueTask.CompletedTask;
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++) await Screens.Settle(1);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task A_copied_citation_is_filled_in_shown_and_opened_in_the_reader()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        var copied = await ClipboardText.SetAsync(window, $"{Theogony} 457");
        var going = GoToCitationFeature.OpenAsync(window);
        await Screens.Settle(4);

        var dialog = window.OwnedWindows.OfType<GoToCitationWindow>().Single();
        if (copied) Assert.Equal($"{Theogony} 457", dialog.Box.Text);
        else dialog.Box.Text = $"{Theogony} 457";

        await Until(() => dialog.Shown is CitationLookupResult);
        Assert.Equal("Hesiod, Theogony 457", dialog.HeadingText);
        Assert.True(dialog.OpenButton.IsEnabled);

        dialog.OpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => going.IsCompleted);
        await going;
        await Screens.Settle(8);

        var pane = window.GetVisualDescendants().OfType<ReaderPane>().Single(p => p.Role == PaneRole.Original);
        Assert.Equal(Line457, pane.SelectedNode!.Text);
    }

    [AvaloniaFact]
    public async Task A_citation_the_library_cannot_open_says_why_and_opens_nothing()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        var dialog = new GoToCitationWindow(new CopiedCitationResolver());
        dialog.Show(window);
        await Screens.Settle(2);

        dialog.Box.Text = $"{Theogony} 9999";
        await Until(() => dialog.Shown is CitationLookupResult { Outcome: CitationLookupOutcome.ReferenceNotFound });
        Assert.Equal("Hesiod, Theogony", dialog.HeadingText);
        Assert.Contains("No passage of it is numbered 9999", dialog.NoteText);
        Assert.Equal("Open at the Start", dialog.OpenButton.Content);

        dialog.Box.Text = "tlg9999.tlg001.perseus-grc1 1.1";
        await Until(() => dialog.Shown is CitationLookupResult { Outcome: CitationLookupOutcome.NotInLibrary });
        Assert.Contains("Nothing in this library has the identifier tlg9999.tlg001.perseus-grc1", dialog.NoteText);
        Assert.False(dialog.OpenButton.IsEnabled);

        dialog.Box.Text = "CatmusLines – Paris, BnF, lat. 9768 #120078";
        await Until(() => dialog.NoteText.Contains("Medieval Hands"));
        Assert.Contains("choose Paris, BnF, lat. 9768", dialog.NoteText);
        Assert.False(dialog.OpenButton.IsEnabled);
    }
}
