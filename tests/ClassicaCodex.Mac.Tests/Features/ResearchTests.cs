using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.Research;
using ClassicaCodex.Mac.Features.Research.Audit;
using ClassicaCodex.Mac.Features.Research.Bench;
using ClassicaCodex.Mac.Features.Research.Bibliography;
using ClassicaCodex.Mac.Features.Research.Claims;
using ClassicaCodex.Mac.Features.Research.Echoes;
using ClassicaCodex.Mac.Features.Research.Hypotheses;
using ClassicaCodex.Mac.Features.Research.Inquiry;
using ClassicaCodex.Mac.Features.Research.Log;
using ClassicaCodex.Mac.Features.Research.Reading;
using ClassicaCodex.Mac.Features.Research.Snapshots;
using ClassicaCodex.Mac.Features.Research.Sources;
using ClassicaCodex.Mac.Features.Research.Synthesis;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Shell;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The research windows write to the library, and the connection factory they
/// write through is one static for the whole process, so these run on their
/// own rather than beside tests that point it somewhere else.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ResearchCollection : ICollectionFixture<ResearchLibrary>
{
    public const string Name = "Research";
}

/// <summary>
/// One copy of the sample library for the research tests to write to, and a
/// folder for the files they write, both removed when the tests are done.
/// </summary>
public sealed class ResearchLibrary : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "classicacodex-research-tests", Guid.NewGuid().ToString("N"));

    private string? _copy;

    /// <summary>The copy, made on first use; null when there is no sample library.</summary>
    public string? Copy()
    {
        if (Screens.SampleLibrary is not { } library) return null;
        if (_copy != null) return _copy;

        Directory.CreateDirectory(Folder);
        _copy = Path.Combine(Folder, "library.db");
        File.Copy(library, _copy);
        File.SetAttributes(_copy, FileAttributes.Normal);
        return _copy;
    }

    public string PathFor(string name)
    {
        Directory.CreateDirectory(Folder);
        return Path.Combine(Folder, name);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder in the temporary directory; the system clears it in time.
        }
    }
}

/// <summary>
/// The research area driven end to end against a copy of the sample library:
/// projects, questions in the order the researcher gave them, evidence, the
/// claims matrix, passage inquiries and their mark, echo captures through to
/// promoted evidence, and every window photographed in both appearances.
/// </summary>
[Collection(ResearchCollection.Name)]
public class ResearchTests
{
    private readonly ResearchLibrary _library;

    public ResearchTests(ResearchLibrary library) => _library = library;

