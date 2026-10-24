using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Concordance;
using ClassicaCodex.Mac.Features.Search;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Search and the Concordance, driven against the sample library: the
/// searches a reader would run, the filters, the grouped view, the jump into
/// the reader, and the exports.
/// </summary>
public class SearchTests
{
    private const int AeneidWorkId = 65;

    [AvaloniaFact]
    public async Task Finds_arma_in_the_Aeneid_and_jumps_to_it_in_the_reader()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var search = session.OpenSearch();
        search.QueryBox.Text = "arma";
        await search.RunSearchAsync();
        await Screens.Settle(4);

        Assert.False(search.Truncated);
        Assert.True(search.VisiblePassages.Count > 100, $"only {search.VisiblePassages.Count} lines");
        Assert.Equal(search.Distribution.TotalMatches, search.VisiblePassages.Count);
        Assert.Contains("match(es)", search.StatusText);
        Assert.All(search.Rows, r => Assert.Equal(ResultRowKind.Passage, r.Kind));

        // Aeneid 1.1, with the word marked where it sits in the line.
        var index = search.VisiblePassages.ToList().FindIndex(p => p.WorkId == AeneidWorkId && p.CitationRef == "1.1");
        Assert.True(index >= 0, "Aeneid 1.1 is not among the results");
        var row = search.Rows[index];
        Assert.StartsWith("Arma virumque cano", row.Text);
        Assert.Contains((0, 4), ResultText.HighlightSpans(row.Text, new[] { "arma" }, WordOccurrences.TargetsFor("arma"), wholeWordsOnly: true));
        Assert.Equal("[1.1]", row.Citation);
        Assert.Equal("P. Vergilius Maro (Virgil), Aeneid [1.1]: Arma virumque cano, Troiae qui primus ab oris", search.CopyTextAt(index));

        search.SelectRow(index);
        await Screens.Settle(2);
        Screens.Save(search, "search-01-arma-light");

        await search.OpenSelectedAsync();
        await Screens.Settle(10);
        Assert.Equal(AeneidWorkId, session.Main.OpenWork?.WorkId);
        var target = search.VisiblePassages[index].TextNodeId;
        Assert.Contains(session.Main.GetVisualDescendants().OfType<ReaderPane>(), p => p.SelectedNode?.TextNodeId == target);
        Screens.Save(session.Main, "search-02-jumped-into-reader");

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(4);
        Screens.Save(search, "search-03-arma-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    [AvaloniaFact]
    public async Task Whole_words_find_Greek_typed_without_accents_where_anywhere_cannot()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var search = session.OpenSearch();
        search.QueryBox.Text = "μηνιν";

        // "Anywhere in the line" compares the letters typed against the
        // letters printed, and the text has its accents.
        search.MatchModeBox.SelectedIndex = 0;
        await search.RunSearchAsync();
        var anywhere = search.VisiblePassages.Count;

        search.MatchModeBox.SelectedIndex = 1;
        await search.RunSearchAsync();
        await Screens.Settle(4);
        var wholeWords = search.VisiblePassages.Count;

        Assert.True(wholeWords > anywhere, $"whole words {wholeWords}, anywhere {anywhere}");
        Assert.Contains(search.VisiblePassages, p => p.WorkTitle == "Iliad" && p.Text.Contains("μῆνιν"));

        // The row is marked on the accented word, although the query had none.
        var iliad = search.Rows.First(r => r.Lead == "Homer, Iliad" && r.Text.Contains("μῆνιν"));
        var spans = ResultText.HighlightSpans(iliad.Text, new[] { "μηνιν" }, WordOccurrences.TargetsFor("μηνιν"), wholeWordsOnly: true);
        Assert.Contains(spans, s => iliad.Text.Substring(s.Start, s.Length) == "μῆνιν");

        Screens.Save(search, "search-04-greek");
    }

    [AvaloniaFact]
    public async Task Filters_narrow_the_search_and_are_kept_in_the_recent_list()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var search = session.OpenSearch();
        search.QueryBox.Text = "virtus";
        search.LatinCheck.IsChecked = true;
        await search.RunSearchAsync();
        await Screens.Settle(4);

        Assert.NotEmpty(search.VisiblePassages);
        Assert.All(search.VisiblePassages, p => Assert.Contains(p.AuthorName, new[] { "Ovid", "P. Vergilius Maro (Virgil)", "Julius Caesar" }));
        Assert.Equal("Search: virtus (Latin)", search.Description);

