using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Echoes;

/// <summary>Close reading and classification of one saved passage-to-passage edge.</summary>
internal sealed class ParallelStudioWindow : ResearchWindow
{
    private readonly ResearchProject _project;
    private readonly Work _sourceWork;
    private readonly string _sourceAuthor;
    private readonly ResearchEchoInvestigation _investigation;
    private readonly ResearchEchoResult _result;
    private readonly ResearchEchoRepository _echoes = new();
    private readonly TextNodeRepository _textNodes = new();
    private readonly ComboBox _connection = Ui.EnumCombo<ResearchEchoConnectionType>();
    private readonly ComboBox _direction = Ui.EnumCombo<ResearchEchoDirectionality>();
    private readonly TextBox _motifs = Ui.Line();
    private readonly TextBox _parallelNote = Ui.Area(double.NaN);
    private readonly ComboBox _history = new() { MinWidth = 230 };
    private readonly TextBox _analysis = Ui.Area(double.NaN, readOnly: true);
    private readonly TextBlock _aiStatus = Ui.Muted(string.Empty);
    private readonly Button _analyze = Ui.Button("Analyze This Pair with Gemini", "Sparkle");

    public ParallelStudioWindow(IReaderHost? host, ResearchProject project, Work sourceWork, string sourceAuthor,
        ResearchEchoInvestigation investigation, ResearchEchoResult result) : base(host)
    {
        _project = project;
        _sourceWork = sourceWork;
        _sourceAuthor = sourceAuthor;
        _investigation = investigation;
        _result = result;
        Title = $"Parallel Passage Studio — {sourceWork.Title} {PassageCitation.Display(investigation.SourceCitationRef, investigation.SourceMilestone)} ↔ {result.TargetWorkTitle} {PassageCitation.Display(result.TargetCitationRef, result.TargetMilestone)}";
        Width = 1280;
        Height = 820;
        MinWidth = 940;
        MinHeight = 640;

        var header = Ui.Notice("A parallel is a research object, not proof of influence. Compare the wording and differences, classify the relationship, then decide what it can support.");
        header.Margin = new Thickness(16, 12, 16, 10);

        var tabs = new TabControl { Margin = new Thickness(14, 0, 14, 10) };
        tabs.Items.Add(new TabItem { Header = "Passages", Content = BuildComparisonTab() });
        tabs.Items.Add(new TabItem { Header = "Human Classification", Content = BuildHumanTab() });
        tabs.Items.Add(new TabItem { Header = "AI Close-Reading Candidates", Content = BuildAiTab() });

        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(tabs);
        Content = dock;

        Opened += (_, _) => Run(() => LoadAnalysesAsync());
    }

