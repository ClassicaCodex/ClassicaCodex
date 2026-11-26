using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Quotations;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.DictionaryCitations;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The dictionaries' citations, end to end: LSJ's entry for μῆνις in Word
/// Study with its citations set back, the one that opens a line of the
/// library followed by a click and Back, the reader's § on the lines it
/// cites, and the window listing the entries that cite a line.
///
/// On a library of its own - three lines of the Iliad and their translation,
/// the lemma for μῆνιν, and the entry read from TEI as the dictionary step
/// reads it - so it runs everywhere, with or without a sample library.
/// </summary>
public class DictionaryCitationsTests
{
    private const string Iliad = "tlg0012.tlg001.perseus-grc2";
    private const string Translation = "tlg0012.tlg001.perseus-eng3";
    private const string Line1 = "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος";
    private const string Line2 = "οὐλομένην, ἣ μυρίʼ Ἀχαιοῖς ἄλγεʼ ἔθηκε,";
    private const string Line3 = "πολλὰς δʼ ἰφθίμους ψυχὰς Ἄϊδι προΐαψεν";

    private const string Entry = @"<entryFree key=""mh=nis"" type=""main""><orth>mh=nis</orth>, <gen>h(</gen>,
        <sense n=""A"" level=""1""><tr>wrath</tr>, <cit><quote lang=""greek"">mh=nin a)/eide qea/</quote>
            <bibl n=""urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.1""><author>Il.</author> <biblScope>1.1</biblScope></bibl></cit>;
            <bibl n=""urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:3""><biblScope>3</biblScope></bibl>;</sense>
        <sense n=""II"" level=""2"">of the gods, <bibl n=""urn:cts:greekLit:tlg0059.tlg030.perseus-grc1:390e""><author>Pl.</author> <title>R.</title> 390e</bibl>;
            <bibl><author>Hsch.</author></bibl></sense></entryFree>";

    private static string Ref(string edition, string reference) => $"urn:cts:greekLit:{edition}.{reference}";

    private sealed class Library : IAsyncDisposable
    {
        private readonly string? _previousLibrary = DbConnectionFactory.DatabasePath;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-dictionary-tests", Guid.NewGuid().ToString("N"));
        private readonly IDisposable _quotations = QuotationIndex.UseInstead(new QuotationIndex(Array.Empty<QuotationLink>()));

        public MainWindow Window { get; private set; } = null!;

        /// <param name="kept">Whether the dictionary was loaded keeping its citations, as now, or before, as an older library was.</param>
        public async Task<MainWindow> OpenAsync(bool kept = true)
        {
            Directory.CreateDirectory(_folder);
            DbConnectionFactory.Configure(Path.Combine(_folder, "library.db"), remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            await using (var conn = await DbConnectionFactory.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES (1, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit');
                    INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES (1, 1, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
                    INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                        (1, 1, '{Iliad}', 'Original', 'grc', 'perseus-greek'),
                        (2, 1, '{Translation}', 'Translation', 'eng', 'perseus-greek');
                    INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text, IsVerse) VALUES
                        (1, '{Ref(Iliad, "1.1")}', 1, '{Line1}', 1),
                        (1, '{Ref(Iliad, "1.2")}', 2, '{Line2}', 1),
                        (1, '{Ref(Iliad, "1.3")}', 3, '{Line3}', 1),
                        (2, '{Ref(Translation, "1.1")}', 1, 'Sing, goddess, the wrath of Achilles,', 0),
                        (2, '{Ref(Translation, "1.2")}', 2, 'the ruinous wrath that brought the Achaeans countless pains,', 0),
                        (2, '{Ref(Translation, "1.3")}', 3, 'and sent many strong souls to Hades.', 0);
                    INSERT INTO Lemmas (Form, NormalizedForm, Headword, Language) VALUES ('μῆνιν', 'μηνιν', 'μῆνις', 'grc');";
                await cmd.ExecuteNonQueryAsync();
            }

            var (text, citations) = LexiconIngestService.FlattenEntry(XElement.Parse(Entry, LoadOptions.PreserveWhitespace), "grc");
            await new DefinitionRepository().BulkInsertAsync(new[]
            {
                new Definition
                {
                    Headword = "μῆνις",
                    NormalizedHeadword = WordNormalizer.NormalizeHeadword("μῆνις", "grc"),
                    Language = "grc",
                    Source = "LSJ",
                    Entry = kept ? text : "mh=nis , h( , wrath",
                    Citations = kept ? citations : Array.Empty<ClassicaCodex.Core.Lexicon.LexiconCitation>()
                }
            });

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

    private static ReaderPane Pane(MainWindow window, PaneRole role) =>
        window.GetVisualDescendants().OfType<ReaderPane>().Single(p => p.Role == role);

    private static async Task<ReaderPane> OpenIliadAsync(MainWindow window)
    {
        Assert.True(await window.OpenWorkAsync(window.LibraryWorks.Single(w => w.Title == "Iliad").WorkId));
        await Screens.Settle(8);
        return Pane(window, PaneRole.Original);
    }

    private static int RowOf(ReaderPane pane, string reference)
    {
        for (var i = 0; i < pane.Count; i++)
            if (PassageCitation.Display(pane.NodeAt(i)!.CitationRef) == reference) return i;
        throw new InvalidOperationException($"no row {reference}");
    }

    private static TextBlock PassageBlock(ReaderPane pane, int row)
    {
        pane.EnsureVisible(row);
        var container = pane.List.ContainerFromIndex(row)!;
        var text = pane.NodeAt(row)!.Text;
        return container.GetVisualDescendants().OfType<TextBlock>().First(b => SteadyLines.TextOf(b).StartsWith(text, StringComparison.Ordinal));
    }

    /// <summary>Word Study on Iliad 1.1 with μῆνιν chosen and its headword's entry showing, the reader on that line.</summary>
    private static async Task<WordStudyWindow> StudyAsync(MainWindow window)
    {
        await OpenIliadAsync(window);
        var edition = (await new EditionRepository().GetByWorkAsync(1)).Single(e => e.Kind == EditionKind.Original);
        var node = (await new TextNodeRepository().GetByEditionAsync(edition.EditionId)).First();
        await window.NavigateToPassageAsync(1, node.TextNodeId);
        await Screens.Settle(6);

        WordStudyFeature.Open(window, new PassageContext(node, edition, null, FromOriginal: true, "Palatino", null, null), "μῆνιν");
        await Screens.Settle(6);
        var study = window.OwnedWindows.OfType<WordStudyWindow>().Single();
        await study.WhenIdleAsync();

        Assert.Equal("μῆνις", Assert.Single(study.Headwords).Headword);
        study.HeadwordList.SelectedIndex = 0;
        await study.WhenIdleAsync();
        await Screens.Settle(4);
        return study;
    }

    /// <summary>Where a citation is drawn, in the Word Study window's own coordinates.</summary>
    private static Avalonia.Point PointOn(WordStudyWindow study, CitationSpan span)
    {
        var block = study.DefinitionBlock;
        var rect = block.TextLayout.HitTestTextRange(span.Start, span.End - span.Start).First();
        return block.TranslatePoint(rect.Center, study)!.Value;
    }

    [AvaloniaFact]
    public async Task Word_study_sets_the_citations_back_and_opens_the_line_one_names()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();
        var study = await StudyAsync(window);

        // Every citation the entry prints, in order, with what it opens.
        Assert.Equal(new[] { "Il. 1.1", "3", "Pl. R. 390e", "Hsch." }, study.Citations.Select(c => c.Citation.Label));
        Assert.Equal(new[] { "Homer, Iliad 1.1", "Homer, Iliad 1.3", null, null }, study.Citations.Select(c => c.Destination));
        Assert.Contains("μῆνιν ἄειδε θεά Il. 1.1", study.DefinitionText);
        Assert.Contains("of the gods, Pl. R. 390e", study.DefinitionText);

        // Set back: smaller than the definition, in the accent where it leads
        // somewhere and in the faint ink where it does not.
        var runs = study.DefinitionBlock.Inlines!.OfType<Run>().ToList();
        var bodySize = study.DefinitionBlock.FontSize;
        Assert.All(runs.Where(r => r.Text is "Il. 1.1" or "3" or "Pl. R. 390e" or "Hsch."), r => Assert.True(r.FontSize < bodySize));
        Screens.Save(study, "dictionary-citations-1-word-study");

        // Hovering the citation of line 3 shows the line; the Plato it cannot open shows nothing.
        var line3 = study.Citations[1];
        study.MouseMove(PointOn(study, line3));
        await Screens.Settle(2);
        Assert.Equal($"Homer, Iliad 1.3\n{Line3}", study.CitationTipShowing);

        study.MouseMove(PointOn(study, study.Citations[2]));
        await Screens.Settle(2);
        Assert.Null(study.CitationTipShowing);

        // A click on it puts the reader on line 3; the window stays for the next.
        var point = PointOn(study, line3);
        study.MouseMove(point);
        study.MouseDown(point, MouseButton.Left);
        study.MouseUp(point, MouseButton.Left);
        await Screens.Settle(10);
        Assert.Equal(Line3, Pane(window, PaneRole.Original).SelectedNode?.Text);
        Assert.True(study.IsVisible);

        // Back returns to the line Word Study was opened on.
        var back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BackButton");
        Assert.True(back.IsEnabled);
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Screens.Settle(8);
        Assert.Equal(Line1, Pane(window, PaneRole.Original).SelectedNode?.Text);

        // A press on a citation that opens nothing goes nowhere.
        await study.OpenCitationAsync(study.Citations[2]);
        await Screens.Settle(4);
        Assert.Equal(Line1, Pane(window, PaneRole.Original).SelectedNode?.Text);
    }

    [AvaloniaFact]
    public async Task The_lines_LSJ_cites_are_marked_and_list_the_entries_that_cite_them()
    {
        await using var library = new Library();
        var window = await library.OpenAsync();
        var pane = await OpenIliadAsync(window);

        Assert.True(pane.MarksAt(RowOf(pane, "1.1")).HasFlag(PassageMarks.Dictionary));
        Assert.False(pane.MarksAt(RowOf(pane, "1.2")).HasFlag(PassageMarks.Dictionary));
        Assert.True(pane.MarksAt(RowOf(pane, "1.3")).HasFlag(PassageMarks.Dictionary), "the bare 3 after Il. 1.1 is line 3");
        Assert.EndsWith(PassageMarkSymbols.Dictionary, SteadyLines.TextOf(PassageBlock(pane, RowOf(pane, "1.1"))));

        // The translation beside it is not what the dictionary cites.
        var translation = Pane(window, PaneRole.Translation);
        Assert.All(Enumerable.Range(0, translation.Count), i => Assert.False(translation.MarksAt(i).HasFlag(PassageMarks.Dictionary)));
        Screens.Save(window, "dictionary-citations-2-mark");

        // Offered on a cited line and on no other.
        pane.SelectOnly(RowOf(pane, "1.2"));
        pane.List.RaiseEvent(new ContextRequestedEventArgs());
        await Screens.Settle(2);
        var items = pane.List.ContextMenu!.Items.OfType<MenuItem>().ToList();
        Assert.False(items.Single(i => i.Header as string == "Cited in the Dictionaries…").IsVisible);
        pane.List.ContextMenu.Close();
        await Screens.Settle(2);

        pane.SelectOnly(RowOf(pane, "1.1"));
        pane.List.RaiseEvent(new ContextRequestedEventArgs());
        await Screens.Settle(2);
        var citedIn = pane.List.ContextMenu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Cited in the Dictionaries…");
        Assert.True(citedIn.IsVisible);
        citedIn.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        pane.List.ContextMenu.Close();
        await Screens.Settle(4);

        var cited = window.OwnedWindows.OfType<DictionaryCitationsWindow>().Single();
        await cited.LoadTask!;
        await Screens.Settle(4);

        // μῆνις, sense A, with the phrase LSJ quotes before its citation.
        var entry = Assert.Single(cited.Items);
        Assert.Equal("μῆνις", entry.Headword);
        Assert.Equal("LSJ, sense A", entry.Heading);
        Assert.Equal("Il. 1.1", entry.Label);
        Assert.EndsWith("wrath , μῆνιν ἄειδε θεά", entry.Before);
        Assert.Contains("Pl. R. 390e", entry.Entry);

        var shown = string.Join("\n", cited.GetVisualDescendants().OfType<TextBlock>().Select(SteadyLines.TextOf));
        Assert.Contains("37 opened the one the dictionary quotes", shown);
        Screens.Save(cited, "dictionary-citations-3-window");
    }

    [AvaloniaFact]
    public async Task A_library_loaded_before_citations_were_kept_is_told_how_to_get_them()
    {
        await using var library = new Library();
        var window = await library.OpenAsync(kept: false);
        var study = await StudyAsync(window);

        Assert.Empty(study.Citations);
        Assert.Contains("Run the Dictionaries step in Setup again", study.DefinitionText);
        Assert.False(Pane(window, PaneRole.Original).MarksAt(0).HasFlag(PassageMarks.Dictionary));
    }
}
