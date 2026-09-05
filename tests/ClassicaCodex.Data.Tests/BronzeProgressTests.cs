using System.Numerics;
using System.Text.Json;
using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class BronzeGiftTests
{
    [Fact]
    public void OffersPersistAcrossReloadAndNeverRepeatOwnedGifts()
    {
        var owned = new List<BronzeGiftId>();
        for (var chapter = 0; chapter < 5; chapter++)
        {
            var offers = BronzeGifts.Offer(owned, 99, chapter);
            Assert.Equal(offers, BronzeGifts.Offer(owned, 99, chapter));
            Assert.Equal(Math.Min(3, 6 - owned.Count), offers.Length);
            Assert.Equal(offers.Length, offers.Distinct().Count());
            Assert.DoesNotContain(offers, g => owned.Contains(g.Id)); owned.Add(offers[0].Id);
        }
    }

    [Fact]
    public void AthenasShieldReflectsARealShotAndCanKillItsSender()
    {
        var arena = new BronzeArena(2, 1, new[] { BronzeGiftId.Athena });
        var shot = new BronzeShot { Position = arena.Player + new Vector2(8, 0), Velocity = new Vector2(-100, 0), Hostile = true, Damage = 20 };
        arena.Shots.Add(shot); arena.Update(.01f, new BronzeInput(Vector2.Zero, Shield: true));
        Assert.False(shot.Hostile); Assert.True(shot.Velocity.X > 0); Assert.Equal(arena.MaxHealth, arena.Health);
        Assert.InRange(arena.Guard, 91, 93);
        arena.Enemies.Add(new BronzeEnemy { Position = arena.Player + new Vector2(40, 0), Health = 35, MaxHealth = 35, Clock = 10 });
        for (var i = 0; i < 20; i++) arena.Update(.01f, default);
        Assert.Equal(1, arena.Kills);
    }

    [Fact]
    public void HermesImprovesSpeedDodgeCostAndCooldown()
    {
        var arena = new BronzeArena(1, 1, new[] { BronzeGiftId.Hermes }); var start = arena.Player;
        arena.Update(.05f, new BronzeInput(Vector2.UnitX)); Assert.InRange(Vector2.Distance(start, arena.Player), 6.24f, 6.26f);
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Dodge: true)); Assert.Equal(88, arena.Guard);
        for (var i = 0; i < 11; i++) arena.Update(.05f, default);
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Dodge: true)); Assert.True(arena.DodgeTime > 0);
    }

    [Fact]
    public void HephaestusAddsHealthAndReducesDamage()
    {
        var arena = new BronzeArena(1, 1, new[] { BronzeGiftId.Hephaestus }); Assert.Equal(135, arena.MaxHealth);
        arena.Shots.Add(new BronzeShot { Position = arena.Player, Hostile = true, Damage = 20 });
        arena.Update(.01f, default); Assert.Equal(119, arena.Health);
    }

    [Fact]
    public void ApolloReducesCostAndDoublesRegeneration()
    {
        var arena = new BronzeArena(2, 1, new[] { BronzeGiftId.Apollo });
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true)); Assert.Equal(75, arena.Mana);
        arena.Update(.05f, default); Assert.InRange(arena.Mana, 75.69f, 75.71f);
    }

    [Fact]
    public void PoseidonCreatesThreeStrongerJavelins()
    {
        var arena = new BronzeArena(1, 1, new[] { BronzeGiftId.Poseidon });
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Javelin: true)); Assert.Equal(3, arena.Shots.Count);
        Assert.All(arena.Shots, s => Assert.Equal(36, s.Damage));
        Assert.Contains(arena.Shots, s => s.Velocity.Y < 0); Assert.Contains(arena.Shots, s => s.Velocity.Y > 0);
    }

    [Fact]
    public void HadesConcealsPastTheDodgeAndPreventsFreshAiming()
    {
        var arena = new BronzeArena(1, 1, new[] { BronzeGiftId.Hades });
        var enemy = new BronzeEnemy { Position = arena.Player + new Vector2(90, 0), Kind = BronzeEnemyKind.Gorgon, Health = 100, MaxHealth = 100 };
        arena.Enemies.Add(enemy); arena.Update(.01f, new BronzeInput(Vector2.Zero, Dodge: true));
        for (var i = 0; i < 8; i++) arena.Update(.05f, default);
        Assert.Equal(0, arena.DodgeTime); Assert.True(arena.ConcealTime > 0); Assert.Equal(0, enemy.Telegraph);
        arena.Shots.Add(new BronzeShot { Position = arena.Player, Hostile = true, Damage = 1000 });
        arena.Update(.01f, default); Assert.Equal(arena.MaxHealth, arena.Health);
    }

    [Fact]
    public void GiftsCombineWithoutDuplicatingEffects()
    {
        var arena = new BronzeArena(2, 1, new[] { BronzeGiftId.Apollo, BronzeGiftId.Poseidon, BronzeGiftId.Hephaestus, BronzeGiftId.Hephaestus });
        Assert.Equal(150, arena.MaxHealth);
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Javelin: true, Magic: true));
        Assert.Equal(6, arena.Shots.Count); Assert.Equal(75, arena.Mana);
    }
}

