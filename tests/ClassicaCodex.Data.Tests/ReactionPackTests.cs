using ClassicaCodex.Core.Reactions;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The rules that keep the Fictional Ancient Reactions content honest.
///
/// Every test here starts from one pack that validates, changes exactly one
/// thing, and asserts that the one thing is caught. That shape is deliberate
/// and it is the reason to trust these: a fixture written wrong from scratch
/// can fail for a reason nobody intended, pass the assertion, and leave the
/// rule it was supposed to guard untested. <see cref="TheBaseFixtureIsValid"/>
/// is what makes the rest mean anything - if it ever goes red, every other
/// test in this file is measuring the wrong failure.
///
/// The rule worth reading twice is <see cref="AHistoricalCriticCannotSpeak
/// WithoutASource"/>. Inventing an Athenian farmer harms nobody. Inventing an
/// opinion for Plato and presenting it in a tool full of real Plato is the one
/// thing this feature could do that would be genuinely dishonest, because the
/// reader has no way to tell it from the real thing. The citation is what
/// makes it checkable, so the citation is enforced rather than remembered.
/// </summary>
public class ReactionPackTests
{
    /// <summary>
    /// A complete, valid pack. Each test below takes this and breaks one
    /// thing, via a plain string replacement on a token that appears once.
    /// </summary>
    private const string ValidPack = """
    {
      "critics": [
        { "id": "alpha", "name": "Alpha", "kind": "Composite", "era": "Classical Athens",
          "floruitStart": -450, "floruitEnd": -400, "role": "potter" },
        { "id": "beta", "name": "Beta", "kind": "Composite", "era": "Classical Athens",
          "floruitStart": -450, "floruitEnd": -400, "role": "juror" },
        { "id": "plato", "name": "Plato", "kind": "Historical", "era": "Classical Athens",
          "floruitStart": -399, "floruitEnd": -347, "role": "philosopher" }
      ],
      "debate": {
        "id": "test-debate",
        "work": { "authorKey": "aristophanes", "titleKeys": ["clouds"] },
        "title": "A test debate",
        "kind": "Scene",
        "compositionYear": -423,
        "settingYear": -423,
        "settingPlace": "Athens",
        "settingNote": "A wine-shop."
      },
      "turns": [
        { "seq": 1, "criticId": "alpha", "text": "One.", "passageRef": "225" },
        { "seq": 2, "criticId": "beta", "text": "Two." },
        { "seq": 3, "criticId": "alpha", "text": "Three." },
        { "seq": 4, "criticId": "beta", "text": "Four." },
        { "seq": 5, "criticId": "alpha", "text": "Five." },
        { "seq": 6, "criticId": "beta", "text": "Six." }
      ]
    }
    """;

    private static IReadOnlyList<string> ProblemsIn(string json)
    {
        var result = ReactionPackReader.Read(json, "fixture.json");
        return result.Pack == null
            ? result.Problems
            : result.Problems.Concat(ReactionPackValidator.Validate(result.Pack)).ToList();
    }

