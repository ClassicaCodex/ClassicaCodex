using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// Chooses which works a search covers - the Windows WorkPickerForm, used by
/// Word Study's Choose Texts and by the morphology search.
///
/// The two ends of the range are the obvious ones - this text, or everything
/// - but the useful answers are often in between: the three surviving plays
/// of a trilogy, or everything by one author. A checkbox could only ever
/// offer the ends.
///
/// The list is long enough that it needs filtering to be usable at all: a
/// full corpus is a couple of thousand authors and rather more works, so the
/// filter box is the primary control here and the list is what it acts on.
/// </summary>
public sealed class WorkPickerWindow : FeatureWindow
{
    /// <summary>
    /// Capped because a full corpus runs to thousands of works and a list
    /// rebuilt from all of them on every keystroke is slow. Typing narrows
    /// it; the count says when that is needed.
    /// </summary>
    private const int MaxShown = 500;

    private readonly TextBox _filterBox;
    private readonly ComboBox _showCombo;
    private readonly ListBox _list;
    private readonly CheckBox _allWorksCheck;
    private readonly TextBlock _countLabel;

    private readonly List<WorkEntry> _allWorks = new();
    private readonly HashSet<int> _selected = new();
    private List<WorkEntry> _shown = new();

    /// <summary>
    /// Which works the list shows, independent of the text filter.
    ///
    /// Picking through a couple of thousand works happens across several
    /// filter terms - type an author, tick two plays, type another author,
    /// tick three more - and by the end there is no way to see what has been
    /// accumulated, because every choice made is off screen the moment the
    /// filter moves on. Chosen answers that, and Not chosen is what makes
    /// "select all shown" usable for the remainder.
    /// </summary>
    internal enum ShowMode
    {
        All = 0,
        Chosen = 1,
        NotChosen = 2
    }

    /// <summary>
    /// Asks which texts to search, starting from <paramref name="initiallySelected"/>.
    /// The author and work lists are read here rather than held by the
    /// caller, so opening Word Study on a word costs nothing extra unless the
    /// scope is actually changed.
    /// </summary>
    /// <returns>
    /// The chosen work ids - empty for "everything", which callers pass on as
    /// no filter at all - or null if the reader cancelled.
    /// </returns>
    public static async Task<IReadOnlyCollection<int>?> ChooseAsync(Window owner, IReadOnlyCollection<int> initiallySelected)
    {
        var authors = await new AuthorRepository().GetAllAsync();
        var worksByAuthor = await new WorkRepository().GetAllGroupedByAuthorAsync();

        var picker = new WorkPickerWindow(authors, worksByAuthor, initiallySelected);
        await picker.ShowDialog(owner);
        return picker.Accepted ? picker.SelectedWorkIds : null;
    }

    public WorkPickerWindow(
        IReadOnlyList<Author> authors,
        IReadOnlyDictionary<int, List<Work>> worksByAuthor,
        IReadOnlyCollection<int> initiallySelected)
    {
        Title = "Choose Texts to Search";
        Width = 640;
        Height = 580;
        MinWidth = 520;
        MinHeight = 420;
        CanMinimize = false;
        ShowInTaskbar = false;

        foreach (var id in initiallySelected) _selected.Add(id);

        foreach (var author in authors)
        {
            if (!worksByAuthor.TryGetValue(author.AuthorId, out var works)) continue;

            foreach (var work in works)
                _allWorks.Add(new WorkEntry(work.WorkId, $"{author.Name} - {work.Title}", _selected, UpdateCount));
        }

        _allWorksCheck = new CheckBox
        {
            Content = "Search every text in the corpus",
            IsChecked = initiallySelected.Count == 0
        };

        _filterBox = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Filter by author or title",
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Search"), Width = 13, Height = 13, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6 }
        };

