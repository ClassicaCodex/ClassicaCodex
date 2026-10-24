using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Compare;

/// <summary>
/// Two editions of one work, side by side, with what they disagree about -
/// the Windows CollationForm.
///
/// The one thing in the app that needs more than one collection to exist at
/// all. A single corpus gives one printing of a text and no way to know what
/// it settled; two give the disagreement, which is where the editorial work
/// actually is.
///
/// Deliberately not a verdict. It sorts differences by how much of one they
/// are - typography, spelling, line division, or the words - and shows the
/// counts, so the reader can see at a glance whether a pairing is worth
/// reading before reading any of it. Which reading is right is not a question
/// this can answer, and it does not pretend to.
/// </summary>
internal sealed class CollationWindow : FeatureWindow
{
    /// <summary>
    /// The share of shared passages differing in the words above which two
    /// editions disagree wholesale rather than at points.
    /// </summary>
    private const double Wholesale = 0.6;

    private static readonly string[] ShowChoices = { "Differences in the words", "Every difference", "Every passage" };

    private readonly CollationRepository _repo = new();

    private readonly ComboBox _pairBox;
    private readonly ComboBox _showBox;
    private readonly Button _exportButton;
    private readonly TextBlock _summary;
    private readonly Border _wholesaleNotice;
    private readonly DataGrid _rows;
    private readonly TextBlock _leftHeader;
    private readonly TextBlock _rightHeader;
    private readonly SelectableTextBlock _leftDetail;
    private readonly SelectableTextBlock _rightDetail;

    private List<CollationPair> _pairs = new();
    private CollationResult? _result;
    private CollationPair? _resultPair;
    private bool _loading;

