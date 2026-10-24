using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// "Let AI Suggest a New Project": Gemini proposes projects for this work from a
/// bounded sample of its text and, if asked, verified Crossref metadata. Every
/// reading lead and passage a proposal cites is checked against what was
/// actually retrieved or sent before it is shown, and nothing becomes a project
/// until the researcher picks one.
/// </summary>
internal sealed class AiProjectSuggestionWindow : ResearchWindow
{
    private const int MaxCorpusChars = 90_000;

    private readonly Work _work;
    private readonly string _author;
    private readonly ResearchRepository _research = new();
    private readonly EditionRepository _editions = new();
    private readonly TextNodeRepository _nodes = new();
    private readonly TextBox _query = Ui.Line();
    private readonly CheckBox _crossref = new() { Content = "Include verified Crossref scholarly metadata leads", IsChecked = true };
    private readonly Button _generate = Ui.Button("Suggest Projects with Gemini", "Sparkle", accent: true);
    private readonly ListBox _suggestions;
    private readonly TextBox _details = Ui.Area(double.NaN, readOnly: true);
    private readonly TextBlock _status = Ui.Status();
    private readonly Button _create = Ui.Button("Create Selected Project", "Plus");

    private GeminiProjectSuggestionsResult? _result;
    private List<ScholarlyReadingLead> _leads = new();
    private readonly Dictionary<string, (TextNode Node, Edition Edition)> _passages = new(StringComparer.OrdinalIgnoreCase);

    public AiProjectSuggestionWindow(IReaderHost? host, Work work, string author) : base(host)
    {
        _work = work;
        _author = author;
        Title = $"Let AI Suggest a New Project — {work.Title}";
        Width = 1250;
        Height = 780;
        MinWidth = 900;
        MinHeight = 600;

        _query.Text = $"{author} {work.Title} classical philology authorship interpretation";
        _generate.Click += (_, _) => Run(GenerateAsync);
        _create.IsEnabled = false;
        _create.Click += (_, _) => Run(CreateAsync);
        _suggestions = Ui.TextList<AiResearchProjectSuggestion>(s => $"[{s.Category}] {s.Title}");
        _suggestions.SelectionChanged += (_, _) => ShowSuggestion();

        var queryRow = new DockPanel();
        DockPanel.SetDock(_generate, Dock.Right);
        _generate.Margin = new Thickness(10, 0, 0, 0);
        queryRow.Children.Add(_generate);
        queryRow.Children.Add(_query);

        var top = new StackPanel { Margin = new Thickness(18, 14, 18, 12), Spacing = 8 };
        top.Children.Add(Ui.Field("Scholarly search terms (editable)", queryRow, 0));
        top.Children.Add(_crossref);
        top.Children.Add(Ui.Caption("Crossref receives only these search terms. Gemini receives a bounded sample of the work, its attribution note, the titles of this work's existing projects, and any Crossref metadata retrieved."));

        var detailPane = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        detailPane.Children.Add(_details);
        _suggestions.Margin = new Thickness(8);
        var split = Ui.Split(_suggestions, detailPane, 360, 280, 520);

        var bar = Ui.ButtonBar(_status, _create);
        var topBorder = new Border { Child = top, BorderThickness = new Thickness(0, 0, 0, 1) };
        topBorder.Bind(Border.BorderBrushProperty, topBorder.GetResourceObservable("Cc.Rule"));
        topBorder.Bind(Border.BackgroundProperty, topBorder.GetResourceObservable("Cc.PageHeader"));

        var dock = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(topBorder);
        dock.Children.Add(bar);
        dock.Children.Add(split);
        Content = dock;
    }

    /// <summary>The project made from the chosen proposal, if one was.</summary>
    public long? CreatedProjectId { get; private set; }

