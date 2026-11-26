using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Quotations;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.QuotedBy;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Quoted By, end to end: the “ mark on a line later authors quote, the
/// window listing them with their wording against the edition's, the jump to
/// a quoter and Back from it, and the underlined words in a quoting passage
/// that lead back to the line.
///
/// On a small library of its own - Iliad 2.557 and three passages that quote
/// it, and a line of the Aeneid with one - and a quotation index made from
/// those texts in place of the shipped one, whose editions no test library
/// holds. So these run everywhere, with or without a sample library.
/// </summary>
public class QuotedByTests
{
    private const string Iliad = "tlg0012.tlg001.perseus-grc2";
    private const string Solon = "tlg0007.tlg007.perseus-grc2";
    private const string Geography = "tlg0099.tlg001.perseus-grc2";
    private const string Scholia = "tlg5026.tlg001.1st1K-grc1";
    private const string Aeneid = "phi0690.phi003.perseus-lat2";
    private const string CityOfGod = "stoa0040.stoa003.opp-lat3";

    private const string Line2556 = "τῷ δ᾽ ἅμα πεντήκοντα μέλαιναι νῆες ἕποντο.";
    private const string Line2557 = "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας,";
    private const string Line2558 = "στῆσε δ᾽ ἄγων ἵν᾽ Ἀθηναίων ἵσταντο φάλαγγες.";
    private const string Solon101 = "ὅμως δ᾽ οὖν ὁ Σόλων λέγεται τὸν Ὅμηρον ἐπαγαγέσθαι· Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας, καὶ τὸ ἑξῆς ἐμβαλεῖν.";
    private const string Solon102 = "τοῖς δ᾽ Ἀθηναίοις αὐτὸς ἐπιφέρει τὸν λόγον.";
    private const string Strabo = "ὁ δὲ ποιητὴς οὕτω φησίν· Αἴας δ᾽ ἐκ Σαλαμῖνος ἦγε δυοκαίδεκα νῆας. τοῦτο δὲ οὐ πάντες δέχονται.";
    private const string Scholion = "Αἴαϲ δ᾽ ἐκ Σαλαμῖνοϲ ἄγεν: ὅτι τὴν Σαλαμῖνα Ἀθηναίοις προσνέμει.";
    private const string Aeneid6726 = "Principio caelum ac terras camposque liquentis";
    private const string Augustine = "Unde et ille ait: principio caelum ac terras camposque liquentis lucentemque globum lunae.";

    private static string Ref(string edition, string reference, string ns = "greekLit") => $"urn:cts:{ns}:{edition}.{reference}";

    private static QuotationLink Link(
        string quotingEdition, string quotingRef, string quotingText, string quoted,
        string sourceEdition, string sourceRef, string sourceText, string matched,
        QuotationFinder finder = QuotationFinder.Testimonia, string? quotingFingerprint = null)
    {
        var q = quotingText.IndexOf(quoted, StringComparison.Ordinal);
        var s = sourceText.IndexOf(matched, StringComparison.Ordinal);
        Assert.True(q >= 0 && s >= 0);
        return new QuotationLink(
            new PassageKey(quotingEdition, quotingRef), quotingFingerprint ?? TextFingerprint.Of(quotingText), q, q + quoted.Length,
            new PassageKey(sourceEdition, sourceRef), TextFingerprint.Of(sourceText), s, s + matched.Length,
            finder, finder == QuotationFinder.Testimonia ? "text" : "both", finder == QuotationFinder.Testimonia ? 3 : 55.5, string.Empty);
    }

