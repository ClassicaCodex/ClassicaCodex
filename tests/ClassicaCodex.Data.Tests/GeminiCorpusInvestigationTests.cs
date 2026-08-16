using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class GeminiCorpusInvestigationTests
{
    [Fact]
    public void ParsesFencedCandidatesAndNormalizesUnknownRoles()
    {
        var parsed = GeminiTranslationService.ParseCorpusInvestigationCandidates("""
            ```json
            [
              {"candidateKey":"P000012","role":"counterexample","confidence":"high",
               "rationale":"The situation recurs but reverses agency.","suggestedMotifs":"failed recognition"},
              {"candidateKey":"P000099","role":"invented-role","rationale":"Worth checking."},
              {"role":"parallel","rationale":"No locally resolvable key."}
            ]
            ```
            """);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("P000012", parsed[0].CandidateKey);
        Assert.Equal("counterexample", parsed[0].Role);
        Assert.Equal("failed recognition", parsed[0].SuggestedMotifs);
        Assert.Equal("unclassified", parsed[1].Role);
        Assert.Equal("unspecified", parsed[1].Confidence);
    }

    [Fact]
    public void InvalidCorpusCandidateJsonFailsClearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GeminiTranslationService.ParseCorpusInvestigationCandidates("not json"));

        Assert.Contains("corpus-investigation response wasn't valid candidate JSON", error.Message);
    }
}
