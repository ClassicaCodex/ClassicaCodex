using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A matrix of attributed scholarly propositions. Source wording, stance and
/// the researcher's verification remain separate rather than collapsing into
/// a single note.
/// </summary>
public sealed class ScholarlyClaimsForm : Form
{
    private readonly ResearchProject _project;
    private readonly ResearchRepository _repo = new();
    private readonly ComboBox _filter = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly TextBox _claimant = new();
    private readonly TextBox _claimText = new();
    private readonly TextBox _locator = new();
    private readonly ComboBox _question = new();
    private readonly ComboBox _source = new();
    private readonly ComboBox _relationship = new();
    private readonly ComboBox _judgment = new();
    private readonly TextBox _notes = new();
    private readonly Label _status = new();
    private readonly SplitContainer _split = new();

    private List<ResearchQuestion> _questions = new();
    private List<EvidenceItem> _sources = new();
    private List<ScholarlyClaim> _claims = new();
    private ScholarlyClaim? _editing;
    private readonly long _initialClaimId;

    public ScholarlyClaimsForm(ResearchProject project, long initialClaimId = 0)
    {
        _project = project;
        _initialClaimId = initialClaimId;
        Text = $"Scholarly Claims Matrix — {project.Name}";
        Width = 1220;
        Height = 780;
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "WordStudy");