        _showCombo = new ComboBox
        {
            ItemsSource = new[] { "All texts", "Chosen only", "Not chosen" },
            SelectedIndex = 0,
            MinWidth = 150,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _showCombo.SelectionChanged += (_, _) => RefreshList();

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4),
            ItemTemplate = new FuncDataTemplate<WorkEntry?>((entry, _) => entry == null ? null : BuildRow(entry), supportsRecycling: false)
        };

        // Rows as close as a Finder list's. Fluent sizes an item and a checkbox
        // for a finger, and at that size a few hundred works is a lot of
        // scrolling to see what has been chosen.
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(6, 0)),
                new Setter(ListBoxItem.MinHeightProperty, 0d)
            }
        });

        // Space ticks the highlighted row, as it does a checkbox: the list is
        // as usable from the keyboard as the filter box above it.
        _list.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || _list.SelectedItem is not WorkEntry entry) return;
            entry.IsChecked = !entry.IsChecked;
            e.Handled = true;
        };

        _list.ContextMenu = BuildListMenu();

        _countLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        var useThese = new Button { Content = "Use These", Classes = { "accent" }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        useThese.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();

        _filterBox.TextChanged += (_, _) => RefreshList();
        _allWorksCheck.IsCheckedChanged += (_, _) =>
        {
            ApplyAllWorksState();
            UpdateCount();
        };

        var filterRow = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
        DockPanel.SetDock(_showCombo, Dock.Right);
        filterRow.Children.Add(_showCombo);
        filterRow.Children.Add(_filterBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(useThese);

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_countLabel);

        var listPage = new Border { Classes = { "page" }, Child = _list };

        var layout = new DockPanel { Margin = new Thickness(20, 18, 20, 18) };
        DockPanel.SetDock(_allWorksCheck, Dock.Top);
        DockPanel.SetDock(filterRow, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(_allWorksCheck);
        layout.Children.Add(filterRow);
        layout.Children.Add(bottom);
        layout.Children.Add(listPage);
        Content = layout;

        RefreshList();
        ApplyAllWorksState();

        Opened += (_, _) =>
        {
            if (_allWorksCheck.IsChecked != true) _filterBox.Focus();
        };
    }

    /// <summary>Whether the reader chose Use These rather than cancelling.</summary>
    public bool Accepted { get; private set; }

    /// <summary>
    /// The chosen work ids, or empty for "everything" - which the caller
    /// passes on as no filter at all rather than as a list of every id.
    /// </summary>
    public IReadOnlyCollection<int> SelectedWorkIds =>
        _allWorksCheck.IsChecked == true ? Array.Empty<int>() : _selected.ToList();

    // For the tests, which drive the window as a reader would.
    internal TextBox FilterBox => _filterBox;
    internal CheckBox AllWorksCheck => _allWorksCheck;
    internal IReadOnlyList<WorkEntry> ShownWorks => _shown;
    internal string CountText => _countLabel.Text ?? string.Empty;
    internal ShowMode CurrentShowMode => (ShowMode)Math.Max(_showCombo.SelectedIndex, 0);
    internal void SetShowMode(ShowMode mode) => _showCombo.SelectedIndex = (int)mode;

    internal void Accept()
    {
        Accepted = true;
        Close();
    }

    /// <summary>
    /// One work: a checkbox carrying its label. The tick is the entry's,
    /// which is the chosen set's, so a row scrolled away and back - or
    /// rebuilt by the filter - shows what was chosen rather than what the row
    /// last held.
    /// </summary>
    private static Control BuildRow(WorkEntry entry)
    {
        var box = new CheckBox
        {
            Content = new TextBlock { Text = entry.Label, TextTrimming = TextTrimming.CharacterEllipsis },
            IsChecked = entry.IsChecked,
            MinHeight = 30,
            Margin = new Thickness(4, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var syncing = false;
        box.IsCheckedChanged += (_, _) =>
        {
            if (syncing) return;
            entry.IsChecked = box.IsChecked == true;
        };

        PropertyChangedEventHandler onChanged = (_, _) =>
        {
            syncing = true;
            box.IsChecked = entry.IsChecked;
            syncing = false;
        };

        // Subscribed only while the row is on screen, so a row the list has
        // let go of is not kept alive by the entry it showed.
        box.AttachedToVisualTree += (_, _) =>
        {
            entry.PropertyChanged -= onChanged;
            entry.PropertyChanged += onChanged;
            onChanged(entry, new PropertyChangedEventArgs(nameof(WorkEntry.IsChecked)));
        };
        box.DetachedFromVisualTree += (_, _) => entry.PropertyChanged -= onChanged;

        return box;
    }

    /// <summary>
    /// Acts on what the filter is showing, not on the whole corpus -
    /// narrowing to an author and taking the lot is the reason to have this
    /// at all. "Clear every choice" is separate because it is the one that
    /// reaches beyond what you can see, and shouldn't be something you hit by
    /// aiming for the one above it.
    /// </summary>
    private ContextMenu BuildListMenu()
    {
        var menu = new ContextMenu();

        var selectShown = new MenuItem { Icon = Glyphs.Icon("Check", 14) };
        selectShown.Click += (_, _) => SetAllShown(true);

        var unselectShown = new MenuItem { Icon = Glyphs.Icon("Minus", 14) };
        unselectShown.Click += (_, _) => SetAllShown(false);

        var clearAll = new MenuItem { Header = "Clear Every Choice", Icon = Glyphs.Icon("Trash", 14) };
        clearAll.Click += (_, _) =>
        {
            _selected.Clear();
            RefreshList();
        };

        menu.Items.Add(selectShown);
        menu.Items.Add(unselectShown);
        menu.Items.Add(new Separator());
        menu.Items.Add(clearAll);

        menu.Opening += (_, e) =>
        {
            // Nothing to act on when the filter has matched nothing, or when
            // every text is being searched and the list is out of play.
            if (_shown.Count == 0 || _allWorksCheck.IsChecked == true)
            {
                e.Cancel = true;
                return;
            }

            selectShown.Header = $"Select All {_shown.Count:N0} Shown";
            unselectShown.Header = $"Unselect All {_shown.Count:N0} Shown";
            clearAll.IsEnabled = _selected.Count > 0;
        };

        return menu;
    }

    private void ApplyAllWorksState()
    {
        var everything = _allWorksCheck.IsChecked == true;
        _filterBox.IsEnabled = !everything;
        _showCombo.IsEnabled = !everything;
        _list.IsEnabled = !everything;
        _list.Opacity = everything ? 0.55 : 1;
    }

    /// <summary>
    /// Rebuilds the visible list for the current filter and show mode. Ticks
    /// come from what has been chosen rather than from what the list held
    /// before, so typing in the filter can never silently untick a work it
    /// hid.
    ///
    /// The two narrowings compose: "Chosen only" with an author typed shows
    /// what has been picked from that author, not everything picked.
    /// </summary>
    private void RefreshList()
    {
        var filter = (_filterBox.Text ?? string.Empty).Trim();

        IEnumerable<WorkEntry> matching = _allWorks;

        if (filter.Length > 0)
            matching = matching.Where(w => w.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

        matching = CurrentShowMode switch
        {
            ShowMode.Chosen => matching.Where(w => _selected.Contains(w.WorkId)),
            ShowMode.NotChosen => matching.Where(w => !_selected.Contains(w.WorkId)),
            _ => matching
        };

        var matched = matching.ToList();
        _shown = matched.Take(MaxShown).ToList();
        _list.ItemsSource = _shown;

        // Entries keep their own tick; a rebuilt list after a bulk change has
        // to be told the ticks moved under it.
        foreach (var entry in _shown) entry.Refresh();

        UpdateCount(matched.Count);
    }

    /// <summary>
    /// Ticks or unticks everything the filter is currently showing.
    ///
    /// A bulk change makes the list wrong under the filtered modes -
    /// everything just unticked is still listed under "Chosen only" - so it
    /// is rebuilt. Deliberately not done for a single tick: an item vanishing
    /// from under the pointer as it is clicked makes the next click land on
    /// something else.
    /// </summary>
    internal void SetAllShown(bool chosen)
    {
        foreach (var entry in _shown) entry.SetQuietly(chosen);

        if (CurrentShowMode != ShowMode.All)
        {
            RefreshList();
            return;
        }

        foreach (var entry in _shown) entry.Refresh();
        UpdateCount();
    }

    private void UpdateCount() => UpdateCount(null);

    private void UpdateCount(int? matchingCount)
    {
        if (_allWorksCheck.IsChecked == true)
        {
            _countLabel.Text = "Every text in the corpus.";
            return;
        }

        var shown = matchingCount != null && matchingCount > _shown.Count
            ? $"  ({_shown.Count:N0} of {matchingCount:N0} shown - keep typing to narrow)"
            : string.Empty;

        _countLabel.Text = _selected.Count == 0
            ? $"No texts chosen - nothing will be searched.{shown}"
            : $"{_selected.Count:N0} text(s) chosen.{shown}";
    }

    /// <summary>
    /// A work in the list. Its tick is recorded in the chosen set as it
    /// happens rather than read off the list at the end - the list only ever
    /// holds the works matching the current filter, so anything filtered out
    /// would otherwise be silently unticked by the act of typing.
    /// </summary>
    internal sealed class WorkEntry : INotifyPropertyChanged
    {
        private readonly HashSet<int> _chosen;
        private readonly Action _changed;

        public WorkEntry(int workId, string label, HashSet<int> chosen, Action changed)
        {
            WorkId = workId;
            Label = label;
            _chosen = chosen;
            _changed = changed;
        }

        public int WorkId { get; }

        public string Label { get; }

        public bool IsChecked
        {
            get => _chosen.Contains(WorkId);
            set
            {
                if (!SetQuietly(value)) return;
                Refresh();
                _changed();
            }
        }

        /// <summary>Records the tick without telling anyone - for bulk changes, which tell once at the end.</summary>
        public bool SetQuietly(bool value) => value ? _chosen.Add(WorkId) : _chosen.Remove(WorkId);

        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));

        public override string ToString() => Label;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
