using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Model replies that are right in substance and loose in form. Each of these used
/// to throw away every candidate in the reply - one list where the prompt described a
/// string was enough - and the researcher saw "wasn't valid JSON" for an answer that
/// was perfectly readable.
/// </summary>
public class GeminiReplyShapeTests
{
    [Fact]
    public void AListWhereTheParallelAnalysisAskedForTextIsReadAsText()
    {
        var analysis = GeminiTranslationService.ParseParallelAnalysis("""
            {"summary":"Close.","sharedFeatures":["night","watch-fires"],
             "verificationTasks":["check the scholia","compare Il. 10"],"suggestedMotifs":["night","watch"]}
            """, "model", "prompt");

        Assert.Equal("Close.", analysis.Summary);
        Assert.Equal("night, watch-fires", analysis.SharedFeatures);
        Assert.Equal("check the scholia, compare Il. 10", analysis.VerificationTasks);
        Assert.Equal("night, watch", analysis.SuggestedMotifs);
    }

    [Fact]
    public void ACorpusCandidateWithListedMotifsIsKept()
    {
        var parsed = GeminiTranslationService.ParseCorpusInvestigationCandidates("""
            [{"candidateKey":"P000012","role":"parallel","suggestedMotifs":["night","recognition"]}]
            """);

        Assert.Equal("night, recognition", Assert.Single(parsed).SuggestedMotifs);
    }

    [Fact]
    public void NumbersMayArriveAsTextAndTextAsNumbers()
    {
        var parsed = GeminiTranslationService.ParseResearchEvidenceCandidates("""
            [{"citationRef":12,"questionIndex":"2","confidence":0.8}]
            """);

        var candidate = Assert.Single(parsed);
        Assert.Equal("12", candidate.CitationRef);
        Assert.Equal(2, candidate.QuestionIndex);
        Assert.Equal("0.8", candidate.Confidence);
    }

    [Fact]
    public void AFencedReplyWithASentenceEitherSideIsRead()
    {
        var parsed = GeminiTranslationService.ParseResearchEvidenceCandidates("""
            Here are the passages I found:

            ```json
            [{"citationRef":"1.1","rationale":"Opening."}]
            ```

            Let me know if you need more.
            """);

        Assert.Equal("1.1", Assert.Single(parsed).CitationRef);
    }

    [Fact]
    public void AReplyWrappedInAnObjectIsReadThrough()
    {
        var parsed = GeminiTranslationService.ParseHypothesisChallengeProposals("""
            {"proposals":[{"kind":"experiment","title":"Meter","statement":"Count resolutions.","method":["Stylometry"]}]}
            """);

        var proposal = Assert.Single(parsed);
        Assert.Equal("Meter", proposal.Title);
        Assert.Equal("Stylometry", proposal.Method);
    }

    [Fact]
    public void AFenceQuotedInsideAValueIsNotTakenForTheReplysOwn()
    {
        var parsed = GeminiTranslationService.ParseEchoCandidates("""
            [{"citationRef":"6.851","rationale":"The scholiast writes ``` where the line breaks."}]
            """);

        Assert.Equal("The scholiast writes ``` where the line breaks.", Assert.Single(parsed).Rationale);
    }

    [Fact]
    public void ASingleStringWhereAListWasAskedForIsAListOfOne()
    {
        var parsed = GeminiTranslationService.ParseProjectSuggestions("""
            [{"title":"Night","centralQuestion":"Why night?","researchQuestions":"Is the watch Homeric?",
              "hypotheses":[{"title":"Homeric","statement":["Borrowed","from Il. 10"]}]}]
            """);

        var suggestion = Assert.Single(parsed);
        Assert.Equal(["Is the watch Homeric?"], suggestion.ResearchQuestions);
        Assert.Equal("Borrowed, from Il. 10", Assert.Single(suggestion.Hypotheses).Statement);
    }

    [Fact]
    public void AReplyWithNoJsonInItStillSaysSo() =>
        Assert.Throws<InvalidOperationException>(() =>
            GeminiTranslationService.ParseResearchEvidenceCandidates("I could not find any relevant passages."));
}