        var header = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10) };
        var label = new Label { Text = "Show:", Left = 10, Top = 16, Width = 42, Height = 20 };
        _filter.SetBounds(54, 11, 360, 26);
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.SelectedIndexChanged += (_, _) => RenderMatrix();
        _summary.SetBounds(430, 15, 740, 22);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        header.Controls.AddRange(new Control[] { label, _filter, _summary });

        _split.Dock = DockStyle.Fill;
        _split.Orientation = Orientation.Horizontal;
        BuildMatrix(_split.Panel1);
        BuildEditor(_split.Panel2);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 24;
        _status.Padding = new Padding(8, 4, 0, 0);
        Controls.Add(_split);
        Controls.Add(header);
        Controls.Add(_status);

        ReadingTheme.AttachTo(this, () =>
        {
            _summary.ForeColor = ReadingTheme.MutedText;
            _status.ForeColor = ReadingTheme.MutedText;
        });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            var maximum = _split.ClientSize.Height - 255 - _split.SplitterWidth;
            if (maximum >= 250)
            {
                _split.SplitterDistance = Math.Clamp(350, 250, maximum);
                _split.Panel1MinSize = 250;
                _split.Panel2MinSize = 255;
            }
            await ReloadAsync(_initialClaimId);
        };
    }

    private void BuildMatrix(Control host)
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.Columns.Add(Column("Judgment", "Review", 78));
        _grid.Columns.Add(Column("Relationship", "Stance", 98));
        _grid.Columns.Add(Column("Claimant", "Scholar / claimant", 155));
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "ClaimText", HeaderText = "Claim",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _grid.Columns.Add(Column("SourceTitle", "Linked source", 210));
        _grid.Columns.Add(Column("Locator", "Page / locator", 105));
        _grid.SelectionChanged += (_, _) => ShowClaim(CurrentRow?.Claim);
        host.Controls.Add(_grid);
    }

    private void BuildEditor(Control host)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 8;
        AddField(scroll, "Scholar / claimant", _claimant, ref y);
        AddArea(scroll, "Claim (record the proposition, not your conclusion)", _claimText, 65, ref y);
        AddField(scroll, "Page, section, or other exact locator", _locator, ref y);
        AddCombo(scroll, "Research question", _question, ref y);
        AddCombo(scroll, "Linked source evidence", _source, ref y);
        AddCombo(scroll, "Relationship to the working theory", _relationship, ref y);
        _relationship.DataSource = Enum.GetValues<EvidenceRelationship>();
        AddCombo(scroll, "Human verification", _judgment, ref y);
        _judgment.DataSource = Enum.GetValues<EvidenceJudgment>();
        AddArea(scroll, "Researcher note / qualification", _notes, 58, ref y);

        var create = Button("New claim", 10, y + 4, 100);
        var save = Button("Save claim", 118, y + 4, 100);
        var remove = Button("Remove", 226, y + 4, 90);
        create.Click += (_, _) => NewClaim();
        save.Click += async (_, _) => await SaveAsync();
        remove.Click += async (_, _) => await RemoveAsync();
        scroll.Controls.AddRange(new Control[] { create, save, remove });
        host.Controls.Add(scroll);
    }

    private ClaimRow? CurrentRow => _grid.CurrentRow?.DataBoundItem as ClaimRow;

    private async Task ReloadAsync(long selectId = 0, long? revealQuestionId = null)
    {
        _questions = await _repo.GetQuestionsAsync(_project.ResearchProjectId);
        _sources = await _repo.GetEvidenceAsync(_project.ResearchProjectId);
        _claims = await _repo.GetScholarlyClaimsAsync(_project.ResearchProjectId);

        var questionId = (_question.SelectedItem as QuestionChoice)?.Id;
        var questionChoices = new List<QuestionChoice> { new(null, "General project claim") };
        questionChoices.AddRange(_questions.Select(q => new QuestionChoice(q.ResearchQuestionId, q.Text)));
        _question.DataSource = questionChoices;
        _question.SelectedItem = questionChoices.FirstOrDefault(q => q.Id == questionId) ?? questionChoices[0];

        var sourceId = (_source.SelectedItem as SourceChoice)?.Id;
        var sourceChoices = new List<SourceChoice> { new(null, "No linked source evidence") };
        sourceChoices.AddRange(_sources
            .OrderBy(e => e.Type == EvidenceType.Scholarship ? 0 : 1)
            .ThenBy(e => e.Title)
            .Select(e => new SourceChoice(e.EvidenceItemId, $"[{e.Type}] {e.Title}")));
        _source.DataSource = sourceChoices;
        _source.SelectedItem = sourceChoices.FirstOrDefault(s => s.Id == sourceId) ?? sourceChoices[0];

        var filterId = revealQuestionId ?? ((_filter.SelectedItem as FilterChoice)?.Id ?? -1);
        var filters = new List<FilterChoice> { new(-1, "All questions"), new(0, "General / unlinked claims") };
        filters.AddRange(_questions.Select(q => new FilterChoice(q.ResearchQuestionId, q.Text)));
        _filter.DataSource = filters;
        _filter.SelectedItem = filters.FirstOrDefault(f => f.Id == filterId) ?? filters[0];

        RenderMatrix(selectId);
    }

    private void RenderMatrix(long selectId = 0)
    {
        if (_filter.SelectedItem is not FilterChoice filter) return;
        var visible = filter.Id switch
        {
            -1 => _claims,
            0 => _claims.Where(c => c.ResearchQuestionId == null).ToList(),
            _ => _claims.Where(c => c.ResearchQuestionId == filter.Id).ToList()
        };
        var sourceNames = _sources.ToDictionary(e => e.EvidenceItemId, e => e.Title);
        var rows = visible.Select(c => new ClaimRow(
            c,
            c.Judgment,
            c.Relationship,
            c.Claimant,
            c.ClaimText,
            c.SourceEvidenceItemId is long id && sourceNames.TryGetValue(id, out var title) ? title : "—",
            c.Locator ?? "—")).ToList();
        _grid.DataSource = rows;
        _summary.Text = $"{rows.Count} shown • {_claims.Count(c => c.Judgment == EvidenceJudgment.Uncertain)} awaiting verification";
        if (selectId != 0)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.DataBoundItem is not ClaimRow item || item.Claim.ScholarlyClaimId != selectId) continue;
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                break;
            }
        }
        if (rows.Count == 0) ShowClaim(null);
    }

    private void NewClaim()
    {
        _grid.ClearSelection();
        ShowClaim(new ScholarlyClaim
        {
            ResearchProjectId = _project.ResearchProjectId,
            SortOrder = _claims.Count
        });
        _claimant.Focus();
    }

    private void ShowClaim(ScholarlyClaim? claim)
    {
        _editing = claim;
        _claimant.Text = claim?.Claimant ?? "";
        _claimText.Text = claim?.ClaimText ?? "";
        _locator.Text = claim?.Locator ?? "";
        _question.SelectedItem = _question.Items.Cast<QuestionChoice>()
            .FirstOrDefault(q => q.Id == claim?.ResearchQuestionId) ?? _question.Items.Cast<QuestionChoice>().FirstOrDefault();
        _source.SelectedItem = _source.Items.Cast<SourceChoice>()
            .FirstOrDefault(s => s.Id == claim?.SourceEvidenceItemId) ?? _source.Items.Cast<SourceChoice>().FirstOrDefault();
        _relationship.SelectedItem = claim?.Relationship ?? EvidenceRelationship.Contextualizes;
        _judgment.SelectedItem = claim?.Judgment ?? EvidenceJudgment.Uncertain;
        _notes.Text = claim?.Notes ?? "";
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_claimant.Text))
        {
            MessageBox.Show(this, "Attribute the claim to a scholar or source.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_claimText.Text))
        {
            MessageBox.Show(this, "Enter the scholarly claim.");
            return;
        }
        var claim = _editing ?? new ScholarlyClaim
        {
            ResearchProjectId = _project.ResearchProjectId, SortOrder = _claims.Count
        };
        claim.Claimant = _claimant.Text.Trim();
        claim.ClaimText = _claimText.Text.Trim();
        claim.Locator = Empty(_locator.Text);
        claim.ResearchQuestionId = (_question.SelectedItem as QuestionChoice)?.Id;
        claim.SourceEvidenceItemId = (_source.SelectedItem as SourceChoice)?.Id;
        claim.Relationship = (EvidenceRelationship)_relationship.SelectedItem!;
        claim.Judgment = (EvidenceJudgment)_judgment.SelectedItem!;
        claim.Notes = Empty(_notes.Text);
        await _repo.SaveScholarlyClaimAsync(claim);
        await ReloadAsync(claim.ScholarlyClaimId, claim.ResearchQuestionId ?? 0);
        _status.Text = "Claim saved. Source wording, stance, and human verification remain separate.";
    }

    private async Task RemoveAsync()
    {
        var claim = _editing;
        if (claim?.ScholarlyClaimId is not > 0) return;
        if (MessageBox.Show(this, $"Remove the claim attributed to {claim.Claimant}?",
                "Remove claim", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await _repo.DeleteScholarlyClaimAsync(claim.ScholarlyClaimId);
        await ReloadAsync();
        _status.Text = "Claim removed; the change remains visible in the research log.";
    }

    private static DataGridViewTextBoxColumn Column(string property, string header, int width) =>
        new() { DataPropertyName = property, HeaderText = header, Width = width };
    private static Button Button(string text, int x, int y, int width) =>
        new() { Text = text, Left = x, Top = y, Width = width, Height = 28 };
    private static void AddField(Control host, string label, TextBox box, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 520, Height = 20 }); y += 20;
        box.SetBounds(10, y, 760, 26); box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 35;
    }
    private static void AddArea(Control host, string label, TextBox box, int height, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 600, Height = 20 }); y += 20;
        box.SetBounds(10, y, 760, height); box.Multiline = true; box.ScrollBars = ScrollBars.Vertical;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += height + 9;
    }
    private static void AddCombo(Control host, string label, ComboBox box, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 520, Height = 20 }); y += 20;
        box.SetBounds(10, y, 760, 26); box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 35;
    }
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record FilterChoice(long Id, string Text) { public override string ToString() => Text; }
    private sealed record QuestionChoice(long? Id, string Text) { public override string ToString() => Text; }
    private sealed record SourceChoice(long? Id, string Text) { public override string ToString() => Text; }
    private sealed record ClaimRow(
        ScholarlyClaim Claim,
        EvidenceJudgment Judgment,
        EvidenceRelationship Relationship,
        string Claimant,
        string ClaimText,
        string SourceTitle,
        string Locator);
}
