using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// Reception History - the Windows Reception Tracker. The same rare-word
/// candidates as Find Echoes, framed chronologically: authors who wrote LATER
/// than the source (candidates for quoting or imitating it) in one column,
/// authors who wrote EARLIER (candidates for the source having drawn on them)
/// in another. Classicists call the first reception history and the second
/// Quellenforschung, source criticism - both real, named subfields, both
/// usually done by hand.
///
/// The dates come from the same curated era table as the Timeline, so the
/// same gaps: an author the table does not know lands in "unknown era" rather
/// than being guessed at, and so does one from the source's own rough era,
/// which fits neither direction cleanly.
/// </summary>
internal sealed class ReceptionTrackerWindow : FeatureWindow
{
    private readonly TextNode _sourceNode;
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EchoList _laterList;
    private readonly EchoList _earlierList;
    private readonly EchoList _unknownList;
    private readonly TextBlock _laterCount = Count();
    private readonly TextBlock _earlierCount = Count();
    private readonly TextBlock _unknownCount = Count();
    private readonly TextBlock _era;

    private List<EchoHit> _later = new();
    private List<EchoHit> _earlier = new();
    private List<EchoHit> _unknown = new();

    public ReceptionTrackerWindow(TextNode sourceNode, FontFamily readingFont, string? attribution = null)
    {
        _sourceNode = sourceNode;

        Title = "Reception Tracker";
        Width = 1300;
        Height = 760;
        MinWidth = 820;
        MinHeight = 480;

        var reference = PassageCitation.Display(sourceNode.CitationRef, sourceNode.Milestone);

        // A jump leaves the window open - see EchoResultsWindow.
        Func<EchoHit, Task> jump = hit => NavigateAsync(hit.WorkId, hit.TextNodeId);
        _laterList = new EchoList(readingFont, () => $"Reception of [{reference}] - later authors") { Activated = jump };
        _earlierList = new EchoList(readingFont, () => $"Reception of [{reference}] - earlier authors") { Activated = jump };
        _unknownList = new EchoList(readingFont, () => $"Reception of [{reference}] - undated authors") { Activated = jump };

        _era = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*,14,*") };
        AddColumn(columns, 0, "Later authors (may be echoing this):", _laterCount, _laterList);
        AddColumn(columns, 2, "Earlier authors (this may echo them):", _earlierCount, _earlierList);
        AddColumn(columns, 4, "Unknown era (can't place chronologically):", _unknownCount, _unknownList);

        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 14) };
        top.Children.Add(EchoUi.SourceHeader("SOURCE", sourceNode, readingFont, attribution));
        top.Children.Add(EchoUi.Explainer(
            "The same rare-word candidates as Find Echoes, sorted by whether their author wrote before or after this one. " +
            "Candidates worth a human look, not proof of borrowing. Double-click one to open it in the reader."));
        top.Children.Add(_era);

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(columns);
        Content = layout;

        Opened += (_, _) => LoadTask = LoadAsync();
    }

    internal Task? LoadTask { get; private set; }

    internal IReadOnlyList<EchoHit> Later => _later;

    internal IReadOnlyList<EchoHit> Earlier => _earlier;

    internal IReadOnlyList<EchoHit> Unknown => _unknown;

    internal EchoList LaterList => _laterList;

    private static TextBlock Count() =>
        new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };

    private static void AddColumn(Grid grid, int column, string heading, TextBlock count, EchoList list)
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(new TextBlock { Text = heading, Classes = { "h3" }, TextTrimming = TextTrimming.CharacterEllipsis });

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(new Border { Classes = { "page" }, Child = list });

        Grid.SetColumn(dock, column);
        grid.Children.Add(dock);
    }

    private async Task LoadAsync()
    {
        foreach (var list in new[] { _laterList, _earlierList, _unknownList }) list.ShowMessage("Searching…");

        (int StartYear, int EndYear)? sourceEra;
        List<EchoHit> echoes;
        string? sourceAuthor;

        try
        {
            var source = await _textNodeRepo.GetTextNodeSourceInfoAsync(_sourceNode.TextNodeId);
            sourceAuthor = source?.AuthorName;
            sourceEra = sourceAuthor != null ? AuthorEraData.Lookup(sourceAuthor) : null;

            // Off the UI thread, as Find Echoes does.
            echoes = EchoHit.From(await Task.Run(() => _textNodeRepo.FindEchoesAsync(_sourceNode.TextNodeId)));
        }
        catch (Exception ex)
        {
            foreach (var list in new[] { _laterList, _earlierList, _unknownList })
                list.ShowMessage($"Couldn't search the library: {ex.Message}");
            return;
        }

        if (sourceEra == null)
        {
            // The source itself cannot be placed in time, so "earlier" and
            // "later" mean nothing - everything is shown ungrouped rather than
            // pretending to a chronology there is no basis for.
            _unknown = echoes;
            _later = new List<EchoHit>();
            _earlier = new List<EchoHit>();

            _era.Text = sourceAuthor == null
                ? string.Empty
                : $"{sourceAuthor} is not in the Timeline's table of dates, so nothing here can be put before or after.";
            _laterList.ShowMessage("(source author's date is unknown - can't split by chronology)");
            _earlierList.ShowMessage("(source author's date is unknown - can't split by chronology)");
            Populate(_unknownList, _unknownCount, _unknown);
            _laterCount.Text = _earlierCount.Text = string.Empty;
            return;
        }

        var (start, end) = sourceEra.Value;
        _era.Text = $"{sourceAuthor}: {AuthorEraData.FormatYear(start)} – {AuthorEraData.FormatYear(end)}, " +
                    "from the Timeline's table of dates. Each candidate is placed by its own author's start date against this one's.";

        _later = new List<EchoHit>();
        _earlier = new List<EchoHit>();
        _unknown = new List<EchoHit>();

        foreach (var echo in echoes)
        {
            var era = AuthorEraData.Lookup(echo.AuthorName);
            if (era == null) _unknown.Add(echo);
            else if (era.Value.StartYear > start) _later.Add(echo);
            else if (era.Value.StartYear < start) _earlier.Add(echo);
            else _unknown.Add(echo); // the same rough era, or the same author: a clean fit for neither
        }

        Populate(_laterList, _laterCount, _later);
        Populate(_earlierList, _earlierCount, _earlier);
        Populate(_unknownList, _unknownCount, _unknown);
    }

    private static void Populate(EchoList list, TextBlock count, List<EchoHit> items)
    {
        list.Show(items, "(none found)");
        count.Text = items.Count == 0 ? string.Empty : items.Count.ToString("N0");
    }
}
