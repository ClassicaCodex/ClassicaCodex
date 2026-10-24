using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Research.Synthesis;

/// <summary>Turns reviewed evidence into explicit, auditable researcher-owned findings.</summary>
internal sealed class SynthesisWindow : ResearchWindow
{
    private static readonly FilePickerFileType Markdown = new("Markdown") { Patterns = new[] { "*.md" } };
    private const string LinkColumns = "56,140,96,*,200";

    private readonly ResearchProject _project;
    private readonly Work _work;
    private readonly string _authorName;
    private readonly ResearchRepository _research = new();
    private readonly ResearchFindingRepository _findingsRepo = new();
    private readonly ListBox _findings;
    private readonly TextBox _title = Ui.Line();
    private readonly TextBox _statement = Ui.Area(100);
    private readonly ComboBox _status = Ui.EnumCombo<ResearchFindingStatus>();
    private readonly ComboBox _question = Ui.Combo(Ui.QuestionChoices("General project finding", Array.Empty<ResearchQuestion>()));
    private readonly TextBox _conclusion = Ui.Area(250);
    private readonly TextBox _aiCandidate = Ui.Area(double.NaN, readOnly: true);
    private readonly TextBlock _aiProvenance = Ui.Muted(string.Empty);
    private readonly ListBox _evidence;
    private readonly TextBlock _statusLine = Ui.Status();
    private readonly TabControl _tabs = new() { Margin = new Thickness(14, 0, 14, 0) };
    private ResearchFinding? _editing;
    private List<ResearchQuestion> _questions = new();
    private List<EvidenceItem> _allEvidence = new();
    private List<ScholarlyClaim> _claims = new();
    private List<ResearchFinding> _findingItems = new();
    private List<EvidenceLinkRow> _linkRows = new();
    private bool _loading;

    public SynthesisWindow(ResearchProject project, Work work, string authorName) : base(null)
    {
        _project = project;
        _work = work;
        _authorName = authorName;
        Title = $"Synthesis & Findings — {project.Name}";
        Width = 1400;
        Height = 860;
        MinWidth = 1100;
        MinHeight = 680;

        var header = Ui.Notice("Findings are researcher-owned propositions. AI synthesis remains a labelled candidate until you adopt or rewrite it.");
        header.Margin = new Thickness(16, 12, 16, 10);

        _findings = Ui.TextList<ResearchFinding>(f => f.Title, f => Words.Of(f.Status));
        _findings.SelectionChanged += (_, _) =>
        {
            if (!_loading) Run(() => ShowFindingAsync(_findings.SelectedItem as ResearchFinding));
        };
        _evidence = Ui.Compact(new ListBox { ItemTemplate = new FuncDataTemplate<EvidenceLinkRow>((row, _) => row == null ? new TextBlock() : BuildLinkRow(row), supportsRecycling: false) }, 4);

        var status = Ui.StatusBar(_statusLine);
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(header);
        dock.Children.Add(status);
        dock.Children.Add(Ui.Split(BuildFindingList(), BuildEditor(), 330, 280, 760));
        Content = dock;

        Opened += (_, _) => Run(() => LoadWorkspaceAsync());
    }

    private Control BuildFindingList()
    {
        var add = Ui.Button("New Finding", "Plus", tip: "Start a new finding (" + Ui.Chord("N") + ")");
        var remove = Ui.Button("Remove", "Minus");
        var export = Ui.Button("Export Dossier…", "Export");
        add.Click += (_, _) => NewFinding();
        remove.Click += (_, _) => Run(RemoveAsync);
        export.Click += (_, _) => Run(ExportDossierAsync);

        var strip = new WrapPanel { Margin = new Thickness(10, 12, 10, 8) };
        foreach (var button in new[] { add, remove, export })
        {
            button.Margin = new Thickness(0, 0, 6, 6);
            strip.Children.Add(button);
        }

        var dock = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        dock.Children.Add(strip);
        _findings.Margin = new Thickness(6, 0, 6, 8);
        dock.Children.Add(_findings);
        return Ui.Sidebar(dock, double.NaN);
    }

