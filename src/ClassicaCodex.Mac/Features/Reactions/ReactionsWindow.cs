using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Reactions;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Reactions;

/// <summary>
/// A staged argument about one work, read the way you would read a group chat
/// you were not in.
///
/// <b>The banner is not decoration.</b> This application is otherwise full of
/// real ancient text, and a reader arriving here from the library has every
/// reason to assume the same rules apply. They do not: the words in these
/// bubbles were written for this window. The banner says so before anything
/// else is readable, every speaker's card says so again, and a speaker who is
/// a real person carries a badge and a reference to the ancient passage their
/// view actually comes from. Being tiresome about this is the price of the
/// feature existing at all.
///
/// <b>What it is for.</b> Not amusement, or not only. A reader who has just
/// finished the Medea and is told that the first surviving theory of plot
/// singles out its ending as the thing to avoid - and can click through to
/// Poetics 1454a in their own library, in Greek - has been handed a thread to
/// pull. That is the whole design: the invented voices carry the argument, and
/// every real claim in it is a link to something they can read for themselves.
///
/// <b>The conversation.</b> Everyone's messages run down the left, each with a
/// face and in their own colour. The reader is a bystander here, not a
/// participant, so nothing is aligned right - that side is where your own
/// messages go, and none of these are yours. The Windows window paints the
/// transcript by hand because laying out variable-height bubbles with
/// WinForms anchors meant fighting the layout engine for every pixel; here
/// each turn is ordinary controls and Avalonia measures them, which is also
/// why the banner and the setting note can never be cut short at a narrow
/// width - they are paragraphs that grow, not boxes of a fixed height.
/// </summary>
public sealed class ReactionsWindow : FeatureWindow
{
    private const double AvatarSize = 44;
    private const double Gutter = 12;
    private const double Inset = 12;
    private const double BlockGap = 12;
    private const double TuckedGap = 4;
    private const double MaxBubbleWidth = 620;

    private readonly Work _work;
    private readonly IReadOnlyList<ReactionDebate> _debates;
    private readonly ReactionPassageRepository _passages = new();

    private readonly TextBlock _title;
    private readonly TextBlock _setting;
    private readonly TextBlock _note;
    private readonly ComboBox? _picker;
    private readonly StackPanel _transcript;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _status;

    private readonly double _bodySize;
    private readonly double _metaSize;

    private IReadOnlyList<TurnView> _views = Array.Empty<TurnView>();
    private CancellationTokenSource? _loading;
    private bool _closed;

    public ReactionsWindow(Work work, string authorName, IReadOnlyList<ReactionDebate> debates)
    {
        _work = work;
        _debates = debates;
        AuthorName = authorName;

        Title = $"Fictional Ancient Reactions — {work.Title}";
        Width = 900;
        Height = 740;
        MinWidth = 560;
        MinHeight = 480;

        // Text size is a reading preference and this is text to read, so the
        // bubbles follow it - the two sizes the Windows window chooses between,
        // in points converted to the Mac's units, a step up because these are
        // set in the reading face. The name and the small print move with it,
        // or a large body size would leave the speaker's name looking like a
        // footnote to their own sentence.
        var points = ReadingFontSettings.SourceSize >= 14 ? 11.25 : 9.75;
        _bodySize = points * 96 / 72 + 1;
        _metaSize = Math.Max(10.5, _bodySize - 2.5);

        _title = new TextBlock
        {
            Classes = { "h1" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        _title.Bind(TextBlock.FontFamilyProperty, _title.GetResourceObservable("Cc.DisplayFont"));
        _setting = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        _note = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 18 };

        // Only when there is a choice to make. A menu with one entry is a
        // control that looks interactive and is not.
        if (debates.Count > 1)
        {
            _picker = new ComboBox
            {
                ItemsSource = debates.Select(d => d.Title).ToList(),
                SelectedIndex = 0,
                MinWidth = 180,
                MaxWidth = 280,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(16, 2, 0, 0)
            };
            ToolTip.SetTip(_picker, $"{debates.Count} debates about {work.Title}");
            _picker.SelectionChanged += async (_, _) => await ShowSelectedAsync();
        }

        var titleBlock = new StackPanel();
        titleBlock.Children.Add(_title);
        titleBlock.Children.Add(_setting);

        var titleRow = new DockPanel();
        if (_picker != null)
        {
            DockPanel.SetDock(_picker, Dock.Right);
            titleRow.Children.Add(_picker);
        }
        titleRow.Children.Add(titleBlock);

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 12) };
        header.Children.Add(titleRow);
        header.Children.Add(_note);