    public CollationWindow()
    {
        Title = "Collate Editions";
        Width = 1180;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;

        var explainer = new TextBlock
        {
            Text = "Works this library holds more than one edition of. Differences are graded: " +
                   "only the last kind is a difference in the words.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        };

        _pairBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 460 };
        _pairBox.SelectionChanged += async (_, _) => await LoadSelectedAsync();

        _showBox = new ComboBox { Width = 230, ItemsSource = ShowChoices, SelectedIndex = 0 };
        _showBox.SelectionChanged += (_, _) => RenderRows();

        _exportButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { Glyphs.Icon("Export", 14), new TextBlock { Text = "Export…", VerticalAlignment = VerticalAlignment.Center } }
            },
            Margin = new Thickness(14, 0, 0, 0)
        };

        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
        AddCell(controls, Label("Work:"), 0);
        AddCell(controls, _pairBox, 1);
        var showLabel = Label("Show:");
        showLabel.Margin = new Thickness(16, 0, 8, 0);
        AddCell(controls, showLabel, 2);
        AddCell(controls, _showBox, 3);
        AddCell(controls, _exportButton, 4);

        _summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };

        var warningIcon = new PathIcon { Data = Glyphs.Get("Warning"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0) };
        warningIcon.Bind(PathIcon.ForegroundProperty, warningIcon.GetResourceObservable("Cc.Danger"));
        var warningText = new TextBlock
        {
            // A note about what a high rate means, not a warning that something
            // is wrong. Sophocles' Ajax comes out at 89%, and the rows show the
            // rate is honest: a dialectal kunagia against kunegia, an anexei
            // against an anexei subjunctive, differing word division. Those are
            // two editors' texts, not two printings of one - so the list is a
            // description of the distance between them, not a shortlist of
            // cruxes, and should not be read as one.
            Text = "At this rate the two disagree wholesale rather than at points - different editors' texts, " +
                   "or one transcribed less carefully. Read it as the distance between them, not a shortlist of cruxes.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5
        };
        var warningRow = new DockPanel();
        DockPanel.SetDock(warningIcon, Dock.Left);
        warningRow.Children.Add(warningIcon);
        warningRow.Children.Add(warningText);
        _wholesaleNotice = new Border { Classes = { "notice" }, Child = warningRow, IsVisible = false };

        _rows = new DataGrid
        {
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
            // The passages in the editions' own order, which is the order the
            // export writes - sorting a column would make the screen and the
            // file disagree about what "these rows" are.
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        _rows.Columns.Add(TextColumn("Passage", r => r.Passage, new DataGridLength(96)));
        _rows.Columns.Add(TextColumn("Difference", r => r.Difference, new DataGridLength(134)));
        _rows.Columns.Add(TextColumn(string.Empty, r => r.LeftCell, new DataGridLength(1, DataGridLengthUnitType.Star), reading: true));
        _rows.Columns.Add(TextColumn(string.Empty, r => r.RightCell, new DataGridLength(1, DataGridLengthUnitType.Star), reading: true));
        _rows.SelectionChanged += (_, _) => ShowSelectedDetail();

        // The same menu on the table's right-click and on the Export button. A
        // collation is a table someone will want in a spreadsheet, and an
        // export found only by right-clicking a list is one most people never
        // find.
        TableExport.Attach(_rows, SuggestedFileName, ExportRows, SelectedExportRows, ExportNotes);
        var buttonMenu = TableExport.Attach(_exportButton, SuggestedFileName, ExportRows, SelectedExportRows, ExportNotes);
        buttonMenu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        _exportButton.Click += (_, _) => buttonMenu.Open(_exportButton);

        _leftHeader = new TextBlock { Classes = { "h3" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) };
        _rightHeader = new TextBlock { Classes = { "h3" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 6) };
        _leftDetail = DetailText();
        _rightDetail = DetailText();

        var detail = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        AddCell(detail, DetailPane(_leftHeader, _leftDetail), 0);
        AddCell(detail, DetailPane(_rightHeader, _rightDetail), 2);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Height = 10, Background = Brushes.Transparent };
        var split = new Grid { RowDefinitions = new RowDefinitions("3*,10,2*") };
        split.RowDefinitions[0].MinHeight = 140;
        split.RowDefinitions[2].MinHeight = 110;
        Grid.SetRow(_rows, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(detail, 2);
        split.Children.Add(_rows);
        split.Children.Add(splitter);
        split.Children.Add(detail);

        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(explainer);
        top.Children.Add(controls);
        top.Children.Add(_summary);
        top.Children.Add(_wholesaleNotice);

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(split);
        Content = layout;

        Opened += (_, _) => LoadTask = LoadPairsAsync();
    }

    internal Task? LoadTask { get; private set; }

    /// <summary>The comparison now loading, for the tests to wait on.</summary>
    internal Task? CollateTask { get; private set; }

    internal IReadOnlyList<CollationPair> Pairs => _pairs;

    internal CollationResult? Result => _result;

    internal string SummaryText => _summary.Text ?? string.Empty;

    internal bool WholesaleNoticeShown => _wholesaleNotice.IsVisible;

    internal ComboBox PairBox => _pairBox;

    internal ComboBox ShowBox => _showBox;

    internal DataGrid Rows => _rows;

    internal string LeftDetailText => _leftDetail.Text ?? string.Empty;

    internal IReadOnlyList<IReadOnlyList<string>> ExportTable => ExportRows();

    internal IReadOnlyList<string> ExportNoteLines => ExportNotes();

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

    private static void AddCell(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static DataGridTextColumn TextColumn(string header, System.Linq.Expressions.Expression<Func<RowView, string>> value, DataGridLength width, bool reading = false)
    {
        var column = new DataGridTextColumn
        {
            Header = header,
            Binding = CompiledBinding.Create(value),
            Width = width,
            IsReadOnly = true
        };

        // The reading face for the two columns that are the text itself.
        if (reading) column.FontFamily = EchoUi.SourceFont;
        return column;
    }

    /// <summary>
    /// The reader's face and size for the original, because these two panes
    /// are the text, not a report about it.
    /// </summary>
    private static SelectableTextBlock DetailText() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontFamily = EchoUi.SourceFont,
        FontSize = ReadingFontSettings.SourceSize * 96.0 / 72.0,
        LineHeight = ReadingFontSettings.SourceSize * 96.0 / 72.0 * 1.38,
        Margin = new Thickness(14, 10)
    };

    private static Control DetailPane(TextBlock header, SelectableTextBlock text)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(new Border { Classes = { "page" }, Child = new ScrollViewer { Content = text } });
        return dock;
    }

    private sealed record PairOption(CollationPair Pair, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>One row of the table: a passage as the two editions print it, and how far apart they are.</summary>
    internal sealed record RowView(CollationRow Row, string Passage, string Difference, string LeftCell, string RightCell);

    /// <summary>
    /// What the picker shows for one pairing.
    ///
    /// The collection names alone are not enough - assuming they were made
    /// five entries of Agathemerus read identically. A work can have more than
    /// two editions (one has six), so several pairings share the same two
    /// collections, and two editions can come from one collection entirely,
    /// which leaves the collection name saying nothing.
    ///
    /// So the version identifier is added wherever the collections do not
    /// separate the pairings, and left off where they do: it is precise, but it
    /// is not what anyone is looking for, and "perseus-grc2" on every row would
    /// bury the collection names under identifiers that matter for a handful of
    /// works.
    /// </summary>
    private string DescribePair(CollationPair pair)
    {
        var left = SetupDataSourceCatalog.DescribeCollection(pair.LeftCollection);
        var right = SetupDataSourceCatalog.DescribeCollection(pair.RightCollection);

        var sameWork = _pairs.Count(p => p.WorkId == pair.WorkId);
        var sameCollections = _pairs.Count(p =>
            p.WorkId == pair.WorkId
            && p.LeftCollection == pair.LeftCollection
            && p.RightCollection == pair.RightCollection);

        var sides = sameWork > 1 && (sameCollections > 1 || pair.WithinOneCollection)
            ? $"{left} {pair.LeftVersion} / {right} {pair.RightVersion}"
            : $"{left} / {right}";

        return $"{pair.AuthorName} - {pair.WorkTitle}  ({sides})";
    }

    private async Task LoadPairsAsync()
    {
        try
        {
            _pairs = await _repo.FindPairsAsync();
        }
        catch (Exception ex)
        {
            _summary.Text = $"Couldn't read the library: {ex.Message}";
            _showBox.IsEnabled = false;
            return;
        }

        if (_pairs.Count == 0)
        {
            // Not an error, and the commonest state for a library with one
            // collection - so it says what would make it possible rather than
            // just showing an empty list.
            _summary.Text = "No work in this library has two original-language editions, so there is " +
                            "nothing to collate yet. Installing a collection that overlaps one you " +
                            "already have creates the pairs.";
            _showBox.IsEnabled = false;
            _pairBox.IsEnabled = false;
            _pairBox.PlaceholderText = "No work with two editions";
            return;
        }

        _pairBox.ItemsSource = _pairs.Select(p => new PairOption(p, DescribePair(p))).ToList();
        _pairBox.SelectedIndex = 0;
    }

    private Task LoadSelectedAsync() => CollateTask = LoadSelectedCoreAsync();

    private async Task LoadSelectedCoreAsync()
    {
        if (_loading || _pairBox.SelectedItem is not PairOption { Pair: var pair }) return;

        _loading = true;
        try
        {
            _summary.Text = "Collating…";
            _wholesaleNotice.IsVisible = false;

            // Off the UI thread: both editions read and compared line by line,
            // which on a long work is long enough to freeze the window.
            _result = await Task.Run(() => _repo.CollateAsync(pair));
            _resultPair = pair;

            // The version identifier always, on the columns and panes that hold
            // the text itself. Two editions from one collection would otherwise
            // give both sides the same heading, and even across collections the
            // identifier is what a reader would cite.
            _leftHeader.Text = $"{SetupDataSourceCatalog.DescribeCollection(pair.LeftCollection)}  ({pair.LeftVersion})";
            _rightHeader.Text = $"{SetupDataSourceCatalog.DescribeCollection(pair.RightCollection)}  ({pair.RightVersion})";

            RenderSummary();
            RenderRows();
        }
        catch (Exception ex)
        {
            _result = null;
            _resultPair = null;
            _summary.Text = $"Couldn't collate these editions: {ex.Message}";
            RenderRows();
        }
        finally
        {
            _loading = false;
        }

        // The reader picked another pairing while this one was being read:
        // load that, rather than leave the table showing one pairing under
        // the name of another.
        if (_pairBox.SelectedItem is PairOption { Pair: var now } && now != pair) await LoadSelectedCoreAsync();
    }

    private void RenderSummary()
    {
        _wholesaleNotice.IsVisible = false;
        if (_result is not { } result) return;

        if (!result.IsAlignable)
        {
            // Said rather than hidden. These two divide the work so differently
            // that their references do not name the same passages - a fact
            // about the editions worth knowing, and much better than a list of
            // a thousand invented variants.
            _summary.Text = result.Shared == 0
                ? "These two editions share no passage references - they divide the work differently, " +
                  "so there is nothing to line up."
                : $"These two editions disagree at almost every one of the {result.Shared:N0} references " +
                  "they appear to share, which means the references are not naming the same passages. " +
                  "Not collatable.";
            return;
        }

        _summary.Text =
            $"{result.Shared:N0} shared passages:  {result.Identical:N0} identical,  " +
            $"{result.PresentationDiffers:N0} punctuation or spacing,  " +
            $"{result.OrthographyDiffers:N0} spelling,  " +
            $"{result.LineationDiffers:N0} line division,  " +
            $"{result.TextDiffers:N0} in the words." +
            (result.OnlyInLeft + result.OnlyInRight > 0
                ? $"   ({result.OnlyInLeft:N0} only on the left, {result.OnlyInRight:N0} only on the right)"
                : string.Empty);

        _wholesaleNotice.IsVisible = result.Shared > 0 && result.TextDiffers > result.Shared * Wholesale;
    }

    private void RenderRows()
    {
        _rows.Columns[2].Header = _leftHeader.Text ?? string.Empty;
        _rows.Columns[3].Header = _rightHeader.Text ?? string.Empty;

        if (_result is not { } result)
        {
            _rows.ItemsSource = null;
            ShowSelectedDetail();
            return;
        }

        var shown = result.Rows.Where(Included)
            .Select(row => new RowView(row, row.PassageRef, Describe(row.Status), row.Left ?? "—", row.Right ?? "—"))
            .ToList();

        _rows.ItemsSource = shown;
        _rows.SelectedIndex = shown.Count > 0 ? 0 : -1;
        ShowSelectedDetail();
    }

    /// <summary>
    /// The three views, narrowest first, because the narrowest is the one
    /// worth opening with: on the Aeschylus pairings roughly a fifth of the
    /// shared lines differ in the words and the rest is typography, and
    /// showing the typography by default would bury the readings under it.
    /// </summary>
    private bool Included(CollationRow row) => _showBox.SelectedIndex switch
    {
        0 => row.Status is CollationStatus.TextDiffers or CollationStatus.OnlyInLeft or CollationStatus.OnlyInRight,
        1 => row.Status != CollationStatus.Identical,
        _ => true
    };

    internal static string Describe(CollationStatus status) => status switch
    {
        CollationStatus.Identical => "identical",
        CollationStatus.PresentationDiffers => "punctuation",
        CollationStatus.OrthographyDiffers => "spelling",
        CollationStatus.LineationDiffers => "line division",
        CollationStatus.TextDiffers => "THE WORDS",
        CollationStatus.OnlyInLeft => "only on the left",
        _ => "only on the right"
    };

    /// <summary>
    /// Named for the pairing rather than the work: a work can pair up several
    /// ways, and two files called "collation-Ajax.csv" holding different
    /// comparisons would be worse than one badly named file.
    /// </summary>
    private string SuggestedFileName() =>
        _resultPair is { } pair ? $"collation-{pair.WorkTitle}-{pair.LeftVersion}-{pair.RightVersion}" : "collation";

    /// <summary>
    /// The rows as shown, filter included. What is on screen is what someone
    /// means by "export this"; writing the full comparison would hand back
    /// thousands of punctuation differences nobody asked to see, and the note
    /// above the table says which view produced the file, so it cannot be
    /// mistaken for the whole collation.
    ///
    /// Built from the rows themselves rather than the cells, because a cell
    /// shows a dash where an edition has nothing at a reference. The dash is
    /// right on screen and wrong in a file, where it would read as a passage
    /// whose text is "—" rather than as an absence.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>> ExportRows()
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Passage", "Difference", _leftHeader.Text ?? string.Empty, _rightHeader.Text ?? string.Empty }
        };

        if (_result is not { } result) return table;

        foreach (var row in result.Rows.Where(Included))
            table.Add(ExportRow(row));

        return table;
    }

    private IReadOnlyList<IReadOnlyList<string>> SelectedExportRows()
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Passage", "Difference", _leftHeader.Text ?? string.Empty, _rightHeader.Text ?? string.Empty }
        };

        // In the table's order, not the order they were clicked in.
        var selected = _rows.SelectedItems.OfType<RowView>().Select(v => v.Row).ToHashSet();
        if (_result is { } result)
        {
            foreach (var row in result.Rows.Where(r => selected.Contains(r)))
                table.Add(ExportRow(row));
        }

        return table;
    }

    private static string[] ExportRow(CollationRow row) =>
        new[] { row.PassageRef, Describe(row.Status), row.Left ?? string.Empty, row.Right ?? string.Empty };

    /// <summary>
    /// What the table means, written above it.
    ///
    /// A collation exported without this is four columns of Greek whose
    /// provenance has to be remembered - which two editions, graded how,
    /// filtered to what. The counts go in too, because the substantive figure
    /// says whether the pairing is worth trusting, and a file showing 89%
    /// without it looks like a discovery rather than a warning.
    /// </summary>
    private IReadOnlyList<string> ExportNotes()
    {
        var notes = new List<string> { $"Classica Codex collation - {DateTime.Now:yyyy-MM-dd HH:mm}" };

        if (_resultPair is { } pair)
        {
            notes.Add($"{pair.AuthorName}, {pair.WorkTitle}");
            notes.Add($"Left:  {_leftHeader.Text}   ({pair.LeftEditionUrn})");
            notes.Add($"Right: {_rightHeader.Text}   ({pair.RightEditionUrn})");
        }

        if (_result is { } result)
        {
            notes.Add(result.IsAlignable
                ? $"{result.Shared:N0} shared passages: {result.Identical:N0} identical, " +
                  $"{result.PresentationDiffers:N0} punctuation, {result.OrthographyDiffers:N0} spelling, " +
                  $"{result.LineationDiffers:N0} line division, {result.TextDiffers:N0} in the words. " +
                  $"{result.OnlyInLeft:N0} only on the left, {result.OnlyInRight:N0} only on the right."
                : "These editions are not alignable - their citation references do not name the " +
                  "same passages - so the rows below are not a collation.");

            if (result.Shared > 0 && result.TextDiffers > result.Shared * Wholesale)
            {
                notes.Add("NOTE: at this rate the two editions disagree wholesale rather than at " +
                          "points - different editors' texts, or one transcribed less carefully. " +
                          "These rows describe the distance between them; they are not a shortlist " +
                          "of cruxes.");
            }
        }

        notes.Add($"Showing: {_showBox.SelectedItem}. Differences are graded - only \"THE WORDS\" is a " +
                  "difference in what the text says. Nothing here rules on which reading is right.");

        return notes;
    }

    private void ShowSelectedDetail()
    {
        if (_rows.SelectedItem is not RowView { Row: var row })
        {
            _leftDetail.Text = _rightDetail.Text = string.Empty;
            return;
        }

        _leftDetail.Text = row.Left ?? "(this edition has nothing at this reference)";
        _rightDetail.Text = row.Right ?? "(this edition has nothing at this reference)";
    }
}
