using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>A human-review inbox for saved offline and AI intertextual candidates.</summary>
public sealed class ResearchEchoInvestigationsForm : ScaledForm
{
    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly string _authorName;
    private readonly ResearchEchoRepository _echoes = new();
    private readonly ResearchRepository _research = new();
    private readonly ResearchFindingRepository _findings = new();
    private readonly TextNodeRepository _textNodes = new();
    private readonly ListBox _investigations = new();
    private readonly DataGridView _results = new();
    private readonly ComboBox _disposition = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _note = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _details = new();
    private bool _loading;

    public (int WorkId, long TextNodeId)? NavigationTarget { get; private set; }
    private ResearchEchoInvestigation? Investigation => _investigations.SelectedItem as ResearchEchoInvestigation;
    private ResearchEchoResult? Result => _results.CurrentRow?.DataBoundItem as ResearchEchoResult;

    public ResearchEchoInvestigationsForm(ResearchProject project, Work work, string authorName)
    {
        _project = project; _work = work; _authorName = authorName;
        Text = $"Echo investigations — {project.Name}";
        Width = 1280; Height = 760; MinimumSize = new Size(980, 600); StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "SimilarWorks");

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        _investigations.Dock = DockStyle.Fill;
        _investigations.SelectedIndexChanged += async (_, _) => { if (!_loading) await LoadResultsAsync(); };
        var delete = new Button { Text = "Remove investigation", Dock = DockStyle.Bottom, Height = 34 };
        delete.Click += async (_, _) => await DeleteAsync();
        split.Panel1.Controls.Add(_investigations); split.Panel1.Controls.Add(delete);

