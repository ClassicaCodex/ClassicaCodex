using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Features.Research;

/// <summary>A column of a <see cref="CheckTable{T}"/>: its header, its width and what it shows.</summary>
internal sealed record CheckColumn<T>(string Header, GridLength Width, Func<T, string> Text, bool Wrap = false, bool Source = false);

/// <summary>
/// A list of candidates with a box to tick on each - the accept step of every
/// AI and import surface in the research area, where nothing is written until
/// the researcher has chosen it.
///
/// Rows rather than a grid of cells, because a candidate's corpus text and
/// rationale are a few lines long and are the thing being judged: a grid cell
/// would cut them to one line.
/// </summary>
internal sealed class CheckTable<T> : UserControl where T : class
{
    private sealed class Row
    {
        public Row(T item, bool include, bool enabled)
        {
            Item = item;
            Include = include;
            Enabled = enabled;
        }

        public T Item { get; }
        public bool Include { get; set; }
        public bool Enabled { get; set; }
    }

    private readonly string _checkHeader;
    private readonly IReadOnlyList<CheckColumn<T>> _columns;
    private readonly ListBox _list;
    private List<Row> _rows = new();
    private Func<T, bool>? _canCheck;

    public CheckTable(string checkHeader, params CheckColumn<T>[] columns)
    {
        _checkHeader = checkHeader;
        _columns = columns;

        var header = BuildGrid();
        AddCell(header, 0, new TextBlock { Text = checkHeader, Classes = { "caption" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i < columns.Length; i++)
            AddCell(header, i + 1, new TextBlock { Text = columns[i].Header, Classes = { "caption" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var headerBorder = new Border { Child = header, Padding = new Thickness(10, 7), BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Cc.PageHeader"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Cc.Rule"));

        _list = Ui.Compact(new ListBox
        {
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => row == null ? new TextBlock() : BuildRow(row), supportsRecycling: false)
        }, 6);
        _list.SelectionChanged += (_, _) => SelectionChanged?.Invoke();

        var dock = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        dock.Children.Add(headerBorder);
        dock.Children.Add(_list);

        var frame = new Border { Child = dock, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Cc.Rule"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Cc.Page"));
        Content = frame;
    }

    /// <summary>Raised when a box is ticked or cleared.</summary>
    public event Action? CheckedChanged;

    /// <summary>Raised when the highlighted row changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// Fills the table. <paramref name="initiallyChecked"/> says which rows start
    /// ticked; <paramref name="canCheck"/> which may be ticked at all - a
    /// duplicate or an already-imported record shows, but cannot be chosen.
    /// </summary>
    public void SetItems(IEnumerable<T> items, Func<T, bool>? initiallyChecked = null, Func<T, bool>? canCheck = null)
    {
        _canCheck = canCheck;
        _rows = items.Select(i =>
        {
            var enabled = canCheck?.Invoke(i) ?? true;
            return new Row(i, enabled && (initiallyChecked?.Invoke(i) ?? false), enabled);
        }).ToList();
        _list.ItemsSource = _rows;
        if (_rows.Count > 0) _list.SelectedIndex = 0;
    }

    public IReadOnlyList<T> Items => _rows.Select(r => r.Item).ToList();

    public IReadOnlyList<T> Checked => _rows.Where(r => r.Include && r.Enabled).Select(r => r.Item).ToList();

    public T? SelectedItem => (_list.SelectedItem as Row)?.Item;

    public void SetChecked(T item, bool include)
    {
        var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Item, item));
        if (row == null || !row.Enabled) return;
        row.Include = include;
        Refresh();
    }

    public void CheckAll()
    {
        foreach (var row in _rows.Where(r => r.Enabled)) row.Include = true;
        Refresh();
        CheckedChanged?.Invoke();
    }

    /// <summary>Redraws the rows after their items changed - an import marking rows done.</summary>
    public void Refresh()
    {
        var selected = _list.SelectedIndex;
        foreach (var row in _rows)
        {
            row.Enabled = _canCheck?.Invoke(row.Item) ?? true;
            if (!row.Enabled) row.Include = false;
        }

        _list.ItemsSource = null;
        _list.ItemsSource = _rows;
        if (selected >= 0 && selected < _rows.Count) _list.SelectedIndex = selected;
    }

    private Grid BuildGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(48, _checkHeader.Length * 8 + 12), GridUnitType.Pixel));
        foreach (var column in _columns) grid.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
        return grid;
    }

    private static void AddCell(Grid grid, int column, Control control)
    {
        control.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private Control BuildRow(Row row)
    {
        var grid = BuildGrid();
        var box = new CheckBox { IsChecked = row.Include, IsEnabled = row.Enabled, VerticalAlignment = VerticalAlignment.Top, MinWidth = 0, Padding = new Thickness(0) };
        box.IsCheckedChanged += (_, _) =>
        {
            row.Include = box.IsChecked == true;
            CheckedChanged?.Invoke();
        };
        AddCell(grid, 0, box);

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var text = new TextBlock
            {
                Text = column.Text(row.Item),
                TextWrapping = column.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = column.Wrap ? 5 : 1,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0)
            };
            if (column.Source) text.Classes.Add("source");
            if (!row.Enabled) text.Classes.Add("faint");
            ToolTip.SetTip(text, column.Wrap ? column.Text(row.Item) : null);
            AddCell(grid, i + 1, text);
        }

        return grid;
    }
}
