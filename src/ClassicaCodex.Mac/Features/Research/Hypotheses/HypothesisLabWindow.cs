using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Research.Bibliography;
using ClassicaCodex.Mac.Features.Research.Echoes;
using ClassicaCodex.Mac.Features.Research.Reading;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Hypotheses;

/// <summary>Compares rival explanations against the saved record and turns uncertainty into explicit tests.</summary>
internal sealed class HypothesisLabWindow : ResearchWindow
{
    private const string MatrixColumns = "56,92,2*,80,3*,176,104,2*";

    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly string _author;
    private readonly ResearchHypothesisRepository _repo = new();
    private readonly ResearchRepository _research = new();
    private readonly ListBox _hypotheses;
    private readonly TextBox _title = Ui.Line();
    private readonly TextBox _statement = Ui.Area(110);
    private readonly TextBox _hypothesisNote = Ui.Area(180);
    private readonly ComboBox _hypothesisStatus = Ui.EnumCombo<ResearchHypothesisStatus>();
    private readonly TextBlock _origin = Ui.Caption(string.Empty);
    private readonly ContentControl _overview = new();
    private readonly ListBox _matrix;
    private readonly DataGrid _experiments = Ui.Table();
    private readonly TextBox _experimentTitle = Ui.Line();
    private readonly TextBox _predicted = Ui.Area(90);
    private readonly TextBox _falsification = Ui.Area(90);
    private readonly TextBox _experimentNote = Ui.Area(90);
    private readonly ComboBox _method = Ui.EnumCombo<ResearchExperimentMethod>();
    private readonly ComboBox _experimentStatus = Ui.EnumCombo<ResearchExperimentStatus>();
    private readonly ComboBox _experimentHypothesis = Ui.Combo(new[] { new Choice<long?>(null, "(project-wide)") });
    private readonly TabControl _tabs = new() { Margin = new Thickness(14, 0, 14, 0) };
    private readonly TextBlock _status = Ui.Status();
    private ResearchHypothesis? _editingHypothesis;
    private ResearchExperiment? _editingExperiment;
    private List<ResearchHypothesis> _hypothesisData = new();
    private List<HypothesisSource> _sources = new();
    private List<ResearchExperiment> _experimentData = new();
    private List<MatrixRow> _matrixRows = new();
    private bool _loading;

    public HypothesisLabWindow(IReaderHost? host, ResearchProject project, Work work, string author) : base(host)
    {
        _project = project;
        _work = work;
        _author = author;
        Title = $"Hypothesis Lab — {project.Name}";
        Width = 1500;
        Height = 880;
        MinWidth = 1180;
        MinHeight = 680;

        var header = Ui.Notice("Compare explanations, state what would count against them, and distinguish diagnostic evidence from material that fits every theory.");
        header.Margin = new Thickness(16, 12, 16, 10);

        _hypotheses = Ui.TextList<ResearchHypothesis>(h => h.Title, h => Words.Of(h.Status) + (h.Origin == EvidenceOrigin.AiCandidate ? " · accepted AI proposal" : string.Empty));
        _hypotheses.SelectionChanged += (_, _) =>
        {
            if (!_loading) Run(() => SelectHypothesisAsync(_hypotheses.SelectedItem as ResearchHypothesis));
        };
        _matrix = Ui.Compact(new ListBox { ItemTemplate = new FuncDataTemplate<MatrixRow>((row, _) => row == null ? new TextBlock() : BuildMatrixRow(row), supportsRecycling: false) }, 5);

        _tabs.Items.Add(new TabItem { Header = "Comparison Overview", Content = BuildOverview() });
        _tabs.Items.Add(new TabItem { Header = "Hypothesis", Content = BuildDetails() });
        _tabs.Items.Add(new TabItem { Header = "Assess Selected Hypothesis", Content = BuildMatrix() });
        _tabs.Items.Add(new TabItem { Header = "Falsification Experiments", Content = BuildExperiments() });

        var status = Ui.StatusBar(_status);
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(status);
        dock.Children.Add(Ui.Split(BuildLeft(), _tabs, 320, 260, 820));
        Content = dock;

        Opened += (_, _) => Run(() => ReloadAsync());
    }

