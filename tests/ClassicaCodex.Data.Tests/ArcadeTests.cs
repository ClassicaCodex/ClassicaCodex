using System.Numerics;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class ArcadeQuestTests
{
    private static ArcadeStory Story(bool unique = true)
    {
        var clues = new[] { new QuestPassage("Homer", new[] { "Iliad" }, "1.1", "Clue", "Meaning", unique),
            new QuestPassage("Homer", new[] { "Odyssey" }, "1.1", "Next clue", "Ending") };
        return new ArcadeStory(new QuestArc("test", "Journey", "Premise", "Payoff", clues),
            clues.Select((p, i) => (IReadOnlyList<ArcadePassage>)new[] {
                new ArcadePassage(i + 1, i + 1, "Homer", p.TitleKeys[0], "urn:cts:greekLit:tlg0012.tlg001.test-grc1." + p.CitationRef, "same words", "grc") }).ToArray());
    }

    [Fact]
    public void EntireStoryRequiresVictoryAndCorrectReadingBeforeEachAdvance()
    {
        var story = Story(); var quest = new ArcadeQuest(story); var row = story.Sources[0][0];
        Assert.False(quest.Submit(row)); Assert.False(quest.WinBattle());
        Assert.True(quest.BeginBattle()); Assert.False(quest.BeginBattle()); Assert.False(quest.Submit(row));
        Assert.True(quest.WinBattle()); Assert.False(quest.BeginBattle());
        Assert.False(quest.Submit(row with { Title = "Odyssey" }));
        Assert.False(quest.Submit(row with { Author = "Virgil" }));
        Assert.False(quest.Submit(row with { Citation = "11.1" }));
        Assert.False(quest.Submit(row with { Text = "  " }));
        Assert.True(quest.Submit(row)); Assert.False(quest.Submit(row));
        Assert.Equal(ArcadeQuestPhase.Revelation, quest.Phase);
        Assert.True(quest.BeginBattle()); Assert.Equal(1, quest.Chapter);
        Assert.True(quest.WinBattle()); Assert.True(quest.Submit(story.Sources[1][0]));
        Assert.Equal(ArcadeQuestPhase.Complete, quest.Phase); Assert.Equal(2, quest.Found.Count);
        Assert.False(quest.BeginBattle()); Assert.False(quest.WinBattle());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RepeatedLinesAreAcceptedOnlyWhenClaudeMarksThemNonUnique(bool unique, bool accepted)
    {
        var quest = new ArcadeQuest(Story(unique)); quest.BeginBattle(); quest.WinBattle();
        Assert.Equal(accepted, quest.Submit(new ArcadePassage(99, 99, "Other", "Other", "99", " same\n words ", "grc")));
    }

    [Fact]
    public void UnresolvedStoryCannotStart() => Assert.Throws<ArgumentException>(() =>
        new ArcadeQuest(Story() with { Sources = Array.Empty<IReadOnlyList<ArcadePassage>>() }));
}

public class BronzeArenaTests
{
    private static BronzeEnemy Enemy(Vector2 position, float health = 100) =>
        new() { Position = position, Health = health, MaxHealth = health, Clock = 10 };

    [Fact]
    public void MovementNormalizesDiagonalsAndRejectsClockGaps()
    {
        var arena = new BronzeArena(1, 1); var start = arena.Player;
        arena.Update(1, new BronzeInput(new Vector2(1, 1)));
        Assert.InRange(Vector2.Distance(start, arena.Player), 4.99f, 5.01f);
        var time = arena.Time; arena.Update(float.NaN, default); arena.Update(-1, default);
        Assert.Equal(time, arena.Time);
        arena.Update(.01f, new BronzeInput(new Vector2(float.NaN, 1)));
        Assert.True(float.IsFinite(arena.Player.X));
    }

    [Fact]
    public void SpearHitsAheadAndCooldownPreventsRepeatedDamage()
    {
        var arena = new BronzeArena(1, 1);
        var ahead = Enemy(arena.Player + new Vector2(30, 0)); var behind = Enemy(arena.Player - new Vector2(30, 0));
        arena.Enemies.AddRange(new[] { ahead, behind });
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Strike: true));
        Assert.Equal(71, ahead.Health); Assert.Equal(100, behind.Health);
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Strike: true)); Assert.Equal(71, ahead.Health);
    }

    [Fact]
    public void JavelinCanKillAtRange()
    {
        var arena = new BronzeArena(1, 1); arena.Enemies.Add(Enemy(arena.Player + new Vector2(90, 0), 30));
        for (var i = 0; i < 30; i++) arena.Update(1f / 60, new BronzeInput(Vector2.Zero, Javelin: i == 0));
        Assert.Equal(1, arena.Kills); Assert.True(arena.Score > 0);
    }

    [Fact]
    public void ShieldBlocksInFrontButNotBehind()
    {
        var front = new BronzeArena(1, 1); front.Enemies.Add(Enemy(front.Player + new Vector2(12, 0)));
        front.Update(.01f, new BronzeInput(Vector2.Zero, Shield: true)); Assert.Equal(front.MaxHealth, front.Health);
        Assert.True(front.Guard < 90);
        var rear = new BronzeArena(1, 1); rear.Enemies.Add(Enemy(rear.Player - new Vector2(12, 0)));
        rear.Update(.01f, new BronzeInput(Vector2.Zero, Shield: true)); Assert.True(rear.Health < rear.MaxHealth);
        var health = rear.Health; rear.Update(.01f, default); Assert.Equal(health, rear.Health);
    }

    [Fact]
    public void DodgeConsumesGuardAndAvoidsContact()
    {
        var arena = new BronzeArena(1, 1); arena.Enemies.Add(Enemy(arena.Player + new Vector2(8, 0)));
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Dodge: true));
        Assert.Equal(arena.MaxHealth, arena.Health); Assert.Equal(80, arena.Guard); Assert.True(arena.Invulnerable > 0);
    }

    [Fact]
    public void MagicUnlocksAndUsesManaWithCooldown()
    {
        var first = new BronzeArena(1, 1); first.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true));
        Assert.Equal(100, first.Mana); Assert.Empty(first.Shots);
        var second = new BronzeArena(2, 1); second.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true));
        Assert.Equal(65, second.Mana); Assert.Equal(3, second.Shots.Count);
        second.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true)); Assert.Equal(3, second.Shots.Count);
        var fourth = new BronzeArena(4, 1); fourth.Enemies.Add(Enemy(fourth.Player + new Vector2(70, 0)));
        fourth.Shots.Add(new BronzeShot { Position = fourth.Player + new Vector2(40, 0), Hostile = true });
        fourth.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true));
        Assert.Empty(fourth.Shots); Assert.Equal(39, fourth.Enemies[0].Health);
    }

    [Theory]
    [InlineData(1, BronzeEnemyKind.Cyclops)]
    [InlineData(2, BronzeEnemyKind.Gorgon)]
    [InlineData(5, BronzeEnemyKind.Hydra)]
    public void VictoryRequiresWholeWaveAndBoss(int level, BronzeEnemyKind bossKind)
    {
        var arena = new BronzeArena(level, 11); bool sawBoss = false;
        // Remove combat difficulty from this check to isolate wave completion.
        for (var i = 0; i < 3000 && arena.State == BronzeBattleState.Fighting; i++)
        {
            foreach (var enemy in arena.Enemies)
            {
                if (enemy.Boss) { Assert.Equal(bossKind, enemy.Kind); sawBoss = true; }
                enemy.Health = 0;
            }
            arena.Update(1f / 60, default);
        }
        Assert.True(sawBoss); Assert.Equal(BronzeBattleState.Won, arena.State);
        Assert.Equal(arena.WaveSize + 1, arena.Kills);
        var time = arena.Time; arena.Update(.01f, new BronzeInput(Vector2.One)); Assert.Equal(time, arena.Time);
    }

    [Fact]
    public void DeathStopsSimulationAndRetryStartsHealthy()
    {
        var arena = new BronzeArena(3, 4);
        arena.Shots.Add(new BronzeShot { Position = arena.Player, Hostile = true, Damage = 1000 });
        arena.Update(.01f, default); Assert.Equal(BronzeBattleState.Lost, arena.State);
        var position = arena.Player; arena.Update(.05f, new BronzeInput(Vector2.One)); Assert.Equal(position, arena.Player);
        var retry = new BronzeArena(3, 5); Assert.Equal(retry.MaxHealth, retry.Health);
    }
}

