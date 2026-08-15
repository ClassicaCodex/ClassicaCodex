using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// An offline workspace where evidence is reviewed rather than silently
/// promoted into a finding. The selected work supplies context; projects,
/// questions and evidence retain their own durable records.
/// </summary>
public class ResearchBenchForm : Form
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly ResearchRepository _repo = new();
    private readonly ListBox _projects = new();
    private readonly TextBox _theory = new();
    private readonly ComboBox _projectStatus = new();
    private readonly TextBox _projectNotes = new();
    private readonly ListBox _questions = new();
    private readonly DataGridView _evidence = new();
    private readonly TextBox _title = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _question = new();
    private readonly ComboBox _judgment = new();
    private readonly ComboBox _relationship = new();
    private readonly TextBox _sourceType = new();
    private readonly TextBox _stableId = new();
    private readonly TextBox _reference = new();
    private readonly TextBox _provenance = new();
    private readonly TextBox _excerpt = new();
    private readonly TextBox _researcherNote = new();
    private readonly Label _statusLine = new();
    private SplitContainer? _outerSplit;
    private SplitContainer? _rightSplit;
    private EvidenceItem? _editingEvidence;

    private ResearchProject? CurrentProject => _projects.SelectedItem as ResearchProject;
    private EvidenceItem? CurrentEvidence => _evidence.CurrentRow?.DataBoundItem as EvidenceItem;

    public ResearchBenchForm(Work work, string authorName)
    {
        _work = work;
        _authorName = authorName;
        Text = $"Research Bench — {work.Title}";
        AppIcons.ApplyWindowIcon(this, "WordStudy");
        Width = 1500;
        Height = 850;
        MinimumSize = new Size(1300, 650);
        StartPosition = FormStartPosition.CenterParent;

        var header = BuildHeader();
        var body = BuildBody();
        _statusLine.Dock = DockStyle.Bottom;
        _statusLine.Height = 24;
        _statusLine.Padding = new Padding(8, 4, 0, 0);

        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(_statusLine);

        ReadingTheme.AttachTo(this, () =>
        {
            _statusLine.ForeColor = ReadingTheme.MutedText;
        });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            ConfigureSplitter(_outerSplit, 285, 240, 700);
            ConfigureSplitter(_rightSplit, 500, 360, 420);
            await LoadProjectsAsync();
        };
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 116, Padding = new Padding(10) };
        var attribution = _work.AttributionStatus switch
        {
            ClassicaCodex.Core.AttributionStatus.Disputed => "attribution disputed",
            ClassicaCodex.Core.AttributionStatus.Spurious => "not attributed to this author",
            _ => "accepted attribution"
        };
        var workLabel = new Label
        {
            Text = $"{_authorName}, {_work.Title}  •  {_work.CtsUrn}  •  {attribution}",
            Left = 10, Top = 8, Width = 1100, Height = 22,
            Font = new Font(Font, FontStyle.Bold)
        };
        _theory.SetBounds(10, 38, 770, 26);
        _theory.PlaceholderText = "Working theory or research question";
        _projectStatus.SetBounds(790, 38, 135, 26);
        _projectStatus.DropDownStyle = ComboBoxStyle.DropDownList;
        _projectStatus.DataSource = Enum.GetValues<ResearchProjectStatus>();
        var save = ButtonAt("Save project", 935, 37, 110);
        save.Click += async (_, _) => await SaveProjectAsync();
        var create = ButtonAt("New project", 1053, 37, 110);
        create.Click += async (_, _) => await NewProjectAsync();
        var archive = ButtonAt("Archive", 1171, 37, 90);
        archive.Click += async (_, _) => await ArchiveProjectAsync();
        _projectNotes.SetBounds(10, 72, 1251, 30);
        _projectNotes.PlaceholderText = "Project-level notes, scope, or current judgment";
        _projectNotes.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.AddRange(new Control[] { workLabel, _theory, _projectStatus, save, create, archive, _projectNotes });
        return panel;
    }

    private Control BuildBody()
    {
        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1
        };
        _outerSplit = outer;
        BuildLeft(outer.Panel1);
        var right = new SplitContainer
        {
            Dock = DockStyle.Fill
        };
        _rightSplit = right;
        BuildEvidenceList(right.Panel1);
        BuildInspector(right.Panel2);
        outer.Panel2.Controls.Add(right);
        return outer;
    }

    private static void ConfigureSplitter(
        SplitContainer? split,
        int preferredDistance,
        int panel1Minimum,
        int panel2Minimum)
    {
        if (split is null)
            return;

        // SplitContainers still have their small default width while this form's
        // control tree is being constructed. Both minimum-size setters validate
        // the current distance immediately, so defer the entire configuration.
        var maximum = split.ClientSize.Width - panel2Minimum - split.SplitterWidth;
        if (maximum < panel1Minimum)
            return;

        split.SplitterDistance = Math.Clamp(preferredDistance, panel1Minimum, maximum);
        split.Panel1MinSize = panel1Minimum;
        split.Panel2MinSize = panel2Minimum;
    }

    private void BuildLeft(Control host)
    {
        var projectLabel = LabelAt("Projects for this work", 8, 8, 250);
        _projects.SetBounds(8, 32, 268, 170);
        _projects.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _projects.SelectedIndexChanged += async (_, _) => await ProjectChangedAsync();
        var questionLabel = LabelAt("Research questions", 8, 218, 250);
        _questions.SetBounds(8, 242, 268, 340);
        _questions.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        var add = ButtonAt("Add", 8, 590, 58);
        var edit = ButtonAt("Edit", 70, 590, 58);
        var remove = ButtonAt("Remove", 132, 590, 70);
        var up = ButtonAt("↑", 206, 590, 32);
        var down = ButtonAt("↓", 242, 590, 32);
        foreach (var b in new[] { add, edit, remove, up, down })
            b.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        add.Click += async (_, _) => await AddQuestionAsync();
        edit.Click += async (_, _) => await EditQuestionAsync();
        remove.Click += async (_, _) => await RemoveQuestionAsync();
        up.Click += async (_, _) => await MoveQuestionAsync(-1);
        down.Click += async (_, _) => await MoveQuestionAsync(1);
        host.Controls.AddRange(new Control[] { projectLabel, _projects, questionLabel, _questions, add, edit, remove, up, down });
    }

    private void BuildEvidenceList(Control host)
    {
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4) };
        var add = new Button { Text = "New evidence", Width = 105, Height = 28 };
        var remove = new Button { Text = "Remove", Width = 78, Height = 28 };
        add.Click += (_, _) => NewEvidence();
        remove.Click += async (_, _) => await RemoveEvidenceAsync();
        strip.Controls.Add(add);
        strip.Controls.Add(remove);

        _evidence.Dock = DockStyle.Fill;
        _evidence.AutoGenerateColumns = false;
        _evidence.AllowUserToAddRows = false;
        _evidence.AllowUserToDeleteRows = false;
        _evidence.ReadOnly = true;
        _evidence.MultiSelect = false;
        _evidence.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _evidence.RowHeadersVisible = false;
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Judgment", HeaderText = "Review", Width = 78 });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Title", HeaderText = "Evidence", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Type", HeaderText = "Type", Width = 95 });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CanonicalReference", HeaderText = "Reference", Width = 110 });
        _evidence.SelectionChanged += (_, _) => ShowEvidence(CurrentEvidence);
        host.Controls.Add(_evidence);
        host.Controls.Add(strip);
    }

    private void BuildInspector(Control host)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 8;
        AddField(scroll, "Evidence title", _title, ref y);
        AddComboField(scroll, "Evidence type", _type, Enum.GetValues<EvidenceType>(), ref y);
        AddComboField(scroll, "Research question", _question, Array.Empty<object>(), ref y);
        AddComboField(scroll, "Relationship", _relationship, Enum.GetValues<EvidenceRelationship>(), ref y);
        AddComboField(scroll, "Human review", _judgment, Enum.GetValues<EvidenceJudgment>(), ref y);
        AddField(scroll, "Source system/type (CTS, DOI, museum, inscription…)", _sourceType, ref y);
        AddField(scroll, "Stable identifier", _stableId, ref y);
        AddField(scroll, "Canonical reference / page / passage", _reference, ref y);
        AddArea(scroll, "Source and provenance details", _provenance, 62, ref y);
        AddArea(scroll, "Raw excerpt or factual summary", _excerpt, 105, ref y);
        AddArea(scroll, "Researcher note / interpretation", _researcherNote, 90, ref y);
        var save = ButtonAt("Save evidence", 10, y + 4, 120);
        save.Click += async (_, _) => await SaveEvidenceAsync();
        scroll.Controls.Add(save);
        host.Controls.Add(scroll);
    }

    private async Task LoadProjectsAsync(long selectId = 0)
    {
        var items = await _repo.GetProjectsForWorkAsync(_work.WorkId);
        _projects.DataSource = null;
        _projects.DataSource = items;
        if (selectId != 0)
            _projects.SelectedItem = items.FirstOrDefault(p => p.ResearchProjectId == selectId);
        if (items.Count == 0)
        {
            ClearProject();
            _statusLine.Text = "No projects yet. Create one from a working theory.";
        }
    }

    private async Task ProjectChangedAsync()
    {
        var project = CurrentProject;
        if (project == null) { ClearProject(); return; }
        _theory.Text = project.Name;
        _projectStatus.SelectedItem = project.Status;
        _projectNotes.Text = project.Notes ?? "";
        var questions = await _repo.GetQuestionsAsync(project.ResearchProjectId);
        _questions.DataSource = questions;
        RefreshQuestionChoices(questions);
        await LoadEvidenceAsync(project.ResearchProjectId);
        _statusLine.Text = $"Opened {project.Name} — last updated {project.UpdatedUtc.ToLocalTime():g}.";
    }

    private async Task NewProjectAsync()
    {
        var name = TextPromptForm.Ask(this, "New research project", "Working theory or research question:");
        if (name == null) return;
        var project = new ResearchProject { WorkId = _work.WorkId, Name = name };
        await _repo.SaveProjectAsync(project);
        await LoadProjectsAsync(project.ResearchProjectId);
    }

    private async Task SaveProjectAsync()
    {
        var project = CurrentProject;
        if (project == null) { await NewProjectAsync(); return; }
        if (string.IsNullOrWhiteSpace(_theory.Text)) { MessageBox.Show(this, "Enter a working theory."); return; }
        project.Name = _theory.Text.Trim();
        project.Status = (ResearchProjectStatus)_projectStatus.SelectedItem!;
        project.Notes = EmptyToNull(_projectNotes.Text);
        await _repo.SaveProjectAsync(project);
        await LoadProjectsAsync(project.ResearchProjectId);
        _statusLine.Text = "Project saved.";
    }

    private async Task ArchiveProjectAsync()
    {
        var project = CurrentProject;
        if (project == null) return;
        if (MessageBox.Show(this, $"Archive “{project.Name}”? Its questions and evidence will be retained.",
                "Archive project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await _repo.ArchiveProjectAsync(project.ResearchProjectId);
        await LoadProjectsAsync();
    }

    private async Task AddQuestionAsync()
    {
        var project = CurrentProject;
        if (project == null) return;
        var text = TextPromptForm.Ask(this, "Research question", "What needs to be established?");
        if (text == null) return;
        await _repo.SaveQuestionAsync(new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId, Text = text, SortOrder = _questions.Items.Count
        });
        await ReloadQuestionsAsync();
    }

    private async Task EditQuestionAsync()
    {
        if (_questions.SelectedItem is not ResearchQuestion item) return;
        var text = TextPromptForm.Ask(this, "Edit research question", "Question:", item.Text);
        if (text == null) return;
        item.Text = text;
        await _repo.SaveQuestionAsync(item);
        await ReloadQuestionsAsync(item.ResearchQuestionId);
    }

    private async Task RemoveQuestionAsync()
    {
        if (_questions.SelectedItem is not ResearchQuestion item) return;
        if (MessageBox.Show(this, "Remove this question? Linked evidence will remain in the project.",
                "Remove question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await _repo.DeleteQuestionAsync(item.ResearchQuestionId);
        await ReloadQuestionsAsync();
        if (CurrentProject != null) await LoadEvidenceAsync(CurrentProject.ResearchProjectId);
    }

    private async Task MoveQuestionAsync(int delta)
    {
        if (_questions.SelectedItem is not ResearchQuestion selected) return;
        var list = _questions.Items.Cast<ResearchQuestion>().ToList();
        var index = list.IndexOf(selected);
        var target = index + delta;
        if (target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        await _repo.ReorderQuestionsAsync(list.Select(q => q.ResearchQuestionId).ToList());
        await ReloadQuestionsAsync(selected.ResearchQuestionId);
    }

    private async Task ReloadQuestionsAsync(long selectId = 0)
    {
        if (CurrentProject == null) return;
        var list = await _repo.GetQuestionsAsync(CurrentProject.ResearchProjectId);
        _questions.DataSource = list;
        _questions.SelectedItem = list.FirstOrDefault(q => q.ResearchQuestionId == selectId);
        RefreshQuestionChoices(list);
    }

    private async Task LoadEvidenceAsync(long projectId, long selectId = 0)
    {
        var items = await _repo.GetEvidenceAsync(projectId);
        _evidence.DataSource = items;
        if (selectId != 0)
            foreach (DataGridViewRow row in _evidence.Rows)
                if (row.DataBoundItem is EvidenceItem e && e.EvidenceItemId == selectId) { row.Selected = true; _evidence.CurrentCell = row.Cells[0]; break; }
        if (items.Count == 0) ShowEvidence(null);
    }

    private void NewEvidence()
    {
        if (CurrentProject == null) return;
        _evidence.ClearSelection();
        ShowEvidence(new EvidenceItem { ResearchProjectId = CurrentProject.ResearchProjectId, SortOrder = _evidence.Rows.Count });
        _title.Focus();
    }

    private async Task SaveEvidenceAsync()
    {
        var project = CurrentProject;
        if (project == null) return;
        var item = _editingEvidence ?? new EvidenceItem { ResearchProjectId = project.ResearchProjectId, SortOrder = _evidence.Rows.Count };
        if (string.IsNullOrWhiteSpace(_title.Text)) { MessageBox.Show(this, "Evidence needs a title."); return; }
        item.Title = _title.Text.Trim();
        item.Type = (EvidenceType)_type.SelectedItem!;
        item.ResearchQuestionId = (_question.SelectedItem as QuestionChoice)?.Id;
        item.Relationship = (EvidenceRelationship)_relationship.SelectedItem!;
        item.Judgment = (EvidenceJudgment)_judgment.SelectedItem!;
        item.SourceType = EmptyToNull(_sourceType.Text);
        item.StableIdentifier = EmptyToNull(_stableId.Text);
        item.CanonicalReference = EmptyToNull(_reference.Text);
        item.Provenance = EmptyToNull(_provenance.Text);
        item.Excerpt = EmptyToNull(_excerpt.Text);
        item.ResearcherNote = EmptyToNull(_researcherNote.Text);
        await _repo.SaveEvidenceAsync(item);
        await LoadEvidenceAsync(project.ResearchProjectId, item.EvidenceItemId);
        _statusLine.Text = "Evidence saved. Review judgment and researcher interpretation remain explicit.";
    }

    private async Task RemoveEvidenceAsync()
    {
        var item = CurrentEvidence;
        if (item == null || CurrentProject == null) return;
        if (MessageBox.Show(this, $"Remove “{item.Title}”?", "Remove evidence",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await _repo.DeleteEvidenceAsync(item.EvidenceItemId);
        await LoadEvidenceAsync(CurrentProject.ResearchProjectId);
    }

    private void ShowEvidence(EvidenceItem? item)
    {
        _editingEvidence = item;
        _title.Text = item?.Title ?? "";
        _type.SelectedItem = item?.Type ?? EvidenceType.PrimaryText;
        _question.SelectedItem = _question.Items.Cast<QuestionChoice>().FirstOrDefault(q => q.Id == item?.ResearchQuestionId) ?? _question.Items.Cast<QuestionChoice>().FirstOrDefault();
        _relationship.SelectedItem = item?.Relationship ?? EvidenceRelationship.Contextualizes;
        _judgment.SelectedItem = item?.Judgment ?? EvidenceJudgment.Uncertain;
        _sourceType.Text = item?.SourceType ?? "";
        _stableId.Text = item?.StableIdentifier ?? "";
        _reference.Text = item?.CanonicalReference ?? "";
        _provenance.Text = item?.Provenance ?? "";
        _excerpt.Text = item?.Excerpt ?? "";
        _researcherNote.Text = item?.ResearcherNote ?? "";
    }

    private void RefreshQuestionChoices(IEnumerable<ResearchQuestion> questions)
    {
        var selected = (_question.SelectedItem as QuestionChoice)?.Id;
        var choices = new List<QuestionChoice> { new(null, "General project evidence") };
        choices.AddRange(questions.Select(q => new QuestionChoice(q.ResearchQuestionId, q.Text)));
        _question.DataSource = choices;
        _question.SelectedItem = choices.FirstOrDefault(q => q.Id == selected) ?? choices[0];
    }

    private void ClearProject()
    {
        _theory.Clear(); _projectNotes.Clear(); _questions.DataSource = null; _evidence.DataSource = null;
        RefreshQuestionChoices(Array.Empty<ResearchQuestion>()); ShowEvidence(null);
    }

    private static void AddField(Control host, string label, TextBox box, ref int y)
    {
        host.Controls.Add(LabelAt(label, 10, y, 520)); y += 20;
        box.SetBounds(10, y, 520, 26); box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 35;
    }

    private static void AddArea(Control host, string label, TextBox box, int height, ref int y)
    {
        host.Controls.Add(LabelAt(label, 10, y, 520)); y += 20;
        box.SetBounds(10, y, 520, height); box.Multiline = true; box.ScrollBars = ScrollBars.Vertical;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += height + 9;
    }

    private static void AddComboField(Control host, string label, ComboBox box, object values, ref int y)
    {
        host.Controls.Add(LabelAt(label, 10, y, 520)); y += 20;
        box.SetBounds(10, y, 520, 26); box.DropDownStyle = ComboBoxStyle.DropDownList;
        if (values is Array array && array.Length > 0) box.DataSource = array;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 35;
    }

    private static Button ButtonAt(string text, int x, int y, int width) => new() { Text = text, Left = x, Top = y, Width = width, Height = 28 };
    private static Label LabelAt(string text, int x, int y, int width) => new() { Text = text, Left = x, Top = y, Width = width, Height = 20 };
    private static string? EmptyToNull(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private sealed record QuestionChoice(long? Id, string Text) { public override string ToString() => Text; }
}
