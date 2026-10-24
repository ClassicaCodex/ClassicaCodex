using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// An offline workspace where evidence is reviewed rather than silently
/// promoted into a finding. The selected work supplies context; projects,
/// questions and evidence retain their own durable records.
///
/// Laid out as a Mac source list: the projects for this work and the open
/// project's questions in the sidebar, the project's theory and notes across
/// the top, and its evidence beside the inspector that edits it. The Windows
/// Bench's Project and Gather buttons are pull-down menus here, with the same
/// items in the same order.
/// </summary>
internal sealed partial class ResearchBenchWindow : ResearchWindow
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly long _initialProjectId;
    private readonly ResearchRepository _repo = new();

    private readonly ComboBox _statusFilter;
    private readonly ListBox _projects;
    private readonly ListBox _questions;
    private readonly TextBox _theory = Ui.Line("Working theory or research question");
    private readonly TextBox _projectNotes = Ui.Area(48, "Project-level notes, scope, or current judgment");
    private readonly Button _archiveToggle;
    private readonly Control _projectHeader;

    private readonly DataGrid _evidence;
    private readonly TextBox _title = Ui.Line();
    private readonly ComboBox _type = Ui.EnumCombo<EvidenceType>();
    private readonly ComboBox _question = Ui.Combo(Ui.QuestionChoices("General project evidence", Array.Empty<ResearchQuestion>()));
    private readonly ComboBox _judgment = Ui.EnumCombo<EvidenceJudgment>();
    private readonly ComboBox _relationship = Ui.EnumCombo<EvidenceRelationship>();
    private readonly TextBox _sourceType = Ui.Line();
    private readonly TextBox _stableId = Ui.Line();
    private readonly TextBox _reference = Ui.Line();
    private readonly TextBox _provenance = Ui.Area(62);
    private readonly TextBox _excerpt = Ui.Area(105);
    private readonly TextBlock _originLine = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
    private readonly Button _openAnalysis = new() { Classes = { "link" }, Content = "Open this saved run in Stylometry →", IsVisible = false };
    private readonly Button _sourceFiles = new() { Classes = { "link" }, Content = "Source files & page notes →", IsVisible = false };
    private readonly TextBox _interpretation = Ui.Area(80, readOnly: true);
    private readonly TextBox _generatorPrompt = Ui.Area(72, readOnly: true);
    private readonly TextBox _researcherNote = Ui.Area(90);
    private readonly TextBlock _statusLine = Ui.Status();

    private List<ResearchProject> _projectItems = new();
    private List<ResearchQuestion> _questionItems = new();
    private List<EvidenceItem> _evidenceItems = new();
    private EvidenceItem? _editingEvidence;
    private bool _suspendFilterReload;
    private bool _loadingProjects;
    private bool _loadingEvidence;

    public ResearchBenchWindow(IReaderHost host, Work work, string authorName, long initialProjectId = 0) : base(host)
    {
        _work = work;
        _authorName = authorName;
        _initialProjectId = initialProjectId;
        Title = $"Research Bench — {work.Title}";
        Width = 1320;
        Height = 840;
        MinWidth = 1180;
        MinHeight = 640;

        _statusFilter = new ComboBox { ItemsSource = StatusFilter.Choices, SelectedIndex = 0, MinWidth = 140 };
        _projects = BuildProjectList();
        _questions = Ui.TextList<ResearchQuestion>(q => q.Text);
        _archiveToggle = Ui.Button("Archive", "Archive");
        _evidence = BuildEvidenceTable();
        _projectHeader = BuildProjectHeader();

        var main = new DockPanel();
        var toolbar = BuildEvidenceToolbar();
        DockPanel.SetDock(_projectHeader, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        main.Children.Add(_projectHeader);
        main.Children.Add(toolbar);
        main.Children.Add(Ui.Split(_evidence, BuildInspector(), 560, 300, 400, rightWidth: 474));

        var status = Ui.StatusBar(_statusLine);
        var sidebar = Ui.Sidebar(BuildSidebar(), 286);
        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(status);
        root.Children.Add(sidebar);
        root.Children.Add(main);
        Content = root;

        Opened += (_, _) => Run(() => LoadProjectsAsync(_initialProjectId));
    }

    private ResearchProject? CurrentProject => _projects.SelectedItem as ResearchProject;

    private StatusFilter CurrentFilter => _statusFilter.SelectedItem as StatusFilter ?? StatusFilter.Current;

    // Read the selection: the inspector must always describe the highlighted evidence.
    private EvidenceItem? CurrentEvidence => (_evidence.SelectedItem as EvidenceRow)?.Item;

    // ── Layout ────────────────────────────────────────────────────────────

    private ListBox BuildProjectList()
    {
        var list = new ListBox
        {
            Height = 190,
            // Mark anything not active at display time rather than in
            // ResearchProject.ToString, which the dossier export and several
            // menus also render.
            ItemTemplate = new FuncDataTemplate<ResearchProject>((p, _) =>
            {
                if (p == null) return new TextBlock();
                var stack = new StackPanel { Spacing = 1, Margin = new Thickness(2, 3) };
                stack.Children.Add(new TextBlock { Text = p.Name, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis });
                if (p.Status != ResearchProjectStatus.Active)
                    stack.Children.Add(new TextBlock { Text = StatusFilter.Describe(p.Status), Classes = { "caption" } });
                return stack;
            }, supportsRecycling: false)
        };
        list.SelectionChanged += (_, _) =>
        {
            if (!_loadingProjects) Run(ProjectChangedAsync);
        };
        return list;
    }

    private Control BuildSidebar()
    {
        _statusFilter.SelectionChanged += (_, _) =>
        {
            if (_suspendFilterReload) return;

            // Keep the open project selected only if the new filter still admits it,
            // or LoadProjectsAsync would widen the list straight back to honour it and
            // choosing a filter would appear to do nothing.
            var open = CurrentProject;
            var keep = open != null && CurrentFilter.Admits(open.Status);
            Run(() => LoadProjectsAsync(keep ? open!.ResearchProjectId : 0));
        };

        var filterRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var show = new TextBlock { Text = "Show:", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var newProject = Ui.Glyph("Plus", "New project… (" + Ui.Chord("N", shift: true) + ")");
        newProject.Click += (_, _) => Run(NewProjectAsync);
        DockPanel.SetDock(show, Dock.Left);
        DockPanel.SetDock(newProject, Dock.Right);
        filterRow.Children.Add(show);
        filterRow.Children.Add(newProject);
        filterRow.Children.Add(_statusFilter);

        var add = Ui.Glyph("Plus", "Add a research question…");
        var edit = Ui.Glyph("Feather", "Edit the selected question…");
        var remove = Ui.Glyph("Minus", "Remove the selected question");
        var up = Ui.Glyph("ArrowUp", "Move up (" + Ui.Chord("↑") + ")");
        var down = Ui.Glyph("ArrowDown", "Move down (" + Ui.Chord("↓") + ")");
        add.Click += (_, _) => Run(AddQuestionAsync);
        edit.Click += (_, _) => Run(EditQuestionAsync);
        remove.Click += (_, _) => Run(RemoveQuestionAsync);
        up.Click += (_, _) => Run(() => MoveQuestionAsync(-1));
        down.Click += (_, _) => Run(() => MoveQuestionAsync(1));
        _questions.DoubleTapped += (_, _) => Run(EditQuestionAsync);
        var questionTools = Ui.Row(add, edit, remove, up, down);
        questionTools.Spacing = 2;
        questionTools.Margin = new Thickness(0, 6, 0, 0);

        var questionsLabel = Ui.Section("Research questions");
        questionsLabel.Margin = new Thickness(0, 16, 0, 6);

        var dock = new DockPanel { Margin = new Thickness(12, 14, 12, 10) };
        var projectsLabel = Ui.Section("Projects for this work");
        foreach (var top in new Control[] { projectsLabel, filterRow, _projects, questionsLabel })
        {
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
        }

        DockPanel.SetDock(questionTools, Dock.Bottom);
        dock.Children.Add(questionTools);
        dock.Children.Add(_questions);
        return dock;
    }

    private Control BuildProjectHeader()
    {
        var attribution = _work.AttributionStatus switch
        {
            ClassicaCodex.Core.AttributionStatus.Disputed => "attribution disputed",
            ClassicaCodex.Core.AttributionStatus.Spurious => "not attributed to this author",
            _ => "accepted attribution"
        };
        var workLabel = new TextBlock
        {
            Text = $"{_authorName}, {_work.Title}  •  {_work.CtsUrn}  •  {attribution}",
            Classes = { "caption" },
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _theory.FontSize = 15;
        _theory.MinHeight = 32;

        var save = Ui.Button("Save Project", "Check", accent: true, tip: "Save the theory and notes (" + Ui.Chord("S") + " while editing them)");
        var create = Ui.Button("New Project…", "Plus", tip: "Start a project from a new working theory (" + Ui.Chord("N", shift: true) + ")");
        var suggest = Ui.Button("Let AI Suggest a New Project…", "Sparkle");
        save.Click += (_, _) => Run(SaveProjectAsync);
        create.Click += (_, _) => Run(NewProjectAsync);
        // One button, reflecting the state it would change. Archiving used to be a
        // one-way door: nothing in the Bench listed an archived project afterwards, so
        // "retained, not deleted" was true of the database and false of the interface.
        _archiveToggle.Click += (_, _) => Run(ToggleArchiveAsync);
        suggest.Click += (_, _) => Run(SuggestProjectAsync);

        // The buttons keep to the right and the theory gives way, so nothing runs
        // off the window at its smallest size.
        var theoryRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var buttons = Ui.Row(save, create, _archiveToggle, suggest);
        buttons.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(buttons, Dock.Right);
        theoryRow.Children.Add(buttons);
        theoryRow.Children.Add(_theory);

        var stack = new StackPanel();
        stack.Children.Add(workLabel);
        stack.Children.Add(theoryRow);
        stack.Children.Add(_projectNotes);

        var header = new Border { Padding = new Thickness(18, 14, 18, 14), BorderThickness = new Thickness(0, 0, 0, 1), Child = stack };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("Cc.PageHeader"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Cc.Rule"));
        return header;
    }

    private Control BuildEvidenceToolbar()
    {
        var add = Ui.Button("New Evidence", "Plus", tip: "Describe a new piece of evidence in the inspector (" + Ui.Chord("N") + ")");
        var remove = Ui.Button("Remove", "Minus");
        add.Click += (_, _) => NewEvidence();
        remove.Click += (_, _) => Run(RemoveEvidenceAsync);

        var project = Ui.MenuButton("Project", "Notebook", BuildProjectMenu());
        var gather = Ui.MenuButton("Gather Evidence", "Lightbulb", BuildGatherMenu());

        var left = Ui.Row(add, remove);
        var right = Ui.Row(project, gather);
        var dock = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(left);

        var bar = new Border { Child = dock, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Cc.Rule"));
        return bar;
    }

    private DataGrid BuildEvidenceTable()
    {
        var grid = Ui.Table();
        grid.Columns.Add(Ui.Column("Review", nameof(EvidenceRow.Review), 84));
        grid.Columns.Add(Ui.Column("Evidence", nameof(EvidenceRow.Title)));
        grid.Columns.Add(Ui.Column("Origin", nameof(EvidenceRow.Origin), 96));
        grid.Columns.Add(Ui.Column("Type", nameof(EvidenceRow.Type), 100));
        grid.Columns.Add(Ui.Column("Reference", nameof(EvidenceRow.Reference), 96));
        grid.Margin = new Thickness(10);
        grid.SelectionChanged += (_, _) =>
        {
            if (!_loadingEvidence) ShowEvidence(CurrentEvidence);
        };
        return grid;
    }

    private Control BuildInspector()
    {
        _excerpt.Bind(TextBox.FontFamilyProperty, _excerpt.GetResourceObservable("Cc.SourceFont"));
        _excerpt.FontSize = 14;
        _openAnalysis.Click += (_, _) => OpenAttachedStylometryRun();
        _sourceFiles.Click += (_, _) => OpenEvidenceSources();

        var form = new StackPanel();
        form.Children.Add(Ui.Section("Evidence"));
        form.Children.Add(Ui.Field("Evidence title", _title));
        form.Children.Add(Ui.Pair(Ui.Field("Evidence type", _type), Ui.Field("Research question", _question)));
        form.Children.Add(Ui.Pair(Ui.Field("Relationship", _relationship), Ui.Field("Human review", _judgment)));
        form.Children.Add(Ui.Field("Source system/type (CTS, DOI, museum, inscription…)", _sourceType));
        form.Children.Add(Ui.Pair(Ui.Field("Stable identifier", _stableId), Ui.Field("Canonical reference / page / passage", _reference)));
        form.Children.Add(Ui.Field("Source and provenance details", _provenance));
        form.Children.Add(Ui.Field("Raw excerpt or factual summary", _excerpt));
        form.Children.Add(_originLine);
        form.Children.Add(_sourceFiles);
        form.Children.Add(_openAnalysis);
        _sourceFiles.Margin = new Thickness(0, 0, 0, 6);
        _openAnalysis.Margin = new Thickness(0, 0, 0, 10);
        form.Children.Add(Ui.Field("App / AI interpretation (not raw evidence)", _interpretation));
        form.Children.Add(Ui.Field("Generation prompt and corpus scope", _generatorPrompt));
        form.Children.Add(Ui.Field("Researcher note / interpretation", _researcherNote));

        var save = Ui.Button("Save Evidence", "Check", accent: true, tip: "Save the evidence in the inspector (" + Ui.Chord("S") + ")");
        save.Click += (_, _) => Run(SaveEvidenceAsync);
        var bar = Ui.ButtonBar(null, save);

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(Ui.Scroll(form));
        return dock;
    }

    protected override bool OnCommandKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.S:
                // Save what is being typed in: the theory and notes when the
                // header has the keyboard, the evidence otherwise.
                Run(_projectHeader.IsKeyboardFocusWithin ? SaveProjectAsync : SaveEvidenceAsync);
                return true;
            case Key.N when shift:
                Run(NewProjectAsync);
                return true;
            case Key.N:
                NewEvidence();
                return true;
            case Key.Up when _questions.IsKeyboardFocusWithin:
                Run(() => MoveQuestionAsync(-1));
                return true;
            case Key.Down when _questions.IsKeyboardFocusWithin:
                Run(() => MoveQuestionAsync(1));
                return true;
            default:
                return false;
        }
    }

    // ── Projects ──────────────────────────────────────────────────────────

    internal async Task LoadProjectsAsync(long selectId = 0)
    {
        var all = await _repo.GetProjectsForWorkAsync(_work.WorkId, includeArchived: true, workCtsUrn: _work.CtsUrn);
        var items = all.Where(p => CurrentFilter.Admits(p.Status)).ToList();

        // Archiving is retention, not deletion, so a caller that names a project - a
        // passage inquiry opening the project it was promoted into - must still be able
        // to reach it. Widening the filter rather than quietly ignoring it keeps the
        // menu honest about what the list is showing.
        var widened = false;
        if (selectId != 0 && items.All(p => p.ResearchProjectId != selectId) && all.Any(p => p.ResearchProjectId == selectId))
        {
            items = all;
            SetFilterSilently(StatusFilter.Everything);
            widened = true;
        }

        _projectItems = items;
        _loadingProjects = true;
        try
        {
            _projects.ItemsSource = items;
            _projects.SelectedItem = (selectId != 0 ? items.FirstOrDefault(p => p.ResearchProjectId == selectId) : null)
                                     ?? items.FirstOrDefault();
        }
        finally
        {
            _loadingProjects = false;
        }

        if (items.Count == 0)
        {
            ClearProject();
            _statusLine.Text = "No projects yet. Create one from a working theory.";
            return;
        }

        await ProjectChangedAsync();

        if (widened && CurrentProject is { } wanted && wanted.ResearchProjectId == selectId)
            _statusLine.Text = $"“{wanted.Name}” is {StatusFilter.Describe(wanted.Status)}. The filter has been set to All so it is visible.";
    }

    private async Task ProjectChangedAsync()
    {
        var project = CurrentProject;
        if (project == null)
        {
            ClearProject();
            return;
        }

        _theory.Text = project.Name;
        UpdateArchiveButton(project);
        _projectNotes.Text = project.Notes ?? string.Empty;
        _questionItems = await _repo.GetQuestionsAsync(project.ResearchProjectId);
        _questions.ItemsSource = _questionItems;
        RefreshQuestionChoices(_questionItems);
        await LoadEvidenceAsync(project.ResearchProjectId);
        _statusLine.Text = $"Opened {project.Name} — last updated {project.UpdatedUtc.ToLocalTime():g}.";
    }

    internal async Task NewProjectAsync()
    {
        var name = await PromptAsync("New research project", "Working theory or research question:");
        if (name == null) return;

        var project = new ResearchProject { WorkId = _work.WorkId, WorkCtsUrn = _work.CtsUrn, Name = name };
        await _repo.SaveProjectAsync(project);
        await LoadProjectsAsync(project.ResearchProjectId);
    }

    internal async Task SaveProjectAsync()
    {
        var project = CurrentProject;
        if (project == null)
        {
            await NewProjectAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(_theory.Text))
        {
            await Alert.Info(this, "Research Bench", "Enter a working theory.");
            return;
        }

        project.Name = _theory.Text.Trim();
        project.Notes = Ui.Clean(_projectNotes.Text);
        await _repo.SaveProjectAsync(project);
        await LoadProjectsAsync(project.ResearchProjectId);
        _statusLine.Text = "Project saved.";
    }

    /// <summary>
    /// Sets the open project's status. The Archive button is the shortcut for the
    /// common case; the Project menu reaches the rest.
    /// </summary>
    internal async Task SetProjectStatusAsync(ResearchProjectStatus status)
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
    /// the reload its own selection handler would run.
    /// </summary>
    private void SetFilterSilently(StatusFilter filter)
    {
        if (ReferenceEquals(CurrentFilter, filter)) return;
        _suspendFilterReload = true;
        try
        {
            _statusFilter.SelectedItem = filter;
        }
        finally
        {
            _suspendFilterReload = false;
        }
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

        if (!await ConfirmAsync("Archive project", $"Archive “{project.Name}”? Its questions and evidence will be retained.", "Archive"))
            return;

        // Keep it selected rather than reloading to nothing, so the researcher can see
        // what happened and press Restore straight away if it was a mistake.
        await SetProjectStatusAsync(ResearchProjectStatus.Archived);
    }

    private void UpdateArchiveButton(ResearchProject? project)
    {
        var archived = project?.Status == ResearchProjectStatus.Archived;
        _archiveToggle.Content = Ui.Row(Glyphs.Icon(archived ? "Refresh" : "Archive", 14), new TextBlock { Text = archived ? "Restore" : "Archive", VerticalAlignment = VerticalAlignment.Center });
        ((StackPanel)_archiveToggle.Content).Spacing = 6;
    }

    /// <summary>
    /// The open project, or null once the researcher has been told one is needed.
    /// Six of the Project and Gather menu items used to do nothing at all without
    /// one, while their neighbours said why.
    /// </summary>
    private async Task<ResearchProject?> RequireProjectAsync()
    {
        if (CurrentProject is { } project) return project;
        await Alert.Info(this, "Research Bench", "Open or create a research project first.");
        return null;
    }

    private void ClearProject()
    {
        _theory.Text = string.Empty;
        _projectNotes.Text = string.Empty;
        _questionItems = new List<ResearchQuestion>();
        _questions.ItemsSource = _questionItems;
        _evidenceItems = new List<EvidenceItem>();
        _evidence.ItemsSource = null;
        RefreshQuestionChoices(Array.Empty<ResearchQuestion>());
        ShowEvidence(null);
        UpdateArchiveButton(null);
    }

    // ── Questions ─────────────────────────────────────────────────────────

    internal async Task AddQuestionAsync()
    {
        var project = CurrentProject;
        if (project == null) return;

        var text = await PromptAsync("Research question", "What needs to be established?");
        if (text == null) return;

        var question = new ResearchQuestion
        {
            ResearchProjectId = project.ResearchProjectId,
            Text = text,
            SortOrder = _questionItems.Count
        };
        await _repo.SaveQuestionAsync(question);
        await ReloadQuestionsAsync(question.ResearchQuestionId);
    }

    private async Task EditQuestionAsync()
    {
        if (_questions.SelectedItem is not ResearchQuestion item) return;

        var text = await PromptAsync("Edit research question", "Question:", item.Text);
        if (text == null) return;

        item.Text = text;
        await _repo.SaveQuestionAsync(item);
        await ReloadQuestionsAsync(item.ResearchQuestionId);
    }

    internal async Task RemoveQuestionAsync()
    {
        if (_questions.SelectedItem is not ResearchQuestion item) return;
        if (!await ConfirmAsync("Remove question", "Remove this question? Linked evidence will remain in the project.", "Remove"))
            return;

        await _repo.DeleteQuestionAsync(item.ResearchQuestionId);
        await ReloadQuestionsAsync();
        if (CurrentProject != null) await LoadEvidenceAsync(CurrentProject.ResearchProjectId);
    }

    internal async Task MoveQuestionAsync(int delta)
    {
        if (_questions.SelectedItem is not ResearchQuestion selected) return;

        var list = _questionItems.ToList();
        var index = list.IndexOf(selected);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;

        (list[index], list[target]) = (list[target], list[index]);
        await _repo.ReorderQuestionsAsync(list.Select(q => q.ResearchQuestionId).ToList());
        await ReloadQuestionsAsync(selected.ResearchQuestionId);
    }

    private async Task ReloadQuestionsAsync(long selectId = 0)
    {
        if (CurrentProject == null) return;

        _questionItems = await _repo.GetQuestionsAsync(CurrentProject.ResearchProjectId);
        _questions.ItemsSource = _questionItems;
        _questions.SelectedItem = _questionItems.FirstOrDefault(q => q.ResearchQuestionId == selectId);
        RefreshQuestionChoices(_questionItems);
    }

    private void RefreshQuestionChoices(IEnumerable<ResearchQuestion> questions)
    {
        var selected = Ui.Value<long?>(_question);
        Ui.SetChoices(_question, Ui.QuestionChoices("General project evidence", questions), selected);
    }

    // ── Evidence ──────────────────────────────────────────────────────────

    internal async Task LoadEvidenceAsync(long projectId, long selectId = 0)
    {
        _evidenceItems = await _repo.GetEvidenceAsync(projectId);
        var rows = _evidenceItems.Select(e => new EvidenceRow(e)).ToList();

        _loadingEvidence = true;
        try
        {
            _evidence.ItemsSource = rows;
            var wanted = (selectId != 0 ? rows.FirstOrDefault(r => r.Item.EvidenceItemId == selectId) : null) ?? rows.FirstOrDefault();
            Ui.Reveal(_evidence, wanted);
        }
        finally
        {
            _loadingEvidence = false;
        }

        // Bound explicitly to the row just selected rather than left to the
        // selection event, which does not fire when the selection is unchanged.
        ShowEvidence(CurrentEvidence);
    }

    private void NewEvidence()
    {
        if (CurrentProject == null) return;

        _loadingEvidence = true;
        try
        {
            _evidence.SelectedItem = null;
        }
        finally
        {
            _loadingEvidence = false;
        }

        ShowEvidence(new EvidenceItem { ResearchProjectId = CurrentProject.ResearchProjectId, SortOrder = _evidenceItems.Count });
        _title.Focus();
    }

    internal async Task SaveEvidenceAsync()
    {
        var project = CurrentProject;
        if (project == null) return;

        var item = _editingEvidence ?? new EvidenceItem { ResearchProjectId = project.ResearchProjectId, SortOrder = _evidenceItems.Count };
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            await Alert.Info(this, "Research Bench", "Evidence needs a title.");
            return;
        }

        item.Title = _title.Text.Trim();
        item.Type = Ui.Value<EvidenceType>(_type);
        item.ResearchQuestionId = Ui.Value<long?>(_question);
        item.Relationship = Ui.Value<EvidenceRelationship>(_relationship);
        item.Judgment = Ui.Value<EvidenceJudgment>(_judgment);
        item.SourceType = Ui.Clean(_sourceType.Text);
        item.StableIdentifier = Ui.Clean(_stableId.Text);
        item.CanonicalReference = Ui.Clean(_reference.Text);
        item.Provenance = Ui.Clean(_provenance.Text);
        item.Excerpt = Ui.Clean(_excerpt.Text);
        item.ResearcherNote = Ui.Clean(_researcherNote.Text);
        await _repo.SaveEvidenceAsync(item);
        await LoadEvidenceAsync(project.ResearchProjectId, item.EvidenceItemId);
        _statusLine.Text = "Evidence saved. Review judgment and researcher interpretation remain explicit.";
    }

    private async Task RemoveEvidenceAsync()
    {
        var item = CurrentEvidence;
        if (item == null || CurrentProject == null) return;
        if (!await ConfirmAsync("Remove evidence", $"Remove “{item.Title}”?", "Remove", AlertKind.Warning)) return;

        await _repo.DeleteEvidenceAsync(item.EvidenceItemId);
        await LoadEvidenceAsync(CurrentProject.ResearchProjectId);
    }

    private void ShowEvidence(EvidenceItem? item)
    {
        _editingEvidence = item;
        _title.Text = item?.Title ?? string.Empty;
        Ui.Select(_type, item?.Type ?? EvidenceType.PrimaryText);
        Ui.Select(_question, item?.ResearchQuestionId);
        Ui.Select(_relationship, item?.Relationship ?? EvidenceRelationship.Contextualizes);
        Ui.Select(_judgment, item?.Judgment ?? EvidenceJudgment.Uncertain);
        _sourceType.Text = item?.SourceType ?? string.Empty;
        _stableId.Text = item?.StableIdentifier ?? string.Empty;
        _reference.Text = item?.CanonicalReference ?? string.Empty;
        _provenance.Text = item?.Provenance ?? string.Empty;
        _excerpt.Text = item?.Excerpt ?? string.Empty;
        _originLine.Text = item == null
            ? "Origin: manual evidence"
            : $"Origin: {Words.Of(item.Origin)}" + (string.IsNullOrWhiteSpace(item.InterpretationAuthor)
                ? string.Empty
                : $" — interpretation by {item.InterpretationAuthor}");
        _openAnalysis.Tag = ResearchLinks.GetStylometryRunId(item);
        _openAnalysis.IsVisible = _openAnalysis.Tag is long;
        _sourceFiles.IsVisible = item?.EvidenceItemId > 0;
        _interpretation.Text = item?.Interpretation ?? string.Empty;
        _generatorPrompt.Text = item?.GeneratorPrompt ?? string.Empty;
        _researcherNote.Text = item?.ResearcherNote ?? string.Empty;
    }

    /// <summary>Selects an evidence row and scrolls to it - the audit's "Open item".</summary>
    private void RevealEvidence(long evidenceId)
    {
        if (_evidence.ItemsSource is not IEnumerable<EvidenceRow> rows) return;
        var row = rows.FirstOrDefault(r => r.Item.EvidenceItemId == evidenceId);
        if (row == null) return;

        _loadingEvidence = true;
        try
        {
            Ui.Reveal(_evidence, row);
        }
        finally
        {
            _loadingEvidence = false;
        }

        ShowEvidence(row.Item);
    }

    /// <summary>An evidence item as the table shows it.</summary>
    private sealed class EvidenceRow
    {
        public EvidenceRow(EvidenceItem item) => Item = item;

        public EvidenceItem Item { get; }
        public string Review => Words.Of(Item.Judgment);
        public string Title => Item.Title;

        public string Origin => Item.Origin switch
        {
            EvidenceOrigin.ClassicaCodexAnalysis => "App analysis",
            EvidenceOrigin.AiCandidate => "AI candidate",
            _ => "Manual"
        };

        public string Type => Words.Of(Item.Type);
        public string Reference => Item.CanonicalReference ?? string.Empty;
    }

    // ── What the tests read ───────────────────────────────────────────────

    internal IReadOnlyList<ResearchProject> Projects => _projectItems;
    internal ResearchProject? OpenProject => CurrentProject;
    internal IReadOnlyList<ResearchQuestion> Questions => _questionItems;
    internal IReadOnlyList<EvidenceItem> Evidence => _evidenceItems;
    internal EvidenceItem? EditingEvidence => _editingEvidence;
    internal string StatusText => _statusLine.Text ?? string.Empty;

    internal void SelectQuestion(long questionId) =>
        _questions.SelectedItem = _questionItems.FirstOrDefault(q => q.ResearchQuestionId == questionId);

    internal void SetTheory(string theory, string? notes = null)
    {
        _theory.Text = theory;
        if (notes != null) _projectNotes.Text = notes;
    }

    internal void EditEvidence(string title, string? excerpt = null, string? note = null, EvidenceJudgment? judgment = null)
    {
        _title.Text = title;
        if (excerpt != null) _excerpt.Text = excerpt;
        if (note != null) _researcherNote.Text = note;
        if (judgment is { } j) Ui.Select(_judgment, j);
    }

    internal void StartNewEvidence() => NewEvidence();
}
