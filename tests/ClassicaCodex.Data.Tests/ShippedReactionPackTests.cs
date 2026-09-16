using ClassicaCodex.Core.Reactions;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The content that actually ships, run through the same rules as the fixtures
/// next door.
///
/// This is the test that matters. ReactionPackTests proves the validator can
/// catch a broken pack; this proves the packs in the executable are not broken,
/// on every build, rather than on the day somebody remembered to look. A pack
/// that fails validation is silently skipped at runtime - which is the right
/// behaviour for a stranger's file dropped in the Reactions folder and the
/// wrong thing to discover about our own after a release, when the symptom is
/// a menu item that has quietly stopped appearing.
/// </summary>
public class ShippedReactionPackTests
{
    [Fact]
    public void EveryShippedPackLoads()
    {
        Assert.Empty(ReactionLibrary.Problems);
        Assert.NotEmpty(ReactionLibrary.Packs);
    }

    /// <summary>
    /// Debate ids are how a window remembers which conversation it is showing
    /// when a work has more than one. Two packs sharing one would make the
    /// second unreachable.
    /// </summary>
    [Fact]
    public void DebateIdsAreUnique()
    {
        var duplicates = ReactionLibrary.Packs
            .GroupBy(p => p.Debate.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, "duplicate debate ids: " + string.Join(", ", duplicates));
    }

    /// <summary>
    /// Every turn's speaker resolves to a critic with a name, a sketch and a
    /// portrait. The window shows all three; a critic missing one displays as
    /// a blank card, which reads as a bug rather than as a gap in the content.
    /// </summary>
    [Fact]
    public void EverySpeakerIsFullyDescribed()
    {
        var thin = new List<string>();

        foreach (var pack in ReactionLibrary.Packs)
        foreach (var id in pack.Debate.SpeakerIds)
        {
            var critic = ReactionLibrary.Critic(id);

            if (critic == null) { thin.Add($"{pack.Name}: {id} is not declared anywhere"); continue; }
            if (string.IsNullOrWhiteSpace(critic.Sketch)) thin.Add($"{critic.Id} has no sketch");
            if (string.IsNullOrWhiteSpace(critic.Tastes)) thin.Add($"{critic.Id} has no tastes");
            if (string.IsNullOrWhiteSpace(critic.Avatar)) thin.Add($"{critic.Id} has no avatar");
            if (string.IsNullOrWhiteSpace(critic.Role)) thin.Add($"{critic.Id} has no role");
            if (string.IsNullOrWhiteSpace(critic.Era)) thin.Add($"{critic.Id} has no era");
        }

        Assert.True(thin.Count == 0, string.Join("\n", thin));
    }

    /// <summary>
    /// Every real person in the shipped content, with the reference for what
    /// they are made to say. Not an assertion so much as a printed ledger -
    /// but the count is asserted, so adding a historical critic without a
    /// citation cannot pass, and the list is here to be read by a person who
    /// wants to check the scholarship rather than the code.
    /// </summary>
    [Fact]
    public void EveryRealPersonSpeaksFromASource()
    {
        var uncited = new List<string>();
        var cited = 0;

        foreach (var pack in ReactionLibrary.Packs)
        foreach (var turn in pack.Debate.Turns)
        {
            var critic = ReactionLibrary.Critic(turn.CriticId);
            if (critic?.Kind != CriticKind.Historical) continue;

            if (string.IsNullOrWhiteSpace(turn.SourceCitation))
                uncited.Add($"{pack.Name} turn {turn.Seq}: {critic.Name}");
            else
                cited++;
        }

        Assert.True(uncited.Count == 0, "uncited words in a real mouth: " + string.Join(", ", uncited));
        Assert.True(cited > 0, "no historical critic speaks anywhere - the guard above proves nothing");
    }

    /// <summary>
    /// A composite must never be mistakable for the real person they stand
    /// beside. Nobody invented may carry the name of somebody who existed and
    /// is quoted elsewhere in the same content.
    /// </summary>
    [Fact]
    public void NoCompositeBorrowsARealCriticsName()
    {
        var real = ReactionLibrary.Packs
            .SelectMany(p => p.Critics)
            .Where(c => c.Kind == CriticKind.Historical)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var borrowed = ReactionLibrary.Packs
            .SelectMany(p => p.Critics)
            .Where(c => c.Kind == CriticKind.Composite && real.Contains(c.Name))
            .Select(c => c.Id)
            .ToList();

        Assert.True(borrowed.Count == 0, "invented critics using a real name: " + string.Join(", ", borrowed));
    }

    /// <summary>
    /// Each debate names a work that at least looks like the one it is about.
    /// The library cannot be consulted from here - it varies per reader, and
    /// these tests run against an empty one - so this checks the reference is
    /// filled in rather than that it resolves.
    /// </summary>
    [Fact]
    public void EveryDebateNamesAWork()
    {
        foreach (var pack in ReactionLibrary.Packs)
        {
            Assert.False(string.IsNullOrWhiteSpace(pack.Debate.Work.AuthorKey), pack.Name);
            Assert.NotEmpty(pack.Debate.Work.TitleKeys);
            Assert.False(string.IsNullOrWhiteSpace(pack.Debate.SettingNote), pack.Name + " has no setting note");
        }
    }

    /// <summary>
    /// The six works the kickoff spec asked for first. Named explicitly so
    /// that removing one is a decision somebody makes rather than something
    /// that happens.
    /// </summary>
    [Theory]
    [InlineData("Aristophanes", "Clouds")]
    [InlineData("Euripides", "Medea")]
    [InlineData("Homer", "Iliad")]
    [InlineData("P. Vergilius Maro (Virgil)", "Aeneid")]
    [InlineData("Ovid", "Metamorphoses")]
    [InlineData("Thucydides", "History of the Peloponnesian War")]
    public void ThePilotWorksAllHaveADebate(string author, string title)
    {
        Assert.NotEmpty(ReactionLibrary.DebatesFor(author, title));
    }

    /// <summary>
    /// The author and title strings above are the ones a real Perseus library
    /// actually carries, taken from one rather than guessed at - which is the
    /// only way this test means anything. A work with no debate must come back
    /// empty rather than matching something loosely.
    /// </summary>
    [Fact]
    public void AWorkWithNoDebateHasNone()
    {
        Assert.Empty(ReactionLibrary.DebatesFor("Xenophon", "Anabasis"));
        Assert.Empty(ReactionLibrary.DebatesFor(string.Empty, string.Empty));
    }
}
