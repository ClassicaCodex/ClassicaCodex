using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// A short list of works that are reasonable to translate first, filtered to
/// what this library actually holds.
///
/// Offered beside Translate It Yourself, because that is the moment the
/// choice is made and the moment it goes wrong. See StartingPoints for why
/// the recommendations exist at all.
/// </summary>
public sealed class StartingPointsWindow : FeatureWindow
{
    private readonly ListBox _list;
    private readonly TextBlock _why;
    private readonly Button _translate;

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private List<(StartingPoints.Suggestion Suggestion, Work Work)> _available = new();

    /// <param name="onChosen">What to do with the work the reader picks: open it, and the workbench on it.</param>
    public StartingPointsWindow(Func<Work, Task> onChosen)
    {
        Title = "Where Should I Start?";
        Width = 760;
        Height = 660;
        MinWidth = 600;
        MinHeight = 540;

        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(new TextBlock { Text = "Good places to start translating", Classes = { "h1" } });
        header.Children.Add(new TextBlock
        {
            Text = "Ancient texts vary enormously in difficulty, and the library gives no sign of it. "
                   + "These are ordered easiest first, roughly the order they are taught in. "
                   + "Only works found in your library are shown.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" }
        });

        _list = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<(StartingPoints.Suggestion Suggestion, Work Work)>((entry, _) => BuildRow(entry))
        };
        _list.SelectionChanged += (_, _) => ShowWhyForSelection();
        _list.DoubleTapped += async (_, _) => await ChooseSelectedAsync(onChosen);

        var listBorder = new Border { Classes = { "page" }, Padding = new Thickness(6), Child = _list };

        // The reason sits in its own panel rather than a tooltip or a fourth
        // column: it is a sentence or two of prose and it is the part worth
        // reading, not an annotation on the row.
        _why = new TextBlock
        {
            Text = "Select a work to see why it is a good place to begin.",
            TextWrapping = TextWrapping.Wrap
        };
        var whyBorder = new Border { Classes = { "notice" }, Padding = new Thickness(14, 10), Child = _why, MinHeight = 64 };

        var hard = new TextBlock
        {
            Text = StartingPoints.HardWorksNote,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "caption" }
        };

        _translate = new Button
        {
            Content = "Translate This",
            Classes = { "accent" },
            IsDefault = true,
            IsEnabled = false,
            MinWidth = 120,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        _translate.Click += async (_, _) => await ChooseSelectedAsync(onChosen);

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(close);
        buttons.Children.Add(_translate);

        var bottom = new StackPanel { Spacing = 12, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(whyBorder);
        bottom.Children.Add(hard);
        bottom.Children.Add(buttons);

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        header.Margin = new Thickness(0, 0, 0, 14);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(bottom);
        root.Children.Add(listBorder);
        Content = root;

        Opened += async (_, _) => await LoadSuggestionsAsync();
    }

    internal ListBox List => _list;

    internal string WhyText => _why.Text ?? string.Empty;

    private static Control BuildRow((StartingPoints.Suggestion Suggestion, Work Work) entry)
    {
        var language = entry.Suggestion.Language == "grc" ? "Greek" : "Latin";

        var title = new TextBlock { Text = entry.Suggestion.Display, Classes = { "h3" }, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock
        {
            Text = $"{language}  ·  in your library as {entry.Work.Title}",
            Classes = { "caption" },
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var badge = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = language.ToUpperInvariant(), Classes = { "section" } }
        };

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(detail);

        var row = new DockPanel { Margin = new Thickness(6, 6) };
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);
        row.Children.Add(text);
        return row;
    }

    internal async Task LoadSuggestionsAsync()
    {
        try
        {
            var authors = await _authorRepo.GetAllAsync();
            var authorNames = authors.ToDictionary(a => a.AuthorId, a => a.Name);
            var worksByAuthor = await _workRepo.GetAllGroupedByAuthorAsync();

            var candidates = StartingPoints.AvailableIn(worksByAuthor, authorNames);

            // A work with no original-language edition cannot be translated
            // from, and the workbench would refuse it after the reader had
            // already committed to the choice. Checked here rather than in
            // the matcher because it costs a query per candidate, and there
            // are at most a dozen of those.
            _available = new List<(StartingPoints.Suggestion, Work)>();
            foreach (var candidate in candidates)
            {
                var editions = await _editionRepo.GetByWorkAsync(candidate.Work.WorkId);
                if (editions.Any(e => e.Kind == EditionKind.Original)) _available.Add(candidate);
            }

            _list.ItemsSource = _available;

            if (_available.Count == 0)
            {
                _why.Text = "None of the recommended works are in this library yet. "
                            + "Setup can fetch more of the corpus.";
            }
        }
        catch (Exception ex)
        {
            _why.Text = $"Couldn't read the library: {ex.Message}";
        }
    }

    private void ShowWhyForSelection()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _available.Count)
        {
            _translate.IsEnabled = false;
            return;
        }

        _why.Text = _available[_list.SelectedIndex].Suggestion.Why;
        _translate.IsEnabled = true;
    }

    private async Task ChooseSelectedAsync(Func<Work, Task> onChosen)
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _available.Count) return;

        var work = _available[_list.SelectedIndex].Work;
        Close();
        await onChosen(work);
    }
}
