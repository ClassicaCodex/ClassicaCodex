using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.Export;
using ClassicaCodex.Mac.Features.Translation;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// One copy of the sample library for the whole class, and a folder for the
/// files the exports write - both gone when the class is done, since the
/// library is a couple of hundred megabytes and the tests run often.
/// </summary>
public sealed class TranslationTestLibrary : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "classicacodex-translation-tests", Guid.NewGuid().ToString("N"));

    public TranslationTestLibrary()
    {
        Scratch = Path.Combine(_folder, "exports");
        if (Screens.SampleLibrary is not { } library) return;

        Directory.CreateDirectory(_folder);
        LibraryPath = Path.Combine(_folder, "library.db");
        File.Copy(library, LibraryPath);
        File.SetAttributes(LibraryPath, FileAttributes.Normal);
    }

    /// <summary>The copy to write to, or null when there is no sample library to copy.</summary>
    public string? LibraryPath { get; }

    public string Scratch { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the system's temporary-file sweep.
        }
    }
}

/// <summary>
/// Translating and exporting, driven against a copy of the sample library:
/// the workbench writes to it, and so does Create Translation's resume check.
/// </summary>
public class TranslationTests : IClassFixture<TranslationTestLibrary>
{
    private readonly TranslationTestLibrary _library;

    public TranslationTests(TranslationTestLibrary library) => _library = library;

    private string Scratch => _library.Scratch;

