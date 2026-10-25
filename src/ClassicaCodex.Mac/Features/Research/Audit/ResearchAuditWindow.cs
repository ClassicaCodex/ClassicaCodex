using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Audit;

/// <summary>Actionable audit of the records currently saved in one project.</summary>
internal sealed class ResearchAuditWindow : ResearchWindow
{
    private readonly DataGrid _findings = Ui.Table();
    private readonly Button _open = Ui.Button("Open Item", accent: true);

    public ResearchAuditWindow(
        ResearchProject project,
        IReadOnlyCollection<ResearchQuestion> questions,
        IReadOnlyCollection<EvidenceItem> evidence,
        IReadOnlyCollection<ScholarlyClaim>? claims = null) : base(null)
    {
        var report = ResearchProjectAudit.Evaluate(questions, evidence, claims);
        Report = report;
        Title = $"Project Audit — {project.Name}";
        Width = 1080;
        Height = 680;
        MinWidth = 780;
        MinHeight = 500;

        var heading = new TextBlock
        {
            Text = $"{report.QuestionCount} question(s) • {report.EvidenceCount} evidence item(s) • " +
                   $"{report.ClaimCount} scholarly claim(s) • " +
                   $"{report.UncertainEvidenceCount + report.UncertainClaimCount} awaiting review • " +
                   $"{report.Findings.Count} audit finding(s)",
            Classes = { "h3" },
            TextWrapping = TextWrapping.Wrap
        };
        var scope = Ui.Muted("This checks internal coverage, traceability, and review state only. " +
                             "It cannot determine whether the external scholarship or source record is complete.");
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(18, 16, 18, 12) };
        top.Children.Add(heading);
        top.Children.Add(scope);

        _findings.Columns.Add(Ui.Column("Priority", nameof(FindingRow.Severity), 90));
        _findings.Columns.Add(Ui.Column("Check", nameof(FindingRow.Category), 110));
        _findings.Columns.Add(Ui.Column("Item", nameof(FindingRow.Subject), 270));
        _findings.Columns.Add(Ui.Column("Finding", nameof(FindingRow.Message)));
        _findings.ItemsSource = report.Findings.Select(f => new FindingRow(f)).ToList();
        _findings.Margin = new Thickness(16, 0, 16, 12);
        _findings.SelectionChanged += (_, _) => UpdateOpenButton();
        Ui.OnRowDoubleTapped(_findings, () =>
        {
            OpenSelected();
            return Task.CompletedTask;
        });
        TableExport.Attach(_findings, () => Ui.SafeName(project.Name, "project") + "-audit",
            () => Rows(report.Findings));

        _open.Click += (_, _) => OpenSelected();
        var close = Ui.Button("Close");
        close.IsCancel = true;
        close.Click += (_, _) => Close();

        var bar = Ui.ButtonBar(null, close, _open);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bar);
        dock.Children.Add(_findings);
        Content = dock;
        UpdateOpenButton();
    }

    public ResearchAuditReport Report { get; }

    /// <summary>True when the reader chose a finding to open rather than closing.</summary>
    public bool ItemChosen { get; private set; }

    public long? SelectedQuestionId { get; private set; }
    public long? SelectedEvidenceId { get; private set; }
    public long? SelectedClaimId { get; private set; }

    private ResearchAuditFinding? CurrentFinding => (_findings.SelectedItem as FindingRow)?.Finding;

    private void UpdateOpenButton()
    {
        var finding = CurrentFinding;
        _open.IsEnabled = finding?.EvidenceItemId != null || finding?.ResearchQuestionId != null ||
                          finding?.ScholarlyClaimId != null;
    }

    internal void Select(ResearchAuditFinding finding) =>
        _findings.SelectedItem = (_findings.ItemsSource as IEnumerable<FindingRow>)?.FirstOrDefault(r => r.Finding == finding);

    internal void OpenSelected()
    {
        var finding = CurrentFinding;
        if (finding?.EvidenceItemId == null && finding?.ResearchQuestionId == null && finding?.ScholarlyClaimId == null) return;

        SelectedEvidenceId = finding.EvidenceItemId;
        SelectedQuestionId = finding.ResearchQuestionId;
        SelectedClaimId = finding.ScholarlyClaimId;
        ItemChosen = true;
        Close();
    }

    private static IReadOnlyList<IReadOnlyList<string>> Rows(IEnumerable<ResearchAuditFinding> findings)
    {
        var rows = new List<IReadOnlyList<string>> { new[] { "Priority", "Check", "Item", "Finding" } };
        rows.AddRange(findings.Select(f => (IReadOnlyList<string>)new[] { f.Severity.ToString(), f.Category, f.Subject, f.Message }));
        return rows;
    }

    private sealed class FindingRow
    {
        public FindingRow(ResearchAuditFinding finding) => Finding = finding;

        public ResearchAuditFinding Finding { get; }
        public string Severity => Words.Of(Finding.Severity);
        public string Category => Finding.Category;
        public string Subject => Finding.Subject;
        public string Message => Finding.Message;
    }
}