        var right = new Panel { Dock = DockStyle.Fill };
        _details.Dock = DockStyle.Top; _details.Height = 76; _details.Padding = new Padding(8); _details.AutoEllipsis = true;
        _results.Dock = DockStyle.Fill; _results.AutoGenerateColumns = false; _results.ReadOnly = true;
        _results.AllowUserToAddRows = false; _results.RowHeadersVisible = false; _results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Disposition", HeaderText = "Review", Width = 80 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ConnectionType", HeaderText = "Relation", Width = 92 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ScoreLabel", HeaderText = "Score", Width = 85 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TargetAuthorName", HeaderText = "Author", Width = 120 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TargetWorkTitle", HeaderText = "Work", Width = 140 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TargetCitationRef", HeaderText = "Citation", Width = 90 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "TargetText", HeaderText = "Passage", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "MotifTags", HeaderText = "Motifs", Width = 150 });
        _results.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Rationale", HeaderText = "Rationale", Width = 230 });
        _results.SelectionChanged += (_, _) => ShowResult();
        _results.CellDoubleClick += async (_, _) => await OpenStudioAsync();

        var review = new Panel { Dock = DockStyle.Bottom, Height = 128, Padding = new Padding(8) };
        var dispositionLabel = new Label { Text = "Human review", Left = 8, Top = 9, Width = 100 };
        _disposition.SetBounds(110, 6, 130, 26); _disposition.DataSource = Enum.GetValues<ResearchEchoDisposition>();
        var save = Button("Save review", 250, 5, 105); save.Click += async (_, _) => await SaveReviewAsync();
        var promote = Button("Promote to evidence", 365, 5, 140); promote.Click += async (_, _) => await PromoteAsync();
        var source = Button("Open source", 515, 5, 100); source.Click += async (_, _) => await OpenSourceAsync();
        var target = Button("Open target", 625, 5, 100); target.Click += async (_, _) => await OpenTargetAsync();
        var studio = Button("Parallel studio", 735, 5, 115); studio.Click += async (_, _) => await OpenStudioAsync();
        _note.SetBounds(8, 40, 900, 76); _note.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        _note.PlaceholderText = "Why you accepted or rejected this candidate; parallels, differences, chronology, bibliography to check…";
        review.Controls.AddRange([dispositionLabel, _disposition, save, promote, source, target, studio, _note]);

        right.Controls.Add(_results); right.Controls.Add(_details); right.Controls.Add(review);
        split.Panel2.Controls.Add(right); Controls.Add(split);
        ReadingTheme.AttachTo(this, () => _details.ForeColor = ReadingTheme.MutedText);
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            var maximum = split.ClientSize.Width - 700 - split.SplitterWidth;
            if (maximum >= 240) { split.SplitterDistance = Math.Clamp(320, 240, maximum); split.Panel1MinSize = 240; split.Panel2MinSize = 700; }
            await LoadInvestigationsAsync();
        };
    }

    private static Button Button(string text, int x, int y, int width) => new() { Text = text, Left = x, Top = y, Width = width, Height = 28 };

    private async Task LoadInvestigationsAsync(long selectId = 0)
    {
        _loading = true;
        var items = await _echoes.GetInvestigationsAsync(_project.ResearchProjectId);
        _investigations.DataSource = null; _investigations.DataSource = items;
        if (selectId > 0) _investigations.SelectedItem = items.FirstOrDefault(i => i.ResearchEchoInvestigationId == selectId);
        _loading = false;
        await LoadResultsAsync();
    }

    private async Task LoadResultsAsync(long selectId = 0)
    {
        var investigation = Investigation;
        if (investigation == null) { _results.DataSource = null; _details.Text = "No saved echo investigations yet."; return; }
        _details.Text = $"{investigation.Method} • source {_authorName}, {_work.Title} {PassageCitation.Display(investigation.SourceCitationRef, investigation.SourceMilestone)}\n" +
                        $"Scope: {investigation.TargetScope ?? "not recorded"}" +
                        (investigation.AiModel == null ? "" : $" • AI: {investigation.AiModel} (candidate generator, not reviewer)");
        var rows = await _echoes.GetResultsAsync(investigation.ResearchEchoInvestigationId);
        _results.DataSource = rows;
        if (selectId > 0)
            foreach (DataGridViewRow row in _results.Rows)
                if (row.DataBoundItem is ResearchEchoResult r && r.ResearchEchoResultId == selectId) { _results.CurrentCell = row.Cells[0]; break; }
        ShowResult();
    }

    private void ShowResult()
    {
        var result = Result;
        if (result == null) { _note.Text = ""; return; }
        _disposition.SelectedItem = result.Disposition;
        _note.Text = result.ResearcherNote ?? "";
    }

    private async Task SaveReviewAsync()
    {
        if (Result is not { } result || _disposition.SelectedItem is not ResearchEchoDisposition disposition) return;
        await _echoes.SaveReviewAsync(result.ResearchEchoResultId, disposition, string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim());
        await LoadResultsAsync(result.ResearchEchoResultId);
    }

    private async Task PromoteAsync()
    {
        var investigation = Investigation; var result = Result;
        if (investigation == null || result == null) return;
        if (result.EvidenceItemId != null) { MessageBox.Show(this, "This candidate is already linked to evidence."); return; }
        if (result.Disposition != ResearchEchoDisposition.Accepted)
        {
            MessageBox.Show(this, "Accept and save the candidate first. Promotion means ‘worth retaining as paired evidence,’ not proof of borrowing.");
            return;
        }
        var evidence = new EvidenceItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            ResearchQuestionId = investigation.ResearchQuestionId,
            Title = $"Echo candidate: {_work.Title} {PassageCitation.Display(investigation.SourceCitationRef, investigation.SourceMilestone)} ↔ {result.TargetWorkTitle} {PassageCitation.Display(result.TargetCitationRef, result.TargetMilestone)}",
            Type = EvidenceType.PrimaryText,
            SourceType = "ClassicaCodex paired-passage echo investigation",
            StableIdentifier = $"classicacodex:echo-investigation:{investigation.ResearchEchoInvestigationId}:result:{result.ResearchEchoResultId}",
            CanonicalReference = $"{investigation.SourceEditionCtsUrn}:{investigation.SourceCitationRef} ↔ {result.TargetEditionCtsUrn}:{result.TargetCitationRef}",
            Provenance = $"Method: {investigation.Method}. {investigation.Settings}" +
                         $" Human parallel classification: {result.ConnectionType}; directionality: {result.Directionality}; motifs: {result.MotifTags ?? "not classified"}." +
                         (investigation.AiModel == null ? "" : $" Generated by {investigation.AiModel} at {investigation.AiGeneratedUtc:u}. Citations resolved against the local corpus before capture."),
            Excerpt = $"SOURCE — {_authorName}, {_work.Title} [{PassageCitation.Display(investigation.SourceCitationRef, investigation.SourceMilestone)}]\r\n{investigation.SourceText}\r\n\r\nTARGET — {result.TargetAuthorName}, {result.TargetWorkTitle} [{PassageCitation.Display(result.TargetCitationRef, result.TargetMilestone)}]\r\n{result.TargetText}",
            Judgment = EvidenceJudgment.Uncertain,
            Relationship = EvidenceRelationship.Contextualizes,
            ResearcherNote = string.Join("\r\n\r\n", new[] { result.ResearcherNote, result.ParallelNote }.Where(n => !string.IsNullOrWhiteSpace(n))),
            // Every AI-assisted method is AI-derived evidence, not the app's own
            // deterministic analysis. Testing one method by name silently exported
            // Corpus Investigator candidates as ClassicaCodexAnalysis; ask whether a
            // model was involved at all instead, so a method added later cannot repeat it.
            Origin = investigation.AiModel != null ||
                     investigation.Method is ResearchEchoMethod.AiCrossLanguage
                                          or ResearchEchoMethod.AiCorpusInvestigation
                ? EvidenceOrigin.AiCandidate
                : EvidenceOrigin.ClassicaCodexAnalysis,
            // The AI rationale only. The researcher's own close-reading note is
            // ResearcherNote above; folding it in here would attribute the human's
            // words to the model named in InterpretationAuthor.
            Interpretation = result.Rationale,
            InterpretationAuthor = investigation.AiModel == null ? "ClassicaCodex" : $"Gemini ({investigation.AiModel})",
            GeneratorPrompt = investigation.AiPrompt,
            GeneratedUtc = investigation.AiGeneratedUtc,
            SortOrder = (await _research.GetEvidenceAsync(_project.ResearchProjectId)).Count
        };
        var evidenceId = await _research.SaveEvidenceAsync(evidence);
        await _echoes.MarkPromotedAsync(result.ResearchEchoResultId, evidenceId);
        if (investigation.ResearchFindingId is long findingId)
        {
            var links = await _findings.GetLinksAsync(findingId);
            if (links.All(l => l.EvidenceItemId != evidenceId))
            {
                links.Add(new ResearchFindingEvidenceLink { ResearchFindingId = findingId, EvidenceItemId = evidenceId, Relationship = EvidenceRelationship.Contextualizes, Note = "Promoted from accepted echo candidate." });
                await _findings.SaveLinksAsync(findingId, links);
            }
        }
        await LoadResultsAsync(result.ResearchEchoResultId);
        MessageBox.Show(this, "Paired passages were added as uncertain evidence. Review the new evidence record before treating it as support for a claim.");
    }

    private async Task OpenSourceAsync()
    {
        if (Investigation is not { } investigation) return;
        var target = await _textNodes.ResolvePassageNavigationAsync(
            investigation.SourceTextNodeId, investigation.SourceEditionCtsUrn,
            investigation.SourceCitationRef, investigation.SourceText);
        if (target == null) { MessageBox.Show(this, "The source passage could not be resolved in the current corpus."); return; }
        NavigationTarget = target; DialogResult = DialogResult.OK; Close();
    }

    private async Task OpenTargetAsync()
    {
        if (Result is not { } result) return;
        var target = await _textNodes.ResolvePassageNavigationAsync(
            result.TargetTextNodeId, result.TargetEditionCtsUrn, result.TargetCitationRef, result.TargetText);
        if (target == null) { MessageBox.Show(this, "The target passage could not be resolved in the current corpus."); return; }
        NavigationTarget = target; DialogResult = DialogResult.OK; Close();
    }

    private async Task OpenStudioAsync()
    {
        if (Investigation is not { } investigation || Result is not { } result) return;
        using var studio = new ParallelPassageStudioForm(_project, _work, _authorName, investigation, result);
        studio.ShowDialog(this);
        if (studio.NavigationTarget is { } target)
        {
            NavigationTarget = target; DialogResult = DialogResult.OK; Close(); return;
        }
        await LoadResultsAsync(result.ResearchEchoResultId);
    }

    private async Task DeleteAsync()
    {
        if (Investigation is not { } investigation) return;
        if (MessageBox.Show(this, $"Remove “{investigation.Title}” and its candidate reviews? Promoted evidence will be retained.", "Remove investigation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await _echoes.DeleteInvestigationAsync(investigation.ResearchEchoInvestigationId);
        await LoadInvestigationsAsync();
    }
}
