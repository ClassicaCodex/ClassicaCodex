using ClassicaCodex.Core;
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
    private readonly Label _originLine = new();
    private readonly LinkLabel _openAnalysis = new();
    private readonly TextBox _interpretation = new();
    private readonly TextBox _generatorPrompt = new();
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
            _openAnalysis.LinkColor = ReadingTheme.IsDark
                ? Color.FromArgb(115, 180, 245)
                : Color.FromArgb(0, 70, 140);
            _openAnalysis.ActiveLinkColor = ReadingTheme.SelectionText;
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
        var add = new Button { Text = "New evidence", Width = 95, Height = 28 };
        var remove = new Button { Text = "Remove", Width = 70, Height = 28 };
        var projectTools = new Button { Text = "Project ▾", Width = 92, Height = 28 };
        var gather = new Button { Text = "Gather evidence ▾", Width = 120, Height = 28 };
        add.Click += (_, _) => NewEvidence();
        remove.Click += async (_, _) => await RemoveEvidenceAsync();
        var projectMenu = new ContextMenuStrip();
        projectMenu.Items.Add("Scholarly claims matrix", null, (_, _) => OpenScholarlyClaims());
        projectMenu.Items.Add("Import RIS / BibTeX bibliography…", null, async (_, _) => await OpenBibliographyImportAsync());
        projectMenu.Items.Add(new ToolStripSeparator());
        projectMenu.Items.Add("Project audit", null, async (_, _) => await OpenProjectAuditAsync());
        projectMenu.Items.Add("Research log", null, (_, _) => OpenResearchLog());
        projectTools.Click += (_, _) => projectMenu.Show(projectTools, new Point(0, projectTools.Height));
        var gatherMenu = new ContextMenuStrip();
        gatherMenu.Items.Add("Attach saved stylometry run", null, async (_, _) => await AttachStylometryRunAsync());
        gatherMenu.Items.Add(new ToolStripSeparator());
        gatherMenu.Items.Add("AI: Find relevant corpus passages", null, async (_, _) => await GatherCorpusEvidenceAsync(false));
        gatherMenu.Items.Add("AI: Challenge the working theory", null, async (_, _) => await GatherCorpusEvidenceAsync(true));
        gather.Click += (_, _) => gatherMenu.Show(gather, new Point(0, gather.Height));
        strip.Controls.Add(add);
        strip.Controls.Add(remove);
        strip.Controls.Add(projectTools);
        strip.Controls.Add(gather);

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
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Origin", HeaderText = "Origin", Width = 96 });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Type", HeaderText = "Type", Width = 95 });
        _evidence.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CanonicalReference", HeaderText = "Reference", Width = 110 });
        _evidence.CellFormatting += (_, e) =>
        {
            if (_evidence.Columns[e.ColumnIndex].DataPropertyName != "Origin" || e.Value is not EvidenceOrigin origin)
                return;
            e.Value = origin switch
            {
                EvidenceOrigin.ClassicaCodexAnalysis => "App analysis",
                EvidenceOrigin.AiCandidate => "AI candidate",
                _ => "Manual"
            };
            e.FormattingApplied = true;
        };
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
        _originLine.SetBounds(10, y, 520, 22);
        _originLine.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _originLine.Font = new Font(Font, FontStyle.Bold);
        scroll.Controls.Add(_originLine);
        y += 27;
        _openAnalysis.SetBounds(10, y, 520, 22);
        _openAnalysis.Text = "Open this saved run in Stylometry →";
        _openAnalysis.Visible = false;
        _openAnalysis.LinkClicked += (_, _) => OpenAttachedStylometryRun();
        scroll.Controls.Add(_openAnalysis);
        y += 27;
        _interpretation.ReadOnly = true;
        AddArea(scroll, "App / AI interpretation (not raw evidence)", _interpretation, 80, ref y);
        _generatorPrompt.ReadOnly = true;
        AddArea(scroll, "Generation prompt and corpus scope", _generatorPrompt, 72, ref y);
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

    private void OpenResearchLog()
    {
        var project = CurrentProject;
        if (project == null)
        {
            MessageBox.Show(this, "Open or create a research project first.");
            return;
        }

        using var log = new ResearchLogForm(project);
        log.ShowDialog(this);
    }

    private void OpenScholarlyClaims()
    {
        var project = CurrentProject;
        if (project == null)
        {
            MessageBox.Show(this, "Open or create a research project first.");
            return;
        }

        using var claims = new ScholarlyClaimsForm(project);
        claims.ShowDialog(this);
    }

    private async Task OpenBibliographyImportAsync()
    {
        var project = CurrentProject;
        if (project == null)
        {
            MessageBox.Show(this, "Open or create a research project first.");
            return;
        }

        using var import = new BibliographyImportForm(project);
        import.ShowDialog(this);
        if (import.ImportedCount > 0)
        {
            await LoadEvidenceAsync(project.ResearchProjectId);
            _statusLine.Text = $"Imported {import.ImportedCount} scholarship source(s); review remains uncertain.";
        }
    }

    private async Task OpenProjectAuditAsync()
    {
        var project = CurrentProject;
        if (project == null) return;
        var questions = await _repo.GetQuestionsAsync(project.ResearchProjectId);
        var evidence = await _repo.GetEvidenceAsync(project.ResearchProjectId);
        var claims = await _repo.GetScholarlyClaimsAsync(project.ResearchProjectId);
        using var form = new ResearchAuditForm(project, questions, evidence, claims);
        if (form.ShowDialog(this) != DialogResult.OK) return;

        if (form.SelectedClaimId is long claimId)
        {
            using var claimsForm = new ScholarlyClaimsForm(project, claimId);
            claimsForm.ShowDialog(this);
            return;
        }

        if (form.SelectedEvidenceId is long evidenceId)
        {
            foreach (DataGridViewRow row in _evidence.Rows)
            {
                if (row.DataBoundItem is not EvidenceItem item || item.EvidenceItemId != evidenceId) continue;
                row.Selected = true;
                _evidence.CurrentCell = row.Cells[0];
                _evidence.FirstDisplayedScrollingRowIndex = row.Index;
                return;
            }
        }

        if (form.SelectedQuestionId is long questionId)
            _questions.SelectedItem = _questions.Items.Cast<ResearchQuestion>()
                .FirstOrDefault(q => q.ResearchQuestionId == questionId);
    }

    private async Task AttachStylometryRunAsync()
    {
        var project = CurrentProject;
        if (project == null) return;
        var runRepo = new StylometryRunRepository();
        var runs = (await runRepo.GetAllRunsAsync())
            .Where(r => r.TargetWorkId == _work.WorkId)
            .ToList();
        if (runs.Count == 0)
        {
            MessageBox.Show(this, "There are no saved stylometry runs targeting this work yet.");
            return;
        }

        using var picker = new StylometryRunEvidencePickerForm(runs);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedRun is not { } run) return;

        var stableId = $"classicacodex:stylometry-run:{run.RunId}";
        var existing = await _repo.GetEvidenceAsync(project.ResearchProjectId);
        if (existing.Any(e => string.Equals(e.StableIdentifier, stableId, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That saved run is already attached to this project.");
            return;
        }

        var metrics = (await runRepo.GetRunMetricsAsync()).FirstOrDefault(m => m.RunId == run.RunId);
        var neighbors = await runRepo.GetNeighborsAsync(run.RunId, 12);
        var features = (await runRepo.GetFeaturesAsync(run.RunId)).Take(20).ToList();
        var resultLines = new List<string>
        {
            $"Target: {run.TargetAuthorName}, {run.TargetWorkTitle}",
            $"Nearest neighbour: {metrics?.NearestAuthor ?? "unknown"}, {metrics?.NearestTitle ?? "unknown"} (Δ {metrics?.DeltaFloor.ToString("0.0000") ?? "n/a"})",
            $"Depth to first outsider: {metrics?.DepthToFirstOutsider?.ToString() ?? "none in pool"}",
            $"Author purity among top 10: {(metrics == null ? "n/a" : metrics.AuthorPurityAt10.ToString("P1"))}",
            "",
            "Nearest works:"
        };
        resultLines.AddRange(neighbors.Select(
            n => $"{n.Rank}. {n.AuthorName}, {n.WorkTitle} — Δ {n.Delta:0.0000}"));
        resultLines.Add("");
        resultLines.Add("Most frequent features:");
        resultLines.AddRange(features.Select(
            f => $"{f.Rank}. {f.Word} — {f.RelativeFrequency:P4}"));
        var questionId = (_questions.SelectedItem as ResearchQuestion)?.ResearchQuestionId;
        var item = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = questionId,
            Title = string.IsNullOrWhiteSpace(run.Label)
                ? $"Saved stylometry run #{run.RunId}" : $"Stylometry: {run.Label}",
            Type = EvidenceType.Stylometric,
            SourceType = "ClassicaCodex saved stylometry run",
            StableIdentifier = stableId,
            CanonicalReference = _work.CtsUrn,
            Provenance = $"Saved {run.CreatedUtc:O}; {run.Settings.Describe()}; pool {run.PoolSize:N0}; " +
                         $"target tokens {metrics?.TargetTokenCount?.ToString("N0") ?? "unknown"}. Exact run ID {run.RunId}.",
            Excerpt = string.Join(Environment.NewLine, resultLines),
            Judgment = EvidenceJudgment.Uncertain,
            Relationship = EvidenceRelationship.Contextualizes,
            Origin = EvidenceOrigin.ClassicaCodexAnalysis,
            SortOrder = _evidence.Rows.Count
        };
        await _repo.SaveEvidenceAsync(item);
        await LoadEvidenceAsync(project.ResearchProjectId, item.EvidenceItemId);
        _statusLine.Text = $"Attached saved stylometry run #{run.RunId} as uncertain analysis evidence.";
    }

    private async Task GatherCorpusEvidenceAsync(bool challengeTheory)
    {
        var project = CurrentProject;
        if (project == null) return;
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settings = new TranslateApiSettingsForm();
            settings.ShowDialog(this);
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return;
        }

        var editions = await new EditionRepository().GetByWorkAsync(_work.WorkId);
        var edition = editions.FirstOrDefault(e => e.Kind == EditionKind.Original
            && (string.IsNullOrWhiteSpace(e.Orthography) || e.Orthography == "normalised"))
            ?? editions.FirstOrDefault(e => e.Kind == EditionKind.Original);
        if (edition == null)
        {
            MessageBox.Show(this, "This work has no original-language edition to search.");
            return;
        }

        var nodes = await new TextNodeRepository().GetByEditionAsync(edition.EditionId, readingLinesOnly: true);
        var (taggedCorpus, truncatedAtRef) = BuildTaggedCorpus(nodes);
        if (string.IsNullOrWhiteSpace(taggedCorpus))
        {
            MessageBox.Show(this, "The selected edition contains no searchable reading text.");
            return;
        }

        if (TranslationSettings.AlwaysConfirmBeforeSending)
        {
            var action = challengeTheory ? "challenge the working theory" : "find relevant passages";
            if (MessageBox.Show(this,
                    $"This will send the project theory, research questions, saved-evidence titles, and " +
                    $"{taggedCorpus.Length:N0} characters from the local edition of {_work.Title} to Gemini " +
                    $"to {action}. Returned citations will be checked against this edition before anything is saved.\n\nContinue?",
                    "Send research context to Gemini?", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        var questions = await _repo.GetQuestionsAsync(project.ResearchProjectId);
        var existing = await _repo.GetEvidenceAsync(project.ResearchProjectId);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(taggedCorpus))).ToLowerInvariant();
        _statusLine.Text = challengeTheory
            ? "Gemini is looking for counterevidence and rival explanations…"
            : "Gemini is looking for relevant passages in the local corpus…";
        Enabled = false;
        try
        {
            var generatedAt = DateTime.UtcNow;
            var theoryContext = project.Name
                + (string.IsNullOrWhiteSpace(project.Notes) ? "" : $"\nProject notes: {project.Notes}")
                + $"\nLibrary attribution status: {_work.AttributionStatus}"
                + (string.IsNullOrWhiteSpace(_work.AttributionNote) ? "" : $" — {_work.AttributionNote}");
            var result = await GeminiTranslationService.FindResearchEvidenceAsync(
                theoryContext,
                questions.Select(q => q.Text).ToList(),
                existing.Select(e => $"{e.Title} [{e.CanonicalReference ?? e.StableIdentifier ?? "no ref"}]").ToList(),
                _authorName, _work.Title, edition.Language, edition.CtsUrn, hash, truncatedAtRef,
                taggedCorpus, challengeTheory, TranslationSettings.GeminiApiKey!);

            var textByRef = nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.CitationRef))
                .GroupBy(n => n.CitationRef, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(n => n.Text)), StringComparer.OrdinalIgnoreCase);
            var knownIds = existing.Select(e => e.StableIdentifier)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var unresolved = 0;
            foreach (var candidate in result.Candidates)
            {
                var citation = candidate.CitationRef.Trim().TrimStart('[').TrimEnd(']');
                if (!textByRef.TryGetValue(citation, out var corpusText))
                {
                    unresolved++;
                    continue;
                }
                var stableId = $"{edition.CtsUrn}:{citation}";
                if (!knownIds.Add(stableId)) continue;
                var relationship = Enum.TryParse<EvidenceRelationship>(candidate.Relationship, true, out var parsed)
                    ? parsed : challengeTheory ? EvidenceRelationship.Contradicts : EvidenceRelationship.Contextualizes;
                long? questionId = candidate.QuestionIndex is > 0
                    && candidate.QuestionIndex <= questions.Count
                    ? questions[candidate.QuestionIndex.Value - 1].ResearchQuestionId : null;
                var item = new EvidenceItem
                {
                    ResearchProjectId = project.ResearchProjectId,
                    ResearchQuestionId = questionId,
                    Title = $"AI candidate: {candidate.Title}",
                    Type = EvidenceType.PrimaryText,
                    SourceType = "Local corpus passage; Gemini candidate",
                    StableIdentifier = stableId,
                    CanonicalReference = citation,
                    Provenance = $"Verified against local edition {edition.CtsUrn}; corpus SHA-256 {hash}; " +
                                 $"Gemini model {result.Model}; generated {generatedAt:O}; relevance confidence {candidate.Confidence}. " +
                                 (truncatedAtRef == null ? "Complete edition searched." : $"Search truncated after {truncatedAtRef}."),
                    Excerpt = corpusText,
                    Judgment = EvidenceJudgment.Uncertain,
                    Relationship = relationship,
                    Origin = EvidenceOrigin.AiCandidate,
                    Interpretation = candidate.Rationale,
                    InterpretationAuthor = $"Gemini ({result.Model})",
                    GeneratorPrompt = result.PromptProvenance,
                    GeneratedUtc = generatedAt,
                    SortOrder = _evidence.Rows.Count + added
                };
                await _repo.SaveEvidenceAsync(item);
                added++;
            }

            await LoadEvidenceAsync(project.ResearchProjectId);
            _statusLine.Text = added == 0
                ? $"Gemini returned no new verified candidates ({unresolved} unresolved citation(s))."
                : $"Added {added} uncertain AI candidate(s) for human review; rejected {unresolved} unresolved citation(s).";
        }
        catch (Exception ex)
        {
            _statusLine.Text = $"AI evidence gathering did not finish: {ex.Message}";
        }
        finally
        {
            Enabled = true;
        }
    }

    private static (string Text, string? TruncatedAtRef) BuildTaggedCorpus(IEnumerable<TextNode> nodes)
    {
        const int maxCharacters = 220_000;
        var builder = new System.Text.StringBuilder();
        string? lastRef = null;
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text) || string.IsNullOrWhiteSpace(node.CitationRef)) continue;
            var line = $"[{node.CitationRef}] {node.Text}\n";
            if (builder.Length + line.Length > maxCharacters)
                return (builder.ToString(), lastRef);
            builder.Append(line);
            lastRef = node.CitationRef;
        }
        return (builder.ToString(), null);
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
        _originLine.Text = item == null
            ? "Origin: manual evidence"
            : $"Origin: {item.Origin}" + (string.IsNullOrWhiteSpace(item.InterpretationAuthor)
                ? "" : $" — interpretation by {item.InterpretationAuthor}");
        _openAnalysis.Tag = GetStylometryRunId(item);
        _openAnalysis.Visible = _openAnalysis.Tag is long;
        _interpretation.Text = item?.Interpretation ?? "";
        _generatorPrompt.Text = item?.GeneratorPrompt ?? "";
        _researcherNote.Text = item?.ResearcherNote ?? "";
    }

    private void OpenAttachedStylometryRun()
    {
        if (_openAnalysis.Tag is not long runId) return;
        using var form = new StylometryAnalysisForm(runId);
        form.ShowDialog(this);
    }

    private static long? GetStylometryRunId(EvidenceItem? item)
    {
        const string prefix = "classicacodex:stylometry-run:";
        if (item?.Origin != EvidenceOrigin.ClassicaCodexAnalysis ||
            item.StableIdentifier?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) != true)
            return null;

        return long.TryParse(item.StableIdentifier[prefix.Length..], out var runId) ? runId : null;
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
