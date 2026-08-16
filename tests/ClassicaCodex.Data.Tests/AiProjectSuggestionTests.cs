using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class AiProjectSuggestionTests
{
    [Fact]
    public void ParsesFencedStructuredProjectBlueprintsAndDropsIncompleteOnes()
    {
        var parsed=GeminiTranslationService.ParseProjectSuggestions("""
            ```json
            [{"category":"novelTheory","title":"Watch imagery and authorship","centralQuestion":"Does watch imagery distinguish Rhesus?",
              "rationale":"A locally testable pattern.","grounding":"P00001",
              "researchQuestions":["Is the distribution distinctive?"],
              "hypotheses":[{"title":"Distinctive cluster","statement":"The cluster differs from controls."}],
              "experiments":[{"title":"Tragic controls","method":"CorpusInvestigator","predictedOutcome":"A narrow cluster","falsificationCriterion":"Broad distribution"}],
              "readingLeadKeys":["R001"],"passageKeys":["P00001"]},
             {"category":"novelTheory","title":"Missing its question"}]
            ```
            """);

        var suggestion=Assert.Single(parsed);Assert.Equal("Watch imagery and authorship",suggestion.Title);
        Assert.Equal("Distinctive cluster",Assert.Single(suggestion.Hypotheses).Title);
        Assert.Equal("CorpusInvestigator",Assert.Single(suggestion.Experiments).Method);
        Assert.Equal("R001",Assert.Single(suggestion.ReadingLeadKeys));
    }

    [Fact]
    public void InvalidProjectProposalJsonFailsClearly()
    {
        var error=Assert.Throws<InvalidOperationException>(()=>GeminiTranslationService.ParseProjectSuggestions("not json"));
        Assert.Contains("weren't valid proposal JSON",error.Message);
    }
}
