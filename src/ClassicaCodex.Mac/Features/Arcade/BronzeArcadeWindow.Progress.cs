using Avalonia.Controls;
using Avalonia.Input;
using ClassicaCodex.Core;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// What the arcade keeps between openings, and the rewards along the way:
/// the checkpoint and the chronicle (bestiary, laurels, the sound and
/// scanline switches), the divine gifts, the hall of recovered verses. All of
/// it is written by BronzeSaveStore - one atomic file per library, beside the
/// other application data, never inside the library itself - exactly as the
/// Windows game writes it, so the file is the same file on either machine.
/// </summary>
internal sealed partial class BronzeArcadeWindow
{
    private BronzeSaveStore? _saveStore;
    private BronzeChronicle _chronicle = new();
    private readonly Func<int, long, Task>? _openPassage;
    private readonly Button _giftButton;
    private readonly ArcadeAction _collectionButton;
    private readonly TextBlock _saveStatus;
    private readonly List<BronzeGiftId> _gifts = new();
    private readonly HashSet<BronzeEnemyKind> _chapterFoes = new();
    private readonly Dictionary<BronzeEnemyKind, int> _recordedDefeats = new();
    private readonly TaskCompletionSource _ready = new();
    private Guid _runId = Guid.NewGuid();
    private int _runSeed, _runScore;
    private bool _profileLoaded, _preserveSavedRun;
    private double _lastAutoSave;
    private bool NeedsGift => _quest?.Phase == ArcadeQuestPhase.Revelation && _gifts.Count < _quest.Found.Count;

    private void LoadChronicle()
    {
        if (_profileLoaded) return;
        _profileLoaded = true;
        try
        {
            _chronicle = _saveStore!.Load();
            _saveStatus.Text = _saveStore.RecoveredBackup ? "Recovered your previous checkpoint from its backup." : "Chapter checkpoints save automatically.";
        }
        catch (Exception ex)
        {
            _saveStore = null;
            _saveStatus.Text = "Saving unavailable: " + ex.Message;
        }
        _audio.Enabled = _chronicle.Sound;
        UpdateSoundButton();
        _canvas.Scanlines = _chronicle.Scanlines;
        UpdateStars();
    }

