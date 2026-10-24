using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research.Claims;

/// <summary>
/// A matrix of attributed scholarly propositions. Source wording, stance and
/// the researcher's verification remain separate rather than collapsing into
/// a single note.
/// </summary>
internal sealed class ScholarlyClaimsWindow : ResearchWindow
{
    private readonly ResearchProject _project;
    private readonly ResearchRepository _repo = new();
    private readonly ComboBox _filter = new() { MinWidth = 320 };
    private readonly TextBlock _summary = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _grid = Ui.Table();
    private readonly TextBox _claimant = Ui.Line();
    private readonly TextBox _claimText = Ui.Area(65);
    private readonly TextBox _locator = Ui.Line();
    private readonly ComboBox _question = Ui.Combo(Ui.QuestionChoices("General project claim", Array.Empty<ResearchQuestion>()));
    private readonly ComboBox _source = Ui.Combo(new[] { new Choice<long?>(null, "No linked source evidence") });
    private readonly ComboBox _relationship = Ui.EnumCombo<EvidenceRelationship>();
    private readonly ComboBox _judgment = Ui.EnumCombo<EvidenceJudgment>();
    private readonly TextBox _notes = Ui.Area(58);
    private readonly TextBlock _status = Ui.Status();
    private readonly long _initialClaimId;

    private List<ResearchQuestion> _questions = new();
    private List<EvidenceItem> _sources = new();
    private List<ScholarlyClaim> _claims = new();
    private ScholarlyClaim? _editing;
    private bool _reloading;

