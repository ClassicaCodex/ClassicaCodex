using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The passage-hunts, checked for the things that would break a player's run
/// rather than for their prose.
///
/// The citations themselves were resolved against a full library when they
/// were written - all 28 of them, in the editions this application installs.
/// What these tests hold is the structure: that an arc has a beginning and an
/// end, that nothing in it is blank, and above all that no arc can strand
/// somebody halfway through a story.
/// </summary>
public class QuestArcTests
{
    [Fact]
    public void ThereAreArcsToPlay() => Assert.NotEmpty(QuestArcs.All);

    [Fact]
    public void EveryArcKeyIsDistinct()
    {
        var keys = QuestArcs.All.Select(a => a.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// A one-passage arc is not a story and a two-passage one barely is. The
    /// point of the exercise is that arriving at the last passage changes what
    /// the first one meant, which needs room.
    /// </summary>
    [Fact]
    public void EveryArcHasEnoughPassagesToBeAStory() =>
        Assert.All(QuestArcs.All, a =>
            Assert.True(a.Passages.Length >= 3, $"{a.Key} has only {a.Passages.Length}"));

    [Fact]
    public void EveryArcSaysWhatItIsAndWhatItAddedUpTo() =>
        Assert.All(QuestArcs.All, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Title), $"{a.Key} has no title");
            Assert.False(string.IsNullOrWhiteSpace(a.Premise), $"{a.Key} has no premise");
            Assert.False(string.IsNullOrWhiteSpace(a.Payoff), $"{a.Key} has no payoff");
        });

    [Fact]
    public void EveryPassageIsCompletelyFilledIn() =>
        Assert.All(QuestArcs.All.SelectMany(a => a.Passages.Select(p => (a.Key, p))), x =>
        {
            var (key, p) = x;
            Assert.False(string.IsNullOrWhiteSpace(p.AuthorKey), $"{key}: no author");
            Assert.NotEmpty(p.TitleKeys);
            Assert.All(p.TitleKeys, t => Assert.False(string.IsNullOrWhiteSpace(t)));
            Assert.False(string.IsNullOrWhiteSpace(p.CitationRef), $"{key}: no citation");
            Assert.False(string.IsNullOrWhiteSpace(p.Award), $"{key} {p.CitationRef}: no award text");
            Assert.False(string.IsNullOrWhiteSpace(p.Reveal), $"{key} {p.CitationRef}: no reveal text");
        });

    /// <summary>
    /// The award is a clue, not the answer. A passage whose award quoted the
    /// line would hand the player the thing they are meant to go and find.
    /// </summary>
    [Fact]
    public void NoAwardGivesTheGreekAway() =>
        Assert.All(QuestArcs.All.SelectMany(a => a.Passages), p =>
            Assert.DoesNotContain(p.Award, c => c >= 'Ͱ' && c <= 'Ͽ'));

    /// <summary>
    /// A citation has to be the reference alone. Passing the stored form -
    /// which in this corpus usually carries the whole CTS URN - would never
    /// match, since the game compares against PassageCitation.Display.
    /// </summary>
    [Fact]
    public void NoCitationCarriesAUrn() =>
        Assert.All(QuestArcs.All.SelectMany(a => a.Passages), p =>
            Assert.DoesNotContain("urn:", p.CitationRef, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// No arc may send a player to the same line twice - it would read as the
    /// game losing its place.
    /// </summary>
    [Fact]
    public void NoArcRepeatsAPassage() =>
        Assert.All(QuestArcs.All, a =>
        {
            var seen = a.Passages.Select(p => $"{p.AuthorKey}|{p.TitleKeys[0]}|{p.CitationRef}").ToList();
            Assert.Equal(seen.Count, seen.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });

    // ---- what a library can actually offer --------------------------------

    /// <summary>
    /// All or nothing. An arc missing its last passage is worse than an arc
    /// not offered, because the player reaches the end of a story that cannot
    /// finish.
    /// </summary>
    [Fact]
    public void AnArcMissingOnePassageIsNotOffered()
    {
        var target = QuestArcs.All[0].Passages[^1];

        var playable = QuestArcs.PlayableIn(p => !ReferenceEquals(p, target));

        Assert.DoesNotContain(playable, a => a.Key == QuestArcs.All[0].Key);
    }

    [Fact]
    public void EverythingPresentMeansEverythingPlayable() =>
        Assert.Equal(QuestArcs.All.Length, QuestArcs.PlayableIn(_ => true).Count);

    [Fact]
    public void NothingPresentMeansNothingPlayable() =>
        Assert.Empty(QuestArcs.PlayableIn(_ => false));

    /// <summary>
    /// A library with only the classical Greek core - no tragedy, no
    /// Hellenistic epic - still has something to play, which is why two arcs
    /// draw on Homer alone.
    /// </summary>
    [Fact]
    public void AHomerOnlyLibraryCanStillPlaySomething()
    {
        var playable = QuestArcs.PlayableIn(p =>
            p.AuthorKey.Equals("Homer", StringComparison.OrdinalIgnoreCase));

        Assert.NotEmpty(playable);
        Assert.All(playable, a => Assert.All(a.Passages, p => Assert.Equal("Homer", p.AuthorKey)));
    }
}
