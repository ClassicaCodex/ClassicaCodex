using System.ComponentModel;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Lets a researcher choose which already-computed echo candidates become a saved investigation.</summary>
public sealed class ResearchEchoCaptureForm : Form
{
    private readonly EchoCaptureRequest _capture;
    private readonly long? _defaultProjectId;
    private readonly ResearchRepository _research = new();
    private readonly ResearchEchoRepository _echoes = new();
    private readonly ResearchFindingRepository _findings = new();
    private readonly ComboBox _project = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _question = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _finding = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _title = new();
    private readonly DataGridView _grid = new();
    private readonly BindingList<CandidateRow> _rows;
    private readonly Button _save = new() { Text = "Save investigation", Width = 140 };

    public long? SavedInvestigationId { get; private set; }

    public ResearchEchoCaptureForm(EchoCaptureRequest capture, long? defaultProjectId = null)
    {
        _capture = capture;
        _defaultProjectId = defaultProjectId;
        _rows = new BindingList<CandidateRow>(capture.Candidates.Select(c => new CandidateRow(c)).ToList());
        Text = "Save echo investigation";
        Width = 1050; Height = 650; MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "SimilarWorks");

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 126, Padding = new Padding(10), ColumnCount = 4, RowCount = 3 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.Controls.Add(new Label { Text = "Project", AutoSize = true }, 0, 0); top.Controls.Add(_project, 1, 0);
        top.Controls.Add(new Label { Text = "Question", AutoSize = true }, 2, 0); top.Controls.Add(_question, 3, 0);
        top.Controls.Add(new Label { Text = "Finding", AutoSize = true }, 0, 1); top.Controls.Add(_finding, 1, 1);
        top.Controls.Add(new Label { Text = "Title", AutoSize = true }, 0, 2); top.Controls.Add(_title, 1, 2);
        top.SetColumnSpan(_title, 3);
        foreach (Control control in new Control[] { _project, _question, _finding, _title }) control.Dock = DockStyle.Fill;

        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = false; _grid.DataSource = _rows;
        _grid.AllowUserToAddRows = false; _grid.RowHeadersVisible = false; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(CandidateRow.Include), HeaderText = "Save", Width = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CandidateRow.Score), HeaderText = "Score", Width = 85, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CandidateRow.Target), HeaderText = "Target", Width = 210, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CandidateRow.Citation), HeaderText = "Citation", Width = 90, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CandidateRow.Passage), HeaderText = "Passage / rationale", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
        _save.Click += async (_, _) => await SaveAsync();
        bottom.Controls.Add(cancel); bottom.Controls.Add(_save);
        Controls.Add(_grid); Controls.Add(top); Controls.Add(bottom);
        AcceptButton = _save; CancelButton = cancel;
        _title.Text = capture.Title;
        _project.SelectedIndexChanged += async (_, _) => await LoadScopesAsync();
        Load += async (_, _) => await LoadProjectsAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadProjectsAsync()
    {
        var projects = await _research.GetProjectsForWorkAsync(_capture.Source.WorkId);
        _project.DataSource = projects;
        if (_defaultProjectId is { } id)
        {
            var preferred = projects.FirstOrDefault(p => p.ResearchProjectId == id);
            if (preferred != null) _project.SelectedItem = preferred;
        }
        _save.Enabled = projects.Count > 0;
        if (projects.Count == 0)
            MessageBox.Show(this, "Create a Research Bench project for this source work first, then save the search.", "No research project");
    }

    private async Task LoadScopesAsync()
    {
        if (_project.SelectedItem is not ResearchProject project) return;
        _question.DataSource = Choice<ResearchQuestion>.WithNone(await _research.GetQuestionsAsync(project.ResearchProjectId));
        _finding.DataSource = Choice<ResearchFinding>.WithNone(await _findings.GetAsync(project.ResearchProjectId));
    }

    private async Task SaveAsync()
    {
        _grid.EndEdit();
        if (_project.SelectedItem is not ResearchProject project) return;
        if (string.IsNullOrWhiteSpace(_title.Text)) { MessageBox.Show(this, "Give the investigation a title."); return; }
        var candidates = _rows.Where(r => r.Include).Select(r => r.Candidate).ToList();
        if (candidates.Count == 0) { MessageBox.Show(this, "Select at least one candidate to save."); return; }
        var request = _capture with { Title = _title.Text.Trim(), Candidates = candidates };
        var questionId = (_question.SelectedItem as Choice<ResearchQuestion>)?.Value?.ResearchQuestionId;
        var findingId = (_finding.SelectedItem as Choice<ResearchFinding>)?.Value?.ResearchFindingId;
        var saved = await _echoes.SaveCaptureAsync(project.ResearchProjectId, questionId, findingId, request);
        SavedInvestigationId = saved.ResearchEchoInvestigationId;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class CandidateRow
    {
        public CandidateRow(EchoCaptureCandidate candidate) { Candidate = candidate; }
        public EchoCaptureCandidate Candidate { get; }
        public bool Include { get; set; } = true;
        public string Score => Candidate.ScoreLabel ?? Candidate.Score?.ToString("0.##") ?? "—";
        public string Target => $"{Candidate.AuthorName}, {Candidate.WorkTitle}";
        public string Citation => Candidate.CitationRef;
        public string Passage => Candidate.Rationale is null ? Candidate.Text : $"{Candidate.Text} — {Candidate.Rationale}";
    }

    private sealed class Choice<T> where T : class
    {
        public T? Value { get; init; }
        public string Label { get; init; } = "(none)";
        public override string ToString() => Label;
        public static List<Choice<T>> WithNone(IEnumerable<T> values) =>
            new[] { new Choice<T>() }.Concat(values.Select(v => new Choice<T> { Value = v, Label = v.ToString() ?? "" })).ToList();
    }
}
