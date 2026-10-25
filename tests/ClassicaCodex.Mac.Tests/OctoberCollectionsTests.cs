using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.About;
using ClassicaCodex.Mac.Features.Help;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests;

/// <summary>
/// The reader on the collections that arrived in October, which the Windows
/// reader learned to handle first: a cuneiform tablet held twice over, in its
/// signs and in transliteration, with no translation; tools that would open on
/// nothing for it; and a text whose English is on its source's own site.
///
/// On a small library of their own rather than the sample one, which has no
/// cuneiform in it, so these run everywhere.
/// </summary>
public class OctoberCollectionsTests
{
    private const string Tablet = "http://oracc.org/cmawro/Q002711";

    // AN, EN, KI and LUGAL: four signs from the Cuneiform block, which is
    // outside the Basic Multilingual Plane - written as escapes so that what
    // the test holds is not at the mercy of an editor's font.
    private const string Signs = "\U0001202D\U00012097 \U000121A4 \U00012217";

    private sealed class Library : IAsyncDisposable
    {
        private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-october-tests", Guid.NewGuid().ToString("N"));

        public MainWindow? Window { get; private set; }

        public async Task<MainWindow> OpenAsync()
        {
            Directory.CreateDirectory(_folder);
            DbConnectionFactory.Configure(Path.Combine(_folder, "library.db"), remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            await using (var conn = await DbConnectionFactory.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                // An Oracc tablet as the setup step installs one: the
                // transliteration first, then the signs, both originals in
                // Akkadian, both pointing at the tablet's page on Oracc. And a
                // Greek work with a translation, one lemma and one apparatus
                // note, whose translation's source is a file and not a page.
                cmd.CommandText = $@"
                    INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES
                        (1, 'urn:oracc:cmawro', 'Anonymous', 'oracc'),
                        (2, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                        (1, 1, 'oracc:cmawro/maqlu:Q002711', 'Maqlû VII'),
                        (2, 2, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
                    INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Collection) VALUES
                        (1, 1, 'oracc:cmawro/maqlu:Q002711:transliteration', 'Original', 'akk', NULL, '{Tablet}', 'oracc'),
                        (2, 1, 'oracc:cmawro/maqlu:Q002711:cuneiform', 'Original', 'akk', NULL, '{Tablet}', 'oracc'),
                        (3, 2, 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2', 'Original', 'grc', NULL, '/Users/reader/ClassicaCodexData/greek-texts/tlg0012.tlg001.perseus-grc2.xml', 'perseus-greek'),
                        (4, 2, 'urn:cts:greekLit:tlg0012.tlg001.perseus-eng3', 'Translation', 'eng', 'Samuel Butler', 'file:///Users/reader/ClassicaCodexData/greek-texts/tlg0012.tlg001.perseus-eng3.xml', 'perseus-greek');
                    INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES
                        (1, 'oracc:cmawro/maqlu:Q002711:transliteration:1', 1, 'en-lil₂ šar₂ mātāti'),
                        (1, 'oracc:cmawro/maqlu:Q002711:transliteration:2', 2, 'ēpušanni ēpušakki'),
                        (1, 'oracc:cmawro/maqlu:Q002711:transliteration:3', 3, 'kīma kibrī'),
                        (2, 'oracc:cmawro/maqlu:Q002711:cuneiform:1', 1, '{Signs}'),
                        (2, 'oracc:cmawro/maqlu:Q002711:cuneiform:2', 2, '{Signs}'),
                        (2, 'oracc:cmawro/maqlu:Q002711:cuneiform:3', 3, '{Signs}'),
                        (3, 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2:1.1', 1, 'μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος'),
                        (3, 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2:1.2', 2, 'οὐλομένην, ἣ μυρί᾽ Ἀχαιοῖς ἄλγε᾽ ἔθηκε,'),
                        (4, 'urn:cts:greekLit:tlg0012.tlg001.perseus-eng3:1.1', 1, 'Sing, O goddess, the anger of Achilles son of Peleus,'),
                        (4, 'urn:cts:greekLit:tlg0012.tlg001.perseus-eng3:1.2', 2, 'that brought countless ills upon the Achaeans.');
                    INSERT INTO Lemmas (Form, NormalizedForm, Headword, Language) VALUES
                        ('μῆνιν', 'μηνιν', 'μῆνις', 'grc');
                    INSERT INTO ApparatusEntries (EditionId, CitationRef, SortOrder, Kind, Lemma, Witness, Content) VALUES
                        (3, 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2:1.1', 1, 'rdg', 'μῆνιν', 'A', 'μῆνιν A');";
                await cmd.ExecuteNonQueryAsync();
            }

            Window = new MainWindow { Width = 1280, Height = 800 };
            Window.Show();
            await Screens.Settle();
            return Window;
        }

        public async Task<(ReaderPane Original, ReaderPane Second)> ReadAsync(int workId)
        {
            Assert.True(await Window!.OpenWorkAsync(workId));
            await Screens.Settle();
            var panes = Window.GetVisualDescendants().OfType<ReaderPane>().ToList();
            return (panes.Single(p => p.Role == PaneRole.Original), panes.Single(p => p.Role == PaneRole.Translation));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var owned in Window?.OwnedWindows.ToList() ?? new()) owned.Close();
            Window?.Close();
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

    /// <summary>
    /// Opens a pane's passage menu the way a right-click does, so that its
    /// Opening handler decides what it shows, and hands back the entries.
    /// </summary>
    private static async Task<List<MenuItem>> OpenMenuAsync(ReaderPane pane)
    {
        pane.List.RaiseEvent(new ContextRequestedEventArgs());
        await Screens.Settle(2);
        return pane.List.ContextMenu!.Items.OfType<MenuItem>().ToList();
    }

    private static async Task CloseMenuAsync(ReaderPane pane)
    {
        pane.List.ContextMenu!.Close();
        await Screens.Settle(2);
    }

    private static bool Shows(List<MenuItem> items, string header) =>
        items.Single(i => i.Header as string == header).IsVisible;

    private static MenuItem SourcePageItem(List<MenuItem> items) =>
        items.Single(i => i.Header is string h && (h == "View Online…" || h.StartsWith("View on ", StringComparison.Ordinal)));

    private static string? PrimaryFont(ReaderPane pane) => pane.List.FontFamily.FamilyNames.PrimaryFamilyName;

    [AvaloniaFact]
    public async Task A_tablet_with_no_translation_opens_its_signs_beside_its_transliteration()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        var (original, second) = await library.ReadAsync(1);

        // Both are originals; the second goes opposite the first rather than
        // queueing behind it in one menu while the other page sits empty.
        Assert.Equal(1, original.Edition?.EditionId);
        Assert.Equal(2, second.Edition?.EditionId);
        Assert.Equal(3, second.Count);
        Assert.Equal("ORIGINAL", original.RoleText);
        Assert.Equal("ORIGINAL", second.RoleText);

        // The signs are drawn in a face that has them; the transliteration
        // keeps the reading font.
        var cuneiform = ScriptFonts.FamilyFor(Signs);
        Assert.NotNull(cuneiform);
        Assert.Equal(cuneiform, PrimaryFont(second));
        Assert.NotEqual(cuneiform, PrimaryFont(original));
        Screens.Save(window, "october-cuneiform-beside-transliteration");

        // A work with a translation puts it back, label and font together.
        (_, second) = await library.ReadAsync(2);
        Assert.Equal(4, second.Edition?.EditionId);
        Assert.Equal("TRANSLATION", second.RoleText);
        Assert.NotEqual(cuneiform, PrimaryFont(second));
    }

    [AvaloniaFact]
    public async Task The_passage_menu_offers_only_what_the_text_can_answer()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();

        // The tablet: no Akkadian lemmas and no apparatus, so neither tool,
        // and its page on Oracc, named by host.
        var (original, second) = await library.ReadAsync(1);
        foreach (var pane in new[] { original, second })
        {
            var items = await OpenMenuAsync(pane);
            Assert.False(Shows(items, "Word Study…"));
            Assert.False(Shows(items, "Editor's Notes…"));

            var page = SourcePageItem(items);
            Assert.True(page.IsVisible);
            Assert.Equal("View on oracc.org…", page.Header);
            Assert.Equal(Tablet, window.SourceWebPageFor(pane)?.AbsoluteUri);
            await CloseMenuAsync(pane);
        }

        // The Greek has a lemma and a note, so both tools; its source is a
        // file, so no link. The English has neither, and a file URI is not a
        // web page however it is spelled.
        (original, second) = await library.ReadAsync(2);

        var greek = await OpenMenuAsync(original);
        Assert.True(Shows(greek, "Word Study…"));
        Assert.True(Shows(greek, "Editor's Notes…"));
        Assert.False(SourcePageItem(greek).IsVisible);
        await CloseMenuAsync(original);

        var english = await OpenMenuAsync(second);
        Assert.False(Shows(english, "Word Study…"));
        Assert.False(Shows(english, "Editor's Notes…"));
        Assert.False(SourcePageItem(english).IsVisible);
        Assert.Null(window.SourceWebPageFor(second));
        await CloseMenuAsync(second);
    }

    [AvaloniaFact]
    public async Task About_and_Help_credit_the_new_collections()
    {
        var about = new AboutWindow();
        about.Show();
        await Screens.Settle(4);

        var text = string.Join("\n", about.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("Cuneiform Texts", text);
        Assert.Contains("Creative Commons Attribution-ShareAlike 3.0, or CC0, depending on the project", text);
        Assert.Contains("Tzvi Abusch's Maqlû", text);
        about.Close();

        // The TLA, the curse tablets and the magical papyri are credited in
        // their help topics, as on Windows, each with the terms it is under.
        string Topic(string title) => HelpTopics.All.Single(t => t.Title == title).Body;
        Assert.Contains("CC BY-SA 4.0", Topic("Egyptian texts (Thesaurus Linguae Aegyptiae)"));
        Assert.Contains("CC BY 4.0", Topic("Curse tablets"));
        Assert.Contains("CC BY-SA 4.0", Topic("Curse tablets"));
        Assert.Contains("CC BY 3.0", Topic("Magical papyri"));
        Assert.Contains("Open Database License", Topic("Magical papyri"));
    }
}
