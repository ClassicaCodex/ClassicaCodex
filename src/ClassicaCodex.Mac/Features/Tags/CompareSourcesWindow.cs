using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// Given a tag - a myth, a character, a theme - lines up the sources it
/// appears in side by side: Hesiod's, Aeschylus' and Ovid's Prometheus, all
/// visible at once. The Mac counterpart of the Windows CompareForm.
///
/// The columns wrap, where the Windows lists run on along one line: here a
/// column is something to read across from, and a line cut at the column's
/// edge would have to be scrolled to be compared at all.
/// </summary>
internal sealed class CompareSourcesWindow : FeatureWindow
{
    private readonly TagRepository _tagRepo = new();
    private readonly string _tagName;
    private readonly StackPanel _sourceList;
    private readonly TextBlock _sourceMessage;
    private readonly Grid _columnsHost;
    private readonly TextBlock _columnsMessage;
    private readonly List<(SourceKey Source, CheckBox Box)> _sources = new();

    private List<PassageItem> _allResults = new();

    public CompareSourcesWindow(string tagName)
    {
        _tagName = tagName;
        Title = $"Compare Sources — “{tagName}”";
        Width = 1120;
        Height = 760;
        MinWidth = 720;
        MinHeight = 420;
        PassageResults.AddListStyles(this);

        _sourceList = new StackPanel { Spacing = 2, Margin = new Thickness(10, 8) };
        _sourceMessage = PassageResults.Placeholder();

        var sourceGrid = new Grid();
        sourceGrid.Children.Add(new ScrollViewer { Content = _sourceList });
        sourceGrid.Children.Add(_sourceMessage);
        var sourcePage = new Border { Classes = { "page" }, Child = sourceGrid };

        var compareButton = new Button
        {
            Content = TagBrowserWindow.Labelled("Compare Selected", "CompareTranslations"),
            Classes = { "accent" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        compareButton.Click += async (_, _) => await RenderColumnsAsync();

        _columnsHost = new Grid();
        _columnsMessage = PassageResults.Placeholder();
        PassageResults.SetMessage(_columnsMessage, "Check the sources to set side by side, then Compare Selected.");

        var columnsGrid = new Grid();
        columnsGrid.Children.Add(_columnsHost);
        columnsGrid.Children.Add(_columnsMessage);
        var columnsPage = new Border { Classes = { "page" }, Child = columnsGrid };

        var instructions = new TextBlock
        {
            Text = $"Check 2 or more sources tagged “{tagName}”, then Compare:",
            Classes = { "muted" },
            Margin = new Thickness(2, 0, 0, 8)
        };

        var left = new DockPanel { Width = 280 };
        DockPanel.SetDock(compareButton, Dock.Bottom);
        left.Children.Add(compareButton);
        left.Children.Add(sourcePage);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("280,16,*") };
        Grid.SetColumn(columnsPage, 2);
        body.Children.Add(left);
        body.Children.Add(columnsPage);

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
        DockPanel.SetDock(instructions, Dock.Top);
        root.Children.Add(instructions);
        root.Children.Add(body);
        Content = root;

        Opened += async (_, _) => await LoadSourcesAsync();
    }

    /// <summary>The sources offered, in order; for tests.</summary>
    internal IReadOnlyList<string> SourceNames => _sources.Select(s => s.Source.ToString()).ToList();

    internal void Check(int index, bool value = true) => _sources[index].Box.IsChecked = value;

    internal int ColumnCount => _columnsHost.ColumnDefinitions.Count(c => c.Width.IsStar);

    internal async Task LoadSourcesAsync()
    {
        try
        {
            _allResults = (await _tagRepo.GetByTagAsync(_tagName)).Select(PassageItem.From).ToList();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the sources", ex);
            return;
        }

        _sourceList.Children.Clear();
        _sources.Clear();

        var sources = _allResults
            .Select(r => new SourceKey(r.AuthorName, r.WorkTitle))
            .Distinct()
            .OrderBy(s => s.AuthorName)
            .ThenBy(s => s.WorkTitle)
            .ToList();

        PassageResults.SetMessage(_sourceMessage, sources.Count == 0 ? "No sources found for this tag." : null);

        foreach (var source in sources)
        {
            var count = _allResults.Count(r => r.AuthorName == source.AuthorName && r.WorkTitle == source.WorkTitle);
            var label = new StackPanel { Spacing = 0 };
            label.Children.Add(new TextBlock { Text = source.WorkTitle, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = $"{source.AuthorName} · {(count == 1 ? "1 passage" : $"{count:N0} passages")}", Classes = { "caption" } });

            var box = new CheckBox { Content = label, Padding = new Thickness(6, 2), VerticalContentAlignment = VerticalAlignment.Top };
            _sources.Add((source, box));
            _sourceList.Children.Add(box);
        }
    }

    internal async Task RenderColumnsAsync()
    {
        var selected = _sources.Where(s => s.Box.IsChecked == true).Select(s => s.Source).ToList();
        if (selected.Count == 0)
        {
            await Alert.Info(this, "Nothing selected", "Check at least one source first.");
            return;
        }

        _columnsHost.Children.Clear();
        _columnsHost.ColumnDefinitions.Clear();
        PassageResults.SetMessage(_columnsMessage, null);

        for (var col = 0; col < selected.Count; col++)
        {
            var source = selected[col];
            var passages = _allResults
                .Where(r => r.AuthorName == source.AuthorName && r.WorkTitle == source.WorkTitle)
                .ToList();

            if (col > 0)
            {
                _columnsHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var divider = new Border { Classes = { "vrule" } };
                Grid.SetColumn(divider, _columnsHost.ColumnDefinitions.Count - 1);
                _columnsHost.Children.Add(divider);
            }

            _columnsHost.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var column = BuildColumn(source, passages);
            Grid.SetColumn(column, _columnsHost.ColumnDefinitions.Count - 1);
            _columnsHost.Children.Add(column);
        }
    }

    /// <summary>One source: its name across the top, a shade off the page so it reads as a label, and its passages below.</summary>
    private Control BuildColumn(SourceKey source, List<PassageItem> passages)
    {
        var header = new Border
        {
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = source.ToString(),
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("Cc.PageHeader"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Cc.Rule"));

        var list = new ListBox
        {
            Classes = { "results" },
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) => item is PassageItem p
                ? PassageResults.Row(
                    null,
                    PassageResults.RowText(p.Text, PassageResults.ComparisonRowLimit),
                    p.CitationRef, p.Milestone, maxLines: 0)
                : new TextBlock(), supportsRecycling: false),
            ItemsSource = passages
        };

        var message = PassageResults.Placeholder();
        PassageResults.SetMessage(message, passages.Count == 0
            ? "Nothing tagged with this in this source - unexpected, worth re-checking."
            : null);

        if (passages.Count > 0)
        {
            PassageResults.Menu(list, PassageResults.CopyItem(list, item => (item as PassageItem)?.CopyText));

            list.DoubleTapped += async (_, _) =>
            {
                if (list.SelectedItem is PassageItem p) await NavigateAsync(p.WorkId, p.TextNodeId);
            };
        }

        var body = new Grid();
        body.Children.Add(list);
        body.Children.Add(message);

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(body);
        return dock;
    }

    /// <summary>Author and work, kept as the pair rather than re-parsed from a display string.</summary>
    private sealed record SourceKey(string AuthorName, string WorkTitle)
    {
        public override string ToString() => $"{AuthorName}, {WorkTitle}";
    }
}
