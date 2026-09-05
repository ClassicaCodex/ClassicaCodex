using System.Diagnostics;
using System.Numerics;
using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>A modeless arcade: fighting pauses when focus leaves; reading remains in MainForm.</summary>
public sealed partial class BronzeArcadeForm : ScaledForm
{
    private readonly Icon _windowIcon;
    private readonly Bitmap _bestiaryButtonImage;
    private readonly Dictionary<string, Bitmap> _buttonImages = new();
    private readonly Bitmap _giftButtonImage;
    private readonly Font _giftButtonFont = new("Segoe UI", 13, FontStyle.Bold);
    private readonly FlowLayoutPanel _giftRow = new() { Dock = DockStyle.Fill, AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 8, 6, 4), Visible = false };
    private readonly BronzeArcadeCanvas _canvas = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _story = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(22, 18, 34), ForeColor = Color.FromArgb(255, 225, 173),
        Font = new Font("Segoe UI", 11), DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical };
    private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
    private readonly Button _start, _read, _submit, _hint, _journal, _newRun, _sound;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly HashSet<Keys> _keys = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<long?> _selectedNode;
    private readonly Func<string?> _activeDatabase;
    private readonly Func<int, Task> _openWork;
    private readonly Action? _activateLibrary;
    private readonly string? _database;
    private readonly Random _random = new();
    private readonly BronzeArcadeSound _audio = new();
    private List<ArcadeStory> _available = new();
    private ArcadeQuest? _quest;
    private BronzeArena? _arena;
    private ArcadeQuestRepository? _repository;
    private bool _paused, _busy, _practice, _resourcesDisposed, _closed;
    private int _hintLevel;
    private double _lastTick, _accumulator;

    /// <summary>
    /// Windows keeps delivering messages to a form that is on its way out -
    /// WM_ACTIVATE in particular arrives after Dispose has released the button
    /// bitmaps. Anything that touches the arena or the controls checks this
    /// first, because reacting to those messages means drawing with handles
    /// that no longer exist.
    /// </summary>
    private bool ShuttingDown => _closed || _resourcesDisposed || Disposing || IsDisposed;

    public BronzeArcadeForm(string? database, Func<long?> selectedNode, Func<string?> activeDatabase, Func<int, Task> openWork,
        string? saveDirectory = null, Func<int, long, Task>? openPassage = null, Action? activateLibrary = null)
    {
        _database = database; _selectedNode = selectedNode; _activeDatabase = activeDatabase; _openWork = openWork;
        _saveStore = new ClassicaCodex.Data.BronzeSaveStore(database, saveDirectory);
        _openPassage = openPassage;
        _activateLibrary = activateLibrary;
        using (var stream = typeof(BronzeArcadeForm).Assembly.GetManifestResourceStream("ClassicaCodex.UI.Icons.BronzeAndThunder.ico")
            ?? throw new InvalidOperationException("The arcade icon resource is missing."))
        using (var embeddedIcon = new Icon(stream))
            _windowIcon = (Icon)embeddedIcon.Clone();
        Icon = _windowIcon;
        Text = "Bronze & Thunder — ClassicaCodex"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 870); MinimumSize = new Size(760, 700);
        BackColor = Color.FromArgb(15, 12, 27); ForeColor = Color.Wheat; KeyPreview = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        layout.Controls.Add(_canvas, 0, 0); layout.Controls.Add(_story, 0, 1);
        layout.Controls.Add(_giftRow, 0, 2); layout.Controls.Add(_buttons, 0, 3);
        Controls.Add(layout);
        _start = Button("Begin adventure", StartOrContinue);
        _read = Button("Library [F6]", ReturnToLibrary);
        _submit = Button("Check selected passage", SubmitReaderSelection);
        _hint = Button("Oracle hint", ShowHint);
        _journal = Button("Save story journal", SaveJournal);
        _newRun = Button("New adventure", NewAdventure);
        _sound = Button("Sound: on [M]", ToggleSound);
        foreach (var name in new[] { "Journal", "NewAdventure", "BeginAdventure", "SoundOn", "SoundOff",
            "OracleHint", "CheckPassage", "RevealCitation", "HighlightPassage" })
            _buttonImages.Add(name, BronzeIcons.ButtonImage(name));
        _journal.Image = _buttonImages["Journal"];
        _newRun.Image = _buttonImages["NewAdventure"];
        _start.Image = _buttonImages["BeginAdventure"];
        _submit.Image = _buttonImages["CheckPassage"];
        _hint.Image = _buttonImages["OracleHint"];
        _read.Image = AppIcons.Get("AppIcon", 20);
        UpdateSoundButton();
        foreach (var button in new[] { _journal, _newRun, _start, _read, _sound, _submit, _hint })
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
        _giftButton = Button("Next: Choose your divine gift", ChooseGift);
        _giftRow.Controls.Add(_giftButton);
        _giftButton.Font = _giftButtonFont;
        _giftButton.Padding = new Padding(14, 10, 14, 10);
        _giftButton.ForeColor = Color.FromArgb(255, 207, 113);
        _giftButton.BackColor = Color.FromArgb(55, 35, 68);
        _giftButton.FlatAppearance.BorderColor = Color.FromArgb(255, 207, 113);
        _giftButton.FlatAppearance.BorderSize = 2;
        _giftButton.AccessibleDescription = "Next step: choose a divine gift before starting the next chapter.";
        using (var gift = BronzeIcons.DivineGift())
        using (var small = new Icon(gift, 32, 32)) _giftButtonImage = small.ToBitmap();
        _giftButton.Image = _giftButtonImage;
        _giftButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _collectionButton = Button("Bestiary && laurels", ShowCollection);
        using (var beast = BronzeIcons.Bestiary())
        using (var small = new Icon(beast, 20, 20)) _bestiaryButtonImage = small.ToBitmap();
        _collectionButton.Image = _bestiaryButtonImage;
        _collectionButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        _buttons.Controls.Add(_saveStatus);
        Deactivate += (_, _) => { _keys.Clear(); if (!ShuttingDown && _arena?.State == BronzeBattleState.Fighting) SetPaused(true); };
        VisibleChanged += (_, _) => { _keys.Clear(); _lastTick = _watch.Elapsed.TotalSeconds; };
        Shown += async (_, _) => { await LoadStoriesAsync(); if (!IsDisposed) _timer.Start(); };
        _timer.Tick += (_, _) => TickArena();
        KeyDown += (_, e) =>
        {
            if (_arena?.State == BronzeBattleState.Fighting && !_paused && IsCombatKey(e.KeyCode))
            { _keys.Add(e.KeyCode); e.Handled = true; e.SuppressKeyPress = true; }
        };
        KeyUp += (_, e) => { _keys.Remove(e.KeyCode); if (IsCombatKey(e.KeyCode)) e.Handled = true; };
        FormClosing += (_, _) => { SaveProgress(); _lifetime.Cancel(); };
        // Only ever raised on a close that actually went through, so a cancelled
        // one leaves the game running with its clock intact.
        FormClosed += (_, _) => { _closed = true; _timer.Stop(); _keys.Clear(); };
    }

    private Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(47, 32, 56), ForeColor = Color.Wheat, Padding = new Padding(7), Margin = new Padding(4) };
        button.Click += (_, _) => action(); _buttons.Controls.Add(button); return button;
    }

    private bool SameLibrary() => string.Equals(_database, _activeDatabase(), StringComparison.OrdinalIgnoreCase);

    private async Task LoadStoriesAsync(bool newAdventure = false)
    {
        if (_busy) return;
        _busy = true; _practice = false; _paused = false; _arena = null; _canvas.Arena = null; _keys.Clear();
        var lastKey = _quest?.Story.Arc.Key;
        _quest = null; _available.Clear();
        _canvas.Banner = "BRONZE & THUNDER"; _canvas.Subtitle = "THE LOST VERSES"; _canvas.Paused = false;
        _story.Text = "Consulting the installed library… Only complete stories can become adventures.";
        RefreshButtons(); _canvas.Invalidate();
        try
        {
            LoadChronicle();
            if (!SameLibrary())
                throw new InvalidOperationException("The active library changed. Close this game and open it again with Ctrl+Shift+F12 to use that library.");
            if (!string.IsNullOrWhiteSpace(_database))
            {
                _repository = new ArcadeQuestRepository(_database);
                _available = await Task.Run(() => _repository.Load(_lifetime.Token), _lifetime.Token);
            }
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            if (!newAdventure && TryRestoreAdventure()) return;
            if (_available.Count == 0)
            {
                _story.Text = "NO COMPLETE STORY IN THIS LIBRARY\n\nInstall Homer (Iliad and Odyssey) for the Homer-only adventures; other stories also use Greek tragedy. Then choose New adventure. No passages will be invented.\n\nYou can play the practice arena now. Practice has no passage awards or story progression.\n\n" + ControlsHelp;
            }
            else
            {
                var pool = _available.Where(s => s.Arc.Key != lastKey).ToList();
                if (pool.Count == 0) pool = _available;
                _quest = new ArcadeQuest(pool[_random.Next(pool.Count)]);
                _runId = Guid.NewGuid(); _runSeed = _random.Next(); _runScore = 0;
                _gifts.Clear(); _chapterFoes.Clear(); _preserveSavedRun = false;
                _hintLevel = 0;
                _story.Text = $"{_quest.Story.Arc.Title.ToUpperInvariant()}\n\n{_quest.Story.Arc.Premise}\n\n"
                    + $"{_quest.Story.Arc.Passages.Length} battles. One connected story. Defeat each guardian to earn a clue, then find the passage in your library. Select the line and press Ctrl+Shift+Enter to submit it.\n\n"
                    + ControlsHelp;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed) _story.Text = "The oracle could not read this library.\n\n" + ex.Message + "\n\nYou can still play the practice arena. Choose New adventure to retry.";
        }
        finally { if (!IsDisposed) { _busy = false; RefreshButtons(); SaveProgress(); } }
    }

    private const string ControlsHelp = "WASD / arrows: move and aim · J: spear / xiphos · K: javelin (Poseidon's trident if gifted) · Shift: face and block · Space: dodge · L: magic · Esc: pause · F6: library · M: sound · F2: scanlines.\n\nL casts blue sacred fire in chapters 2–3. From chapter 4 it becomes a thunder ring around you. Watch the spell name and mana cost beneath the arena.\n\nEnemy pink rings warn of attacks. Collect red ambrosia for health and cyan nectar for magic. A fallen hero can retry the same chapter.";

    private void StartOrContinue()
    {
        if (_busy) return;
        if (_paused && _arena?.State == BronzeBattleState.Fighting) { SetPaused(false); return; }
        if (_arena?.State == BronzeBattleState.Lost)
        { BeginArena(_arena.Level); return; }
        if (_quest == null) { _practice = true; BeginArena(1); return; }
        if (!SameLibrary()) { _story.Text = "The library changed. Close and reopen the arcade to start a run in the new library."; return; }
        if (NeedsGift) { ChooseGift(); return; }
        if (_quest.Phase == ArcadeQuestPhase.Battle && _arena == null) { BeginArena(_quest.Chapter + 1); return; }
        if (_quest.Phase == ArcadeQuestPhase.Revelation) _chapterFoes.Clear();
        if (_quest.BeginBattle()) BeginArena(_quest.Chapter + 1);
    }

    private void BeginArena(int level)
    {
        _arena = new BronzeArena(level, unchecked(_runSeed + level * 3571), _practice ? null : _gifts); _canvas.Arena = _arena;
        _recordedDefeats.Clear();
        _keys.Clear(); _paused = false; _canvas.Paused = false; _hintLevel = 0;
        _lastTick = _watch.Elapsed.TotalSeconds; _accumulator = 0;
        _canvas.Subtitle = "FIND THE WORDS. FOLLOW THE STORY.";
        _story.Text = $"{(_practice ? "PRACTICE" : _quest!.Story.Arc.Title.ToUpperInvariant())} — CHAPTER {level}\n\n"
            + $"{_arena.Weapon} · {_arena.Blessing}\n"
            + $"K: {_arena.RangedName} · L: {_arena.MagicName}" + (level >= 2 ? $" — {_arena.MagicCost} mana\n" : " — unlocks in chapter 2\n")
            + $"Armor tier {level}: {_arena.MaxHealth:0} health, stronger damage protection each chapter.\n\n" + ControlsHelp;
        if (!_practice && _gifts.Count > 0) _story.AppendText("\n\nDIVINE GIFTS\n" + string.Join("\n", _gifts.Select(g => BronzeGifts.Get(g).Name + " — " + BronzeGifts.Get(g).Effect)));
        _audio.Play(0); RefreshButtons(); _canvas.Focus();
        SaveProgress();
    }

    private void TickArena()
    {
        if (ShuttingDown) return;
        var now = _watch.Elapsed.TotalSeconds;
        var elapsed = Math.Min(.1, now - _lastTick); _lastTick = now;
        if (!Visible || _paused || _busy || _arena?.State != BronzeBattleState.Fighting) return;
        _accumulator += elapsed;
        var priorKills = _arena.Kills; var priorHealth = _arena.Health; var priorShots = _arena.Shots.Count;
        var priorCasts = _arena.MagicCasts;
        while (_accumulator >= 1.0 / 60)
        {
            _arena.Update(1f / 60, ReadInput()); _accumulator -= 1.0 / 60;
        }
        RecordEncounters();
        if (_arena.Health < priorHealth) _audio.Play(2);
        else if (_arena.Kills > priorKills) _audio.Play(1);
        else if (_arena.MagicCasts > priorCasts) _audio.Play(0);
        else if (_arena.Shots.Count > priorShots && (_keys.Contains(Keys.K) || _keys.Contains(Keys.L))) _audio.Play(0);
        if (_arena.State == BronzeBattleState.Won)
        {
            _keys.Clear(); _audio.Play(3);
            if (_practice) _story.Text = "PRACTICE COMPLETE\n\nThe guardian has fallen. Install a corpus with a complete story and choose New adventure for a passage quest.";
            else if (_quest!.WinBattle()) { _runScore += _arena.Score; ShowClue(); }
            RefreshButtons();
            SaveProgress();
        }
        else if (_arena.State == BronzeBattleState.Lost)
        {
            _keys.Clear(); _canvas.Subtitle = "YOUR STORY IS NOT OVER";
            _story.Text = "THE HERO HAS FALLEN\n\nRetry this chapter with full health and magic. Your found passages are safe.\n\nKeep moving, face an enemy before blocking, and dodge the pink attack warnings.\n\n" + ControlsHelp;
            RefreshButtons();
            SaveProgress();
        }
        if (now - _lastAutoSave > 15) { SaveProgress(); _lastAutoSave = now; }
        _canvas.Invalidate();
    }

    private BronzeInput ReadInput()
    {
        bool Has(Keys k) => _keys.Contains(k);
        return new BronzeInput(new Vector2((Has(Keys.D) || Has(Keys.Right) ? 1 : 0) - (Has(Keys.A) || Has(Keys.Left) ? 1 : 0),
            (Has(Keys.S) || Has(Keys.Down) ? 1 : 0) - (Has(Keys.W) || Has(Keys.Up) ? 1 : 0)),
            Has(Keys.J), Has(Keys.K), Has(Keys.L), Has(Keys.Space), Has(Keys.ShiftKey) || Has(Keys.LShiftKey) || Has(Keys.RShiftKey));
    }

    private static bool IsCombatKey(Keys key) => key is Keys.W or Keys.A or Keys.S or Keys.D or Keys.Up or Keys.Down or Keys.Left or Keys.Right
        or Keys.J or Keys.K or Keys.L or Keys.Space or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _arena?.State == BronzeBattleState.Fighting) { SetPaused(!_paused); return true; }
        if (keyData == Keys.M && !_story.Focused) { ToggleSound(); return true; }
        if (keyData == Keys.F2) { _canvas.Scanlines = !_canvas.Scanlines; _canvas.Invalidate(); SaveProgress(); return true; }
        if (keyData == Keys.F6) { ReturnToLibrary(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Enter)) { SubmitReaderSelection(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SetPaused(bool paused)
    {
        _paused = paused; _canvas.Paused = paused; _keys.Clear(); _accumulator = 0;
        _lastTick = _watch.Elapsed.TotalSeconds; RefreshButtons(); _canvas.Invalidate();
        if (!paused) _canvas.Focus();
        SaveProgress();
    }

    private void ReturnToLibrary()
    {
        _keys.Clear();
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        if (_activateLibrary != null) _activateLibrary(); else Owner?.Activate();
    }

    private void ShowClue()
    {
        if (_quest == null) return;
        _story.Text = $"VERSE {_quest.Chapter + 1} / {_quest.Story.Arc.Passages.Length} — THE ORACLE'S CLUE\n\n{_quest.Clue.Award}\n\n"
            + "Return to the library and find the line. Select it in either reader pane and press Ctrl+Shift+Enter. Reopen this window with Ctrl+Shift+F12 whenever you need the clue.\n\nOracle hint reveals the author and work first, then the exact citation. Still lost? Highlight passage takes you to the line in the reader. Press Ctrl+Shift+Enter when you are ready to submit it.";
        if (_hintLevel > 0)
        {
            var source = _quest.Story.Sources[_quest.Chapter][0];
            _story.AppendText($"\n\nORACLE: {source.Author} — {source.Title}"
                + (_hintLevel > 1 ? $", {PassageCitation.Display(source.Citation)}" : ""));
        }
    }

    private async void ShowHint()
    {
        if (_busy || _quest?.Phase != ArcadeQuestPhase.Hunt) return;
        if (_hintLevel >= 2)
        {
            _busy = true; RefreshButtons();
            try
            {
                if (_repository == null || !SameLibrary())
                    throw new InvalidOperationException("Reopen the arcade in the matching library to find this verse.");
                var source = _quest.Story.Sources[_quest.Chapter][0];
                var row = await Task.Run(() => _repository.FindRememberedPassage(source), _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                if (!SameLibrary()) throw new InvalidOperationException("The library changed. Reopen the arcade in the matching library.");
                if (row == null) throw new InvalidOperationException("That verse is no longer present in this library.");
                if (_openPassage != null) await _openPassage(row.WorkId, row.NodeId);
                else await _openWork(row.WorkId);
                if (!IsDisposed) ReturnToLibrary();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) _story.AppendText("\n\nCould not open the passage: " + ex.Message); }
            finally { if (!IsDisposed) { _busy = false; RefreshButtons(); } }
            return;
        }
        _hintLevel = Math.Min(2, _hintLevel + 1); ShowClue();
        SaveProgress();
        RefreshButtons();
    }

    /// <summary>Called by the main reader shortcut as well as the game button.</summary>
    public async void SubmitReaderSelection()
    {
        if (_busy || _quest?.Phase != ArcadeQuestPhase.Hunt || _repository == null) return;
        Show(); Activate();
        if (!SameLibrary()) { _story.Text = "This run belongs to another library. Close and reopen the arcade to start with the current corpus."; return; }
        var id = _selectedNode();
        if (id == null) { ShowClue(); _story.AppendText("\n\nSelect a line in the main reader before submitting it."); return; }
        _busy = true; RefreshButtons();
        var quest = _quest;
        try
        {
            var row = await Task.Run(() => _repository.GetPassage(id.Value), _lifetime.Token);
            if (IsDisposed || _lifetime.IsCancellationRequested) return;
            if (row == null || !quest.Submit(row))
            {
                ShowClue(); _story.AppendText("\n\nThat is not the passage yet. Try another line, or ask the oracle for a hint."); return;
            }
            _audio.Play(3);
            _chronicle.RememberVerse(_chapterFoes, new BronzeRecoveredVerse(quest.Story.Arc.Title, quest.Clue.Reveal, row));
            if (quest.Phase == ArcadeQuestPhase.Complete) AwardLaurel();
            ShowRevelation(); SaveProgress();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) { ShowClue(); _story.AppendText("\n\nCould not check the passage: " + ex.Message); } }
        finally { if (!IsDisposed) { _busy = false; RefreshButtons(); } }
    }

    private void RefreshButtons()
    {
        if (ShuttingDown) return;
        _start.Enabled = !_busy; _newRun.Enabled = !_busy;
        _start.Visible = _arena == null || _arena.State == BronzeBattleState.Lost || _paused
            || _quest?.Phase is ArcadeQuestPhase.Revelation || (_practice && _arena.State == BronzeBattleState.Won);
        if (_quest?.Phase is ArcadeQuestPhase.Hunt or ArcadeQuestPhase.Complete) _start.Visible = false;
        _start.Text = _paused ? "Resume [Esc]" : _arena?.State == BronzeBattleState.Lost ? "Retry chapter"
            : _quest?.Phase == ArcadeQuestPhase.Revelation ? "Next chapter" : _quest == null ? "Practice arena" : "Begin adventure";
        var hunt = _quest?.Phase == ArcadeQuestPhase.Hunt;
        _read.Visible = true; _submit.Visible = hunt; _hint.Visible = hunt;
        _submit.Enabled = !_busy; _hint.Enabled = !_busy;
        _journal.Visible = _quest?.Found.Count > 0;
        _hint.Text = _hintLevel == 0 ? "Oracle hint" : _hintLevel == 1 ? "Reveal citation" : "Highlight passage";
        _hint.Image = _buttonImages[_hintLevel == 0 ? "OracleHint" : _hintLevel == 1 ? "RevealCitation" : "HighlightPassage"];
        _hint.AccessibleDescription = _hintLevel == 0 ? "Reveal the author and work."
            : _hintLevel == 1 ? "Reveal the exact citation."
            : "Open the exact passage, scroll it into view, and highlight it. Press Ctrl+Shift+Enter in the reader to submit it.";
        _giftButton.Visible = NeedsGift;
        _giftRow.Visible = NeedsGift;
        _giftButton.Enabled = !_busy;
        _collectionButton.Enabled = !_busy;
        if (NeedsGift) _start.Visible = false;
        if (_quest?.Phase == ArcadeQuestPhase.Battle && _arena == null) _start.Text = "Resume chapter";
    }

    private void UpdateSoundButton()
    {
        _sound.Text = _audio.Enabled ? "Sound: on [M]" : "Sound: off [M]";
        _sound.Image = _buttonImages[_audio.Enabled ? "SoundOn" : "SoundOff"];
    }
    private void ToggleSound() { _audio.Enabled = !_audio.Enabled; UpdateSoundButton(); SaveProgress(); }

    private void SaveJournal()
    {
        if (_quest == null) return;
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        using var dialog = new SaveFileDialog { Filter = "Text journal (*.txt)|*.txt", FileName = "Bronze-and-Thunder-" + _quest.Story.Arc.Key + ".txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var text = new StringBuilder(_quest.Story.Arc.Title + "\n\n" + _quest.Story.Arc.Premise + "\n");
        for (var i = 0; i < _quest.Found.Count; i++)
        {
            var row = _quest.Found[i];
            text.AppendLine($"\n{row.Author} — {row.Title} {PassageCitation.Display(row.Citation)}\n{row.Text}\n\n{_quest.Story.Arc.Passages[i].Reveal}");
        }
        if (_quest.Phase == ArcadeQuestPhase.Complete) text.AppendLine("\n" + _quest.Story.Arc.Payoff);
        try { File.WriteAllText(dialog.FileName, text.ToString(), Encoding.UTF8); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save journal"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            SaveProgress();
            _resourcesDisposed = true;
            _lifetime.Cancel(); _timer.Stop(); _timer.Dispose(); _audio.Dispose(); _lifetime.Dispose(); _windowIcon.Dispose();
            // A button asks GDI+ about its Image every time Visible changes, and
            // messages still arrive after this point - so hand the bitmaps back
            // before freeing them rather than leaving dead handles on screen.
            foreach (var button in new[] { _journal, _newRun, _start, _read, _sound, _submit, _hint, _giftButton, _collectionButton })
                button.Image = null;
            _bestiaryButtonImage.Dispose(); _giftButtonImage.Dispose(); _giftButtonFont.Dispose();
            foreach (var image in _buttonImages.Values) image.Dispose();
        }
        base.Dispose(disposing);
    }
}