    private Control BuildLeft()
    {
        var add = Ui.Button("New", "Plus", tip: "Write a new hypothesis (" + Ui.Chord("N") + ")");
        var remove = Ui.Button("Remove", "Minus");
        var challenge = Ui.Button("AI Challenge…", "Sparkle");
        add.Click += (_, _) => NewHypothesis();
        remove.Click += (_, _) => Run(RemoveHypothesisAsync);
        challenge.Click += (_, _) => Run(ChallengeAsync);

        var tools = new WrapPanel { Margin = new Thickness(10, 12, 10, 8) };
        foreach (var button in new[] { add, remove, challenge })
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            tools.Children.Add(button);
        }

        var dock = new DockPanel();
        DockPanel.SetDock(tools, Dock.Top);
        dock.Children.Add(tools);
        _hypotheses.Margin = new Thickness(6, 0, 6, 8);
        dock.Children.Add(_hypotheses);
        return Ui.Sidebar(dock, double.NaN);
    }

    private Control BuildOverview()
    {
        var note = Ui.Muted("Each hypothesis is a column. Blank means not yet assessed; ‘does not discriminate’ records material that fits the rival explanations equally well.");
        note.Margin = new Thickness(4, 12, 4, 10);
        var scroll = new ScrollViewer
        {
            Content = _overview,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var frame = new Border { Child = scroll, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Cc.Rule"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Cc.Page"));

        var dock = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        dock.Children.Add(note);
        dock.Children.Add(frame);
        return dock;
    }

    private Control BuildDetails()
    {
        var save = Ui.Button("Save Hypothesis", "Check", accent: true, tip: "Save the hypothesis (" + Ui.Chord("S") + ")");
        save.Click += (_, _) => Run(SaveHypothesisAsync);

        var statusRow = new DockPanel();
        var statusField = Ui.Field("Researcher status", _hypothesisStatus, 0);
        statusField.Width = 260;
        DockPanel.SetDock(statusField, Dock.Left);
        _origin.VerticalAlignment = VerticalAlignment.Bottom;
        _origin.Margin = new Thickness(16, 0, 0, 6);
        statusRow.Children.Add(statusField);
        statusRow.Children.Add(_origin);

        var form = new StackPanel();
        form.Children.Add(Ui.Field("Short title", _title));
        form.Children.Add(Ui.Field("Testable statement", _statement));
        form.Children.Add(statusRow);
        form.Children.Add(new Border { Height = 12 });
        form.Children.Add(Ui.Field("Researcher note", _hypothesisNote));
        form.Children.Add(save);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        return Ui.Scroll(form, new Thickness(4, 14, 4, 14));
    }

    private Control BuildMatrix()
    {
        var note = Ui.Muted("Link only sources you have weighed. “Does not discriminate” is important: it records evidence compatible with several rivals without pretending it favors one.");
        note.Margin = new Thickness(4, 12, 4, 10);

        var columnHeader = new Grid { ColumnDefinitions = new ColumnDefinitions(MatrixColumns), Margin = new Thickness(10, 7) };
        foreach (var (text, column) in new[] { ("Assess", 0), ("Kind", 1), ("Source", 2), ("Review", 3), ("Recorded material", 4), ("Bearing", 5), ("Strength", 6), ("Researcher assessment", 7) })
        {
            var label = new TextBlock { Text = text, Classes = { "caption" }, FontWeight = FontWeight.SemiBold };
            Grid.SetColumn(label, column);
            columnHeader.Children.Add(label);
        }

        var headerBorder = new Border { Child = columnHeader, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Cc.PageHeader"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Cc.Rule"));

        var table = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        table.Children.Add(headerBorder);
        table.Children.Add(_matrix);
        var frame = new Border { Child = table, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Cc.Rule"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Cc.Page"));

        var save = Ui.Button("Save Assessments", "Check", accent: true);
        save.Click += (_, _) => Run(SaveAssessmentsAsync);
        save.Margin = new Thickness(0, 10, 0, 10);

        var dock = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(save, Dock.Bottom);
        dock.Children.Add(note);
        dock.Children.Add(save);
        dock.Children.Add(frame);
        return dock;
    }

    private Control BuildMatrixRow(MatrixRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(MatrixColumns) };
        var linked = new CheckBox { IsChecked = row.Linked, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        linked.IsCheckedChanged += (_, _) => row.Linked = linked.IsChecked == true;
        var bearing = Ui.EnumCombo<HypothesisRelationship>();
        Ui.Select(bearing, row.Relationship);
        bearing.SelectionChanged += (_, _) => row.Relationship = Ui.Value<HypothesisRelationship>(bearing);
        var strength = Ui.EnumCombo<HypothesisStrength>();
        Ui.Select(strength, row.Strength);
        strength.SelectionChanged += (_, _) => row.Strength = Ui.Value<HypothesisStrength>(strength);
        var note = new TextBox { Text = row.Note, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 30 };
        note.TextChanged += (_, _) => row.Note = note.Text ?? string.Empty;

        void Add(Control control, int column)
        {
            control.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        Add(linked, 0);
        Add(Cell(Words.Of(row.Source.Kind)), 1);
        Add(Cell(row.Source.Title, wrap: true), 2);
        Add(Cell(row.Source.ReviewState), 3);
        var detail = Cell(row.Source.Detail, wrap: true);
        ToolTip.SetTip(detail, row.Source.Detail);
        Add(detail, 4);
        Add(bearing, 5);
        Add(strength, 6);
        Add(note, 7);
        return grid;
    }

    private static TextBlock Cell(string text, bool wrap = false) => new()
    {
        Text = text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = wrap ? 3 : 1,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Control BuildExperiments()
    {
        var add = Ui.Button("New", "Plus");
        var remove = Ui.Button("Remove", "Minus");
        add.Click += (_, _) => ShowExperiment(new ResearchExperiment { ResearchProjectId = _project.ResearchProjectId, SortOrder = _experimentData.Count });
        remove.Click += (_, _) => Run(RemoveExperimentAsync);

        _experiments.Columns.Add(Ui.Column("Status", nameof(ExperimentRow.Status), 90));
        _experiments.Columns.Add(Ui.Column("Method", nameof(ExperimentRow.Method), 130));
        _experiments.Columns.Add(Ui.Column("Experiment", nameof(ExperimentRow.Title)));
        _experiments.SelectionChanged += (_, _) =>
        {
            if (!_loading) ShowExperiment((_experiments.SelectedItem as ExperimentRow)?.Experiment);
        };

        var listTools = Ui.Row(add, remove);
        listTools.Margin = new Thickness(0, 12, 0, 8);
        var list = new DockPanel { Margin = new Thickness(0, 0, 10, 10) };
        DockPanel.SetDock(listTools, Dock.Top);
        list.Children.Add(listTools);
        list.Children.Add(_experiments);

        var save = Ui.Button("Save Experiment", "Check", accent: true);
        var open = Ui.Button("Open Method Tool", "OpenExternal");
        save.Click += (_, _) => Run(SaveExperimentAsync);
        open.Click += (_, _) => Run(OpenMethodAsync);

        var form = new StackPanel();
        form.Children.Add(Ui.Field("Experiment", _experimentTitle));
        form.Children.Add(Ui.Pair(Ui.Field("Method", _method), Ui.Field("Status", _experimentStatus)));
        form.Children.Add(Ui.Field("Tests hypothesis", _experimentHypothesis));
        form.Children.Add(Ui.Field("Predicted result if the linked explanation is right", _predicted));
        form.Children.Add(Ui.Field("Result that would count against it", _falsification));
        form.Children.Add(Ui.Field("Researcher note", _experimentNote));
        form.Children.Add(Ui.Row(save, open));

        return Ui.Split(list, Ui.Scroll(form, new Thickness(14, 12, 4, 12)), 360, 260, 460);
    }

    protected override bool OnCommandKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.S when _tabs.SelectedIndex == 2:
                Run(SaveAssessmentsAsync);
                return true;
            case Key.S when _tabs.SelectedIndex == 3:
                Run(SaveExperimentAsync);
                return true;
            case Key.S:
                Run(SaveHypothesisAsync);
                return true;
            case Key.N:
                NewHypothesis();
                return true;
            default:
                return false;
        }
    }

    internal async Task ReloadAsync(long selectHypothesis = 0, long selectExperiment = 0)
    {
        _hypothesisData = await _repo.GetHypothesesAsync(_project.ResearchProjectId);
        _sources = await _repo.GetSourcesAsync(_project.ResearchProjectId);
        _experimentData = await _repo.GetExperimentsAsync(_project.ResearchProjectId);

        _loading = true;
        ExperimentRow? experiment;
        try
        {
            _hypotheses.ItemsSource = _hypothesisData;
            _hypotheses.SelectedItem = (selectHypothesis > 0 ? _hypothesisData.FirstOrDefault(h => h.ResearchHypothesisId == selectHypothesis) : null)
                                       ?? _hypothesisData.FirstOrDefault();

            var choices = new List<Choice<long?>> { new(null, "(project-wide)") };
            choices.AddRange(_hypothesisData.Select(h => new Choice<long?>(h.ResearchHypothesisId, h.Title)));
            Ui.SetChoices(_experimentHypothesis, choices);

            var rows = _experimentData.Select(e => new ExperimentRow(e)).ToList();
            _experiments.ItemsSource = rows;
            experiment = (selectExperiment > 0 ? rows.FirstOrDefault(r => r.Experiment.ResearchExperimentId == selectExperiment) : null) ?? rows.FirstOrDefault();
            Ui.Reveal(_experiments, experiment);
        }
        finally
        {
            _loading = false;
        }

        await SelectHypothesisAsync(_hypotheses.SelectedItem as ResearchHypothesis);
        await BuildOverviewAsync();
        ShowExperiment(experiment?.Experiment);
        _status.Text = $"{_hypothesisData.Count} hypothesis/hypotheses · {_sources.Count} available research source(s) · {_experimentData.Count} experiment(s).";
    }

    private async Task BuildOverviewAsync()
    {
        var assessments = new Dictionary<long, Dictionary<(HypothesisSourceKind, long), ResearchHypothesisAssessment>>();
        foreach (var hypothesis in _hypothesisData)
            assessments[hypothesis.ResearchHypothesisId] = (await _repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId)).ToDictionary(a => (a.SourceKind, a.SourceId));

        var grid = new Grid { Margin = new Thickness(10, 6, 10, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(96, GridUnitType.Pixel));
        grid.ColumnDefinitions.Add(new ColumnDefinition(270, GridUnitType.Pixel));
        foreach (var _ in _hypothesisData) grid.ColumnDefinitions.Add(new ColumnDefinition(170, GridUnitType.Pixel));
        for (var i = 0; i <= _sources.Count; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void Put(Control control, int row, int column)
        {
            control.Margin = new Thickness(0, 4, 10, 4);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        TextBlock HeaderCell(string text) => new() { Text = text, Classes = { "caption" }, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom };

        var headerRule = new Border { Classes = { "rule" }, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(headerRule, grid.ColumnDefinitions.Count);
        grid.Children.Add(headerRule);
        Put(HeaderCell("Kind"), 0, 0);
        Put(HeaderCell("Research source"), 0, 1);
        for (var i = 0; i < _hypothesisData.Count; i++) Put(HeaderCell(_hypothesisData[i].Title), 0, i + 2);

        for (var r = 0; r < _sources.Count; r++)
        {
            var source = _sources[r];
            Put(Cell(Words.Of(source.Kind)), r + 1, 0);
            var title = Cell(source.Title, wrap: true);
            ToolTip.SetTip(title, source.Detail);
            Put(title, r + 1, 1);
            for (var i = 0; i < _hypothesisData.Count; i++)
            {
                if (assessments[_hypothesisData[i].ResearchHypothesisId].TryGetValue((source.Kind, source.Id), out var a))
                    Put(Cell($"{Words.Of(a.Relationship)} · {Words.Of(a.Strength)}", wrap: true), r + 1, i + 2);
            }
        }

        if (_sources.Count == 0)
        {
            var empty = Ui.Muted("No evidence, findings, claims or echo results are saved in this project yet.");
            Grid.SetRow(empty, 1);
            Grid.SetColumnSpan(empty, Math.Max(2, _hypothesisData.Count + 2));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            empty.Margin = new Thickness(0, 8);
            grid.Children.Add(empty);
        }

        _overview.Content = grid;
    }

    private void NewHypothesis()
    {
        _loading = true;
        try
        {
            _hypotheses.SelectedItem = null;
        }
        finally
        {
            _loading = false;
        }

        _editingHypothesis = new ResearchHypothesis { ResearchProjectId = _project.ResearchProjectId, SortOrder = _hypothesisData.Count };
        PopulateHypothesis();
        _tabs.SelectedIndex = 1;
        _title.Focus();
    }

    private async Task SelectHypothesisAsync(ResearchHypothesis? hypothesis)
    {
        _editingHypothesis = hypothesis;
        PopulateHypothesis();
        var links = hypothesis == null ? new List<ResearchHypothesisAssessment>() : await _repo.GetAssessmentsAsync(hypothesis.ResearchHypothesisId);
        var by = links.ToDictionary(a => (a.SourceKind, a.SourceId));
        _matrixRows = _sources.Select(s =>
        {
            by.TryGetValue((s.Kind, s.Id), out var a);
            return new MatrixRow(s, a);
        }).ToList();
        _matrix.ItemsSource = _matrixRows;
    }

    private void PopulateHypothesis()
    {
        var h = _editingHypothesis;
        _title.Text = h?.Title ?? string.Empty;
        _statement.Text = h?.Statement ?? string.Empty;
        Ui.Select(_hypothesisStatus, h?.Status ?? ResearchHypothesisStatus.Active);
        _hypothesisNote.Text = h?.ResearcherNote ?? string.Empty;
        _origin.Text = h == null
            ? "New researcher-authored hypothesis"
            : h.Origin == EvidenceOrigin.AiCandidate
                ? $"Accepted AI proposal · {h.AiModel} · {h.AiGeneratedUtc?.ToLocalTime():g}"
                : "Researcher-authored hypothesis";
    }

    internal async Task SaveHypothesisAsync()
    {
        if (_editingHypothesis == null) return;
        if (string.IsNullOrWhiteSpace(_title.Text) || string.IsNullOrWhiteSpace(_statement.Text))
        {
            await Alert.Info(this, "Save hypothesis", "Enter a title and a testable statement.");
            return;
        }

        _editingHypothesis.Title = _title.Text.Trim();
        _editingHypothesis.Statement = _statement.Text.Trim();
        _editingHypothesis.Status = Ui.Value<ResearchHypothesisStatus>(_hypothesisStatus);
        _editingHypothesis.ResearcherNote = Ui.Clean(_hypothesisNote.Text);
        await _repo.SaveHypothesisAsync(_editingHypothesis);
        await ReloadAsync(_editingHypothesis.ResearchHypothesisId);
    }

    private async Task RemoveHypothesisAsync()
    {
        if (_editingHypothesis?.ResearchHypothesisId is not > 0) return;
        if (!await ConfirmAsync("Remove hypothesis",
                $"Remove “{_editingHypothesis.Title}”? Its assessments will be removed and linked experiments will become project-wide.", "Remove", AlertKind.Warning))
            return;

        await _repo.DeleteHypothesisAsync(_editingHypothesis.ResearchHypothesisId);
        await ReloadAsync();
    }

    internal async Task SaveAssessmentsAsync()
    {
        if (_editingHypothesis?.ResearchHypothesisId is not > 0)
        {
            await Alert.Info(this, "Save assessments", "Save a hypothesis before assessing sources.");
            return;
        }

        var links = _matrixRows.Where(r => r.Linked).Select(r => new ResearchHypothesisAssessment
        {
            ResearchHypothesisId = _editingHypothesis.ResearchHypothesisId,
            SourceKind = r.Source.Kind,
            SourceId = r.Source.Id,
            Relationship = r.Relationship,
            Strength = r.Strength,
            ResearcherNote = Ui.Clean(r.Note)
        }).ToList();
        await _repo.SaveAssessmentsAsync(_editingHypothesis.ResearchHypothesisId, links);
        await BuildOverviewAsync();
        _status.Text = $"Saved {links.Count} source assessment(s) for {_editingHypothesis.Title}.";
    }

    private void ShowExperiment(ResearchExperiment? experiment)
    {
        _editingExperiment = experiment;
        if (experiment == null)
        {
            _experimentTitle.Text = _predicted.Text = _falsification.Text = _experimentNote.Text = string.Empty;
            return;
        }

        _experimentTitle.Text = experiment.Title;
        Ui.Select(_method, experiment.Method);
        Ui.Select(_experimentStatus, experiment.Status);
        _predicted.Text = experiment.PredictedOutcome ?? string.Empty;
        _falsification.Text = experiment.FalsificationCriterion ?? string.Empty;
        _experimentNote.Text = experiment.ResearcherNote ?? string.Empty;
        Ui.Select(_experimentHypothesis, experiment.ResearchHypothesisId);
    }

    internal async Task SaveExperimentAsync()
    {
        if (_editingExperiment == null) return;
        if (string.IsNullOrWhiteSpace(_experimentTitle.Text))
        {
            await Alert.Info(this, "Save experiment", "Give the experiment a title.");
            return;
        }

        _editingExperiment.Title = _experimentTitle.Text.Trim();
        _editingExperiment.Method = Ui.Value<ResearchExperimentMethod>(_method);
        _editingExperiment.Status = Ui.Value<ResearchExperimentStatus>(_experimentStatus);
        _editingExperiment.ResearchHypothesisId = Ui.Value<long?>(_experimentHypothesis);
        _editingExperiment.PredictedOutcome = Ui.Clean(_predicted.Text);
        _editingExperiment.FalsificationCriterion = Ui.Clean(_falsification.Text);
        _editingExperiment.ResearcherNote = Ui.Clean(_experimentNote.Text);
        await _repo.SaveExperimentAsync(_editingExperiment);
        await ReloadAsync(_editingHypothesis?.ResearchHypothesisId ?? 0, _editingExperiment.ResearchExperimentId);
    }

    private async Task RemoveExperimentAsync()
    {
        if (_editingExperiment?.ResearchExperimentId is not > 0) return;
        if (!await ConfirmAsync("Remove experiment", $"Remove experiment “{_editingExperiment.Title}”?", "Remove", AlertKind.Warning)) return;

        await _repo.DeleteExperimentAsync(_editingExperiment.ResearchExperimentId);
        await ReloadAsync(_editingHypothesis?.ResearchHypothesisId ?? 0);
    }

    /// <summary>
    /// Opens the tool an experiment's method names. The windows it opens jump the
    /// reader themselves, so "Open source" in a queue or studio opened from here goes
    /// to the passage - on Windows it once closed them and went nowhere, because this
    /// window had no way to pass the passage up.
    /// </summary>
    private async Task OpenMethodAsync()
    {
        if (_editingExperiment == null) return;

        switch (_editingExperiment.Method)
        {
            case ResearchExperimentMethod.Stylometry when Host != null:
                Stylometry.StylometryFeature.Open(Host);
                break;
            case ResearchExperimentMethod.CorpusInvestigator:
            case ResearchExperimentMethod.ParallelStudio:
                ResearchWindows.Show($"atlas:{_project.ResearchProjectId}", this, () => new IntertextualAtlasWindow(Host, _project));
                break;
            case ResearchExperimentMethod.Bibliography:
                ResearchWindows.Show($"bibliography:{_project.ResearchProjectId}", this, () => new ResearchBibliographyWindow(_project));
                break;
            case ResearchExperimentMethod.ReadingQueue:
                ResearchWindows.Show($"reading:{_project.ResearchProjectId}", this, () => new ReadingQueueWindow(Host, _project, _work));
                break;
            default:
                await Alert.Info(this, "Open method tool", "This experiment is a manual protocol; its prediction and falsification criterion are the working instructions.");
                break;
        }
    }

    private async Task ChallengeAsync()
    {
        if (!await EnsureGeminiKeyAsync()) return;

        var sentSources = _sources.Take(150).Select(s => $"{s.Kind}; review {s.ReviewState}; {s.Title}: {Ui.Clip(s.Detail, 700)}").ToList();
        if (!await ConfirmSendAsync("Send project structure to Gemini?",
                $"This will send the project name/notes, {_hypothesisData.Count} hypotheses, {sentSources.Count} bounded source summaries, and {_experimentData.Count} experiment descriptions to Gemini. Returned proposals remain unsaved until you check and accept them. Continue?"))
            return;

        SetBusy(true);
        _status.Text = "Gemini is looking for rival explanations and discriminating tests…";
        try
        {
            var ai = await GeminiTranslationService.ChallengeResearchHypothesesAsync(
                $"{_author}, {_work.Title}\n{_project.Name}\n{_project.Notes}",
                _hypothesisData.Select(h => $"{h.Title}: {h.Statement} ({h.Status})").ToList(),
                sentSources,
                _experimentData.Select(e => $"{e.Title}; {e.Method}; {e.Status}; prediction {e.PredictedOutcome}; falsified by {e.FalsificationCriterion}").ToList(),
                TranslationSettings.GeminiApiKey!);
            await _research.AddSystemResearchLogEntryAsync(new ResearchLogEntry
            {
                ResearchProjectId = _project.ResearchProjectId,
                Kind = ResearchLogEntryKind.HypothesisChallengeGenerated,
                Summary = "Generated AI hypothesis challenge proposals",
                Details = $"{ai.Model}; {ai.Proposals.Count} proposal(s); {DateTime.UtcNow:O}"
            });

            SetBusy(false);
            var review = new HypothesisChallengeReviewWindow(ai);
            await review.ShowDialog(this);
            if (review.Accepted is { } accepted)
            {
                var nextHypothesisSort = _hypothesisData.Count;
                var nextExperimentSort = _experimentData.Count;
                foreach (var p in accepted)
                {
                    if (p.Kind == "rivalHypothesis")
                    {
                        await _repo.SaveHypothesisAsync(new ResearchHypothesis
                        {
                            ResearchProjectId = _project.ResearchProjectId,
                            Title = p.Title,
                            Statement = p.Statement,
                            ResearcherNote = p.Rationale,
                            Origin = EvidenceOrigin.AiCandidate,
                            AiModel = ai.Model,
                            AiPrompt = ai.PromptProvenance,
                            AiGeneratedUtc = DateTime.UtcNow,
                            SortOrder = nextHypothesisSort++
                        });
                    }
                    else
                    {
                        await _repo.SaveExperimentAsync(new ResearchExperiment
                        {
                            ResearchProjectId = _project.ResearchProjectId,
                            ResearchHypothesisId = _editingHypothesis?.ResearchHypothesisId,
                            Title = p.Title,
                            Method = Enum.TryParse<ResearchExperimentMethod>(p.Method, true, out var parsed) ? parsed : ResearchExperimentMethod.Manual,
                            PredictedOutcome = Ui.Clean(p.PredictedOutcome),
                            FalsificationCriterion = Ui.Clean(p.FalsificationCriterion),
                            ResearcherNote = $"{p.Statement}\r\n\r\nWhy proposed: {p.Rationale}",
                            Origin = EvidenceOrigin.AiCandidate,
                            AiModel = ai.Model,
                            AiPrompt = ai.PromptProvenance,
                            AiGeneratedUtc = DateTime.UtcNow,
                            SortOrder = nextExperimentSort++
                        });
                    }
                }

                await ReloadAsync();
            }

            _status.Text = $"Gemini proposed {ai.Proposals.Count} rival(s)/experiment(s)." +
                           (sentSources.Count < _sources.Count ? $" Scope was bounded to the first {sentSources.Count} of {_sources.Count} sources." : string.Empty);
        }
        catch (Exception ex)
        {
            _status.Text = "AI challenge did not finish: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── What the tests drive and read ─────────────────────────────────────

    internal IReadOnlyList<ResearchHypothesis> Hypotheses => _hypothesisData;
    internal IReadOnlyList<ResearchExperiment> Experiments => _experimentData;
    internal IReadOnlyList<HypothesisSource> Sources => _sources;

    internal void DraftHypothesis(string title, string statement)
    {
        NewHypothesis();
        _title.Text = title;
        _statement.Text = statement;
    }

    internal void Assess(int sourceIndex, HypothesisRelationship bearing, HypothesisStrength strength, string note)
    {
        var row = _matrixRows[sourceIndex];
        row.Linked = true;
        row.Relationship = bearing;
        row.Strength = strength;
        row.Note = note;
        _matrix.ItemsSource = null;
        _matrix.ItemsSource = _matrixRows;
    }

    internal void DraftExperiment(string title, ResearchExperimentMethod method, string prediction, string falsification)
    {
        ShowExperiment(new ResearchExperiment { ResearchProjectId = _project.ResearchProjectId, SortOrder = _experimentData.Count });
        _experimentTitle.Text = title;
        Ui.Select(_method, method);
        _predicted.Text = prediction;
        _falsification.Text = falsification;
    }

    internal void ShowTab(int index) => _tabs.SelectedIndex = index;

    private sealed class ExperimentRow
    {
        public ExperimentRow(ResearchExperiment experiment) => Experiment = experiment;

        public ResearchExperiment Experiment { get; }
        public string Status => Words.Of(Experiment.Status);
        public string Method => Words.Of(Experiment.Method);
        public string Title => Experiment.Title;
    }

    private sealed class MatrixRow
    {
        public MatrixRow(HypothesisSource source, ResearchHypothesisAssessment? assessment)
        {
            Source = source;
            Linked = assessment != null;
            Relationship = assessment?.Relationship ?? HypothesisRelationship.Contextualizes;
            Strength = assessment?.Strength ?? HypothesisStrength.Moderate;
            Note = assessment?.ResearcherNote ?? string.Empty;
        }

        public HypothesisSource Source { get; }
        public bool Linked { get; set; }
        public HypothesisRelationship Relationship { get; set; }
        public HypothesisStrength Strength { get; set; }
        public string Note { get; set; }
    }
}
