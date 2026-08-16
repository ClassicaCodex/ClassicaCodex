using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Turns reviewed evidence into explicit, auditable researcher-owned findings.</summary>
public sealed class ResearchSynthesisForm : Form
{
    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly string _authorName;
    private readonly ResearchRepository _research = new();
    private readonly ResearchFindingRepository _findingsRepo = new();
    private readonly ListBox _findings = new();
    private readonly TextBox _title = new();
    private readonly TextBox _statement = new();
    private readonly ComboBox _status = new();
    private readonly ComboBox _question = new();
    private readonly TextBox _conclusion = new();
    private readonly TextBox _aiCandidate = new();
    private readonly Label _aiProvenance = new();
    private readonly DataGridView _evidence = new();
    private readonly Label _statusLine = new();
    private ResearchFinding? _editing;
    private List<ResearchQuestion> _questions = [];
    private List<EvidenceItem> _allEvidence = [];
    private List<ScholarlyClaim> _claims = [];
    private bool _loading;

    public ResearchSynthesisForm(ResearchProject project, Work work, string authorName)
    {
        _project = project;
        _work = work;
        _authorName = authorName;
        Text = $"Synthesis & Findings — {project.Name}";
        Width = 1480;
        Height = 880;
        MinimumSize = new Size(1200, 700);
        StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "WordStudy");

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 9, 8, 0),
            Text = "Findings are researcher-owned propositions. AI synthesis remains a labelled candidate until you adopt or rewrite it."
        };
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        BuildFindingList(split.Panel1);
        BuildEditor(split.Panel2);
        _statusLine.Dock = DockStyle.Bottom;
        _statusLine.Height = 25;
        _statusLine.Padding = new Padding(8, 4, 0, 0);
        Controls.Add(split);
        Controls.Add(header);
        Controls.Add(_statusLine);
        ReadingTheme.AttachTo(this, () =>
        {
            header.ForeColor = ReadingTheme.MutedText;
            _aiProvenance.ForeColor = ReadingTheme.MutedText;
            _statusLine.ForeColor = ReadingTheme.MutedText;
        });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            var maximum = split.ClientSize.Width - 780 - split.SplitterWidth;
            if (maximum >= 280)
            {
                split.SplitterDistance = Math.Clamp(330, 280, maximum);
                split.Panel1MinSize = 280;
                split.Panel2MinSize = 780;
            }
            await LoadWorkspaceAsync();
        };
    }

    private void BuildFindingList(Control host)
    {
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(5) };
        var add = Button("New finding", 100);
        var remove = Button("Remove", 75);
        var export = Button("Export dossier…", 120);
        add.Click += (_, _) => NewFinding();
        remove.Click += async (_, _) => await RemoveAsync();
        export.Click += async (_, _) => await ExportDossierAsync();
        strip.Controls.AddRange([add, remove, export]);
        _findings.Dock = DockStyle.Fill;
        _findings.SelectedIndexChanged += async (_, _) =>
        {
            if (!_loading) await ShowFindingAsync(_findings.SelectedItem as ResearchFinding);
        };
        host.Controls.Add(_findings);
        host.Controls.Add(strip);
    }

    private void BuildEditor(Control host)
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var findingTab = new TabPage("Finding & conclusion");
        var evidenceTab = new TabPage("Linked evidence");
        var aiTab = new TabPage("AI candidate synthesis");
        BuildFindingTab(findingTab);
        BuildEvidenceTab(evidenceTab);
        BuildAiTab(aiTab);
        tabs.TabPages.AddRange([findingTab, evidenceTab, aiTab]);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(6) };
        var save = Button("Save finding", 105);
        save.Click += async (_, _) => await SaveAsync();
        actions.Controls.Add(save);
        host.Controls.Add(tabs);
        host.Controls.Add(actions);
    }

    private void BuildFindingTab(Control host)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var y = 12;
        AddField(panel, "Short title", _title, ref y);
        AddArea(panel, "Proposition being assessed", _statement, 100, ref y);
        AddComboPair(panel, "Status", _status, Enum.GetValues<ResearchFindingStatus>(),
            "Research question", _question, ref y);
        AddArea(panel, "Researcher conclusion", _conclusion, 250, ref y);
        var note = new Label
        {
            Text = "Changing a status is a human judgment. Linked evidence remains visible on its own tab.",
            Left = 10,
            Top = y,
            Width = 780,
            Height = 30,
            ForeColor = ReadingTheme.MutedText
        };
        panel.Controls.Add(note);
        host.Controls.Add(panel);
    }

    private void BuildEvidenceTab(Control host)
    {
        var note = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 6, 6, 0),
            Text = "Check only evidence you have actually weighed for this proposition. Its role here may differ from its project-level relationship."
        };
        _evidence.Dock = DockStyle.Fill;
        _evidence.AutoGenerateColumns = false;
        _evidence.AllowUserToAddRows = false;
        _evidence.AllowUserToDeleteRows = false;
        _evidence.RowHeadersVisible = false;
        _evidence.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(EvidenceLinkRow.Linked), HeaderText = "Link", Width = 48 });
        _evidence.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(EvidenceLinkRow.Relationship),
            HeaderText = "Role",
            Width = 115,
            DataSource = Enum.GetValues<EvidenceRelationship>()
        });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(EvidenceLinkRow.Judgment), HeaderText = "Review", Width = 80, ReadOnly = true });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(EvidenceLinkRow.Title), HeaderText = "Evidence", Width = 320, ReadOnly = true });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(EvidenceLinkRow.Reference), HeaderText = "Reference", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        host.Controls.Add(_evidence);
        host.Controls.Add(note);
    }

    private void BuildAiTab(Control host)
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(5) };
        var draft = Button("Draft from linked evidence", 180);
        var copy = Button("Copy candidate", 110);
        draft.Click += async (_, _) => await DraftWithGeminiAsync();
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_aiCandidate.Text)) Clipboard.SetText(_aiCandidate.Text); };
        actions.Controls.AddRange([draft, copy]);
        _aiProvenance.Dock = DockStyle.Top;
        _aiProvenance.Height = 46;
        _aiProvenance.Padding = new Padding(8, 4, 6, 0);
        _aiCandidate.Dock = DockStyle.Fill;
        _aiCandidate.Multiline = true;
        _aiCandidate.ScrollBars = ScrollBars.Vertical;
        _aiCandidate.ReadOnly = true;
        host.Controls.Add(_aiCandidate);
        host.Controls.Add(_aiProvenance);
        host.Controls.Add(actions);
    }

    private async Task LoadWorkspaceAsync(long selectId = 0)
    {
        _questions = await _research.GetQuestionsAsync(_project.ResearchProjectId);
        _allEvidence = await _research.GetEvidenceAsync(_project.ResearchProjectId);
        _claims = await _research.GetScholarlyClaimsAsync(_project.ResearchProjectId);
        var questionChoices = new List<QuestionChoice> { new(null, "General project finding") };
        questionChoices.AddRange(_questions.Select(q => new QuestionChoice(q.ResearchQuestionId, q.Text)));
        _question.DataSource = questionChoices;
        var findings = await _findingsRepo.GetAsync(_project.ResearchProjectId);
        _loading = true;
        _findings.DataSource = findings;
        if (selectId > 0)
            _findings.SelectedItem = findings.FirstOrDefault(f => f.ResearchFindingId == selectId);
        _loading = false;
        await ShowFindingAsync(_findings.SelectedItem as ResearchFinding);
    }

    private void NewFinding()
    {
        _findings.ClearSelected();
        _editing = new ResearchFinding
        {
            ResearchProjectId = _project.ResearchProjectId,
            SortOrder = _findings.Items.Count
        };
        PopulateEditor(_editing, []);
        _title.Focus();
    }

    private async Task ShowFindingAsync(ResearchFinding? finding)
    {
        _editing = finding;
        var links = finding?.ResearchFindingId > 0
            ? await _findingsRepo.GetLinksAsync(finding.ResearchFindingId) : [];
        PopulateEditor(finding, links);
    }

    private void PopulateEditor(ResearchFinding? finding, IReadOnlyCollection<ResearchFindingEvidenceLink> links)
    {
        _title.Text = finding?.Title ?? "";
        _statement.Text = finding?.Statement ?? "";
        _status.SelectedItem = finding?.Status ?? ResearchFindingStatus.Hypothesis;
        if (_question.DataSource is IEnumerable<QuestionChoice> choices)
            _question.SelectedItem = choices.FirstOrDefault(q => q.Id == finding?.ResearchQuestionId) ?? choices.FirstOrDefault();
        _conclusion.Text = finding?.ResearcherConclusion ?? "";
        _aiCandidate.Text = finding?.AiCandidateSynthesis ?? "";
        _aiProvenance.Text = finding?.AiCandidateSynthesis == null
            ? "No AI candidate has been generated. Nothing is sent without your explicit action."
            : $"Candidate only · {finding.AiModel ?? "unknown model"} · {finding.AiGeneratedUtc?.ToLocalTime():g}";
        var byEvidence = links.ToDictionary(link => link.EvidenceItemId);
        _evidence.DataSource = _allEvidence.Select(item =>
        {
            byEvidence.TryGetValue(item.EvidenceItemId, out var link);
            return new EvidenceLinkRow(item, link != null, link?.Relationship ?? item.Relationship);
        }).ToList();
    }

    private async Task SaveAsync()
    {
        if (_editing == null) return;
        if (string.IsNullOrWhiteSpace(_title.Text) || string.IsNullOrWhiteSpace(_statement.Text))
        {
            MessageBox.Show(this, "A finding needs a short title and a proposition to assess.", "Save finding");
            return;
        }
        _evidence.EndEdit();
        _editing.Title = _title.Text.Trim();
        _editing.Statement = _statement.Text.Trim();
        _editing.Status = (ResearchFindingStatus)_status.SelectedItem!;
        _editing.ResearchQuestionId = (_question.SelectedItem as QuestionChoice)?.Id;
        _editing.ResearcherConclusion = Clean(_conclusion.Text);
        await _findingsRepo.SaveAsync(_editing);
        var links = (_evidence.DataSource as IEnumerable<EvidenceLinkRow> ?? [])
            .Where(row => row.Linked)
            .Select(row => new ResearchFindingEvidenceLink
            {
                ResearchFindingId = _editing.ResearchFindingId,
                EvidenceItemId = row.Evidence.EvidenceItemId,
                Relationship = row.Relationship
            }).ToList();
        await _findingsRepo.SaveLinksAsync(_editing.ResearchFindingId, links);
        await LoadWorkspaceAsync(_editing.ResearchFindingId);
        _statusLine.Text = $"Saved finding with {links.Count} explicit evidence link(s).";
    }

    private async Task RemoveAsync()
    {
        if (_editing?.ResearchFindingId is not > 0) return;
        if (MessageBox.Show(this, $"Remove finding “{_editing.Title}”? Evidence records will not be deleted.",
                "Remove finding", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await _findingsRepo.DeleteAsync(_editing.ResearchFindingId);
        await LoadWorkspaceAsync();
    }

    private async Task DraftWithGeminiAsync()
    {
        if (_editing?.ResearchFindingId is not > 0)
        {
            MessageBox.Show(this, "Save the finding and its evidence links before requesting a synthesis.");
            return;
        }
        await SaveAsync();
        var rows = (_evidence.DataSource as IEnumerable<EvidenceLinkRow> ?? []).Where(row => row.Linked).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Link at least one evidence item before asking for a synthesis.");
            return;
        }
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settings = new TranslateApiSettingsForm();
            settings.ShowDialog(this);
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return;
        }
        var evidenceContext = rows.Select(row =>
        {
            var item = row.Evidence;
            var excerpt = item.Excerpt ?? "(no excerpt recorded)";
            if (excerpt.Length > 1800) excerpt = excerpt[..1800] + "…";
            return $"{row.Relationship}; human judgment {item.Judgment}; {item.Title}; " +
                   $"ref {item.CanonicalReference ?? item.StableIdentifier ?? "none"}; excerpt: {excerpt}; " +
                   $"researcher note: {item.ResearcherNote ?? "none"}";
        }).ToList();
        var relevantClaims = _claims.Where(c => _editing.ResearchQuestionId == null ||
                                                c.ResearchQuestionId == _editing.ResearchQuestionId)
            .Select(c => $"{c.Claimant}: {c.ClaimText} ({c.Judgment}; {c.Relationship}; {c.Locator ?? "no locator"})")
            .ToList();
        if (TranslationSettings.AlwaysConfirmBeforeSending &&
            MessageBox.Show(this,
                $"This will send the project theory, this proposition, {rows.Count} linked evidence item(s), " +
                $"and {relevantClaims.Count} recorded scholarly claim(s) to Gemini. The response will be saved only as an AI candidate.\n\nContinue?",
                "Send synthesis context to Gemini?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        Enabled = false;
        _statusLine.Text = "Gemini is drafting a provisional synthesis…";
        try
        {
            var result = await GeminiTranslationService.DraftResearchSynthesisAsync(
                _project.Name + (string.IsNullOrWhiteSpace(_project.Notes) ? "" : $"\n{_project.Notes}"),
                _editing.Statement, evidenceContext, relevantClaims, TranslationSettings.GeminiApiKey!);
            _editing.AiCandidateSynthesis = result.CandidateText;
            _editing.AiModel = result.Model;
            _editing.AiPrompt = result.PromptProvenance;
            _editing.AiGeneratedUtc = DateTime.UtcNow;
            await _findingsRepo.SaveAsync(_editing);
            await _research.AddSystemResearchLogEntryAsync(new ResearchLogEntry
            {
                ResearchProjectId = _project.ResearchProjectId,
                Kind = ResearchLogEntryKind.FindingAiCandidateGenerated,
                Summary = $"Generated AI candidate synthesis for: {_editing.Title}",
                Details = $"Gemini {result.Model}; {_editing.AiGeneratedUtc:O}"
            });
            await LoadWorkspaceAsync(_editing.ResearchFindingId);
            _statusLine.Text = "AI candidate saved separately from the researcher conclusion.";
        }
        catch (Exception ex)
        {
            _statusLine.Text = $"AI synthesis did not finish: {ex.Message}";
        }
        finally { Enabled = true; }
    }

    private async Task ExportDossierAsync()
    {
        var findings = await _findingsRepo.GetAsync(_project.ResearchProjectId);
        var links = new Dictionary<long, IReadOnlyList<ResearchFindingEvidenceLink>>();
        foreach (var finding in findings)
            links[finding.ResearchFindingId] = await _findingsRepo.GetLinksAsync(finding.ResearchFindingId);
        using var dialog = new SaveFileDialog
        {
            Title = "Export research dossier",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            DefaultExt = "md",
            FileName = SafeName(_project.Name) + "-research-dossier.md"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var hypothesisRepo = new ResearchHypothesisRepository();
        var hypotheses = await hypothesisRepo.GetHypothesesAsync(_project.ResearchProjectId);
        var hypothesisLinks = new Dictionary<long, IReadOnlyList<ResearchHypothesisAssessment>>();
        foreach (var hypothesis in hypotheses)
            hypothesisLinks[hypothesis.ResearchHypothesisId] = await hypothesisRepo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId);
        var dossier = new ResearchDossierData(_project, _work.Title, _authorName, _questions, _allEvidence,
            _claims, findings, links,
            await new ResearchCorpusSnapshotRepository().GetSnapshotsAsync(_project.ResearchProjectId),
            await _research.GetResearchLogAsync(_project.ResearchProjectId), hypotheses, hypothesisLinks,
            await hypothesisRepo.GetSourcesAsync(_project.ResearchProjectId),
            await hypothesisRepo.GetExperimentsAsync(_project.ResearchProjectId));
        await File.WriteAllTextAsync(dialog.FileName, ResearchDossierExport.ToMarkdown(dossier));
        await _research.AddSystemResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchLogEntryKind.ResearchDossierExported,
            Summary = "Exported research dossier",
            Details = Path.GetFileName(dialog.FileName)
        });
        _statusLine.Text = $"Exported {Path.GetFileName(dialog.FileName)}.";
    }

    private static Button Button(string text, int width) => new() { Text = text, Width = width, Height = 28 };
    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static void AddField(Control host, string label, TextBox box, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 780, Height = 20 }); y += 20;
        box.SetBounds(10, y, 780, 26); box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        host.Controls.Add(box); y += 36;
    }
    private static void AddArea(Control host, string label, TextBox box, int height, ref int y)
    {
        host.Controls.Add(new Label { Text = label, Left = 10, Top = y, Width = 780, Height = 20 }); y += 20;
        box.SetBounds(10, y, 780, height); box.Multiline = true; box.ScrollBars = ScrollBars.Vertical;
        box.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; host.Controls.Add(box); y += height + 10;
    }
    private static void AddComboPair(Control host, string leftLabel, ComboBox left, object values,
        string rightLabel, ComboBox right, ref int y)
    {
        host.Controls.Add(new Label { Text = leftLabel, Left = 10, Top = y, Width = 370, Height = 20 });
        host.Controls.Add(new Label { Text = rightLabel, Left = 410, Top = y, Width = 380, Height = 20 }); y += 20;
        left.SetBounds(10, y, 370, 26); right.SetBounds(410, y, 380, 26);
        left.DropDownStyle = right.DropDownStyle = ComboBoxStyle.DropDownList; left.DataSource = values;
        host.Controls.Add(left); host.Controls.Add(right); y += 36;
    }
    private sealed record QuestionChoice(long? Id, string Text) { public override string ToString() => Text; }
    private sealed class EvidenceLinkRow
    {
        public EvidenceItem Evidence { get; }
        public bool Linked { get; set; }
        public EvidenceRelationship Relationship { get; set; }
        public EvidenceJudgment Judgment => Evidence.Judgment;
        public string Title => Evidence.Title;
        public string Reference => Evidence.CanonicalReference ?? Evidence.StableIdentifier ?? "";
        public EvidenceLinkRow(EvidenceItem evidence, bool linked, EvidenceRelationship relationship) =>
            (Evidence, Linked, Relationship) = (evidence, linked, relationship);
    }
}