        // Recorded, and offered back.
        var recent = search.RecentBox.Items.OfType<string>().ToList();
        Assert.Equal("(pick a recent search)", recent[0]);
        Assert.Contains("Search: virtus (Latin)", recent);

        // An author filter on top, then everything cleared and the recent
        // entry chosen: it restores the language filter and runs again.
        search.AuthorBox.SelectedItem = "Ovid";
        await search.RunSearchAsync();
        Assert.All(search.VisiblePassages, p => Assert.Equal("Ovid", p.AuthorName));
        Assert.Equal("Search: virtus (Latin, Ovid)", search.Description);

        search.QueryBox.Text = string.Empty;
        search.LatinCheck.IsChecked = false;
        search.AuthorBox.SelectedIndex = 0;
        search.RecentBox.SelectedItem = "Search: virtus (Latin)";
        await Screens.Settle(10);

        Assert.Equal("virtus", search.QueryBox.Text);
        Assert.True(search.LatinCheck.IsChecked);
        Assert.Equal(0, search.AuthorBox.SelectedIndex);
        Assert.Contains(search.VisiblePassages, p => p.AuthorName != "Ovid");
    }

    [AvaloniaFact]
    public async Task Return_runs_the_search_and_opens_a_result_and_command_F_comes_back_to_the_box()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var search = session.OpenSearch();
        await Screens.Settle(4);
        Assert.True(search.QueryBox.IsFocused);

        search.KeyTextInput("caesar");
        search.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        for (var i = 0; i < 100 && search.VisiblePassages.Count == 0; i++) await Screens.Settle(1);
        Assert.Contains(search.VisiblePassages, p => p.WorkTitle == "Gallic War");

        search.SelectRow(0);
        await Screens.Settle(2);
        Assert.True(search.ResultsList.ContainerFromIndex(0)?.Focus());
        search.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        for (var i = 0; i < 100 && session.Main.OpenWork == null; i++) await Screens.Settle(1);
        Assert.Equal(search.VisiblePassages[0].WorkId, session.Main.OpenWork?.WorkId);

        search.KeyPress(Key.F, RawInputModifiers.Meta, PhysicalKey.F, "f");
        await Screens.Settle(2);
        Assert.True(search.QueryBox.IsFocused);
        Assert.Equal("caesar", search.QueryBox.SelectedText);
    }

    [AvaloniaFact]
    public async Task A_filter_that_excludes_everything_says_which_way_to_widen()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var search = session.OpenSearch();
        search.QueryBox.Text = "virtus";
        search.GreekCheck.IsChecked = true;
        await search.RunSearchAsync();
        await Screens.Settle(4);

        Assert.Empty(search.VisiblePassages);
        Assert.Equal("No matches.", search.StatusText);
        Assert.Equal("No matches. Try Clear Filters, or a different match mode.", search.EmptyMessage);
        Screens.Save(search, "search-08-no-matches");

        // Nothing typed is not a search.
        search.QueryBox.Text = "   ";
        await search.RunSearchAsync();
        Assert.Equal("Type something to search for.", search.StatusText);
    }

    [AvaloniaFact]
    public async Task One_row_per_document_counts_the_whole_library_past_the_cap()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var search = session.OpenSearch();
        search.QueryBox.Text = "et";
        await search.RunSearchAsync();
        await Screens.Settle(4);

        // More lines than one search returns: the passage list is the front of
        // the alphabet, and says so.
        Assert.True(search.Truncated);
        Assert.Equal(ResultRowKind.Notice, search.Rows[^1].Kind);
        Assert.Contains("One row per document", search.Rows[^1].Text);
        Assert.True(search.Distribution.TotalMatches > search.VisiblePassages.Count);

        search.ResultsList.ScrollIntoView(search.Rows.Count - 1);
        await Screens.Settle(4);
        Screens.Save(search, "search-07-truncated-notice");

        search.SetView(1);
        await Screens.Settle(4);
        Assert.All(search.Rows, r => Assert.Equal(ResultRowKind.Document, r.Kind));
        Assert.Equal(search.Distribution.TotalMatches, search.Documents.Sum(d => d.Matches));
        Assert.Contains("counted across the whole library", search.StatusText);
        Screens.Save(search, "search-05-documents");

        // The Aeneid sorts last of the Latin and the capped rows never reach
        // it; opening it asks again with the work pinned, so all of its lines
        // are listed rather than none.
        var aeneid = search.Documents.First(d => d.WorkId == AeneidWorkId);
        Assert.DoesNotContain(search.VisiblePassages, p => p.WorkId == AeneidWorkId);
        await search.OpenDocumentAsync(AeneidWorkId);
        await Screens.Settle(4);

        Assert.Equal(aeneid.Matches, search.VisiblePassages.Count);
        Assert.All(search.VisiblePassages, p => Assert.Equal(AeneidWorkId, p.WorkId));
        Assert.Contains("Switch Show back", search.StatusText);
        Screens.Save(search, "search-06-one-document");

        // And back: the search itself is unchanged by having looked inside it.
        search.SetView(1);
        Assert.Equal(search.Distribution.WorkCount, search.Documents.Count);
        search.SetView(0);
        Assert.DoesNotContain(search.VisiblePassages, p => p.WorkId == AeneidWorkId);
    }

    [AvaloniaFact]
    public async Task Opening_search_again_brings_the_same_window_forward()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        var first = session.OpenSearch();
        await Screens.Settle(4);
        Screens.Save(first, "search-00-empty");
        first.QueryBox.Text = "wrath";
        session.Main.OpenTool("search");
        await Screens.Settle(4);

        Assert.Same(first, session.Main.OwnedWindows.OfType<SearchWindow>().Single());
        Assert.Equal("wrath", first.QueryBox.Text);

        first.Close();
        await Screens.Settle(2);
        session.Main.OpenTool("search");
        await Screens.Settle(4);
        var second = session.Main.OwnedWindows.OfType<SearchWindow>().Single();
        Assert.NotSame(first, second);
    }

    [AvaloniaFact]
    public async Task Concordance_lines_up_every_occurrence_and_jumps_to_it()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        session.Main.OpenTool("concordance");
        await Screens.Settle(6);
        var concordance = session.Main.OwnedWindows.OfType<ConcordanceWindow>().Single();
        Screens.Save(concordance, "concordance-00-empty");

        concordance.WordBox.Text = "arma";
        await concordance.RunConcordanceAsync();
        await Screens.Settle(6);

        Assert.NotEmpty(concordance.Rows);
        Assert.Contains("occurrence(s)", concordance.StatusText);

        foreach (var row in concordance.Rows)
        {
            // The keyword is the word as the edition prints it, and the three
            // columns put the line back together around it.
            Assert.Equal("arma", WordNormalizer.Normalize(row.Keyword));
            var at = row.FullLine.IndexOf(row.Left + row.Keyword, StringComparison.Ordinal);
            Assert.True(at >= 0, row.FullLine);
            Assert.EndsWith(row.Right, row.FullLine.TrimEnd());
            Assert.True(row.DisplayLeft.Length <= 92);
        }

        // One passage per line in the export, however often the word occurs in it.
        Assert.Equal(concordance.Passages.Count, concordance.Passages.Select(p => p.TextNodeId).Distinct().Count());
        Assert.True(concordance.Rows.Count >= concordance.Passages.Count);
        Assert.All(concordance.Passages, p => Assert.Contains("⟨", p.Detail));

        var table = concordance.TableRows;
        Assert.Equal(new[] { "LeftContext", "Keyword", "RightContext", "Source", "Citation", "FullLine" }, table[0]);
        Assert.Equal(concordance.Rows.Count + 1, table.Count);

        var opening = concordance.Rows.First(r => r.WorkId == AeneidWorkId && r.CitationRef == "1.1");
        Assert.Equal("Arma", opening.Keyword);
        Assert.Equal(string.Empty, opening.Left);
        Assert.StartsWith(" virumque cano", opening.Right);

        // Sorted by left context, the words nearest the keyword lead.
        concordance.Grid.Columns[0].Sort(System.ComponentModel.ListSortDirection.Ascending);
        await Screens.Settle(2);
        var nearest = concordance.Grid.CollectionView.Cast<KwicRow>()
            .Select(r => r.Left.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty)
            .Select(WordNormalizer.Normalize)
            .ToList();
        Assert.Equal(nearest.OrderBy(w => w, StringComparer.Ordinal), nearest);
        Screens.Save(concordance, "concordance-04-sorted-by-left-context");
        concordance.Grid.Columns[0].ClearSort();

        concordance.Grid.SelectedItem = opening;
        concordance.Grid.ScrollIntoView(opening, null);
        await Screens.Settle(4);
        Screens.Save(concordance, "concordance-01-arma-light");

        // Return on the selected row, which the grid would otherwise take to
        // move down a row.
        Assert.True(concordance.Grid.Focus());
        concordance.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        for (var i = 0; i < 100 && session.Main.OpenWork == null; i++) await Screens.Settle(1);
        await Screens.Settle(6);
        Assert.Equal(AeneidWorkId, session.Main.OpenWork?.WorkId);
        Assert.Same(opening, concordance.Grid.SelectedItem);
        Assert.Contains(session.Main.GetVisualDescendants().OfType<ReaderPane>(), p => p.SelectedNode?.TextNodeId == opening.TextNodeId);

        // Modeless beside the reader, so it is still there for the next one.
        Assert.True(concordance.IsVisible);

        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        await Screens.Settle(4);
        Screens.Save(concordance, "concordance-02-arma-dark");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    [AvaloniaFact]
    public async Task Concordance_of_Greek_keeps_the_run_up_to_the_word()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        session.Main.OpenTool("concordance");
        await Screens.Settle(6);
        var concordance = session.Main.OwnedWindows.OfType<ConcordanceWindow>().Single();

        concordance.WordBox.Text = "θεα";
        await concordance.RunConcordanceAsync();
        await Screens.Settle(6);

        Assert.NotEmpty(concordance.Rows);
        Assert.All(concordance.Rows, r => Assert.Equal("θεα", WordNormalizer.Normalize(r.Keyword)));

        // A long left context loses its beginning, never the words before the keyword.
        var longLeft = concordance.Rows.FirstOrDefault(r => r.Left.Length > 120);
        if (longLeft != null)
        {
            Assert.StartsWith("… ", longLeft.DisplayLeft);
            Assert.EndsWith(longLeft.DisplayLeft[2..], longLeft.Left);
        }

        Screens.Save(concordance, "concordance-03-greek");
    }

    [AvaloniaFact]
    public async Task Exports_the_passages_a_search_found()
    {
        await using var session = await Session.StartAsync();
        if (session == null) return;

        session.Main.OpenTool("concordance");
        await Screens.Settle(6);
        var concordance = session.Main.OwnedWindows.OfType<ConcordanceWindow>().Single();
        concordance.WordBox.Text = "arma";
        await concordance.RunConcordanceAsync();

        var export = new PassageSetExportWindow("Concordance: arma", concordance.Passages, "the keyword in context");
        export.Show(concordance);
        await Screens.Settle(6);
        Screens.Save(export, "export-01-passages");

        var chunks = export.BuildRenderChunks();
        Assert.Contains(chunks, c => c.Label == "P. Vergilius Maro (Virgil), Aeneid" && c.Text.Length == 0);
        Assert.Contains(chunks, c => c.Label == "[1.1]" && c.Text.StartsWith("Arma virumque cano"));
        Assert.Contains(chunks, c => c.Label == "(the keyword in context)" && c.Text.Contains("⟨Arma⟩"));

        var folder = Path.Combine(Path.GetTempPath(), "classicacodex-mac-search-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var txt = Path.Combine(folder, "arma.txt");
            export.Write(txt, "txt");
            var written = await File.ReadAllTextAsync(txt);
            Assert.StartsWith("Concordance: arma", written);
            Assert.Contains("[1.1] Arma virumque cano", written);

            var docx = Path.Combine(folder, "arma.docx");
            export.Write(docx, "docx");
            Assert.True(new FileInfo(docx).Length > 0);
        }
        finally
        {
            export.Close();
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The main window over a private copy of the sample library: Search
    /// records every search it runs in the recent list, so it writes.
    /// </summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly string _copy;

        private Session(string copy, MainWindow main)
        {
            _copy = copy;
            Main = main;
        }

        public MainWindow Main { get; }

        public static async Task<Session?> StartAsync()
        {
            if (Screens.SampleLibrary is not { } library) return null;

            var folder = Path.Combine(Path.GetTempPath(), "classicacodex-mac-search-tests");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".db");
            File.Copy(library, copy);
            File.SetAttributes(copy, FileAttributes.Normal);

            DbConnectionFactory.Configure(copy, remember: false);
            await SchemaInitializer.EnsureSchemaAsync();

            var main = new MainWindow { Width = 1360, Height = 860 };
            main.Show();
            await Screens.Settle(10);
            return new Session(copy, main);
        }

        public SearchWindow OpenSearch()
        {
            Main.OpenTool("search");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return Main.OwnedWindows.OfType<SearchWindow>().Single();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var owned in Main.OwnedWindows.ToList()) owned.Close();
            Main.Close();
            await Screens.Settle(2);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _copy, _copy + "-wal", _copy + "-shm" })
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }
}