    /// <summary>Breaks exactly one thing, and insists it really was one thing.</summary>
    private static IReadOnlyList<string> ProblemsWith(string find, string replace)
    {
        Assert.Equal(1, Occurrences(ValidPack, find));
        return ProblemsIn(ValidPack.Replace(find, replace));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static void AssertMentions(IReadOnlyList<string> problems, string fragment)
    {
        Assert.True(
            problems.Any(p => p.Contains(fragment, StringComparison.OrdinalIgnoreCase)),
            $"expected a problem mentioning \"{fragment}\", got: "
            + (problems.Count == 0 ? "(none)" : string.Join(" | ", problems)));
    }

    // ---- the fixture itself ------------------------------------------------

    [Fact]
    public void TheBaseFixtureIsValid()
    {
        Assert.Empty(ProblemsIn(ValidPack));
    }

    // ---- structure ---------------------------------------------------------

    [Fact]
    public void SequenceNumbersHaveNoGaps()
    {
        AssertMentions(ProblemsWith("\"seq\": 4", "\"seq\": 5"), "numbered");
    }

    [Fact]
    public void SequenceNumbersStartAtOne()
    {
        AssertMentions(ProblemsWith("\"seq\": 1", "\"seq\": 0"), "start at 1");
    }

    [Fact]
    public void ADebateIsLongEnoughToBeOne()
    {
        // Cut the last turn out, comma and all. Done by index rather than by
        // replacing a literal with a newline in it: this file's line endings
        // are whatever git checked out, and a replacement that silently
        // matches nothing would leave the fixture valid and the test green.
        var start = ValidPack.IndexOf("{ \"seq\": 6", StringComparison.Ordinal);
        Assert.True(start > 0, "the fixture no longer has a sixth turn to remove");

        var end = ValidPack.IndexOf('}', start) + 1;
        var comma = ValidPack.LastIndexOf(',', start);
        var short_ = ValidPack.Remove(comma, end - comma);

        Assert.Equal(5, Occurrences(short_, "\"seq\":"));
        AssertMentions(ProblemsIn(short_), "turns");
    }

    [Fact]
    public void ATurnHasToSaySomething()
    {
        AssertMentions(ProblemsWith("\"text\": \"Three.\"", "\"text\": \"   \""), "says nothing");
    }

    // ---- speakers ----------------------------------------------------------

    [Fact]
    public void EverySpeakerIsDeclared()
    {
        AssertMentions(ProblemsWith("\"criticId\": \"beta\", \"text\": \"Two.\"",
            "\"criticId\": \"nobody\", \"text\": \"Two.\""), "not declared");
    }

    [Fact]
    public void ADebateNeedsTwoSpeakers()
    {
        // Every turn to one voice. Not ProblemsWith, because this is the one
        // change that legitimately touches more than one line.
        AssertMentions(ProblemsIn(ValidPack.Replace("\"criticId\": \"beta\"", "\"criticId\": \"alpha\"")),
            "at least two");
    }

    [Fact]
    public void AFloruitCannotEndBeforeItBegins()
    {
        AssertMentions(ProblemsWith("\"floruitStart\": -450, \"floruitEnd\": -400, \"role\": \"potter\"",
            "\"floruitStart\": -400, \"floruitEnd\": -450, \"role\": \"potter\""), "before it starts");
    }

    // ---- anachronism -------------------------------------------------------

    [Fact]
    public void ADebateCannotHappenBeforeTheWorkExists()
    {
        AssertMentions(ProblemsWith("\"settingYear\": -423", "\"settingYear\": -450"),
            "before the work was written");
    }

    /// <summary>
    /// The rule the spec leads with, and the one a reader would never catch.
    /// Plato was born around 428 and this debate is set in 423.
    /// </summary>
    [Fact]
    public void ASpeakerHasToHaveBeenAlive()
    {
        AssertMentions(ProblemsWith("{ \"seq\": 3, \"criticId\": \"alpha\"",
            "{ \"seq\": 3, \"criticId\": \"plato\""), "outside their floruit");
    }

    [Fact]
    public void AScenesTurnsDoNotCarryTheirOwnYears()
    {
        AssertMentions(ProblemsWith("\"text\": \"Two.\"", "\"text\": \"Two.\", \"year\": -420"),
            "this is a Scene");
    }

    [Fact]
    public void ASceneNeedsAYear()
    {
        AssertMentions(ProblemsWith("\"settingYear\": -423,", string.Empty), "needs a settingYear");
    }

    // ---- across the centuries ----------------------------------------------

    /// <summary>
    /// The same pack as a conversation that never happened: no shared year,
    /// every turn dated, Plato now able to speak because he speaks in 399.
    /// </summary>
    private static string AcrossTheCenturies(params string[] replacements)
    {
        var json = ValidPack
            .Replace("\"kind\": \"Scene\"", "\"kind\": \"AcrossTheCenturies\"")
            .Replace("\"settingYear\": -423,", string.Empty)
            .Replace("\"criticId\": \"alpha\", \"text\": \"One.\", \"passageRef\": \"225\"",
                "\"criticId\": \"alpha\", \"text\": \"One.\", \"year\": -423")
            .Replace("\"criticId\": \"beta\", \"text\": \"Two.\"",
                "\"criticId\": \"beta\", \"text\": \"Two.\", \"year\": -420")
            .Replace("\"criticId\": \"alpha\", \"text\": \"Three.\"",
                "\"criticId\": \"alpha\", \"text\": \"Three.\", \"year\": -415")
            .Replace("\"criticId\": \"beta\", \"text\": \"Four.\"",
                "\"criticId\": \"beta\", \"text\": \"Four.\", \"year\": -410")
            .Replace("\"criticId\": \"alpha\", \"text\": \"Five.\"",
                "\"criticId\": \"alpha\", \"text\": \"Five.\", \"year\": -405")
            .Replace("\"criticId\": \"beta\", \"text\": \"Six.\"",
                "\"criticId\": \"plato\", \"text\": \"Six.\", \"year\": -380, "
                + "\"sourceCitation\": \"Republic 10.607a\"");

        for (var i = 0; i + 1 < replacements.Length; i += 2)
            json = json.Replace(replacements[i], replacements[i + 1]);

        return json;
    }

    [Fact]
    public void ACenturiesSpanningPackIsValid()
    {
        Assert.Empty(ProblemsIn(AcrossTheCenturies()));
    }

    [Fact]
    public void EveryTurnOfACenturiesSpanningDebateIsDated()
    {
        AssertMentions(ProblemsIn(AcrossTheCenturies("\"text\": \"Three.\", \"year\": -415",
            "\"text\": \"Three.\"")), "no year");
    }

    [Fact]
    public void TheCenturiesOnlyMoveForward()
    {
        AssertMentions(ProblemsIn(AcrossTheCenturies("\"text\": \"Four.\", \"year\": -410",
            "\"text\": \"Four.\", \"year\": -440")), "earlier than the turn before");
    }

    [Fact]
    public void ADatedTurnStillHasToBeInsideItsSpeakersLifetime()
    {
        AssertMentions(ProblemsIn(AcrossTheCenturies("\"text\": \"Six.\", \"year\": -380",
            "\"text\": \"Six.\", \"year\": -340")), "outside their floruit");
    }

    // ---- citation ----------------------------------------------------------

    /// <summary>
    /// A real person may only be given a view that can be checked. See the
    /// class summary; this is the rule the feature stands on.
    /// </summary>
    [Fact]
    public void AHistoricalCriticCannotSpeakWithoutASource()
    {
        AssertMentions(ProblemsIn(AcrossTheCenturies(
            ", \"sourceCitation\": \"Republic 10.607a\"", string.Empty)), "real person");
    }

    [Fact]
    public void AComposite_DoesNotNeedASource()
    {
        // The mirror of the rule above, and worth asserting: if invention
        // needed a citation too, the packs could not be written at all.
        Assert.Empty(ProblemsIn(ValidPack));
        Assert.DoesNotContain(ProblemsIn(ValidPack), p => p.Contains("real person"));
    }

    [Fact]
    public void APassageReferenceHasToLookLikeOne()
    {
        AssertMentions(ProblemsWith("\"passageRef\": \"225\"",
            "\"passageRef\": \"somewhere near the start\""), "shape of a citation");
    }

    [Fact]
    public void ALinkedSourceNeedsALabelToPrint()
    {
        AssertMentions(ProblemsWith("\"text\": \"Two.\"",
            "\"text\": \"Two.\", \"sourceWork\": { \"authorKey\": \"plato\", "
            + "\"titleKeys\": [\"republic\"], \"citationRef\": \"10.607\" }"), "no label");
    }

    // ---- malformed input ---------------------------------------------------

    /// <summary>
    /// A reader can drop their own pack in a folder beside the executable, so
    /// "somebody else's file has a stray comma in it" is an input, not a bug.
    /// It has to come back as a sentence, never as an exception.
    /// </summary>
    [Fact]
    public void BrokenJsonIsReportedRatherThanThrown()
    {
        var problems = ProblemsIn("{ \"critics\": [ oops ] }");

        Assert.NotEmpty(problems);
        AssertMentions(problems, "not valid JSON");
    }

    [Fact]
    public void AnEmptyFileIsReportedRatherThanThrown()
    {
        Assert.NotEmpty(ProblemsIn(string.Empty));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{ \"critics\": [] }")]
    [InlineData("{ \"debate\": {} }")]
    public void APackMissingItsPartsIsReportedRatherThanThrown(string json)
    {
        Assert.NotEmpty(ProblemsIn(json));
    }

    [Fact]
    public void AnUnknownCriticKindIsReported()
    {
        AssertMentions(ProblemsWith("\"kind\": \"Historical\"", "\"kind\": \"Imaginary\""),
            "expected Composite or Historical");
    }

    // ---- matching a work in the reader's own library ------------------------

    [Fact]
    public void AWorkReferenceMatchesTheLibrarysSpelling()
    {
        var aeneid = new WorkReference("vergil", new[] { "aeneid" });

        Assert.True(aeneid.Matches("P. Vergilius Maro (Virgil)", "Aeneid"));
    }

    /// <summary>
    /// StartingPoints learned this the hard way: "caesar" sits inside
    /// "Caesarius Arelatensis Episcopus", and a beginner was sent to a
    /// sixth-century bishop of Arles for the Gallic War. No debate here is
    /// about a work of disputed authorship, so a pseudonymous author is never
    /// the one meant.
    /// </summary>
    [Fact]
    public void AWorkReferenceRefusesThePseudonymousTwin()
    {
        var clouds = new WorkReference("aristophanes", new[] { "clouds" });

        Assert.True(clouds.Matches("Aristophanes", "Clouds"));
        Assert.False(clouds.Matches("Pseudo-Aristophanes", "Clouds"));
    }

    [Fact]
    public void AWorkReferenceDoesNotMatchADifferentWorkByTheSameAuthor()
    {
        var clouds = new WorkReference("aristophanes", new[] { "clouds", "nubes" });

        Assert.False(clouds.Matches("Aristophanes", "Frogs"));
        Assert.True(clouds.Matches("Aristophanes", "Nubes"));
    }
}