        _transcript = new StackPanel { Margin = new Thickness(Inset, Inset + 2, Inset, Inset + 6) };
        _scroller = new ScrollViewer
        {
            Content = _transcript,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        var page = new Border { Classes = { "page" }, Child = _scroller, Margin = new Thickness(18, 0, 18, 0) };

        _status = new TextBlock
        {
            Classes = { "caption" },
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var about = new Button { Content = "What am I reading?", MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Center };
        about.Click += async (_, _) => await ShowExplanationAsync();

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(about);
        buttons.Children.Add(close);

        var footer = new DockPanel { Margin = new Thickness(22, 12, 18, 14) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);

        var root = new DockPanel();
        var banner = BuildBanner();
        DockPanel.SetDock(banner, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(banner);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(page);
        Content = root;

        Opened += async (_, _) => await ShowSelectedAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            CancelLoading();
        };

        // The bubbles are tinted toward the page they sit on, so a change of
        // appearance re-tints them from the same recipes.
        ActualThemeVariantChanged += (_, _) => RenderTurns();
    }

    /// <summary>The author the library files this work under, as the debates were matched on.</summary>
    public string AuthorName { get; }

    /// <summary>The debate being shown.</summary>
    internal ReactionDebate Selected =>
        _debates[_picker?.SelectedIndex is int i && i >= 0 && i < _debates.Count ? i : 0];

    /// <summary>The turns as last laid out, for the tests.</summary>
    internal IReadOnlyList<TurnView> Views => _views;

    internal string StatusText => _status.Text ?? string.Empty;

    internal ComboBox? Picker => _picker;

    /// <summary>
    /// The fiction label: first in the window, in a colour nothing else in
    /// the app uses for a band, and never shortened. Its whole job is to not
    /// look like the rest of the window, so it is a wash of gold over the
    /// window rather than another card.
    /// </summary>
    private static Control BuildBanner()
    {
        var wash = new Border { Opacity = 0.2 };
        wash.Bind(Border.BackgroundProperty, wash.GetResourceObservable("Cc.Gold"));

        var edge = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.7 };
        edge.Bind(Border.BackgroundProperty, edge.GetResourceObservable("Cc.Gold"));

        var icon = new PathIcon
        {
            Data = Glyphs.Get("Reactions"),
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 12, 0)
        };
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable("Cc.AccentStrong"));

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        text.Inlines!.Add(new Run("Fictional reconstruction. ") { FontWeight = FontWeight.Bold });
        text.Inlines.Add(new Run(DebateTranscript.FictionLabel));

        var row = new DockPanel { Margin = new Thickness(22, 11, 22, 12) };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);

        var banner = new Panel { Name = "FictionBanner" };
        banner.Children.Add(wash);
        banner.Children.Add(edge);
        banner.Children.Add(row);
        return banner;
    }

    private async Task ShowSelectedAsync()
    {
        var debate = Selected;

        _title.Text = debate.Title;
        _setting.Text = DebateTranscript.SettingLine(debate);
        _note.Text = debate.SettingNote ?? string.Empty;
        _note.IsVisible = !string.IsNullOrWhiteSpace(debate.SettingNote);

        // One cancellation per load, so clicking through the picker quickly
        // cannot have an earlier debate's resolved links arrive after a later
        // one's and repaint the window with the wrong chips.
        //
        // Cancelled and replaced, never disposed. The token it handed out is
        // registered inside a SQLite call that may still be in flight, and
        // disposing a source out from under a live registration is exactly how
        // closing the Windows window threw ObjectDisposedException on every
        // debate until that was run rather than reasoned about. A source with
        // no timer holds nothing the collector cannot take back.
        CancelLoading();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        _status.Text = "Checking which passages this library has…";

        try
        {
            var views = await BuildViewsAsync(debate, token);
            if (token.IsCancellationRequested || _closed) return;

            SetTurns(views);
            _status.Text = DebateTranscript.Summarise(debate, views);
        }
        catch (OperationCanceledException)
        {
            // Another debate was picked, or the window closed. Its own load
            // will set the status.
        }
        catch (Exception ex)
        {
            // A debate that cannot check its links is still a debate worth
            // reading, so the turns go up regardless and only the links are
            // lost. Nothing here is worth a dialog.
            CrashLog.Write(ex, "Fictional Ancient Reactions: resolving passage links");
            if (_closed) return;

            SetTurns(DebateTranscript.BuildViews(debate, new Dictionary<int, string>(), new Dictionary<int, string>()));
            _status.Text = "The passage links could not be checked against this library.";
        }
    }

    /// <summary>
    /// Asks the library which of this debate's citations it can actually open.
    ///
    /// Done once per debate rather than per click, because the answer decides
    /// whether a chip is drawn as a link at all - a chip that looks clickable
    /// and does nothing is worse than no chip. Which corpora a reader
    /// installed decides most of this, and an empty library resolves nothing,
    /// which is a perfectly good outcome.
    ///
    /// On a worker thread, deliberately. Microsoft.Data.Sqlite's Async methods
    /// run synchronously - awaiting them on the UI thread blocks it just as a
    /// synchronous call would - and this is up to twenty-eight queries against
    /// a library that can be three gigabytes. Awaited as written, the Windows
    /// window came up and then froze before it had painted a single bubble.
    /// </summary>
    private async Task<IReadOnlyList<TurnView>> BuildViewsAsync(ReactionDebate debate, CancellationToken token)
    {
        var workUrn = _work.CtsUrn;

        var (passages, sources) = await Task.Run(async () =>
        {
            var found = new Dictionary<int, string>();
            var cited = new Dictionary<int, string>();

            foreach (var turn in debate.Turns)
            {
                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(turn.PassageRef))
                {
                    var passage = await _passages.FindInWorkAsync(workUrn, turn.PassageRef!, token);
                    if (passage != null) found[turn.Seq] = passage.CitationRef;
                }

                if (turn.SourceWork?.CitationRef != null)
                {
                    var source = await _passages.FindByWorkReferenceAsync(turn.SourceWork, token);
                    if (source != null) cited[turn.Seq] = source.CitationRef;
                }
            }

            return (found, cited);
        }, token);

        return DebateTranscript.BuildViews(debate, passages, sources);
    }

    private void SetTurns(IReadOnlyList<TurnView> views)
    {
        _views = views;
        RenderTurns();
        _scroller.Offset = default;
    }

    private void RenderTurns()
    {
        _transcript.Children.Clear();
        if (_views.Count == 0) return;

        var surface = ThemeColour("Cc.Page", Colors.White);
        var rule = ThemeColour("Cc.Rule", Colors.LightGray);
        var dark = ActualThemeVariant == ThemeVariant.Dark;

        for (var i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            if (view.DividerBefore != null) _transcript.Children.Add(BuildDivider(view.DividerBefore));

            var turn = BuildTurn(view, surface, rule, dark);

            // A tucked turn sits close under the one before; a new speaker
            // gets room, so the eye sees who is talking before what is said.
            var next = i + 1 < _views.Count ? _views[i + 1] : null;
            turn.Margin = new Thickness(0, 0, 0, next == null ? 0 : next.ShowSpeaker ? BlockGap : TuckedGap);
            _transcript.Children.Add(turn);
        }
    }

    /// <summary>
    /// The "Tuesday" line in a messaging app, doing the job that matters most
    /// in this window: these people are centuries apart and nobody is replying
    /// to anybody.
    /// </summary>
    private Control BuildDivider(string label)
    {
        var text = new TextBlock { Text = label, FontSize = _metaSize, FontWeight = FontWeight.SemiBold };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));

        var pill = new Border
        {
            Child = text,
            Padding = new Thickness(12, 4),
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 14)
        };
        pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable("Cc.PageHeader"));
        pill.Bind(Border.BorderBrushProperty, pill.GetResourceObservable("Cc.Rule"));
        return pill;
    }

    private Control BuildTurn(TurnView view, Color surface, Color rule, bool dark)
    {
        var signature = AncientAvatars.Signature(view.Critic.Avatar);

        // Toward the page's own colour rather than to a fixed white, so one
        // set of recipes works in both appearances: a pale wash on parchment
        // and a dark, saturated one by lamplight, from the same source colour.
        var fill = AncientAvatars.Mix(signature, surface, dark ? 0.74 : 0.86);

        var content = new StackPanel();

        if (view.ShowSpeaker)
        {
            var name = new TextBlock
            {
                Text = view.Critic.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = _bodySize - 0.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Clickable(name, "Cc.Ink", "Cc.Accent", () => ShowCriticCardAsync(view.Critic), "Who is this?");

            var nameRow = new WrapPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(name);
            if (view.Critic.Kind == CriticKind.Historical)
            {
                var badge = RealPersonBadge(_metaSize - 0.5);
                badge.Margin = new Thickness(8, 1, 0, 1);
                badge.VerticalAlignment = VerticalAlignment.Center;
                nameRow.Children.Add(badge);
            }

            content.Children.Add(nameRow);
            content.Children.Add(new TextBlock
            {
                Text = DebateTranscript.SpeakerMeta(view.Critic),
                Classes = { "caption" },
                FontSize = _metaSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 7)
            });
        }

        var body = new SelectableTextBlock
        {
            Text = view.Turn.Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = _bodySize,
            LineHeight = Math.Round(_bodySize * 1.42)
        };
        body.Bind(TextBlock.FontFamilyProperty, body.GetResourceObservable("Cc.TranslationFont"));
        content.Children.Add(body);

        if (view.PassageLabel != null) content.Children.Add(BuildPassageChip(view, signature, surface));

        if (view.SourceLabel != null)
        {
            // Only as wide as its words. On Windows the source line was laid
            // out across the whole text column, because that is what it wraps
            // at, and hit-tested there too - so the blank half of a bubble
            // showed a hand cursor and, on a click, closed the window and sent
            // the reader to Cicero. Left-aligned, this one is clickable where
            // it is drawn and nowhere else.
            var source = new TextBlock
            {
                Text = view.SourceLabel,
                FontStyle = FontStyle.Italic,
                FontSize = _metaSize,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };

            if (view.SourceResolved)
                Clickable(source, "Cc.Link", "Cc.Accent", () => NavigateToSourceAsync(view.Turn), "Open the source in the reader");
            else
                source.Bind(TextBlock.ForegroundProperty, source.GetResourceObservable("Cc.InkMuted"));

            content.Children.Add(source);
        }

        // A stripe of the speaker's own colour down the leading edge. The
        // wash is deliberately faint so the text stays readable, which leaves
        // it too faint to identify anybody - this is the part you actually
        // recognise at a glance. An inset shadow rather than a child, so it
        // follows the bubble's rounded corners exactly.
        var bubble = new Border
        {
            Child = content,
            Background = new SolidColorBrush(fill),
            BorderBrush = new SolidColorBrush(AncientAvatars.Mix(signature, rule, 0.55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 4, Color = signature, IsInset = true }),
            Padding = new Thickness(Inset + 5, Inset - 1, Inset + 2, Inset),
            MaxWidth = MaxBubbleWidth,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{AvatarSize},{Gutter},*") };
        Grid.SetColumn(bubble, 2);
        row.Children.Add(bubble);

        if (view.ShowSpeaker)
        {
            var portrait = new CriticPortrait(view.Critic, AvatarSize)
            {
                VerticalAlignment = VerticalAlignment.Top,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(portrait, $"{view.Critic.Name} - who is this?");
            portrait.Tapped += async (_, _) => await ShowCriticCardAsync(view.Critic);
            row.Children.Add(portrait);
        }

        return row;
    }

    /// <summary>
    /// The passage in the work this turn is about. A link when the library
    /// has it; otherwise the same pill, flat and muted, saying so - a chip
    /// that looks clickable and does nothing is worse than none. The corners
    /// are as round as the height allows, so it is a pill at any text size.
    /// </summary>
    private Control BuildPassageChip(TurnView view, Color signature, Color surface)
    {
        var text = new TextBlock { Text = view.PassageLabel, FontSize = _metaSize };

        var chip = new Border
        {
            Child = text,
            Padding = new Thickness(10, 3, 11, 4),
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 9, 0, 0)
        };
        chip.Bind(Border.BorderBrushProperty, chip.GetResourceObservable("Cc.Rule"));

        if (!view.PassageResolved)
        {
            chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Cc.PageHeader"));
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.InkMuted"));
            ToolTip.SetTip(chip, "Your library has no edition of this work with this passage.");
            return chip;
        }

        var rest = new SolidColorBrush(AncientAvatars.Mix(signature, surface, 0.55));
        var hot = new SolidColorBrush(AncientAvatars.Mix(signature, surface, 0.35));
        chip.Background = rest;
        chip.Cursor = new Cursor(StandardCursorType.Hand);
        chip.PointerEntered += (_, _) => chip.Background = hot;
        chip.PointerExited += (_, _) => chip.Background = rest;
        chip.Tapped += async (_, _) => await NavigateToPassageAsync(view.Turn);
        ToolTip.SetTip(chip, $"Open {_work.Title} {view.Turn.PassageRef} in the reader");
        return chip;
    }

    /// <summary>
    /// Marks a turn as belonging to somebody who existed. The words are still
    /// a paraphrase written for this window - what the badge promises is that
    /// the view is attested and that the reference underneath is where to
    /// check it.
    /// </summary>
    internal static Control RealPersonBadge(double fontSize)
    {
        var wash = new Border { CornerRadius = new CornerRadius(999), Opacity = 0.16 };
        wash.Bind(Border.BackgroundProperty, wash.GetResourceObservable("Cc.Verdigris"));

        var text = new TextBlock { Text = "real person", FontSize = fontSize, FontWeight = FontWeight.SemiBold };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Cc.Verdigris"));

        var edge = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 1, 8, 2)
        };
        edge.Bind(Border.BorderBrushProperty, edge.GetResourceObservable("Cc.Verdigris"));

        var badge = new Panel();
        badge.Children.Add(wash);
        badge.Children.Add(edge);
        return badge;
    }

    /// <summary>Text that acts as a link: a hand, a colour that answers the pointer, a click.</summary>
    private static void Clickable(TextBlock text, string restKey, string hotKey, Func<Task> action, string tip)
    {
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(restKey));
        text.Cursor = new Cursor(StandardCursorType.Hand);
        text.PointerEntered += (_, _) => text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(hotKey));
        text.PointerExited += (_, _) => text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(restKey));
        text.Tapped += async (_, _) => await action();
        ToolTip.SetTip(text, tip);
    }

    private async Task ShowCriticCardAsync(AncientCritic critic)
    {
        var card = new CriticCardWindow(critic);
        await card.ShowDialog(this);
    }

    /// <summary>
    /// Opens the passage this turn is about and closes this window, so the
    /// reader is left looking at the text rather than at a window on top of it.
    /// </summary>
    private async Task NavigateToPassageAsync(DebateTurn turn)
    {
        if (OnNavigate == null || string.IsNullOrWhiteSpace(turn.PassageRef)) return;

        try
        {
            var workUrn = _work.CtsUrn;
            var passage = await Task.Run(() => _passages.FindInWorkAsync(workUrn, turn.PassageRef!));
            if (passage == null) return;

            await OnNavigate(passage.WorkId, passage.TextNodeId);
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Fictional Ancient Reactions: opening a passage");
            await ShowErrorAsync("Couldn't open that passage", ex);
        }
    }

    /// <summary>The same, for the ancient source a turn's view is taken from.</summary>
    private async Task NavigateToSourceAsync(DebateTurn turn)
    {
        if (OnNavigate == null || turn.SourceWork == null) return;

        try
        {
            var reference = turn.SourceWork;
            var passage = await Task.Run(() => _passages.FindByWorkReferenceAsync(reference));
            if (passage == null) return;

            await OnNavigate(passage.WorkId, passage.TextNodeId);
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Fictional Ancient Reactions: opening a source");
            await ShowErrorAsync("Couldn't open that source", ex);
        }
    }

    /// <summary>
    /// "What am I reading?" - the long answer, in a sheet of its own with room
    /// to scroll, because it runs to six paragraphs and the folder a reader's
    /// own packs go in, and an alert that tall would run off a laptop screen.
    /// </summary>
    private async Task ShowExplanationAsync()
    {
        var sheet = BuildExplanationSheet();
        await sheet.ShowDialog(this);
    }

    internal static Window BuildExplanationSheet()
    {
        var sheet = new Window
        {
            Title = "What am I reading?",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var paragraphs = new StackPanel { Spacing = 10 };
        foreach (var paragraph in DebateTranscript.Explanation())
        {
            paragraphs.Children.Add(new SelectableTextBlock
            {
                Text = paragraph,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            });
        }

        var icon = new PathIcon { Data = Glyphs.Get("Reactions"), Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 16, 0) };
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable("Cc.Accent"));

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "These debates are fiction.", Classes = { "h2" } });
        body.Children.Add(new ScrollViewer { Content = paragraphs, MaxHeight = 470, Padding = new Thickness(0, 0, 10, 0) });

        var top = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(body);

        var ok = new Button
        {
            Content = "OK",
            Classes = { "accent" },
            IsDefault = true,
            IsCancel = true,
            MinWidth = 84,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ok.Click += (_, _) => sheet.Close();

        var layout = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 16 };
        layout.Children.Add(top);
        layout.Children.Add(ok);
        sheet.Content = layout;
        return sheet;
    }

    /// <summary>
    /// A cancelled load must not throw if the source is already cancelled;
    /// both the picker and closing the window come through here so neither
    /// has to know.
    /// </summary>
    private void CancelLoading()
    {
        try
        {
            _loading?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already gone; nothing to cancel.
        }
    }

    private Color ThemeColour(string brushKey, Color fallback) =>
        this.TryFindResource(brushKey, ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? brush.Color
            : fallback;
}
