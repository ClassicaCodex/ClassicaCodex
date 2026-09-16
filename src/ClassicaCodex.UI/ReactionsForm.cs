using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Reactions;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A staged argument about one work, read the way you would read a group chat
/// you were not in.
///
/// <b>The banner is not decoration.</b> This application is otherwise full of
/// real ancient text, and a reader arriving here from the library tree has
/// every reason to assume the same rules apply. They do not: the words in
/// these bubbles were written for this window. The banner says so before
/// anything else is readable, every speaker's card says so again, and a
/// speaker who is a real person carries a badge and a reference to the ancient
/// passage their view actually comes from. Being tiresome about this is the
/// price of the feature existing at all.
///
/// <b>What it is for.</b> Not amusement, or not only. A reader who has just
/// finished the Medea and is told that the first surviving theory of plot
/// singles out its ending as the thing to avoid - and can click through to
/// Poetics 1454a in their own library, in Greek - has been handed a thread to
/// pull. That is the whole design: the invented voices carry the argument, and
/// every real claim in it is a link to something they can read for themselves.
/// </summary>
public sealed class ReactionsForm : ScaledForm
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly IReadOnlyList<ReactionDebate> _debates;
    private readonly ReactionPassageRepository _passages = new();

    private readonly Panel _banner;
    private readonly Label _bannerText;
    private readonly Label _title;
    private readonly Label _setting;
    private readonly Label _note;
    private readonly ComboBox? _picker;
    private readonly ReactionsCanvas _canvas;
    private readonly Label _status;

    private CancellationTokenSource? _loading;

    /// <summary>
    /// Set by MainForm. A passage chip invokes this with the work and line to
    /// open, then closes this window so the reader is left looking at the text
    /// rather than at a dialog on top of it.
    /// </summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public ReactionsForm(Work work, string authorName, IReadOnlyList<ReactionDebate> debates)
    {
        _work = work;
        _authorName = authorName;
        _debates = debates;

        Text = $"Fictional Ancient Reactions - {work.Title}";
        AppIcons.ApplyWindowIcon(this, "MythNetwork");
        ClientSize = new Size(900, 740);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;

        _banner = new Panel
        {
            Left = 0,
            Top = 0,
            Width = 900,
            Height = 52,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _bannerText = new Label
        {
            Text = "Fictional reconstruction. These conversations were written for this window - "
                   + "nobody said these words. Speakers marked “real person” are paraphrased "
                   + "from something they wrote, and their turns carry the reference.",
            Left = 14,
            Top = 6,
            Width = 872,
            Height = 40,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _banner.Controls.Add(_bannerText);

        _title = new Label
        {
            Left = 14,
            Top = 62,
            Width = 700,
            Height = 26,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _setting = new Label
        {
            Left = 14,
            Top = 90,
            Width = 700,
            Height = 20,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _note = new Label
        {
            Left = 14,
            Top = 114,
            Width = 872,
            Height = 76,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // Only when there is a choice to make. A combo box with one entry is a
        // control that looks interactive and is not.
        if (debates.Count > 1)
        {
            _picker = new ComboBox
            {
                Left = 724,
                Top = 62,
                Width = 162,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            foreach (var debate in debates) _picker.Items.Add(debate.Title);
            _picker.SelectedIndex = 0;
            _picker.SelectedIndexChanged += async (_, _) => await ShowSelectedAsync();
        }

        _canvas = new ReactionsCanvas
        {
            Left = 8,
            Top = 196,
            Width = 884,
            Height = 498,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _canvas.CriticClicked += ShowCriticCard;
        _canvas.PassageClicked += async turn => await NavigateToPassageAsync(turn);
        _canvas.SourceClicked += async turn => await NavigateToSourceAsync(turn);

        _status = new Label
        {
            Left = 14,
            Top = 706,
            Width = 560,
            Height = 20,
            ForeColor = ReadingTheme.MutedText,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };

        var about = new Button
        {
            Text = "What am I reading?",
            Left = 640,
            Top = 700,
            Width = 152,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        about.Click += (_, _) => ShowExplanation();

        var close = new Button
        {
            Text = "Close",
            Left = 800,
            Top = 700,
            Width = 92,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };

        Controls.AddRange(new Control[] { _banner, _title, _setting, _note, _canvas, _status, about, close });
        if (_picker != null) Controls.Add(_picker);

        CancelButton = close;

        ReadingTheme.AttachTo(this, ApplyBannerColours);
        ApplyBannerColours();

        _canvas.UseTextSize(ReadingFontSettings.SourceSize >= 14 ? 11.25f : 9.75f);

        Load += async (_, _) => await ShowSelectedAsync();
    }

    /// <summary>
    /// The banner keeps its own colours in both themes rather than taking the
    /// window's, because its whole job is to not look like the rest of the
    /// window. ReadingTheme.Apply walks every child control, so this runs
    /// after it and puts them back.
    /// </summary>
    private void ApplyBannerColours()
    {
        _banner.BackColor = ReadingTheme.IsDark
            ? Color.FromArgb(68, 54, 24)
            : Color.FromArgb(253, 243, 205);

        _bannerText.BackColor = _banner.BackColor;
        _bannerText.ForeColor = ReadingTheme.IsDark
            ? Color.FromArgb(246, 226, 160)
            : Color.FromArgb(104, 74, 8);

        _setting.ForeColor = ReadingTheme.MutedText;
        _note.ForeColor = ReadingTheme.MutedText;
        _status.ForeColor = ReadingTheme.MutedText;
        _canvas.BackColor = ReadingTheme.Background;
    }

    private ReactionDebate Selected =>
        _debates[_picker?.SelectedIndex is int i && i >= 0 && i < _debates.Count ? i : 0];

    private async Task ShowSelectedAsync()
    {
        var debate = Selected;

        _title.Text = debate.Title;
        _setting.Text = debate.Kind == DebateKind.AcrossTheCenturies
            ? $"Not a conversation · {debate.SettingLabel}"
            : $"A scene · {debate.SettingLabel}";
        _note.Text = debate.SettingNote ?? string.Empty;

        // One cancellation per load, so clicking through the picker quickly
        // cannot have an earlier debate's resolved links arrive after a later
        // one's and repaint the window with the wrong chips.
        //
        // Cancelled and replaced, never disposed here. The token it handed out
        // is registered inside a SQLite call that is still in flight at this
        // moment, and disposing a source out from under a live registration is
        // exactly how closing this window threw ObjectDisposedException - which
        // it did, on every one of the seven debates, until this was run rather
        // than reasoned about.
        CancelLoading();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        _status.Text = "Checking which passages this library has...";

        try
        {
            var views = await BuildViewsAsync(debate, token);
            if (token.IsCancellationRequested || IsDisposed) return;

            _canvas.SetTurns(views);
            _status.Text = Summarise(debate, views);
        }
        catch (OperationCanceledException)
        {
            // Another debate was picked. Its own load will set the status.
        }
        catch (Exception ex)
        {
            // A debate that cannot check its links is still a debate worth
            // reading, so the turns go up regardless and only the links are
            // lost. Nothing here is worth a dialog.
            CrashReporter.LogHandled(ex, "Fictional Ancient Reactions: resolving passage links");

            if (IsDisposed) return;
            _canvas.SetTurns(BuildViews(debate, new Dictionary<int, string>(), new Dictionary<int, string>()));
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
    /// </summary>
    private async Task<IReadOnlyList<TurnView>> BuildViewsAsync(
        ReactionDebate debate, CancellationToken token)
    {
        // On a worker thread, deliberately. Microsoft.Data.Sqlite's Async
        // methods run synchronously - awaiting them on the UI thread blocks
        // it just as a synchronous call would - and this is up to twenty-eight
        // queries against a library that can be three gigabytes. Awaited as
        // they were written, the window came up and then froze before it had
        // painted a single bubble.
        var (passages, sources) = await Task.Run(async () =>
        {
            var found = new Dictionary<int, string>();
            var cited = new Dictionary<int, string>();

            foreach (var turn in debate.Turns)
            {
                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(turn.PassageRef))
                {
                    var passage = await _passages.FindInWorkAsync(_work.CtsUrn, turn.PassageRef!, token);
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

        return BuildViews(debate, passages, sources);
    }

    private IReadOnlyList<TurnView> BuildViews(
        ReactionDebate debate,
        IReadOnlyDictionary<int, string> resolvedPassages,
        IReadOnlyDictionary<int, string> resolvedSources)
    {
        var views = new List<TurnView>();

        string? previousSpeaker = null;
        string? previousDivider = null;

        foreach (var turn in debate.Turns)
        {
            var critic = ReactionLibrary.Critic(turn.CriticId);

            // A pack that validated has every speaker declared, so this cannot
            // happen from shipped content - but a turn is not worth dropping
            // silently either, so it gets a placeholder that says what is
            // wrong rather than disappearing.
            critic ??= new AncientCritic(turn.CriticId, turn.CriticId, CriticKind.Composite,
                "unknown", 0, 0, null, "speaker not found in any pack", null, null, null);

            // The date pill, and only when the date changes - otherwise every
            // turn of a five-turn year carries its own label and the thing
            // that matters (the centuries opening up) stops standing out.
            string? divider = null;
            if (debate.Kind == DebateKind.AcrossTheCenturies && turn.Year.HasValue)
            {
                var label = AncientCritic.Year(turn.Year.Value);
                if (label != previousDivider)
                {
                    divider = label;
                    previousDivider = label;
                }
            }

            var showSpeaker = divider != null || previousSpeaker != turn.CriticId;
            previousSpeaker = turn.CriticId;

            views.Add(new TurnView(
                turn,
                critic,
                showSpeaker,
                divider,
                PassageLabel(turn, resolvedPassages.ContainsKey(turn.Seq)),
                resolvedPassages.ContainsKey(turn.Seq),
                SourceLabel(turn, resolvedSources.ContainsKey(turn.Seq)),
                resolvedSources.ContainsKey(turn.Seq)));
        }

        return views;
    }

    /// <summary>
    /// The citation alone, not the work and the citation.
    ///
    /// It used to name the work, which is fine for the Iliad and absurd for
    /// Thucydides: "History of the Peloponnesian War 7.87.5" is a chip wider
    /// than some of the sentences above it, three times on one screen. The
    /// work is in the title bar, in the header, and is the thing the reader
    /// right-clicked to get here.
    /// </summary>
    private static string? PassageLabel(DebateTurn turn, bool resolved)
    {
        if (string.IsNullOrWhiteSpace(turn.PassageRef)) return null;

        return resolved
            ? $"›  read {turn.PassageRef}"
            : $"{turn.PassageRef} – not in this library";
    }

    private static string? SourceLabel(DebateTurn turn, bool resolved)
    {
        if (string.IsNullOrWhiteSpace(turn.SourceCitation)) return null;

        return resolved
            ? "Source: " + turn.SourceCitation + "  – open it"
            : "Source: " + turn.SourceCitation;
    }

    /// <summary>
    /// Counts rather than names. Listing the real speakers ran off the end of
    /// the line on the Iliad debate, which has five of them - and a sentence
    /// cut off mid-word is worse than a shorter one, particularly this
    /// sentence, whose job is to say how much of what is above was invented.
    /// </summary>
    private static string Summarise(ReactionDebate debate, IReadOnlyList<TurnView> views)
    {
        var critics = views.Select(v => v.Critic).DistinctBy(c => c.Id).ToList();

        var real = critics.Count(c => c.Kind == CriticKind.Historical);
        var invented = critics.Count - real;

        var people = real == 0
            ? $"{invented} invented speakers"
            : $"{invented} invented, {real} real (click a face for which is which)";

        return $"{debate.Turns.Count} turns · {people}.";
    }

    private void ShowCriticCard(AncientCritic critic)
    {
        using var card = new CriticCardForm(critic);
        card.ShowDialog(this);
    }

    private async Task NavigateToPassageAsync(DebateTurn turn)
    {
        if (OnNavigate == null || string.IsNullOrWhiteSpace(turn.PassageRef)) return;

        var passage = await _passages.FindInWorkAsync(_work.CtsUrn, turn.PassageRef!);
        if (passage == null) return;

        await OnNavigate(passage.WorkId, passage.TextNodeId);
        Close();
    }

    private async Task NavigateToSourceAsync(DebateTurn turn)
    {
        if (OnNavigate == null || turn.SourceWork == null) return;

        var passage = await _passages.FindByWorkReferenceAsync(turn.SourceWork);
        if (passage == null) return;

        await OnNavigate(passage.WorkId, passage.TextNodeId);
        Close();
    }

    private void ShowExplanation()
    {
        var problems = ReactionLibrary.Problems;

        var text =
            "These debates are fiction.\r\n\r\n"
            + "Nobody in antiquity had these conversations. The words in the bubbles were "
            + "written for this window, and most of the speakers are invented people - a "
            + "farmer from Acharnae, a rhapsode, a schoolmaster - who stand in for the "
            + "audiences and readers who really did argue about these works and left nothing "
            + "in writing.\r\n\r\n"
            + "Some speakers are real, and those are handled differently. A speaker marked "
            + "“real person” may only voice a view that survives in an ancient text, "
            + "and every one of their turns carries the reference for it. Where your library "
            + "has that text, the reference is a link and you can go and read the passage. "
            + "The wording is still a paraphrase written for this window; what is guaranteed "
            + "is the position and the citation, not the sentence.\r\n\r\n"
            + "The invented speakers are kept honest too. Nobody may speak before the work "
            + "they are discussing was written, or outside their own lifetime, and a "
            + "conversation whose speakers could not all have been in one room is labelled "
            + "as centuries of reaction rather than as a scene. Those rules are checked by "
            + "the program on every build, not by whoever wrote the content.\r\n\r\n"
            + "None of this is evidence, and none of it should be cited. What it is for is "
            + "the thing a work like this is hardest to get at on your own: that it landed in "
            + "a particular room, in front of people with their own quarrels about morality, "
            + "religion, decorum and craft, and that the argument about it started at once and "
            + "has never stopped. If a turn makes you want to go and read the passage, or the "
            + "source under it, or disagree with both - that is the entire point, and every "
            + "link in this window is there to let you.\r\n\r\n"
            + $"Debates available: {ReactionLibrary.Packs.Count}. You can add your own as JSON "
            + $"files in:\r\n{ReactionLibrary.UserPackDirectory}";

        if (problems.Count > 0)
        {
            text += "\r\n\r\nSome packs could not be loaded:\r\n  "
                    + string.Join("\r\n  ", problems.Take(6));
        }

        MessageBox.Show(this, text, "What am I reading?",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Cancelling a source that has already been disposed throws, and a form
    /// can be disposed more than once - Close() does it and then the caller's
    /// using does it again. Both callers go through here so neither has to
    /// know that.
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelLoading();
            _loading?.Dispose();
            _loading = null;
        }

        base.Dispose(disposing);
    }
}
