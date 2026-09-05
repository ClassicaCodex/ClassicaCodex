using ClassicaCodex.Core;
using ClassicaCodex.Data;

namespace ClassicaCodex.UI;

public sealed partial class BronzeArcadeForm
{
    private BronzeSaveStore? _saveStore;
    private BronzeChronicle _chronicle = new();
    private readonly Func<int, long, Task>? _openPassage;
    private readonly Button _giftButton, _collectionButton;
    private readonly Label _saveStatus = new() { AutoSize = true, ForeColor = Color.FromArgb(102, 240, 216),
        Margin = new Padding(10, 12, 8, 4), MaximumSize = new Size(900, 0) };
    private readonly List<BronzeGiftId> _gifts = new();
    private readonly HashSet<BronzeEnemyKind> _chapterFoes = new();
    private readonly Dictionary<BronzeEnemyKind, int> _recordedDefeats = new();
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
                _story.Text = $"WELCOME BACK — {_quest.Story.Arc.Title}\n\n{_quest.Story.Arc.Premise}\n\n"
                    + $"Chapter {_quest.Chapter + 1}. {_quest.Found.Count} verses recovered. {_gifts.Count} divine gifts.\n\n"
                    + "Your battle resumes from the beginning of the chapter with full health. Recovered verses and gifts are safe.\n\n" + ControlsHelp;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _quest = null; _preserveSavedRun = true;
            _story.Text = "YOUR ADVENTURE IS SAFE, BUT CANNOT RESUME HERE\n\n" + ex.Message
                + "\n\nRestore the missing texts and reopen the game, or choose New adventure to replace this run. Your bestiary and laurels remain. Practice is available meanwhile.";
        }
        return true;
    }

    private void SaveProgress()
    {
        if (!_profileLoaded || _saveStore == null || _resourcesDisposed) return;
        RecordEncounters();
        if (_quest != null && !_preserveSavedRun)
            _chronicle.Run = new BronzeRunSave
            {
                RunId = _runId, ArcKey = _quest.Story.Arc.Key, StorySignature = BronzeRunSave.Signature(_quest.Story.Arc),
                Seed = _runSeed, Chapter = _quest.Chapter, Phase = _quest.Phase,
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
            if (!_practice) _chapterFoes.Add(kind);
        }
    }

    private void NewAdventure()
    {
        if (_busy) return;
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        if (_chronicle.Run is { Phase: not ArcadeQuestPhase.Complete }
            && MessageBox.Show(this, "Begin a new adventure and replace this run's checkpoint? Your bestiary and earned laurels will remain.",
                "A new thread of fate", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _chronicle.Run = null; _preserveSavedRun = false;
        _ = LoadStoriesAsync(true);
    }

    private void ChooseGift()
    {
        if (_busy || !NeedsGift || _quest == null) return;
        using var shrine = new BronzeGiftForm(BronzeGifts.Offer(_gifts, _runSeed, _quest.Chapter));
        if (shrine.ShowDialog(this) != DialogResult.OK || shrine.SelectedGift is not { } gift) return;
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
        _story.Text = $"FOUND: {row.Author} — {row.Title} {PassageCitation.Display(row.Citation)}\n\n{row.Text}\n\n{_quest.Clue.Reveal}\n\n"
            + (complete ? _quest.Story.Arc.Payoff + $"\n\n{BronzeGifts.Epithet(_gifts)} — {_runScore:N0} points. Your laurel and this story now live in Bestiary & laurels."
                : NeedsGift ? "A shrine opens. Choose one of three divine gifts before the next chapter. Every gift you accept stays with you for the rest of this adventure."
                : "Gift accepted: " + BronzeGifts.Get(_gifts.Last()).Name + ".\n" + BronzeGifts.Get(_gifts.Last()).Effect + "\n\nThe next chapter awaits.");
        UpdateStars(); _canvas.Invalidate();
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

    private void ShowCollection()
    {
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        SaveProgress();
        using var collection = new BronzeCollectionForm(_chronicle, ReopenRecoveredVerse, LoadAncientWitnesses);
        if (collection.ShowDialog(this) == DialogResult.OK) ReturnToLibrary();
    }

    private async Task ReopenRecoveredVerse(ArcadePassage remembered)
    {
        if (_repository == null || !SameLibrary()) throw new InvalidOperationException("Reopen the arcade in the matching library to revisit this verse.");
        var row = await Task.Run(() => _repository.FindRememberedPassage(remembered), _lifetime.Token);
        if (row == null) throw new InvalidOperationException("That edition or verse is no longer present. Its recovered text remains in your chronicle.");
        if (_openPassage != null) await _openPassage(row.WorkId, row.NodeId);
        else await _openWork(row.WorkId);
    }

    private Task<List<BronzeWitness>> LoadAncientWitnesses(BronzeEnemyKind creature, CancellationToken cancellationToken)
    {
        if (_repository == null || !SameLibrary())
            throw new InvalidOperationException("Open the arcade with the matching library to read its ancient witnesses.");
        return Task.Run(() => _repository.LoadWitnesses(creature, cancellationToken), cancellationToken);
    }
}
