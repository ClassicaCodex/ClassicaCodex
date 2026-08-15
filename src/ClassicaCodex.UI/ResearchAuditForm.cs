using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>Actionable audit of the records currently saved in one project.</summary>
public sealed class ResearchAuditForm : Form
{
    private readonly DataGridView _findings = new();
    private readonly Button _open = new();

    public long? SelectedQuestionId { get; private set; }
    public long? SelectedEvidenceId { get; private set; }

    public ResearchAuditForm(
        ResearchProject project,
        IReadOnlyCollection<ResearchQuestion> questions,
        IReadOnlyCollection<EvidenceItem> evidence)
    {
        var report = ResearchProjectAudit.Evaluate(questions, evidence);
        Text = $"Project Audit — {project.Name}";
        Width = 1080;
        Height = 680;
        MinimumSize = new Size(780, 500);
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "WordStudy");

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(10, 10, 8, 0),
            Font = new Font(Font, FontStyle.Bold),
            Text = $"{report.QuestionCount} question(s) • {report.EvidenceCount} evidence item(s) • " +
                   $"{report.UncertainEvidenceCount} awaiting review • {report.Findings.Count} audit finding(s)"
        };
        var scope = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 4, 8, 0),
            Text = "This checks internal coverage, traceability, and review state only. " +
                   "It cannot determine whether the external scholarship or source record is complete."
        };

        _findings.Dock = DockStyle.Fill;
        _findings.AutoGenerateColumns = false;
        _findings.AllowUserToAddRows = false;
        _findings.AllowUserToDeleteRows = false;
        _findings.ReadOnly = true;
        _findings.MultiSelect = false;
        _findings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _findings.RowHeadersVisible = false;
        _findings.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Severity", HeaderText = "Priority", Width = 85
        });
        _findings.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Category", HeaderText = "Check", Width = 105
        });
        _findings.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Subject", HeaderText = "Item", Width = 270
        });
        _findings.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = "Message", HeaderText = "Finding",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _findings.DataSource = report.Findings.ToList();
        _findings.SelectionChanged += (_, _) => UpdateOpenButton();
        _findings.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) OpenSelected();
        };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10) };
        _open.Text = "Open item";
        _open.SetBounds(10, 10, 100, 30);
        _open.Click += (_, _) => OpenSelected();
        var close = new Button { Text = "Close", Width = 90, Height = 30, Top = 10 };
        close.Left = footer.ClientSize.Width - close.Width - 10;
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Click += (_, _) => Close();
        footer.Controls.AddRange(new Control[] { _open, close });

        Controls.Add(_findings);
        Controls.Add(footer);
        Controls.Add(scope);
        Controls.Add(heading);
        ReadingTheme.AttachTo(this, () => scope.ForeColor = ReadingTheme.MutedText);
        WindowShortcuts.CloseOnEscape(this);
        UpdateOpenButton();
    }

    private ResearchAuditFinding? CurrentFinding =>
        _findings.CurrentRow?.DataBoundItem as ResearchAuditFinding;

    private void UpdateOpenButton()
    {
        var finding = CurrentFinding;
        _open.Enabled = finding?.EvidenceItemId != null || finding?.ResearchQuestionId != null;
    }

    private void OpenSelected()
    {
        var finding = CurrentFinding;
        if (finding?.EvidenceItemId == null && finding?.ResearchQuestionId == null) return;
        SelectedEvidenceId = finding.EvidenceItemId;
        SelectedQuestionId = finding.ResearchQuestionId;
        DialogResult = DialogResult.OK;
        Close();
    }
}
