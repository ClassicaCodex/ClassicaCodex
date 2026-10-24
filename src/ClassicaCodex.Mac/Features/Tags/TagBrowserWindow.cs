using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Artifacts;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// Browse by tag: every tag on the left, and on the right every passage
/// carrying the chosen one, across every author - which is the point of
/// tagging at all. From here a tag can be compared across its sources,
/// deleted, or the whole set cleared.
///
/// The Mac counterpart of the Windows TagBrowserForm, with its guards and
/// its two-step confirmation for clearing everything.
/// </summary>
internal sealed class TagBrowserWindow : FeatureWindow
{
    private readonly TagRepository _tagRepo = new();
    private readonly ListBox _tagList;
    private readonly TextBlock _tagMessage;
    private readonly ListBox _resultsList;
    private readonly TextBlock _resultsMessage;
    private readonly TextBlock _resultsCount;

    private List<PassageItem> _currentResults = new();

    public TagBrowserWindow()
    {
        Title = "Browse by Tag — Cross-Reference Across Authors";
        Width = 1000;
        Height = 660;
        MinWidth = 640;
        MinHeight = 420;
        PassageResults.AddListStyles(this);

        // The tags.
        var (tagPage, tagList, tagMessage) = PassageResults.Page(item => TagRow(item));
        _tagList = tagList;
        _tagMessage = tagMessage;
        _tagList.SelectionChanged += async (_, _) => await LoadResultsAsync();
        _tagList.KeyDown += async (_, e) =>
        {
            if (e.Key is Key.Back or Key.Delete)
            {
                e.Handled = true;
                await DeleteSelectedTagAsync();
            }
        };

        PassageResults.Menu(_tagList, PassageResults.Item("Show Related Artifacts…", "Image", () =>
        {
            if (_tagList.SelectedItem is Tag tag) ArtifactsFeature.Open(this, tag.Name, tag.Name);
            return Task.CompletedTask;
        }));

        // The passages carrying the selected one.
        var (resultsPage, resultsList, resultsMessage) = PassageResults.Page(item => item is PassageItem p
            ? PassageResults.Row(p.Source, PassageResults.RowText(p.Text), p.CitationRef, p.Milestone)
            : new TextBlock());
        _resultsList = resultsList;
        _resultsMessage = resultsMessage;
        _resultsList.DoubleTapped += async (_, _) => await JumpToSelectedResultAsync();
        _resultsList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedResultAsync();
            }
        };

        PassageResults.Menu(_resultsList,
            PassageResults.CopyItem(_resultsList, item => (item as PassageItem)?.CopyText),
            PassageResults.ExportAllItem(this, () => _currentResults.Count));

        // The same passages as rows, for the questions a document cannot
        // answer - which authors a tag actually spans, how it is distributed
        // across a work - which is most of why someone tags a hundred passages.
        TableExport.Attach(
            _resultsList,
            () => _tagList.SelectedItem is Tag t ? $"tag-{t.Name}" : "tagged-passages",
            () =>
            {
                var tagName = _tagList.SelectedItem is Tag tag ? tag.Name : string.Empty;
                var table = new List<IReadOnlyList<string>> { new[] { "Tag", "Author", "Work", "Citation", "Text" } };
                foreach (var r in _currentResults)
                    table.Add(new[] { tagName, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text });
                return table;
            },
            notes: () => new[]
            {
                $"Classica Codex tagged passages - {DateTime.Now:yyyy-MM-dd HH:mm}",
                _tagList.SelectedItem is Tag t2
                    ? $"Tag: {t2.Name}   ({_currentResults.Count:N0} passages)"
                    : $"{_currentResults.Count:N0} passages.",
                "One tag, the one selected when this was written - not the whole tag set."
            });

        var compareButton = new Button { Content = Labelled("Compare Sources…", "CompareTranslations"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        compareButton.Click += async (_, _) => await CompareSelectedAsync();

        var deleteButton = new Button { Content = Labelled("Delete Tag", "Trash") };
        deleteButton.Click += async (_, _) => await DeleteSelectedTagAsync();

        var clearButton = new Button { Content = Labelled("Clear All Tags…", "Trash") };
        clearButton.Click += async (_, _) => await ClearAllTagsAsync();

        _resultsCount = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Bottom };

        var tagHeading = new StackPanel { Spacing = 2, Margin = new Thickness(2, 0, 0, 8) };
        tagHeading.Children.Add(new TextBlock { Text = "TAGS", Classes = { "section" } });
        tagHeading.Children.Add(new TextBlock { Text = "Right-click one for related artifacts", Classes = { "caption" } });

        var resultsHeading = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        DockPanel.SetDock(_resultsCount, Dock.Right);
        resultsHeading.Children.Add(_resultsCount);
        resultsHeading.Children.Add(new TextBlock
        {
            Text = "Every passage tagged with this - across every author (double-click to jump to it):",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Bottom
        });

        var resultButtons = new DockPanel();
        DockPanel.SetDock(clearButton, Dock.Right);
        resultButtons.Children.Add(clearButton);
        resultButtons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { deleteButton } });

        var grid = new Grid
        {
            Margin = new Thickness(20, 16, 20, 18),
            ColumnDefinitions = new ColumnDefinitions("240,16,*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };

        Place(grid, tagHeading, 0, 0);
        Place(grid, resultsHeading, 0, 2);
        Place(grid, tagPage, 1, 0);
        Place(grid, resultsPage, 1, 2);
        compareButton.Margin = new Thickness(0, 12, 0, 0);
        resultButtons.Margin = new Thickness(0, 12, 0, 0);
        Place(grid, compareButton, 2, 0);
        Place(grid, resultButtons, 2, 2);

        Content = grid;

        Opened += async (_, _) => await LoadTagsAsync();
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    internal static Control Labelled(string text, string glyph)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Glyphs.Icon(glyph, 14));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    private static Control TagRow(object item)
    {
        if (item is not Tag tag) return new TextBlock();

        var row = new DockPanel();
        if (!string.IsNullOrWhiteSpace(tag.Category))
        {
            var category = new TextBlock { Text = tag.Category, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(category, Dock.Right);
            row.Children.Add(category);
        }

        row.Children.Add(new TextBlock { Text = tag.Name, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>The tags, as listed; for tests.</summary>
    internal IReadOnlyList<Tag> Tags => _tagList.ItemsSource as IReadOnlyList<Tag> ?? Array.Empty<Tag>();

    /// <summary>The passages showing for the selected tag; for tests.</summary>
    internal IReadOnlyList<PassageItem> Results => _currentResults;

    internal void SelectTag(string name) =>
        _tagList.SelectedItem = Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    internal async Task LoadTagsAsync()
    {
        List<Tag> tags;
        try
        {
            tags = await _tagRepo.GetAllTagsAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the tags", ex);
            return;
        }

        _tagList.ItemsSource = tags;
        PassageResults.SetMessage(_tagMessage, tags.Count == 0
            ? "No tags yet - right-click a line in the reader to add one."
            : null);
    }

    private async Task LoadResultsAsync()
    {
        _currentResults = new List<PassageItem>();
        _resultsList.ItemsSource = null;
        _resultsCount.Text = string.Empty;
        PassageResults.SetMessage(_resultsMessage, null);

        if (_tagList.SelectedItem is not Tag tag) return;

        try
        {
            _currentResults = (await _tagRepo.GetByTagAsync(tag.Name)).Select(PassageItem.From).ToList();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the passages", ex);
            return;
        }

        // A later selection may have arrived while this one was reading.
        if (!ReferenceEquals(_tagList.SelectedItem, tag)) return;

        _resultsList.ItemsSource = _currentResults;
        _resultsCount.Text = _currentResults.Count == 1 ? "1 passage" : $"{_currentResults.Count:N0} passages";
        PassageResults.SetMessage(_resultsMessage, _currentResults.Count == 0 ? "Nothing tagged with this yet." : null);
    }

    /// <summary>
    /// Puts the reader on the passage, then closes the browser so the reader
    /// is what's left on screen.
    /// </summary>
    private async Task JumpToSelectedResultAsync()
    {
        if (_resultsList.SelectedItem is not PassageItem result) return;

        await NavigateAsync(result.WorkId, result.TextNodeId);
        Close();
    }

    internal async Task CompareSelectedAsync()
    {
        if (_tagList.SelectedItem is not Tag tag)
        {
            await Alert.Info(this, "Nothing selected", "Select a tag first.");
            return;
        }

        var compare = new CompareSourcesWindow(tag.Name) { OnNavigate = OnNavigate };
        compare.Show(this);
    }

    private async Task DeleteSelectedTagAsync()
    {
        if (_tagList.SelectedItem is not Tag tag)
        {
            await Alert.Info(this, "Nothing selected", "Select a tag first.");
            return;
        }

        if (!await Alert.Confirm(this, "Confirm Delete",
                $"Delete the tag “{tag.Name}”? This removes it from every line it's applied to. This can't be undone.",
                "Delete", "Cancel", AlertKind.Warning))
            return;

        try
        {
            await _tagRepo.DeleteTagAsync(tag.TagId);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't delete the tag", ex);
            return;
        }

        _resultsList.ItemsSource = null;
        _currentResults = new List<PassageItem>();
        await LoadTagsAsync();
    }

    private async Task ClearAllTagsAsync()
    {
        if (!await Alert.Confirm(this, "Clear All Tags",
                "Delete every tag you've created, and every line association with them?\n\n" +
                "This does not touch bookmarks or anything else - only tags. This can't be undone.",
                "Clear All Tags", "Cancel", AlertKind.Warning))
            return;

        // A second, more deliberate confirmation - this is a full wipe, not a
        // single deletion, and deserves more friction than one click.
        if (!await Alert.Confirm(this, "Are you sure?",
                "Really clear ALL tags? There is no undo for this.",
                "Clear Them All", "Cancel", AlertKind.Warning))
            return;

        try
        {
            await _tagRepo.ClearAllTagsAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't clear the tags", ex);
            return;
        }

        _resultsList.ItemsSource = null;
        _currentResults = new List<PassageItem>();
        await LoadTagsAsync();

        await Alert.Info(this, "Done", "All tags cleared.");
    }
}