public class BronzeCheckpointTests
{
    private static ArcadeStory Story()
    {
        var arc = new QuestArc("test", "Test", "Beginning", "End", new[] {
            new QuestPassage("Homer", new[] { "Iliad" }, "1.1", "clue", "meaning"),
            new QuestPassage("Homer", new[] { "Iliad" }, "1.2", "clue2", "meaning2") });
        return new ArcadeStory(arc, arc.Passages.Select((p, i) => (IReadOnlyList<ArcadePassage>)new[] {
            new ArcadePassage(i + 50, 4, "Homer", "Iliad", p.CitationRef, "original " + i, "grc") }).ToArray());
    }

    [Theory]
    [InlineData(ArcadeQuestPhase.Introduction)]
    [InlineData(ArcadeQuestPhase.Battle)]
    [InlineData(ArcadeQuestPhase.Hunt)]
    [InlineData(ArcadeQuestPhase.Revelation)]
    [InlineData(ArcadeQuestPhase.Complete)]
    public void EveryPhaseRestoresWithoutSkippingAReadingGate(ArcadeQuestPhase phase)
    {
        var story = Story(); var chapter = phase == ArcadeQuestPhase.Complete ? 1 : 0;
        var found = phase is ArcadeQuestPhase.Revelation or ArcadeQuestPhase.Complete
            ? story.Sources.Take(chapter + 1).Select(s => s[0] with { NodeId = 0, WorkId = 0 }).ToList() : new List<ArcadePassage>();
        var saved = new BronzeRunSave { ArcKey = story.Arc.Key, StorySignature = BronzeRunSave.Signature(story.Arc), Chapter = chapter,
            Phase = phase, Found = found, Gifts = phase == ArcadeQuestPhase.Complete ? new() { BronzeGiftId.Athena } : new() };
        var restored = saved.Restore(story); Assert.Equal(phase, restored.Phase); Assert.Equal(chapter, restored.Chapter);
        Assert.Equal(found.Count, restored.Found.Count);
    }

    [Fact]
    public void LaterChapterRequiresItsGiftAndRecoveredVerse()
    {
        var story = Story(); var saved = new BronzeRunSave { ArcKey = story.Arc.Key, StorySignature = BronzeRunSave.Signature(story.Arc),
            Chapter = 1, Phase = ArcadeQuestPhase.Battle, Found = new() { story.Sources[0][0] } };
        Assert.Throws<ArgumentException>(() => saved.Restore(story));
        saved.Gifts.Add(BronzeGiftId.Hermes); Assert.Equal(1, saved.Restore(story).Chapter);
        saved.Found[0] = saved.Found[0] with { Citation = "99" }; Assert.Throws<ArgumentException>(() => saved.Restore(story));
    }

    [Fact]
    public void RewrittenStoryAndInvalidPhaseDoNotSilentlyResume()
    {
        var story = Story(); var saved = new BronzeRunSave { ArcKey = story.Arc.Key, StorySignature = "old" };
        Assert.Throws<ArgumentException>(() => saved.Restore(story));
        saved.StorySignature = BronzeRunSave.Signature(story.Arc); saved.Phase = (ArcadeQuestPhase)99;
        Assert.Throws<ArgumentException>(() => saved.Restore(story));
    }