    private bool TryRestoreAdventure()
    {
        if (_chronicle.Run is not { } saved) return false;
        var story = _available.FirstOrDefault(s => s.Arc.Key == saved.ArcKey);
        try
        {
            if (story == null) throw new InvalidOperationException("This library no longer contains every verse needed by your saved story.");
            _quest = saved.Restore(story);
            _runId = saved.RunId; _runSeed = saved.Seed; _runScore = saved.Score;
            _gifts.Clear(); _gifts.AddRange(saved.Gifts);
            _chapterFoes.Clear(); _chapterFoes.UnionWith(saved.ChapterFoes);
            _hintLevel = saved.HintLevel; _preserveSavedRun = false;
            if (_quest.Phase == ArcadeQuestPhase.Hunt) ShowClue();
            else if (_quest.Phase is ArcadeQuestPhase.Revelation or ArcadeQuestPhase.Complete) ShowRevelation();
            else
            {
                _canvas.Subtitle = "THE FATES KEPT YOUR PLACE";
                Story($"WELCOME BACK — {_quest.Story.Arc.Title}\n\n{_quest.Story.Arc.Premise}\n\n"
                    + $"Chapter {_quest.Chapter + 1}. {_quest.Found.Count} verses recovered. {_gifts.Count} divine gifts.\n\n"
                    + "Your battle resumes from the beginning of the chapter with full health. Recovered verses and gifts are safe.\n\n" + ControlsHelp);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _quest = null; _preserveSavedRun = true;
            Story("YOUR ADVENTURE IS SAFE, BUT CANNOT RESUME HERE\n\n" + ex.Message
                + "\n\nRestore the missing texts and reopen the game, or choose New adventure to replace this run. Your bestiary and laurels remain. Practice is available meanwhile.");
        }
        return true;
    }

    private void SaveProgress()
    {
        if (!_profileLoaded || _saveStore == null || _closed) return;
        RecordEncounters();
        if (_quest != null && !_preserveSavedRun)
            _chronicle.Run = new BronzeRunSave
            {
                RunId = _runId, ArcKey = _quest.Story.Arc.Key, StorySignature = BronzeRunSave.Signature(_quest.Story.Arc),
                Seed = _runSeed, Chapter = _quest.Chapter, Phase = _quest.Phase,
                // Numeric ids belong to this library as it is today; a
                // re-ingest renumbers every passage, so the save keeps the
                // address and the words and finds the row again by those.
                Found = _quest.Found.Select(p => p with { NodeId = 0, WorkId = 0 }).ToList(),
                Gifts = _gifts.ToList(), ChapterFoes = _chapterFoes.ToList(), Score = _runScore, HintLevel = _hintLevel
            };
        _chronicle.Sound = _audio.Enabled; _chronicle.Scanlines = _canvas.Scanlines;
        try
        {
            _saveStore.Save(_chronicle);
            _saveStatus.Text = "Saved · Battles resume at the chapter's start; verses, gifts and discoveries are kept.";
        }
        catch (Exception ex) { _saveStatus.Text = "Could not save this checkpoint: " + ex.Message; }
    }

    private void RecordEncounters()
    {
        if (_arena == null) return;
        foreach (var (kind, count) in _arena.DefeatCounts)
        {
            var delta = count - _recordedDefeats.GetValueOrDefault(kind);
            if (delta <= 0) continue;
            _chronicle.RecordDefeat(kind, delta); _recordedDefeats[kind] = count;
            // Practice teaches the bestiary but earns nothing in a story.
            if (!_practice) _chapterFoes.Add(kind);
        }
    }

    private async Task NewAdventureAsync()
    {
        if (_busy) return;
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        if (_chronicle.Run is { Phase: not ArcadeQuestPhase.Complete }
            && !await Alert.Confirm(this, "A new thread of fate",
                "Begin a new adventure and replace this run's checkpoint? Your bestiary and earned laurels will remain.",
                "New Adventure", "Keep This Run")) return;
        if (_closed) return;
        _chronicle.Run = null; _preserveSavedRun = false;
        await LoadStoriesAsync(true);
    }

    internal async Task ChooseGiftAsync()
    {
        if (_busy || !NeedsGift || _quest == null) return;
        var shrine = new BronzeGiftWindow(BronzeGifts.Offer(_gifts, _runSeed, _quest.Chapter));
        if (!await shrine.ShowDialog<bool>(this) || shrine.SelectedGift is not { } gift || _closed) return;
        _gifts.Add(gift); _audio.Play(3);
        ShowRevelation(); RefreshButtons(); SaveProgress();
    }

    private void ShowRevelation()
    {
        if (_quest == null || _quest.Found.Count == 0) return;
        var row = _quest.Found.Last();
        var complete = _quest.Phase == ArcadeQuestPhase.Complete;
        _canvas.Arena = null; _canvas.Paused = false;
        _canvas.Banner = complete ? "A LAUREL IS YOURS" : "WORDS RECOVERED";
        _canvas.Subtitle = complete ? BronzeGifts.Epithet(_gifts).ToUpperInvariant() : "THE GODS HAVE TAKEN NOTICE";
        Story($"FOUND: {row.Author} — {row.Title} {PassageCitation.Display(row.Citation)}\n\n{row.Text}\n\n{_quest.Clue.Reveal}\n\n"
            + (complete ? _quest.Story.Arc.Payoff + $"\n\n{BronzeGifts.Epithet(_gifts)} — {_runScore:N0} points. Your laurel and this story now live in Bestiary & laurels."
                : NeedsGift ? "A shrine opens. Choose one of three divine gifts before the next chapter. Every gift you accept stays with you for the rest of this adventure."
                : "Gift accepted: " + BronzeGifts.Get(_gifts.Last()).Name + ".\n" + BronzeGifts.Get(_gifts.Last()).Effect + "\n\nThe next chapter awaits."));
        UpdateStars();
    }

    private void AwardLaurel()
    {
        if (_quest == null) return;
        var arc = _quest.Story.Arc;
        _chronicle.Crown(new BronzeTrophy(_runId, arc.Key, arc.Title, BronzeGifts.Epithet(_gifts), _runScore,
            DateTimeOffset.Now, arc.Premise, arc.Payoff,
            _quest.Found.Select((p, i) => new BronzeRecoveredVerse(arc.Title, arc.Passages[i].Reveal, p)).ToList(), _gifts.ToList()));
    }

    private void UpdateStars() => _canvas.CompletedStories = _chronicle.Trophies.Select(t => t.ArcKey).Distinct().Count();

    internal async Task ShowCollectionAsync()
    {
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        SaveProgress();
        var collection = new BronzeCollectionWindow(_chronicle, ReopenRecoveredVerse, LoadAncientWitnesses);
        if (await collection.ShowDialog<bool>(this) && !_closed) ReturnToLibrary();
    }

    private async Task ReopenRecoveredVerse(ArcadePassage remembered)
    {
        if (_repository == null || !SameLibrary()) throw new InvalidOperationException("Reopen the arcade in the matching library to revisit this verse.");
        var repository = _repository;
        var row = await Task.Run(() => repository.FindRememberedPassage(remembered), _lifetime.Token);
        if (row == null) throw new InvalidOperationException("That edition or verse is no longer present. Its recovered text remains in your chronicle.");
        if (_openPassage != null) await _openPassage(row.WorkId, row.NodeId);
        else await _openWork(row.WorkId);
    }

    private Task<List<BronzeWitness>> LoadAncientWitnesses(BronzeEnemyKind creature, CancellationToken cancellationToken)
    {
        if (_repository == null || !SameLibrary())
            throw new InvalidOperationException("Open the arcade with the matching library to read its ancient witnesses.");
        var repository = _repository;
        return Task.Run(() => repository.LoadWitnesses(creature, cancellationToken), cancellationToken);
    }

    // ---- What the tests look at ------------------------------------------------

    /// <summary>Finishes when the first loading of the library's stories has.</summary>
    internal Task Ready => _ready.Task;

    internal bool IsBusy => _busy;
    internal ArcadeQuest? Quest => _quest;
    internal BronzeArena? Arena => _arena;
    internal bool IsPaused => _paused;
    internal IReadOnlyCollection<Key> HeldKeys => _keys;
    internal int HintLevel => _hintLevel;
    internal IReadOnlyList<ArcadeStory> AvailableStories => _available;
    internal IReadOnlyList<BronzeGiftId> Gifts => _gifts;
    internal BronzeChronicle Chronicle => _chronicle;
    internal string? SaveFile => _saveStore?.FilePath;
    internal string StoryText => _story.Story;
    internal string SaveStatus => _saveStatus.Text ?? string.Empty;
    internal bool SoundEnabled => _audio.Enabled;
    internal bool ScanlinesOn => _canvas.Scanlines;
    internal BronzeArcadeCanvas Canvas => _canvas;
    internal ArcadeAction StartAction => _start;
    internal ArcadeAction SubmitAction => _submit;
    internal ArcadeAction HintAction => _hint;
    internal ArcadeAction JournalAction => _journal;
    internal ArcadeAction SoundAction => _sound;
    internal ArcadeAction CollectionAction => _collectionButton;
    internal Button GiftChoice => _giftButton;

    /// <summary>
    /// When set, the battle moves only when <see cref="Advance"/> moves it:
    /// the tests' clock in place of the display's, so a test that photographs
    /// a battle is not racing the render loop while it does.
    /// </summary>
    internal bool ManualClock { get; set; }

    /// <summary>Moves the battle on by <paramref name="seconds"/>, as that many seconds of frames would.</summary>
    internal void Advance(double seconds) => TickArena(_lastTick + seconds);
}
