using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>Close reading and classification of one saved passage-to-passage edge.</summary>
public sealed class ParallelPassageStudioForm : Form
{
    private readonly ResearchProject _project;
    private readonly Work _sourceWork;
    private readonly string _sourceAuthor;
    private readonly ResearchEchoInvestigation _investigation;
    private readonly ResearchEchoResult _result;
    private readonly ResearchEchoRepository _echoes = new();
    private readonly TextNodeRepository _textNodes = new();
    private readonly ComboBox _connection = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _direction = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _motifs = new();
    private readonly TextBox _parallelNote = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly ComboBox _history = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _analysis = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _aiStatus = new();

    public (int WorkId, long TextNodeId)? NavigationTarget { get; private set; }
    private ResearchEchoParallelAnalysis? CurrentAnalysis => _history.SelectedItem as ResearchEchoParallelAnalysis;

    public ParallelPassageStudioForm(ResearchProject project, Work sourceWork, string sourceAuthor,
        ResearchEchoInvestigation investigation, ResearchEchoResult result)
    {
        _project = project; _sourceWork = sourceWork; _sourceAuthor = sourceAuthor;
        _investigation = investigation; _result = result;
        Text = $"Parallel Passage Studio — {sourceWork.Title} {investigation.SourceCitationRef} ↔ {result.TargetWorkTitle} {result.TargetCitationRef}";
        Width = 1280; Height = 820; MinimumSize = new Size(940, 640); StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "SimilarWorks");