    /// <summary>
    /// The main window open on the research copy of the sample library - these
    /// tests write to it - or null when there is no sample library.
    /// </summary>
    private async Task<MainWindow?> OpenAsync()
    {
        if (_library.Copy() is not { } copy) return null;

        DbConnectionFactory.Configure(copy, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(8);
        return window;
    }

    private static Work Work(MainWindow main, string title) => main.LibraryWorks.First(w => w.Title.Contains(title));

    private static async Task<(Edition Edition, List<TextNode> Nodes)> OriginalAsync(Work work)
    {
        var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
        var edition = editions.First(e => e.Kind == EditionKind.Original);
        return (edition, await new TextNodeRepository().GetByEditionAsync(edition.EditionId, readingLinesOnly: true));
    }

    private static async Task<ResearchProject> ProjectAsync(Work work, string name)
    {
        var project = new ResearchProject { WorkId = work.WorkId, WorkCtsUrn = work.CtsUrn, Name = name };
        await new ResearchRepository().SaveProjectAsync(project);
        return project;
    }

    /// <summary>The newest window <paramref name="owner"/> owns that <paramref name="match"/> accepts, waiting for it to open.</summary>
    private static async Task<T> WaitForAsync<T>(Window owner, Func<T, bool>? match = null) where T : Window
    {
        for (var i = 0; i < 80; i++)
        {
            await Screens.Settle(1);
            var found = owner.OwnedWindows.OfType<T>().LastOrDefault(w => w.IsVisible && (match?.Invoke(w) ?? true));
            if (found != null)
            {
                await Screens.Settle(3);
                return found;
            }
        }

        throw new InvalidOperationException($"No {typeof(T).Name} opened over {owner.Title}.");
    }

    private static string? ButtonText(Button button) => button.Content switch
    {
        string text => text,
        StackPanel row => row.Children.OfType<TextBlock>().FirstOrDefault()?.Text,
        _ => null
    };

    private static void Click(Window window, string text)
    {
        var button = window.GetVisualDescendants().OfType<Button>().First(b => ButtonText(b) == text);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>Answers the one-line prompt open over <paramref name="owner"/>.</summary>
    private static async Task AnswerAsync(Window owner, string text)
    {
        var prompt = await WaitForAsync<Window>(owner, w => w.GetType() == typeof(Window) && w.GetVisualDescendants().OfType<TextBox>().Any());
        prompt.GetVisualDescendants().OfType<TextBox>().First().Text = text;
        Click(prompt, "OK");
        await Screens.Settle(3);
    }

    /// <summary>Presses <paramref name="button"/> on the alert open over <paramref name="owner"/>.</summary>
    private static async Task PressAsync(Window owner, string button)
    {
        // Alerts and prompts are plain windows; the research windows are not.
        var alert = await WaitForAsync<Window>(owner, w => w.GetType() == typeof(Window) && w.GetVisualDescendants().OfType<Button>().Any(b => ButtonText(b) == button));
        Click(alert, button);
        await Screens.Settle(3);
    }

    /// <summary>Takes a window's photograph in the light and the dark appearance.</summary>
    private static async Task PhotographAsync(Window window, string name)
    {
        await Screens.Settle(4);
        Screens.Save(window, name + "-light");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        try
        {
            await Screens.Settle(4);
            Screens.Save(window, name + "-dark");
        }
        finally
        {
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Screens.Settle(2);
        }

        // And at the smallest size the window allows, where nothing may be lost.
        var (width, height) = (window.Width, window.Height);
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        await Screens.Settle(4);
        Screens.Save(window, name + "-smallest");
        window.Width = width;
        window.Height = height;
        await Screens.Settle(2);
    }

    [AvaloniaFact]
    public async Task The_bench_keeps_a_project_its_questions_in_the_order_given_and_its_evidence()
    {
        if (await OpenAsync() is not { } main) return;

        var work = Work(main, "Agamemnon");
        Assert.True(await main.OpenWorkAsync(work.WorkId));
        ResearchFeature.OpenBench(main, work, main.AuthorNameFor(work));
        var bench = await WaitForAsync<ResearchBenchWindow>(main);

        // A project from a working theory, as New Project… makes one.
        var creating = bench.NewProjectAsync();
        await AnswerAsync(bench, "The watchman's speech frames the whole trilogy");
        await creating;
        Assert.Equal("The watchman's speech frames the whole trilogy", bench.OpenProject?.Name);
        var projectId = bench.OpenProject!.ResearchProjectId;

        foreach (var text in new[] { "Who is the watchman waiting for?", "What does the beacon announce?", "Why does he fall silent?" })
        {
            var adding = bench.AddQuestionAsync();
            await AnswerAsync(bench, text);
            await adding;
        }

        Assert.Equal(3, bench.Questions.Count);
        var (a, b, c) = (bench.Questions[0], bench.Questions[1], bench.Questions[2]);

        // Move the third above the second, then remove the first. Before 3.15.1 the
        // renumbering after a delete counted rows it had already rewritten, and the two
        // just reordered came back tied, in their old order.
        bench.SelectQuestion(c.ResearchQuestionId);
        await bench.MoveQuestionAsync(-1);
        Assert.Equal(new[] { a.Text, c.Text, b.Text }, bench.Questions.Select(q => q.Text));

        bench.SelectQuestion(a.ResearchQuestionId);
        var removing = bench.RemoveQuestionAsync();
        await PressAsync(bench, "Remove");
        await removing;

        var stored = await new ResearchRepository().GetQuestionsAsync(projectId);
        Assert.Equal(new[] { c.Text, b.Text }, stored.Select(q => q.Text));
        Assert.Equal(stored.Count, stored.Select(q => q.SortOrder).Distinct().Count());
        Assert.Equal(new[] { c.Text, b.Text }, bench.Questions.Select(q => q.Text));

        // Evidence, written in the inspector and saved.
        var (_, nodes) = await OriginalAsync(work);
        bench.StartNewEvidence();
        bench.EditEvidence("The watchman's opening lines", nodes[0].Text, "Sets up waiting as the trilogy's first mood.", EvidenceJudgment.Accepted);
        await bench.SaveEvidenceAsync();
        bench.StartNewEvidence();
        bench.EditEvidence("The beacon speech", nodes[Math.Min(20, nodes.Count - 1)].Text);
        await bench.SaveEvidenceAsync();

        var evidence = await new ResearchRepository().GetEvidenceAsync(projectId);
        Assert.Equal(new[] { "The watchman's opening lines", "The beacon speech" }, evidence.Select(e => e.Title));
        Assert.Equal(EvidenceJudgment.Accepted, evidence[0].Judgment);
        Assert.Equal("The beacon speech", bench.EditingEvidence?.Title);
        Assert.Contains("Evidence saved", bench.StatusText);

        // The theory and notes, edited in the header.
        bench.SetTheory("The watchman's speech frames the trilogy", "Scope: the prologue only, for now.");
        await bench.SaveProjectAsync();
        Assert.Equal("Scope: the prologue only, for now.", (await new ResearchRepository().GetProjectsForWorkAsync(work.WorkId)).Single(p => p.ResearchProjectId == projectId).Notes);

        await PhotographAsync(bench, "research-bench");


        // Archive keeps the project, and Restore brings it back.
        var archiving = bench.SetProjectStatusAsync(ResearchProjectStatus.Archived);
        await archiving;
        Assert.Equal(ResearchProjectStatus.Archived, bench.OpenProject?.Status);
        Assert.Contains("archived", bench.StatusText);
        await bench.SetProjectStatusAsync(ResearchProjectStatus.Active);
        Assert.Equal(ResearchProjectStatus.Active, bench.OpenProject?.Status);

        main.Close();
    }

    [AvaloniaFact]
    public async Task The_claims_matrix_edits_and_saves_the_claim_that_is_highlighted()
    {
        if (await OpenAsync() is not { } main) return;

        var work = Work(main, "Persians");
        var project = await ProjectAsync(work, "How Persian is the Persians?");
        var repo = new ResearchRepository();
        foreach (var (who, what) in new[] { ("Hall", "The play constructs the barbarian."), ("Garvie", "Darius is idealised."), ("Pelling", "Xerxes is pitied, not mocked.") })
            await repo.SaveScholarlyClaimAsync(new ScholarlyClaim { ResearchProjectId = project.ResearchProjectId, Claimant = who, ClaimText = what });
        var claims = await repo.GetScholarlyClaimsAsync(project.ResearchProjectId);

        // Opened on the third, as the audit's "Open item" opens it: the editor must
        // show the highlighted claim, not the first one.
        var matrix = new ScholarlyClaimsWindow(project, claims[2].ScholarlyClaimId);
        matrix.Show(main);
        await Screens.Settle(8);
        Assert.Equal(claims[2].ScholarlyClaimId, matrix.Highlighted?.ScholarlyClaimId);
        Assert.Equal(claims[2].ScholarlyClaimId, matrix.Editing?.ScholarlyClaimId);
        Assert.Equal("Pelling", matrix.EditorClaimant);

        // Choose the second, change it, save: the second is what changes.
        matrix.Highlight(claims[1].ScholarlyClaimId);
        await Screens.Settle(2);
        Assert.Equal("Garvie", matrix.EditorClaimant);
        matrix.Draft("Garvie", "Darius is idealised, and the ghost scene makes the point.", "pp. 27–30");
        await matrix.SaveAsync();

        var after = await repo.GetScholarlyClaimsAsync(project.ResearchProjectId);
        Assert.Equal("The play constructs the barbarian.", after.Single(c => c.ScholarlyClaimId == claims[0].ScholarlyClaimId).ClaimText);
        Assert.Equal("Darius is idealised, and the ghost scene makes the point.", after.Single(c => c.ScholarlyClaimId == claims[1].ScholarlyClaimId).ClaimText);
        Assert.Equal(claims[1].ScholarlyClaimId, matrix.Highlighted?.ScholarlyClaimId);
        Assert.Equal(claims[1].ScholarlyClaimId, matrix.Editing?.ScholarlyClaimId);

        // A new claim saved is the one left highlighted and in the editor.
        matrix.StartNewClaim();
        matrix.Draft("Rosenbloom", "The play is about Athens.");
        await matrix.SaveAsync();
        var created = (await repo.GetScholarlyClaimsAsync(project.ResearchProjectId)).Single(c => c.Claimant == "Rosenbloom");
        Assert.Equal(created.ScholarlyClaimId, matrix.Highlighted?.ScholarlyClaimId);
        Assert.Equal(created.ScholarlyClaimId, matrix.Editing?.ScholarlyClaimId);

        await PhotographAsync(matrix, "research-claims");

        // The audit's Open item, followed from the Bench, lands on that claim too.
        var audit = new ResearchAuditWindow(project, await repo.GetQuestionsAsync(project.ResearchProjectId), await repo.GetEvidenceAsync(project.ResearchProjectId), after);
        audit.Show(main);
        await Screens.Settle(4);
        await PhotographAsync(audit, "research-audit");
        var finding = audit.Report.Findings.FirstOrDefault(f => f.ScholarlyClaimId != null);
        Assert.NotNull(finding);
        audit.Select(finding!);
        audit.OpenSelected();
        Assert.True(audit.ItemChosen);
        Assert.Equal(finding!.ScholarlyClaimId, audit.SelectedClaimId);

        main.Close();
    }

    [AvaloniaFact]
    public async Task An_inquiry_saved_without_promotion_marks_the_passage_and_promotion_opens_the_bench()
    {
        if (await OpenAsync() is not { } main) return;

        var work = Work(main, "Prometheus");
        Assert.True(await main.OpenWorkAsync(work.WorkId));
        await Screens.Settle(6);
        var (edition, nodes) = await OriginalAsync(work);

        // Saved, then closed: an inquiry without a project. The window closes the way
        // an abandoned one does, and the passage is marked all the same.
        var first = nodes[3];
        var starting = ResearchFeature.StartInquiryAsync(main, new PassageContext(first, edition, null, true, "Palatino", null, null));
        var inquiry = await WaitForAsync<PassageInquiryWindow>(main);
        Assert.False(inquiry.PromotionOffered);
        Assert.False(inquiry.AiPanelVisible);
        inquiry.Write("Kratos speaks before anyone else does.", "Why does power get the first word?");
        inquiry.SetDirection(PassageInquiryDirection.Research);
        Assert.True(inquiry.AiPanelVisible);
        Assert.True(await inquiry.SaveAsync());
        Assert.True(inquiry.PromotionOffered);
        await PhotographAsync(inquiry, "research-inquiry");
        inquiry.Close();
        Assert.True(await starting);

        var saved = await new PassageInquiryRepository().GetAsync(edition.CtsUrn, first.CitationRef);
        Assert.NotNull(saved);
        Assert.Null(saved!.ResearchProjectId);
        var marks = await new PassageMarkRepository().GetForEditionAsync(edition.EditionId, edition.CtsUrn);
        Assert.True(marks.TryGetValue(first.CitationRef, out var mark) && mark.HasFlag(PassageMarks.Inquiry));

        // Abandoned without a word written: no inquiry, no mark.
        var second = nodes[7];
        starting = ResearchFeature.StartInquiryAsync(main, new PassageContext(second, edition, null, true, "Palatino", null, null));
        inquiry = await WaitForAsync<PassageInquiryWindow>(main);
        inquiry.Close();
        Assert.False(await starting);
        Assert.Null(await new PassageInquiryRepository().GetAsync(edition.CtsUrn, second.CitationRef));

        // Edited and not saved: closing asks first, and keeping on editing keeps it open.
        var third = nodes[11];
        starting = ResearchFeature.StartInquiryAsync(main, new PassageContext(third, edition, null, true, "Palatino", null, null));
        inquiry = await WaitForAsync<PassageInquiryWindow>(main);
        inquiry.Write("Hephaestus pities what he is told to do.", "Is the craftsman complicit???");
        Assert.True(inquiry.IsDirty);
        inquiry.Close();
        await PressAsync(inquiry, "Keep Editing");
        Assert.True(inquiry.IsVisible);

        // Saved and promoted: a project, its question, the passage as evidence, and the
        // Bench opened on it.
        Assert.True(await inquiry.SaveAsync());
        await inquiry.PromoteOrOpenAsync();
        Assert.True(await starting);

        var linked = await new PassageInquiryRepository().GetAsync(edition.CtsUrn, third.CitationRef);
        Assert.NotNull(linked?.ResearchProjectId);
        var bench = await WaitForAsync<ResearchBenchWindow>(main);
        await Screens.Settle(6);
        Assert.Equal(linked!.ResearchProjectId, bench.OpenProject?.ResearchProjectId);
        Assert.Equal("Is the craftsman complicit", bench.OpenProject?.Name);
        Assert.Single(bench.Questions);
        var evidence = Assert.Single(bench.Evidence);
        Assert.Equal($"{edition.CtsUrn}:{third.CitationRef}", evidence.StableIdentifier);
        Assert.Equal("Hephaestus pities what he is told to do.", evidence.ResearcherNote);

        main.Close();
    }

    [AvaloniaFact]
    public async Task An_echo_capture_is_saved_reviewed_and_promoted_to_evidence()
    {
        if (await OpenAsync() is not { } main) return;

        var source = Work(main, "Eumenides");
        var target = Work(main, "Odyssey");
        var project = await ProjectAsync(source, "Orestes at Athens and Orestes in Homer");
        var (_, sourceNodes) = await OriginalAsync(source);
        var (_, targetNodes) = await OriginalAsync(target);
        var identity = await new TextNodeRepository().GetPassageResearchIdentityAsync(sourceNodes[0].TextNodeId);
        Assert.NotNull(identity);

        var candidates = targetNodes.Skip(28).Take(3).Select((n, i) => new EchoCaptureCandidate(
            target.WorkId, n.TextNodeId, main.AuthorNameFor(target), target.Title, n.CitationRef, n.Text,
            0.9 - i * 0.1, null, i == 0 ? "Orestes' revenge held up as a model." : null)).ToList();
        var request = new EchoCaptureRequest(ResearchEchoMethod.RareWordOverlap, identity!, "Orestes in the Odyssey",
            "Homer, Odyssey", "Rare-word overlap, 3 candidates", null, null, null, candidates);

        var capturing = ResearchFeature.CaptureEchoAsync(main, request, project.ResearchProjectId);
        var capture = await WaitForAsync<EchoCaptureWindow>(main);
        await Screens.Settle(4);
        Assert.Equal(project.ResearchProjectId, capture.SelectedProject?.ResearchProjectId);
        Assert.Equal(3, capture.Candidates.Checked.Count);
        capture.Candidates.SetChecked(candidates[2], false);
        await PhotographAsync(capture, "research-echo-capture");
        await capture.SaveAsync();
        Assert.True(await capturing);

        var echoes = new ResearchEchoRepository();
        var investigation = Assert.Single(await echoes.GetInvestigationsAsync(project.ResearchProjectId));
        Assert.Equal(2, (await echoes.GetResultsAsync(investigation.ResearchEchoInvestigationId)).Count);

        // Reviewed in the inbox: accepted, then promoted to paired evidence.
        var inbox = new EchoInvestigationsWindow(main, project, source, main.AuthorNameFor(source));
        inbox.Show(main);
        await Screens.Settle(8);
        Assert.Equal(2, inbox.Results.Count);

        // Promotion before acceptance is refused, with the reason.
        var refusing = inbox.PromoteAsync();
        await PressAsync(inbox, "OK");
        await refusing;
        Assert.Empty(await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId));

        inbox.Review(ResearchEchoDisposition.Accepted, "The Odyssey's Orestes is the model Athena's court has to answer.");
        await inbox.SaveReviewAsync();
        var promoting = inbox.PromoteAsync();
        await PressAsync(inbox, "OK");
        await promoting;
        var evidence = Assert.Single(await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId));
        Assert.StartsWith("Echo candidate:", evidence.Title);
        Assert.Equal(EvidenceOrigin.ClassicaCodexAnalysis, evidence.Origin);
        await PhotographAsync(inbox, "research-echo-investigations");

        // Classified in the studio, with motifs, and seen on the atlas.
        var result = inbox.Results.First(r => r.Disposition == ResearchEchoDisposition.Accepted);
        var studio = new ParallelStudioWindow(main, project, source, main.AuthorNameFor(source), investigation, result);
        studio.Show(main);
        await Screens.Settle(6);
        studio.Classify(ResearchEchoConnectionType.Narrative, ResearchEchoDirectionality.Unknown, "revenge, homecoming", "Both tell the same story to different ends.");
        var saving = studio.SaveClassificationAsync();
        await PressAsync(studio, "OK");
        await saving;
        await PhotographAsync(studio, "research-parallel-studio");
        studio.Close();

        var atlas = new IntertextualAtlasWindow(main, project);
        atlas.Show(main);
        await Screens.Settle(8);
        Assert.Single(atlas.Filtered);
        Assert.Equal(2, atlas.Graph.NodeCount);
        atlas.SetFilters(ResearchEchoDisposition.Accepted, motifs: true);
        await Screens.Settle(4);
        Assert.Equal(4, atlas.Graph.NodeCount);
        atlas.Graph.Click("Motif: revenge");
        Assert.Equal(1, atlas.ShownCount);
        await PhotographAsync(atlas, "research-atlas");

        var investigator = new CorpusInvestigatorWindow(main, project, atlas.Filtered[0]);
        investigator.Show(main);
        await Screens.Settle(10);
        Assert.True(investigator.WorkCount > 0);
        Assert.NotEmpty(investigator.SelectedEditionIds);
        await PhotographAsync(investigator, "research-corpus-investigator");

        // Studio, atlas and investigator jump the reader themselves.
        main.Close();
    }