    private Control BuildEditor()
    {
        var tabs = _tabs;
        tabs.Items.Add(new TabItem { Header = "Finding & Conclusion", Content = BuildFindingTab() });
        tabs.Items.Add(new TabItem { Header = "Linked Evidence", Content = BuildEvidenceTab() });
        tabs.Items.Add(new TabItem { Header = "AI Candidate Synthesis", Content = BuildAiTab() });

        var save = Ui.Button("Save Finding", "Check", accent: true, tip: "Save the finding and its evidence links (" + Ui.Chord("S") + ")");
        save.Click += (_, _) => Run(async () => await SaveAsync());
        var bar = Ui.ButtonBar(null, save);

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(tabs);
        return dock;
    }

    private Control BuildFindingTab()
    {
        var form = new StackPanel();
        form.Children.Add(Ui.Field("Short title", _title));
        form.Children.Add(Ui.Field("Proposition being assessed", _statement));
        form.Children.Add(Ui.Pair(Ui.Field("Status", _status), Ui.Field("Research question", _question)));
        form.Children.Add(Ui.Field("Researcher conclusion", _conclusion));
        form.Children.Add(Ui.Muted("Changing a status is a human judgment. Linked evidence remains visible on its own tab."));
        return Ui.Scroll(form, new Thickness(4, 14, 4, 14));
    }

    private Control BuildEvidenceTab()
    {
        var note = Ui.Muted("Check only evidence you have actually weighed for this proposition. Its role here may differ from its project-level relationship.");
        note.Margin = new Thickness(4, 12, 4, 10);

        var columnHeader = new Grid { ColumnDefinitions = new ColumnDefinitions(LinkColumns), Margin = new Thickness(10, 7) };
        foreach (var (text, column) in new[] { ("Link", 0), ("Role", 1), ("Review", 2), ("Evidence", 3), ("Reference", 4) })
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
        table.Children.Add(_evidence);
        var frame = new Border { Child = table, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("Cc.Rule"));
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("Cc.Page"));

