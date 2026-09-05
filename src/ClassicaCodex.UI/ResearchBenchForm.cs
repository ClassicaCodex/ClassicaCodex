using System.ComponentModel;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// An offline workspace where evidence is reviewed rather than silently
/// promoted into a finding. The selected work supplies context; projects,
/// questions and evidence retain their own durable records.
/// </summary>
public class ResearchBenchForm : ScaledForm
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly ResearchRepository _repo = new();
    private readonly ListBox _projects = new();
    private readonly TextBox _theory = new();
    private readonly TextBox _projectNotes = new();
    private readonly ComboBox _statusFilter = new();
    private Button? _archiveToggle;
    private bool _suspendFilterReload;

    private StatusFilter CurrentFilter => _statusFilter.SelectedItem as StatusFilter ?? StatusFilter.Current;
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
    private readonly LinkLabel _sourceFiles = new() { UseMnemonic = false };
    private readonly TextBox _interpretation = new();
    private readonly TextBox _generatorPrompt = new();
    private readonly TextBox _researcherNote = new();
    private readonly Label _statusLine = new();
    private SplitContainer? _outerSplit;
    private SplitContainer? _rightSplit;
    private EvidenceItem? _editingEvidence;
    private readonly long _initialProjectId;

    public (int WorkId, long TextNodeId)? NavigationTarget { get; private set; }

    private ResearchProject? CurrentProject => _projects.SelectedItem as ResearchProject;
    // Read the selection, not CurrentRow: the two disagree while a selection change
    // is in flight, and the inspector must always describe the highlighted evidence.
    private EvidenceItem? CurrentEvidence =>
        (_evidence.SelectedRows.Count > 0 ? _evidence.SelectedRows[0] : _evidence.CurrentRow)?.DataBoundItem as EvidenceItem;

    public ResearchBenchForm(Work work, string authorName, long initialProjectId = 0)
    {
        _work = work;
        _authorName = authorName;
        _initialProjectId = initialProjectId;
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
            _sourceFiles.LinkColor = _openAnalysis.LinkColor;
            _sourceFiles.ActiveLinkColor = ReadingTheme.SelectionText;
        });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) =>
        {
            ConfigureSplitter(_outerSplit, 285, 240, 700);
            ConfigureSplitter(_rightSplit, 500, 360, 420);
            await LoadProjectsAsync(_initialProjectId);
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
            Left = 10,
            Top = 8,
            Width = 1100,
            Height = 22,
            Font = new Font(Font, FontStyle.Bold)
        };
        _theory.SetBounds(10, 38, 915, 26);
        _theory.PlaceholderText = "Working theory or research question";
        var save = ButtonAt("Save project", 935, 37, 110);
        save.Click += async (_, _) => await SaveProjectAsync();
        var create = ButtonAt("New project", 1053, 37, 110);
        create.Click += async (_, _) => await NewProjectAsync();
        // One button, reflecting the state it would change. Archiving used to be a
        // one-way door: nothing in the Bench listed an archived project afterwards, so
        // "retained, not deleted" was true of the database and false of the interface.
        var archive = ButtonAt("Archive", 1171, 37, 90);
        _archiveToggle = archive;
        archive.Click += async (_, _) => await ToggleArchiveAsync();
        var suggest = ButtonAt("Let AI Suggest a New Project", 1270, 37, 200);
        suggest.Click += async (_, _) => await SuggestProjectAsync();
        _projectNotes.SetBounds(10, 72, 1251, 30);
        _projectNotes.PlaceholderText = "Project-level notes, scope, or current judgment";
        _projectNotes.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.AddRange(new Control[] { workLabel, _theory, save, create, archive, suggest, _projectNotes });
        return panel;
    }

    private async Task SuggestProjectAsync()
    {
        using var form = new AiProjectSuggestionForm(_work, _authorName);
        if (form.ShowDialog(this) != DialogResult.OK || form.CreatedProjectId is not { } id) return;
        await LoadProjectsAsync(id);
        _statusLine.Text = "Created the selected AI-proposed project with reviewable questions, hypotheses, experiments, and reading leads.";
    }

    private Control BuildBody()
    {
        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1
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
        var projectLabel = LabelAt("Projects for this work", 8, 8, 130);
        // Left-anchored, deliberately. This panel is built while the SplitContainer
        // still has its small default width, so a Top|Right anchor records a negative
        // distance from an edge that is not there yet and puts the control off-screen
        // once the panel grows - which is exactly what happened to its predecessor.
        var showLabel = LabelAt("Show:", 8, 34, 40);
        _statusFilter.SetBounds(50, 30, 130, 26);
        _statusFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusFilter.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _statusFilter.DataSource = StatusFilter.Choices;
        _statusFilter.SelectedIndexChanged += async (_, _) =>
        {
            if (_suspendFilterReload) return;
            // Keep the open project selected only if the new filter still admits it,
            // or LoadProjectsAsync would widen the list straight back to honour it and
            // choosing a filter would appear to do nothing.
            var open = CurrentProject;
            var keep = open != null && CurrentFilter.Admits(open.Status);
            await LoadProjectsAsync(keep ? open!.ResearchProjectId : 0);
        };
        _projects.SetBounds(8, 62, 268, 140);
        _projects.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        _projects.SelectedIndexChanged += async (_, _) => await ProjectChangedAsync();
        // Mark anything not active at display time rather than in
        // ResearchProject.ToString, which the dossier export and several combo boxes
        // also render.
        _projects.FormattingEnabled = true;
        _projects.Format += (_, e) =>
        {
            if (e.ListItem is ResearchProject p && p.Status != ResearchProjectStatus.Active)
                e.Value = $"{p.Name}  ({StatusFilter.Describe(p.Status)})";
        };
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
        host.Controls.AddRange(new Control[] { projectLabel, showLabel, _statusFilter, _projects, questionLabel, _questions, add, edit, remove, up, down });
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
        projectMenu.Items.Add("Bibliography & Zotero export…", null, (_, _) => OpenBibliography());
        projectMenu.Items.Add("Corpus snapshots…", null, (_, _) => OpenCorpusSnapshots());
        projectMenu.Items.Add("Reading queue & passage notebook…", null, async (_, _) => await OpenReadingQueueAsync());
        projectMenu.Items.Add("Echo investigations…", null, (_, _) => OpenEchoInvestigations());
        projectMenu.Items.Add("Intertextual Atlas…", null, (_, _) => OpenIntertextualAtlas());
        projectMenu.Items.Add("Hypothesis Lab…", null, (_, _) => OpenHypothesisLab());
        projectMenu.Items.Add("Synthesis & findings…", null, (_, _) => OpenSynthesis());
        projectMenu.Items.Add(new ToolStripSeparator());
        // Archive has a button of its own because it is the common case. On hold and
        // concluded need somewhere to live too - they used to be set from the header
        // dropdown, which is now the list filter.
        var statusMenu = new ToolStripMenuItem("Set project status");
        foreach (var status in Enum.GetValues<ResearchProjectStatus>())
            statusMenu.DropDownItems.Add(StatusFilter.Title(status), null,
                async (_, _) => await SetProjectStatusAsync(status));
        projectMenu.Items.Add(statusMenu);
        projectMenu.Items.Add("Project audit", null, async (_, _) => await OpenProjectAuditAsync());
        projectMenu.Items.Add("Research log", null, (_, _) => OpenResearchLog());
        projectTools.Click += (_, _) => projectMenu.Show(projectTools, new Point(0, projectTools.Height));
        var gatherMenu = new ContextMenuStrip();
        gatherMenu.Items.Add("Attach saved stylometry run", null, async (_, _) => await AttachStylometryRunAsync());
        gatherMenu.Items.Add(new ToolStripSeparator());
        gatherMenu.Items.Add("AI: Find relevant corpus passages", null, async (_, _) => await GatherCorpusEvidenceAsync(false));
        gatherMenu.Items.Add("AI: Challenge the working theory", null, async (_, _) => await GatherCorpusEvidenceAsync(true));
        gather.Click += (_, _) => gatherMenu.Show(gather, new Point(0, gather.Height));

        // A ContextMenuStrip is a component, not a child control, so ReadingTheme's
        // control-tree walk never reaches these two - they would drop out of a dark
        // window in system light. Theme them here, and again on every toggle, the way
        // MainForm does for the menus it owns.
        void ThemeMenus()
        {
            ReadingTheme.ApplyToContextMenu(projectMenu);
            ReadingTheme.ApplyToContextMenu(gatherMenu);
        }
        ThemeMenus();
        ReadingTheme.Changed += ThemeMenus;
        FormClosed += (_, _) =>
        {
            ReadingTheme.Changed -= ThemeMenus;
            projectMenu.Dispose();
            gatherMenu.Dispose();
        };
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
        _sourceFiles.SetBounds(10, y, 520, 22);
        _sourceFiles.Text = "Source files & page notes →";
        _sourceFiles.Visible = false;
        _sourceFiles.LinkClicked += (_, _) => OpenEvidenceSources();
        scroll.Controls.Add(_sourceFiles);
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
        var all = await _repo.GetProjectsForWorkAsync(
            _work.WorkId, includeArchived: true, workCtsUrn: _work.CtsUrn);
        var items = all.Where(p => CurrentFilter.Admits(p.Status)).ToList();
        // Archiving is retention, not deletion, so a caller that names a project - a
        // passage inquiry opening the project it was promoted into - must still be able
        // to reach it. Widening the filter rather than quietly ignoring it keeps the
        // dropdown honest about what the list is showing.
        var widened = false;
        if (selectId != 0 && items.All(p => p.ResearchProjectId != selectId)
            && all.Any(p => p.ResearchProjectId == selectId))
        {
            items = all;
            SetFilterSilently(StatusFilter.Everything);
            widened = true;
        }
        _projects.DataSource = null;
        _projects.DataSource = items;
        if (selectId != 0)
        {
            var wanted = items.FirstOrDefault(p => p.ResearchProjectId == selectId);
            _projects.SelectedItem = wanted;
            if (widened && wanted != null)
                _statusLine.Text = $"\u201c{wanted.Name}\u201d is {StatusFilter.Describe(wanted.Status)}. " +
                                   "The filter has been set to All so it is visible.";
        }
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
        UpdateArchiveButton(project);
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
        var project = new ResearchProject { WorkId = _work.WorkId, WorkCtsUrn = _work.CtsUrn, Name = name };
        await _repo.SaveProjectAsync(project);
        await LoadProjectsAsync(project.ResearchProjectId);
    }

    private async Task SaveProjectAsync()
    {
        var project = CurrentProject;
        if (project == null) { await NewProjectAsync(); return; }
        if (string.IsNullOrWhiteSpace(_theory.Text)) { MessageBox.Show(this, "Enter a working theory."); return; }
        project.Name = _theory.Text.Trim();
        project.Notes = EmptyToNull(_projectNotes.Text);
        await _repo.SaveProjectAsync(project);
        await LoadProjectsAsync(project.ResearchProjectId);
        _statusLine.Text = "Project saved.";
    }

    /// <summary>
    /// Sets the open project's status. The Archive button is the shortcut for the
    /// common case; the Project menu reaches the rest.
    /// </summary>
    private async Task SetProjectStatusAsync(ResearchProjectStatus status)
    {
        var project = CurrentProject;
        if (project == null || project.Status == status) return;
        await _repo.SetProjectStatusAsync(project.ResearchProjectId, status);
        await LoadProjectsAsync(project.ResearchProjectId);
        _statusLine.Text = status == ResearchProjectStatus.Archived
            ? $"“{project.Name}” is archived. Press Restore to undo."
            : $"“{project.Name}” is now {StatusFilter.Describe(status)}.";
    }

    /// <summary>
    /// Moves the filter to match what the list is actually showing, without setting off
    /// the reload its own SelectedIndexChanged handler would run.
    /// </summary>
    private void SetFilterSilently(StatusFilter filter)
    {
        if (ReferenceEquals(CurrentFilter, filter)) return;
        _suspendFilterReload = true;
        try { _statusFilter.SelectedItem = filter; }
        finally { _suspendFilterReload = false; }
    }

    private async Task ToggleArchiveAsync()
    {
        var project = CurrentProject;
        if (project == null) return;

        if (project.Status == ResearchProjectStatus.Archived)
        {
            await SetProjectStatusAsync(ResearchProjectStatus.Active);
            return;
        }

        if (MessageBox.Show(this, $"Archive “{project.Name}”? Its questions and evidence will be retained.",
                "Archive project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        // Keep it selected rather than reloading to nothing, so the researcher can see
        // what happened and press Restore straight away if it was a mistake.
        await SetProjectStatusAsync(ResearchProjectStatus.Archived);
    }

    private void UpdateArchiveButton(ResearchProject? project)
    {
        if (_archiveToggle == null) return;
        _archiveToggle.Text = project?.Status == ResearchProjectStatus.Archived ? "Restore" : "Archive";
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

    private void OpenBibliography()
    {
        var project = CurrentProject;
        if (project == null)
        {
            MessageBox.Show(this, "Select or create a research project first.");
            return;
        }
        using var form = new ResearchBibliographyForm(project);
        form.ShowDialog(this);
    }

    private void OpenCorpusSnapshots()
    {
        var project = CurrentProject;
        if (project == null)
        {
            MessageBox.Show(this, "Select or create a research project first.");
            return;
        }
        using var form = new ResearchCorpusSnapshotsForm(project);
        form.ShowDialog(this);
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
                _evidence.CurrentCell = row.Cells[0];
                row.Selected = true;
                ShowEvidence(item);
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
            var offered = new List<EvidenceCandidatePreview>();
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
                offered.Add(new EvidenceCandidatePreview(candidate.Title, citation, stableId, corpusText,
                    relationship, questionId, candidate.Confidence, candidate.Rationale));
            }

            if (offered.Count == 0)
            {
                _statusLine.Text = $"Gemini returned no new verified candidates ({unresolved} unresolved citation(s)).";
                return;
            }

            // Nothing is written until the researcher accepts it. This was the only AI
            // surface in the Bench that wrote straight into the evidence register and the
            // append-only research log - no cap and no accept step - while the Hypothesis
            // Lab, the Corpus Investigator, the echo investigations and the synthesis all
            // require a human to choose. The prompt asks for at most 12 candidates and
            // nothing enforces it; the review is where that stops mattering, because an
            // over-long list is now something the researcher sees rather than something
            // that silently lands in the register.
            Enabled = true; // the review is the researcher's turn, not the model's
            List<EvidenceCandidatePreview> accepted;
            using (var review = new ResearchEvidenceReviewForm(offered, result.Model, unresolved, challengeTheory))
            {
                if (review.ShowDialog(this) != DialogResult.OK)
                {
                    _statusLine.Text = $"Discarded {offered.Count} AI candidate(s). Nothing was saved.";
                    return;
                }
                accepted = review.Accepted.ToList();
            }

            var added = 0;
            foreach (var chosen in accepted)
            {
                var item = new EvidenceItem
                {
                    ResearchProjectId = project.ResearchProjectId,
                    ResearchQuestionId = chosen.QuestionId,
                    Title = $"AI candidate: {chosen.Title}",
                    Type = EvidenceType.PrimaryText,
                    SourceType = "Local corpus passage; Gemini candidate",
                    StableIdentifier = chosen.StableId,
                    CanonicalReference = chosen.Citation,
                    Provenance = $"Verified against local edition {edition.CtsUrn}; corpus SHA-256 {hash}; " +
                                 $"Gemini model {result.Model}; generated {generatedAt:O}; relevance confidence {chosen.Confidence}. " +
                                 (truncatedAtRef == null ? "Complete edition searched." : $"Search truncated after {truncatedAtRef}.") +
                                 " Accepted for review by the researcher.",
                    Excerpt = chosen.Excerpt,
                    Judgment = EvidenceJudgment.Uncertain,
                    Relationship = chosen.Relationship,
                    Origin = EvidenceOrigin.AiCandidate,
                    Interpretation = chosen.Rationale,
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
                ? $"None of the {offered.Count} candidate(s) were accepted. Nothing was saved."
                : $"Saved {added} of {offered.Count} candidate(s) as uncertain AI evidence; " +
                  $"{offered.Count - added} declined, {unresolved} unresolved citation(s) rejected.";
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
            // Stored form - the model's candidates are resolved against real
            // passages by this reference. See CrossLanguageEchoForm.
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
            ResearchProjectId = project.ResearchProjectId,
            Text = text,
            SortOrder = _questions.Items.Count
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
                if (row.DataBoundItem is EvidenceItem e && e.EvidenceItemId == selectId) { _evidence.CurrentCell = row.Cells[0]; row.Selected = true; ShowEvidence(e); break; }
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
        _sourceFiles.Visible = item?.EvidenceItemId > 0;
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

    private void OpenEvidenceSources()
    {
        if (_editingEvidence?.EvidenceItemId is not > 0) return;
        using var form = new EvidenceSourcesForm(_editingEvidence);
        form.ShowDialog(this);
    }

    private async Task OpenReadingQueueAsync()
    {
        var project = CurrentProject;
        if (project == null) return;
        using var form = new ResearchReadingQueueForm(project, _work);
        form.ShowDialog(this);
        if (form.NavigationTarget is { } target)
        {
            NavigationTarget = target;
            Close();
            return;
        }
        if (form.PromotedEvidenceItemId is long evidenceId)
            await LoadEvidenceAsync(project.ResearchProjectId, evidenceId);
    }

    private void OpenSynthesis()
    {
        if (CurrentProject is not { } project) return;
        using var form = new ResearchSynthesisForm(project, _work, _authorName);
        form.ShowDialog(this);
    }

    private void OpenHypothesisLab()
    {
        if (_projects.SelectedItem is not ResearchProject project) return;
        using var form = new HypothesisLabForm(project, _work, _authorName);
        form.ShowDialog(this);
    }

    private void OpenEchoInvestigations()
    {
        var project = CurrentProject;
        if (project == null) { MessageBox.Show(this, "Select or create a research project first."); return; }
        using var form = new ResearchEchoInvestigationsForm(project, _work, _authorName);
        form.ShowDialog(this);
        if (form.NavigationTarget is { } target)
        {
            NavigationTarget = target;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void OpenIntertextualAtlas()
    {
        var project = CurrentProject;
        if (project == null) { MessageBox.Show(this, "Select or create a research project first."); return; }
        using var form = new IntertextualAtlasForm(project);
        form.ShowDialog(this);
        if (form.NavigationTarget is { } target)
        {
            NavigationTarget = target;
            DialogResult = DialogResult.OK;
            Close();
        }
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
        UpdateArchiveButton(null);
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

/// <summary>
/// A choice in the project list's "Show" dropdown. Statuses are a property of a
/// project, but which of them the list shows is a property of the view, so the two are
/// kept apart: this type names the view's choices, and never writes anything.
/// </summary>
internal sealed class StatusFilter
{
    private readonly Func<ResearchProjectStatus, bool> _admits;

    private StatusFilter(string label, Func<ResearchProjectStatus, bool> admits)
    {
        Label = label;
        _admits = admits;
    }

    public string Label { get; }
    public bool Admits(ResearchProjectStatus status) => _admits(status);
    public override string ToString() => Label;

    /// <summary>Everything still in play - the default, and what the Bench used to show.</summary>
    public static readonly StatusFilter Current = new("Current", s => s != ResearchProjectStatus.Archived);
    public static readonly StatusFilter Everything = new("All", _ => true);
    private static StatusFilter Only(string label, ResearchProjectStatus only) => new(label, s => s == only);

    public static readonly StatusFilter[] Choices =
    [
        Current,
        Only("Active", ResearchProjectStatus.Active),
        Only("On hold", ResearchProjectStatus.OnHold),
        Only("Concluded", ResearchProjectStatus.Concluded),
        Only("Archived", ResearchProjectStatus.Archived),
        Everything
    ];

    /// <summary>Lowercase, for reading inside a sentence.</summary>
    public static string Describe(ResearchProjectStatus status) => status switch
    {
        ResearchProjectStatus.OnHold => "on hold",
        ResearchProjectStatus.Concluded => "concluded",
        ResearchProjectStatus.Archived => "archived",
        _ => "active"
    };

    /// <summary>Capitalised, for a menu item.</summary>
    public static string Title(ResearchProjectStatus status)
    {
        var described = Describe(status);
        return char.ToUpperInvariant(described[0]) + described[1..];
    }
}

/// <summary>
/// One resolved candidate, held in memory only until the researcher accepts it. Every
/// field has already been checked against the local edition - the citation resolved, and
/// the excerpt is the corpus text rather than anything the model wrote - so accepting is
/// a decision about relevance, not about whether the passage exists.
/// </summary>
internal sealed record EvidenceCandidatePreview(
    string Title, string Citation, string StableId, string Excerpt,
    EvidenceRelationship Relationship, long? QuestionId, string Confidence, string Rationale);

/// <summary>
/// The human accept step for AI-gathered corpus evidence. Candidates are shown with the
/// local corpus text they resolved to, and only checked rows are saved.
/// </summary>
internal sealed class ResearchEvidenceReviewForm : ScaledForm
{
    private readonly DataGridView _grid = new();
    private readonly BindingList<Row> _rows;

    public IReadOnlyList<EvidenceCandidatePreview> Accepted =>
        _rows.Where(r => r.Include).Select(r => r.Candidate).ToList();

    internal ResearchEvidenceReviewForm(IReadOnlyList<EvidenceCandidatePreview> candidates,
        string model, int unresolved, bool challengeTheory)
    {
        Text = challengeTheory ? "Review AI counterevidence candidates" : "Review AI corpus evidence candidates";
        Width = 1150; Height = 650; MinimumSize = new Size(820, 480);
        StartPosition = FormStartPosition.CenterParent;
        _rows = new BindingList<Row>(candidates.Select(c => new Row(c)).ToList());
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 58, Padding = new Padding(9, 7, 5, 0),
            Text = $"{candidates.Count} candidate(s) from {model}, each resolved against this edition" +
                   (unresolved == 0 ? "" : $"; {unresolved} more were rejected because their citation did not resolve") +
                   ". Check the ones worth keeping. Accepted rows are saved as uncertain AI candidates for you to judge; " +
                   "nothing is written to the project until you accept."
        };
        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = false; _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false; _grid.DataSource = _rows;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(Row.Include), HeaderText = "Keep", Width = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Row.Citation), HeaderText = "Reference", Width = 95, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Row.Title), HeaderText = "Candidate", Width = 215, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Row.Relationship), HeaderText = "Relationship", Width = 105, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Row.Confidence), HeaderText = "Confidence", Width = 85, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(Row.Detail), HeaderText = "Corpus text — model's rationale", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Discard all", Width = 90, DialogResult = DialogResult.Cancel };
        var accept = new Button { Text = "Save checked", Width = 110 };
        var all = new Button { Text = "Check all", Width = 90 };
        all.Click += (_, _) => { foreach (var r in _rows) r.Include = true; _grid.Refresh(); };
        accept.Click += (_, _) => { _grid.EndEdit(); DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.AddRange([cancel, accept, all]);
        Controls.Add(_grid); Controls.Add(note); Controls.Add(bottom);
        ReadingTheme.AttachTo(this, () => note.ForeColor = ReadingTheme.MutedText);
        WindowShortcuts.CloseOnEscape(this);
    }

    private sealed class Row
    {
        public Row(EvidenceCandidatePreview candidate) => Candidate = candidate;
        public EvidenceCandidatePreview Candidate { get; }
        public bool Include { get; set; }
        public string Citation => Candidate.Citation;
        public string Title => Candidate.Title;
        public string Relationship => Candidate.Relationship.ToString();
        public string Confidence => Candidate.Confidence;
        public string Detail => $"{Trim(Candidate.Excerpt)} — {Candidate.Rationale}";
        private static string Trim(string text) => text.Length <= 220 ? text : text[..220] + "…";
    }
}
