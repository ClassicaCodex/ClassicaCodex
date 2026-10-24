using System.Diagnostics;
using System.Numerics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// Bronze and Thunder, the hidden arcade: a modeless game in which a battle
/// wins a clue and only reading wins the story. Fighting pauses when focus
/// leaves; reading remains in the main window. The Mac counterpart of the
/// Windows BronzeArcadeForm, rule for rule and message for message.
///
/// <b>An independent window, not an owned one.</b> An owned window always
/// stays above its owner, and the whole game is going back and forth to the
/// reader to find a line - so the reader has to be able to cover the arcade on
/// a small screen. That is also why this is a plain Window rather than a
/// FeatureWindow: those are tool windows that ride on the reader, and they
/// close on Escape, which here pauses the battle.
///
/// <b>The arena is the game's; the window is the Mac's.</b> The 480 by 300
/// screen keeps its own night-time colours in both appearances (see
/// BronzeArcadeCanvas), set into a bronze bezel; the story beneath it, the
/// controls and the dialogs are the application's parchment or lamplight,
/// bronze and lapis.
/// </summary>
internal sealed partial class BronzeArcadeWindow : Window
{
    /// <summary>The main window's chords, which open the arcade and hand it the reader's selection (Shell/MainWindow.Arcade.cs).</summary>
    internal const string SubmitChord = "⌃⇧Return";
    internal const string OpenChord = "⌃⇧F12";

    private static readonly Color Night = Color.FromRgb(9, 8, 20);

    private readonly BronzeArcadeCanvas _canvas = new();
    private readonly StoryText _story = new();
    private readonly ScrollViewer _storyScroll;
    private readonly Border _giftRow;
    private readonly ArcadeAction _start, _read, _submit, _hint, _journal, _newRun, _sound;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly HashSet<Key> _keys = new();
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
    private bool _paused, _busy, _practice, _closed;
    private int _hintLevel, _frameChain;
    private double _lastTick, _accumulator;