    [Fact]
    public void EncountersAccumulateAndReadingMemoriesSurviveReingestedIds()
    {
        var chronicle = new BronzeChronicle(); chronicle.RecordDefeat(BronzeEnemyKind.Hydra, 1); chronicle.RecordDefeat(BronzeEnemyKind.Hydra, 2);
        var verse = new BronzeRecoveredVerse("story", "meaning", Story().Sources[0][0]);
        chronicle.RememberVerse(new[] { BronzeEnemyKind.Hydra }, verse);
        chronicle.RememberVerse(new[] { BronzeEnemyKind.Hydra }, verse with { Passage = verse.Passage with { NodeId = 999 } });
        var entry = Assert.Single(chronicle.Bestiary); Assert.Equal(3, entry.Defeats); Assert.Single(entry.Verses);
        chronicle.Run = null; Assert.Single(entry.Verses);
    }

    [Fact]
    public void ACompletedRunEarnsOnlyOneLaurel()
    {
        var chronicle = new BronzeChronicle(); var trophy = new BronzeTrophy(Guid.NewGuid(), "test", "Test", "Hero", 100,
            DateTimeOffset.UtcNow, "premise", "payoff", new(), new());
        chronicle.Crown(trophy); chronicle.Crown(trophy); Assert.Single(chronicle.Trophies);
    }
}

public sealed class BronzeSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bronze-save-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public void RoundTripPreservesCollectionsPreferencesAndCheckpoint()
    {
        var store = new BronzeSaveStore(null, _directory); var chronicle = new BronzeChronicle { Sound = false, Scanlines = false,
            Run = new BronzeRunSave { ArcKey = "journey", Gifts = new() { BronzeGiftId.Athena }, Phase = ArcadeQuestPhase.Hunt } };
        chronicle.RecordDefeat(BronzeEnemyKind.Cyclops, 3); store.Save(chronicle);
        var loaded = store.Load(); Assert.False(loaded.Sound); Assert.False(loaded.Scanlines);
        Assert.Equal(BronzeGiftId.Athena, Assert.Single(loaded.Run!.Gifts)); Assert.Equal(3, Assert.Single(loaded.Bestiary).Defeats);
    }

    [Fact]
    public void DamagedPrimaryRecoversBackupAndDoesNotReplaceGoodBackupWithBadData()
    {
        var store = new BronzeSaveStore(null, _directory); store.Save(new BronzeChronicle { Sound = false }); store.Save(new BronzeChronicle());
        File.WriteAllText(store.FilePath, "broken"); var loaded = store.Load(); Assert.True(store.RecoveredBackup); Assert.False(loaded.Sound);
        store.Save(loaded); Assert.Contains("\"Sound\": false", File.ReadAllText(store.FilePath + ".bak"));
        Assert.False(store.Load().Sound);
    }

    [Fact]
    public void UnreadableAndFutureSavesArePreserved()
    {
        var store = new BronzeSaveStore(null, _directory); Directory.CreateDirectory(_directory); File.WriteAllText(store.FilePath, "broken");
        Assert.Throws<InvalidDataException>(() => store.Load()); Assert.Equal("broken", File.ReadAllText(store.FilePath));
        File.WriteAllText(store.FilePath, JsonSerializer.Serialize(new BronzeChronicle { Version = 99 }));
        Assert.Throws<NotSupportedException>(() => store.Load()); Assert.Contains("99", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void LibrariesHaveSeparateSavesAndNoTemporaryFileIsLeft()
    {
        var a = new BronzeSaveStore(Path.Combine(_directory, "a.db"), _directory);
        var b = new BronzeSaveStore(Path.Combine(_directory, "b.db"), _directory);
        Assert.NotEqual(a.FilePath, b.FilePath); a.Save(new BronzeChronicle { Sound = false }); Assert.True(b.Load().Sound);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void MalformedCollectionIsRejectedBeforeItCanCrashTheBestiary()
    {
        var store = new BronzeSaveStore(null, _directory); Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "{\"Version\":1,\"Trophies\":[null]}");
        Assert.Throws<InvalidDataException>(() => store.Load());
        Assert.Contains("null", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void AFailedWriteReportsFailureWithoutErasingTheCheckpoint()
    {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "not-a-directory"); File.WriteAllText(blocked, "keep");
        var store = new BronzeSaveStore(null, blocked);
        Assert.Throws<IOException>(() => store.Save(new BronzeChronicle()));
        Assert.Equal("keep", File.ReadAllText(blocked));
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
