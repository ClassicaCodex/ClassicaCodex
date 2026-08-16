using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Interactive projection of reviewed passage pairs; every visual edge drills back to its evidence.</summary>
public sealed class IntertextualAtlasForm : ScaledForm
{
    private readonly ResearchProject _openingProject;
    private readonly ResearchEchoRepository _echoes = new();
    private readonly GraphCanvas _graph = new() { EmptyMessage = "No connections match these atlas filters." };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _review = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _view = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _classifiedOnly = new() { Text = "Human-classified only", AutoSize = true };
    private readonly DataGridView _connections = new();
    private readonly Label _selection = new();
    private readonly Label _status = new();
    private List<IntertextualAtlasConnection> _all = [];
    private List<IntertextualAtlasConnection> _filtered = [];

    public (int WorkId, long TextNodeId)? NavigationTarget { get; private set; }
    private AtlasRow? SelectedRow => _connections.CurrentRow?.DataBoundItem as AtlasRow;
    private bool MotifView => _view.SelectedIndex == 1;

    public IntertextualAtlasForm(ResearchProject openingProject)
    {
        _openingProject = openingProject;
        Text = $"Intertextual Atlas — {openingProject.Name}";
        Width = 1500; Height = 850; MinimumSize = new Size(1150, 650); StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "MythNetwork");

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
        // Archiving is how a researcher says a line of inquiry is closed, so the
        // cross-project view offers it as a separate choice rather than quietly
        // including work that has been set aside. The single-project scope always shows
        // the project that was opened, archived or not.
        _scope.Items.AddRange(["This research project", "All active projects", "All projects, including archived"]); _scope.SelectedIndex = 0;
        _review.Items.AddRange(["Accepted connections", "Pending candidates", "Rejected candidates", "All review states"]); _review.SelectedIndex = 0;
        _view.Items.AddRange(["Works network", "Works ↔ motifs network"]); _view.SelectedIndex = 0;
        _scope.Width = 190; _review.Width = 175; _view.Width = 175; _classifiedOnly.Padding = new Padding(8, 6, 0, 0);
        var relayout = Button("Re-layout", 85); relayout.Click += (_, _) => _graph.Relayout();
        controls.Controls.AddRange([new Label { Text = "Scope", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, _scope,
            new Label { Text = "Review", AutoSize = true, Padding = new Padding(8, 7, 0, 0) }, _review,
            new Label { Text = "View", AutoSize = true, Padding = new Padding(8, 7, 0, 0) }, _view,
            _classifiedOnly, relayout]);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        _graph.Dock = DockStyle.Fill; _graph.BorderStyle = BorderStyle.FixedSingle;
        _graph.NodeClicked += FilterToNode; _graph.EdgeClicked += FilterToEdge;
        split.Panel1.Controls.Add(_graph);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _selection.Dock = DockStyle.Top; _selection.Height = 54; _selection.Padding = new Padding(5); _selection.Text = "Click a node or edge to inspect its passage-level records.";
        _connections.Dock = DockStyle.Fill; _connections.AutoGenerateColumns = false; _connections.ReadOnly = true;
        _connections.AllowUserToAddRows = false; _connections.RowHeadersVisible = false; _connections.MultiSelect = false;
        _connections.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _connections.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Review", HeaderText = "Review", Width = 72 });
        _connections.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Relation", HeaderText = "Relation", Width = 90 });
        _connections.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Project", HeaderText = "Project", Width = 120 });
        _connections.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Source", HeaderText = "Source", Width = 170 });
        _connections.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Target", HeaderText = "Target", Width = 170 });
        _connections.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Motifs", HeaderText = "Motifs", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _connections.CellDoubleClick += async (_, _) => await OpenStudioAsync();
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(6) };
        var studio = Button("Open Parallel Studio", 150); studio.Click += async (_, _) => await OpenStudioAsync();
        var investigate = Button("Investigate selected…", 150); investigate.Click += (_, _) => OpenInvestigator();
        var clear = Button("Show all matching", 125); clear.Click += (_, _) => ShowConnections(_filtered, "All connections matching the current filters.");
        bottom.Controls.AddRange([studio, investigate, clear]);
        right.Controls.Add(_connections); right.Controls.Add(_selection); right.Controls.Add(bottom); split.Panel2.Controls.Add(right);

