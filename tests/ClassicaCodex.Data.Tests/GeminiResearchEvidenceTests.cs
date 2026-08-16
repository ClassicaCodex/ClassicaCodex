using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class GeminiResearchEvidenceTests
{
    [Fact]
    public void ParsesFencedCandidatesWithoutTreatingMissingOptionalFieldsAsEvidenceFailure()
    {
        var json = """
            ```json
            [
              {"citationRef":"12-14","title":"Messenger vocabulary","questionIndex":2,
               "relationship":"contradicts","confidence":"high","rationale":"The diction diverges."},
              {"citationRef":"20","rationale":"Potential context."},
              {"title":"No real citation"}
            ]
            ```
            """;

        var parsed = GeminiTranslationService.ParseResearchEvidenceCandidates(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("12-14", parsed[0].CitationRef);
        Assert.Equal(2, parsed[0].QuestionIndex);
        Assert.Equal("contradicts", parsed[0].Relationship);
        Assert.Equal("Corpus passage 20", parsed[1].Title);
        Assert.Null(parsed[1].QuestionIndex);
        Assert.Equal("contextualizes", parsed[1].Relationship);
    }

    [Fact]
    public void InvalidCandidateJsonFailsClearlyInsteadOfReturningAnEmptyFinding()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GeminiTranslationService.ParseResearchEvidenceCandidates("not json"));

        Assert.Contains("wasn't valid candidate JSON", error.Message);
    }
}
