using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using ClassicaCodex.Core;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.Arcade;
using ClassicaCodex.Mac.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// Bronze and Thunder, the hidden arcade, played against the sample library:
/// a story is chosen, a battle is won for its clue, a wrong line is turned
/// away and the right one - found through the oracle and submitted from the
/// reader with the reader's own chord - advances it; the checkpoint is saved,
/// and a reopened game resumes from it.
///
/// Every test saves into a scratch folder of its own, never the real one,
/// and the library is only read: the arcade opens it read-only.
///
/// Battles are fought by the test rather than by a player. The arena is the
/// real one and so is every frame of it; the test simply strikes each
/// creature down as it appears, by setting its health to nothing, and lets
/// the arena count the kills, spawn the guardian and declare the victory.
/// </summary>
public class ArcadeTests
{
    private static string NewSaveFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "classicacodex-arcade-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Forget(string saves)
    {
        try { Directory.Delete(saves, recursive: true); }
        catch (IOException) { }
    }

    private static async Task<MainWindow?> OpenLibraryAsync()
    {
        if (Screens.SampleLibrary is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        var main = new MainWindow { Width = 1280, Height = 820 };
        main.Show();
        await Screens.Settle(10);
        return main;
    }

    private static async Task<BronzeArcadeWindow> OpenArcadeAsync(MainWindow main, string saves, bool manualClock = true)
    {
        ArcadeFeature.SaveDirectory = saves;
        ArcadeFeature.Open(main);
        Assert.True(ArcadeFeature.IsOpen);
        var arcade = ArcadeFeature.Window!;
        arcade.ManualClock = manualClock;
        await WaitUntilAsync(() => arcade.Ready.IsCompleted && !arcade.IsBusy);
        await Screens.Settle(4);
        return arcade;
    }

    private static async Task<T> WaitForAsync<T>(Func<T?> probe, int rounds = 150) where T : class
    {
        for (var i = 0; i < rounds; i++)
        {
            if (probe() is { } found) return found;
            await Screens.Settle(1);
        }

        throw new TimeoutException($"Nothing appeared: {typeof(T).Name}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int rounds = 150)
    {
        for (var i = 0; i < rounds; i++)
        {
            if (condition()) return;
            await Screens.Settle(1);
        }

        throw new TimeoutException("The condition never came true.");
    }

    private static void Click(ArcadeAction action)
    {
        Assert.True(action.IsVisible, $"{action.Label} is hidden");
        Assert.True(action.IsEnabled, $"{action.Label} is disabled");
        action.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyVisible && button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task InBothAppearancesAsync(TopLevel window, string name)
    {
        try
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Screens.Settle(4);
            Screens.Save(window, name + "-light");

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            await Screens.Settle(4);
            Screens.Save(window, name + "-dark");
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Screens.Settle(2);
        }
    }

    /// <summary>Strikes down every creature as it appears until the battle is decided.</summary>
    private static void WinTheBattle(BronzeArcadeWindow arcade)
    {
        var arena = arcade.Arena!;
        for (var i = 0; i < 1000 && arena.State == BronzeBattleState.Fighting; i++)
        {
            foreach (var enemy in arena.Enemies) enemy.Health = 0;
            arcade.Advance(0.1);
        }

        Assert.Equal(BronzeBattleState.Won, arena.State);
    }

    /// <summary>Another line of the same edition as <paramref name="nodeId"/> - the line a careless player selects.</summary>
    private static long NeighbouringLine(string library, long nodeId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = library, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT n.TextNodeId FROM TextNodes n
            WHERE n.EditionId = (SELECT EditionId FROM TextNodes WHERE TextNodeId = @id) AND n.TextNodeId <> @id
            AND COALESCE(n.NodeKind,'line')='line' AND trim(n.Text)<>''
            ORDER BY abs(n.TextNodeId - @id) LIMIT 1";
        command.Parameters.AddWithValue("@id", nodeId);
        return (long)command.ExecuteScalar()!;
    }

    [AvaloniaFact]
    public async Task A_story_starts_a_won_battle_gives_its_clue_and_the_right_line_from_the_reader_advances_it()
    {
        if (await OpenLibraryAsync() is not { } main) return;
        var library = DbConnectionFactory.DatabasePath!;
        var saves = NewSaveFolder();

        try
        {
            var arcade = await OpenArcadeAsync(main, saves);

            // A story was chosen from those this library can finish: the
            // sample has Homer and Aeschylus but no Hesiod, so the two
            // Homer-only stories are offered and Prometheus is not.
            var keys = arcade.AvailableStories.Select(s => s.Arc.Key).ToList();
            Assert.Contains("achilles-pity", keys);
            Assert.Contains("odysseus-home", keys);
            Assert.DoesNotContain("prometheus-fire", keys);

            var quest = arcade.Quest!;
            Assert.Equal(ArcadeQuestPhase.Introduction, quest.Phase);
            Assert.StartsWith(quest.Story.Arc.Title.ToUpperInvariant(), arcade.StoryText);
            Assert.Contains(quest.Story.Arc.Premise, arcade.StoryText);
            Assert.Contains("press ⌃⇧Return to submit it", arcade.StoryText);
            Assert.Equal("Begin adventure", arcade.StartAction.Label);
            Assert.False(arcade.SubmitAction.IsVisible);

            // The checkpoint lives in the scratch folder, keyed to this library.
            Assert.StartsWith(saves, arcade.SaveFile);
            Assert.True(File.Exists(arcade.SaveFile));
            Assert.Contains("Saved", arcade.SaveStatus);

            await InBothAppearancesAsync(arcade, "arcade-01-title");

            // ---- The battle ----
            Click(arcade.StartAction);
            Assert.Equal(ArcadeQuestPhase.Battle, quest.Phase);
            Assert.Equal(1, arcade.Arena!.Level);
            Assert.StartsWith($"{quest.Story.Arc.Title.ToUpperInvariant()} — CHAPTER 1", arcade.StoryText);
            Assert.False(arcade.StartAction.IsVisible);

            for (var i = 0; i < 30; i++) arcade.Advance(0.1);
            Assert.NotEmpty(arcade.Arena.Enemies);

            // The game's keys reach the arena: D walks right, K throws.
            var start = arcade.Arena.Player;
            arcade.KeyPress(Key.D, RawInputModifiers.None, PhysicalKey.D, "d");
            arcade.KeyPress(Key.K, RawInputModifiers.None, PhysicalKey.K, "k");
            Assert.Contains(Key.D, arcade.HeldKeys);
            arcade.Advance(0.05);
            arcade.Advance(0.05);
            Assert.True(arcade.Arena.Player.X > start.X);
            Assert.Contains(arcade.Arena.Shots, s => !s.Hostile);
            await InBothAppearancesAsync(arcade, "arcade-02-battle");
            arcade.KeyRelease(Key.D, RawInputModifiers.None, PhysicalKey.D, "d");
            arcade.KeyRelease(Key.K, RawInputModifiers.None, PhysicalKey.K, "k");

            // Escape pauses, and the start button offers to resume.
            arcade.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(arcade.IsPaused);
            Assert.True(arcade.StartAction.IsVisible);
            Assert.Equal("Resume", arcade.StartAction.Label);
            await Screens.Settle(2);
            Screens.Save(arcade, "arcade-03-paused-light");
            arcade.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(arcade.IsPaused);

            WinTheBattle(arcade);

            // ---- The clue ----
            Assert.Equal(ArcadeQuestPhase.Hunt, quest.Phase);
            Assert.StartsWith($"VERSE 1 / {quest.Story.Arc.Passages.Length} — THE ORACLE'S CLUE", arcade.StoryText);
            Assert.Contains(quest.Clue.Award, arcade.StoryText);
            Assert.True(arcade.SubmitAction.IsVisible);
            Assert.Equal("Oracle hint", arcade.HintAction.Label);
            Assert.False(arcade.StartAction.IsVisible);

            // The chapter's guardian is in the bestiary now.
            Assert.Contains(arcade.Chronicle.Bestiary, d => d.Kind == BronzeEnemyKind.Cyclops && d.Defeats >= 1);
            await InBothAppearancesAsync(arcade, "arcade-04-clue");

            // ---- A wrong line, submitted from the reader with its chord ----
            var source = quest.Story.Sources[0][0];
            var wrong = NeighbouringLine(library, source.NodeId);
            await main.NavigateToPassageAtTopAsync(source.WorkId, wrong);
            await Screens.Settle(4);
            Assert.Equal(wrong, main.SelectedPassageId);

            main.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null);
            await WaitUntilAsync(() => !arcade.IsBusy && arcade.StoryText.Contains("That is not the passage yet."));
            Assert.Equal(ArcadeQuestPhase.Hunt, quest.Phase);
            Assert.Empty(quest.Found);

            // ---- The oracle: the work, then the citation, then the line itself ----
            Click(arcade.HintAction);
            Assert.Equal(1, arcade.HintLevel);
            Assert.Contains($"ORACLE: {source.Author} — {source.Title}", arcade.StoryText);
            Assert.Equal("Reveal citation", arcade.HintAction.Label);

            Click(arcade.HintAction);
            Assert.Contains($"ORACLE: {source.Author} — {source.Title}, {PassageCitation.Display(source.Citation)}", arcade.StoryText);
            Assert.Equal("Highlight passage", arcade.HintAction.Label);

            Click(arcade.HintAction);
            await WaitUntilAsync(() => !arcade.IsBusy);
            await Screens.Settle(4);
            var selected = main.SelectedPassageId;
            Assert.NotNull(selected);
            var row = new ArcadeQuestRepository(library).GetPassage(selected!.Value)!;
            Assert.True(ArcadeQuest.MatchesAddress(quest.Clue, row));
            Screens.Save(main, "arcade-05-reader-highlights-the-clue");

            // ---- The right line ----
            ArcadeFeature.SubmitReaderSelection();
            await WaitUntilAsync(() => !arcade.IsBusy && quest.Phase != ArcadeQuestPhase.Hunt);
            Assert.Equal(ArcadeQuestPhase.Revelation, quest.Phase);
            Assert.Single(quest.Found);
            Assert.StartsWith($"FOUND: {row.Author} — {row.Title} {PassageCitation.Display(row.Citation)}", arcade.StoryText);
            Assert.Contains(quest.Story.Arc.Passages[0].Reveal, arcade.StoryText);
            Assert.Contains("A shrine opens.", arcade.StoryText);
            Assert.True(arcade.GiftChoice.IsVisible);
            Assert.False(arcade.StartAction.IsVisible);
            Assert.True(arcade.JournalAction.IsVisible);

            var journal = BronzeArcadeWindow.JournalText(quest);
            Assert.Contains(row.Text, journal);
            Assert.Contains(quest.Story.Arc.Passages[0].Reveal, journal);
            await InBothAppearancesAsync(arcade, "arcade-06-revelation");

            // The checkpoint on disk: the verse kept by its address, not its id.
            var saved = new BronzeSaveStore(library, saves).Load();
            Assert.Equal(quest.Story.Arc.Key, saved.Run!.ArcKey);
            Assert.Equal(ArcadeQuestPhase.Revelation, saved.Run.Phase);
            Assert.Single(saved.Run.Found);
            Assert.Equal(0, saved.Run.Found[0].NodeId);
            Assert.Equal(row.Citation, saved.Run.Found[0].Citation);
            Assert.Contains(saved.Bestiary, d => d.Kind == BronzeEnemyKind.Cyclops && d.Verses.Count == 1);

            // ---- The shrine ----
            var choosing = arcade.ChooseGiftAsync();
            var shrine = await WaitForAsync(() => arcade.OwnedWindows.OfType<BronzeGiftWindow>().FirstOrDefault());
            Assert.Equal(3, shrine.Offers.Count);
            Assert.All(shrine.CardTexts, t => Assert.Contains("Mythic source:", t));
            await InBothAppearancesAsync(shrine, "arcade-07-gift");
            var offered = shrine.Offers[0];
            shrine.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
            await choosing;

            Assert.Equal(new[] { offered.Id }, arcade.Gifts);
            Assert.Contains("Gift accepted: " + offered.Name, arcade.StoryText);
            Assert.False(arcade.GiftChoice.IsVisible);
            Assert.Equal("Next chapter", arcade.StartAction.Label);

            // ---- Closed and reopened: the fates kept the place ----
            arcade.Close();
            await Screens.Settle(2);
            Assert.False(ArcadeFeature.IsOpen);

            var resumed = await OpenArcadeAsync(main, saves);
            Assert.NotSame(arcade, resumed);
            Assert.Equal(quest.Story.Arc.Key, resumed.Quest!.Story.Arc.Key);
            Assert.Equal(ArcadeQuestPhase.Revelation, resumed.Quest.Phase);
            Assert.Single(resumed.Quest.Found);
            Assert.Equal(new[] { offered.Id }, resumed.Gifts);
            Assert.StartsWith("FOUND:", resumed.StoryText);
            Assert.Contains("Gift accepted: " + offered.Name, resumed.StoryText);
            Assert.Equal("Next chapter", resumed.StartAction.Label);

            Click(resumed.StartAction);
            Assert.Equal(ArcadeQuestPhase.Battle, resumed.Quest.Phase);
            Assert.Equal(1, resumed.Quest.Chapter);
            Assert.Equal(2, resumed.Arena!.Level);
            Assert.Contains("DIVINE GIFTS", resumed.StoryText);
            Assert.Contains(offered.Id, resumed.Arena.Gifts);

            resumed.Close();
        }
        finally
        {
            ArcadeFeature.Window?.Close();
            main.Close();
            ArcadeFeature.SaveDirectory = null;
            Forget(saves);
        }
    }

    [AvaloniaFact]
    public async Task The_chronicle_shows_what_was_met_and_finished_and_reopens_a_verse_in_the_reader()
    {
        if (await OpenLibraryAsync() is not { } main) return;
        var library = DbConnectionFactory.DatabasePath!;
        var saves = NewSaveFolder();

        try
        {
            // A chronicle from earlier adventures: a finished Iliad story, a
            // serpent-hunter's tally and one guardian met with a verse.
            var story = new ArcadeQuestRepository(library).Load().Single(s => s.Arc.Key == "achilles-pity");
            var verses = story.Arc.Passages.Select((p, i) => new BronzeRecoveredVerse(story.Arc.Title, p.Reveal, story.Sources[i][0])).ToList();
            var chronicle = new BronzeChronicle();
            chronicle.RecordDefeat(BronzeEnemyKind.Serpent, 12);
            chronicle.RecordDefeat(BronzeEnemyKind.Cyclops, 3);
            chronicle.Bestiary.Single(d => d.Kind == BronzeEnemyKind.Cyclops).Verses.Add(verses[0]);
            var gifts = new List<BronzeGiftId> { BronzeGiftId.Hephaestus, BronzeGiftId.Apollo };
            chronicle.Crown(new BronzeTrophy(Guid.NewGuid(), story.Arc.Key, story.Arc.Title, BronzeGifts.Epithet(gifts), 23450,
                DateTimeOffset.Now, story.Arc.Premise, story.Arc.Payoff, verses, gifts));
            new BronzeSaveStore(library, saves).Save(chronicle);

            var arcade = await OpenArcadeAsync(main, saves);
            Assert.Equal(1, arcade.Canvas.CompletedStories);
            Screens.Save(arcade, "arcade-08-title-with-a-star-light");

            var showing = arcade.ShowCollectionAsync();
            var collection = await WaitForAsync(() => arcade.OwnedWindows.OfType<BronzeCollectionWindow>().FirstOrDefault());
            await Screens.Settle(4);

            Assert.Equal(new[]
            {
                "Serpent  ·  12", "?  Undiscovered", "?  Undiscovered", "Cyclops  ·  3", "?  Undiscovered", "?  Undiscovered"
            }, collection.CreatureLabels);
            Assert.StartsWith("SERPENT\n", collection.Description);
            Assert.Contains("Apollodorus, Library 1.4.1", collection.Description);
            Assert.Contains("12 defeated across your adventures.", collection.Description);
            Assert.True(collection.AncientWitnesses.IsEnabled);
            await InBothAppearancesAsync(collection, "arcade-09-bestiary");

            // An undiscovered creature stays a shadow.
            collection.Creatures.SelectedIndex = (int)BronzeEnemyKind.Harpy;
            Assert.StartsWith("A SHADOW WITHOUT A NAME", collection.Description);
            Assert.False(collection.AncientWitnesses.IsEnabled);
            Assert.Equal("No encounter recorded yet.", collection.BestiaryMemories.Heading);

            // The Cyclops: its verse, and its witnesses from the library -
            // Homer's Odyssey is in the sample, Hesiod's Theogony is not.
            collection.Creatures.SelectedIndex = (int)BronzeEnemyKind.Cyclops;
            Assert.Equal("Verses you recovered after encounters with this creature:", collection.BestiaryMemories.Heading);
            Assert.Single(collection.BestiaryMemories.Items);
            Assert.Contains(verses[0].Meaning, collection.BestiaryMemories.Text);

            Click(collection.AncientWitnesses);
            var scholia = await WaitForAsync(() => collection.OwnedWindows.OfType<BronzeWitnessWindow>().FirstOrDefault());
            await Screens.Settle(4);
            Assert.Contains("Odysseus's borrowed name", scholia.NoteText);
            Assert.False(string.IsNullOrWhiteSpace(scholia.PassageText));
            Assert.Contains("Odyssey 9.366", scholia.CitationText);
            Assert.Contains("Stored citation:", scholia.CitationText);
            await InBothAppearancesAsync(scholia, "arcade-10-witness");
            scholia.Close();
            await WaitUntilAsync(() => collection.AncientWitnesses.IsEnabled);

            // The hall of laurels.
            collection.Tabs.SelectedIndex = 1;
            await Screens.Settle(4);
            Assert.Equal(new[] { "★ Wrath into Pity" }, collection.VictoryLabels);
            Assert.StartsWith("THE BRONZE DAWN\nWrath into Pity · 23,450 points", collection.InscriptionText);
            Assert.Contains("Gifts: The Living Bronze · The Silver Bow", collection.InscriptionText);
            Assert.Equal(5, collection.HallMemories.Items.Count);
            await InBothAppearancesAsync(collection, "arcade-11-laurels");

            // Reopening a verse puts the reader on it and brings the library forward.
            collection.HallMemories.Verses.SelectedIndex = 3;
            var remembered = collection.HallMemories.Items[3].Passage;
            Click(collection.HallMemories.OpenButton);
            await showing;
            await Screens.Settle(4);
            Assert.False(collection.IsVisible);
            var expected = new ArcadeQuestRepository(library).FindRememberedPassage(remembered)!;
            Assert.Equal(expected.NodeId, main.SelectedPassageId);

            arcade.Close();
        }
        finally
        {
            ArcadeFeature.Window?.Close();
            main.Close();
            ArcadeFeature.SaveDirectory = null;
            Forget(saves);
        }
    }

    [AvaloniaFact]
    public async Task A_battle_runs_on_the_display_frames_and_pauses_for_the_library()
    {
        if (await OpenLibraryAsync() is not { } main) return;
        var saves = NewSaveFolder();

        try
        {
            var arcade = await OpenArcadeAsync(main, saves, manualClock: false);
            Click(arcade.StartAction);
            var arena = arcade.Arena!;

            // Nobody advances this one by hand: the render loop does.
            await WaitUntilAsync(() => arena.Time > 0.3f);

            // F6, back to the library: the battle pauses while the player reads.
            arcade.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
            await Screens.Settle(2);
            Assert.True(arcade.IsPaused);
            var frozen = arena.Time;
            await Screens.Settle(10);
            Assert.Equal(frozen, arena.Time);

            // And resuming starts the frames again.
            arcade.Activate();
            Click(arcade.StartAction);
            Assert.False(arcade.IsPaused);
            await WaitUntilAsync(() => arena.Time > frozen + 0.2f);
            arcade.Close();
        }
        finally
        {
            ArcadeFeature.Window?.Close();
            main.Close();
            ArcadeFeature.SaveDirectory = null;
            Forget(saves);
        }
    }

    [AvaloniaFact]
    public async Task Sound_and_scanlines_are_remembered_between_openings()
    {
        if (await OpenLibraryAsync() is not { } main) return;
        var saves = NewSaveFolder();

        try
        {
            var arcade = await OpenArcadeAsync(main, saves);
            Assert.True(arcade.SoundEnabled);
            Assert.True(arcade.ScanlinesOn);
            Assert.Equal("Sound: on", arcade.SoundAction.Label);

            arcade.KeyPress(Key.M, RawInputModifiers.None, PhysicalKey.M, "m");
            arcade.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
            Assert.False(arcade.SoundEnabled);
            Assert.False(arcade.ScanlinesOn);
            Assert.Equal("Sound: off", arcade.SoundAction.Label);
            arcade.Close();
            await Screens.Settle(2);

            var reopened = await OpenArcadeAsync(main, saves);
            Assert.False(reopened.SoundEnabled);
            Assert.False(reopened.ScanlinesOn);
            Assert.Equal("Sound: off", reopened.SoundAction.Label);

            // Opening it again while it is open brings the same game forward.
            ArcadeFeature.Open(main);
            Assert.Same(reopened, ArcadeFeature.Window);

            // And the game does not outlive the reader.
            main.Close();
            await Screens.Settle(2);
            Assert.False(ArcadeFeature.IsOpen);
        }
        finally
        {
            ArcadeFeature.Window?.Close();
            main.Close();
            ArcadeFeature.SaveDirectory = null;
            Forget(saves);
        }
    }

    [AvaloniaFact]
    public async Task A_saved_story_this_library_cannot_finish_is_kept_safe_and_practice_still_plays()
    {
        if (await OpenLibraryAsync() is not { } main) return;
        var library = DbConnectionFactory.DatabasePath!;
        var saves = NewSaveFolder();

        try
        {
            // A run of the Prometheus story, which needs Hesiod - not in the sample.
            var prometheus = QuestArcs.All.Single(a => a.Key == "prometheus-fire");
            new BronzeSaveStore(library, saves).Save(new BronzeChronicle
            {
                Run = new BronzeRunSave
                {
                    ArcKey = prometheus.Key, StorySignature = BronzeRunSave.Signature(prometheus),
                    Seed = 7, Chapter = 0, Phase = ArcadeQuestPhase.Battle
                }
            });

            var arcade = await OpenArcadeAsync(main, saves);
            Assert.Null(arcade.Quest);
            Assert.StartsWith("YOUR ADVENTURE IS SAFE, BUT CANNOT RESUME HERE", arcade.StoryText);
            Assert.Contains("This library no longer contains every verse needed by your saved story.", arcade.StoryText);
            Assert.Equal("Practice arena", arcade.StartAction.Label);
            await InBothAppearancesAsync(arcade, "arcade-12-cannot-resume");

            // Practice: a real battle, no story, and the saved run untouched.
            Click(arcade.StartAction);
            Assert.StartsWith("PRACTICE — CHAPTER 1", arcade.StoryText);
            WinTheBattle(arcade);
            Assert.StartsWith("PRACTICE COMPLETE", arcade.StoryText);
            Assert.Equal("Practice arena", arcade.StartAction.Label);

            arcade.Close();
            await Screens.Settle(2);
            var saved = new BronzeSaveStore(library, saves).Load();
            Assert.Equal(prometheus.Key, saved.Run!.ArcKey);
            Assert.Equal(ArcadeQuestPhase.Battle, saved.Run.Phase);
            // Practice teaches the bestiary even though it earns no story.
            Assert.NotEmpty(saved.Bestiary);
        }
        finally
        {
            ArcadeFeature.Window?.Close();
            main.Close();
            ArcadeFeature.SaveDirectory = null;
            Forget(saves);
        }
    }
}
