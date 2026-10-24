using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Shell;

/// <summary>An author row in the library.</summary>
public sealed class AuthorNode
{
    public AuthorNode(Author author, string label, List<WorkNode> works)
    {
        Author = author;
        Label = label;
        Works = works;
        foreach (var work in works) work.Parent = this;
    }

    public Author Author { get; }
    public string Label { get; }
    public List<WorkNode> Works { get; }
}

/// <summary>A work row in the library.</summary>
public sealed class WorkNode
{
    public WorkNode(Work work, bool isFavorite)
    {
        Work = work;
        IsFavorite = isFavorite;
    }

    public Work Work { get; }
    public bool IsFavorite { get; }
    public AuthorNode? Parent { get; set; }
}

/// <summary>A line in the tree that is a message rather than a row: an empty filter, an empty library.</summary>
public sealed record SidebarMessage(string Text);

/// <summary>
/// The left column: the library as a tree of authors and works, or the list
/// of tools, switched by the control at its top.
///
/// The library half is the Windows tree and its filter row, behaviour for
/// behaviour - the author filter that waits for the typing to settle, the
/// star that shows only favourites, the collections menu that appears only
/// when there is more than one collection to choose between, and the empty
/// states that say which of those is responsible for an empty tree.
/// </summary>
public sealed class LibrarySidebar : UserControl
{
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly FavoriteWorkRepository _favoriteRepo = new();

    private readonly TreeView _tree;
    private readonly TextBox _filter;
    private readonly ToggleButton _favoritesOnly;
    private readonly Button _collectionsButton;
    private readonly ListBox _tools;
    private readonly Panel _libraryPanel;
    private readonly ToggleButton _libraryTab;
    private readonly ToggleButton _toolsTab;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

    private List<Author> _allAuthors = new();
    private Dictionary<int, List<Work>> _worksByAuthor = new();
    private HashSet<string> _favoriteUrns = new(StringComparer.Ordinal);
    private HashSet<int>? _collectionWorkIds;
    private readonly List<(string Key, string Title, CheckBox Box)> _collections = new();
    private bool _suppressSelection;

    public LibrarySidebar()
    {
        // The switch between the two lists, drawn as one pill with two halves.
        _libraryTab = SegmentButton("Library", "Library");
        _toolsTab = SegmentButton("Tools", "Gallery");
        _libraryTab.Click += (_, _) => ShowMode("library");
        _toolsTab.Click += (_, _) => ShowMode("tools");

        var segment = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Margin = new Thickness(12, 4, 12, 10),
            Child = new UniformGrid { Columns = 2, Children = { _libraryTab, _toolsTab } }
        };
        segment.Bind(Border.BackgroundProperty, segment.GetResourceObservable("Cc.Hover"));

