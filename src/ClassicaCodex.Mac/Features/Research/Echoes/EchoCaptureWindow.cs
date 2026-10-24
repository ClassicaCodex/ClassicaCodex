using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Echoes;

/// <summary>
/// Lets a researcher choose which already-computed echo candidates become a saved
/// investigation - the "Save echo investigation" sheet that Find Echoes,
/// Cross-Language Echo and the Corpus Investigator all open.
/// </summary>
internal sealed class EchoCaptureWindow : ResearchWindow
{
    private readonly EchoCaptureRequest _capture;
    private readonly long? _defaultProjectId;
    private readonly ResearchRepository _research = new();
    private readonly ResearchEchoRepository _echoes = new();
    private readonly ResearchFindingRepository _findings = new();
    private readonly ComboBox _project = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    private readonly ComboBox _question = Ui.Combo(new[] { new Choice<long?>(null, "(none)") });
    private readonly ComboBox _finding = Ui.Combo(new[] { new Choice<long?>(null, "(none)") });
    private readonly TextBox _title = Ui.Line();
    private readonly CheckTable<EchoCaptureCandidate> _rows;
    private readonly Button _save = Ui.Button("Save Investigation", "Check", accent: true);

    public EchoCaptureWindow(EchoCaptureRequest capture, long? defaultProjectId = null) : base(null)
    {
        _capture = capture;
        _defaultProjectId = defaultProjectId;
        Title = "Save echo investigation";
        Width = 1050;
        Height = 650;
        MinWidth = 760;
        MinHeight = 480;

        _title.Text = capture.Title;
        _project.SelectionChanged += (_, _) => Run(LoadScopesAsync);

        var form = new StackPanel { Margin = new Thickness(16, 14, 16, 10) };
        form.Children.Add(Ui.Pair(Ui.Field("Project", _project), Ui.Field("Question", _question)));
        form.Children.Add(Ui.Pair(Ui.Field("Finding", _finding), new Border()));
        form.Children.Add(Ui.Field("Title", _title, 0));

        _rows = new CheckTable<EchoCaptureCandidate>("Save",
            new CheckColumn<EchoCaptureCandidate>("Score", new GridLength(85), c => c.ScoreLabel ?? c.Score?.ToString("0.##") ?? "—"),
            new CheckColumn<EchoCaptureCandidate>("Target", new GridLength(210), c => $"{c.AuthorName}, {c.WorkTitle}", Wrap: true),
            new CheckColumn<EchoCaptureCandidate>("Citation", new GridLength(90), c => PassageCitation.Display(c.CitationRef)),
            new CheckColumn<EchoCaptureCandidate>("Passage / rationale", new GridLength(1, GridUnitType.Star),
                c => c.Rationale is null ? c.Text : $"{c.Text} — {c.Rationale}", Wrap: true, Source: true));
        _rows.SetItems(capture.Candidates, initiallyChecked: _ => true);
        _rows.Margin = new Thickness(16, 0, 16, 12);

        var cancel = Ui.Button("Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();
        _save.IsDefault = true;
        _save.Click += (_, _) => Run(SaveAsync);

        var bar = Ui.ButtonBar(null, cancel, _save);
        var dock = new DockPanel();
        DockPanel.SetDock(form, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(form);
        dock.Children.Add(bar);
        dock.Children.Add(_rows);
        Content = dock;

        Opened += (_, _) => Run(LoadProjectsAsync);
    }

    /// <summary>The investigation saved, if the reader saved one.</summary>
    public long? SavedInvestigationId { get; private set; }

    internal CheckTable<EchoCaptureCandidate> Candidates => _rows;
    internal IReadOnlyList<ResearchProject> Projects => _project.ItemsSource as IReadOnlyList<ResearchProject> ?? Array.Empty<ResearchProject>();
    internal ResearchProject? SelectedProject => _project.SelectedItem as ResearchProject;

    protected override bool OnCommandKey(Key key, bool shift)
    {
        if (key != Key.S) return false;
        Run(SaveAsync);
        return true;
    }

    internal async Task LoadProjectsAsync()
    {
        // The project this search was started from is offered even if it is archived:
        // the Atlas can investigate archived projects, and leaving it out made the form
        // preselect some other project without saying so, or refuse to save at all.
        var projects = (await _research.GetProjectsForWorkAsync(
                _capture.Source.WorkId, includeArchived: true, workCtsUrn: _capture.Source.WorkCtsUrn))
            .Where(p => p.Status != ResearchProjectStatus.Archived || p.ResearchProjectId == _defaultProjectId)
            .ToList();
        _project.ItemsSource = projects;
        var preferred = _defaultProjectId is { } id ? projects.FirstOrDefault(p => p.ResearchProjectId == id) : null;
        _project.SelectedItem = preferred ?? projects.FirstOrDefault();

        _save.IsEnabled = projects.Count > 0;
        if (projects.Count == 0)
            await Alert.Info(this, "No research project", "Create a Research Bench project for this source work first, then save the search.");
    }

    private async Task LoadScopesAsync()
    {
        if (_project.SelectedItem is not ResearchProject project) return;

        var questions = await _research.GetQuestionsAsync(project.ResearchProjectId);
        var findings = await _findings.GetAsync(project.ResearchProjectId);
        var questionChoices = new List<Choice<long?>> { new(null, "(none)") };
        questionChoices.AddRange(questions.Select(q => new Choice<long?>(q.ResearchQuestionId, q.Text)));
        var findingChoices = new List<Choice<long?>> { new(null, "(none)") };
        findingChoices.AddRange(findings.Select(f => new Choice<long?>(f.ResearchFindingId, f.Title)));
        Ui.SetChoices(_question, questionChoices);
        Ui.SetChoices(_finding, findingChoices);
    }

    internal async Task SaveAsync()
    {
        if (_project.SelectedItem is not ResearchProject project) return;
        if (string.IsNullOrWhiteSpace(_title.Text))
        {
            await Alert.Info(this, "Save echo investigation", "Give the investigation a title.");
            return;
        }

        var candidates = _rows.Checked.ToList();
        if (candidates.Count == 0)
        {
            await Alert.Info(this, "Save echo investigation", "Select at least one candidate to save.");
            return;
        }

        var request = _capture with { Title = _title.Text.Trim(), Candidates = candidates };
        var saved = await _echoes.SaveCaptureAsync(project.ResearchProjectId, Ui.Value<long?>(_question), Ui.Value<long?>(_finding), request);
        SavedInvestigationId = saved.ResearchEchoInvestigationId;
        Close();
    }
}
