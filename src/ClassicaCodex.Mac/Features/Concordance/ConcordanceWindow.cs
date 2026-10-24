using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Search;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Features.Export;

namespace ClassicaCodex.Mac.Features.Concordance;

/// <summary>
/// One occurrence of the keyword: the row as the grid draws it, and the hit
/// as it is written out, kept together rather than read back off the cells.
///
/// The left context on screen is deliberately cut - that is what keeps the
/// keyword column aligned down the page, which is the whole point of a
/// concordance - so the visible cells are not the data. Exporting them would
/// hand over a left context trimmed to fit a column.
/// </summary>
internal sealed record KwicRow(
    string Left,
    string DisplayLeft,
    string Keyword,
    string Right,
    string DisplayRight,
    string Source,
    string CitationRef,
    string? Milestone,
    string FullLine,
    int WorkId,
    long TextNodeId,
    FontFamily TextFont)
{
    /// <summary>The citation as the hover shows it - "[1.1]", never the stored URN.</summary>
    public string Citation => PassageCitation.Bracketed(CitationRef, Milestone);
}

/// <summary>
/// A traditional KWIC concordance: every occurrence of a word lined up in
/// three columns - left context, the word, right context - so patterns in
/// usage jump out visually. This is the format published concordances to
/// Homer, Livy and the rest have used for a century, computed on demand here
/// instead of printed in a volume.
///
/// The Windows ConcordanceForm, column for column. It needed a zero-width
/// spacer column because Win32's list view draws its first column
/// left-aligned whatever it is told; a DataGrid takes the alignment it is
/// given, so the left context is simply right-aligned and the spacer goes.
/// </summary>
public sealed class ConcordanceWindow : FeatureWindow
{
    private readonly TextBox _wordBox;
    private readonly Button _searchButton;
    private readonly TextBlock _statusLabel;
    private readonly DataGrid _resultsGrid;
    private readonly StackPanel _emptyState;
    private readonly TextBlock _emptyMessage;
    private readonly TextNodeRepository _textNodeRepo = new();

    /// <summary>Every occurrence, in the order found - which is the order exports keep.</summary>
    private List<KwicRow> _rows = new();

    /// <summary>
    /// The distinct lines that matched, for export - deliberately not one
    /// entry per row. A KWIC list shows a row per occurrence, so a word
    /// appearing three times in a line produces three rows; exporting that
    /// shape would repeat the same line three times in the document. What
    /// belongs in an export is the passages, once each.
    /// </summary>
    private List<ExportPassage> _currentPassages = new();

    /// <summary>The word the rows on screen were built for, not whatever the box says now.</summary>
    private string _builtFor = string.Empty;

    private const string Intro =
        "Every occurrence of a word, lined up between what precedes it and what follows - " +
        "the form published concordances have taken for a century. Type a word and press Return.";

