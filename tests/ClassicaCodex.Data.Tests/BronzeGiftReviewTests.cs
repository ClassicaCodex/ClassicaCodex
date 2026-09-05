using System.Numerics;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

public class BronzeGiftReviewTests
{
    public static IEnumerable<object[]> GiftsAndChapters() =>
        Enum.GetValues<BronzeGiftId>().SelectMany(g => new[] { 2, 3, 4, 5 }.Select(c => new object[] { g, c }));

    [Theory]
    [MemberData(nameof(GiftsAndChapters))]
    public void EveryGiftAllowsItsChaptersMagicEvenWhileThrowing(BronzeGiftId gift, int chapter)
    {
        var arena = new BronzeArena(chapter, 123, new[] { gift });
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Javelin: true, Magic: true));
        Assert.Equal(1, arena.MagicCasts);
        Assert.Equal(gift == BronzeGiftId.Apollo ? 75 : 65, arena.Mana);
        Assert.Equal(gift == BronzeGiftId.Poseidon ? 3 : 1, arena.Shots.Count(s => !s.Magic));
        Assert.Equal(chapter < 4 ? 3 : 0, arena.Shots.Count(s => s.Magic));
        Assert.Equal(chapter < 4 ? "SACRED FIRE" : "THUNDER RING", arena.MagicFeedback);
        Assert.True(arena.MagicFeedbackTime > 0);
    }

    [Fact]
    public void PoseidonsVolleyAndFireBothReachAndDamageAnEnemy()
    {
        var arena = new BronzeArena(2, 1, new[] { BronzeGiftId.Poseidon });
        var target = new BronzeEnemy { Position = arena.Player + new Vector2(105, 0), Health = 100, MaxHealth = 100, Clock = 10 };
        arena.Enemies.Add(target);
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Javelin: true, Magic: true));
        Assert.Equal(3, arena.Shots.Count(s => s.SeaBlessed)); Assert.Equal(3, arena.Shots.Count(s => s.Magic));
        for (var i = 0; i < 50; i++) arena.Update(.01f, default);
        Assert.True(target.Health <= 16, "Both ranged attacks should hit, not just the trident.");
    }

    [Fact]
    public void LowManaAndLockedMagicGiveFeedbackWithoutPretendingToCast()
    {
        var first = new BronzeArena(1, 1); first.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true));
        Assert.Equal(0, first.MagicCasts); Assert.Contains("CHAPTER II", first.MagicFeedback);
        var second = new BronzeArena(2, 1);
        for (var frame = 0; frame < 180; frame++) second.Update(1f / 60, new BronzeInput(Vector2.Zero, Magic: true));
        second.Update(.01f, default);
        var casts = second.MagicCasts;
        second.Update(.01f, new BronzeInput(Vector2.Zero, Magic: true));
        Assert.Equal(casts, second.MagicCasts); Assert.Equal("NEED 35 MANA", second.MagicFeedback);
    }

    [Fact]
    public void AllGiftsTogetherKeepTheThunderRingAndTridentFunctional()
    {
        var arena = new BronzeArena(5, 1, Enum.GetValues<BronzeGiftId>());
        var target = new BronzeEnemy { Position = arena.Player + new Vector2(60, 0), Health = 100, MaxHealth = 100, Clock = 10 };
        arena.Enemies.Add(target); arena.Shots.Add(new BronzeShot { Position = arena.Player + new Vector2(30, 0), Hostile = true });
        arena.Update(.01f, new BronzeInput(Vector2.Zero, Javelin: true, Magic: true));
        Assert.Equal(35, target.Health); Assert.DoesNotContain(arena.Shots, s => s.Hostile);
        Assert.Equal(3, arena.Shots.Count); Assert.Equal(75, arena.Mana); Assert.Equal(195, arena.MaxHealth);
    }
}

[Collection("Database")]
public class BronzeWitnessTests
{
    [Fact]
    public async Task WitnessesComeFromMatchingWorksAndExactCitationsWithOriginalFirst()
    {
        using var db = await TempDatabase.CreateAsync();
        var english = await db.SeedFullEditionAsync("witness", "Homer", "greekLit", "Odyssey", "Translation", "eng");
        var greek = await db.SeedSiblingEditionAsync("witness", "witness-grc", "Original", "grc");
        await db.InsertLinesAsync(english, ("9.366", "English witness"));
        await db.InsertLinesAsync(greek, ("urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.9.366", "Greek witness"), ("19.366", "Wrong suffix"));
        var wrong = await db.SeedFullEditionAsync("wrong", "Homer", "greekLit", "Iliad", "Original", "grc");
        await db.InsertLinesAsync(wrong, ("9.366", "Wrong work"));
        var result = Assert.Single(new ArcadeQuestRepository(db.Path).LoadWitnesses(BronzeEnemyKind.Cyclops));
        Assert.Equal(2, result.Editions.Count); Assert.Equal("grc", result.Editions[0].Language);
        Assert.DoesNotContain(result.Editions, e => e.Text.StartsWith("Wrong"));
    }

    [Fact]
    public async Task MissingWitnessesStayMissingAndCancellationIsHonored()
    {
        using var db = await TempDatabase.CreateAsync(); var repository = new ArcadeQuestRepository(db.Path);
        Assert.Empty(repository.LoadWitnesses(BronzeEnemyKind.Hydra));
        Assert.Throws<OperationCanceledException>(() => repository.LoadWitnesses(BronzeEnemyKind.Hydra, new CancellationToken(true)));
    }

    [Fact]
    public async Task ProseWitnessUsesFirstChildParagraphOncePerEditionWithoutCrossingSectionBoundaries()
    {
        using var db = await TempDatabase.CreateAsync();
        var edition = await db.SeedFullEditionAsync("hydra", "Apollodorus", "greekLit", "Library", "Original", "grc");
        await db.InsertLinesAsync(edition,
            ("urn:cts:greekLit:tlg0548.tlg001.perseus-grc1.2.5.2.2", "Second paragraph"),
            ("urn:cts:greekLit:tlg0548.tlg001.perseus-grc1.2.5.2.1", "Beginning of the Hydra section"),
            ("urn:cts:greekLit:tlg0548.tlg001.perseus-grc1.2.5.20.1", "Different section"));
        var witness = Assert.Single(new ArcadeQuestRepository(db.Path).LoadWitnesses(BronzeEnemyKind.Hydra));
        Assert.Equal("Beginning of the Hydra section", Assert.Single(witness.Editions).Text);
        // This section lookup must not loosen the adventure's exact citation gate.
        var clue = new QuestPassage("Apollodorus", new[] { "Library" }, "2.5.2", "clue", "meaning");
        Assert.False(ArcadeQuest.MatchesAddress(clue, witness.Editions[0]));
    }
}
