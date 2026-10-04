using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// What the Research Bench sends Gemini when it asks for corpus evidence, and
/// what a returned citation may resolve against.
///
/// The edition is cut at a character budget - the whole Iliad does not fit -
/// and a citation is accepted only if it names a passage that was sent. It used
/// to resolve against every passage in the edition, so a reference to book 22
/// recalled from training, when the text stopped in book 6, was saved as
/// "verified against local edition" beside a note that the search had been
/// truncated before it.
/// </summary>
public class ResearchEvidenceCorpusTests
{
    private static TextNode Node(string citation, string text) => new() { CitationRef = citation, Text = text };

    [Fact]
    public void OnlyThePassagesThatFitAreReturnedAsSent()
    {
        var nodes = new[]
        {
            Node("1.1", "μῆνιν ἄειδε θεὰ"),
            Node("1.2", "οὐλομένην, ἣ μυρί᾽"),
            Node("22.395", "ἦ ῥα, καὶ Ἕκτορα δῖον ἀεικέα μήδετο ἔργα"),
        };
        var budget = "[1.1] μῆνιν ἄειδε θεὰ\n".Length + "[1.2] οὐλομένην, ἣ μυρί᾽\n".Length;

        var (text, truncatedAt, sent) = ResearchBenchForm.BuildTaggedCorpus(nodes, budget);

        Assert.Equal(["1.1", "1.2"], sent.Select(n => n.CitationRef));
        Assert.Equal("1.2", truncatedAt);
        Assert.DoesNotContain("22.395", text);
    }

    [Fact]
    public void AnEditionThatFitsIsSentWholeWithNoTruncationMark()
    {
        var nodes = new[] { Node("1", "first"), Node("", "unnumbered"), Node("2", "  "), Node("3", "third") };

        var (_, truncatedAt, sent) = ResearchBenchForm.BuildTaggedCorpus(nodes);

        Assert.Null(truncatedAt);
        // Lines with no reference or no text are never sent, so they cannot be cited.
        Assert.Equal(["1", "3"], sent.Select(n => n.CitationRef));
    }
}
