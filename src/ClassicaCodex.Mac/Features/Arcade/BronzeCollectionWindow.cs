using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// The hero's chronicle: the creatures met in battle and the stories
/// finished, kept across adventures.
///
/// A creature stays a shadow until it is defeated - no portrait, no tactics -
/// so the bestiary is something earned rather than a manual. Once met, it
/// shows how to fight it, the myth it comes from with the ancient source
/// named, and the verses the player recovered after battles in which it
/// appeared; and its ancient witnesses can be read from the library itself.
/// The hall of laurels keeps each finished story with its epithet, its score
/// and its verses, and every verse can be reopened in the reader.
///
/// Closes with true when the player chose to go and read something, which
/// the arcade answers by bringing the library forward.
/// </summary>
internal sealed class BronzeCollectionWindow : FeatureWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ListBox _creatures;
    private readonly ListBox _victories;
    private readonly StoryText _description;
    private readonly StoryText _inscription;
    private readonly SpritePortrait _portrait = new() { Width = 100, Height = 100 };
    private readonly Border _portraitFrame;
    private readonly Button _ancient;
    private readonly MemoryReader _memories, _story;
    private readonly TabControl _tabs;
    private bool _closed;

    private static readonly string[] Tactics =
    {
        "SERPENT\nA low, relentless pursuer. Keep your spear between its head and your heels.",
        "HARPY\nA winged skirmisher that holds its distance. Watch its warning, then dodge across the bolt's path.",
        "BOAR\nIts warning marks a charge. Step aside before it commits, then strike from the flank.",
        "CYCLOPS\nA close-range guardian with a crushing stomp. Leave the wide pink ring before the blow lands.",
        "GORGON\nA ranged guardian whose bolts punish a direct approach. Athena's shield can return them.",
        "HYDRA\nFive bolts spread from a single warning. Find a gap, or erase nearby bolts with the thunder ring."
    };

    private static readonly string[] MythicEchoes =
    {
        "Mythic echo: Apollo kills the serpent Python at Delphi. Apollodorus, Library 1.4.1.",
        "Mythic echo: the Harpies torment Phineus by stealing his food. Apollodorus, Library 1.9.21.",
        "Mythic echo: Heracles captures the Erymanthian boar alive. Apollodorus, Library 2.5.4.",
        "Mythic echo: Odysseus meets Polyphemus in the Cyclops's cave. Homer, Odyssey 9.",
        "Mythic echo: Perseus approaches Medusa by watching her reflection. Apollodorus, Library 2.4.2.",
        "Mythic echo: Heracles fights the Lernaean Hydra with Iolaus's help. Apollodorus, Library 2.5.2."
    };

    public BronzeCollectionWindow(BronzeChronicle chronicle, Func<ArcadePassage, Task> reopen,
        Func<BronzeEnemyKind, CancellationToken, Task<List<BronzeWitness>>>? loadWitnesses = null)
    {
        Title = "The hero's chronicle — Bestiary & laurels";
        Width = 940;
        Height = 720;
        MinWidth = 760;
        MinHeight = 620;
        if (BronzeIcons.Bestiary() is { } icon) Icon = new WindowIcon(icon);
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); };

        // ---- Discovered bestiary ----
        _creatures = List();
        var rows = new List<Control>();
        foreach (var kind in Enum.GetValues<BronzeEnemyKind>())
        {
            var discovery = chronicle.Bestiary.FirstOrDefault(d => d.Kind == kind);
            CreatureLabels.Add(discovery == null ? "?  Undiscovered" : $"{kind}  ·  {discovery.Defeats:N0}");
            rows.Add(CreatureRow(kind, discovery));
        }
        _creatures.ItemsSource = rows;

        _portraitFrame = new Border
        {
            Width = 112,
            Height = 112,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.Parse("#A0612B")),
            Background = new SolidColorBrush(Color.FromRgb(15, 12, 34)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _portrait
        };

        _description = Inscription();
        _ancient = new Button
        {
            Content = "Scholia · Read the ancient witnesses",
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _memories = new MemoryReader(reopen, this);

        var loadingWitness = false;
        _ancient.Click += async (_, _) =>
        {
            var index = _creatures.SelectedIndex;
            if (loadingWitness || index < 0 || loadWitnesses == null) return;
            loadingWitness = true; _ancient.IsEnabled = false; _ancient.Content = "Consulting the ancient witnesses…";
            try
            {
                var witnesses = await loadWitnesses((BronzeEnemyKind)index, _lifetime.Token);
                if (_closed || _lifetime.IsCancellationRequested || _creatures.SelectedIndex != index) return;
                if (witnesses.Count == 0)
                {
                    await Alert.Info(this, "No ancient witness in this library", "This library does not contain the referenced passages for this creature. Its mythic references remain in the bestiary.");
                    return;
                }
                var scholia = new BronzeWitnessWindow(witnesses);
                if (await scholia.ShowDialog<bool>(this) && scholia.SelectedPassage is { } row)
                { await reopen(row); Close(true); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_closed) await Alert.Error(this, "Could not consult this witness", ex.Message); }
            finally
            {
                if (!_closed)
                {
                    loadingWitness = false; _ancient.Content = "Scholia · Read the ancient witnesses";
                    _ancient.IsEnabled = chronicle.Bestiary.Any(d => (int)d.Kind == _creatures.SelectedIndex) && loadWitnesses != null;
                }
            }
        };

        _creatures.SelectionChanged += (_, _) =>
        {
            if (_creatures.SelectedIndex < 0) return;
            var kind = (BronzeEnemyKind)_creatures.SelectedIndex;
            var entry = chronicle.Bestiary.FirstOrDefault(d => d.Kind == kind);
            _ancient.IsEnabled = entry != null && loadWitnesses != null && !loadingWitness;
            if (entry == null)
            {
                _portrait.Kind = null;
                _description.Set("A SHADOW WITHOUT A NAME\n\nDefeat this creature to reveal its portrait and arena tactics.");
                _memories.Set(Array.Empty<BronzeRecoveredVerse>(), "No encounter recorded yet."); return;
            }
            _portrait.Kind = kind;
            _description.Set(Tactics[(int)kind] + "\n\n" + MythicEchoes[(int)kind]
                + $"\n\n{entry.Defeats:N0} defeated across your adventures.");
            _memories.Set(entry.Verses, "Verses you recovered after encounters with this creature:");
        };

        var portraitRow = new DockPanel();
        DockPanel.SetDock(_portraitFrame, Dock.Left);
        _portraitFrame.Margin = new Thickness(0, 0, 18, 0);
        portraitRow.Children.Add(_portraitFrame);
        portraitRow.Children.Add(_description);

        var bestiaryDetail = new DockPanel { Margin = new Thickness(18, 4, 4, 4) };
        var bestiaryTop = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        bestiaryTop.Children.Add(portraitRow);
        bestiaryTop.Children.Add(_ancient);
        DockPanel.SetDock(bestiaryTop, Dock.Top);
        bestiaryDetail.Children.Add(bestiaryTop);
        bestiaryDetail.Children.Add(_memories);

        _creatures.SelectedIndex = 0;

        // ---- Hall of laurels ----
        _victories = List();
        var trophies = chronicle.Trophies.OrderByDescending(t => t.EarnedAt).ToList();
        VictoryLabels.AddRange(trophies.Select(t => "★ " + t.ArcTitle));
        _victories.ItemsSource = trophies.Select(VictoryRow).ToList();

        _inscription = Inscription();
        _story = new MemoryReader(reopen, this);
        _inscription.Set("THE STARS ARE WAITING\n\nFinish a connected story to earn a laurel, a heroic epithet, and a star on the title screen. Your completed stories stay here when you begin another adventure.");
        _victories.SelectionChanged += (_, _) =>
        {
            if (_victories.SelectedIndex < 0) return;
            var trophy = trophies[_victories.SelectedIndex];
            var text = $"{trophy.Epithet.ToUpperInvariant()}\n{trophy.ArcTitle} · {trophy.Score:N0} points · {trophy.EarnedAt.LocalDateTime:d}\n\n"
                + trophy.Premise + "\n\n" + trophy.Payoff + "\n\nGifts: " + string.Join(" · ", trophy.Gifts.Select(g => BronzeGifts.Get(g).Name));
            if (chronicle.Trophies.Select(t => t.ArcKey).Distinct().Count() >= QuestArcs.All.Length)
                text += "\n\nTHE OWL'S SECRET: You have returned every lost story to the sky. Even Athena closes her book to applaud.";
            _inscription.Set(text);
            _story.Set(trophy.Verses, "The verses that made this story:");
        };

        var hallDetail = new DockPanel { Margin = new Thickness(18, 4, 4, 4) };
        var inscriptionScroll = new ScrollViewer
        {
            Content = _inscription,
            MaxHeight = 240,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(inscriptionScroll, Dock.Top);
        hallDetail.Children.Add(inscriptionScroll);
        hallDetail.Children.Add(_story);
        if (trophies.Count > 0) _victories.SelectedIndex = 0;
        else _story.Set(Array.Empty<BronzeRecoveredVerse>(), "The verses that made this story:");

        _tabs = new TabControl
        {
            Margin = new Thickness(16, 8, 16, 16),
            Items =
            {
                new TabItem { Header = TabHeader("Discovered bestiary", BronzeIcons.Bestiary()), Content = Split(_creatures, bestiaryDetail) },
                new TabItem { Header = TabHeader("Hall of laurels", BronzeIcons.Laurels()), Content = Split(_victories, hallDetail) }
            }
        };
        Content = _tabs;
    }

    // ---- What the tests look at ----
    internal List<string> CreatureLabels { get; } = new();
    internal List<string> VictoryLabels { get; } = new();
    internal ListBox Creatures => _creatures;
    internal ListBox Victories => _victories;
    internal TabControl Tabs => _tabs;
    internal Button AncientWitnesses => _ancient;
    internal string Description => _description.Story;
    internal string InscriptionText => _inscription.Story;
    internal MemoryReader BestiaryMemories => _memories;
    internal MemoryReader HallMemories => _story;

    private static Control TabHeader(string text, Avalonia.Media.Imaging.Bitmap? icon)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(BronzeIcons.Picture(icon, 28));
        header.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return header;
    }

    private static Control Split(ListBox list, Control detail)
    {
        var listFrame = new Border { Classes = { "panel" }, Padding = new Thickness(4), Child = list };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*"), Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetColumn(detail, 1);
        grid.Children.Add(listFrame);
        grid.Children.Add(detail);
        return grid;
    }

    private static ListBox List() => new() { SelectionMode = SelectionMode.Single };

    internal static SelectableTextBlock TextPanel() => new() { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };

    /// <summary>A creature's entry or a laurel's inscription: its shouted first line set as a heading, as the story panel sets the game's.</summary>
    private static StoryText Inscription() => new() { FontSize = 14, LineHeight = 20 };

    /// <summary>
    /// A creature in the list: its portrait in a small night-sky tile once it
    /// has been met, a question mark until then, and the count of defeats.
    /// The words are the Windows list's.
    /// </summary>
    private static Control CreatureRow(BronzeEnemyKind kind, BronzeDiscovery? discovery)
    {
        var tile = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(15, 12, 34)),
            Margin = new Thickness(0, 0, 10, 0),
            Child = discovery == null
                ? new TextBlock
                {
                    Text = "?",
                    FontSize = 17,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(116, 91, 137)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : new SpritePortrait { Kind = kind, Width = 34, Height = 34 }
        };

        var name = new TextBlock
        {
            Text = discovery == null ? "Undiscovered" : kind.ToString(),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = discovery == null ? FontWeight.Normal : FontWeight.SemiBold
        };
        if (discovery == null) name.Classes.Add("faint");

        var row = new DockPanel { Margin = new Thickness(2, 3) };
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        if (discovery != null)
        {
            var count = new TextBlock { Text = discovery.Defeats.ToString("N0"), Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
        }
        row.Children.Add(name);
        return row;
    }

    private static Control VictoryRow(BronzeTrophy trophy)
    {
        var star = new TextBlock { Text = "★", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
        star.Bind(TextBlock.ForegroundProperty, star.GetResourceObservable("Cc.Gold"));
        var title = new TextBlock { Text = trophy.ArcTitle, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel { Margin = new Thickness(2, 5) };
        DockPanel.SetDock(star, Dock.Left);
        row.Children.Add(star);
        row.Children.Add(title);
        return row;
    }

    /// <summary>
    /// A creature's portrait: its sprite at boss size on a 40-pixel grid,
    /// enlarged with whole pixels to whatever size it is given - the Windows
    /// portrait, drawn rather than resampled from a bitmap.
    /// </summary>
    internal sealed class SpritePortrait : Control
    {
        private BronzeEnemyKind? _kind;

        public BronzeEnemyKind? Kind
        {
            get => _kind;
            set { _kind = value; InvalidateVisual(); }
        }

        public override void Render(DrawingContext context)
        {
            if (Kind is not { } kind) return;
            var scale = Math.Min(Bounds.Width, Bounds.Height) / 40;
            var offset = new Point((Bounds.Width - 40 * scale) / 2, (Bounds.Height - 40 * scale) / 2);
            using var place = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));
            using var pixels = context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.None, EdgeMode = EdgeMode.Aliased });
            BronzeSprites.Draw(context, kind.ToString(), 20, 29, false, true, 0, false);
        }
    }

    /// <summary>
    /// The reading trail under a creature or a laurel: which verses, what each
    /// turned out to mean, and a way back to it in the reader. Saved numeric
    /// ids are never trusted - the arcade finds the verse again by its address,
    /// so a re-ingested library still opens it, and one that has lost the
    /// edition says so while the recovered words stay here.
    /// </summary>
    internal sealed class MemoryReader : DockPanel
    {
        private readonly ListBox _verses = new() { SelectionMode = SelectionMode.Single, Height = 104 };
        private readonly SelectableTextBlock _text = TextPanel();
        private readonly TextBlock _heading = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        private readonly Button _open = new() { Content = "Reopen selected verse in the library", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        private IReadOnlyList<BronzeRecoveredVerse> _items = Array.Empty<BronzeRecoveredVerse>();

        public MemoryReader(Func<ArcadePassage, Task> reopen, Window owner)
        {
            _heading.Bind(TextBlock.ForegroundProperty, _heading.GetResourceObservable("Cc.Verdigris"));
            _text.Bind(TextBlock.FontFamilyProperty, _text.GetResourceObservable("Cc.SourceFont"));
            _text.FontSize = 14.5;

            var listFrame = new Border { Classes = { "panel" }, Padding = new Thickness(2), Child = _verses };
            var page = new Border
            {
                Classes = { "page" },
                Margin = new Thickness(0, 10, 0, 0),
                Child = new ScrollViewer { Content = _text, Padding = new Thickness(16, 12), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            };

            SetDock(_heading, Dock.Top);
            SetDock(listFrame, Dock.Top);
            SetDock(_open, Dock.Bottom);
            Children.Add(_heading);
            Children.Add(listFrame);
            Children.Add(_open);
            Children.Add(page);

            _verses.SelectionChanged += (_, _) =>
            {
                _open.IsEnabled = _verses.SelectedIndex >= 0;
                if (_verses.SelectedIndex < 0) return;
                var item = _items[_verses.SelectedIndex];
                _text.Text = item.ArcTitle + "\n\n" + item.Passage.Text + "\n\n" + item.Meaning;
            };
            _open.Click += async (_, _) =>
            {
                if (_verses.SelectedIndex < 0) return;
                var row = _items[_verses.SelectedIndex].Passage; _open.IsEnabled = false;
                try { await reopen(row); owner.Close(true); }
                catch (Exception ex) { if (owner.IsVisible) await Alert.Error(owner, "Could not reopen verse", ex.Message); }
                finally { _open.IsEnabled = _verses.SelectedIndex >= 0; }
            };
        }

        internal string Heading => _heading.Text ?? string.Empty;
        internal string Text => _text.Text ?? string.Empty;
        internal IReadOnlyList<BronzeRecoveredVerse> Items => _items;
        internal Button OpenButton => _open;
        internal ListBox Verses => _verses;

        public void Set(IReadOnlyList<BronzeRecoveredVerse> items, string heading)
        {
            _items = items; _heading.Text = heading; _text.Text = string.Empty; _open.IsEnabled = false;
            _verses.ItemsSource = items.Select(item => $"{item.Passage.Author} — {item.Passage.Title} {PassageCitation.Display(item.Passage.Citation)}").ToList();
            if (items.Count > 0) _verses.SelectedIndex = 0;
            else _text.Text = "Recover a story passage after battle to add its reading trail here.";
        }
    }
}