    private static QuotationIndex Index() => new(new[]
    {
        Link(Solon, Ref(Solon, "10.1"), Solon101, "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας", Iliad, Ref(Iliad, "2.557"), Line2557, "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας"),
        Link(Geography, Ref(Geography, "9.1.10"), Strabo, "Αἴας δ᾽ ἐκ Σαλαμῖνος ἦγε δυοκαίδεκα νῆας", Iliad, Ref(Iliad, "2.557"), Line2557, "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας"),
        Link(Scholia, Ref(Scholia, "2.557"), Scholion, "Αἴαϲ δ᾽ ἐκ Σαλαμῖνοϲ ἄγεν", Iliad, Ref(Iliad, "2.557"), Line2557, "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν"),
        Link(CityOfGod, Ref(CityOfGod, "7.6.1", "latinLit"), Augustine, "principio caelum ac terras camposque liquentis", Aeneid, "6.726", Aeneid6726, "Principio caelum ac terras camposque liquentis", QuotationFinder.Afterlives),

        // A link found in a copy of Solon 10.2 that reads differently from
        // this library's: its positions do not fit, so it must not underline.
        Link(Solon, Ref(Solon, "10.2"), Solon102, "τοῖς δ᾽ Ἀθηναίοις", Iliad, Ref(Iliad, "2.558"), Line2558, "στῆσε δ᾽ ἄγων", quotingFingerprint: "00000000")
    });

