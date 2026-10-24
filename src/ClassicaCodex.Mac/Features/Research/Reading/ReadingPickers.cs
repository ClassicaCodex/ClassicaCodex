using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Reading;

/// <summary>Chooses one of the project's evidence sources to queue for reading.</summary>
internal sealed class ReadingEvidencePickerWindow : ResearchWindow
{
    private readonly ListBox _list;

    public ReadingEvidencePickerWindow(IReadOnlyList<EvidenceItem> evidence) : base(null)
    {
        Title = "Queue an evidence source";
        Width = 620;
        Height = 440;
        MinWidth = 500;
        MinHeight = 350;

        _list = Ui.TextList<EvidenceItem>(e => e.Title, e => e.CanonicalReference ?? e.StableIdentifier);
        _list.ItemsSource = evidence.ToList();
        _list.Margin = new Thickness(12);
        _list.DoubleTapped += (_, _) => Choose();

        var cancel = Ui.Button("Cancel");
        var queue = Ui.Button("Queue Source", accent: true);
        cancel.IsCancel = true;
        queue.IsDefault = true;
        cancel.Click += (_, _) => Close();
        queue.Click += (_, _) => Choose();

        var bar = Ui.ButtonBar(null, cancel, queue);
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(_list);
        Content = dock;
    }

    /// <summary>The source chosen, or null if the reader cancelled.</summary>
    public EvidenceItem? SelectedEvidence { get; private set; }

    private void Choose()
    {
        if (_list.SelectedItem is not EvidenceItem item) return;
        SelectedEvidence = item;
        Close();
    }
}

/// <summary>Chooses a passage of the work, from any of its editions, to queue for reading.</summary>
internal sealed class PassagePickerWindow : ResearchWindow
{
    private readonly Work _work;
    private readonly ComboBox _editions = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly TextBox _filter = new() { Classes = { "search" }, PlaceholderText = "Filter citation or text" };
    private readonly DataGrid _nodes = Ui.Table();
    private List<TextNode> _allNodes = new();

    public PassagePickerWindow(Work work) : base(null)
    {
        _work = work;
        Title = $"Choose passage — {work.Title}";
        Width = 900;
        Height = 650;
        MinWidth = 700;
        MinHeight = 500;

        _nodes.Columns.Add(Ui.Column("Citation", nameof(NodeRow.Citation), 120));
        _nodes.Columns.Add(Ui.SourceColumn("Passage", nameof(NodeRow.Text)));
        _nodes.Margin = new Thickness(12, 0, 12, 10);
        Ui.OnRowDoubleTapped(_nodes, () => ChooseAsync());

        _editions.SelectionChanged += (_, _) => Run(LoadNodesAsync);
        _filter.TextChanged += (_, _) => ApplyFilter();

        // The filter is a single-line box in a window with a default button, so
        // Return would otherwise choose - frequently with nothing selected, which
        // reads to the researcher as the passage silently failing to be added.
        // Return here means "apply the filter".
        _filter.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            ApplyFilter();
        }, RoutingStrategies.Tunnel);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,10,2*"), Margin = new Thickness(12, 12, 12, 10) };
        Grid.SetColumn(_editions, 0);
        Grid.SetColumn(_filter, 2);
        top.Children.Add(_editions);
        top.Children.Add(_filter);

        var cancel = Ui.Button("Cancel");
        var choose = Ui.Button("Add to Reading Queue", "Plus", accent: true);
        cancel.IsCancel = true;
        choose.IsDefault = true;
        cancel.Click += (_, _) => Close();
        choose.Click += (_, _) => Run(ChooseAsync);

        var bar = Ui.ButtonBar(null, cancel, choose);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bar);
        dock.Children.Add(_nodes);
        Content = dock;

        Opened += (_, _) => Run(LoadEditionsAsync);
    }

    public Edition? SelectedEdition { get; private set; }
    public TextNode? SelectedNode { get; private set; }

    internal int ShownCount => (_nodes.ItemsSource as IReadOnlyCollection<NodeRow>)?.Count ?? 0;

    private async Task LoadEditionsAsync()
    {
        var editions = await new EditionRepository().GetByWorkAsync(_work.WorkId);
        _editions.ItemsSource = editions.Select(e => new Choice<Edition>(e, Describe(e))).ToList();
        if (editions.Count > 0) _editions.SelectedIndex = 0;
        else await Alert.Info(this, "Choose passage", "No ingested editions are available for this work.");
    }

    private async Task LoadNodesAsync()
    {
        if (Ui.Value<Edition>(_editions) is not { } edition) return;
        _allNodes = await new TextNodeRepository().GetByEditionAsync(edition.EditionId);
        ApplyFilter();
    }

    internal void Filter(string text) => _filter.Text = text;

    private void ApplyFilter()
    {
        var query = (_filter.Text ?? string.Empty).Trim();
        var shown = string.IsNullOrEmpty(query)
            ? _allNodes
            : _allNodes.Where(n => n.CitationRef.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                   n.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _nodes.ItemsSource = shown.Select(n => new NodeRow(n)).ToList();
    }

    internal void Pick(int index)
    {
        if (_nodes.ItemsSource is IReadOnlyList<NodeRow> shown && index < shown.Count) _nodes.SelectedItem = shown[index];
    }

    /// <summary>
    /// Never hands back a choice without a passage. The queue treats that as
    /// "cancelled" and returns without a word, so the researcher would see the
    /// window close and nothing appear in the queue.
    /// </summary>
    internal async Task ChooseAsync()
    {
        if (_nodes.SelectedItem is not NodeRow { Node: var node } || Ui.Value<Edition>(_editions) is not { } edition)
        {
            await Alert.Info(this, "Choose passage", "Choose a passage from the list first.");
            return;
        }

        SelectedEdition = edition;
        SelectedNode = node;
        Close();
    }

    /// <summary>A passage as the list shows it: its reference as a reader writes it, and its text.</summary>
    internal sealed class NodeRow
    {
        public NodeRow(TextNode node) => Node = node;

        public TextNode Node { get; }
        public string Citation => ClassicaCodex.Core.PassageCitation.Display(Node.CitationRef, Node.Milestone);
        public string Text => Node.Text;
    }

    private static string Describe(Edition edition)
    {
        var description = new List<string>();
        if (!string.IsNullOrWhiteSpace(edition.Language)) description.Add(edition.Language);
        description.Add(edition.Kind.ToString());
        if (!string.IsNullOrWhiteSpace(edition.Translator)) description.Add($"trans. {edition.Translator}");
        return $"{string.Join(" · ", description)} — {edition.CtsUrn}";
    }
}