    public BronzeArcadeWindow(string? database, Func<long?> selectedNode, Func<string?> activeDatabase, Func<int, Task> openWork,
        string? saveDirectory = null, Func<int, long, Task>? openPassage = null, Action? activateLibrary = null)
    {
        _database = database; _selectedNode = selectedNode; _activeDatabase = activeDatabase; _openWork = openWork;
        _saveStore = new ClassicaCodex.Data.BronzeSaveStore(database, saveDirectory);
        _openPassage = openPassage;
        _activateLibrary = activateLibrary;

        Title = "Bronze & Thunder — ClassicaCodex";
        Width = 1000;
        Height = 860;
        MinWidth = 760;
        MinHeight = 700;
        if (BronzeIcons.Emblem() is { } emblem) Icon = new WindowIcon(emblem);
        AddArcadeStyles();

        _start = new ArcadeAction("Begin adventure", BronzeIcons.Get("BeginAdventure"), StartOrContinue, primary: true);
        _submit = new ArcadeAction("Check selected passage", BronzeIcons.Get("CheckPassage"), () => _ = SubmitReaderSelectionAsync(), primary: true, keyCap: "⌃⇧↩")
        { Tip = $"Check the line selected in the reader ({SubmitChord} from either window)." };
        _hint = new ArcadeAction("Oracle hint", BronzeIcons.Get("OracleHint"), () => _ = ShowHintAsync(), primary: true);
        _read = new ArcadeAction("Library", Art.AppIcon(), ReturnToLibrary, primary: false) { Tip = "Back to the library (F6). The battle pauses while you read." };
        _journal = new ArcadeAction("Save story journal", BronzeIcons.Get("Journal"), () => _ = SaveJournalAsync(), primary: false) { Tip = "Save the verses you have recovered, and what they meant, as a text journal." };
        _newRun = new ArcadeAction("New adventure", BronzeIcons.Get("NewAdventure"), () => _ = NewAdventureAsync(), primary: false) { Tip = "Begin another story. Your bestiary and laurels remain." };
        _sound = new ArcadeAction("Sound: on", BronzeIcons.Get("SoundOn"), ToggleSound, primary: false);
        _collectionButton = new ArcadeAction("Bestiary & laurels", BronzeIcons.Get("Bestiary"), () => _ = ShowCollectionAsync(), primary: false) { Tip = "The creatures you have met and the stories you have finished." };
        _start.Button.Classes.Add("accent");
        _submit.Button.Classes.Add("accent");
        UpdateSoundButton();

        _giftButton = GiftButton();
        _giftRow = new Border { Child = _giftButton, Margin = new Thickness(0, 12, 0, 0), IsVisible = false };

        _saveStatus = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0) };
        _saveStatus.Bind(TextBlock.ForegroundProperty, _saveStatus.GetResourceObservable("Cc.Verdigris"));

        _storyScroll = new ScrollViewer
        {
            Content = _story,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 10, 0)
        };

        var primary = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in new[] { _start, _submit, _hint })
        {
            action.Button.Margin = new Thickness(0, 2, 8, 2);
            primary.Children.Add(action.Button);
        }

        var secondary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in new[] { _read, _journal, _collectionButton, _newRun, _sound })
            secondary.Children.Add(action.Button);

        var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(secondary, Dock.Right);
        bar.Children.Add(secondary);
        bar.Children.Add(primary);

        var layout = new Grid { RowDefinitions = new RowDefinitions("68*,32*,Auto,Auto,Auto"), Margin = new Thickness(16, 14, 16, 12) };
        AddRow(layout, new AspectFrame { Child = Bezel(_canvas) }, 0);
        AddRow(layout, new Border
        {
            Classes = { "card" },
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(20, 14, 8, 14),
            Child = _storyScroll
        }, 1);
        AddRow(layout, _giftRow, 2);
        AddRow(layout, bar, 3);
        AddRow(layout, _saveStatus, 4);
        Content = layout;

        AddHandler(KeyDownEvent, OnArcadeKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnArcadeKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => { _keys.Clear(); if (!_closed && _arena?.State == BronzeBattleState.Fighting) SetPaused(true); };
        Activated += (_, _) => { _keys.Clear(); _lastTick = _watch.Elapsed.TotalSeconds; };
        Opened += async (_, _) => { await LoadStoriesAsync(); _ready.TrySetResult(); };
        Closing += (_, _) => { SaveProgress(); _lifetime.Cancel(); };
        // Only ever raised on a close that actually went through, so a
        // cancelled one leaves the game running with its clock intact.
        Closed += (_, _) => { _closed = true; _keys.Clear(); _audio.Dispose(); };
    }

    private static void AddRow(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    /// <summary>
    /// The arena's frame: bronze, in the metal colours the application's tile
    /// medallions use (Common/Art), lit from above and darkening towards the
    /// foot - a cabinet's bezel rather than a box. Metal looks the same in either
    /// appearance, so these are fixed colours rather than palette ones.
    /// </summary>
    private static Border Bezel(Control canvas)
    {
        var screen = new Border
        {
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = new SolidColorBrush(Night),
            BorderBrush = new SolidColorBrush(Color.Parse("#3B2410")),
            BorderThickness = new Thickness(1),
            Child = canvas
        };

        var bezel = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(BezelInset),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#D29858"), 0),
                    new GradientStop(Color.Parse("#A0612B"), 0.45),
                    new GradientStop(Color.Parse("#6E3F18"), 1)
                }
            },
            Child = screen
        };
        bezel.Bind(Border.BoxShadowProperty, bezel.GetResourceObservable("Cc.LiftShadow"));
        return bezel;
    }

    private const double BezelInset = 7;

    /// <summary>
    /// The next step after a recovered verse, set apart from the other
    /// controls because it is the only thing to do: lapis with a gold rim and
    /// the shrine's painted gift, the way the Windows game marks it out in gold
    /// on purple.
    /// </summary>
    private Button GiftButton()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(BronzeIcons.Picture(BronzeIcons.DivineGift(), 32));
        content.Children.Add(new TextBlock
        {
            Text = "Next: Choose your divine gift",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new Button
        {
            Content = content,
            Classes = { "gift" },
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 9, 22, 9),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10)
        };
        button.Click += (_, _) => _ = ChooseGiftAsync();
        ToolTip.SetTip(button, "Next step: choose a divine gift before starting the next chapter.");
        Avalonia.Automation.AutomationProperties.SetHelpText(button, "Next step: choose a divine gift before starting the next chapter.");
        return button;
    }

    /// <summary>The gift button's lapis and gold, which the theme's own button states would otherwise repaint on hover.</summary>
    private void AddArcadeStyles()
    {
        LinearGradientBrush Lapis(string top, string bottom) => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(bottom), 1) }
        };

        var gold = new SolidColorBrush(Color.Parse("#FFCF71"));
        Style Part(Func<Selector?, Selector> state, IBrush background) =>
            new(x => state(x).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters =
                {
                    new Setter(ContentPresenter.BackgroundProperty, background),
                    new Setter(ContentPresenter.BorderBrushProperty, gold),
                    new Setter(ContentPresenter.ForegroundProperty, gold)
                }
            };

        Styles.Add(Part(x => x.OfType<Button>().Class("gift"), Lapis("#3E5F9E", "#22375E")));
        Styles.Add(Part(x => x.OfType<Button>().Class("gift").Class(":pointerover"), Lapis("#4E72B4", "#2A4373")));
        Styles.Add(Part(x => x.OfType<Button>().Class("gift").Class(":pressed"), Lapis("#2A4373", "#1A2B4A")));
    }

    private bool SameLibrary() => string.Equals(_database, _activeDatabase(), StringComparison.OrdinalIgnoreCase);

    private void Story(string text)
    {
        _story.Set(text);
        _storyScroll.ScrollToHome();
    }

    private void AppendStory(string text)
    {
        _story.Append(text);
        // What was added is what the player needs to read - "that is not the
        // passage yet" - so it comes into view even below a long clue.
        Dispatcher.UIThread.Post(() => _storyScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private async Task LoadStoriesAsync(bool newAdventure = false)
    {
        if (_busy) return;
        _busy = true; _practice = false; _paused = false; _arena = null; _canvas.Arena = null; _keys.Clear();
        var lastKey = _quest?.Story.Arc.Key;
        _quest = null; _available.Clear();
        _canvas.Banner = "BRONZE & THUNDER"; _canvas.Subtitle = "THE LOST VERSES"; _canvas.Paused = false;
        Story("Consulting the installed library… Only complete stories can become adventures.");
        RefreshButtons();
        try
        {
            LoadChronicle();
            if (!SameLibrary())
                throw new InvalidOperationException($"The active library changed. Close this game and open it again with {OpenChord} to use that library.");
            if (!string.IsNullOrWhiteSpace(_database))
            {
                _repository = new ArcadeQuestRepository(_database);
                var repository = _repository;
                _available = await Task.Run(() => repository.Load(_lifetime.Token), _lifetime.Token);
            }
            if (_closed || _lifetime.IsCancellationRequested) return;
            if (!newAdventure && TryRestoreAdventure()) return;
            if (_available.Count == 0)
            {
                Story("NO COMPLETE STORY IN THIS LIBRARY\n\nInstall Homer (Iliad and Odyssey) for the Homer-only adventures; other stories also use Greek tragedy. Then choose New adventure. No passages will be invented.\n\nYou can play the practice arena now. Practice has no passage awards or story progression.\n\n" + ControlsHelp);
            }
            else
            {
                var pool = _available.Where(s => s.Arc.Key != lastKey).ToList();
                if (pool.Count == 0) pool = _available;
                _quest = new ArcadeQuest(pool[_random.Next(pool.Count)]);
                _runId = Guid.NewGuid(); _runSeed = _random.Next(); _runScore = 0;
                _gifts.Clear(); _chapterFoes.Clear(); _preserveSavedRun = false;
                _hintLevel = 0;
                Story($"{_quest.Story.Arc.Title.ToUpperInvariant()}\n\n{_quest.Story.Arc.Premise}\n\n"
                    + $"{_quest.Story.Arc.Passages.Length} battles. One connected story. Defeat each guardian to earn a clue, then find the passage in your library. Select the line and press {SubmitChord} to submit it.\n\n"
                    + ControlsHelp);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed) Story("The oracle could not read this library.\n\n" + ex.Message + "\n\nYou can still play the practice arena. Choose New adventure to retry.");
        }
        finally { if (!_closed) { _busy = false; RefreshButtons(); SaveProgress(); } }
    }

    private const string ControlsHelp = "WASD / arrows: move and aim · J: spear / xiphos · K: javelin (Poseidon's trident if gifted) · Shift: face and block · Space: dodge · L: magic · Esc: pause · F6: library · M: sound · F2: scanlines.\n\nL casts blue sacred fire in chapters 2–3. From chapter 4 it becomes a thunder ring around you. Watch the spell name and mana cost beneath the arena.\n\nEnemy pink rings warn of attacks. Collect red ambrosia for health and cyan nectar for magic. A fallen hero can retry the same chapter.";

    private void StartOrContinue()
    {
        if (_busy) return;
        if (_paused && _arena?.State == BronzeBattleState.Fighting) { SetPaused(false); return; }
        if (_arena?.State == BronzeBattleState.Lost)
        { BeginArena(_arena.Level); return; }
        if (_quest == null) { _practice = true; BeginArena(1); return; }
        if (!SameLibrary()) { Story("The library changed. Close and reopen the arcade to start a run in the new library."); return; }
        if (NeedsGift) { _ = ChooseGiftAsync(); return; }
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
        Story($"{(_practice ? "PRACTICE" : _quest!.Story.Arc.Title.ToUpperInvariant())} — CHAPTER {level}\n\n"
            + $"{_arena.Weapon} · {_arena.Blessing}\n"
            + $"K: {_arena.RangedName} · L: {_arena.MagicName}" + (level >= 2 ? $" — {_arena.MagicCost} mana\n" : " — unlocks in chapter 2\n")
            + $"Armor tier {level}: {_arena.MaxHealth:0} health, stronger damage protection each chapter.\n\n" + ControlsHelp);
        if (!_practice && _gifts.Count > 0) _story.Append("\n\nDIVINE GIFTS\n" + string.Join("\n", _gifts.Select(g => BronzeGifts.Get(g).Name + " — " + BronzeGifts.Get(g).Effect)));
        _audio.Play(0); RefreshButtons(); _canvas.Focus();
        SaveProgress();
        RequestFrames();
    }

    /// <summary>
    /// Keeps the arena moving: one tick per frame the display draws, through
    /// Avalonia's render loop, while a battle is being fought and not paused.
    /// The Windows game polls on a 16 ms timer; tying the tick to the
    /// display's own refresh is what keeps motion even on a Mac, whose screens
    /// may run at 60 or 120 frames a second. The simulation itself still
    /// steps in fixed sixtieths of a second (see <see cref="TickArena"/>), so
    /// the game plays at the same speed on either.
    ///
    /// Between battles and while paused nothing is requested, so an idle
    /// arcade does not keep the Mac drawing sixty frames a second for a still
    /// picture. Each start begins a fresh chain of frames and retires any
    /// older one, so a frame the system never delivered - a window hidden at
    /// the wrong moment - cannot leave the game frozen on resume.
    /// </summary>
    private void RequestFrames()
    {
        if (_closed || ManualClock) return;
        NextFrame(++_frameChain);
    }

    private void NextFrame(int chain) => RequestAnimationFrame(_ =>
    {
        if (chain != _frameChain || _closed || ManualClock) return;
        TickArena(_watch.Elapsed.TotalSeconds);
        if (!_closed && !_paused && _arena?.State == BronzeBattleState.Fighting) NextFrame(chain);
    });

    /// <summary>Advances the battle to the moment <paramref name="now"/>, in seconds on the window's clock.</summary>
    internal void TickArena(double now)
    {
        if (_closed) return;
        var elapsed = Math.Min(.1, now - _lastTick); _lastTick = now;
        if (!IsVisible || _paused || _busy || _arena?.State != BronzeBattleState.Fighting) return;
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
        else if (_arena.Shots.Count > priorShots && (_keys.Contains(Key.K) || _keys.Contains(Key.L))) _audio.Play(0);
        if (_arena.State == BronzeBattleState.Won)
        {
            _keys.Clear(); _audio.Play(3);
            if (_practice) Story("PRACTICE COMPLETE\n\nThe guardian has fallen. Install a corpus with a complete story and choose New adventure for a passage quest.");
            else if (_quest!.WinBattle()) { _runScore += _arena.Score; ShowClue(); }
            RefreshButtons();
            SaveProgress();
        }
        else if (_arena.State == BronzeBattleState.Lost)
        {
            _keys.Clear(); _canvas.Subtitle = "YOUR STORY IS NOT OVER";
            Story("THE HERO HAS FALLEN\n\nRetry this chapter with full health and magic. Your found passages are safe.\n\nKeep moving, face an enemy before blocking, and dodge the pink attack warnings.\n\n" + ControlsHelp);
            RefreshButtons();
            SaveProgress();
        }
        if (now - _lastAutoSave > 15) { SaveProgress(); _lastAutoSave = now; }
        _canvas.InvalidateVisual();
    }

    private BronzeInput ReadInput()
    {
        bool Has(Key k) => _keys.Contains(k);
        return new BronzeInput(new Vector2((Has(Key.D) || Has(Key.Right) ? 1 : 0) - (Has(Key.A) || Has(Key.Left) ? 1 : 0),
            (Has(Key.S) || Has(Key.Down) ? 1 : 0) - (Has(Key.W) || Has(Key.Up) ? 1 : 0)),
            Has(Key.J), Has(Key.K), Has(Key.L), Has(Key.Space), Has(Key.LeftShift) || Has(Key.RightShift));
    }

    private static bool IsCombatKey(Key key) => key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right
        or Key.J or Key.K or Key.L or Key.Space or Key.LeftShift or Key.RightShift;

    /// <summary>
    /// The game's keys, caught on the way down before any button sees them -
    /// the Windows form's KeyPreview - so Space dodges rather than pressing
    /// whichever button last had focus.
    /// </summary>
    private void OnArcadeKeyDown(object? sender, KeyEventArgs e)
    {
        var plain = e.KeyModifiers == KeyModifiers.None;
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        if (e.Key == Key.W && e.KeyModifiers == command) { Close(); e.Handled = true; return; }
        if (e.Key == Key.Escape && plain && _arena?.State == BronzeBattleState.Fighting) { SetPaused(!_paused); e.Handled = true; return; }
        if (e.Key == Key.M && plain && !(FocusManager?.GetFocusedElement() is SelectableTextBlock)) { ToggleSound(); e.Handled = true; return; }
        if (e.Key == Key.F2 && plain) { _canvas.Scanlines = !_canvas.Scanlines; SaveProgress(); e.Handled = true; return; }
        if (e.Key == Key.F6 && plain) { ReturnToLibrary(); e.Handled = true; return; }
        if (e.Key == Key.Enter && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) { _ = SubmitReaderSelectionAsync(); e.Handled = true; return; }

        if (_arena?.State == BronzeBattleState.Fighting && !_paused && IsCombatKey(e.Key))
        { _keys.Add(e.Key); e.Handled = true; }
    }

    private void OnArcadeKeyUp(object? sender, KeyEventArgs e)
    {
        _keys.Remove(e.Key);
        if (IsCombatKey(e.Key)) e.Handled = true;
    }

    private void SetPaused(bool paused)
    {
        _paused = paused; _canvas.Paused = paused; _keys.Clear(); _accumulator = 0;
        _lastTick = _watch.Elapsed.TotalSeconds; RefreshButtons();
        if (!paused) { _canvas.Focus(); RequestFrames(); }
        SaveProgress();
    }

    private void ReturnToLibrary()
    {
        _keys.Clear();
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        _activateLibrary?.Invoke();
    }

    /// <summary>Brings the arcade forward, restoring it from the Dock if it was minimised.</summary>
    internal void BringForward()
    {
        if (_closed) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void ShowClue()
    {
        if (_quest == null) return;
        Story($"VERSE {_quest.Chapter + 1} / {_quest.Story.Arc.Passages.Length} — THE ORACLE'S CLUE\n\n{_quest.Clue.Award}\n\n"
            + $"Return to the library and find the line. Select it in either reader pane and press {SubmitChord}. Reopen this window with {OpenChord} whenever you need the clue.\n\nOracle hint reveals the author and work first, then the exact citation. Still lost? Highlight passage takes you to the line in the reader. Press {SubmitChord} when you are ready to submit it.");
        if (_hintLevel > 0)
        {
            var source = _quest.Story.Sources[_quest.Chapter][0];
            _story.Append($"\n\nORACLE: {source.Author} — {source.Title}"
                + (_hintLevel > 1 ? $", {PassageCitation.Display(source.Citation)}" : ""));
        }
    }

    private async Task ShowHintAsync()
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
                var repository = _repository;
                var row = await Task.Run(() => repository.FindRememberedPassage(source), _lifetime.Token);
                if (_closed || _lifetime.IsCancellationRequested) return;
                if (!SameLibrary()) throw new InvalidOperationException("The library changed. Reopen the arcade in the matching library.");
                if (row == null) throw new InvalidOperationException("That verse is no longer present in this library.");
                if (_openPassage != null) await _openPassage(row.WorkId, row.NodeId);
                else await _openWork(row.WorkId);
                if (!_closed) ReturnToLibrary();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_closed) AppendStory("\n\nCould not open the passage: " + ex.Message); }
            finally { if (!_closed) { _busy = false; RefreshButtons(); } }
            return;
        }
        _hintLevel = Math.Min(2, _hintLevel + 1); ShowClue();
        SaveProgress();
        RefreshButtons();
    }

    /// <summary>Called by the main reader's chord as well as the game's button.</summary>
    public async Task SubmitReaderSelectionAsync()
    {
        if (_busy || _quest?.Phase != ArcadeQuestPhase.Hunt || _repository == null) return;
        BringForward();
        if (!SameLibrary()) { Story("This run belongs to another library. Close and reopen the arcade to start with the current corpus."); return; }
        var id = _selectedNode();
        if (id == null) { ShowClue(); AppendStory("\n\nSelect a line in the main reader before submitting it."); return; }
        _busy = true; RefreshButtons();
        var quest = _quest;
        var repository = _repository;
        try
        {
            var row = await Task.Run(() => repository.GetPassage(id.Value), _lifetime.Token);
            if (_closed || _lifetime.IsCancellationRequested) return;
            if (row == null || !quest.Submit(row))
            {
                ShowClue(); AppendStory("\n\nThat is not the passage yet. Try another line, or ask the oracle for a hint."); return;
            }
            _audio.Play(3);
            _chronicle.RememberVerse(_chapterFoes, new BronzeRecoveredVerse(quest.Story.Arc.Title, quest.Clue.Reveal, row));
            if (quest.Phase == ArcadeQuestPhase.Complete) AwardLaurel();
            ShowRevelation(); SaveProgress();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) { ShowClue(); AppendStory("\n\nCould not check the passage: " + ex.Message); } }
        finally { if (!_closed) { _busy = false; RefreshButtons(); } }
    }

    private void RefreshButtons()
    {
        if (_closed) return;
        _start.IsEnabled = !_busy; _newRun.IsEnabled = !_busy;
        _start.IsVisible = _arena == null || _arena.State == BronzeBattleState.Lost || _paused
            || _quest?.Phase is ArcadeQuestPhase.Revelation || (_practice && _arena.State == BronzeBattleState.Won);
        if (_quest?.Phase is ArcadeQuestPhase.Hunt or ArcadeQuestPhase.Complete) _start.IsVisible = false;
        _start.Label = _paused ? "Resume" : _arena?.State == BronzeBattleState.Lost ? "Retry chapter"
            : _quest?.Phase == ArcadeQuestPhase.Revelation ? "Next chapter" : _quest == null ? "Practice arena" : "Begin adventure";
        _start.KeyCap = _paused ? "esc" : null;
        var hunt = _quest?.Phase == ArcadeQuestPhase.Hunt;
        _read.IsVisible = true; _submit.IsVisible = hunt; _hint.IsVisible = hunt;
        _submit.IsEnabled = !_busy; _hint.IsEnabled = !_busy;
        _journal.IsVisible = _quest?.Found.Count > 0;
        _hint.Label = _hintLevel == 0 ? "Oracle hint" : _hintLevel == 1 ? "Reveal citation" : "Highlight passage";
        _hint.Icon = BronzeIcons.Get(_hintLevel == 0 ? "OracleHint" : _hintLevel == 1 ? "RevealCitation" : "HighlightPassage");
        _hint.Tip = _hintLevel == 0 ? "Reveal the author and work."
            : _hintLevel == 1 ? "Reveal the exact citation."
            : $"Open the exact passage, scroll it into view, and highlight it. Press {SubmitChord} in the reader to submit it.";
        _giftButton.IsVisible = NeedsGift;
        _giftRow.IsVisible = NeedsGift;
        _giftButton.IsEnabled = !_busy;
        _collectionButton.IsEnabled = !_busy;
        if (NeedsGift) _start.IsVisible = false;
        if (_quest?.Phase == ArcadeQuestPhase.Battle && _arena == null) _start.Label = "Resume chapter";
    }

    private void UpdateSoundButton()
    {
        _sound.Label = _audio.Enabled ? "Sound: on" : "Sound: off";
        _sound.Icon = BronzeIcons.Get(_audio.Enabled ? "SoundOn" : "SoundOff");
        _sound.Tip = _audio.Enabled ? "Sound is on. Press M to turn it off." : "Sound is off. Press M to turn it on.";
    }

    private void ToggleSound() { _audio.Enabled = !_audio.Enabled; UpdateSoundButton(); SaveProgress(); }

    private async Task SaveJournalAsync()
    {
        if (_quest == null) return;
        if (_arena?.State == BronzeBattleState.Fighting) SetPaused(true);
        var quest = _quest;
        var path = await Files.SaveAsync(this, "Save story journal", "Bronze-and-Thunder-" + quest.Story.Arc.Key + ".txt", Files.Text);
        if (path == null) return;
        try { File.WriteAllText(path, JournalText(quest), Encoding.UTF8); }
        catch (Exception ex) { await Alert.Error(this, "Could not save journal", ex.Message); }
    }

    /// <summary>The journal as the Windows game writes it: the premise, each verse with what it meant, and the payoff once the story is done.</summary>
    internal static string JournalText(ArcadeQuest quest)
    {
        var text = new StringBuilder(quest.Story.Arc.Title + "\n\n" + quest.Story.Arc.Premise + "\n");
        for (var i = 0; i < quest.Found.Count; i++)
        {
            var row = quest.Found[i];
            text.AppendLine($"\n{row.Author} — {row.Title} {PassageCitation.Display(row.Citation)}\n{row.Text}\n\n{quest.Story.Arc.Passages[i].Reveal}");
        }
        if (quest.Phase == ArcadeQuestPhase.Complete) text.AppendLine("\n" + quest.Story.Arc.Payoff);
        return text.ToString();
    }

    /// <summary>
    /// Fits the bezel to the arena's 480 by 300 proportions in whatever room
    /// the window gives it, centred, so the bronze frames the picture rather
    /// than a letterbox of empty screen around it.
    /// </summary>
    private sealed class AspectFrame : Decorator
    {
        private static Size Fit(Size available)
        {
            var width = double.IsInfinity(available.Width) ? 960 : available.Width;
            var height = double.IsInfinity(available.Height) ? width * 300 / 480 : available.Height;
            var scale = Math.Max(0, Math.Min((width - BezelInset * 2) / 480, (height - BezelInset * 2) / 300));
            return new Size(480 * scale + BezelInset * 2, 300 * scale + BezelInset * 2);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var size = Fit(availableSize);
            Child?.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = Fit(finalSize);
            Child?.Arrange(new Rect((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
            return finalSize;
        }
    }
}
