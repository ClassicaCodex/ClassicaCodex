using Avalonia.Controls;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// The Gather Evidence menu: a saved stylometry run copied in as analysis
/// evidence, and the two Gemini searches of the local edition - for passages
/// that bear on the theory, and for passages that challenge it.
/// </summary>
internal sealed partial class ResearchBenchWindow
{
    private MenuFlyout BuildGatherMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Ui.Item("Attach Saved Stylometry Run…", "Stylometry", () => Run(AttachStylometryRunAsync)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("AI: Find Relevant Corpus Passages…", "Sparkle", () => Run(() => GatherCorpusEvidenceAsync(false))));
        menu.Items.Add(Ui.Item("AI: Challenge the Working Theory…", "Sparkle", () => Run(() => GatherCorpusEvidenceAsync(true))));
        return menu;
    }

    private async Task AttachStylometryRunAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;

        var runRepo = new StylometryRunRepository();
        var runs = (await runRepo.GetAllRunsAsync()).Where(r => r.TargetWorkId == _work.WorkId).ToList();
        if (runs.Count == 0)
        {
            await Alert.Info(this, "Attach saved stylometry run", "There are no saved stylometry runs targeting this work yet.");
            return;
        }

        var picker = new StylometryRunPickerWindow(runs);
        await picker.ShowDialog(this);
        if (picker.SelectedRun is not { } run) return;

        var stableId = ResearchLinks.StylometryRunId(run.RunId);
        var existing = await _repo.GetEvidenceAsync(project.ResearchProjectId);
        if (existing.Any(e => string.Equals(e.StableIdentifier, stableId, StringComparison.OrdinalIgnoreCase)))
        {
            await Alert.Info(this, "Attach saved stylometry run", "That saved run is already attached to this project.");
            return;
        }

        var metrics = (await runRepo.GetRunMetricsAsync()).FirstOrDefault(m => m.RunId == run.RunId);
        var neighbors = await runRepo.GetNeighborsAsync(run.RunId, 12);
        var features = (await runRepo.GetFeaturesAsync(run.RunId)).Take(20).ToList();
        var resultLines = new List<string>
        {
            $"Target: {run.TargetAuthorName}, {run.TargetWorkTitle}",
            $"Nearest neighbour: {metrics?.NearestAuthor ?? "unknown"}, {metrics?.NearestTitle ?? "unknown"} (Δ {metrics?.DeltaFloor.ToString("0.0000") ?? "n/a"})",
            $"Depth to first outsider: {metrics?.DepthToFirstOutsider?.ToString() ?? "none in pool"}",
            $"Author purity among top 10: {(metrics == null ? "n/a" : metrics.AuthorPurityAt10.ToString("P1"))}",
            string.Empty,
            "Nearest works:"
        };
        resultLines.AddRange(neighbors.Select(n => $"{n.Rank}. {n.AuthorName}, {n.WorkTitle} — Δ {n.Delta:0.0000}"));
        resultLines.Add(string.Empty);
        resultLines.Add("Most frequent features:");
        resultLines.AddRange(features.Select(f => $"{f.Rank}. {f.Word} — {f.RelativeFrequency:P4}"));

        var item = new EvidenceItem
        {
            ResearchProjectId = project.ResearchProjectId,
            ResearchQuestionId = (_questions.SelectedItem as ResearchQuestion)?.ResearchQuestionId,
            Title = string.IsNullOrWhiteSpace(run.Label) ? $"Saved stylometry run #{run.RunId}" : $"Stylometry: {run.Label}",
            Type = EvidenceType.Stylometric,
            SourceType = "ClassicaCodex saved stylometry run",
            StableIdentifier = stableId,
            CanonicalReference = _work.CtsUrn,
            Provenance = $"Saved {run.CreatedUtc:O}; {run.Settings.Describe()}; pool {run.PoolSize:N0}; " +
                         $"target tokens {metrics?.TargetTokenCount?.ToString("N0") ?? "unknown"}. Exact run ID {run.RunId}.",
            Excerpt = string.Join(Environment.NewLine, resultLines),
            Judgment = EvidenceJudgment.Uncertain,
            Relationship = EvidenceRelationship.Contextualizes,
            Origin = EvidenceOrigin.ClassicaCodexAnalysis,
            SortOrder = _evidenceItems.Count
        };
        await _repo.SaveEvidenceAsync(item);
        await LoadEvidenceAsync(project.ResearchProjectId, item.EvidenceItemId);
        _statusLine.Text = $"Attached saved stylometry run #{run.RunId} as uncertain analysis evidence.";
    }

    private async Task GatherCorpusEvidenceAsync(bool challengeTheory)
    {
        if (await RequireProjectAsync() is not { } project) return;
        if (!await EnsureGeminiKeyAsync()) return;

        var editions = await new EditionRepository().GetByWorkAsync(_work.WorkId);
        var edition = editions.FirstOrDefault(e => e.Kind == EditionKind.Original
                                                   && (string.IsNullOrWhiteSpace(e.Orthography) || e.Orthography == "normalised"))
                      ?? editions.FirstOrDefault(e => e.Kind == EditionKind.Original);
        if (edition == null)
        {
            await Alert.Info(this, "Gather evidence", "This work has no original-language edition to search.");
            return;
        }

        var allNodes = await new TextNodeRepository().GetByEditionAsync(edition.EditionId, readingLinesOnly: true);
        var (taggedCorpus, truncatedAtRef, sent) = EvidenceGathering.BuildTaggedCorpus(allNodes);
        if (string.IsNullOrWhiteSpace(taggedCorpus))
        {
            await Alert.Info(this, "Gather evidence", "The selected edition contains no searchable reading text.");
            return;
        }

        var action = challengeTheory ? "challenge the working theory" : "find relevant passages";
        if (!await ConfirmSendAsync("Send research context to Gemini?",
                $"This will send the project theory, research questions, saved-evidence titles, and " +
                $"{taggedCorpus.Length:N0} characters from the local edition of {_work.Title} to Gemini " +
                $"to {action}. Returned citations will be checked against this edition before anything is saved."))
            return;

        var questions = await _repo.GetQuestionsAsync(project.ResearchProjectId);
        var existing = await _repo.GetEvidenceAsync(project.ResearchProjectId);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(taggedCorpus))).ToLowerInvariant();
        _statusLine.Text = challengeTheory
            ? "Gemini is looking for counterevidence and rival explanations…"
            : "Gemini is looking for relevant passages in the local corpus…";
        SetBusy(true);
        try
        {
            var generatedAt = DateTime.UtcNow;
            var theoryContext = project.Name
                                + (string.IsNullOrWhiteSpace(project.Notes) ? string.Empty : $"\nProject notes: {project.Notes}")
                                + $"\nLibrary attribution status: {_work.AttributionStatus}"
                                + (string.IsNullOrWhiteSpace(_work.AttributionNote) ? string.Empty : $" — {_work.AttributionNote}");
            var result = await GeminiTranslationService.FindResearchEvidenceAsync(
                theoryContext,
                questions.Select(q => q.Text).ToList(),
                existing.Select(e => $"{e.Title} [{e.CanonicalReference ?? e.StableIdentifier ?? "no ref"}]").ToList(),
                _authorName, _work.Title, edition.Language, edition.CtsUrn, hash, truncatedAtRef,
                taggedCorpus, challengeTheory, TranslationSettings.GeminiApiKey!);

            var (offered, unresolved) = EvidenceGathering.Resolve(
                result.Candidates, sent, edition.CtsUrn, existing, questions, challengeTheory);
            if (offered.Count == 0)
            {
                _statusLine.Text = $"Gemini returned no new verified candidates ({unresolved} unresolved citation(s)).";
                return;
            }

            // Nothing is written until the researcher accepts it. This was the only AI
            // surface in the Bench that wrote straight into the evidence register and the
            // append-only research log - no cap and no accept step - while the Hypothesis
            // Lab, the Corpus Investigator, the echo investigations and the synthesis all
            // require a human to choose. The prompt asks for at most 12 candidates and
            // nothing enforces it; the review is where that stops mattering, because an
            // over-long list is now something the researcher sees rather than something
            // that silently lands in the register.
            SetBusy(false); // the review is the researcher's turn, not the model's
            var review = new EvidenceReviewWindow(offered, result.Model, unresolved, challengeTheory);
            await review.ShowDialog(this);
            if (review.Accepted is not { } accepted)
            {
                _statusLine.Text = $"Discarded {offered.Count} AI candidate(s). Nothing was saved.";
                return;
            }

            var added = 0;
            foreach (var chosen in accepted)
            {
                var item = new EvidenceItem
                {
                    ResearchProjectId = project.ResearchProjectId,
                    ResearchQuestionId = chosen.QuestionId,
                    Title = $"AI candidate: {chosen.Title}",
                    Type = EvidenceType.PrimaryText,
                    SourceType = "Local corpus passage; Gemini candidate",
                    StableIdentifier = chosen.StableId,
                    CanonicalReference = chosen.Citation,
                    Provenance = $"Verified against local edition {edition.CtsUrn}; corpus SHA-256 {hash}; " +
                                 $"Gemini model {result.Model}; generated {generatedAt:O}; relevance confidence {chosen.Confidence}. " +
                                 (truncatedAtRef == null ? "Complete edition searched." : $"Search truncated after {truncatedAtRef}.") +
                                 " Accepted for review by the researcher.",
                    Excerpt = chosen.Excerpt,
                    Judgment = EvidenceJudgment.Uncertain,
                    Relationship = chosen.Relationship,
                    Origin = EvidenceOrigin.AiCandidate,
                    Interpretation = chosen.Rationale,
                    InterpretationAuthor = $"Gemini ({result.Model})",
                    GeneratorPrompt = result.PromptProvenance,
                    GeneratedUtc = generatedAt,
                    SortOrder = _evidenceItems.Count + added
                };
                await _repo.SaveEvidenceAsync(item);
                added++;
            }

            await LoadEvidenceAsync(project.ResearchProjectId);
            _statusLine.Text = added == 0
                ? $"None of the {offered.Count} candidate(s) were accepted. Nothing was saved."
                : $"Saved {added} of {offered.Count} candidate(s) as uncertain AI evidence; " +
                  $"{offered.Count - added} declined, {unresolved} unresolved citation(s) rejected.";
        }
        catch (Exception ex)
        {
            _statusLine.Text = $"AI evidence gathering did not finish: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }
}