    private async Task<MainWindow?> OpenReaderAsync()
    {
        if (_library.LibraryPath is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(8);
        return window;
    }

    private static Work WorkTitled(MainWindow window, string title) =>
        window.LibraryWorks.First(w => w.Title == title);

    private static async Task<PassageContext> FirstLineAsync(Work work, bool fromOriginal = true)
    {
        var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
        var original = editions.First(e => e.Kind == EditionKind.Original);
        var translation = editions.First(e => e.Kind == EditionKind.Translation && !e.CtsUrn.Contains(".mine-"));
        var edition = fromOriginal ? original : translation;
        var counterpart = fromOriginal ? translation : original;
        var node = (await new TextNodeRepository().GetByEditionAsync(edition.EditionId)).First(n => n.NodeKind is null or "line");

        return new PassageContext(node, edition, counterpart, fromOriginal, fromOriginal ? "Palatino" : "Georgia",
            EditionLabels.Descriptor(edition), EditionLabels.Descriptor(counterpart));
    }

    private static T Owned<T>(Window owner) where T : Window => owner.OwnedWindows.OfType<T>().Last();

    /// <summary>
    /// The real reader for everything but the reload, which it counts: the
    /// reader's own reload currently trips over a null row in ReaderPane's
    /// template (switching works does too), and that is the shell's to fix,
    /// not this feature's to test. What is tested is that it is asked for.
    /// </summary>
    private sealed class RecordingHost : IReaderHost
    {
        private readonly MainWindow _main;

        public RecordingHost(MainWindow main) => _main = main;

        public int Reloads { get; private set; }

        public Window Window => _main;

        public Task NavigateToPassageAsync(int workId, long textNodeId) => _main.NavigateToPassageAsync(workId, textNodeId);

        public Task<bool> OpenWorkAsync(int workId) => _main.OpenWorkAsync(workId);

        public Work? OpenWork => _main.OpenWork;

        public Work? SelectedLibraryWork => _main.SelectedLibraryWork;

        public string AuthorNameFor(Work work) => _main.AuthorNameFor(work);

        public Task RefreshPassageMarksAsync() => _main.RefreshPassageMarksAsync();

        public Task ReloadLibraryAsync() => _main.ReloadLibraryAsync();

        public Task ReloadOpenWorkAsync()
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Photograph(Window window, string name)
    {
        await Screens.Settle(4);
        Screens.Save(window, name + "-light");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(4);
        Screens.Save(window, name + "-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await Screens.Settle(2);
    }

    [AvaloniaFact]
    public async Task Workbench_creates_a_mine_edition_and_saves_and_deletes_what_is_typed()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var work = WorkTitled(window, "Euthyphro");
        Assert.True(await window.OpenWorkAsync(work.WorkId));
        await Screens.Settle(4);

        var host = new RecordingHost(window);

        // No translation of one's own yet, so the workbench asks for a name.
        var opening = TranslationFeature.OpenWorkbenchAsync(host, work);
        await Screens.Settle(4);

        var prompt = window.OwnedWindows.Single(w => w.Title == "Translate This Work");
        Screens.Save(prompt, "translation-name-prompt");
        prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "A literal draft";
        Click(prompt.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save"));
        await opening;
        await Screens.Settle(6);

        var workbench = TranslationFeature.WorkbenchFor(work.WorkId)!;
        Assert.NotNull(workbench);
        Assert.Equal(EditionKind.Translation, workbench.Translation.Kind);
        Assert.Contains(".mine-", workbench.Translation.CtsUrn);
        Assert.Equal("A literal draft", workbench.Translation.Translator);
        Assert.Equal(0, workbench.Index);
        Assert.StartsWith("Passage 1 of", workbench.ProgressText);

        // The Euthyphro opens on its title and its first speaker's name;
        // the dialogue proper starts at the third passage.
        await workbench.MoveAsync(1);
        await workbench.MoveAsync(1);
        Assert.Equal(2, workbench.Index);
        Assert.Empty(await new TextNodeRepository().GetByEditionAsync(workbench.Translation.EditionId));

        // The published translation stays out of reach until something is written.
        Assert.Equal("Compare (write first)", workbench.RevealButtonText);
        Assert.False(workbench.RevealEnabled);

        workbench.WordList.SelectedIndex = 0;
        await Screens.Settle(6);
        Assert.StartsWith("τί", workbench.ReferenceText);
        Assert.DoesNotContain("Looking up", workbench.ReferenceText);
        await Photograph(workbench, "translation-workbench-word");

        await workbench.BuildCribAsync();
        Assert.Contains("Click any word on the left for its full dictionary entry.", workbench.ReferenceText);
        await Photograph(workbench, "translation-workbench-crib");

        workbench.TranslationBox.Text = "What strange thing has happened, Socrates?";
        await Screens.Settle(2);
        Assert.Equal("Compare published", workbench.RevealButtonText);
        Assert.True(workbench.RevealEnabled);

        workbench.RevealPublished();
        await Screens.Settle(2);
        Assert.StartsWith("Published translation of [", workbench.ReferenceText);
        Assert.Equal("Comparing", workbench.RevealButtonText);
        await Photograph(workbench, "translation-workbench-compare");

        var original = (await new EditionRepository().GetByWorkAsync(work.WorkId)).First(e => e.Kind == EditionKind.Original);
        var third = (await new TextNodeRepository().GetByEditionAsync(original.EditionId))[2];

        await workbench.MoveAsync(1);
        Assert.Equal(3, workbench.Index);
        Assert.Equal("What strange thing has happened, Socrates?", workbench.PreviousTranslationText);

        var saved = await new TextNodeRepository().GetByEditionAsync(workbench.Translation.EditionId);
        var line = Assert.Single(saved);
        Assert.Equal(third.CitationRef, line.CitationRef);
        Assert.Equal("What strange thing has happened, Socrates?", line.Text);
        Assert.Contains("1 translated", workbench.ProgressText);

        // Clearing the box and moving on deletes the passage, which keeps
        // "how much is done" an honest count.
        await workbench.MoveAsync(-1);
        Assert.Equal("What strange thing has happened, Socrates?", workbench.TranslationBox.Text);
        workbench.TranslationBox.Text = string.Empty;
        await workbench.MoveAsync(1);
        Assert.Empty(await new TextNodeRepository().GetByEditionAsync(workbench.Translation.EditionId));
        Assert.Contains("0 translated", workbench.ProgressText);

        // Closing saves the passage in hand, trimmed.
        await workbench.MoveAsync(-1);
        workbench.TranslationBox.Text = "  What is new, Socrates?  ";
        workbench.Close();
        await Screens.Settle(6);
        Assert.Null(TranslationFeature.WorkbenchFor(work.WorkId));
        Assert.Equal(1, host.Reloads);
        var afterClose = Assert.Single(await new TextNodeRepository().GetByEditionAsync(workbench.Translation.EditionId));
        Assert.Equal("What is new, Socrates?", afterClose.Text);
        Assert.Equal(third.CitationRef, afterClose.CitationRef);

        // Opening again resumes the same edition, without asking, at the
        // first passage not yet written.
        await TranslationFeature.OpenWorkbenchAsync(host, work);
        await Screens.Settle(4);
        var resumed = TranslationFeature.WorkbenchFor(work.WorkId)!;
        Assert.Equal(workbench.Translation.EditionId, resumed.Translation.EditionId);
        Assert.Equal(0, resumed.Index);
        Assert.Contains("1 translated", resumed.ProgressText);
        resumed.Close();
        await Screens.Settle(4);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Workbench_asks_which_original_when_there_is_more_than_one()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var work = WorkTitled(window, "Civil War");
        var opening = TranslationFeature.OpenWorkbenchAsync(window, work);
        await Screens.Settle(4);

        var chooser = window.OwnedWindows.OfType<EditionChoiceDialog>().Single();
        Screens.Save(chooser, "translation-edition-choice");

        // Choosing, then declining to name it, leaves nothing behind.
        Click(chooser.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Use This"));
        await Screens.Settle(4);
        var prompt = window.OwnedWindows.Single(w => w.Title == "Translate This Work");
        Click(prompt.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel"));
        await opening;

        Assert.Null(TranslationFeature.WorkbenchFor(work.WorkId));
        Assert.DoesNotContain(await new EditionRepository().GetByWorkAsync(work.WorkId), e => e.CtsUrn.Contains(".mine-"));

        window.Close();
    }

    [AvaloniaFact]
    public async Task Workbench_refuses_any_edition_that_is_not_the_readers_own_translation()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var work = WorkTitled(window, "Apology");
        var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
        var original = editions.First(e => e.Kind == EditionKind.Original);
        var published = editions.First(e => e.Kind == EditionKind.Translation);

        foreach (var wrong in new[] { original, published })
        {
            Assert.Throws<InvalidOperationException>(() => new TranslationWorkbenchWindow(
                window, work, "Plato", wrong, original, new List<TextNode>(), new Dictionary<string, string>(),
                new List<(Edition, string)>(), _ => Task.FromResult(new ClassicaCodex.Core.PassageAligner(Array.Empty<TextNode>()))));
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task Export_writes_text_word_pdf_and_a_reference_with_the_citation()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var work = WorkTitled(window, "Agamemnon");
        Assert.True(await window.OpenWorkAsync(work.WorkId));
        var passage = await FirstLineAsync(work);

        await ExportFeature.ExportPassageAsync(window, passage);
        await Screens.Settle(6);
        var export = Owned<PassageExportWindow>(window);

        export.LineCount.Value = 4;
        await Screens.Settle(4);
        Assert.Equal("4 line(s).", export.StatusText);
        Assert.Contains($"[{ClassicaCodex.Core.PassageCitation.Display(passage.Node.CitationRef, passage.Node.Milestone)}]", export.PreviewText);

        export.Bilingual.IsChecked = true;
        await Screens.Settle(6);
        Assert.Contains("(trans.", export.PreviewText);
        await Photograph(export, "export-passage");

        Directory.CreateDirectory(Scratch);
        var citation = ClassicaCodex.Core.PassageCitation.Display(passage.Node.CitationRef, passage.Node.Milestone);

        var text = Path.Combine(Scratch, "agamemnon.txt");
        await export.WriteFileAsync(text, ExportFormat.Text);
        var written = await File.ReadAllTextAsync(text);
        Assert.StartsWith("Aeschylus, Agamemnon [", written);
        Assert.Contains($"[{citation}]", written);
        Assert.Contains(passage.Node.Text, written);
        Assert.Contains("Source: edition urn:cts:greekLit:tlg0085.tlg005", written);

        var word = Path.Combine(Scratch, "agamemnon.docx");
        await export.WriteFileAsync(word, ExportFormat.Word);
        using (var docx = ZipFile.OpenRead(word))
        {
            using var reader = new StreamReader(docx.GetEntry("word/document.xml")!.Open());
            var xml = await reader.ReadToEndAsync();
            Assert.Contains($"[{citation}]", xml);
            Assert.Contains("Aeschylus, Agamemnon", xml);
        }

        // The PDF through the Mac's own font resolver, here reading Linux's
        // font folders: the Windows one would fail at its first font.
        var pdf = Path.Combine(Scratch, "agamemnon.pdf");
        await export.WriteFileAsync(pdf, ExportFormat.Pdf);
        var bytes = await File.ReadAllBytesAsync(pdf);
        Assert.True(bytes.Length > 2000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.True(PdfFonts.FaceCount > 0);

        var bib = Path.Combine(Scratch, "agamemnon.bib");
        await export.WriteFileAsync(bib, ExportFormat.BibTeX);
        var reference = await File.ReadAllTextAsync(bib);
        Assert.Contains("@", reference);
        Assert.Contains("Agamemnon", reference);
        Assert.Contains($"Saved a BibTeX reference to agamemnon.bib.", export.StatusText);

        export.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Passage_set_export_attributes_every_line_and_pairs_translations()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var passages = new List<ExportPassage>();
        foreach (var title in new[] { "Agamemnon", "Aeneid" })
        {
            var work = WorkTitled(window, title);
            var line = await FirstLineAsync(work);
            passages.Add(new ExportPassage(work.WorkId, line.Node.TextNodeId, window.AuthorNameFor(work), work.Title,
                line.Node.CitationRef, line.Node.Text, Detail: "a test detail"));
        }

        ExportFeature.ExportPassageSet(window, "Opening lines", passages, "why each was chosen");
        await Screens.Settle(6);
        var export = Owned<PassageSetExportWindow>(window);

        Assert.Contains("Aeschylus, Agamemnon", export.PreviewText);
        Assert.Contains("(why each was chosen) a test detail", export.PreviewText);

        export.IncludeTranslations.IsChecked = true;
        await Screens.Settle(8);
        Assert.Contains("all paired with a translation", export.StatusText);
        Assert.Contains("(trans.", export.PreviewText);
        await Photograph(export, "export-passage-set");

        Directory.CreateDirectory(Scratch);
        var pdf = Path.Combine(Scratch, "set.pdf");
        await export.WriteFileAsync(pdf, ExportFormat.Pdf);
        Assert.True(new FileInfo(pdf).Length > 2000);

        var text = Path.Combine(Scratch, "set.txt");
        await export.WriteFileAsync(text, ExportFormat.Text);
        Assert.StartsWith("Opening lines", await File.ReadAllTextAsync(text));

        export.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Translate_shows_the_ingested_counterpart_and_asks_for_keys_before_anything_is_sent()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var work = WorkTitled(window, "Agamemnon");
        Assert.True(await window.OpenWorkAsync(work.WorkId));

        await TranslationFeature.TranslatePassageAsync(window, await FirstLineAsync(work));
        await Screens.Settle(6);
        var translate = Owned<TranslatePassageWindow>(window);

        Assert.StartsWith("Found", translate.IngestedStatusText);
        Assert.False(string.IsNullOrWhiteSpace(translate.IngestedText));
        await Photograph(translate, "translate-passage");

        // From the translation's side it looks toward the original.
        await TranslationFeature.TranslatePassageAsync(window, await FirstLineAsync(work, fromOriginal: false));
        await Screens.Settle(6);
        var reverse = Owned<TranslatePassageWindow>(window);
        Assert.NotSame(translate, reverse);
        Assert.False(string.IsNullOrWhiteSpace(reverse.IngestedStatusText));

        reverse.Close();
        translate.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Create_translation_resumes_a_previous_attempt()
    {
        if (await OpenReaderAsync() is not { } window) return;

        var work = WorkTitled(window, "Ibis");
        Assert.True(await window.OpenWorkAsync(work.WorkId));

        await TranslationFeature.CreateTranslationAsync(window, work);
        await Screens.Settle(8);
        var create = Owned<CreateTranslationWindow>(window);
        Assert.True(create.OriginalLineCount > 10);
        Assert.Equal(string.Empty, create.ResumeText);
        await Photograph(create, "create-translation");
        create.Close();
        await Screens.Settle(2);

        // An attempt that got two lines in, as a run stopped by the daily
        // limit would leave it.
        var editions = new EditionRepository();
        var nodes = new TextNodeRepository();
        var original = (await editions.GetByWorkAsync(work.WorkId)).First(e => e.Kind == EditionKind.Original);
        var source = await nodes.GetByEditionAsync(original.EditionId);
        var attempt = await editions.UpsertAsync(new Edition
        {
            WorkId = work.WorkId,
            CtsUrn = $"{work.CtsUrn}.ai-gemini-test",
            Kind = EditionKind.Translation,
            Language = "eng",
            Translator = "Gemini (AI-generated)"
        });
        await nodes.SyncEditionAsync(attempt, source.Take(2).Select((n, i) => new TextNode
        {
            EditionId = attempt, CitationRef = n.CitationRef, SortOrder = i, Text = "translated " + i
        }).ToList());

        await TranslationFeature.CreateTranslationAsync(window, work);
        await Screens.Settle(8);
        var resumed = Owned<CreateTranslationWindow>(window);
        Assert.Equal($"Resuming a previous attempt - 2 of {source.Count:N0} lines already translated.", resumed.ResumeText);
        Assert.False(resumed.PersistedAnything);
        await Photograph(resumed, "create-translation-resume");

        // The button's label grew as the lists filled; it is laid out for
        // its new words, not clipped at the width of its old ones.
        var label = resumed.TranslateButton.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Translate") == true);
        Assert.Equal($"Translate Remaining {source.Count - 2:N0} Line(s)", label.Text);
        Assert.True(label.Bounds.Width >= label.DesiredSize.Width - 0.5);

        resumed.Close();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Starting_points_lists_only_what_the_library_holds()
    {
        if (await OpenReaderAsync() is not { } window) return;

        await TranslationFeature.StartingPointsAsync(window);
        await Screens.Settle(8);
        var starting = Owned<StartingPointsWindow>(window);

        Assert.True(starting.List.ItemCount > 0);
        starting.List.SelectedIndex = 0;
        await Screens.Settle(2);
        Assert.NotEqual("Select a work to see why it is a good place to begin.", starting.WhyText);
        await Photograph(starting, "starting-points");

        starting.Close();
        window.Close();
    }

    [Fact]
    public async Task The_font_resolver_reads_collections_and_falls_back_to_an_installed_serif()
    {
        var catalog = FontCatalog.Scan(FontCatalog.SystemDirectories);
        if (catalog.FaceCount == 0) return;

        var resolver = new MacFontResolver(catalog);

        // Asked the way the reader's palette asks, it lands on something installed.
        var family = resolver.FamilyFor("Palatino, Palatino Linotype, Book Antiqua, DejaVu Serif, FreeSerif, serif");
        Assert.True(catalog.Has(family));

        // A Windows name for a face resolves to an installed one, never to nothing.
        var info = resolver.ResolveTypeface("Palatino Linotype", isBold: true, isItalic: false);
        Assert.NotNull(info);
        Assert.NotNull(resolver.GetFont(info!.FaceName));

        // A face out of a collection comes back as a font on its own - which
        // is how a Mac keeps Palatino - and PdfSharp can embed it, where the
        // whole collection would fail.
        var collection = catalog.Families.SelectMany(catalog.FacesOf).FirstOrDefault(f => f.Path.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase));
        if (collection == null) return;

        var bytes = SfntReader.ReadFaceBytes(collection.Path, collection.CollectionIndex)!;
        var trueType = bytes.Take(4).SequenceEqual(new byte[] { 0x00, 0x01, 0x00, 0x00 }) || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "true";
        Assert.True(trueType, "the face did not come back as a single TrueType font");

        Directory.CreateDirectory(Scratch);
        var pdf = Path.Combine(Scratch, "collection-face.pdf");
        await ExportFiles.WriteAsync(ExportFormat.Pdf, pdf, "A face from a collection", "a test",
            new List<(string, string)> { ("[1]", "Arma virumque cano") }, collection.Family);
        Assert.True(new FileInfo(pdf).Length > 1000);
    }

    [Fact]
    public void The_crib_keeps_every_word_and_trims_the_shared_citation_prefix()
    {
        Assert.Equal(new[] { "τὸ", "δὲ", "περὶ", "ὃ", "δι", "τῶν" }, PassageCrib.SplitWords("τὸ δὲ περὶ ὃ δι' τῶν,").ToArray());

        var passages = new[] { "urn:cts:x:1.10", "urn:cts:x:1.11", "urn:cts:x:1.12" }
            .Select(c => new TextNode { CitationRef = c, Text = "x" }).ToList();
        Assert.Equal("urn:cts:x:1.", PassageCrib.CommonCitationPrefix(passages));
    }
}