        _status.Dock = DockStyle.Bottom; _status.Height = 25; _status.Padding = new Padding(8, 4, 0, 0);
        Controls.Add(split); Controls.Add(controls); Controls.Add(_status);
        _scope.SelectedIndexChanged += async (_, _) => await LoadAsync();
        _review.SelectedIndexChanged += (_, _) => ApplyFilters(); _view.SelectedIndexChanged += (_, _) => ApplyFilters();
        _classifiedOnly.CheckedChanged += (_, _) => ApplyFilters();
        ReadingTheme.AttachTo(this, () => { _selection.ForeColor = ReadingTheme.MutedText; _status.ForeColor = ReadingTheme.MutedText; _graph.Invalidate(); });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            var maximum = split.ClientSize.Width - 650 - split.SplitterWidth;
            if (maximum >= 450) { split.SplitterDistance = maximum; split.Panel1MinSize = 450; split.Panel2MinSize = 650; }
            await LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        _all = await _echoes.GetAtlasConnectionsAsync(
            _scope.SelectedIndex == 0 ? _openingProject.ResearchProjectId : null,
            includeArchived: _scope.SelectedIndex == 2);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var disposition = _review.SelectedIndex switch
        {
            0 => ResearchEchoDisposition.Accepted,
            1 => ResearchEchoDisposition.Pending,
            2 => ResearchEchoDisposition.Rejected,
            _ => (ResearchEchoDisposition?)null
        };
        _filtered = _all.Where(c => disposition == null || c.Result.Disposition == disposition)
            .Where(c => !_classifiedOnly.Checked || c.Result.ConnectionType != ResearchEchoConnectionType.Unclassified)
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
            foreach (var c in _filtered) { AddNode(c.SourceLabel, "work"); AddNode(c.TargetLabel, "work"); AddEdge(c.SourceLabel, c.TargetLabel); }
            _graph.EmptyMessage = "No reviewed work-to-work connections match these filters.";
        }
        else
        {
            foreach (var c in _filtered)
            foreach (var motif in c.Motifs)
            {
                var motifLabel = "Motif: " + motif;
                AddNode(c.SourceLabel, "work"); AddNode(c.TargetLabel, "work");
                nodeData[motifLabel] = ("motif", Math.Max(nodeData.GetValueOrDefault(motifLabel).Usage + 1, 1));
                AddEdge(c.SourceLabel, motifLabel); AddEdge(c.TargetLabel, motifLabel);
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
            var work = first == motif ? second : first; var motifName = motif[7..];
            selected = _filtered.Where(c => c.Motifs.Contains(motifName, StringComparer.OrdinalIgnoreCase)
                && (c.SourceLabel.Equals(work, StringComparison.OrdinalIgnoreCase) || c.TargetLabel.Equals(work, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        else selected = _filtered.Where(c =>
            (c.SourceLabel.Equals(first, StringComparison.OrdinalIgnoreCase) && c.TargetLabel.Equals(second, StringComparison.OrdinalIgnoreCase)) ||
            (c.SourceLabel.Equals(second, StringComparison.OrdinalIgnoreCase) && c.TargetLabel.Equals(first, StringComparison.OrdinalIgnoreCase))).ToList();
        ShowConnections(selected, $"{first} ↔ {second}");
    }

    private void ShowConnections(IEnumerable<IntertextualAtlasConnection> connections, string label)
    {
        var rows = connections.Select(c => new AtlasRow(c)).ToList();
        _connections.DataSource = rows; _selection.Text = $"{label}\r\n{rows.Count} passage-level record(s); double-click one for its complete audit trail.";
    }

    private async Task OpenStudioAsync()
    {
        if (SelectedRow?.Connection is not { } c) return;
        // WorkId is null only for a project detached by a re-ingest, which the atlas
        // cannot navigate into anyway; 0 matches nothing rather than opening the wrong work.
        var work = new Work { WorkId = c.Project.WorkId ?? 0, Title = c.SourceWorkTitle };
        using var studio = new ParallelPassageStudioForm(c.Project, work, c.SourceAuthorName, c.Investigation, c.Result);
        studio.ShowDialog(this);
        if (studio.NavigationTarget is { } target) { NavigationTarget = target; DialogResult = DialogResult.OK; Close(); return; }
        await LoadAsync();
    }

    private void OpenInvestigator()
    {
        if (SelectedRow?.Connection is not { } connection) return;
        using var investigator = new CorpusInvestigatorForm(connection.Project, connection);
        investigator.ShowDialog(this);
    }

    private static Button Button(string text, int width) => new() { Text = text, Width = width, Height = 28 };
    private sealed class AtlasRow
    {
        public AtlasRow(IntertextualAtlasConnection connection) { Connection = connection; }
        public IntertextualAtlasConnection Connection { get; }
        public string Review => Connection.Result.Disposition.ToString();
        public string Relation => Connection.Result.ConnectionType.ToString();
        public string Project => Connection.Project.Name;
        public string Source => $"{Connection.SourceWorkTitle} {Connection.Investigation.SourceCitationRef}";
        public string Target => $"{Connection.Result.TargetWorkTitle} {Connection.Result.TargetCitationRef}";
        public string Motifs => Connection.Result.MotifTags ?? "";
    }
}
