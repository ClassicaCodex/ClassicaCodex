using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Artifacts;
using ClassicaCodex.Mac.Features.Tags;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.MythNetwork;

/// <summary>
/// The myth network: every tag the reader has made, as a graph of who
/// appears with whom. Drag nodes to untangle, click one to see its passages,
/// click a line to see what connects its two ends, right-click a node for
/// related artifacts.
///
/// The Mac counterpart of the Windows MythNetworkForm, with its two kinds of
/// co-occurrence, its proximity window and its minimum-shared slider, and
/// Auto-Tag and the category shapes a click away.
/// </summary>
internal sealed class MythNetworkWindow : FeatureWindow
{
    private readonly IReaderHost _host;
    private readonly GraphCanvas _canvas;
    private readonly Slider _thresholdSlider;
    private readonly TextBlock _thresholdValueLabel;
    private readonly ComboBox _modeComboBox;
    private readonly NumericUpDown _windowUpDown;
    private readonly ListBox _passageList;
    private readonly TextBlock _passageMessage;
    private readonly TextBlock _selectedTagLabel;
    private readonly TagRepository _tagRepo = new();

    private List<PassageItem> _currentPassages = new();
    private bool _loading;
    private int _loadGeneration;

    public MythNetworkWindow(IReaderHost host)
    {
        _host = host;
        Title = "Myth Network";
        Width = 1240;
        Height = 820;
        MinWidth = 820;
        MinHeight = 520;
        PassageResults.AddListStyles(this);

        // Built before the controls whose handlers call into it, so nothing
        // that runs later can find it missing.
        _canvas = new GraphCanvas();
        _canvas.NodeClicked += async name => await LoadPassagesAsync(name);
        _canvas.EdgeClicked += async (nameA, nameB) => await LoadEdgePassagesAsync(nameA, nameB);
        _canvas.NodeRightClicked += name => ArtifactsFeature.Open(this, name, name);

        var relayoutButton = ToolbarButton("Re-layout", "Refresh");
        relayoutButton.Click += (_, _) => _canvas.Relayout();

        var autoTagButton = ToolbarButton("Auto-Tag…", "Sparkle");
        autoTagButton.Click += (_, _) => OpenAutoTag();

        var shapesButton = ToolbarButton("Shapes…", "Sliders");
        shapesButton.Click += async (_, _) =>
        {
            await CategoryShapesWindow.ShowAsync(this, () => _canvas.InvalidateVisual());
            _canvas.InvalidateVisual();
        };

        _modeComboBox = new ComboBox
        {
            ItemsSource = new[]
            {
                "Same work (weaker, more edges)",
                "Same passage (stronger, fewer edges)"
            },
            SelectedIndex = 1, // proximity is the more useful default now that it exists
            MinWidth = 250
        };
        _modeComboBox.SelectionChanged += async (_, _) => await LoadGraphAsync();

        _windowUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 500,
            Value = 25,
            Increment = 1,
            FormatString = "0",
            Width = 110
        };
        _windowUpDown.ValueChanged += async (_, _) => await LoadGraphAsync();

        _thresholdSlider = new Slider
        {
            Minimum = 1,
            Maximum = 10,
            Value = 1,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            IsSnapToTickEnabled = true,
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center
        };
        _thresholdValueLabel = new TextBlock { Text = "1", MinWidth = 24, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        _thresholdSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            var value = (int)Math.Round(_thresholdSlider.Value);
            _thresholdValueLabel.Text = value.ToString();
            _canvas.SetMinSharedWorks(value);
        };

