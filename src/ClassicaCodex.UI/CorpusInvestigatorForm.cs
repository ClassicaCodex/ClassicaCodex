using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Expands one reviewed parallel into a bounded, researcher-chosen local corpus. Gemini can rank
/// candidates, but opaque prompt keys must resolve locally before anything can be displayed or saved.
/// </summary>
public sealed class CorpusInvestigatorForm : ScaledForm
{
    private const int MaxWorks = 8;
    private const int MaxCorpusChars = 220_000;

    private readonly ResearchProject _project;
    private readonly IntertextualAtlasConnection _seed;
    private readonly EditionRepository _editions = new();
    private readonly TextNodeRepository _nodes = new();
    private readonly ResearchRepository _research = new();
    private readonly CheckedListBox _works = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly TextBox _filter = new();
    private readonly TextBox _focus = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _run = new() { Text = "Investigate with Gemini", Width = 170 };
    private readonly Button _save = new() { Text = "Save verified candidates…", Width = 175, Enabled = false };
    private List<WorkOption> _allWorks = [];
    private readonly HashSet<int> _selectedEditionIds = [];
    private BindingList<ResultRow> _results = [];
    private PassageResearchIdentity? _source;
    private GeminiCorpusInvestigationResult? _aiResult;
    private DateTime? _generatedUtc;
    private string? _scope;

