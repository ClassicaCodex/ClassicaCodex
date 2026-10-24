using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Timeline;

/// <summary>
/// Every dated author in the library on one chronological axis, so who was
/// writing at the same time as whom can be seen at a glance. Click an author
/// for their works; double-click a work to open it in the reader.
///
/// The dates are AuthorEraData's - rough consensus estimates, not settled
/// fact - and the window says so, along with how many authors in the library
/// have no date on record and so are not on it at all.
/// </summary>
public sealed class TimelineWindow : FeatureWindow
{
    private readonly ScrollViewer _scrollHost;
    private readonly TimelineCanvas _canvas = new();
    private readonly TextBlock _statusLabel;
    private readonly TextBlock _authorTitle;
    private readonly TextBlock _authorDates;
    private readonly TextBlock _worksHint;
    private readonly TextBlock _worksEmpty;
    private readonly ListBox _workList;
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();

    private List<Work> _currentWorks = new();
    private int _request;

    public TimelineWindow()
    {
        Title = "Author Timeline";
        Width = 1200;
        Height = 800;
        MinWidth = 820;
        MinHeight = 480;

        // Room for a second line: this fills in with a count and a caveat about
        // the dates once the timeline loads, and at most widths that wraps.
        _statusLabel = new TextBlock
        {
            Text = "Loading…",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 900,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(22, 16, 22, 14)
        };

        _canvas.AuthorClicked += entry => PendingLoad = LoadWorksAsync(entry);

        _scrollHost = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _scrollHost.ScrollChanged += (_, _) => SyncViewport();
        _scrollHost.SizeChanged += (_, _) => SyncViewport();

        var timelineSurface = new Border { Classes = { "page" }, Child = _scrollHost };

        _authorTitle = new TextBlock { Classes = { "h2" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _authorDates = new TextBlock { Classes = { "caption" }, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
        _worksHint = new TextBlock
        {
            Text = "Click an author to see their works (double-click one to open it):",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 8)
        };
        _worksEmpty = new TextBlock { Text = "(no works found)", Classes = { "muted" }, IsVisible = false, Margin = new Thickness(4, 6) };

        _workList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<Work>((work, _) => BuildWorkRow(work), supportsRecycling: false)
        };
        _workList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 5)) }
        });
        _workList.DoubleTapped += async (_, _) => await OpenSelectedWorkAsync();
        _workList.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await OpenSelectedWorkAsync();
        }, handledEventsToo: true);

        var column = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*"),
            Margin = new Thickness(18, 16, 18, 14)
        };
        Row(column, new TextBlock { Text = "WORKS", Classes = { "section" }, Margin = new Thickness(0, 0, 0, 8) }, 0);
        Row(column, _authorTitle, 1);
        Row(column, _authorDates, 2);
        Row(column, _worksHint, 3);
        Row(column, _worksEmpty, 4);
        Row(column, _workList, 5);

        var side = new Border { Classes = { "page" }, Child = column, Width = 310 };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,14,Auto"),
            Margin = new Thickness(18, 0, 18, 18)
        };
        Grid.SetColumn(timelineSurface, 0);
        Grid.SetColumn(side, 2);
        body.Children.Add(timelineSurface);
        body.Children.Add(side);

        var root = new DockPanel();
        DockPanel.SetDock(_statusLabel, Dock.Top);
        root.Children.Add(_statusLabel);
        root.Children.Add(body);
        Content = root;

        Opened += (_, _) => PendingLoad = LoadTimelineAsync();
    }

    internal TimelineCanvas Canvas => _canvas;

    internal ListBox WorkList => _workList;

    internal IReadOnlyList<Work> Works => _currentWorks;

    internal string StatusText => _statusLabel.Text ?? string.Empty;

    /// <summary>The load in progress, for tests to wait on.</summary>
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    private void SyncViewport() => _canvas.SetViewport(_scrollHost.Offset.Y, _scrollHost.Viewport.Height);

    private async Task LoadTimelineAsync()
    {
        List<Author> authors;
        try
        {
            authors = await _authorRepo.GetAllAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = string.Empty;
            await ShowErrorAsync("Couldn't load the authors", ex);
            return;
        }

        var entries = new List<TimelineCanvas.TimelineEntry>();
        var unmatchedCount = 0;

        foreach (var author in authors)
        {
            var era = AuthorEraData.Lookup(author.Name);
            if (era == null)
            {
                unmatchedCount++;
                continue;
            }

            entries.Add(new TimelineCanvas.TimelineEntry
            {
                AuthorId = author.AuthorId,
                Name = author.Name,
                StartYear = era.Value.StartYear,
                EndYear = era.Value.EndYear
            });
        }

        _canvas.SetData(entries);
        _statusLabel.Text = $"{entries.Count} authors dated on the timeline below " +
                            $"({unmatchedCount} others in your library have no date on record and aren't shown). " +
                            "Dates are rough consensus estimates, not settled fact.";
        SyncViewport();
    }

    private async Task LoadWorksAsync(TimelineCanvas.TimelineEntry entry)
    {
        var request = ++_request;

        _authorTitle.Text = entry.Name;
        _authorTitle.IsVisible = true;
        _authorDates.Text = $"{AuthorEraData.FormatYear(entry.StartYear)} – {AuthorEraData.FormatYear(entry.EndYear)}";
        _authorDates.IsVisible = true;

        List<Work> works;
        try
        {
            works = await _workRepo.GetByAuthorAsync(entry.AuthorId);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the works", ex);
            return;
        }

        // A second author clicked before the first one's works arrived wins.
        if (request != _request) return;

        _currentWorks = works;
        _workList.ItemsSource = works;
        _worksEmpty.IsVisible = works.Count == 0;
    }

    private async Task OpenSelectedWorkAsync()
    {
        if (_workList.SelectedItem is not Work work || OnOpenWork == null) return;

        // The timeline stays open beside the reader, so the next author's works
        // are still a click away.
        await OnOpenWork(work.WorkId);
    }

    /// <summary>
    /// A work as the library tree shows it - italic where its attribution is
    /// disputed or rejected, with the reason on hover.
    /// </summary>
    private static Control BuildWorkRow(Work work)
    {
        var title = new TextBlock
        {
            Text = work.Title,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 3)
        };

        if (work.IsDoubted)
        {
            title.FontStyle = FontStyle.Italic;
            ToolTip.SetTip(title, work.AttributionStatus == AttributionStatus.Spurious
                ? "Not by this author, in the library's judgement"
                : "Attribution disputed");
        }

        return title;
    }

    private static void Row(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }
}