[Collection("Database")]
public class ArcadeRepositoryTests
{
    [Fact]
    public async Task ResolvesCompleteStoryAcrossCorpusIdentifiersAndRejectsMissingLastLine()
    {
        using var db = await TempDatabase.CreateAsync();
        var arc = QuestArcs.All.First(a => a.Passages.All(p => p.AuthorKey == "Homer"));
        var index = 0;
        foreach (var group in arc.Passages.GroupBy(p => p.TitleKeys[0]))
        {
            var edition = await db.SeedFullEditionAsync("arcade" + index++, "Homer", "greekLit", group.Key, "Original", "grc");
            await db.InsertLinesAsync(edition, group.Select(p => ("urn:cts:greekLit:tlg0012.tlg001.any-edition." + p.CitationRef, "fixture " + p.CitationRef)).ToArray());
        }
        var repository = new ArcadeQuestRepository(db.Path);
        var story = Assert.Single(repository.Load(), s => s.Arc.Key == arc.Key);
        Assert.Equal(arc.Passages.Length, story.Sources.Count);
        var final = story.Sources.Last()[0]; Assert.Equal(final, repository.GetPassage(final.NodeId));
        await db.ExecuteAsync($"UPDATE TextNodes SET CitationRef='999.{arc.Passages.Last().CitationRef}' WHERE TextNodeId={final.NodeId}");
        Assert.DoesNotContain(repository.Load(), s => s.Arc.Key == arc.Key);
        Assert.Null(repository.GetPassage(long.MaxValue));
    }

    [Fact]
    public async Task EmptyLibraryAndCancellationNeverInventAStory()
    {
        using var db = await TempDatabase.CreateAsync(); var repository = new ArcadeQuestRepository(db.Path);
        Assert.Empty(repository.Load());
        Assert.Throws<OperationCanceledException>(() => repository.Load(new CancellationToken(true)));
    }
}

