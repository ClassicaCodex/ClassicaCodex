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
/// Go to Citation, end to end: a citation copied from one of the pages, or a
/// footnote, is on the clipboard; Go to Citation fills it in and shows the
/// lines it names; and Open puts the reader on each line. On a library of its
/// own - lines of the Theogony stored as Perseus stores them, with the
/// edition's URN in front of each number, and the start of the Iliad - so it
/// runs everywhere.
/// </summary>
public class GoToCitationTests
{
    private const string Theogony = "tlg0020.tlg001.perseus-grc2";
    private const string Line457 = "Ζῆνά τε μητιόεντα, θεῶν πατέρʼ ἠδὲ καὶ ἀνδρῶν,";
    private const string Iliad1 = "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος";

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
                    INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES
                        (1, 'urn:cts:greekLit:tlg0020', 'Hesiod', 'greekLit'),
                        (2, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                        (1, 1, 'urn:cts:greekLit:tlg0020.tlg001', 'Theogony'),
                        (2, 2, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
                    INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                        (1, 1, '{Theogony}', 'Original', 'grc', 'perseus-greek'),
                        (2, 2, 'tlg0012.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek');
                    INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text, IsVerse) VALUES
                        (1, 'urn:cts:greekLit:{Theogony}.456', 1, 'ἄλλους μὲν ...', 1),
                        (1, 'urn:cts:greekLit:{Theogony}.457', 2, '{Line457}', 1),
                        (2, '1.1', 3, '{Iliad1}', 1),
                        (2, '1.2', 4, 'οὐλομένην, ἣ μυρίʼ Ἀχαιοῖς ἄλγεʼ ἔθηκε,', 1);";
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
        for (var i = 0; i < 80 && !condition(); i++) await Screens.Settle(1);
        Assert.True(condition());
    }

    private static string? SelectedLine(MainWindow window) =>
        window.GetVisualDescendants().OfType<ReaderPane>().SingleOrDefault(p => p.Role == PaneRole.Original)?.SelectedNode?.Text;

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task A_copied_citation_is_filled_in_shown_and_opened_in_the_reader()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        var copied = await ClipboardText.SetAsync(window, $"{Theogony} 457");
        await GoToCitationFeature.OpenAsync(window);
        await Screens.Settle(4);

        var dialog = window.OwnedWindows.OfType<GoToCitationWindow>().Single();
        if (copied) Assert.Equal($"{Theogony} 457", dialog.Box.Text);
        else dialog.Box.Text = $"{Theogony} 457";

        await Until(() => dialog.Shown is CitationLookupResult);
        Assert.Equal("Hesiod, Theogony 457", dialog.HeadingText);
        Assert.True(dialog.OpenButton.IsEnabled);

        Click(dialog.OpenButton);
        await Until(() => !window.OwnedWindows.OfType<GoToCitationWindow>().Any());
        await Until(() => SelectedLine(window) == Line457);
    }

    [AvaloniaFact]
    public async Task A_citation_the_library_cannot_open_says_why_and_opens_nothing()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        var dialog = new GoToCitationWindow(new CopiedCitationResolver(), _ => Task.CompletedTask);
        dialog.Show(window);
        await Screens.Settle(2);

        dialog.Box.Text = $"{Theogony} 9999";
        await Until(() => dialog.Shown is CitationLookupResult { Outcome: CitationLookupOutcome.ReferenceNotFound });
        Assert.Equal("Hesiod, Theogony", dialog.HeadingText);
        Assert.Contains("No passage of it is numbered 9999", dialog.NoteText);
        Assert.Equal("Open at the Start", dialog.OpenButton.Content);

        dialog.Box.Text = "tlg9999.tlg001.perseus-grc1 1.1";
        await Until(() => dialog.NoteText.Contains("tlg9999"));
        Assert.Contains("Nothing in this library has the identifier tlg9999.tlg001.perseus-grc1", dialog.NoteText);
        Assert.False(dialog.OpenButton.IsEnabled);

        dialog.Box.Text = "CatmusLines – Paris, BnF, lat. 9768 #120078";
        await Until(() => dialog.NoteText.Contains("Medieval Hands"));
        Assert.Contains("choose Paris, BnF, lat. 9768", dialog.NoteText);
        Assert.False(dialog.OpenButton.IsEnabled);
    }

    [AvaloniaFact]
    public async Task A_footnote_is_listed_and_each_reference_opened_in_turn()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        var dialog = new GoToCitationWindow(new CopiedCitationResolver(), result => OpenInReader(window, result));
        dialog.Show(window);
        await Screens.Settle(2);

        dialog.Box.Text = "See Hes. Th. 457; Hom. Il. 1.1; Th. 2.40.1 and Smith 23.";
        await Until(() => dialog.Shown is IReadOnlyList<FootnoteLookup>);

        Assert.True(dialog.References.IsVisible);
        var rows = dialog.References.ItemsSource!.Cast<FootnoteLookup>().ToList();
        Assert.Equal(new[] { "Hes. Th. 457", "Hom. Il. 1.1", "Th. 2.40.1" }, rows.Select(r => r.Written));
        Assert.Equal(CitationLookupOutcome.NotInLibrary, rows[2].Result!.Outcome);
        Assert.Contains("Not read as references: Smith 23", dialog.NoteText);
        Assert.Same(rows[0], dialog.References.SelectedItem);
        Screens.Save(dialog, "go-to-citation-footnote");

        // Open: the first line, and the next reference is chosen.
        Click(dialog.OpenButton);
        await Until(() => SelectedLine(window) == Line457);
        Assert.Same(rows[1], dialog.References.SelectedItem);

        Click(dialog.OpenButton);
        await Until(() => SelectedLine(window) == Iliad1);
        Assert.Contains(dialog, window.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task A_footnote_on_the_clipboard_is_filled_in_and_one_reference_opens_like_a_citation()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        const string Footnote = "As Hesiod puts it (Hes. Th. 457), the father of gods and men.";
        if (!await ClipboardText.SetAsync(window, Footnote)) return; // no clipboard on this machine
        await GoToCitationFeature.OpenAsync(window);

        var dialog = window.OwnedWindows.OfType<GoToCitationWindow>().Single();
        await Until(() => dialog.Box.Text == Footnote);
        await Until(() => dialog.Shown is IReadOnlyList<FootnoteLookup>);

        // One reference shows as a copied citation does, and Open closes the window.
        Assert.False(dialog.References.IsVisible);
        Assert.Equal("Hesiod, Theogony 457", dialog.HeadingText);
        Screens.Save(dialog, "go-to-citation-one-reference");

        Click(dialog.OpenButton);
        await Until(() => !window.OwnedWindows.OfType<GoToCitationWindow>().Any());
        await Until(() => SelectedLine(window) == Line457);
    }

    private static async Task OpenInReader(MainWindow window, object result)
    {
        if (result is CitationLookupResult { Edition: { } edition, TextNodeId: { } passage })
            await window.NavigateToPassageAsync(edition.WorkId, passage);
    }
}