        // In groups, so a narrow window wraps a control together with its
        // label rather than leaving "Min shared:" at the end of one line and
        // its slider alone on the next.
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 20, LineSpacing = 8 };
        toolbar.Children.Add(Group(relayoutButton, autoTagButton, shapesButton));
        toolbar.Children.Add(Group(Label("Co-occurrence:"), _modeComboBox));
        toolbar.Children.Add(Group(Label("Window (lines):"), _windowUpDown));
        toolbar.Children.Add(Group(Label("Min shared:"), _thresholdSlider, _thresholdValueLabel));

        var legend = new TextBlock
        {
            Text = "Node size = how often you've used the tag; shape = its category. Line thickness = how strongly two tags co-occur. " +
                   "Drag nodes to untangle; click one to browse its passages, or a line to see what connects its two ends; " +
                   "right-click a node to search for related artifacts.",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 10, 2, 12)
        };

        var canvasPage = new Border { Classes = { "page" }, Child = _canvas };

        _selectedTagLabel = new TextBlock
        {
            Text = "Click a tag to see its passages here.",
            Classes = { "h3" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 8)
        };

        var (passagePage, passageList, passageMessage) = PassageResults.Page(item => item is PassageItem p
            ? PassageResults.Row(p.Source, PassageResults.RowText(p.Text), p.CitationRef, p.Milestone, maxLines: 3, prefix: p.TagName)
            : new TextBlock());
        _passageList = passageList;
        _passageMessage = passageMessage;
        _passageList.DoubleTapped += async (_, _) => await JumpToSelectedPassageAsync();
        _passageList.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await JumpToSelectedPassageAsync();
        };
        PassageResults.Menu(_passageList, PassageResults.CopyItem(_passageList, item => (item as PassageItem)?.CopyText));

        var side = new DockPanel();
        DockPanel.SetDock(_selectedTagLabel, Dock.Top);
        side.Children.Add(_selectedTagLabel);
        side.Children.Add(passagePage);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,320") };
        Grid.SetColumn(side, 2);
        body.Children.Add(canvasPage);
        body.Children.Add(side);

        var root = new DockPanel { Margin = new Thickness(18, 14, 18, 18) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(legend, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(legend);
        root.Children.Add(body);
        Content = root;

        Opened += async (_, _) => await LoadGraphAsync();
    }

    private static Button ToolbarButton(string text, string glyph) =>
        new() { Content = TagBrowserWindow.Labelled(text, glyph), VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } };

    private static StackPanel Group(params Control[] controls)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var control in controls) group.Children.Add(control);
        return group;
    }

    internal GraphCanvas Canvas => _canvas;

    internal IReadOnlyList<PassageItem> Passages => _currentPassages;

    internal string SelectedLabel => _selectedTagLabel.Text ?? string.Empty;

    internal int Threshold => (int)Math.Round(_thresholdSlider.Value);

    internal double ThresholdMaximum => _thresholdSlider.Maximum;

    internal void SetMode(bool proximity) => _modeComboBox.SelectedIndex = proximity ? 1 : 0;

    internal void SetThreshold(int value) => _thresholdSlider.Value = value;

    private void OpenAutoTag()
    {
        var autoTag = new AutoTagWindow { OnNavigate = OnNavigate };
        autoTag.TagsChanged += async () =>
        {
            await LoadGraphAsync();

            // New tags are new marks in the reader, which is beside this
            // window rather than waiting for it to close.
            await _host.RefreshPassageMarksAsync();
        };
        autoTag.Show(this);
    }

    internal async Task LoadGraphAsync()
    {
        var useProximity = _modeComboBox.SelectedIndex != 0; // the default, index 1, is proximity
        _windowUpDown.IsEnabled = useProximity;
        var generation = ++_loadGeneration;

        List<(int TagId, string Name, string? Category, int UsageCount)> nodes;
        List<(int TagId1, int TagId2, int SharedWorkCount)> edges;
        try
        {
            (nodes, edges) = useProximity
                ? await _tagRepo.GetProximityCoOccurrenceGraphAsync(WindowLines)
                : await _tagRepo.GetCoOccurrenceGraphAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't build the network", ex);
            return;
        }

        // A later change - another click on the window's arrows - has started
        // a newer load; this one's graph is already out of date.
        if (generation != _loadGeneration) return;

        _canvas.SetData(nodes, edges);

        // Sized to the data rather than fixed at 10, which turned out to be
        // far below real edge weights once encyclopedic sources (Apollodorus,
        // Ovid) are in the mix - nearly every pair of gods shares dozens of
        // works with something that mentions the whole pantheon. Sizing the
        // range to the data lets the slider reach wherever this dataset's
        // signal-vs-noise line actually falls.
        //
        // The step stays 1. Windows also spaced its tick marks out to a
        // twentieth of the range, which only changed how many marks were
        // drawn; an Avalonia slider snaps to its ticks, so the same setting
        // here would put every value between them out of reach.
        var maxWeight = _canvas.MaxEdgeWeight;
        _loading = true;
        try
        {
            _thresholdSlider.Maximum = Math.Max(maxWeight, 1);
        }
        finally
        {
            _loading = false;
        }

        // Then put the slider's value back into the canvas, because the two
        // could otherwise disagree without either of them showing it. A new
        // Maximum clamps Value down to it, and on Windows the canvas only
        // heard about a threshold when the slider was dragged - so switching
        // from a mode with heavy edges to one with light ones left the canvas
        // filtering at the old, much higher number while the slider read the
        // new, lower one: every edge gone, and nothing on screen to explain it.
        var threshold = (int)Math.Round(_thresholdSlider.Value);
        _thresholdValueLabel.Text = threshold.ToString();
        _canvas.SetMinSharedWorks(threshold);
    }

    private int WindowLines => (int)Math.Clamp(_windowUpDown.Value ?? 25, 1, 500);

    internal async Task LoadPassagesAsync(string tagName)
    {
        _selectedTagLabel.Text = $"“{tagName}” (double-click a passage to jump to it):";
        _passageList.ItemsSource = null;
        PassageResults.SetMessage(_passageMessage, null);

        try
        {
            _currentPassages = (await _tagRepo.GetByTagAsync(tagName)).Select(PassageItem.From).ToList();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the passages", ex);
            return;
        }

        _passageList.ItemsSource = _currentPassages;
        PassageResults.SetMessage(_passageMessage, _currentPassages.Count == 0 ? "No passages found." : null);
    }

    /// <summary>
    /// The passages that justify a clicked edge - the ones that actually
    /// satisfy the current kind of co-occurrence (same work, or near each other
    /// in the same edition), not just anything tagged with either name. This
    /// is what makes clicking an edge different from clicking either end.
    /// </summary>
    internal async Task LoadEdgePassagesAsync(string tagNameA, string tagNameB)
    {
        _selectedTagLabel.Text = $"“{tagNameA}” ↔ “{tagNameB}” (double-click a passage to jump to it):";
        _passageList.ItemsSource = null;
        PassageResults.SetMessage(_passageMessage, null);

        var useProximity = _modeComboBox.SelectedIndex != 0;

        try
        {
            var edgePassages = await _tagRepo.GetEdgePassagesAsync(tagNameA, tagNameB, useProximity, WindowLines);
            _currentPassages = edgePassages
                .Select(p => new PassageItem(p.WorkId, p.TextNodeId, p.AuthorName, p.WorkTitle, p.CitationRef, p.Text, p.Milestone, p.TagName))
                .ToList();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the passages", ex);
            return;
        }

        _passageList.ItemsSource = _currentPassages;
        PassageResults.SetMessage(_passageMessage, _currentPassages.Count == 0
            ? "Nothing qualified this edge - try widening the proximity window."
            : null);
    }

    private async Task JumpToSelectedPassageAsync()
    {
        if (_passageList.SelectedItem is not PassageItem passage) return;

        await NavigateAsync(passage.WorkId, passage.TextNodeId);
        Close();
    }
}