    [AvaloniaFact]
    public async Task The_reading_queue_promotes_only_reviewed_readings_and_the_bench_sees_the_result()
    {
        if (await OpenAsync() is not { } main) return;

        var work = Work(main, "Aeneid");
        var project = await ProjectAsync(work, "Dido's curse and the Punic Wars");
        ResearchFeature.OpenBench(main, work, main.AuthorNameFor(work), project.ResearchProjectId);
        var bench = await WaitForAsync<ResearchBenchWindow>(main);
        Assert.Equal(project.ResearchProjectId, bench.OpenProject?.ResearchProjectId);

        var (edition, nodes) = await OriginalAsync(work);
        await bench.OpenReadingQueueAsync();
        var queue = await WaitForAsync<ReadingQueueWindow>(bench);
        await queue.AddPassageAsync(edition, nodes[40]);
        Assert.Single(queue.Items);
        Assert.Equal(ResearchReadingKind.CorpusPassage, queue.Editing?.Kind);

        // Not yet reviewed: refused, with the reason, and nothing is made.
        var refusing = queue.PromoteAsync();
        await PressAsync(queue, "OK");
        await refusing;
        Assert.Empty(await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId));

        queue.Edit(ResearchReadingStatus.Reviewed, "The curse is the war, told as prophecy.");
        var promoting = queue.PromoteAsync();
        await PressAsync(queue, "OK");
        await promoting;
        var evidence = Assert.Single(await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId));
        Assert.Equal(evidence.EvidenceItemId, queue.PromotedEvidenceItemId);
        Assert.Equal("The curse is the war, told as prophecy.", evidence.ResearcherNote);
        await PhotographAsync(queue, "research-reading-queue");

        // Open source goes to the passage in the reader, beside the queue.
        await queue.OpenSourceAsync();
        await Screens.Settle(6);
        Assert.Equal(work.WorkId, main.OpenWork?.WorkId);

        // The passage picker it offers.
        var picker = new PassagePickerWindow(work);
        picker.Show(queue);
        await Screens.Settle(8);
        Assert.True(picker.ShownCount > 100);
        picker.Filter(nodes[40].CitationRef);
        await Screens.Settle(2);
        Assert.True(picker.ShownCount >= 1);
        await PhotographAsync(picker, "research-passage-picker");
        picker.Close();

        queue.Close();
        await Screens.Settle(6);
        Assert.Single(bench.Evidence);
        main.Close();
    }

    [AvaloniaFact]
    public async Task Hypotheses_findings_and_the_dossier_are_kept_with_the_project()
    {
        if (await OpenAsync() is not { } main) return;

        var work = Work(main, "Symposium");
        var project = await ProjectAsync(work, "Is Diotima Socrates' invention?");
        var repo = new ResearchRepository();
        await repo.SaveQuestionAsync(new ResearchQuestion { ResearchProjectId = project.ResearchProjectId, Text = "Does any source outside Plato know her?" });
        var evidenceId = await repo.SaveEvidenceAsync(new EvidenceItem { ResearchProjectId = project.ResearchProjectId, Title = "Symposium 201d", Type = EvidenceType.PrimaryText, CanonicalReference = "201d" });

        var lab = new HypothesisLabWindow(main, project, work, main.AuthorNameFor(work));
        lab.Show(main);
        await Screens.Settle(8);
        lab.DraftHypothesis("Invention", "Diotima is a literary device of Socrates' own.");
        await lab.SaveHypothesisAsync();
        lab.DraftHypothesis("Historical priestess", "A Mantinean priestess Socrates really knew.");
        await lab.SaveHypothesisAsync();
        Assert.Equal(2, lab.Hypotheses.Count);
        Assert.Single(lab.Sources);

        lab.Assess(0, HypothesisRelationship.DoesNotDiscriminate, HypothesisStrength.Moderate, "Fits both.");
        await lab.SaveAssessmentsAsync();
        lab.DraftExperiment("Search for Diotima outside Plato", ResearchExperimentMethod.Bibliography, "No independent attestation.", "An independent Mantinean source.");
        await lab.SaveExperimentAsync();
        Assert.Single(lab.Experiments);
        foreach (var (tab, name) in new[] { (0, "overview"), (1, "hypothesis"), (2, "assess"), (3, "experiments") })
        {
            lab.ShowTab(tab);
            await PhotographAsync(lab, $"research-hypothesis-lab-{name}");
        }

        var synthesis = new SynthesisWindow(project, work, main.AuthorNameFor(work));
        synthesis.Show(main);
        await Screens.Settle(8);
        synthesis.StartNewFinding();
        synthesis.Draft("No outside witness", "Nothing outside Plato attests Diotima.", "Provisional, pending the bibliography.");
        synthesis.Link(evidenceId, EvidenceRelationship.Supports);
        Assert.True(await synthesis.SaveAsync());
        var finding = Assert.Single(synthesis.Findings);
        var link = Assert.Single(await new ResearchFindingRepository().GetLinksAsync(finding.ResearchFindingId));
        Assert.Equal(EvidenceRelationship.Supports, link.Relationship);
        await PhotographAsync(synthesis, "research-synthesis");
        synthesis.ShowTab(1);
        await PhotographAsync(synthesis, "research-synthesis-links");
        synthesis.ShowTab(2);
        await PhotographAsync(synthesis, "research-synthesis-ai");
        synthesis.ShowTab(0);

        var dossier = _library.PathFor("dossier.md");
        Assert.True(await synthesis.ExportDossierToAsync(dossier));
        var markdown = await File.ReadAllTextAsync(dossier);
        Assert.Contains("Is Diotima Socrates' invention?", markdown);
        Assert.Contains("No outside witness", markdown);

        // Every one of those changes is in the log, with the dossier export last.
        var log = new ResearchLogWindow(project);
        log.Show(main);
        await Screens.Settle(6);
        Assert.Contains(log.Entries, e => e.Kind == ResearchLogEntryKind.ResearchDossierExported);
        log.Draft("Read Halperin on Diotima next.", "Before deciding the finding.");
        await log.AddNoteAsync();
        Assert.Contains(log.Entries, e => e.Summary == "Read Halperin on Diotima next.");
        await PhotographAsync(log, "research-log");

        main.Close();
    }

    [AvaloniaFact]
    public async Task Bibliography_imports_once_exports_and_snapshots_and_sources_are_fingerprinted()
    {
        if (await OpenAsync() is not { } main) return;

        var work = Work(main, "Phaedo");
        var project = await ProjectAsync(work, "The last argument for immortality");
        var bib = _library.PathFor("phaedo.bib");
        await File.WriteAllTextAsync(bib, """
            @article{frede1978,
              author = {Frede, Dorothea},
              title = {The Final Proof of the Immortality of the Soul in Plato's Phaedo 102a-107a},
              journal = {Phronesis},
              year = {1978},
              volume = {23},
              pages = {27--41},
              doi = {10.1163/156852878X00028}
            }
            @book{gallop1975,
              author = {Gallop, David},
              title = {Plato: Phaedo},
              publisher = {Clarendon Press},
              year = {1975}
            }
            """);

        var import = new BibliographyImportWindow(project);
        import.Show(main);
        await Screens.Settle(6);
        await import.LoadFileAsync(bib);
        Assert.Equal(2, import.Records.Checked.Count);
        await import.ImportSelectedAsync();
        Assert.Equal(2, import.ImportedCount);
        await PhotographAsync(import, "research-bibliography-import");

        // The same file again: both are duplicates now, and none can be chosen.
        await import.LoadFileAsync(bib);
        Assert.Empty(import.Records.Checked);
        import.Close();

        var evidence = await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId);
        Assert.Equal(2, evidence.Count);
        Assert.All(evidence, e => Assert.Equal(EvidenceType.Scholarship, e.Type));

        var bibliography = new ResearchBibliographyWindow(project);
        bibliography.Show(main);
        await Screens.Settle(6);
        Assert.Equal(2, bibliography.Records.Count);
        Assert.True(await bibliography.SaveKeysAsync(showConfirmation: true));
        await PhotographAsync(bibliography, "research-bibliography");
        bibliography.Close();

        var snapshots = new CorpusSnapshotsWindow(project);
        snapshots.Show(main);
        await Screens.Settle(6);
        await snapshots.CaptureAsync();
        for (var i = 0; i < 100 && snapshots.IsWorking; i++) await Screens.Settle(1);
        Assert.Single(snapshots.Snapshots);
        Assert.StartsWith("Captured", snapshots.StatusText);
        await PhotographAsync(snapshots, "research-corpus-snapshots");
        snapshots.Close();

        var pdf = _library.PathFor("frede-1978.pdf");
        await File.WriteAllTextAsync(pdf, "%PDF-1.4 a stand-in for the article");
        var sources = new EvidenceSourcesWindow(evidence[0]);
        sources.Show(main);
        await Screens.Settle(6);
        await sources.AttachFileAsync(pdf);
        Assert.Single(sources.Attachments);
        sources.DraftNote(3, "the soul is deathless and indestructible", "The conclusion the final proof claims.");
        await sources.SaveAnnotationAsync();
        Assert.Single(await new ResearchSourceRepository().GetAnnotationsAsync(sources.Attachments[0].EvidenceAttachmentId));
        await PhotographAsync(sources, "research-evidence-sources");

        main.Close();
    }

    [AvaloniaFact]
    public async Task Without_a_key_the_AI_surfaces_open_the_AI_settings_and_send_nothing()
    {
        if (await OpenAsync() is not { } main) return;
        if (!string.IsNullOrWhiteSpace(ClassicaCodex.Mac.Platform.TranslationSettings.GeminiApiKey)) return;

        var work = Work(main, "Crito");
        var project = await ProjectAsync(work, "Does Socrates argue or obey?");
        ResearchFeature.OpenBench(main, work, main.AuthorNameFor(work), project.ResearchProjectId);
        var bench = await WaitForAsync<ResearchBenchWindow>(main);

        var suggest = new AiProjectSuggestionWindow(main, work, main.AuthorNameFor(work));
        suggest.Show(bench);
        await Screens.Settle(4);
        await PhotographAsync(suggest, "research-ai-suggest");
        var generating = suggest.GenerateAsync();
        var settings = await WaitForAsync<SettingsWindow>(suggest);
        Assert.Equal("AI Settings", settings.Title);
        settings.Close();
        await generating;
        Assert.Null(suggest.CreatedProjectId);
        suggest.Close();

        Assert.Empty(await new ResearchRepository().GetEvidenceAsync(project.ResearchProjectId));
        main.Close();
    }

    [AvaloniaFact]
    public async Task The_review_windows_save_only_what_is_checked()
    {
        if (await OpenAsync() is not { } main) return;

        var candidates = new[]
        {
            new EvidenceCandidatePreview("The watchman", "1", "urn:test:1", "θεοὺς μὲν αἰτῶ τῶνδ᾽ ἀπαλλαγὴν πόνων", EvidenceRelationship.Supports, null, "high", "Opens the waiting."),
            new EvidenceCandidatePreview("The beacon", "22", "urn:test:22", "ὦ χαῖρε λαμπτήρ", EvidenceRelationship.Contradicts, null, "medium", "Joy before the fall.")
        };
        var review = new EvidenceReviewWindow(candidates, "gemini-test", 1, challengeTheory: false);
        review.Show(main);
        await Screens.Settle(4);
        Assert.Empty(review.Table.Checked);
        review.Table.SetChecked(candidates[1], true);
        await PhotographAsync(review, "research-evidence-review");
        Click(review, "Save Checked");
        await Screens.Settle(2);
        Assert.Equal(new[] { candidates[1] }, review.Accepted);

        var challenge = new HypothesisChallengeReviewWindow(new GeminiHypothesisChallengeResult("gemini-test", "prompt", new[]
        {
            new HypothesisChallengeProposal("rivalHypothesis", "A later interpolation", "The beacon speech is post-Aeschylean.", "Its diction is unusual.", "", "", ""),
            new HypothesisChallengeProposal("experiment", "Count rare words", "Compare the beacon speech's vocabulary.", "Discriminates the rivals.", "Stylometry", "Ordinary rates.", "A cluster of late forms.")
        }));
        challenge.Show(main);
        await Screens.Settle(4);
        await PhotographAsync(challenge, "research-challenge-review");
        Click(challenge, "Cancel");
        await Screens.Settle(2);
        Assert.Null(challenge.Accepted);

        main.Close();
    }

    [Fact]
    public void AI_evidence_is_checked_against_the_passages_that_were_sent()
    {
        var nodes = Enumerable.Range(1, 40).Select(i => new TextNode { TextNodeId = i, CitationRef = i.ToString(), Text = new string('α', 30) + i }).ToList();

        // A budget that holds about half the edition.
        var (text, truncatedAt, sent) = EvidenceGathering.BuildTaggedCorpus(nodes, maxCharacters: 800);
        Assert.NotNull(truncatedAt);
        Assert.InRange(sent.Count, 10, 30);
        Assert.Equal(sent[^1].CitationRef, truncatedAt);
        Assert.Contains("[1] ", text);
        Assert.DoesNotContain($"[{nodes[^1].CitationRef}] ", text);

        var beyond = nodes[sent.Count + 2].CitationRef;
        var candidates = new[]
        {
            new ResearchEvidenceCandidate("[3]", "Within what was sent", 1, "supports", "high", "A real line."),
            new ResearchEvidenceCandidate(beyond, "Past the cut", null, "supports", "high", "Recalled from training."),
            new ResearchEvidenceCandidate("999", "Invented", null, "supports", "low", "Not a line at all."),
            new ResearchEvidenceCandidate("5", "Already in the project", null, "nonsense", "low", "Saved before.")
        };
        var existing = new[] { new EvidenceItem { StableIdentifier = "urn:cts:greekLit:tlg0085.tlg005:5" } };
        var questions = new List<ResearchQuestion> { new() { ResearchQuestionId = 77, Text = "Q" } };

        var (offered, unresolved) = EvidenceGathering.Resolve(candidates, sent, "urn:cts:greekLit:tlg0085.tlg005", existing, questions, challengeTheory: false);

        // The line past the cut is rejected as unresolved, as an invented one is,
        // rather than being saved as verified against the edition.
        var only = Assert.Single(offered);
        Assert.Equal("3", only.Citation);
        Assert.Equal(nodes[2].Text, only.Excerpt);
        Assert.Equal(77, only.QuestionId);
        Assert.Equal(EvidenceRelationship.Supports, only.Relationship);
        Assert.Equal(2, unresolved);
    }

    [Fact]
    public void A_reading_opens_its_web_address_or_its_DOI()
    {
        Assert.True(ResearchLinks.TryGetOpenUri("https://example.org/a", null, out var web));
        Assert.Equal("https://example.org/a", web.AbsoluteUri);
        Assert.True(ResearchLinks.TryGetOpenUri("doi:10.1163/156852878X00028", null, out var doi));
        Assert.Equal("https://doi.org/10.1163/156852878X00028", doi.AbsoluteUri);
        Assert.True(ResearchLinks.TryGetOpenUri("isbn:123", "http://example.org/b", out var locator));
        Assert.Equal("http://example.org/b", locator.AbsoluteUri);
        Assert.False(ResearchLinks.TryGetOpenUri("urn:cts:latinLit:phi0690.phi003", "1.1", out _));
    }

    [Fact]
    public void The_status_filter_admits_what_it_says()
    {
        Assert.False(StatusFilter.Current.Admits(ResearchProjectStatus.Archived));
        Assert.True(StatusFilter.Current.Admits(ResearchProjectStatus.OnHold));
        Assert.True(StatusFilter.Everything.Admits(ResearchProjectStatus.Archived));
        Assert.Equal("on hold", StatusFilter.Describe(ResearchProjectStatus.OnHold));
        Assert.Equal("Concluded", StatusFilter.Title(ResearchProjectStatus.Concluded));
        Assert.Equal("Primary text", Words.Of(EvidenceType.PrimaryText));
        Assert.Equal("App analysis", Words.Of(EvidenceOrigin.ClassicaCodexAnalysis));
        Assert.Equal("Does not discriminate", Words.Of(HypothesisRelationship.DoesNotDiscriminate));
    }
}
