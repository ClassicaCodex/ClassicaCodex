using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Echoes;

/// <summary>Interactive projection of reviewed passage pairs; every visual edge drills back to its evidence.</summary>
internal sealed class IntertextualAtlasWindow : ResearchWindow
{
    private readonly ResearchProject _openingProject;
    private readonly ResearchEchoRepository _echoes = new();
    private readonly AtlasGraph _graph = new() { EmptyMessage = "No connections match these atlas filters." };

    // Archiving is how a researcher says a line of inquiry is closed, so the
    // cross-project view offers it as a separate choice rather than quietly
    // including work that has been set aside. The single-project scope always shows
    // the project that was opened, archived or not.
    private readonly ComboBox _scope = Ui.Combo(new[]
    {
        new Choice<int>(0, "This research project"),
        new Choice<int>(1, "All active projects"),
        new Choice<int>(2, "All projects, including archived")
    }, 200);
    private readonly ComboBox _review = Ui.Combo(new[]
    {
        new Choice<ResearchEchoDisposition?>(ResearchEchoDisposition.Accepted, "Accepted connections"),
        new Choice<ResearchEchoDisposition?>(ResearchEchoDisposition.Pending, "Pending candidates"),
        new Choice<ResearchEchoDisposition?>(ResearchEchoDisposition.Rejected, "Rejected candidates"),
        new Choice<ResearchEchoDisposition?>(null, "All review states")
    }, 180);
    private readonly ComboBox _view = Ui.Combo(new[]
    {
        new Choice<bool>(false, "Works network"),
        new Choice<bool>(true, "Works ↔ motifs network")
    }, 180);
    private readonly CheckBox _classifiedOnly = new() { Content = "Human-classified only", VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _connections = Ui.Table();
    private readonly TextBlock _selection = Ui.Caption("Click a node or edge to inspect its passage-level records.");
    private readonly TextBlock _status = Ui.Status();
    private List<IntertextualAtlasConnection> _all = new();
    private List<IntertextualAtlasConnection> _filtered = new();
    private bool _ready;

    public IntertextualAtlasWindow(IReaderHost? host, ResearchProject openingProject) : base(host)
    {
        _openingProject = openingProject;
        Title = $"Intertextual Atlas — {openingProject.Name}";
        Width = 1500;
        Height = 850;
        MinWidth = 1150;
        MinHeight = 650;

        var relayout = Ui.Button("Re-layout", "Refresh");
        relayout.Click += (_, _) => _graph.Relayout();
        var controls = new WrapPanel { Margin = new Thickness(16, 12, 16, 8) };
        foreach (var control in new Control[] { Label("Scope"), _scope, Label("Review"), _review, Label("View"), _view, _classifiedOnly, relayout })
        {
            control.Margin = new Thickness(0, 0, 10, 4);
            controls.Children.Add(control);
        }

        var controlsBorder = new Border { Child = controls, BorderThickness = new Thickness(0, 0, 0, 1) };
        controlsBorder.Bind(Border.BackgroundProperty, controlsBorder.GetResourceObservable("Cc.PageHeader"));
        controlsBorder.Bind(Border.BorderBrushProperty, controlsBorder.GetResourceObservable("Cc.Rule"));

        _graph.NodeClicked += FilterToNode;
        _graph.EdgeClicked += FilterToEdge;
        var graphFrame = new Border { Child = _graph, Margin = new Thickness(12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        graphFrame.Bind(Border.BorderBrushProperty, graphFrame.GetResourceObservable("Cc.Rule"));

        _connections.Columns.Add(Ui.Column("Review", nameof(AtlasRow.Review), 86));
        _connections.Columns.Add(Ui.Column("Relation", nameof(AtlasRow.Relation), 90));
        _connections.Columns.Add(Ui.Column("Project", nameof(AtlasRow.Project), 104));
        _connections.Columns.Add(Ui.Column("Source", nameof(AtlasRow.Source), 120));
        _connections.Columns.Add(Ui.Column("Target", nameof(AtlasRow.Target), 120));
        _connections.Columns.Add(Ui.Column("Motifs", nameof(AtlasRow.Motifs), minWidth: 110));
        Ui.OnRowDoubleTapped(_connections, () =>
        {
            OpenStudio();
            return Task.CompletedTask;
        });

        var studio = Ui.Button("Open Parallel Studio", "CompareTranslations");
        var investigate = Ui.Button("Investigate Selected…", "Search");
        var clear = Ui.Button("Show All Matching", "ListBullets");
        studio.Click += (_, _) => OpenStudio();
        investigate.Click += (_, _) => OpenInvestigator();
        clear.Click += (_, _) => ShowConnections(_filtered, "All connections matching the current filters.");
        var bottom = Ui.Row(studio, investigate, clear);
        bottom.Margin = new Thickness(0, 8, 0, 4);

        _selection.Margin = new Thickness(0, 12, 0, 8);
        var right = new DockPanel { Margin = new Thickness(12, 0, 16, 8) };
        DockPanel.SetDock(_selection, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        right.Children.Add(_selection);
        right.Children.Add(bottom);
        right.Children.Add(_connections);

        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,650") };
        split.ColumnDefinitions[0].MinWidth = 450;
        var rule = new Border { Classes = { "vrule" } };
        Grid.SetColumn(graphFrame, 0);
        Grid.SetColumn(rule, 1);
        Grid.SetColumn(right, 2);
        split.Children.Add(graphFrame);
        split.Children.Add(rule);
        split.Children.Add(right);
        split.Children.Add(new GridSplitter { Width = 6, Margin = new Thickness(-3, 0), ResizeDirection = GridResizeDirection.Columns, [Grid.ColumnProperty] = 1, Background = Avalonia.Media.Brushes.Transparent });

        var status = Ui.StatusBar(_status);
        var dock = new DockPanel();
        DockPanel.SetDock(controlsBorder, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(controlsBorder);
        dock.Children.Add(status);
        dock.Children.Add(split);
        Content = dock;

        _scope.SelectionChanged += (_, _) =>
        {
            if (_ready) Run(LoadAsync);
        };
        _review.SelectionChanged += (_, _) =>
        {
            if (_ready) ApplyFilters();
        };
        _view.SelectionChanged += (_, _) =>
        {
            if (_ready) ApplyFilters();
        };
        _classifiedOnly.IsCheckedChanged += (_, _) =>
        {
            if (_ready) ApplyFilters();
        };

        Opened += (_, _) =>
        {
            _ready = true;
            Run(LoadAsync);
        };
    }

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };

    private AtlasRow? SelectedRow => _connections.SelectedItem as AtlasRow;

    private bool MotifView => Ui.Value<bool>(_view);

    internal AtlasGraph Graph => _graph;
    internal IReadOnlyList<IntertextualAtlasConnection> Filtered => _filtered;
    internal int ShownCount => (_connections.ItemsSource as IReadOnlyCollection<AtlasRow>)?.Count ?? 0;
    internal string SelectionText => _selection.Text ?? string.Empty;

    internal async Task LoadAsync()
    {
        var scope = Ui.Value<int>(_scope);
        _all = await _echoes.GetAtlasConnectionsAsync(scope == 0 ? _openingProject.ResearchProjectId : null, includeArchived: scope == 2);
        ApplyFilters();
    }

    internal void SetFilters(ResearchEchoDisposition? review, bool motifs = false, bool classifiedOnly = false)
    {
        _ready = false;
        Ui.Select(_review, review);
        Ui.Select(_view, motifs);
        _classifiedOnly.IsChecked = classifiedOnly;
        _ready = true;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var disposition = Ui.Value<ResearchEchoDisposition?>(_review);
        var classifiedOnly = _classifiedOnly.IsChecked == true;
        _filtered = _all.Where(c => disposition == null || c.Result.Disposition == disposition)
            .Where(c => !classifiedOnly || c.Result.ConnectionType != ResearchEchoConnectionType.Unclassified)
            .ToList();
        BuildGraph();
        ShowConnections(_filtered, "All connections matching the current filters.");
        var accepted = _all.Count(c => c.Result.Disposition == ResearchEchoDisposition.Accepted);
        _status.Text = $"{_filtered.Count} shown • {_all.Count} saved candidate(s) in scope • {accepted} accepted. Visual lines aggregate passage-level records; click one to audit them.";
    }

    private void BuildGraph()
    {
        var nodeData = new Dictionary<string, (string Category, int Usage)>(StringComparer.OrdinalIgnoreCase);
        var edgeWeights = new Dictionary<(string A, string B), int>();

        void AddNode(string label, string category)
        {
            var usage = _filtered.Count(c => c.SourceLabel.Equals(label, StringComparison.OrdinalIgnoreCase) || c.TargetLabel.Equals(label, StringComparison.OrdinalIgnoreCase));
            nodeData[label] = (category, Math.Max(usage, 1));
        }

        void AddEdge(string first, string second)
        {
            if (first.Equals(second, StringComparison.OrdinalIgnoreCase)) return;
            var key = string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0 ? (first, second) : (second, first);
            edgeWeights[key] = edgeWeights.GetValueOrDefault(key) + 1;
        }

        if (!MotifView)
        {
            foreach (var c in _filtered)
            {
                AddNode(c.SourceLabel, "work");
                AddNode(c.TargetLabel, "work");
                AddEdge(c.SourceLabel, c.TargetLabel);
            }

            _graph.EmptyMessage = "No reviewed work-to-work connections match these filters.";
        }
        else
        {
            foreach (var c in _filtered)
            {
                foreach (var motif in c.Motifs)
                {
                    var motifLabel = "Motif: " + motif;
                    AddNode(c.SourceLabel, "work");
                    AddNode(c.TargetLabel, "work");
                    nodeData[motifLabel] = ("motif", Math.Max(nodeData.GetValueOrDefault(motifLabel).Usage + 1, 1));
                    AddEdge(c.SourceLabel, motifLabel);
                    AddEdge(c.TargetLabel, motifLabel);
                }
            }

            _graph.EmptyMessage = "No motif-labelled connections match these filters. Classify a parallel and add motif labels first.";
        }

        var ids = nodeData.Keys.Select((label, i) => (label, id: i + 1)).ToDictionary(x => x.label, x => x.id, StringComparer.OrdinalIgnoreCase);
        var nodes = nodeData.Select(n => (ids[n.Key], n.Key, (string?)n.Value.Category, n.Value.Usage)).ToList();
        var edges = edgeWeights.Select(e => (ids[e.Key.A], ids[e.Key.B], e.Value)).ToList();
        _graph.SetData(nodes, edges);
    }

    private void FilterToNode(string label)
    {
        var selected = label.StartsWith("Motif: ", StringComparison.OrdinalIgnoreCase)
            ? _filtered.Where(c => c.Motifs.Contains(label[7..], StringComparer.OrdinalIgnoreCase)).ToList()
            : _filtered.Where(c => c.SourceLabel.Equals(label, StringComparison.OrdinalIgnoreCase) || c.TargetLabel.Equals(label, StringComparison.OrdinalIgnoreCase)).ToList();
        ShowConnections(selected, label);
    }

    private void FilterToEdge(string first, string second)
    {
        var motif = new[] { first, second }.FirstOrDefault(v => v.StartsWith("Motif: ", StringComparison.OrdinalIgnoreCase));
        List<IntertextualAtlasConnection> selected;
        if (motif != null)
        {
            var work = first == motif ? second : first;
            var motifName = motif[7..];
            selected = _filtered.Where(c => c.Motifs.Contains(motifName, StringComparer.OrdinalIgnoreCase)
                                            && (c.SourceLabel.Equals(work, StringComparison.OrdinalIgnoreCase) || c.TargetLabel.Equals(work, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        else
        {
            selected = _filtered.Where(c =>
                (c.SourceLabel.Equals(first, StringComparison.OrdinalIgnoreCase) && c.TargetLabel.Equals(second, StringComparison.OrdinalIgnoreCase)) ||
                (c.SourceLabel.Equals(second, StringComparison.OrdinalIgnoreCase) && c.TargetLabel.Equals(first, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        ShowConnections(selected, $"{first} ↔ {second}");
    }

    private void ShowConnections(IEnumerable<IntertextualAtlasConnection> connections, string label)
    {
        var rows = connections.Select(c => new AtlasRow(c)).ToList();
        _connections.ItemsSource = rows;
        if (rows.Count > 0) _connections.SelectedIndex = 0;
        _selection.Text = $"{label}\n{rows.Count} passage-level record(s); double-click one for its complete audit trail.";
    }

    private void OpenStudio()
    {
        if (SelectedRow?.Connection is not { } c) return;

        // WorkId is null only for a project detached by a re-ingest, which the atlas
        // cannot navigate into anyway; 0 matches nothing rather than opening the wrong work.
        var work = new Work { WorkId = c.Project.WorkId ?? 0, Title = c.SourceWorkTitle };
        var (studio, created) = ResearchWindows.Show($"studio:{c.Result.ResearchEchoResultId}", this,
            () => new ParallelStudioWindow(Host, c.Project, work, c.SourceAuthorName, c.Investigation, c.Result));
        if (created) studio.Closed += (_, _) => Run(() => IsVisible ? LoadAsync() : Task.CompletedTask);
    }

    private void OpenInvestigator()
    {
        if (SelectedRow?.Connection is not { } connection) return;
        ResearchWindows.Show($"investigator:{connection.Result.ResearchEchoResultId}", this,
            () => new CorpusInvestigatorWindow(Host, connection.Project, connection));
    }

    private sealed class AtlasRow
    {
        public AtlasRow(IntertextualAtlasConnection connection) => Connection = connection;

        public IntertextualAtlasConnection Connection { get; }
        public string Review => Words.Of(Connection.Result.Disposition);
        public string Relation => Words.Of(Connection.Result.ConnectionType);
        public string Project => Connection.Project.Name;
        public string Source => $"{Connection.SourceWorkTitle} {PassageCitation.Display(Connection.Investigation.SourceCitationRef, Connection.Investigation.SourceMilestone)}";
        public string Target => $"{Connection.Result.TargetWorkTitle} {PassageCitation.Display(Connection.Result.TargetCitationRef, Connection.Result.TargetMilestone)}";
        public string Motifs => Connection.Result.MotifTags ?? string.Empty;
    }
}