    internal async Task GenerateAsync()
    {
        if (!await EnsureGeminiKeyAsync()) return;
        if (!await ConfirmSendAsync("Discover research projects?",
                "This will send a bounded sample of the selected work, its attribution note, existing project titles, and any retrieved Crossref metadata to Gemini. Crossref receives only the editable scholarly search terms. Continue?"))
            return;

        SetBusy(true);
        _result = null;
        _suggestions.ItemsSource = null;
        _details.Text = string.Empty;
        _create.IsEnabled = false;
        try
        {
            _status.Text = _crossref.IsChecked == true ? "Retrieving scholarly metadata from Crossref…" : "Building local corpus sample…";
            string? crossrefWarning = null;
            if (_crossref.IsChecked == true)
            {
                try
                {
                    _leads = (await CrossrefDiscoveryService.SearchAsync(_query.Text ?? string.Empty)).ToList();
                }
                catch (Exception ex)
                {
                    _leads = new List<ScholarlyReadingLead>();
                    crossrefWarning = " Crossref was unavailable, so these proposals use only the local corpus: " + ex.Message;
                }
            }
            else
            {
                _leads = new List<ScholarlyReadingLead>();
            }

            var corpus = await BuildCorpusSampleAsync();
            var projects = await _research.GetProjectsForWorkAsync(_work.WorkId, workCtsUrn: _work.CtsUrn);
            _status.Text = "Gemini is proposing grounded projects…";
            var attribution = $"Attribution: {_work.AttributionStatus}; note: {_work.AttributionNote ?? "none"}";
            _result = await GeminiTranslationService.SuggestResearchProjectsAsync(
                $"{_author}, {_work.Title}; CTS {_work.CtsUrn}; {attribution}",
                projects.Count == 0 ? "(none)" : string.Join("\n", projects.Select(p => "- " + p.Name)),
                corpus, _leads, TranslationSettings.GeminiApiKey!);

            // Only what exists here: a reading lead Crossref returned, a passage key
            // that was in the sample. Anything else in a proposal is dropped.
            var leadKeys = _leads.Select(l => l.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var passageKeys = _passages.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var verified = _result.Suggestions.Select(s => s with
            {
                ReadingLeadKeys = s.ReadingLeadKeys.Where(leadKeys.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                PassageKeys = s.PassageKeys.Where(passageKeys.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            }).ToList();
            _result = _result with { Suggestions = verified };
            _suggestions.ItemsSource = verified;
            if (verified.Count > 0) _suggestions.SelectedIndex = 0;
            _create.IsEnabled = verified.Count > 0;
            _status.Text = $"{verified.Count} proposal(s) · {_leads.Count} verified publication lead(s) · {_passages.Count} locally keyed passage(s)." + crossrefWarning;
        }
        catch (Exception ex)
        {
            _status.Text = "Could not generate suggestions: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<string> BuildCorpusSampleAsync()
    {
        _passages.Clear();
        var editions = await _editions.GetByWorkAsync(_work.WorkId);
        var edition = editions.FirstOrDefault(e => e.Kind == EditionKind.Original
                                                   && (e.Orthography == null || e.Orthography.Equals("normalised", StringComparison.OrdinalIgnoreCase)))
                      ?? editions.FirstOrDefault(e => e.Kind == EditionKind.Original);
        if (edition == null) return "(No original-language edition is ingested.)";

        var text = new StringBuilder();
        var n = 0;
        foreach (var node in await _nodes.GetByEditionAsync(edition.EditionId, true))
        {
            if (string.IsNullOrWhiteSpace(node.Text)) continue;
            var key = $"P{n + 1:00000}";
            var line = $"[{key}] [{PassageCitation.Display(node.CitationRef, node.Milestone)}] {node.Text}\n";
            if (text.Length + line.Length > MaxCorpusChars) break;
            n++;
            text.Append(line);
            _passages[key] = (node, edition);
        }

        return text.ToString();
    }

    private void ShowSuggestion()
    {
        if (_suggestions.SelectedItem is not AiResearchProjectSuggestion s)
        {
            _details.Text = string.Empty;
            return;
        }

        var nl = Environment.NewLine;
        _details.Text =
            $"{s.Category}{nl}{nl}CENTRAL QUESTION{nl}{s.CentralQuestion}{nl}{nl}WHY IT MAY MATTER{nl}{s.Rationale}{nl}{nl}" +
            $"GROUNDING{nl}{s.Grounding}{nl}{nl}QUESTIONS{nl}- {string.Join(nl + "- ", s.ResearchQuestions)}{nl}{nl}" +
            $"RIVAL HYPOTHESES{nl}- {string.Join(nl + "- ", s.Hypotheses.Select(h => $"{h.Title}: {h.Statement}"))}{nl}{nl}" +
            $"EXPERIMENTS{nl}- {string.Join(nl + "- ", s.Experiments.Select(e => $"{e.Title} [{e.Method}] — falsified by: {e.FalsificationCriterion}"))}{nl}{nl}" +
            $"VERIFIED READING LEADS{nl}- {string.Join(nl + "- ", s.ReadingLeadKeys.Select(k => _leads.First(l => l.Key.Equals(k, StringComparison.OrdinalIgnoreCase))).Select(l => $"{l.Title} — DOI {l.Doi}"))}{nl}{nl}" +
            $"LOCAL PASSAGES QUEUED{nl}{string.Join(", ", s.PassageKeys)}";
    }

    private async Task CreateAsync()
    {
        if (_result == null || _suggestions.SelectedItem is not AiResearchProjectSuggestion s) return;

        _create.IsEnabled = false;
        ResearchProject? project = null;
        try
        {
            project = new ResearchProject
            {
                WorkId = _work.WorkId,
                WorkCtsUrn = _work.CtsUrn,
                Name = s.Title,
                Notes = $"AI-proposed project ({s.Category}); human-selected.\r\n\r\n{s.Rationale}\r\n\r\nGrounding claimed by proposal: {s.Grounding}"
            };
            await _research.SaveProjectAsync(project);

            var questions = new[] { s.CentralQuestion }.Concat(s.ResearchQuestions)
                .Where(q => !string.IsNullOrWhiteSpace(q)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < questions.Count; i++)
            {
                await _research.SaveQuestionAsync(new ResearchQuestion
                {
                    ResearchProjectId = project.ResearchProjectId,
                    Text = questions[i],
                    SortOrder = i,
                    Origin = ResearchQuestionOrigin.AiProposed,
                    AiModel = _result.Model,
                    AiPrompt = _result.PromptProvenance,
                    AiGeneratedUtc = DateTime.UtcNow
                });
            }

            var hypotheses = new ResearchHypothesisRepository();
            for (var i = 0; i < s.Hypotheses.Count; i++)
            {
                await hypotheses.SaveHypothesisAsync(new ResearchHypothesis
                {
                    ResearchProjectId = project.ResearchProjectId,
                    Title = s.Hypotheses[i].Title,
                    Statement = s.Hypotheses[i].Statement,
                    Origin = EvidenceOrigin.AiCandidate,
                    AiModel = _result.Model,
                    AiPrompt = _result.PromptProvenance,
                    AiGeneratedUtc = DateTime.UtcNow,
                    SortOrder = i
                });
            }

            for (var i = 0; i < s.Experiments.Count; i++)
            {
                var e = s.Experiments[i];
                await hypotheses.SaveExperimentAsync(new ResearchExperiment
                {
                    ResearchProjectId = project.ResearchProjectId,
                    Title = e.Title,
                    Method = Enum.TryParse<ResearchExperimentMethod>(e.Method, true, out var m) ? m : ResearchExperimentMethod.Manual,
                    PredictedOutcome = Ui.Clean(e.PredictedOutcome),
                    FalsificationCriterion = Ui.Clean(e.FalsificationCriterion),
                    Origin = EvidenceOrigin.AiCandidate,
                    AiModel = _result.Model,
                    AiPrompt = _result.PromptProvenance,
                    AiGeneratedUtc = DateTime.UtcNow,
                    SortOrder = i
                });
            }

            var queue = new ResearchReadingQueueRepository();
            var sort = 0;
            foreach (var key in s.ReadingLeadKeys)
            {
                var l = _leads.First(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                await queue.SaveAsync(new ResearchReadingItem
                {
                    ResearchProjectId = project.ResearchProjectId,
                    Kind = ResearchReadingKind.ExternalSource,
                    Title = l.Title,
                    Purpose = "Verify relevance and read before treating any argument as a scholarly claim.",
                    StableIdentifier = "doi:" + l.Doi,
                    Locator = l.Url,
                    Notes = $"Verified Crossref metadata lead; content not reviewed. {string.Join("; ", l.Authors)} ({l.Year ?? "n.d."}), {l.ContainerTitle ?? l.Publisher}.",
                    SortOrder = sort++
                });
            }

            foreach (var key in s.PassageKeys)
            {
                var p = _passages[key];
                await queue.SaveAsync(new ResearchReadingItem
                {
                    ResearchProjectId = project.ResearchProjectId,
                    Kind = ResearchReadingKind.CorpusPassage,
                    Title = $"{_work.Title} {PassageCitation.Display(p.Node.CitationRef, p.Node.Milestone)}",
                    Purpose = "Inspect the locally grounded passage that motivated this proposed project.",
                    WorkCtsUrn = _work.CtsUrn,
                    EditionCtsUrn = p.Edition.CtsUrn,
                    CitationRef = p.Node.CitationRef,
                    Quotation = p.Node.Text,
                    Notes = "Selected by an AI proposal; passage identity and text were resolved locally.",
                    SortOrder = sort++
                });
            }

            CreatedProjectId = project.ResearchProjectId;
            Close();
        }
        catch (Exception ex)
        {
            var rollback = " The incomplete project was removed.";
            if (project?.ResearchProjectId is > 0)
            {
                try
                {
                    await _research.DeleteIncompleteProjectAsync(project.ResearchProjectId);
                }
                catch (Exception cleanup)
                {
                    rollback = " Cleanup also failed, so inspect the project list for a partial project: " + cleanup.Message;
                }
            }
            else
            {
                rollback = " No project was created.";
            }

            _status.Text = "Could not finish creating the project: " + ex.Message + rollback;
            _create.IsEnabled = true;
        }
    }
}
