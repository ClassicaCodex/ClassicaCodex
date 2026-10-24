using Avalonia.Controls;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Features.Research.Audit;
using ClassicaCodex.Mac.Features.Research.Bibliography;
using ClassicaCodex.Mac.Features.Research.Claims;
using ClassicaCodex.Mac.Features.Research.Echoes;
using ClassicaCodex.Mac.Features.Research.Hypotheses;
using ClassicaCodex.Mac.Features.Research.Log;
using ClassicaCodex.Mac.Features.Research.Reading;
using ClassicaCodex.Mac.Features.Research.Snapshots;
using ClassicaCodex.Mac.Features.Research.Sources;
using ClassicaCodex.Mac.Features.Research.Synthesis;
using ClassicaCodex.Mac.Features.Stylometry;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// The Project menu: the windows that work on the open project, its status,
/// its audit and its log.
///
/// On Windows each of these is a modal dialog the Bench waits on, and it
/// refreshes its evidence when the dialog returns. Here they are windows of
/// their own, owned by the Bench, so a researcher can keep the reading queue
/// open beside the claims matrix; the Bench refreshes when one of them closes,
/// which is the moment the Windows Bench refreshed too.
/// </summary>
internal sealed partial class ResearchBenchWindow
{
    private MenuFlyout BuildProjectMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Ui.Item("Scholarly Claims Matrix…", "Quotes", () => Run(() => OpenScholarlyClaimsAsync())));
        menu.Items.Add(Ui.Item("Import RIS / BibTeX Bibliography…", "Download", () => Run(OpenBibliographyImportAsync)));
        menu.Items.Add(Ui.Item("Bibliography & Zotero Export…", "Upload", () => Run(OpenBibliographyAsync)));
        menu.Items.Add(Ui.Item("Corpus Snapshots…", "Camera", () => Run(OpenCorpusSnapshotsAsync)));
        menu.Items.Add(Ui.Item("Reading Queue & Passage Notebook…", "ListBullets", () => Run(OpenReadingQueueAsync)));
        menu.Items.Add(Ui.Item("Echo Investigations…", "Echo", () => Run(OpenEchoInvestigationsAsync)));
        menu.Items.Add(Ui.Item("Intertextual Atlas…", "Atlas", () => Run(OpenIntertextualAtlasAsync)));
        menu.Items.Add(Ui.Item("Hypothesis Lab…", "Lightbulb", () => Run(OpenHypothesisLabAsync)));
        menu.Items.Add(Ui.Item("Synthesis & Findings…", "Seal", () => Run(OpenSynthesisAsync)));
        menu.Items.Add(new Separator());

        // Archive has a button of its own because it is the common case. On hold and
        // concluded need somewhere to live too - they used to be set from the header
        // drop-down, which is now the list filter.
        var status = new MenuItem { Header = "Set Project Status", Icon = Common.Glyphs.Icon("Flag", 14) };
        foreach (var value in Enum.GetValues<ResearchProjectStatus>())
        {
            var captured = value;
            status.Items.Add(Ui.Item(StatusFilter.Title(value), null, () => Run(() => SetProjectStatusAsync(captured))));
        }

        menu.Items.Add(status);
        menu.Items.Add(Ui.Item("Project Audit…", "Validation", () => Run(OpenProjectAuditAsync)));
        menu.Items.Add(Ui.Item("Research Log…", "Clock", () => Run(OpenResearchLogAsync)));
        return menu;
    }

    private async Task SuggestProjectAsync()
    {
        var window = new AiProjectSuggestionWindow(Host, _work, _authorName);
        await window.ShowDialog(this);
        if (window.CreatedProjectId is not { } id) return;

        await LoadProjectsAsync(id);
        _statusLine.Text = "Created the selected AI-proposed project with reviewable questions, hypotheses, experiments, and reading leads.";
    }

    private async Task OpenResearchLogAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;
        ResearchWindows.Show($"log:{project.ResearchProjectId}", this, () => new ResearchLogWindow(project));
    }

    internal async Task<ScholarlyClaimsWindow?> OpenScholarlyClaimsAsync(long claimId = 0)
    {
        if (await RequireProjectAsync() is not { } project) return null;

        var (window, created) = ResearchWindows.Show($"claims:{project.ResearchProjectId}", this, () => new ScholarlyClaimsWindow(project, claimId));
        if (!created && claimId != 0) await window.ReloadAsync(claimId);
        return window;
    }

    private async Task OpenBibliographyImportAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;

        var import = new BibliographyImportWindow(project);
        await import.ShowDialog(this);
        if (import.ImportedCount > 0)
        {
            await RefreshIfOpenAsync(project);
            _statusLine.Text = $"Imported {import.ImportedCount} scholarship source(s); review remains uncertain.";
        }
    }

    private async Task OpenBibliographyAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;
        ResearchWindows.Show($"bibliography:{project.ResearchProjectId}", this, () => new ResearchBibliographyWindow(project));
    }

    private async Task OpenCorpusSnapshotsAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;
        ResearchWindows.Show($"snapshots:{project.ResearchProjectId}", this, () => new CorpusSnapshotsWindow(project));
    }

    private async Task OpenProjectAuditAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;

        var questions = await _repo.GetQuestionsAsync(project.ResearchProjectId);
        var evidence = await _repo.GetEvidenceAsync(project.ResearchProjectId);
        var claims = await _repo.GetScholarlyClaimsAsync(project.ResearchProjectId);
        var audit = new ResearchAuditWindow(project, questions, evidence, claims);
        await audit.ShowDialog(this);
        await FollowAuditAsync(audit);
    }

    /// <summary>Goes to what the audit's "Open item" chose: a claim, then evidence, then a question.</summary>
    private async Task FollowAuditAsync(ResearchAuditWindow audit)
    {
        if (!audit.ItemChosen) return;

        if (audit.SelectedClaimId is long claimId)
        {
            await OpenScholarlyClaimsAsync(claimId);
            return;
        }

        if (audit.SelectedEvidenceId is long evidenceId)
        {
            RevealEvidence(evidenceId);
            return;
        }

        if (audit.SelectedQuestionId is long questionId) SelectQuestion(questionId);
    }

    private void OpenAttachedStylometryRun()
    {
        if (_openAnalysis.Tag is not long runId || Host == null) return;
        StylometryFeature.OpenCompareRuns(Host, runId);
    }

    private void OpenEvidenceSources()
    {
        if (_editingEvidence is not { EvidenceItemId: > 0 } evidence) return;
        ResearchWindows.Show($"sources:{evidence.EvidenceItemId}", this, () => new EvidenceSourcesWindow(evidence));
    }

    internal async Task OpenReadingQueueAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;

        var (window, created) = ResearchWindows.Show($"reading:{project.ResearchProjectId}", this, () => new ReadingQueueWindow(Host, project, _work));
        if (!created) return;

        window.Closed += (_, _) => Run(async () =>
        {
            if (window.PromotedEvidenceItemId is long evidenceId && CurrentProject?.ResearchProjectId == project.ResearchProjectId)
                await LoadEvidenceAsync(project.ResearchProjectId, evidenceId);
        });
    }

    private async Task OpenSynthesisAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;
        ResearchWindows.Show($"synthesis:{project.ResearchProjectId}", this, () => new SynthesisWindow(project, _work, _authorName));
    }

    private async Task OpenHypothesisLabAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;

        var (window, created) = ResearchWindows.Show($"hypotheses:{project.ResearchProjectId}", this, () => new HypothesisLabWindow(Host, project, _work, _authorName));
        if (created) window.Closed += (_, _) => Run(() => RefreshIfOpenAsync(project));
    }

    private async Task OpenEchoInvestigationsAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;

        var (window, created) = ResearchWindows.Show($"echoes:{project.ResearchProjectId}", this, () => new EchoInvestigationsWindow(Host, project, _work, _authorName));
        if (created) window.Closed += (_, _) => Run(() => RefreshIfOpenAsync(project));
    }

    private async Task OpenIntertextualAtlasAsync()
    {
        if (await RequireProjectAsync() is not { } project) return;
        ResearchWindows.Show($"atlas:{project.ResearchProjectId}", this, () => new IntertextualAtlasWindow(Host, project));
    }

    /// <summary>
    /// After a window that can add evidence: show the evidence as it now stands.
    /// Promoting an echo, or a reading from the queue a Hypothesis Lab experiment
    /// opens, saved the record - and the table behind it went on showing the list
    /// from before, as if nothing had been kept, until another project was opened
    /// and this one reopened.
    /// </summary>
    private async Task RefreshIfOpenAsync(ResearchProject project)
    {
        if (!IsVisible) return;
        if (CurrentProject?.ResearchProjectId == project.ResearchProjectId)
            await LoadEvidenceAsync(project.ResearchProjectId, CurrentEvidence?.EvidenceItemId ?? 0);
    }

    /// <summary>Puts the Bench on a project from outside - a promoted inquiry naming the one it made.</summary>
    internal Task ShowProjectAsync(long projectId) => LoadProjectsAsync(projectId);
}