        _filter = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Filter authors",
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Search"), Width = 13, Height = 13, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6 }
        };
        _filter.TextChanged += (_, _) => QueueRebuild();

        _favoritesOnly = new ToggleButton
        {
            Classes = { "toolbar" },
            Content = new PathIcon { Data = Glyphs.Get("Star") },
            Margin = new Thickness(4, 0, 0, 0)
        };
        ToolTip.SetTip(_favoritesOnly, "Show favourites only");
        _favoritesOnly.IsCheckedChanged += (_, _) =>
        {
            ((PathIcon)_favoritesOnly.Content!).Data = Glyphs.Get(_favoritesOnly.IsChecked == true ? "StarFill" : "Star");
            QueueRebuild();
        };

        _collectionsButton = new Button
        {
            Classes = { "toolbar" },
            Content = new PathIcon { Data = Glyphs.Get("Filter") },
            IsVisible = false
        };
        _collectionsButton.Click += (_, _) => FlyoutBase.ShowAttachedFlyout(_collectionsButton);

        var filterRow = new DockPanel { Margin = new Thickness(12, 0, 10, 8) };
        DockPanel.SetDock(_collectionsButton, Dock.Right);
        DockPanel.SetDock(_favoritesOnly, Dock.Right);
        filterRow.Children.Add(_collectionsButton);
        filterRow.Children.Add(_favoritesOnly);
        filterRow.Children.Add(_filter);

        _tree = new TreeView
        {
            Margin = new Thickness(6, 0, 6, 0),
            ItemTemplate = new FuncTreeDataTemplate<object>(
                (item, _) => BuildTreeRow(item),
                item => item is AuthorNode author ? author.Works : Array.Empty<object>())
        };
        _tree.SelectionChanged += OnTreeSelectionChanged;
        _tree.ContextRequested += OnTreeContextRequested;
        _tree.DoubleTapped += (_, _) =>
        {
            if (_tree.SelectedItem is AuthorNode author && _tree.TreeContainerFromItem(author) is TreeViewItem item)
                item.IsExpanded = !item.IsExpanded;
        };

        var libraryDock = new DockPanel();
        DockPanel.SetDock(filterRow, Dock.Top);
        libraryDock.Children.Add(filterRow);
        libraryDock.Children.Add(_tree);
        _libraryPanel = libraryDock;

        _tools = new ListBox
        {
            Margin = new Thickness(6, 0),
            ItemTemplate = new FuncDataTemplate<object>((item, _) => BuildToolRow(item), supportsRecycling: false)
        };
        _tools.ItemsSource = BuildToolItems();
        _tools.Tapped += (_, _) =>
        {
            if (_tools.SelectedItem is ToolEntry tool)
            {
                ToolInvoked?.Invoke(tool);
                Dispatcher.UIThread.Post(() => _tools.SelectedItem = null);
            }
        };
        _tools.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _tools.SelectedItem is ToolEntry tool) ToolInvoked?.Invoke(tool);
        };

        _status = new TextBlock { Classes = { "caption" }, Margin = new Thickness(16, 6, 16, 12), TextTrimming = TextTrimming.CharacterEllipsis };

        var content = new Grid();
        content.Children.Add(_libraryPanel);
        content.Children.Add(_tools);

        var root = new DockPanel();
        DockPanel.SetDock(segment, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(segment);
        root.Children.Add(_status);
        root.Children.Add(content);
        Content = root;

        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            Populate();
        };

        ShowMode(MacSettings.Current.SidebarMode);
    }

    /// <summary>Raised when the reader picks a work in the tree.</summary>
    public event Action<Work>? WorkSelected;

    /// <summary>Raised when a tool in the Tools list is chosen.</summary>
    public event Action<ToolEntry>? ToolInvoked;

    /// <summary>The menu for a work row, supplied by the main window, which owns the actions.</summary>
    public ContextMenu? WorkMenu { get; set; }

    /// <summary>The work selected in the tree, if a work row is selected.</summary>
    public Work? SelectedWork => (_tree.SelectedItem as WorkNode)?.Work;

    public bool IsFavorite(Work work) => _favoriteUrns.Contains(work.CtsUrn);

    public int AuthorCount => _allAuthors.Count;

    public int WorkCount => _worksByAuthor.Values.Sum(w => w.Count);

    /// <summary>Every work in the library, filtered or not.</summary>
    public IEnumerable<Work> AllWorks => _worksByAuthor.Values.SelectMany(w => w);

    /// <summary>The author's name as the tree shows it, without the collection label.</summary>
    public string AuthorNameFor(Work work) =>
        _allAuthors.FirstOrDefault(a => a.AuthorId == work.AuthorId)?.Name ?? string.Empty;

    public Work? FindWork(int workId) => AllWorks.FirstOrDefault(w => w.WorkId == workId);

    public void ShowMode(string mode)
    {
        var tools = mode == "tools";
        _libraryPanel.IsVisible = !tools;
        _tools.IsVisible = tools;
        _libraryTab.IsChecked = !tools;
        _toolsTab.IsChecked = tools;
        MacSettings.Current.SidebarMode = tools ? "tools" : "library";
    }

    public void FocusFilter()
    {
        ShowMode("library");
        _filter.Focus();
        _filter.SelectAll();
    }

    /// <summary>Reads the library afresh: after opening a library file, after setup has added to it.</summary>
    public async Task LoadAsync()
    {
        try
        {
            _allAuthors = await _authorRepo.GetAllAsync();
            _worksByAuthor = await _workRepo.GetAllGroupedByAuthorAsync();
            _favoriteUrns = await _favoriteRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            _allAuthors = new List<Author>();
            _worksByAuthor = new Dictionary<int, List<Work>>();
            await Alert.Error(TopLevel.GetTopLevel(this) as Window, "Couldn't load the library", ex.Message);
        }

        await BuildCollectionsMenuAsync();
        Populate();
    }

    /// <summary>
    /// Rebuilds the rows from what is loaded - after something a row shows has
    /// changed on a work, such as its attribution, whose italics and tooltip
    /// are decided when the row is built.
    /// </summary>
    public void RefreshRows() => Populate();

    public async Task ToggleFavoriteAsync(Work work)
    {
        if (_favoriteUrns.Contains(work.CtsUrn))
        {
            await _favoriteRepo.RemoveAsync(work.CtsUrn);
            _favoriteUrns.Remove(work.CtsUrn);
        }
        else
        {
            await _favoriteRepo.AddAsync(work.CtsUrn);
            _favoriteUrns.Add(work.CtsUrn);
        }

        Populate();
        Reveal(work.WorkId, select: true);
    }

    /// <summary>
    /// Selects a work's row, clearing whichever filters are hiding it - the
    /// favourites switch first, then the author filter - and only when the
    /// work is genuinely in the library. A filter is a statement about what
    /// the reader wants to look through, not about where they may go, so a
    /// search result in a work that isn't a favourite still arrives.
    /// </summary>
    /// <returns>False when the work is not in the library at all.</returns>
    public bool Reveal(int workId, bool select)
    {
        var node = FindNode(workId);
        if (node == null)
        {
            if (FindWork(workId) == null) return false;

            if (_favoritesOnly.IsChecked == true)
            {
                _favoritesOnly.IsChecked = false;
                Populate();
                node = FindNode(workId);
            }

            if (node == null && !string.IsNullOrEmpty(_filter.Text))
            {
                _filter.Text = string.Empty;
                Populate();
                node = FindNode(workId);
            }

            if (node == null && _collectionWorkIds != null)
            {
                foreach (var (_, _, box) in _collections) box.IsChecked = false;
                _collectionWorkIds = null;
                Populate();
                node = FindNode(workId);
            }

            if (node == null) return false;
        }

        if (node.Parent != null && _tree.TreeContainerFromItem(node.Parent) is TreeViewItem authorItem)
        {
            authorItem.IsExpanded = true;
        }

        if (select)
        {
            _suppressSelection = true;
            try
            {
                _tree.SelectedItem = node;
            }
            finally
            {
                _suppressSelection = false;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_tree.TreeContainerFromItem(node) is Control row) row.BringIntoView();
            }, DispatcherPriority.Background);
        }

        return true;
    }

    private WorkNode? FindNode(int workId) =>
        (_tree.ItemsSource as IEnumerable<object>)?
        .OfType<AuthorNode>()
        .SelectMany(a => a.Works)
        .FirstOrDefault(w => w.Work.WorkId == workId);

    private async Task BuildCollectionsMenuAsync()
    {
        _collections.Clear();
        _collectionWorkIds = null;

        List<string> keys;
        try { keys = await _editionRepo.GetCollectionsAsync(); }
        catch { keys = new List<string>(); }

        // With one collection there is nothing to choose between, and the
        // button stays hidden rather than offering a menu of one.
        _collectionsButton.IsVisible = keys.Count >= 2;
        if (keys.Count < 2) return;

        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "SHOW COLLECTIONS", Classes = { "section" }, Margin = new Thickness(4, 2, 4, 6) });

        foreach (var (key, title) in keys
                     .Select(k => (k, SetupDataSourceCatalog.DescribeCollection(k)))
                     .OrderBy(c => c.Item2, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox { Content = title };
            box.IsCheckedChanged += async (_, _) => await ApplyCollectionFilterAsync();
            _collections.Add((key, title, box));
            panel.Children.Add(box);
        }

        var showAll = new Button { Content = "Show all collections", Classes = { "link" }, Margin = new Thickness(4, 8, 4, 2) };
        showAll.Click += (_, _) =>
        {
            foreach (var (_, _, box) in _collections) box.IsChecked = false;
        };
        panel.Children.Add(showAll);

        FlyoutBase.SetAttachedFlyout(_collectionsButton, new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight });
        UpdateCollectionsTip();
    }

    private async Task ApplyCollectionFilterAsync()
    {
        var chosen = _collections.Where(c => c.Box.IsChecked == true).Select(c => c.Key).ToList();

        try
        {
            _collectionWorkIds = chosen.Count == 0 ? null : await _editionRepo.GetWorkIdsForCollectionsAsync(chosen);
        }
        catch (Exception ex)
        {
            _collectionWorkIds = null;
            await Alert.Warn(TopLevel.GetTopLevel(this) as Window, "Couldn't filter by collection", ex.Message);
        }

        UpdateCollectionsTip();
        Populate();
    }

    private void UpdateCollectionsTip()
    {
        var chosen = _collections.Count(c => c.Box.IsChecked == true);

        // A filter you cannot see is one you forget you set, so the count
        // goes where hovering will find it, and the button takes the accent.
        ToolTip.SetTip(_collectionsButton, chosen == 0
            ? $"Showing all {_collections.Count} collections - click to choose"
            : $"Showing {chosen} of {_collections.Count} collections - click to change");
        _collectionsButton.Foreground = chosen == 0 ? null : (IBrush?)Application.Current?.FindResource("Cc.Accent");
    }

    private void QueueRebuild()
    {
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    /// <summary>
    /// Builds the tree from what was last loaded. Matched on the author's own
    /// name rather than the label, so "eng" finds an author called English
    /// and not every Renaissance writer labelled "(English)".
    /// </summary>
    private void Populate()
    {
        var filter = (_filter.Text ?? string.Empty).Trim();
        var favoritesOnly = _favoritesOnly.IsChecked == true;

        var authors = filter.Length == 0
            ? _allAuthors
            : _allAuthors.Where(a => a.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var nodes = new List<object>();

        foreach (var author in authors)
        {
            if (!_worksByAuthor.TryGetValue(author.AuthorId, out var works)) continue;

            var workNodes = new List<WorkNode>();
            foreach (var work in works)
            {
                if (_collectionWorkIds != null && !_collectionWorkIds.Contains(work.WorkId)) continue;

                var isFavorite = _favoriteUrns.Contains(work.CtsUrn);
                if (favoritesOnly && !isFavorite) continue;

                workNodes.Add(new WorkNode(work, isFavorite));
            }

            // An author earns a row by having a work still showing.
            if (workNodes.Count == 0) continue;

            nodes.Add(new AuthorNode(author, LabelFor(author), workNodes));
        }

        if (nodes.Count == 0)
        {
            nodes.Add(new SidebarMessage(favoritesOnly
                ? (filter.Length > 0 ? $"No favourites matching “{filter}”" : "No favourites yet - right-click a work to add one")
                : filter.Length > 0
                    ? $"No author matching “{filter}”"
                    : _allAuthors.Count == 0
                        ? "No texts yet - open Setup from the Tools list or the File menu"
                        : "Nothing in the chosen collections"));
        }

        var selectedId = SelectedWork?.WorkId;

        _suppressSelection = true;
        try
        {
            _tree.ItemsSource = nodes;
        }
        finally
        {
            _suppressSelection = false;
        }

        if (selectedId != null) Reveal(selectedId.Value, select: true);

        var workCount = _worksByAuthor.Values.Sum(w => w.Count);
        _status.Text = _allAuthors.Count == 0 ? string.Empty : $"{_allAuthors.Count:N0} authors · {workCount:N0} works";
    }

    /// <summary>
    /// The label for an author: the name, plus the collection for anything
    /// outside the Greek and Latin classics, so King James I does not sit
    /// unexplained between Isocrates and Jerome.
    /// </summary>
    private static string LabelFor(Author author) => author.Namespace switch
    {
        "engLit" => $"{author.Name}  (English)",
        "greekLit" or "latinLit" => author.Name,
        "pdlpsci" => $"{author.Name}  (Political theory)",
        null or "" => author.Name,
        _ => $"{author.Name}  ({author.Namespace})"
    };

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (_tree.SelectedItem is WorkNode node) WorkSelected?.Invoke(node.Work);
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // The row under the pointer is selected first, as a right-click on a
        // Mac list does - otherwise the menu would act on whatever was last
        // clicked, not on what the reader right-clicked.
        var row = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (row?.DataContext is not WorkNode node || WorkMenu == null)
        {
            e.Handled = true;
            return;
        }

        _suppressSelection = true;
        try
        {
            _tree.SelectedItem = node;
        }
        finally
        {
            _suppressSelection = false;
        }

        WorkMenu.Open(row);
        e.Handled = true;
    }

    private static Control BuildTreeRow(object item)
    {
        switch (item)
        {
            case AuthorNode author:
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
                var icon = new PathIcon { Data = Glyphs.Get("Author"), Width = 13, Height = 13, Opacity = 0.55 };
                panel.Children.Add(icon);
                panel.Children.Add(new TextBlock { Text = author.Label, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
                return panel;
            }

            case WorkNode work:
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                if (work.IsFavorite)
                {
                    var star = new PathIcon { Data = Glyphs.Get("StarFill"), Width = 11, Height = 11 };
                    star.Bind(PathIcon.ForegroundProperty, star.GetResourceObservable("Cc.Gold"));
                    panel.Children.Add(star);
                }

                var title = new TextBlock { Text = work.Work.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                if (work.Work.IsDoubted)
                {
                    title.FontStyle = FontStyle.Italic;
                    ToolTip.SetTip(title, work.Work.AttributionStatus == AttributionStatus.Spurious
                        ? "Not by this author, in the library's judgement"
                        : "Attribution disputed");
                }

                panel.Children.Add(title);
                return panel;
            }

            case SidebarMessage message:
                return new TextBlock { Text = message.Text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6), MaxWidth = 220 };

            default:
                return new TextBlock();
        }
    }

    private static List<object> BuildToolItems()
    {
        var items = new List<object>();
        foreach (var group in ToolCatalog.All.GroupBy(t => t.Section))
        {
            items.Add(group.Key);
            items.AddRange(group);
        }

        return items;
    }

    private static Control BuildToolRow(object item)
    {
        if (item is ToolSection section)
        {
            return new TextBlock { Text = ToolCatalog.Title(section), Classes = { "section" }, Margin = new Thickness(6, 12, 0, 2), IsHitTestVisible = false };
        }

        if (item is not ToolEntry tool) return new TextBlock();

        var tile = Art.Tile(tool.TileKey, tool.Glyph, 34);
        tile.VerticalAlignment = VerticalAlignment.Center;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = tool.Title, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = tool.Subtitle, Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis });

        var row = new DockPanel { Margin = new Thickness(0, 2) };
        DockPanel.SetDock(tile, Dock.Left);
        tile.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(tile);
        row.Children.Add(text);
        return row;
    }

    private static ToggleButton SegmentButton(string label, string glyph)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new PathIcon { Data = Glyphs.Get(glyph), Width = 13, Height = 13 });
        content.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });

        return new ToggleButton
        {
            Content = content,
            Classes = { "segment" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(6)
        };
    }
}