    public ConcordanceWindow()
    {
        Title = "Concordance (KWIC)";
        Width = 1240;
        Height = 740;
        MinWidth = 820;
        MinHeight = 460;

        _wordBox = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Word",
            Width = 280,
            FontSize = 14,
            MinHeight = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Concordance"), Width = 14, Height = 14, Margin = new Thickness(10, 0, 2, 0), Opacity = 0.6 }
        };
        _wordBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await RunConcordanceAsync();
        };

        _searchButton = new Button { Content = "Build Concordance", Classes = { "accent" }, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        _searchButton.Click += async (_, _) => await RunConcordanceAsync();

        var exportButton = new Button { Classes = { "toolbar" }, Content = IconText("Export", "Export", 14), VerticalAlignment = VerticalAlignment.Top };
        exportButton.Flyout = BuildExportFlyout();

        // Room for two lines, because this reports the totals across the
        // library as well as what is laid out, and one line of it was being
        // cut off mid-sentence on Windows.
        _statusLabel = new TextBlock
        {
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 12, 0),
            MaxLines = 3
        };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(_wordBox, Dock.Left);
        DockPanel.SetDock(_searchButton, Dock.Left);
        DockPanel.SetDock(exportButton, Dock.Right);
        top.Children.Add(_wordBox);
        top.Children.Add(_searchButton);
        top.Children.Add(exportButton);
        top.Children.Add(_statusLabel);

        _resultsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 30,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0)
        };

        _resultsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Left Context",
            Width = new DataGridLength(5, DataGridLengthUnitType.Star),
            CellTemplate = ContextCell(nameof(KwicRow.DisplayLeft), TextAlignment.Right, TextTrimming.LeadingCharacterEllipsis),

            // Sorted on the words nearest the keyword first, which is what a
            // concordance sorted "by left context" means. Sorting the cell's
            // own text would sort on whatever word the line happens to begin
            // with - the part of the context furthest from the word. The
            // member path is what tells the grid the column can sort at all;
            // the comparer is what it sorts by.
            CustomSortComparer = new NearestWordsFirst(),
            SortMemberPath = nameof(KwicRow.Left)
        });
        _resultsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Word",
            Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
            MinWidth = 90,
            CellTemplate = KeywordCell(),
            SortMemberPath = nameof(KwicRow.Keyword)
        });
        _resultsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Right Context",
            Width = new DataGridLength(5, DataGridLengthUnitType.Star),
            CellTemplate = ContextCell(nameof(KwicRow.DisplayRight), TextAlignment.Left, TextTrimming.CharacterEllipsis),
            SortMemberPath = nameof(KwicRow.Right)
        });
        _resultsGrid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Source",
            Width = new DataGridLength(230),
            CellTemplate = SourceCell(),
            SortMemberPath = nameof(KwicRow.Source)
        });

        _resultsGrid.DoubleTapped += async (_, e) =>
        {
            // A double-click on a column header sorts; it is not a jump.
            if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) == null) return;
            await JumpToSelectedAsync();
        };

        // On the way down: a DataGrid takes Return for itself, to move to the
        // next row, so a handler on the way up would never see it.
        _resultsGrid.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedAsync();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _resultsGrid.ContextRequested += OnGridContextRequested;

        // The line itself and the passage export first; then the table,
        // added to the same menu rather than replacing it. A concordance is
        // read as prose and analysed as rows - sorting a thousand occurrences
        // by what precedes the keyword is a spreadsheet question, and the
        // passage export cannot answer it.
        _resultsGrid.ContextMenu = BuildGridMenu();
        TableExport.Attach(_resultsGrid, () => $"concordance-{_builtFor}", KwicRows, SelectedKwicRows, KwicNotes);

        _emptyMessage = new TextBlock
        {
            Text = Intro,
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var tile = Art.Tile("concordance", "Concordance", 56);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        tile.Opacity = 0.85;
        _emptyState = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Children = { tile, _emptyMessage }
        };

        var body = new Grid();
        body.Children.Add(_resultsGrid);
        body.Children.Add(_emptyState);

        var layout = new DockPanel { Margin = new Thickness(18, 16, 18, 18) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(new Border { Classes = { "page" }, Child = body });
        Content = layout;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                FocusWord();
                e.Handled = true;
            }
        };

        Opened += (_, _) => FocusWord();
    }

    public void FocusWord()
    {
        _wordBox.Focus();
        _wordBox.SelectAll();
    }

    // For the tests, which drive the window as a reader would.
    internal TextBox WordBox => _wordBox;
    internal IReadOnlyList<KwicRow> Rows => _rows;
    internal IReadOnlyList<ExportPassage> Passages => _currentPassages;
    internal string StatusText => _statusLabel.Text ?? string.Empty;
    internal DataGrid Grid => _resultsGrid;
    internal IReadOnlyList<IReadOnlyList<string>> TableRows => KwicRows();

    private static StackPanel IconText(string glyph, string text, double size)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Glyphs.Icon(glyph, size));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(Glyphs.Icon("ChevronDown", 10));
        return panel;
    }

    /// <summary>
    /// A context cell, bound rather than filled: the grid reuses its rows as
    /// it scrolls, so a cell built with one occurrence's text would go on
    /// showing it under the next.
    /// </summary>
    private static FuncDataTemplate<KwicRow> ContextCell(string property, TextAlignment alignment, TextTrimming trimming) =>
        new((_, _) =>
        {
            var text = new TextBlock
            {
                TextAlignment = alignment,
                TextTrimming = trimming,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                FontSize = 14,
                [!TextBlock.TextProperty] = new Binding(property),
                [!TextBlock.FontFamilyProperty] = new Binding(nameof(KwicRow.TextFont)),
                [!ToolTip.TipProperty] = new Binding(nameof(KwicRow.Citation))
            };
            ToolTip.SetShowDelay(text, 700);
            return text;
        });

    /// <summary>
    /// The keyword, as that edition prints it rather than as it was typed -
    /// which is the point of a concordance drawn from editions that disagree
    /// about spelling - in the accent, so the column reads down the page.
    /// </summary>
    private static FuncDataTemplate<KwicRow> KeywordCell() =>
        new((_, _) =>
        {
            var text = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                [!TextBlock.TextProperty] = new Binding(nameof(KwicRow.Keyword)),
                [!TextBlock.FontFamilyProperty] = new Binding(nameof(KwicRow.TextFont)),
                [!ToolTip.TipProperty] = new Binding(nameof(KwicRow.Citation))
            };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.Accent"));
            return text;
        });

    /// <summary>
    /// Where the line is from. The citation is on hover rather than in the
    /// cell - baking it into the Source text is what made that column so
    /// cramped on Windows it had to be cut short.
    /// </summary>
    private static FuncDataTemplate<KwicRow> SourceCell() =>
        new((_, _) =>
        {
            var text = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                FontSize = 12,
                [!TextBlock.TextProperty] = new Binding(nameof(KwicRow.Source)),
                [!ToolTip.TipProperty] = new Binding(nameof(KwicRow.Citation))
            };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
            return text;
        });

    private IReadOnlyList<IReadOnlyList<string>> KwicRows() => Table(_rows);

    private IReadOnlyList<IReadOnlyList<string>> SelectedKwicRows() =>
        Table(_resultsGrid.SelectedItems.OfType<KwicRow>().ToList());

    private static IReadOnlyList<IReadOnlyList<string>> Table(IEnumerable<KwicRow> rows)
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "LeftContext", "Keyword", "RightContext", "Source", "Citation", "FullLine" }
        };

        foreach (var row in rows)
            table.Add(new[] { row.Left, row.Keyword, row.Right, row.Source, row.CitationRef, row.FullLine });

        return table;
    }

    private IReadOnlyList<string> KwicNotes() => new[]
    {
        $"Classica Codex concordance - {DateTime.Now:yyyy-MM-dd HH:mm}",
        $"Keyword: {_builtFor}   ({_rows.Count:N0} occurrences)",
        "One row per occurrence, so a line containing the word twice appears twice. " +
        "Left and right context are the full line either side of the keyword, not the " +
        "truncated form the screen shows."
    };

    internal async Task RunConcordanceAsync()
    {
        var word = (_wordBox.Text ?? string.Empty).Trim();
        if (word.Length == 0) return;

        _searchButton.IsEnabled = false;
        _statusLabel.Text = "Searching...";
        Cursor = new Cursor(StandardCursorType.Wait);
        _rows = new List<KwicRow>();
        _currentPassages = new List<ExportPassage>();
        _resultsGrid.ItemsSource = null;

        try
        {
            // Whole words through the index, not a substring scan of the raw
            // text, and this is a correctness fix before it is a speed one.
            //
            // A concordance is a word framed by its context, so it has to find
            // the word. The substring scan compares what was typed against the
            // text as printed, and this corpus is not printed the way anyone
            // types: Greek carries diacritics nobody enters into a search box,
            // 87 editions are set in lunate sigma, and Latin u/v and i/j are
            // the editor's choice rather than the author's. Concordancing
            // Greek "μηνιν" found 8 lines where the word is in 316, and
            // "iustitia" found 1,425 of 4,196 - and a concordance that quietly
            // misses three quarters of its word is worse than no concordance.
            //
            // Being roughly a hundred times faster is the smaller half: it
            // seeks an index instead of reading the whole text.
            //
            // What it gives up is the inflections a substring happened to
            // catch - "sophia" no longer picks up "sophian" - and that is
            // worth giving up, because it only ever caught the ones that
            // differ by a suffix and never the ones that differ by an accent
            // or a prefix. This concordance is now of a word, consistently.
            // The question about every form of a headword has its own window,
            // Morphology, which asks the lemma data rather than guessing from
            // spelling.
            //
            // Task.Run because SQLite's async methods complete synchronously;
            // awaited on the UI thread they would freeze the window instead.
            var filters = new SearchFilters { Query = word, MatchMode = SearchMatchMode.WholeWord };
            var (hits, distribution) = await Task.Run(async () => (
                await _textNodeRepo.SearchFilteredAsync(filters),
                await _textNodeRepo.CountMatchesByWorkAsync(filters)));
            var matches = hits.Rows;

            // What the index was actually asked for, which is what may be
            // sitting in these lines - the typed spelling is only one of them.
            var targets = WordOccurrences.TargetsFor(word);
            var reading = (FontFamily)this.FindResource("Cc.SourceFont")!;

            // The KWIC framing is what a concordance is for, so it travels
            // with the export as each passage's detail - one entry per line,
            // showing every occurrence in that line rather than repeating the
            // line once per occurrence.
            var passages = matches
                .Select(m => new ExportPassage(
                    m.WorkId, m.TextNodeId, m.AuthorName, m.WorkTitle, m.CitationRef, m.Text,
                    BuildKwicDetail(m.Text, targets), m.Milestone))
                .ToList();

            var rows = new List<KwicRow>();
            foreach (var m in matches)
            {
                var font = ScriptFonts.Compose(reading, ScriptFonts.FamilyFor(m.Text));
                var source = $"{m.AuthorName}, {m.WorkTitle}";

                // One KWIC row per occurrence, with the keyword column
                // carrying the word as that edition prints it rather than as
                // it was typed.
                var occurrences = WordOccurrences.Find(m.Text, targets);

                foreach (var (start, length) in occurrences)
                {
                    var left = m.Text[..start].TrimStart();
                    var keyword = m.Text.Substring(start, length);
                    var right = m.Text[(start + length)..].TrimEnd();
                    rows.Add(Row(left, keyword, right, m.Text, source, m.CitationRef, m.Milestone, m.WorkId, m.TextNodeId, font));
                }

                // A line the search matched but whose word cannot be located
                // in it shows whole rather than being silently dropped. It
                // should no longer happen now that both ends normalize the
                // same way, and it is still the right way to fail.
                if (occurrences.Count == 0)
                    rows.Add(Row(string.Empty, "(matched line)", m.Text, m.Text, source, m.CitationRef, m.Milestone, m.WorkId, m.TextNodeId, font));
            }

            _rows = rows;
            _currentPassages = passages;
            _builtFor = word;
            _resultsGrid.ItemsSource = rows;
            _emptyState.IsVisible = rows.Count == 0;
            _emptyMessage.Text = rows.Count == 0 ? $"No line in the library contains “{word}” as a word." : Intro;

            // A concordance that silently stops isn't a concordance - and one
            // that can only say "5000+" cannot answer the question it was
            // opened to answer. The line count is counted across the library
            // rather than across the rows that fitted, so what is capped is
            // how much of it can be laid out.
            _statusLabel.Text = hits.Truncated
                ? $"{distribution.TotalMatches:N0} lines contain {word}, in {distribution.WorkCount:N0} works " +
                  $"by {distribution.AuthorCount:N0} authors.\n" +
                  $"Showing {rows.Count:N0} occurrences from the first {matches.Count:N0} - narrow the search for the rest."
                : $"{rows.Count:N0} occurrence(s) across {matches.Count:N0} line(s), " +
                  $"in {distribution.WorkCount:N0} work(s) by {distribution.AuthorCount:N0} author(s).";
        }
        catch (Exception ex)
        {
            // Windows let this escape to its unhandled-exception dialog; here
            // it would take the app down instead, so it is said where the
            // result would have been.
            _statusLabel.Text = $"Couldn't build the concordance: {ex.Message}";
        }
        finally
        {
            _searchButton.IsEnabled = true;
            Cursor = null;
        }
    }

    private static KwicRow Row(
        string left, string keyword, string right, string fullLine, string source,
        string citationRef, string? milestone, int workId, long textNodeId, FontFamily font) =>
        new(left, TrimLeftContext(left), keyword, right, ResultText.Row(right, 300), source,
            citationRef, milestone, fullLine, workId, textNodeId, font);

    /// <summary>
    /// The keyword shown in context, once per occurrence in the line, in the
    /// same left / word / right shape the columns use.
    /// </summary>
    private static string BuildKwicDetail(string text, IReadOnlyCollection<string> targets)
    {
        var parts = new List<string>();

        foreach (var (start, length) in WordOccurrences.Find(text, targets))
        {
            var left = TrimLeftContext(text[..start].TrimStart());
            var right = text[(start + length)..].TrimEnd();
            if (right.Length > 90) right = right[..90] + "…";

            parts.Add($"{left} ⟨{text.Substring(start, length)}⟩ {right}");
        }

        return parts.Count == 0 ? string.Empty : string.Join("  |  ", parts);
    }

    /// <summary>
    /// Keeps the tail of the left context rather than the head.
    ///
    /// Right-aligning the column is only half the fix. A cell still has to
    /// lose something when the line is longer than the column, and losing
    /// its end would lose the words immediately before the keyword - the ones
    /// a concordance exists to show - and keep the start of the sentence,
    /// which is the part you can already read anywhere. Cutting from the left
    /// here means what survives is always the run-up to the keyword; the cell
    /// trims from its leading edge for the same reason when the column is
    /// narrower still.
    /// </summary>
    internal static string TrimLeftContext(string left)
    {
        const int maxChars = 90;
        if (left.Length <= maxChars) return left;

        // Cut at a word boundary where there is one nearby, so the fragment
        // starts on a whole word rather than mid-syllable.
        var tail = left[^maxChars..];
        var space = tail.IndexOf(' ');
        if (space > 0 && space < 20) tail = tail[(space + 1)..];

        return "… " + tail;
    }

    private KwicRow? SelectedRow => _resultsGrid.SelectedItem as KwicRow;

    private async Task JumpToSelectedAsync()
    {
        if (SelectedRow is not { } row) return;

        // Windows closes its concordance after the jump, because it is modal
        // there and the reader cannot be seen behind it. Here it is modeless
        // beside the reader, so it stays open for the next occurrence.
        await NavigateAsync(row.WorkId, row.TextNodeId);
    }

    /// <summary>The full line for a row: author, work, citation and the whole passage.</summary>
    private static string FullText(KwicRow row) =>
        $"{row.Source} [{PassageCitation.Display(row.CitationRef, row.Milestone)}]: {row.FullLine}";

    private Task ExportPassagesAsync()
    {
        ExportFeature.ExportPassagesFrom(this, $"Concordance: {_builtFor}", _currentPassages, "the keyword in context");
        return Task.CompletedTask;
    }

    private ContextMenu BuildGridMenu()
    {
        var menu = new ContextMenu();

        var open = new MenuItem { Header = "Open in Reader", Icon = Glyphs.Icon("OpenExternal", 14) };
        open.Click += async (_, _) => await JumpToSelectedAsync();

        var copy = new MenuItem { Header = "Copy Line", Icon = Glyphs.Icon("Copy", 14) };
        copy.Click += async (_, _) =>
        {
            if (SelectedRow is { } row) await ClipboardText.SetAsync(this, FullText(row));
        };

        var export = new MenuItem { Header = "Export All Passages…", Icon = Glyphs.Icon("Export", 14) };
        export.Click += async (_, _) => await ExportPassagesAsync();

        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.Items.Add(export);

        menu.Opening += (_, _) =>
        {
            open.IsEnabled = SelectedRow != null;
            copy.IsEnabled = SelectedRow != null;
        };

        return menu;
    }

    /// <summary>The same exports from the toolbar, for anyone who never right-clicks.</summary>
    private MenuFlyout BuildExportFlyout()
    {
        var flyout = new MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };

        MenuItem Item(string header, string glyph, Func<Task> action)
        {
            var item = new MenuItem { Header = header, Icon = Glyphs.Icon(glyph, 14) };
            item.Click += async (_, _) => await action();
            return item;
        }

        flyout.Items.Add(Item("Export All Passages…", "Export", ExportPassagesAsync));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("Export as CSV…", "Table", () => TableExport.ExportAsync(this, KwicRows(), $"concordance-{_builtFor}", KwicNotes, "csv")));
        flyout.Items.Add(Item("Export as Tab-Separated Text…", "Table", () => TableExport.ExportAsync(this, KwicRows(), $"concordance-{_builtFor}", KwicNotes, "txt")));
        flyout.Items.Add(Item("Export as Excel Workbook…", "Table", () => TableExport.ExportAsync(this, KwicRows(), $"concordance-{_builtFor}", KwicNotes, "xlsx")));
        return flyout;
    }

    /// <summary>
    /// Selects the row under the pointer before the menu opens, unless it is
    /// already part of the selection - so the menu acts on what was
    /// right-clicked, and a multi-row selection survives for "Copy Selected
    /// Rows".
    /// </summary>
    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var row = (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true);
        if (row?.DataContext is KwicRow item && !_resultsGrid.SelectedItems.Contains(item))
            _resultsGrid.SelectedItem = item;
    }

    /// <summary>
    /// Orders occurrences by the word immediately before the keyword, then
    /// the one before that, and so on outward - the left-context sort of a
    /// printed concordance.
    /// </summary>
    private sealed class NearestWordsFirst : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = Words(x as KwicRow);
            var b = Words(y as KwicRow);

            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var order = string.Compare(
                    WordNormalizer.Normalize(a[a.Length - 1 - i]),
                    WordNormalizer.Normalize(b[b.Length - 1 - i]),
                    StringComparison.Ordinal);
                if (order != 0) return order;
            }

            return a.Length.CompareTo(b.Length);
        }

        private static string[] Words(KwicRow? row) =>
            row?.Left.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
    }
}