        var dock = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        dock.Children.Add(note);
        dock.Children.Add(frame);
        return dock;
    }

    private Control BuildLinkRow(EvidenceLinkRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(LinkColumns) };
        var link = new CheckBox { IsChecked = row.Linked, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        link.IsCheckedChanged += (_, _) => row.Linked = link.IsChecked == true;
        var role = Ui.EnumCombo<EvidenceRelationship>();
        role.Margin = new Thickness(0, 0, 10, 0);
        Ui.Select(role, row.Relationship);
        role.SelectionChanged += (_, _) => row.Relationship = Ui.Value<EvidenceRelationship>(role);

        Grid.SetColumn(link, 0);
        Grid.SetColumn(role, 1);
        grid.Children.Add(link);
        grid.Children.Add(role);
        foreach (var (text, column) in new[] { (Words.Of(row.Evidence.Judgment), 2), (row.Evidence.Title, 3), (row.Reference, 4) })
        {
            var cell = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private Control BuildAiTab()
    {
        var draft = Ui.Button("Draft from Linked Evidence", "Sparkle");
        var copy = Ui.Button("Copy Candidate", "Copy");
        draft.Click += (_, _) => Run(DraftWithGeminiAsync);
        copy.Click += (_, _) => Run(async () =>
        {
            if (!string.IsNullOrWhiteSpace(_aiCandidate.Text)) await ClipboardText.SetAsync(this, _aiCandidate.Text);
        });

        var top = new StackPanel { Spacing = 8, Margin = new Thickness(4, 12, 4, 10) };
        top.Children.Add(Ui.Row(draft, copy));
        top.Children.Add(_aiProvenance);

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        _aiCandidate.Margin = new Thickness(0, 0, 0, 10);
        dock.Children.Add(_aiCandidate);
        return dock;
    }

    protected override bool OnCommandKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.S:
                Run(async () => await SaveAsync());
                return true;
            case Key.N:
                NewFinding();
                return true;
            default:
                return false;
        }
    }

    internal async Task LoadWorkspaceAsync(long selectId = 0)
    {
        _questions = await _research.GetQuestionsAsync(_project.ResearchProjectId);
        _allEvidence = await _research.GetEvidenceAsync(_project.ResearchProjectId);
        _claims = await _research.GetScholarlyClaimsAsync(_project.ResearchProjectId);
        Ui.SetChoices(_question, Ui.QuestionChoices("General project finding", _questions), Ui.Value<long?>(_question));
        _findingItems = await _findingsRepo.GetAsync(_project.ResearchProjectId);

        _loading = true;
        try
        {
            _findings.ItemsSource = _findingItems;
            _findings.SelectedItem = (selectId > 0 ? _findingItems.FirstOrDefault(f => f.ResearchFindingId == selectId) : null) ?? _findingItems.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }

        await ShowFindingAsync(_findings.SelectedItem as ResearchFinding);
    }

    private void NewFinding()
    {
        _loading = true;
        try
        {
            _findings.SelectedItem = null;
        }
        finally
        {
            _loading = false;
        }

        _editing = new ResearchFinding { ResearchProjectId = _project.ResearchProjectId, SortOrder = _findingItems.Count };
        PopulateEditor(_editing, Array.Empty<ResearchFindingEvidenceLink>());
        _title.Focus();
    }

    private async Task ShowFindingAsync(ResearchFinding? finding)
    {
        _editing = finding;
        IReadOnlyCollection<ResearchFindingEvidenceLink> links = finding?.ResearchFindingId > 0
            ? await _findingsRepo.GetLinksAsync(finding.ResearchFindingId)
            : Array.Empty<ResearchFindingEvidenceLink>();
        PopulateEditor(finding, links);
    }

    private void PopulateEditor(ResearchFinding? finding, IReadOnlyCollection<ResearchFindingEvidenceLink> links)
    {
        _title.Text = finding?.Title ?? string.Empty;
        _statement.Text = finding?.Statement ?? string.Empty;
        Ui.Select(_status, finding?.Status ?? ResearchFindingStatus.Hypothesis);
        Ui.Select(_question, finding?.ResearchQuestionId);
        _conclusion.Text = finding?.ResearcherConclusion ?? string.Empty;
        _aiCandidate.Text = finding?.AiCandidateSynthesis ?? string.Empty;
        _aiProvenance.Text = finding?.AiCandidateSynthesis == null
            ? "No AI candidate has been generated. Nothing is sent without your explicit action."
            : $"Candidate only · {finding.AiModel ?? "unknown model"} · {finding.AiGeneratedUtc?.ToLocalTime():g}";
        var byEvidence = links.ToDictionary(link => link.EvidenceItemId);
        _linkRows = _allEvidence.Select(item =>
        {
            byEvidence.TryGetValue(item.EvidenceItemId, out var link);
            return new EvidenceLinkRow(item, link != null, link?.Relationship ?? item.Relationship);
        }).ToList();
        _evidence.ItemsSource = _linkRows;
    }

    /// <summary>Saves the finding and its links; false when it could not be saved.</summary>
    internal async Task<bool> SaveAsync()
    {
        if (_editing == null) return false;
        if (string.IsNullOrWhiteSpace(_title.Text) || string.IsNullOrWhiteSpace(_statement.Text))
        {
            await Alert.Info(this, "Save finding", "A finding needs a short title and a proposition to assess.");
            return false;
        }

        _editing.Title = _title.Text.Trim();
        _editing.Statement = _statement.Text.Trim();
        _editing.Status = Ui.Value<ResearchFindingStatus>(_status);
        _editing.ResearchQuestionId = Ui.Value<long?>(_question);
        _editing.ResearcherConclusion = Ui.Clean(_conclusion.Text);
        await _findingsRepo.SaveAsync(_editing);
        var links = _linkRows
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
        return true;
    }

    private async Task RemoveAsync()
    {
        if (_editing?.ResearchFindingId is not > 0) return;
        if (!await ConfirmAsync("Remove finding", $"Remove finding “{_editing.Title}”? Evidence records will not be deleted.", "Remove", AlertKind.Warning)) return;

        await _findingsRepo.DeleteAsync(_editing.ResearchFindingId);
        await LoadWorkspaceAsync();
    }

    private async Task DraftWithGeminiAsync()
    {
        if (_editing?.ResearchFindingId is not > 0)
        {
            await Alert.Info(this, "AI candidate synthesis", "Save the finding and its evidence links before requesting a synthesis.");
            return;
        }

        // Stop if the save did not happen. It used to warn and carry on, sending Gemini
        // the old saved proposition beside links that had never been saved - and then
        // reloading over the edits the warning had been about.
        if (!await SaveAsync()) return;

        var rows = _linkRows.Where(row => row.Linked).ToList();
        if (rows.Count == 0)
        {
            await Alert.Info(this, "AI candidate synthesis", "Link at least one evidence item before asking for a synthesis.");
            return;
        }

        if (!await EnsureGeminiKeyAsync()) return;

        var evidenceContext = rows.Select(row =>
        {
            var item = row.Evidence;
            var excerpt = item.Excerpt ?? "(no excerpt recorded)";
            if (excerpt.Length > 1800) excerpt = excerpt[..1800] + "…";
            return $"{row.Relationship}; human judgment {item.Judgment}; {item.Title}; " +
                   $"ref {item.CanonicalReference ?? item.StableIdentifier ?? "none"}; excerpt: {excerpt}; " +
                   $"researcher note: {item.ResearcherNote ?? "none"}";
        }).ToList();
        var relevantClaims = _claims.Where(c => _editing.ResearchQuestionId == null || c.ResearchQuestionId == _editing.ResearchQuestionId)
            .Select(c => $"{c.Claimant}: {c.ClaimText} ({c.Judgment}; {c.Relationship}; {c.Locator ?? "no locator"})")
            .ToList();
        if (!await ConfirmSendAsync("Send synthesis context to Gemini?",
                "This will send to Gemini:\n\n" +
                "• the project name and project notes\n" +
                "• this proposition\n" +
                $"• {rows.Count} linked evidence item(s) - for each: its title, reference, your judgment and relationship, up to 1800 characters of the excerpt, and your researcher note on it\n" +
                $"• {relevantClaims.Count} recorded scholarly claim(s)\n\n" +
                "It will not send the rest of your corpus or the rest of the Research Bench. " +
                "The response will be saved only as an AI candidate.\n\nContinue?"))
            return;

        SetBusy(true);
        _statusLine.Text = "Gemini is drafting a provisional synthesis…";
        try
        {
            var result = await GeminiTranslationService.DraftResearchSynthesisAsync(
                _project.Name + (string.IsNullOrWhiteSpace(_project.Notes) ? string.Empty : $"\n{_project.Notes}"),
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
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ExportDossierAsync()
    {
        var path = await Files.SaveAsync(this, "Export research dossier",
            Ui.SafeName(_project.Name, "project") + "-research-dossier.md", Markdown, Files.Text);
        if (path == null) return;
        await ExportDossierToAsync(path);
    }

    /// <summary>Writes the project's dossier to <paramref name="path"/> and records the export in the log.</summary>
    internal async Task<bool> ExportDossierToAsync(string path)
    {
        var findings = await _findingsRepo.GetAsync(_project.ResearchProjectId);
        var links = new Dictionary<long, IReadOnlyList<ResearchFindingEvidenceLink>>();
        foreach (var finding in findings)
            links[finding.ResearchFindingId] = await _findingsRepo.GetLinksAsync(finding.ResearchFindingId);

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

        // Reported rather than thrown, as the bibliography export already does: a
        // read-only folder or a file open elsewhere put up the crash dialog here. The
        // log records the export only once the file exists.
        try
        {
            await File.WriteAllTextAsync(path, ResearchDossierExport.ToMarkdown(dossier));
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Export failed", $"Could not write the file.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            return false;
        }

        await _research.AddSystemResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = _project.ResearchProjectId,
            Kind = ResearchLogEntryKind.ResearchDossierExported,
            Summary = "Exported research dossier",
            Details = Path.GetFileName(path)
        });
        _statusLine.Text = $"Exported {Path.GetFileName(path)}.";
        return true;
    }

    // ── What the tests drive and read ─────────────────────────────────────

    internal IReadOnlyList<ResearchFinding> Findings => _findingItems;

    internal void Draft(string title, string statement, string? conclusion = null)
    {
        _title.Text = title;
        _statement.Text = statement;
        if (conclusion != null) _conclusion.Text = conclusion;
    }

    internal void Link(long evidenceId, EvidenceRelationship role)
    {
        var row = _linkRows.FirstOrDefault(r => r.Evidence.EvidenceItemId == evidenceId);
        if (row == null) return;
        row.Linked = true;
        row.Relationship = role;
        _evidence.ItemsSource = null;
        _evidence.ItemsSource = _linkRows;
    }

    internal void StartNewFinding() => NewFinding();

    internal void ShowTab(int index) => _tabs.SelectedIndex = index;

    private sealed class EvidenceLinkRow
    {
        public EvidenceLinkRow(EvidenceItem evidence, bool linked, EvidenceRelationship relationship)
        {
            Evidence = evidence;
            Linked = linked;
            Relationship = relationship;
        }

        public EvidenceItem Evidence { get; }
        public bool Linked { get; set; }
        public EvidenceRelationship Relationship { get; set; }
        public string Reference => Evidence.CanonicalReference ?? Evidence.StableIdentifier ?? string.Empty;
    }
}