    private Control BuildComparisonTab()
    {
        var sourceLabel = new TextBlock
        {
            Text = $"SOURCE — {_sourceAuthor}, {_sourceWork.Title} [{PassageCitation.Display(_investigation.SourceCitationRef, _investigation.SourceMilestone)}] ({Language(_investigation.SourceLanguage)})",
            Classes = { "h3" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var targetLabel = new TextBlock
        {
            Text = $"TARGET — {_result.TargetAuthorName}, {_result.TargetWorkTitle} [{PassageCitation.Display(_result.TargetCitationRef, _result.TargetMilestone)}] ({Language(_result.TargetLanguage)})",
            Classes = { "h3" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var source = Ui.Passage(_investigation.SourceText, 16);
        var target = Ui.Passage(_result.TargetText, 16);
        var rationale = Ui.Area(130, readOnly: true);
        rationale.Text = "Why this candidate was proposed:\n" + (_result.Rationale ?? "No rationale was recorded.");

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,16,*"),
            RowDefinitions = new RowDefinitions("Auto,*,12,Auto"),
            Margin = new Thickness(0, 12, 0, 8)
        };
        void Put(Control c, int row, int column, int span = 1)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            Grid.SetColumnSpan(c, span);
            grid.Children.Add(c);
        }

        Put(sourceLabel, 0, 0);
        Put(targetLabel, 0, 2);
        Put(source, 1, 0);
        Put(target, 1, 2);
        Put(rationale, 3, 0, 3);

        var openSource = Ui.Button("Open Source", "Back");
        var openTarget = Ui.Button("Open Target", "Forward");
        openSource.Click += (_, _) => Run(() => NavigateToAsync(_investigation.SourceTextNodeId, _investigation.SourceEditionCtsUrn, _investigation.SourceCitationRef, _investigation.SourceText));
        openTarget.Click += (_, _) => Run(() => NavigateToAsync(_result.TargetTextNodeId, _result.TargetEditionCtsUrn, _result.TargetCitationRef, _result.TargetText));
        var nav = Ui.Row(openSource, openTarget);
        nav.Margin = new Thickness(0, 4, 0, 4);

        var dock = new DockPanel();
        DockPanel.SetDock(nav, Dock.Bottom);
        dock.Children.Add(nav);
        dock.Children.Add(grid);
        return dock;
    }

    private Control BuildHumanTab()
    {
        Ui.Select(_connection, _result.ConnectionType);
        Ui.Select(_direction, _result.Directionality);
        _motifs.Text = _result.MotifTags ?? string.Empty;
        _parallelNote.Text = _result.ParallelNote ?? string.Empty;
        _parallelNote.MinHeight = 240;

        var save = Ui.Button("Save Human Classification", "Check", accent: true);
        save.Click += (_, _) => Run(SaveClassificationAsync);

        var form = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        form.Children.Add(Ui.Pair(Ui.Field("Relationship type", _connection), Ui.Field("Directionality / historical relation", _direction)));
        var warning = Ui.Muted("Directionality is a reviewed claim. ‘Unknown’ is preferable until chronology and transmission are established.");
        warning.Margin = new Thickness(0, 0, 0, 12);
        form.Children.Add(warning);
        form.Children.Add(Ui.Field("Motif labels (comma-separated; e.g. night watch, deceptive dream, recognition)", _motifs));
        form.Children.Add(Ui.FieldLabel("Close-reading note: correspondences, differences, genre controls, chronology, and sources still to check"));

        var dock = new DockPanel();
        DockPanel.SetDock(form, Dock.Top);
        DockPanel.SetDock(save, Dock.Bottom);
        save.Margin = new Thickness(0, 10, 0, 4);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        dock.Children.Add(form);
        dock.Children.Add(save);
        dock.Children.Add(_parallelNote);
        return dock;
    }

    private Control BuildAiTab()
    {
        var apply = Ui.Button("Copy Suggestions into Human Fields", "Copy");
        _analyze.Click += (_, _) => Run(AnalyzeAsync);
        apply.Click += (_, _) => Run(ApplySuggestionsAsync);
        _history.SelectionChanged += (_, _) => ShowAnalysis();
        var saved = new TextBlock { Text = "Saved readings:", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 10) };
        top.Children.Add(Ui.Row(_analyze, apply, saved, _history));
        top.Children.Add(_aiStatus);

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        _analysis.FontSize = 13.5;
        dock.Children.Add(_analysis);
        return dock;
    }

    private ResearchEchoParallelAnalysis? CurrentAnalysis => _history.SelectedItem as ResearchEchoParallelAnalysis;

    internal async Task LoadAnalysesAsync(long selectId = 0)
    {
        var analyses = await _echoes.GetParallelAnalysesAsync(_result.ResearchEchoResultId);
        _history.ItemsSource = analyses;
        _history.SelectedItem = (selectId > 0 ? analyses.FirstOrDefault(a => a.ResearchEchoParallelAnalysisId == selectId) : null) ?? analyses.FirstOrDefault();
        _aiStatus.Text = analyses.Count == 0
            ? "No AI close reading saved. Gemini will receive only these two passages and the project question."
            : $"{analyses.Count} immutable AI candidate reading(s) saved. Human classification is separate.";
        ShowAnalysis();
    }

    private async Task AnalyzeAsync()
    {
        if (!await EnsureGeminiKeyAsync()) return;
        if (!await ConfirmSendAsync("Send paired passages to Gemini?",
                "This sends the two displayed passages, their citations, the saved candidate rationale, and the project question to Gemini. It does not send the rest of the corpus. Continue?"))
            return;

        _analyze.IsEnabled = false;
        _aiStatus.Text = "Gemini is close-reading the pair…";
        try
        {
            var analysis = await GeminiTranslationService.AnalyzeParallelPassagesAsync(
                _project.Name, _sourceAuthor, _sourceWork.Title, _investigation.SourceCitationRef,
                _investigation.SourceText, _investigation.SourceLanguage, _result.TargetAuthorName,
                _result.TargetWorkTitle, _result.TargetCitationRef, _result.TargetText, _result.TargetLanguage,
                _result.Rationale, TranslationSettings.GeminiApiKey!);
            var id = await _echoes.SaveParallelAnalysisAsync(_result.ResearchEchoResultId, analysis);
            await LoadAnalysesAsync(id);
        }
        catch (Exception ex)
        {
            _aiStatus.Text = $"Could not analyze the pair: {ex.Message}";
        }
        finally
        {
            _analyze.IsEnabled = true;
        }
    }

    private void ShowAnalysis()
    {
        var a = CurrentAnalysis;
        var nl = Environment.NewLine;
        _analysis.Text = a == null
            ? string.Empty
            : $"SUMMARY{nl}{a.Summary}{nl}{nl}SHARED FEATURES{nl}{a.SharedFeatures}{nl}{nl}IMPORTANT DIFFERENCES{nl}{a.ImportantDifferences}" +
              $"{nl}{nl}LEXICAL OBSERVATIONS{nl}{a.LexicalObservations}{nl}{nl}ALTERNATIVE EXPLANATIONS{nl}{a.AlternativeExplanations}" +
              $"{nl}{nl}VERIFICATION TASKS{nl}{a.VerificationTasks}{nl}{nl}SUGGESTED MOTIFS{nl}{a.SuggestedMotifs}" +
              $"{nl}{nl}SUGGESTED CLASSIFICATION{nl}{a.SuggestedConnectionType}; {a.SuggestedDirectionality}{nl}{nl}PROVENANCE{nl}{a.Model}, {a.CreatedUtc.ToLocalTime():g}";
    }

    private async Task ApplySuggestionsAsync()
    {
        var a = CurrentAnalysis;
        if (a == null) return;

        Ui.Select(_connection, a.SuggestedConnectionType);
        Ui.Select(_direction, a.SuggestedDirectionality);
        if (!string.IsNullOrWhiteSpace(a.SuggestedMotifs)) _motifs.Text = MergeMotifs(_motifs.Text ?? string.Empty, a.SuggestedMotifs);
        await Alert.Info(this, "AI suggestions", "Suggestions were copied into the human fields but have not been saved. Review and edit them on the Human Classification tab.");
    }

    internal async Task SaveClassificationAsync()
    {
        await _echoes.SaveParallelClassificationAsync(_result.ResearchEchoResultId,
            Ui.Value<ResearchEchoConnectionType>(_connection), Ui.Value<ResearchEchoDirectionality>(_direction),
            Ui.Clean(_motifs.Text), Ui.Clean(_parallelNote.Text));
        await Alert.Info(this, "Parallel Passage Studio", "Human classification saved.");
    }

    private async Task NavigateToAsync(long hint, string editionUrn, string citation, string text)
    {
        var target = await _textNodes.ResolvePassageNavigationAsync(hint, editionUrn, citation, text);
        if (target == null)
        {
            await Alert.Info(this, "Open passage", "This passage could not be resolved in the current corpus.");
            return;
        }

        await NavigateAsync(target.Value.WorkId, target.Value.TextNodeId);
    }

    internal void Classify(ResearchEchoConnectionType type, ResearchEchoDirectionality direction, string motifs, string note)
    {
        Ui.Select(_connection, type);
        Ui.Select(_direction, direction);
        _motifs.Text = motifs;
        _parallelNote.Text = note;
    }

    private static string Language(string? code) => string.IsNullOrWhiteSpace(code) ? "language not recorded" : TranslationLanguageNames.DisplayName(code);

    private static string MergeMotifs(string existing, string suggested) => string.Join(", ", existing.Split(',').Concat(suggested.Split(','))
        .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}