        var header = new Label
        {
            Dock = DockStyle.Top, Height = 50, Padding = new Padding(10, 9, 8, 0),
            Text = "A parallel is a research object, not proof of influence. Compare the wording and differences, classify the relationship, then decide what it can support."
        };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildComparisonTab());
        tabs.TabPages.Add(BuildHumanTab());
        tabs.TabPages.Add(BuildAiTab());
        Controls.Add(tabs); Controls.Add(header);
        ReadingTheme.AttachTo(this, () => { header.ForeColor = ReadingTheme.MutedText; _aiStatus.ForeColor = ReadingTheme.MutedText; });
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) => await LoadAnalysesAsync();
    }

    private TabPage BuildComparisonTab()
    {
        var page = new TabPage("Passages");
        var passages = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 3 };
        passages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); passages.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        passages.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); passages.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); passages.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        var sourceLabel = new Label { Dock = DockStyle.Fill, Text = $"SOURCE — {_sourceAuthor}, {_sourceWork.Title} [{_investigation.SourceCitationRef}] ({Language(_investigation.SourceLanguage)})", Font = new Font(Font, FontStyle.Bold) };
        var targetLabel = new Label { Dock = DockStyle.Fill, Text = $"TARGET — {_result.TargetAuthorName}, {_result.TargetWorkTitle} [{_result.TargetCitationRef}] ({Language(_result.TargetLanguage)})", Font = new Font(Font, FontStyle.Bold) };
        var source = PassageBox(_investigation.SourceText); var target = PassageBox(_result.TargetText);
        var rationale = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = "Why this candidate was proposed:\r\n" + (_result.Rationale ?? "No rationale was recorded.") };
        passages.Controls.Add(sourceLabel, 0, 0); passages.Controls.Add(targetLabel, 1, 0);
        passages.Controls.Add(source, 0, 1); passages.Controls.Add(target, 1, 1);
        passages.Controls.Add(rationale, 0, 2); passages.SetColumnSpan(rationale, 2);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6) };
        var openSource = Button("Open source", 105); openSource.Click += async (_, _) => await OpenSourceAsync();
        var openTarget = Button("Open target", 105); openTarget.Click += async (_, _) => await OpenTargetAsync();
        nav.Controls.AddRange([openSource, openTarget]); page.Controls.Add(passages); page.Controls.Add(nav); return page;
    }

    private TabPage BuildHumanTab()
    {
        var page = new TabPage("Human classification");
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        var typeLabel = LabelAt("Relationship type", 12, 14, 180); _connection.SetBounds(12, 38, 280, 28);
        _connection.DataSource = Enum.GetValues<ResearchEchoConnectionType>(); _connection.SelectedItem = _result.ConnectionType;
        var directionLabel = LabelAt("Directionality / historical relation", 320, 14, 250); _direction.SetBounds(320, 38, 300, 28);
        _direction.DataSource = Enum.GetValues<ResearchEchoDirectionality>(); _direction.SelectedItem = _result.Directionality;
        var warning = LabelAt("Directionality is a reviewed claim. ‘Unknown’ is preferable until chronology and transmission are established.", 12, 76, 900);
        var motifsLabel = LabelAt("Motif labels (comma-separated; e.g. night watch, deceptive dream, recognition)", 12, 112, 850);
        _motifs.SetBounds(12, 138, 900, 28); _motifs.Text = _result.MotifTags ?? "";
        var noteLabel = LabelAt("Close-reading note: correspondences, differences, genre controls, chronology, and sources still to check", 12, 180, 900);
        _parallelNote.SetBounds(12, 206, 1160, 380); _parallelNote.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _parallelNote.Text = _result.ParallelNote ?? "";
        var save = Button("Save human classification", 180); save.SetBounds(12, 602, 180, 30); save.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        save.Click += async (_, _) => await SaveClassificationAsync();
        panel.Controls.AddRange([typeLabel, _connection, directionLabel, _direction, warning, motifsLabel, _motifs, noteLabel, _parallelNote, save]);
        page.Controls.Add(panel); return page;
    }

    private TabPage BuildAiTab()
    {
        var page = new TabPage("AI close-reading candidates");
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6) };
        var analyze = Button("Analyze this pair with Gemini", 200); analyze.Click += async (_, _) => await AnalyzeAsync(analyze);
        var apply = Button("Copy suggestions into human fields", 215); apply.Click += (_, _) => ApplySuggestions();
        _history.Width = 230; _history.SelectedIndexChanged += (_, _) => ShowAnalysis();
        strip.Controls.AddRange([analyze, apply, new Label { Text = "Saved readings:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) }, _history]);
        _aiStatus.Dock = DockStyle.Top; _aiStatus.Height = 34; _aiStatus.Padding = new Padding(9, 5, 0, 0);
        _analysis.Dock = DockStyle.Fill; _analysis.Font = new Font(Font.FontFamily, Math.Max(Font.Size, 10));
        page.Controls.Add(_analysis); page.Controls.Add(_aiStatus); page.Controls.Add(strip); return page;
    }

    private async Task LoadAnalysesAsync(long selectId = 0)
    {
        var analyses = await _echoes.GetParallelAnalysesAsync(_result.ResearchEchoResultId);
        _history.DataSource = null; _history.DataSource = analyses;
        if (selectId > 0) _history.SelectedItem = analyses.FirstOrDefault(a => a.ResearchEchoParallelAnalysisId == selectId);
        _aiStatus.Text = analyses.Count == 0
            ? "No AI close reading saved. Gemini will receive only these two passages and the project question."
            : $"{analyses.Count} immutable AI candidate reading(s) saved. Human classification is separate.";
        ShowAnalysis();
    }

    private async Task AnalyzeAsync(Button button)
    {
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settings = new TranslateApiSettingsForm(); settings.ShowDialog(this); return;
        }
        if (TranslationSettings.AlwaysConfirmBeforeSending && MessageBox.Show(this,
                "This sends the two displayed passages, their citations, the saved candidate rationale, and the project question to Gemini. It does not send the rest of the corpus. Continue?",
                "Send paired passages to Gemini?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        button.Enabled = false; _aiStatus.Text = "Gemini is close-reading the pair…";
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
        catch (Exception ex) { _aiStatus.Text = $"Could not analyze the pair: {ex.Message}"; }
        finally { button.Enabled = true; }
    }

    private void ShowAnalysis()
    {
        var a = CurrentAnalysis;
        _analysis.Text = a == null ? "" :
            $"SUMMARY\r\n{a.Summary}\r\n\r\nSHARED FEATURES\r\n{a.SharedFeatures}\r\n\r\nIMPORTANT DIFFERENCES\r\n{a.ImportantDifferences}" +
            $"\r\n\r\nLEXICAL OBSERVATIONS\r\n{a.LexicalObservations}\r\n\r\nALTERNATIVE EXPLANATIONS\r\n{a.AlternativeExplanations}" +
            $"\r\n\r\nVERIFICATION TASKS\r\n{a.VerificationTasks}\r\n\r\nSUGGESTED MOTIFS\r\n{a.SuggestedMotifs}" +
            $"\r\n\r\nSUGGESTED CLASSIFICATION\r\n{a.SuggestedConnectionType}; {a.SuggestedDirectionality}\r\n\r\nPROVENANCE\r\n{a.Model}, {a.CreatedUtc.ToLocalTime():g}";
    }

    private void ApplySuggestions()
    {
        var a = CurrentAnalysis; if (a == null) return;
        _connection.SelectedItem = a.SuggestedConnectionType; _direction.SelectedItem = a.SuggestedDirectionality;
        if (!string.IsNullOrWhiteSpace(a.SuggestedMotifs)) _motifs.Text = MergeMotifs(_motifs.Text, a.SuggestedMotifs);
        MessageBox.Show(this, "Suggestions were copied into the human fields but have not been saved. Review and edit them on the Human classification tab.");
    }

    private async Task SaveClassificationAsync()
    {
        await _echoes.SaveParallelClassificationAsync(_result.ResearchEchoResultId,
            (ResearchEchoConnectionType)_connection.SelectedItem!, (ResearchEchoDirectionality)_direction.SelectedItem!,
            Empty(_motifs.Text), Empty(_parallelNote.Text));
        MessageBox.Show(this, "Human classification saved.");
    }

    private async Task OpenSourceAsync() => await NavigateAsync(_investigation.SourceTextNodeId, _investigation.SourceEditionCtsUrn, _investigation.SourceCitationRef, _investigation.SourceText);
    private async Task OpenTargetAsync() => await NavigateAsync(_result.TargetTextNodeId, _result.TargetEditionCtsUrn, _result.TargetCitationRef, _result.TargetText);
    private async Task NavigateAsync(long hint, string editionUrn, string citation, string text)
    {
        var target = await _textNodes.ResolvePassageNavigationAsync(hint, editionUrn, citation, text);
        if (target == null) { MessageBox.Show(this, "This passage could not be resolved in the current corpus."); return; }
        NavigationTarget = target; DialogResult = DialogResult.OK; Close();
    }

    private static TextBox PassageBox(string text) => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Text = text, Font = new Font("Segoe UI", 12) };
    private static Button Button(string text, int width) => new() { Text = text, Width = width, Height = 28 };
    private static Label LabelAt(string text, int x, int y, int width) => new() { Text = text, Left = x, Top = y, Width = width, AutoSize = false, Height = 22 };
    private static string Language(string? code) => string.IsNullOrWhiteSpace(code) ? "language not recorded" : TranslationLanguageNames.DisplayName(code);
    private static string? Empty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string MergeMotifs(string existing, string suggested) => string.Join(", ", existing.Split(',').Concat(suggested.Split(','))
        .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}