    private sealed class Library : IAsyncDisposable
    {
        private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-quotedby-tests", Guid.NewGuid().ToString("N"));
        private readonly IDisposable _index = QuotationIndex.UseInstead(Index());

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
                        (1, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit'),
                        (2, 'urn:cts:greekLit:tlg0007', 'Plutarch', 'greekLit'),
                        (3, 'urn:cts:greekLit:tlg0099', 'Strabo', 'greekLit'),
                        (4, 'urn:cts:greekLit:tlg5026', 'Scholia in Homerum', 'greekLit'),
                        (5, 'urn:cts:latinLit:phi0690', 'Virgil', 'latinLit'),
                        (6, 'urn:cts:latinLit:stoa0040', 'Augustine, Saint', 'latinLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                        (1, 1, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad'),
                        (2, 2, 'urn:cts:greekLit:tlg0007.tlg007', 'Solon'),
                        (3, 3, 'urn:cts:greekLit:tlg0099.tlg001', 'Geography'),
                        (4, 4, 'urn:cts:greekLit:tlg5026.tlg001', 'Scholia in Iliadem'),
                        (5, 5, 'urn:cts:latinLit:phi0690.phi003', 'Aeneid'),
                        (6, 6, 'urn:cts:latinLit:stoa0040.stoa003', 'De civitate dei');
                    INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                        (1, 1, '{Iliad}', 'Original', 'grc', 'perseus-greek'),
                        (2, 2, '{Solon}', 'Original', 'grc', 'perseus-greek'),
                        (3, 3, '{Geography}', 'Original', 'grc', 'perseus-greek'),
                        (4, 4, '{Scholia}', 'Original', 'grc', 'first1k-greek'),
                        (5, 5, '{Aeneid}', 'Original', 'lat', 'perseus-latin'),
                        (6, 6, '{CityOfGod}', 'Original', 'lat', 'csel');";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES (@Edition, @Ref, @Order, @Text);";
                var order = 0;
                foreach (var (edition, reference, text) in new[]
                {
                    (1, Ref(Iliad, "2.556"), Line2556), (1, Ref(Iliad, "2.557"), Line2557), (1, Ref(Iliad, "2.558"), Line2558),
                    (2, Ref(Solon, "10.1"), Solon101), (2, Ref(Solon, "10.2"), Solon102),
                    (3, Ref(Geography, "9.1.10"), Strabo),
                    (4, Ref(Scholia, "2.557"), Scholion),
                    (5, "6.726", Aeneid6726),
                    (6, Ref(CityOfGod, "7.6.1", "latinLit"), Augustine)
                })
                {
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@Edition", edition);
                    cmd.Parameters.AddWithValue("@Ref", reference);
                    cmd.Parameters.AddWithValue("@Order", order++);
                    cmd.Parameters.AddWithValue("@Text", text);
                    await cmd.ExecuteNonQueryAsync();
                }
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
            _index.Dispose();
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

    private static ReaderPane Original(MainWindow window) =>
        window.GetVisualDescendants().OfType<ReaderPane>().Single(p => p.Role == PaneRole.Original);

    private static async Task<ReaderPane> OpenAsync(MainWindow window, string title)
    {
        Assert.True(await window.OpenWorkAsync(window.LibraryWorks.Single(w => w.Title == title).WorkId));
        await Screens.Settle(8);
        return Original(window);
    }

    private static int RowOf(ReaderPane pane, string reference)
    {
        for (var i = 0; i < pane.Count; i++)
            if (PassageCitation.Display(pane.NodeAt(i)!.CitationRef) == reference) return i;
        throw new InvalidOperationException($"no row {reference}");
    }

    /// <summary>The block a row's passage is drawn in: the one whose runs hold the passage's text.</summary>
    private static TextBlock PassageBlock(ReaderPane pane, int row)
    {
        pane.EnsureVisible(row);
        var container = pane.List.ContainerFromIndex(row)!;
        var text = pane.NodeAt(row)!.Text;
        return container.GetVisualDescendants().OfType<TextBlock>().First(b => SteadyLines.TextOf(b).StartsWith(text, StringComparison.Ordinal));
    }

    private static async Task<List<MenuItem>> OpenMenuAsync(ReaderPane pane, int row)
    {
        pane.SelectOnly(row);
        pane.List.RaiseEvent(new ContextRequestedEventArgs());
        await Screens.Settle(2);
        return pane.List.ContextMenu!.Items.OfType<MenuItem>().ToList();
    }

    private static async Task CloseMenuAsync(ReaderPane pane)
    {
        pane.List.ContextMenu!.Close();
        await Screens.Settle(2);
    }

    private static MenuItem Entry(List<MenuItem> items, string header) => items.Single(i => i.Header as string == header);

    private static async Task BackAsync(MainWindow window)
    {
        var back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BackButton");
        Assert.True(back.IsEnabled);
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Screens.Settle(8);
    }

    [AvaloniaFact]
    public async Task A_quoted_line_is_marked_and_lists_its_quoters_earliest_first()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();
        var pane = await OpenAsync(window, "Iliad");

        var quoted = RowOf(pane, "2.557");
        Assert.True(pane.MarksAt(quoted).HasFlag(PassageMarks.QuotedBy));
        Assert.False(pane.MarksAt(RowOf(pane, "2.556")).HasFlag(PassageMarks.QuotedBy));
        Assert.EndsWith(PassageMarkSymbols.QuotedBy, SteadyLines.TextOf(PassageBlock(pane, quoted)));
        Screens.Save(window, "quoted-by-1-mark");

        // Offered on the quoted line, and on no other.
        var items = await OpenMenuAsync(pane, RowOf(pane, "2.556"));
        Assert.False(Entry(items, "Quoted By…").IsVisible);
        await CloseMenuAsync(pane);

        items = await OpenMenuAsync(pane, quoted);
        Assert.True(Entry(items, "Quoted By…").IsVisible);
        Assert.False(Entry(items, "Go to Quoted Passage").IsVisible);
        Entry(items, "Quoted By…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await CloseMenuAsync(pane);
        await Screens.Settle(4);

        var quotedBy = window.OwnedWindows.OfType<QuotedByWindow>().Single();
        await quotedBy.LoadTask!;
        await Screens.Settle(4);

        // Strabo, then Plutarch, by their dates; the scholia have none and come last.
        Assert.Equal(new[] { "Strabo, Geography 9.1.10", "Plutarch, Solon 10.1", "Scholia in Homerum, Scholia in Iliadem 2.557" },
            quotedBy.Items.Select(i => i.Heading));
        Assert.Equal("64 BCE–24 CE", quotedBy.Items[0].Era);
        Assert.Null(quotedBy.Items[2].Era);

        // Strabo prints ἦγε for Homer's ἄγεν; Plutarch quotes word for word;
        // the scholiast's lunate sigma is the house's, not a variant.
        var strabo = quotedBy.Items[0].Wording!;
        Assert.False(strabo.SameWords);
        Assert.Contains(strabo.Quoting, p => p.Differs && p.Text == "ἦγε");
        Assert.Contains(strabo.Source, p => p.Differs && p.Text == "ἄγεν");
        Assert.True(quotedBy.Items[1].Wording!.SameWords);
        Assert.True(quotedBy.Items[2].Wording!.SameWords);
        Assert.EndsWith("ἐπαγαγέσθαι· ", quotedBy.Items[1].Before, StringComparison.Ordinal);

        // The window says how far to trust what it lists, and what found each.
        var shown = string.Join("\n", quotedBy.GetVisualDescendants().OfType<TextBlock>().Select(SteadyLines.TextOf));
        Assert.Contains("37 were genuine", shown);
        Assert.Contains("judged 35 genuine", shown);
        Assert.Contains("Testimonia: the author is named beside a citing word", shown);
        Screens.Save(quotedBy, "quoted-by-2-window");

        // To Strabo, and Back to the line he quotes.
        await quotedBy.GoToAsync(quotedBy.Items[0]);
        await Screens.Settle(8);
        Assert.Equal("Geography", window.OpenWork?.Title);
        Assert.Equal(Strabo, Original(window).SelectedNode?.Text);

        await BackAsync(window);
        Assert.Equal("Iliad", window.OpenWork?.Title);
        Assert.Equal(Line2557, Original(window).SelectedNode?.Text);
    }

    [AvaloniaFact]
    public async Task Quoted_words_are_underlined_and_lead_back_to_the_line()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();
        var pane = await OpenAsync(window, "Solon");

        var quoting = RowOf(pane, "10.1");
        var span = Assert.Single(pane.QuotationsAt(quoting));
        Assert.Equal("Homer, Iliad 2.557", span.Label);

        // A link made against a different copy of 10.2 does not fit this one.
        Assert.Empty(pane.QuotationsAt(RowOf(pane, "10.2")));

        var block = PassageBlock(pane, quoting);
        var underlined = block.Inlines!.OfType<Run>().Single(r => r.TextDecorations is { Count: > 0 });
        Assert.Equal("Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας", underlined.Text);
        Screens.Save(window, "quoted-by-3-underline");

        var items = await OpenMenuAsync(pane, quoting);
        Assert.False(Entry(items, "Quoted By…").IsVisible);
        var quotes = Entry(items, "Go to Quoted Passage");
        Assert.True(quotes.IsVisible);
        Assert.Equal(new[] { "Homer, Iliad 2.557" }, quotes.Items.OfType<MenuItem>().Select(i => i.Header as string));
        await CloseMenuAsync(pane);

        // A press on the underlined words, where a reader's pointer would land.
        var hit = block.TextLayout.HitTestTextPosition(span.Start + 3);
        var point = block.TranslatePoint(hit.Center, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await Screens.Settle(10);

        Assert.Equal("Iliad", window.OpenWork?.Title);
        Assert.Equal(Line2557, Original(window).SelectedNode?.Text);

        await BackAsync(window);
        Assert.Equal("Solon", window.OpenWork?.Title);
        Assert.Equal(Solon101, Original(window).SelectedNode?.Text);
    }

    /// <summary>
    /// The Latin side, from Afterlives: said to be Afterlives', with its
    /// rating and score, and the same link back.
    /// </summary>
    [AvaloniaFact]
    public async Task An_Afterlives_quotation_says_what_found_it()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();
        var pane = await OpenAsync(window, "Aeneid");

        var line = RowOf(pane, "6.726");
        Assert.True(pane.MarksAt(line).HasFlag(PassageMarks.QuotedBy));

        var items = await OpenMenuAsync(pane, line);
        Entry(items, "Quoted By…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await CloseMenuAsync(pane);
        await Screens.Settle(4);

        var quotedBy = window.OwnedWindows.OfType<QuotedByWindow>().Single();
        await quotedBy.LoadTask!;
        var augustine = Assert.Single(quotedBy.Items);
        Assert.Equal("Augustine, Saint, De civitate dei 7.6.1", augustine.Heading);
        Assert.Equal("354–430 CE", augustine.Era);
        Assert.True(augustine.Wording!.SameWords);
        Assert.Equal("Afterlives: rated high, score 55.5, found by both its searches.", QuotedByWindow.Provenance(augustine.Links[0]));

        // And from Augustine, back to Virgil through the pane's own path.
        quotedBy.Close();
        pane = await OpenAsync(window, "De civitate dei");
        var quoting = RowOf(pane, "7.6.1");
        pane.SelectOnly(quoting);
        Assert.True(pane.ActivateQuotation(quoting, pane.QuotationsAt(quoting)[0].Start));
        await Screens.Settle(10);
        Assert.Equal("Aeneid", window.OpenWork?.Title);
        Assert.Equal(Aeneid6726, Original(window).SelectedNode?.Text);
    }
}
