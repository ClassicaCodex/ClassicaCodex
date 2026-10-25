using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Echoes;

/// <summary>
/// Expands one reviewed parallel into a bounded, researcher-chosen local corpus. Gemini can rank
/// candidates, but opaque prompt keys must resolve locally before anything can be displayed or saved.
/// </summary>
internal sealed class CorpusInvestigatorWindow : ResearchWindow
{
    private const int MaxWorks = 8;
    private const int MaxCorpusChars = 220_000;

    private readonly ResearchProject _project;
    private readonly IntertextualAtlasConnection _seed;
    private readonly EditionRepository _editions = new();
    private readonly TextNodeRepository _nodes = new();
    private readonly ResearchRepository _research = new();
    private readonly ListBox _works;
    private readonly TextBox _filter = new() { Classes = { "search" }, PlaceholderText = "Filter authors or works" };

    /// <summary>Collapses a burst of typing in the work filter into one refill.</summary>
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TextBox _focus = Ui.Area(72);
    private readonly DataGrid _grid = Ui.Table();
    private readonly TextBlock _status = Ui.Status();
    private readonly Button _run = Ui.Button("Investigate with Gemini", "Sparkle", accent: true);
    private readonly Button _save = Ui.Button("Save Verified Candidates…", "Check");
    private List<WorkOption> _allWorks = new();
    private readonly HashSet<int> _selectedEditionIds = new();
    private readonly List<ResultRow> _results = new();
    private PassageResearchIdentity? _source;
    private GeminiCorpusInvestigationResult? _aiResult;
    private DateTime? _generatedUtc;
    private string? _scope;

    public CorpusInvestigatorWindow(IReaderHost? host, ResearchProject project, IntertextualAtlasConnection seed) : base(host)
    {
        _project = project;
        _seed = seed;
        Title = $"Corpus Investigator — {project.Name}";
        Width = 1250;
        Height = 800;
        MinWidth = 900;
        MinHeight = 620;

        var seedGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*"), RowDefinitions = new RowDefinitions("Auto,*"), Height = 150, Margin = new Thickness(16, 12, 16, 10) };
        var sourceLabel = Ui.Caption($"Reviewed source — {seed.SourceAuthorName}, {seed.SourceWorkTitle} [{PassageCitation.Display(seed.Investigation.SourceCitationRef, seed.Investigation.SourceMilestone)}]");
        var targetLabel = Ui.Caption($"Seed parallel — {seed.Result.TargetAuthorName}, {seed.Result.TargetWorkTitle} [{PassageCitation.Display(seed.Result.TargetCitationRef, seed.Result.TargetMilestone)}]");
        sourceLabel.FontWeight = targetLabel.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        sourceLabel.Margin = targetLabel.Margin = new Thickness(0, 0, 0, 4);
        var sourceBox = Ui.Passage(seed.Investigation.SourceText);
        var targetBox = Ui.Passage(seed.Result.TargetText);
        foreach (var (control, row, column) in new (Control, int, int)[] { (sourceLabel, 0, 0), (targetLabel, 0, 2), (sourceBox, 1, 0), (targetBox, 1, 2) })
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            seedGrid.Children.Add(control);
        }

        var seedBorder = new Border { Child = seedGrid, BorderThickness = new Thickness(0, 0, 0, 1) };
        seedBorder.Bind(Border.BackgroundProperty, seedBorder.GetResourceObservable("Cc.PageHeader"));
        seedBorder.Bind(Border.BorderBrushProperty, seedBorder.GetResourceObservable("Cc.Rule"));

        _works = Ui.Compact(new ListBox
        {
            ItemTemplate = new FuncDataTemplate<WorkOption>((option, _) =>
            {
                if (option == null) return new TextBlock();
                var box = new CheckBox { Content = option.Label, IsChecked = _selectedEditionIds.Contains(option.Edition.EditionId) };
                box.IsCheckedChanged += (_, _) =>
                {
                    if (box.IsChecked == true) _selectedEditionIds.Add(option.Edition.EditionId);
                    else _selectedEditionIds.Remove(option.Edition.EditionId);
                };
                return box;
            }, supportsRecycling: false)
        }, 1);

        var left = new DockPanel { Margin = new Thickness(16, 12, 12, 10) };
        var instructions = Ui.Muted("Choose up to eight original-language works. The model may suggest reinforcing parallels, counterexamples, and generic conventions; every returned passage key is checked against this exact local selection.");
        instructions.Margin = new Thickness(0, 0, 0, 10);
        var focusField = Ui.Field("Investigative focus (editable)", _focus, 10);
        _filter.Margin = new Thickness(0, 0, 0, 6);
        foreach (var top in new Control[] { instructions, focusField, _filter })
        {
            DockPanel.SetDock(top, Dock.Top);
            left.Children.Add(top);
        }