    public ScholarlyClaimsWindow(ResearchProject project, long initialClaimId = 0) : base(null)
    {
        _project = project;
        _initialClaimId = initialClaimId;
        Title = $"Scholarly Claims Matrix — {project.Name}";
        Width = 1220;
        Height = 800;
        MinWidth = 900;
        MinHeight = 640;

        _filter.SelectionChanged += (_, _) =>
        {
            if (!_reloading) RenderMatrix();
        };
        var show = new TextBlock { Text = "Show:", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Margin = new Thickness(16, 12, 16, 10) };
        var left = Ui.Row(show, _filter);
        DockPanel.SetDock(left, Dock.Left);
        _summary.Margin = new Thickness(16, 0, 0, 0);
        header.Children.Add(left);
        header.Children.Add(_summary);
        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Cc.PageHeader"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Cc.Rule"));

        _grid.Columns.Add(Ui.Column("Review", nameof(ClaimRow.Review), 88));
        _grid.Columns.Add(Ui.Column("Stance", nameof(ClaimRow.Stance), 122));
        _grid.Columns.Add(Ui.Column("Scholar / claimant", nameof(ClaimRow.Claimant), 160));
        _grid.Columns.Add(Ui.Column("Claim", nameof(ClaimRow.ClaimText)));
        _grid.Columns.Add(Ui.Column("Linked source", nameof(ClaimRow.SourceTitle), 210));
        _grid.Columns.Add(Ui.Column("Page / locator", nameof(ClaimRow.Locator), 124));
        _grid.Margin = new Thickness(12, 10, 12, 10);
        _grid.SelectionChanged += (_, _) =>
        {
            if (!_reloading) ShowClaim(CurrentRow?.Claim);
        };

        var split = Ui.Stack(_grid, BuildEditor(), 330, 180, 280, bottomHeight: 400);
        var status = Ui.StatusBar(_status);
        var dock = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        dock.Children.Add(headerBorder);
        dock.Children.Add(status);
        dock.Children.Add(split);
        Content = dock;

        Opened += (_, _) => Run(() => ReloadAsync(_initialClaimId));
    }

    private Control BuildEditor()
    {
        _claimText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var form = new StackPanel();
        form.Children.Add(Ui.Pair(Ui.Field("Scholar / claimant", _claimant), Ui.Field("Page, section, or other exact locator", _locator)));
        form.Children.Add(Ui.Field("Claim (record the proposition, not your conclusion)", _claimText));
        form.Children.Add(Ui.Pair(Ui.Field("Research question", _question), Ui.Field("Linked source evidence", _source)));
        form.Children.Add(Ui.Pair(Ui.Field("Relationship to the working theory", _relationship), Ui.Field("Human verification", _judgment)));
        form.Children.Add(Ui.Field("Researcher note / qualification", _notes));

        var create = Ui.Button("New Claim", "Plus", tip: "Start a new claim (" + Ui.Chord("N") + ")");
        var remove = Ui.Button("Remove", "Minus");
        var save = Ui.Button("Save Claim", "Check", accent: true, tip: "Save the claim in the editor (" + Ui.Chord("S") + ")");
        create.Click += (_, _) => NewClaim();
        save.Click += (_, _) => Run(SaveAsync);
        remove.Click += (_, _) => Run(RemoveAsync);

        var bar = Ui.ButtonBar(Ui.Row(create, remove), save);
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
                Run(SaveAsync);
                return true;
            case Key.N:
                NewClaim();
                return true;
            default:
                return false;
        }
    }

    // The selection: the editor must describe the highlighted claim.
    private ClaimRow? CurrentRow => _grid.SelectedItem as ClaimRow;

    internal async Task ReloadAsync(long selectId = 0, long? revealQuestionId = null)
    {
        _questions = await _repo.GetQuestionsAsync(_project.ResearchProjectId);
        _sources = await _repo.GetEvidenceAsync(_project.ResearchProjectId);
        _claims = await _repo.GetScholarlyClaimsAsync(_project.ResearchProjectId);

        _reloading = true;
        try
        {
            Ui.SetChoices(_question, Ui.QuestionChoices("General project claim", _questions), Ui.Value<long?>(_question));

            var sourceChoices = new List<Choice<long?>> { new(null, "No linked source evidence") };
            sourceChoices.AddRange(_sources
                .OrderBy(e => e.Type == EvidenceType.Scholarship ? 0 : 1)
                .ThenBy(e => e.Title)
                .Select(e => new Choice<long?>(e.EvidenceItemId, $"[{Words.Of(e.Type)}] {e.Title}")));
            Ui.SetChoices(_source, sourceChoices, Ui.Value<long?>(_source));

            var filterId = revealQuestionId ?? (_filter.SelectedItem is Choice<long> current ? current.Value : -1);
            var filters = new List<Choice<long>> { new(-1, "All questions"), new(0, "General / unlinked claims") };
            filters.AddRange(_questions.Select(q => new Choice<long>(q.ResearchQuestionId, q.Text)));
            Ui.SetChoices(_filter, filters, filterId);
        }
        finally
        {
            _reloading = false;
        }

        RenderMatrix(selectId);
    }

    private void RenderMatrix(long selectId = 0)
    {
        if (_filter.SelectedItem is not Choice<long> filter) return;

        var visible = filter.Value switch
        {
            -1 => _claims,
            0 => _claims.Where(c => c.ResearchQuestionId == null).ToList(),
            _ => _claims.Where(c => c.ResearchQuestionId == filter.Value).ToList()
        };
        var sourceNames = _sources.ToDictionary(e => e.EvidenceItemId, e => e.Title);
        var rows = visible.Select(c => new ClaimRow(
            c,
            c.SourceEvidenceItemId is long id && sourceNames.TryGetValue(id, out var title) ? title : "—")).ToList();

        _reloading = true;
        ClaimRow? selected;
        try
        {
            _grid.ItemsSource = rows;
            // The row to show, then the selection, then the editor - explicitly, and
            // the same row for all three. Windows once selected first and let the
            // event bind the editor while the first row was still current, so the
            // editor showed the first claim beside a highlighted other one, and Save
            // wrote to the claim on screen.
            selected = (selectId != 0 ? rows.FirstOrDefault(r => r.Claim.ScholarlyClaimId == selectId) : null) ?? rows.FirstOrDefault();
            Ui.Reveal(_grid, selected);
        }
        finally
        {
            _reloading = false;
        }

        _summary.Text = $"{rows.Count} shown • {_claims.Count(c => c.Judgment == EvidenceJudgment.Uncertain)} awaiting verification";
        ShowClaim(selected?.Claim);
    }

    private void NewClaim()
    {
        _reloading = true;
        try
        {
            _grid.SelectedItem = null;
        }
        finally
        {
            _reloading = false;
        }

        ShowClaim(new ScholarlyClaim { ResearchProjectId = _project.ResearchProjectId, SortOrder = _claims.Count });
        _claimant.Focus();
    }

    private void ShowClaim(ScholarlyClaim? claim)
    {
        _editing = claim;
        _claimant.Text = claim?.Claimant ?? string.Empty;
        _claimText.Text = claim?.ClaimText ?? string.Empty;
        _locator.Text = claim?.Locator ?? string.Empty;
        Ui.Select(_question, claim?.ResearchQuestionId);
        Ui.Select(_source, claim?.SourceEvidenceItemId);
        Ui.Select(_relationship, claim?.Relationship ?? EvidenceRelationship.Contextualizes);
        Ui.Select(_judgment, claim?.Judgment ?? EvidenceJudgment.Uncertain);
        _notes.Text = claim?.Notes ?? string.Empty;
    }

    internal async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_claimant.Text))
        {
            await Alert.Info(this, "Save claim", "Attribute the claim to a scholar or source.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_claimText.Text))
        {
            await Alert.Info(this, "Save claim", "Enter the scholarly claim.");
            return;
        }

        var claim = _editing ?? new ScholarlyClaim { ResearchProjectId = _project.ResearchProjectId, SortOrder = _claims.Count };
        claim.Claimant = _claimant.Text.Trim();
        claim.ClaimText = _claimText.Text.Trim();
        claim.Locator = Ui.Clean(_locator.Text);
        claim.ResearchQuestionId = Ui.Value<long?>(_question);
        claim.SourceEvidenceItemId = Ui.Value<long?>(_source);
        claim.Relationship = Ui.Value<EvidenceRelationship>(_relationship);
        claim.Judgment = Ui.Value<EvidenceJudgment>(_judgment);
        claim.Notes = Ui.Clean(_notes.Text);
        await _repo.SaveScholarlyClaimAsync(claim);

        // Move the filter only if it would now hide the claim. Revealing its question
        // unconditionally took "All questions" to "General / unlinked claims" every
        // time an unlinked claim was saved.
        var filter = _filter.SelectedItem is Choice<long> current ? current.Value : -1;
        var claimFilter = claim.ResearchQuestionId ?? 0;
        await ReloadAsync(claim.ScholarlyClaimId, filter == -1 || filter == claimFilter ? null : claimFilter);
        _status.Text = "Claim saved. Source wording, stance, and human verification remain separate.";
    }

    private async Task RemoveAsync()
    {
        var claim = _editing;
        if (claim?.ScholarlyClaimId is not > 0) return;
        if (!await ConfirmAsync("Remove claim", $"Remove the claim attributed to {claim.Claimant}?", "Remove", AlertKind.Warning)) return;

        await _repo.DeleteScholarlyClaimAsync(claim.ScholarlyClaimId);
        await ReloadAsync();
        _status.Text = "Claim removed; the change remains visible in the research log.";
    }

    // ── What the tests drive and read ─────────────────────────────────────

    internal ScholarlyClaim? Editing => _editing;
    internal ScholarlyClaim? Highlighted => CurrentRow?.Claim;
    internal string EditorClaimant => _claimant.Text ?? string.Empty;

    internal void Highlight(long claimId) =>
        _grid.SelectedItem = (_grid.ItemsSource as IEnumerable<ClaimRow>)?.FirstOrDefault(r => r.Claim.ScholarlyClaimId == claimId);

    internal void Draft(string claimant, string claimText, string? locator = null)
    {
        _claimant.Text = claimant;
        _claimText.Text = claimText;
        if (locator != null) _locator.Text = locator;
    }

    internal void StartNewClaim() => NewClaim();

    private sealed class ClaimRow
    {
        public ClaimRow(ScholarlyClaim claim, string sourceTitle)
        {
            Claim = claim;
            SourceTitle = sourceTitle;
        }

        public ScholarlyClaim Claim { get; }
        public string Review => Words.Of(Claim.Judgment);
        public string Stance => Words.Of(Claim.Relationship);
        public string Claimant => Claim.Claimant;
        public string ClaimText => Claim.ClaimText;
        public string SourceTitle { get; }
        public string Locator => Claim.Locator ?? "—";
    }
}
