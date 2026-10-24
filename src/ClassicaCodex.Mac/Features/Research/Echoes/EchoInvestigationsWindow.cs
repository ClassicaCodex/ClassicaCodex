using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Echoes;

/// <summary>A human-review inbox for saved offline and AI intertextual candidates.</summary>
internal sealed class EchoInvestigationsWindow : ResearchWindow
{
    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly string _authorName;
    private readonly ResearchEchoRepository _echoes = new();
    private readonly ResearchRepository _research = new();
    private readonly ResearchFindingRepository _findings = new();
    private readonly TextNodeRepository _textNodes = new();
    private readonly ListBox _investigations;
    private readonly DataGrid _results = Ui.Table();
    private readonly ComboBox _disposition = Ui.EnumCombo<ResearchEchoDisposition>();
    private readonly TextBox _note = Ui.Area(76, "Why you accepted or rejected this candidate; parallels, differences, chronology, bibliography to check…");
    private readonly TextBlock _details = Ui.Caption(string.Empty);
    private bool _loading;

    public EchoInvestigationsWindow(IReaderHost? host, ResearchProject project, Work work, string authorName) : base(host)
    {
        _project = project;
        _work = work;
        _authorName = authorName;
        Title = $"Echo investigations — {project.Name}";
        Width = 1280;
        Height = 760;
        MinWidth = 1210;
        MinHeight = 600;

        _investigations = Ui.TextList<ResearchEchoInvestigation>(i => i.Title, i => $"{Words.Of(i.Method)} · {i.CreatedUtc.ToLocalTime():d}");
        _investigations.SelectionChanged += (_, _) =>
        {
            if (!_loading) Run(() => LoadResultsAsync());
        };
        var delete = Ui.Button("Remove Investigation", "Minus");
        delete.Click += (_, _) => Run(DeleteAsync);
        delete.Margin = new Thickness(10);
        delete.HorizontalAlignment = HorizontalAlignment.Stretch;
        var left = new DockPanel();
        DockPanel.SetDock(delete, Dock.Bottom);
        left.Children.Add(delete);
        _investigations.Margin = new Thickness(6, 8, 6, 0);
        left.Children.Add(_investigations);

        _results.Columns.Add(Ui.Column("Review", nameof(ResultRow.Disposition), 84));
        _results.Columns.Add(Ui.Column("Relation", nameof(ResultRow.ConnectionType), 96));
        _results.Columns.Add(Ui.Column("Score", nameof(ResultRow.Score), 85));
        _results.Columns.Add(Ui.Column("Author", nameof(ResultRow.Author), 110));
        _results.Columns.Add(Ui.Column("Work", nameof(ResultRow.Work), 130));
        _results.Columns.Add(Ui.Column("Citation", nameof(ResultRow.Citation), 90));
        _results.Columns.Add(Ui.SourceColumn("Passage", nameof(ResultRow.Passage)));
        _results.Columns.Add(Ui.Column("Motifs", nameof(ResultRow.Motifs), 120));
        _results.Columns.Add(Ui.Column("Rationale", nameof(ResultRow.Rationale), 200));
        _results.Margin = new Thickness(12, 0, 12, 10);
        _results.SelectionChanged += (_, _) =>
        {
            if (!_loading) ShowResult();
        };
        Ui.OnRowDoubleTapped(_results, () =>
        {
            OpenStudio();
            return Task.CompletedTask;
        });

        _details.Margin = new Thickness(12, 12, 12, 10);
        _details.MaxLines = 3;
        _details.TextTrimming = TextTrimming.CharacterEllipsis;

        var save = Ui.Button("Save Review", "Check", accent: true);
        var promote = Ui.Button("Promote to Evidence", "Seal");
        var source = Ui.Button("Open Source", "Back");
        var target = Ui.Button("Open Target", "Forward");
        var studio = Ui.Button("Parallel Studio", "CompareTranslations");
        save.Click += (_, _) => Run(SaveReviewAsync);
        promote.Click += (_, _) => Run(PromoteAsync);
        source.Click += (_, _) => Run(OpenSourceAsync);
        target.Click += (_, _) => Run(OpenTargetAsync);
        studio.Click += (_, _) => OpenStudio();

        var label = new TextBlock { Text = "Human review", Classes = { "caption" }, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _disposition.MinWidth = 130;
        var reviewRow = new WrapPanel();
        foreach (var control in new Control[] { label, _disposition, save, promote, source, target, studio })
        {
            control.Margin = new Thickness(0, 0, 8, 6);
            reviewRow.Children.Add(control);
        }

        var review = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        review.Children.Add(reviewRow);
        review.Children.Add(_note);
        var reviewBorder = new Border { Child = review, BorderThickness = new Thickness(0, 1, 0, 0) };
        reviewBorder.Bind(Border.BorderBrushProperty, reviewBorder.GetResourceObservable("Cc.Rule"));
        reviewBorder.Bind(Border.BackgroundProperty, reviewBorder.GetResourceObservable("Cc.PageHeader"));

        var right = new DockPanel();
        DockPanel.SetDock(_details, Dock.Top);
        DockPanel.SetDock(reviewBorder, Dock.Bottom);
        right.Children.Add(_details);
        right.Children.Add(reviewBorder);
        right.Children.Add(_results);

        Content = Ui.Split(Ui.Sidebar(left, double.NaN), right, 320, 240, 870);

        Opened += (_, _) => Run(() => LoadInvestigationsAsync());
    }

    private ResearchEchoInvestigation? Investigation => _investigations.SelectedItem as ResearchEchoInvestigation;

    private ResearchEchoResult? Result => (_results.SelectedItem as ResultRow)?.Result;

    internal IReadOnlyList<ResearchEchoInvestigation> Investigations => _investigations.ItemsSource as IReadOnlyList<ResearchEchoInvestigation> ?? Array.Empty<ResearchEchoInvestigation>();
    internal IReadOnlyList<ResearchEchoResult> Results => (_results.ItemsSource as IEnumerable<ResultRow>)?.Select(r => r.Result).ToList() ?? new List<ResearchEchoResult>();

    internal async Task LoadInvestigationsAsync(long selectId = 0)
    {
        var items = await _echoes.GetInvestigationsAsync(_project.ResearchProjectId);
        _loading = true;
        try
        {
            _investigations.ItemsSource = items;
            _investigations.SelectedItem = (selectId > 0 ? items.FirstOrDefault(i => i.ResearchEchoInvestigationId == selectId) : null) ?? items.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        await LoadResultsAsync();
    }

    internal async Task LoadResultsAsync(long selectId = 0)
    {
        var investigation = Investigation;
        if (investigation == null)
        {
            _results.ItemsSource = null;
            _details.Text = "No saved echo investigations yet.";
            ShowResult();
            return;
        }

        _details.Text = $"{Words.Of(investigation.Method)} • source {_authorName}, {_work.Title} {PassageCitation.Display(investigation.SourceCitationRef, investigation.SourceMilestone)}\n" +
                        $"Scope: {investigation.TargetScope ?? "not recorded"}" +
                        (investigation.AiModel == null ? string.Empty : $" • AI: {investigation.AiModel} (candidate generator, not reviewer)");
        var rows = (await _echoes.GetResultsAsync(investigation.ResearchEchoInvestigationId)).Select(r => new ResultRow(r)).ToList();
        _loading = true;
        try
        {
            _results.ItemsSource = rows;
            Ui.Reveal(_results, (selectId > 0 ? rows.FirstOrDefault(r => r.Result.ResearchEchoResultId == selectId) : null) ?? rows.FirstOrDefault());
        }
        finally
        {
            _loading = false;
        }

        ShowResult();
    }

    private void ShowResult()
    {
        var result = Result;
        if (result == null)
        {
            _note.Text = string.Empty;
            return;
        }

        Ui.Select(_disposition, result.Disposition);
        _note.Text = result.ResearcherNote ?? string.Empty;
    }

    internal async Task SaveReviewAsync()
    {
        if (Result is not { } result) return;
        await _echoes.SaveReviewAsync(result.ResearchEchoResultId, Ui.Value<ResearchEchoDisposition>(_disposition), Ui.Clean(_note.Text));
        await LoadResultsAsync(result.ResearchEchoResultId);
    }

    internal async Task PromoteAsync()
    {
        var investigation = Investigation;
        var result = Result;
        if (investigation == null || result == null) return;

        if (result.EvidenceItemId != null)
        {
            await Alert.Info(this, "Promote to evidence", "This candidate is already linked to evidence.");
            return;
        }

        if (result.Disposition != ResearchEchoDisposition.Accepted)
        {
            await Alert.Info(this, "Promote to evidence", "Accept and save the candidate first. Promotion means ‘worth retaining as paired evidence,’ not proof of borrowing.");
            return;
        }

        var source = PassageCitation.Display(investigation.SourceCitationRef, investigation.SourceMilestone);
        var target = PassageCitation.Display(result.TargetCitationRef, result.TargetMilestone);
        var evidence = new EvidenceItem
        {
            ResearchProjectId = _project.ResearchProjectId,
            ResearchQuestionId = investigation.ResearchQuestionId,
            Title = $"Echo candidate: {_work.Title} {source} ↔ {result.TargetWorkTitle} {target}",
            Type = EvidenceType.PrimaryText,
            SourceType = "ClassicaCodex paired-passage echo investigation",
            StableIdentifier = $"classicacodex:echo-investigation:{investigation.ResearchEchoInvestigationId}:result:{result.ResearchEchoResultId}",
            CanonicalReference = $"{investigation.SourceEditionCtsUrn}:{investigation.SourceCitationRef} ↔ {result.TargetEditionCtsUrn}:{result.TargetCitationRef}",
            Provenance = $"Method: {investigation.Method}. {investigation.Settings}" +
                         $" Human parallel classification: {result.ConnectionType}; directionality: {result.Directionality}; motifs: {result.MotifTags ?? "not classified"}." +
                         (investigation.AiModel == null ? string.Empty : $" Generated by {investigation.AiModel} at {investigation.AiGeneratedUtc:u}. Citations resolved against the local corpus before capture."),
            Excerpt = $"SOURCE — {_authorName}, {_work.Title} [{source}]\r\n{investigation.SourceText}\r\n\r\nTARGET — {result.TargetAuthorName}, {result.TargetWorkTitle} [{target}]\r\n{result.TargetText}",
            Judgment = EvidenceJudgment.Uncertain,
            Relationship = EvidenceRelationship.Contextualizes,
            ResearcherNote = string.Join("\r\n\r\n", new[] { result.ResearcherNote, result.ParallelNote }.Where(n => !string.IsNullOrWhiteSpace(n))),

            // Every AI-assisted method is AI-derived evidence, not the app's own
            // deterministic analysis. Testing one method by name silently exported
            // Corpus Investigator candidates as ClassicaCodexAnalysis; ask whether a
            // model was involved at all instead, so a method added later cannot repeat it.
            Origin = investigation.AiModel != null ||
                     investigation.Method is ResearchEchoMethod.AiCrossLanguage or ResearchEchoMethod.AiCorpusInvestigation
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
                links.Add(new ResearchFindingEvidenceLink
                {
                    ResearchFindingId = findingId,
                    EvidenceItemId = evidenceId,
                    Relationship = EvidenceRelationship.Contextualizes,
                    Note = "Promoted from accepted echo candidate."
                });
                await _findings.SaveLinksAsync(findingId, links);
            }
        }

        await LoadResultsAsync(result.ResearchEchoResultId);
        await Alert.Info(this, "Promote to evidence", "Paired passages were added as uncertain evidence. Review the new evidence record before treating it as support for a claim.");
    }

    private async Task OpenSourceAsync()
    {
        if (Investigation is not { } investigation) return;
        var target = await _textNodes.ResolvePassageNavigationAsync(
            investigation.SourceTextNodeId, investigation.SourceEditionCtsUrn, investigation.SourceCitationRef, investigation.SourceText);
        if (target == null)
        {
            await Alert.Info(this, "Open source", "The source passage could not be resolved in the current corpus.");
            return;
        }

        await NavigateAsync(target.Value.WorkId, target.Value.TextNodeId);
    }

    private async Task OpenTargetAsync()
    {
        if (Result is not { } result) return;
        var target = await _textNodes.ResolvePassageNavigationAsync(
            result.TargetTextNodeId, result.TargetEditionCtsUrn, result.TargetCitationRef, result.TargetText);
        if (target == null)
        {
            await Alert.Info(this, "Open target", "The target passage could not be resolved in the current corpus.");
            return;
        }

        await NavigateAsync(target.Value.WorkId, target.Value.TextNodeId);
    }

    private void OpenStudio()
    {
        if (Investigation is not { } investigation || Result is not { } result) return;

        var (studio, created) = ResearchWindows.Show($"studio:{result.ResearchEchoResultId}", this,
            () => new ParallelStudioWindow(Host, _project, _work, _authorName, investigation, result));
        if (created) studio.Closed += (_, _) => Run(() => IsVisible ? LoadResultsAsync(result.ResearchEchoResultId) : Task.CompletedTask);
    }

    private async Task DeleteAsync()
    {
        if (Investigation is not { } investigation) return;
        if (!await ConfirmAsync("Remove investigation",
                $"Remove “{investigation.Title}” and its candidate reviews? Promoted evidence will be retained.", "Remove"))
            return;

        await _echoes.DeleteInvestigationAsync(investigation.ResearchEchoInvestigationId);
        await LoadInvestigationsAsync();
    }

    internal void Review(ResearchEchoDisposition disposition, string? note)
    {
        Ui.Select(_disposition, disposition);
        _note.Text = note ?? string.Empty;
    }

    private sealed class ResultRow
    {
        public ResultRow(ResearchEchoResult result) => Result = result;

        public ResearchEchoResult Result { get; }
        public string Disposition => Words.Of(Result.Disposition);
        public string ConnectionType => Words.Of(Result.ConnectionType);
        public string Score => Result.ScoreLabel ?? Result.Score?.ToString("0.##") ?? string.Empty;
        public string Author => Result.TargetAuthorName;
        public string Work => Result.TargetWorkTitle;
        public string Citation => PassageCitation.Display(Result.TargetCitationRef, Result.TargetMilestone);
        public string Passage => Result.TargetText;
        public string Motifs => Result.MotifTags ?? string.Empty;
        public string Rationale => Result.Rationale ?? string.Empty;
    }
}