        var worksFrame = new Border { Classes = { "panel" }, Child = _works };
        left.Children.Add(worksFrame);

        _grid.Columns.Add(Ui.Column("Role", nameof(ResultRow.Role), 120));
        _grid.Columns.Add(Ui.Column("Confidence", nameof(ResultRow.Confidence), 96));
        _grid.Columns.Add(Ui.Column("Target", nameof(ResultRow.Target), 190));
        _grid.Columns.Add(Ui.Column("Citation", nameof(ResultRow.Citation), 90));
        _grid.Columns.Add(Ui.SourceColumn("Passage / assessment", nameof(ResultRow.Assessment)));
        _grid.Margin = new Thickness(12, 12, 16, 10);

        _run.Click += (_, _) => Run(RunAsync);
        _save.IsEnabled = false;
        _save.Click += (_, _) => Run(SaveAsync);
        var bar = Ui.ButtonBar(_status, _save, _run);

        var dock = new DockPanel();
        DockPanel.SetDock(seedBorder, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(seedBorder);
        dock.Children.Add(bar);
        dock.Children.Add(Ui.Split(left, _grid, 380, 280, 450));
        Content = dock;

        _focus.Text = BuildInitialFocus();

        // Debounced, like the other pickers of this size: a refill is four thousand
        // rows, and a word typed at speed was one per letter. The checks already made
        // live in the selection set, so they survive whichever refill finally runs.
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            RefreshWorks();
        };
        _filter.TextChanged += (_, _) =>
        {
            _filterDebounce.Stop();
            _filterDebounce.Start();
        };
        Closed += (_, _) => _filterDebounce.Stop();
        Opened += (_, _) => Run(LoadAsync);
    }

    internal int WorkCount => _allWorks.Count;
    internal IReadOnlyCollection<int> SelectedEditionIds => _selectedEditionIds;
    internal bool CanRun => _run.IsEnabled;
    internal string StatusText => _status.Text ?? string.Empty;

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

        var seedWork = _allWorks.FirstOrDefault(w => w.Edition.WorkId == _seed.Result.TargetWorkId);
        if (seedWork != null) _selectedEditionIds.Add(seedWork.Edition.EditionId);
        RefreshWorks();

        if (_source == null)
        {
            _run.IsEnabled = false;
            _status.Text = "The reviewed source passage no longer resolves in the local corpus.";
        }
    }

    private void RefreshWorks()
    {
        var filter = (_filter.Text ?? string.Empty).Trim();
        _works.ItemsSource = _allWorks
            .Where(w => filter.Length == 0 || w.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task RunAsync()
    {
        if (!await EnsureGeminiKeyAsync()) return;

        var selected = _allWorks.Where(w => _selectedEditionIds.Contains(w.Edition.EditionId)).ToList();
        if (selected.Count == 0)
        {
            await Alert.Info(this, "Corpus Investigator", "Choose at least one comparison work.");
            return;
        }

        if (selected.Count > MaxWorks)
        {
            await Alert.Info(this, "Corpus Investigator", $"Choose no more than {MaxWorks} works so each receives a meaningful share of the bounded prompt.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_focus.Text))
        {
            await Alert.Info(this, "Corpus Investigator", "Describe the pattern or question this investigation should test.");
            return;
        }

        if (!await ConfirmSendAsync("Send bounded corpus to Gemini?",
                $"This will send both reviewed passages and a bounded selection from {selected.Count} local work(s) to Gemini's API, along with the project name/notes, research questions, and investigative focus. Continue?"))
            return;

        _run.IsEnabled = false;
        _save.IsEnabled = false;
        _status.Text = "Building a balanced local corpus…";
        _results.Clear();
        _grid.ItemsSource = null;
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
            _aiResult = await GeminiTranslationService.InvestigateIntertextualCorpusAsync(context, _focus.Text!.Trim(),
                _seed.SourceAuthorName, _seed.SourceWorkTitle, _seed.Investigation.SourceCitationRef, _seed.Investigation.SourceText, _seed.Investigation.SourceLanguage,
                _seed.Result.TargetAuthorName, _seed.Result.TargetWorkTitle, _seed.Result.TargetCitationRef, _seed.Result.TargetText, _seed.Result.TargetLanguage,
                corpus.Scope, corpus.Hash, corpus.TaggedText, TranslationSettings.GeminiApiKey!);
            _generatedUtc = DateTime.UtcNow;

            var unresolved = 0;
            var returnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in _aiResult.Candidates)
            {
                if (!corpus.Map.TryGetValue(candidate.CandidateKey, out var passage))
                {
                    unresolved++;
                    continue;
                }

                if (!returnedKeys.Add(candidate.CandidateKey)) continue;
                _results.Add(new ResultRow(passage, candidate));
            }

            _grid.ItemsSource = _results.ToList();
            _save.IsEnabled = _results.Count > 0;
            _status.Text = $"{_results.Count} locally verified candidate(s)." +
                           (unresolved > 0 ? $" {unresolved} invented/invalid key(s) discarded." : string.Empty);
        }
        catch (Exception ex)
        {
            _status.Text = "Couldn't finish: " + ex.Message;
        }
        finally
        {
            _run.IsEnabled = _source != null;
        }
    }

    private async Task<CorpusBlock> BuildCorpusAsync(IReadOnlyList<WorkOption> selected)
    {
        var map = new Dictionary<string, CorpusPassage>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        var scopes = new List<string>();
        var keyNumber = 0;
        var perWork = MaxCorpusChars / selected.Count;
        foreach (var work in selected)
        {
            var used = 0;
            string? lastRef = null;
            var truncated = false;
            foreach (var node in await _nodes.GetByEditionAsync(work.Edition.EditionId, readingLinesOnly: true))
            {
                if (string.IsNullOrWhiteSpace(node.Text)) continue;
                if (node.TextNodeId == _seed.Result.TargetTextNodeId) continue; // the known seed is not a new candidate
                var key = $"P{keyNumber + 1:000000}";
                var line = $"[{key}] {work.Edition.AuthorName}, {work.Edition.WorkTitle} [{PassageCitation.Display(node.CitationRef, node.Milestone)}] ({work.Edition.Language ?? "unknown"}) {node.Text}\n";
                if (used + line.Length > perWork)
                {
                    truncated = true;
                    break;
                }

                keyNumber++;
                used += line.Length;
                lastRef = node.CitationRef;
                builder.Append(line);
                map[key] = new CorpusPassage(work, node);
            }

            scopes.Add($"{work.Edition.AuthorName}, {work.Edition.WorkTitle}: " +
                       (truncated ? $"bounded through [{lastRef}]" : "complete ingested reading text"));
        }

        var tagged = builder.ToString();
        return new CorpusBlock(tagged, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tagged))), string.Join("; ", scopes), map);
    }

    private async Task SaveAsync()
    {
        if (_source == null || _aiResult == null || _results.Count == 0) return;

        var request = new EchoCaptureRequest(ResearchEchoMethod.AiCorpusInvestigation, _source,
            $"Corpus investigation: {_focus.Text!.Trim()}", _scope,
            "Gemini expansion from a human-reviewed seed pair. Opaque candidate keys were resolved exactly against the bounded local corpus; role labels and rationales remain hypotheses for human review.",
            _aiResult.Model, _aiResult.PromptProvenance, _generatedUtc,
            _results.Select(r => new EchoCaptureCandidate(r.Passage.Work.Edition.WorkId, r.Passage.Node.TextNodeId,
                r.Passage.Work.Edition.AuthorName, r.Passage.Work.Edition.WorkTitle, r.Passage.Node.CitationRef,
                r.Passage.Node.Text, null, $"{r.Candidate.Role} · {r.Candidate.Confidence}",
                r.Candidate.Rationale + (string.IsNullOrWhiteSpace(r.Candidate.SuggestedMotifs) ? string.Empty : $" Motifs: {r.Candidate.SuggestedMotifs}"))).ToList());
        if (await ResearchFeature.CaptureEchoAsync(this, request, _project.ResearchProjectId))
            await Alert.Info(this, "Corpus Investigator", "The verified candidates and full Gemini prompt provenance are saved as a pending echo investigation.");
    }

    private sealed class WorkOption
    {
        public WorkOption((int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language) edition)
        {
            Edition = edition;
            Label = $"{edition.AuthorName} — {edition.WorkTitle} ({edition.Language?.ToUpperInvariant() ?? "?"})";
        }

        public (int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language) Edition { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    private sealed record CorpusPassage(WorkOption Work, TextNode Node);

    private sealed record CorpusBlock(string TaggedText, string Hash, string Scope, Dictionary<string, CorpusPassage> Map);

    private sealed class ResultRow
    {
        public ResultRow(CorpusPassage passage, CorpusInvestigationCandidate candidate)
        {
            Passage = passage;
            Candidate = candidate;
        }

        public CorpusPassage Passage { get; }
        public CorpusInvestigationCandidate Candidate { get; }
        public string Role => Candidate.Role;
        public string Confidence => Candidate.Confidence;
        public string Target => $"{Passage.Work.Edition.AuthorName}, {Passage.Work.Edition.WorkTitle}";
        public string Citation => PassageCitation.Display(Passage.Node.CitationRef, Passage.Node.Milestone);

        public string Assessment => $"{Passage.Node.Text} — {Candidate.Rationale}" +
                                    (string.IsNullOrWhiteSpace(Candidate.SuggestedMotifs) ? string.Empty : $" [Motifs: {Candidate.SuggestedMotifs}]");
    }
}