    public CorpusInvestigatorForm(ResearchProject project, IntertextualAtlasConnection seed)
    {
        _project = project; _seed = seed;
        Text = $"Corpus Investigator — {project.Name}";
        Width = 1250; Height = 800; MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterParent;
        AppIcons.ApplyWindowIcon(this, "SimilarWorks");

        var seedPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 150, Padding = new Padding(10), ColumnCount = 2, RowCount = 2 };
        seedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); seedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        seedPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); seedPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        seedPanel.Controls.Add(Label($"Reviewed source — {seed.SourceAuthorName}, {seed.SourceWorkTitle} [{seed.Investigation.SourceCitationRef}]"), 0, 0);
        seedPanel.Controls.Add(Label($"Seed parallel — {seed.Result.TargetAuthorName}, {seed.Result.TargetWorkTitle} [{seed.Result.TargetCitationRef}]"), 1, 0);
        seedPanel.Controls.Add(PassageBox(seed.Investigation.SourceText), 0, 1); seedPanel.Controls.Add(PassageBox(seed.Result.TargetText), 1, 1);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var instructions = new Label { Dock = DockStyle.Top, Height = 42, Text = "Choose up to eight original-language works. The model may suggest reinforcing parallels, counterexamples, and generic conventions; every returned passage key is checked against this exact local selection." };
        var focusLabel = new Label { Dock = DockStyle.Top, Height = 22, Text = "Investigative focus (editable)" };
        _focus.Dock = DockStyle.Top; _focus.Height = 72;
        _filter.Dock = DockStyle.Top; _filter.PlaceholderText = "Filter authors or works";
        _works.Dock = DockStyle.Fill;
        left.Controls.Add(_works); left.Controls.Add(_filter); left.Controls.Add(_focus); left.Controls.Add(focusLabel); left.Controls.Add(instructions);

        _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = false; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.DataSource = _results;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResultRow.Role), HeaderText = "Role", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResultRow.Confidence), HeaderText = "Confidence", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResultRow.Target), HeaderText = "Target", Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResultRow.Citation), HeaderText = "Citation", Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ResultRow.Assessment), HeaderText = "Passage / assessment", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        var split = new SplitContainer { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(_grid);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 49, Padding = new Padding(9) };
        _run.Click += async (_, _) => await RunAsync(); _save.Click += (_, _) => Save();
        bottom.Controls.Add(_run); bottom.Controls.Add(_save); bottom.Controls.Add(_status);
        _status.AutoSize = true; _status.Padding = new Padding(8, 7, 0, 0);
        Controls.Add(split); Controls.Add(seedPanel); Controls.Add(bottom);

        _focus.Text = BuildInitialFocus();
        _filter.TextChanged += (_, _) => RefreshWorks();
        Load += async (_, _) => await LoadAsync();
        Shown += (_, _) =>
        {
            split.Panel1MinSize = 280; split.Panel2MinSize = 450;
            var desired = Math.Min(360, split.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth);
            if (desired >= split.Panel1MinSize) split.SplitterDistance = desired;
        };
        ReadingTheme.AttachTo(this, () => _status.ForeColor = ReadingTheme.MutedText);
        WindowShortcuts.CloseOnEscape(this);
    }

    private string BuildInitialFocus()
    {
        var motifs = string.IsNullOrWhiteSpace(_seed.Result.MotifTags) ? "the reviewed resemblance" : _seed.Result.MotifTags;
        return $"Test whether {motifs} recurs in a distinctive way, or is better explained as a generic/shared convention. " +
               (_seed.Result.ParallelNote ?? _seed.Result.Rationale ?? string.Empty);
    }

    private async Task LoadAsync()
    {
        var resolved = await _nodes.ResolvePassageNavigationAsync(_seed.Investigation.SourceTextNodeId,
            _seed.Investigation.SourceEditionCtsUrn, _seed.Investigation.SourceCitationRef, _seed.Investigation.SourceText);
        _source = resolved == null ? null : await _nodes.GetPassageResearchIdentityAsync(resolved.Value.TextNodeId);
        _allWorks = (await _editions.GetAllOriginalEditionsAsync())
            .Where(e => e.WorkId != _project.WorkId)
            .Select(e => new WorkOption(e)).ToList();
        RefreshWorks();
        var seedIndex = Enumerable.Range(0, _works.Items.Count).FirstOrDefault(i =>
            (_works.Items[i] as WorkOption)?.Edition.WorkId == _seed.Result.TargetWorkId, -1);
        if (seedIndex >= 0)
        {
            var seedWork = (WorkOption)_works.Items[seedIndex];
            _selectedEditionIds.Add(seedWork.Edition.EditionId); _works.SetItemChecked(seedIndex, true);
        }
        if (_source == null) { _run.Enabled = false; _status.Text = "The reviewed source passage no longer resolves in the local corpus."; }
    }

    private void RefreshWorks()
    {
        SyncVisibleSelections();
        var filter = _filter.Text.Trim(); _works.Items.Clear();
        foreach (var work in _allWorks.Where(w => filter.Length == 0 || w.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            _works.Items.Add(work, _selectedEditionIds.Contains(work.Edition.EditionId));
    }

    private void SyncVisibleSelections()
    {
        for (var i = 0; i < _works.Items.Count; i++)
        {
            var editionId = ((WorkOption)_works.Items[i]).Edition.EditionId;
            if (_works.GetItemChecked(i)) _selectedEditionIds.Add(editionId);
            else _selectedEditionIds.Remove(editionId);
        }
    }

    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settings = new TranslateApiSettingsForm();
            settings.ShowDialog(this);
            // Continue if a key was entered. Returning unconditionally made the user
            // click the button a second time for no reason they could see.
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return;
        }
        SyncVisibleSelections();
        var selected = _allWorks.Where(w => _selectedEditionIds.Contains(w.Edition.EditionId)).ToList();
        if (selected.Count == 0) { MessageBox.Show(this, "Choose at least one comparison work."); return; }
        if (selected.Count > MaxWorks) { MessageBox.Show(this, $"Choose no more than {MaxWorks} works so each receives a meaningful share of the bounded prompt."); return; }
        if (string.IsNullOrWhiteSpace(_focus.Text)) { MessageBox.Show(this, "Describe the pattern or question this investigation should test."); return; }
        if (TranslationSettings.AlwaysConfirmBeforeSending && MessageBox.Show(this,
            $"This will send both reviewed passages and a bounded selection from {selected.Count} local work(s) to Gemini's API, along with the project name/notes, research questions, and investigative focus. Continue?",
            "Send bounded corpus to Gemini?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _run.Enabled = false; _save.Enabled = false; _status.Text = "Building a balanced local corpus…"; _results.Clear();
        try
        {
            var corpus = await BuildCorpusAsync(selected);
            if (corpus.Map.Count == 0)
                throw new InvalidOperationException("The selected editions contain no reading-text passages to investigate.");
            _scope = corpus.Scope;
            var questions = await _research.GetQuestionsAsync(_project.ResearchProjectId);
            var context = $"Project: {_project.Name}\nNotes: {_project.Notes ?? "(none)"}\nQuestions:\n" +
                          string.Join("\n", questions.Select(q => "- " + q.Text));
            _status.Text = $"Asking Gemini to inspect {corpus.Map.Count:N0} local passages…";
            _aiResult = await GeminiTranslationService.InvestigateIntertextualCorpusAsync(context, _focus.Text.Trim(),
                _seed.SourceAuthorName, _seed.SourceWorkTitle, _seed.Investigation.SourceCitationRef, _seed.Investigation.SourceText, _seed.Investigation.SourceLanguage,
                _seed.Result.TargetAuthorName, _seed.Result.TargetWorkTitle, _seed.Result.TargetCitationRef, _seed.Result.TargetText, _seed.Result.TargetLanguage,
                corpus.Scope, corpus.Hash, corpus.TaggedText, TranslationSettings.GeminiApiKey!);
            _generatedUtc = DateTime.UtcNow;
            var unresolved = 0; var returnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in _aiResult.Candidates)
            {
                if (!corpus.Map.TryGetValue(candidate.CandidateKey, out var passage)) { unresolved++; continue; }
                if (!returnedKeys.Add(candidate.CandidateKey)) continue;
                _results.Add(new ResultRow(passage, candidate));
            }
            _save.Enabled = _results.Count > 0;
            _status.Text = $"{_results.Count} locally verified candidate(s)." +
                           (unresolved > 0 ? $" {unresolved} invented/invalid key(s) discarded." : string.Empty);
        }
        catch (Exception ex) { _status.Text = "Couldn't finish: " + ex.Message; }
        finally { _run.Enabled = true; }
    }

    private async Task<CorpusBlock> BuildCorpusAsync(IReadOnlyList<WorkOption> selected)
    {
        var map = new Dictionary<string, CorpusPassage>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder(); var scopes = new List<string>(); var keyNumber = 0;
        var perWork = MaxCorpusChars / selected.Count;
        foreach (var work in selected)
        {
            var used = 0; string? lastRef = null; var truncated = false;
            foreach (var node in await _nodes.GetByEditionAsync(work.Edition.EditionId, readingLinesOnly: true))
            {
                if (string.IsNullOrWhiteSpace(node.Text)) continue;
                if (node.TextNodeId == _seed.Result.TargetTextNodeId) continue; // the known seed is not a new candidate
                var key = $"P{keyNumber + 1:000000}";
                var line = $"[{key}] {work.Edition.AuthorName}, {work.Edition.WorkTitle} [{node.CitationRef}] ({work.Edition.Language ?? "unknown"}) {node.Text}\n";
                if (used + line.Length > perWork) { truncated = true; break; }
                keyNumber++; used += line.Length; lastRef = node.CitationRef; builder.Append(line);
                map[key] = new CorpusPassage(work, node);
            }
            scopes.Add($"{work.Edition.AuthorName}, {work.Edition.WorkTitle}: " +
                       (truncated ? $"bounded through [{lastRef}]" : "complete ingested reading text"));
        }
        var tagged = builder.ToString();
        return new CorpusBlock(tagged, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tagged))),
            string.Join("; ", scopes), map);
    }

    private void Save()
    {
        if (_source == null || _aiResult == null || _results.Count == 0) return;
        var request = new EchoCaptureRequest(ResearchEchoMethod.AiCorpusInvestigation, _source,
            $"Corpus investigation: {_focus.Text.Trim()}", _scope,
            "Gemini expansion from a human-reviewed seed pair. Opaque candidate keys were resolved exactly against the bounded local corpus; role labels and rationales remain hypotheses for human review.",
            _aiResult.Model, _aiResult.PromptProvenance, _generatedUtc,
            _results.Select(r => new EchoCaptureCandidate(r.Passage.Work.Edition.WorkId, r.Passage.Node.TextNodeId,
                r.Passage.Work.Edition.AuthorName, r.Passage.Work.Edition.WorkTitle, r.Passage.Node.CitationRef,
                r.Passage.Node.Text, null, $"{r.Candidate.Role} · {r.Candidate.Confidence}",
                $"{r.Candidate.Rationale}" + (string.IsNullOrWhiteSpace(r.Candidate.SuggestedMotifs) ? "" : $" Motifs: {r.Candidate.SuggestedMotifs}"))).ToList());
        using var capture = new ResearchEchoCaptureForm(request, _project.ResearchProjectId);
        if (capture.ShowDialog(this) == DialogResult.OK)
            MessageBox.Show(this, "The verified candidates and full Gemini prompt provenance are saved as a pending echo investigation.");
    }

    private static Label Label(string text) => new() { Text = text, AutoSize = true };
    private static TextBox PassageBox(string text) => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = text };

    private sealed class WorkOption
    {
        public WorkOption((int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language) edition)
        { Edition = edition; Label = $"{edition.AuthorName} — {edition.WorkTitle} ({edition.Language?.ToUpperInvariant() ?? "?"})"; }
        public (int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language) Edition { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }
    private sealed record CorpusPassage(WorkOption Work, TextNode Node);
    private sealed record CorpusBlock(string TaggedText, string Hash, string Scope, Dictionary<string, CorpusPassage> Map);
    private sealed class ResultRow
    {
        public ResultRow(CorpusPassage passage, CorpusInvestigationCandidate candidate) { Passage = passage; Candidate = candidate; }
        public CorpusPassage Passage { get; } public CorpusInvestigationCandidate Candidate { get; }
        public string Role => Candidate.Role; public string Confidence => Candidate.Confidence;
        public string Target => $"{Passage.Work.Edition.AuthorName}, {Passage.Work.Edition.WorkTitle}";
        public string Citation => Passage.Node.CitationRef;
        public string Assessment => $"{Passage.Node.Text} — {Candidate.Rationale}" +
            (string.IsNullOrWhiteSpace(Candidate.SuggestedMotifs) ? "" : $" [Motifs: {Candidate.SuggestedMotifs}]");
    }
}
